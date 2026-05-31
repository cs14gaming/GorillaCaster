using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace GorillaCaster
{
    internal enum Verdict { Clean, Sus, Cheat }

    internal class ModReport
    {
        public string name;
        public string detail;
        public Color color;
        public Verdict verdict;
        public string platform = "";
    }

    /// <summary>
    /// Stateful, multi-signal Gorilla Tag mod checker. Remote players can only be judged by what
    /// they leak over the network, so detection combines Photon custom-property cheat signatures
    /// (cosmetic spoofers, named menus) with physics / rig anomalies tracked over time:
    /// speed, teleport, flight, arm-stretch "pull" mods, tag-from-range, play-space-abuse glide,
    /// scale and impossible colour. Each player gets a CLEAN / SUS / CHEATING verdict + reasons.
    /// Update() must be pumped every frame; Scan() returns the current snapshot for display.
    /// </summary>
    internal static class ModChecker
    {
        private class State
        {
            public Vector3 lastHead;
            public bool hasLast;
            public float ewmaSpeed;       // smoothed horizontal speed (m/s)
            public float maxArm;          // farthest hand-from-head reach seen (scale-normalised)
            public int teleports;
            public int flyTicks;          // consecutive frames of vertical-only motion
            public int glideTicks;        // consecutive frames of flat high-speed travel (PSA)
            public float since;           // seconds tracked
        }

        private static readonly Dictionary<VRRig, State> _states = new Dictionary<VRRig, State>();
        private static readonly List<VRRig> _dead = new List<VRRig>();

        // verdict colours
        private static readonly Color CClean = new Color(0.78f, 0.82f, 0.88f);
        private static readonly Color CSus = new Color(0.95f, 0.72f, 0.25f);
        private static readonly Color CCheat = new Color(0.90f, 0.30f, 0.34f);

        /// <summary>Pump per-frame so movement signals accumulate. Cheap.</summary>
        public static void Update(IList<VRRig> rigs, float dt)
        {
            if (rigs == null || dt <= 0f) return;

            // prune rigs that left
            _dead.Clear();
            foreach (var kv in _states) if (kv.Key == null || !rigs.Contains(kv.Key)) _dead.Add(kv.Key);
            foreach (var d in _dead) _states.Remove(d);

            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null) continue;
                if (!_states.TryGetValue(rig, out State st)) { st = new State(); _states[rig] = st; }
                st.since += dt;

                Vector3 head = CasterUtil.HeadPos(rig);
                float scale = RigScale(rig);

                // arm reach (pull-mod signature): hand stretched far from the head
                try
                {
                    float arm = 0f;
                    if (rig.rightHandTransform != null) arm = Mathf.Max(arm, Vector3.Distance(rig.rightHandTransform.position, head));
                    if (rig.leftHandTransform != null) arm = Mathf.Max(arm, Vector3.Distance(rig.leftHandTransform.position, head));
                    arm /= Mathf.Max(0.2f, scale);
                    if (arm > st.maxArm) st.maxArm = arm;
                }
                catch { }

                if (st.hasLast)
                {
                    Vector3 delta = head - st.lastHead;
                    float horiz = new Vector2(delta.x, delta.z).magnitude;
                    float vert = delta.y;
                    float hspeed = horiz / dt;

                    if (delta.magnitude > 12f && dt < 0.4f) st.teleports++;             // teleport pop
                    else st.ewmaSpeed = Mathf.Lerp(st.ewmaSpeed, hspeed, 0.18f);          // ignore teleport spikes

                    // flight: rising fast with almost no horizontal travel
                    if (vert / dt > 4f && horiz / dt < 1f) st.flyTicks++; else st.flyTicks = Mathf.Max(0, st.flyTicks - 2);

                    // play-space abuse: sustained flat glide with no vertical bob
                    if (hspeed > 4f && hspeed < 16f && Mathf.Abs(vert) < 0.012f) st.glideTicks++;
                    else st.glideTicks = Mathf.Max(0, st.glideTicks - 3);
                }
                st.lastHead = head; st.hasLast = true;
            }
        }

        public static List<ModReport> Scan(IList<VRRig> rigs)
        {
            var list = new List<ModReport>();
            if (rigs == null) return list;

            Dictionary<int, Player> players = null;
            try { if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null) players = PhotonNetwork.CurrentRoom.Players; }
            catch { }

            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null) continue;
                _states.TryGetValue(rig, out State st);

                var flags = new List<string>();
                Verdict v = Verdict.Clean;
                void Flag(string f, Verdict sev) { flags.Add(f); if (sev > v) v = sev; }

                float speed = CasterUtil.Speed(rig);

                if (st != null)
                {
                    if (st.ewmaSpeed > 25f || speed > 32f) Flag("SPEED", Verdict.Cheat);
                    else if (st.ewmaSpeed > 16f) Flag("SPEED?", Verdict.Sus);

                    if (st.teleports >= 2) Flag("TELEPORT", Verdict.Cheat);
                    else if (st.teleports == 1) Flag("WARP?", Verdict.Sus);

                    if (st.flyTicks >= 12) Flag("FLY", Verdict.Cheat);
                    else if (st.flyTicks >= 6) Flag("FLY?", Verdict.Sus);

                    if (st.maxArm > 2.0f) Flag("PULL", Verdict.Cheat);
                    else if (st.maxArm > 1.3f) Flag(CasterUtil.IsTagged(rig) ? "REACH" : "STRETCH", Verdict.Sus);

                    if (st.glideTicks >= 36) Flag("PSA", Verdict.Sus);
                }

                // scale + impossible colour
                float sc = RigScale(rig);
                if (sc > 2.0f || sc < 0.3f) Flag("SCALE", Verdict.Sus);
                try
                {
                    Color c = rig.playerColor;
                    if (c.r > 1.01f || c.g > 1.01f || c.b > 1.01f || c.r < -0.01f || c.g < -0.01f || c.b < -0.01f)
                        Flag("COLOR", Verdict.Cheat);
                }
                catch { }

                // custom-property cheat signatures (cosmetic spoofers / named menus)
                string platform = "";
                try
                {
                    var np = rig.Creator;
                    if (np != null && players != null && players.TryGetValue(np.ActorNumber, out Player pl) && pl.CustomProperties != null)
                    {
                        foreach (System.Collections.DictionaryEntry e in pl.CustomProperties)
                        {
                            string ks = e.Key != null ? e.Key.ToString() : "";
                            string vs = e.Value != null ? e.Value.ToString() : "";
                            if (CheatDatabase.Match(ks, out string m1)) Flag("MOD:" + m1, Verdict.Cheat);
                            else if (CheatDatabase.Match(vs, out string m2)) Flag("MOD:" + m2, Verdict.Cheat);
                        }
                        if (pl.CustomProperties.ContainsKey("platform")) platform = SafeStr(pl.CustomProperties["platform"]);
                        else if (pl.CustomProperties.ContainsKey("didPlatform")) platform = SafeStr(pl.CustomProperties["didPlatform"]);
                    }
                }
                catch { }

                string verdictWord = v == Verdict.Cheat ? "CHEATING" : v == Verdict.Sus ? "SUS" : "CLEAN";
                string reasons = flags.Count > 0 ? string.Join(" ", flags) : "no flags";
                string detail = (platform.Length > 0 ? platform + " · " : "") + $"{speed:0}m/s · {verdictWord} · {reasons}";
                Color col = v == Verdict.Cheat ? CCheat : v == Verdict.Sus ? CSus : CClean;
                list.Add(new ModReport { name = CasterUtil.NameOf(rig), detail = detail, color = col, verdict = v, platform = platform });
            }

            // worst offenders first
            list.Sort((a, b) => b.verdict.CompareTo(a.verdict));
            return list;
        }

        private static float RigScale(VRRig r)
        {
            try { return r.transform.localScale.x; } catch { return 1f; }
        }

        private static string SafeStr(object o) { try { return o != null ? o.ToString() : ""; } catch { return ""; } }
    }
}
