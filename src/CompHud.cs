using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Competitive casting overlay: an Infection round timer (3:00 cap) and a live scoreboard
    /// (survivors vs infected, who's IT), plus optional manual team scores. Auto-starts the
    /// timer on first infection and ends it on a wipe or the time cap.
    /// </summary>
    internal class CompHud
    {
        public bool ShowTimer = false;
        public bool ShowScoreboard = false;
        public bool AutoTimer = true;
        public bool ShowTeams = false;

        public int TeamA, TeamB;
        public string TeamAName = "TEAM A", TeamBName = "TEAM B";

        private const float Cap = 180f; // 3-minute round cap
        private float _t;
        private bool _running, _roundOver;
        private string _result = "";
        private int _prevTagged = -1;

        // ----- manual controls (tablet/menu) -----
        public void StartTimer() { _running = true; _roundOver = false; _t = 0f; }
        public void StopTimer() { _running = false; }
        public void ResetTimer() { _running = false; _t = 0f; _roundOver = false; _result = ""; }
        public bool Running => _running;
        public float TimeValue => _t;

        public void Update(float dt, IList<VRRig> rigs)
        {
            int tagged = CountTagged(rigs);
            int total = rigs.Count;

            if (AutoTimer)
            {
                if (_prevTagged <= 0 && tagged > 0) { _running = true; _roundOver = false; _t = 0f; }
                if (tagged == 0) { _running = false; _roundOver = false; _t = 0f; _result = ""; }
                if (_running && total > 1 && tagged >= total) { _running = false; _roundOver = true; _result = "CHASERS WIN"; }
            }
            if (_running)
            {
                _t += dt;
                if (_t >= Cap) { _t = Cap; _running = false; _roundOver = true; _result = "SURVIVORS WIN"; }
            }
            _prevTagged = tagged;
        }

        public void Draw(IList<VRRig> rigs)
        {
            if (!ShowTimer && !ShowScoreboard) return;

            int total = rigs.Count;
            int infected = CountTagged(rigs);
            int survivors = Mathf.Max(0, total - infected);

            string mode = "INFECTION";
            string itName = null;
            try
            {
                var gm = GorillaGameManager.instance as GorillaTagManager;
                if (gm != null)
                {
                    mode = gm.isCurrentlyTag ? "TAG" : "INFECTION";
                    if (gm.currentIt != null && gm.currentIt.IsValid) itName = gm.currentIt.NickName;
                }
            }
            catch { }

            float w = 380, h = (ShowTeams ? 116 : 92);
            var r = new Rect((Screen.width - w) / 2f, 14, w, h);
            Styles.DrawCard(r, new Color(0.06f, 0.07f, 0.09f, 0.92f), 8f);
            Styles.Round(new Rect(r.x, r.y, r.width, 3f), Styles.Accent, 1.5f);

            // mode
            GUI.Label(new Rect(r.x, r.y + 7, r.width, 16), mode,
                new GUIStyle(Styles.Header) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });

            // timer
            if (ShowTimer)
            {
                Color tc = _t >= Cap ? new Color(0.3f, 0.85f, 1f) : (_t >= 150f ? new Color(1f, 0.82f, 0.25f) : Color.white);
                string ts = _roundOver ? _result : $"{(int)_t / 60}:{(int)_t % 60:00}";
                var st = new GUIStyle(Styles.Hud) { fontSize = _roundOver ? 26 : 34, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = _roundOver ? Styles.Accent2 : tc;
                GUI.Label(new Rect(r.x, r.y + 22, r.width, 40), ts, st);
            }

            // scoreboard line
            if (ShowScoreboard)
            {
                float yy = ShowTimer ? r.y + 64 : r.y + 36;
                var s = new GUIStyle(Styles.Hud) { fontSize = 14, alignment = TextAnchor.MiddleCenter, richText = true };
                GUI.Label(new Rect(r.x, yy, r.width, 18),
                    $"<color=#e8eaee>SURVIVORS {survivors}</color>    <color=#db4d58>INFECTED {infected}</color>", s);
                if (itName != null)
                    GUI.Label(new Rect(r.x, yy + 18, r.width, 14),
                        $"<color=#9aa0a8>IT:</color> {itName}", new GUIStyle(s) { fontSize = 11 });
            }

            // team scores
            if (ShowTeams)
            {
                var ts = new GUIStyle(Styles.Hud) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(r.x, r.yMax - 26, r.width, 20),
                    $"<color=#e8eaee>{TeamAName} {TeamA}</color>   —   <color=#db4d58>{TeamB} {TeamBName}</color>",
                    new GUIStyle(ts) { richText = true });
            }
        }

        private static int CountTagged(IList<VRRig> rigs)
        {
            int c = 0;
            for (int i = 0; i < rigs.Count; i++) if (CasterUtil.IsTagged(rigs[i])) c++;
            return c;
        }
    }
}
