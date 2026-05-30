using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace GorillaCaster
{
    internal class ModReport { public string name; public string detail; public Color color; }

    /// <summary>
    /// Lightweight Gorilla Tag "mod checker": for every player it reports platform, speed and
    /// behaviour/property flags. Real mod detection on other clients is only possible via the
    /// signals they leak — Photon custom properties (cheat menus often add keys) and physics
    /// anomalies (speed / flight / teleport). It cannot see arbitrary client-side mods.
    /// </summary>
    internal static class ModChecker
    {
        // Known cheat / mod-menu Photon property keys (extensible). Many menus stamp a key here.
        private static readonly HashSet<string> CheatKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Crew", "crewp", "isModded", "cheater", "modmenu", "menu", "iblastoff", "stickbug",
            "hydra", "atlas", "rumble", "mintchoco", "frostbite", "goldentag", "monkemenu",
            "silly", "redwood", "spiderman", "grappler", "speed", "fly", "noclip", "platformlock"
        };

        public static List<ModReport> Scan(IList<VRRig> rigs)
        {
            var list = new List<ModReport>();
            Dictionary<int, Player> players = null;
            try { if (PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null) players = PhotonNetwork.CurrentRoom.Players; }
            catch { }

            for (int i = 0; i < rigs.Count; i++)
            {
                var rig = rigs[i];
                if (rig == null) continue;

                string name = CasterUtil.NameOf(rig);
                float speed = CasterUtil.Speed(rig);
                var flags = new List<string>();
                if (speed > 25f) flags.Add("SPEED");
                try { if (rig.LatestVelocity().y > 9f) flags.Add("FLY"); } catch { }

                string platform = "";
                try
                {
                    var np = rig.Creator;
                    if (np != null && players != null && players.TryGetValue(np.ActorNumber, out Player pl) && pl.CustomProperties != null)
                    {
                        foreach (object key in pl.CustomProperties.Keys)
                        {
                            string ks = key != null ? key.ToString() : "";
                            if (CheatKeys.Contains(ks) && !flags.Contains("MENU")) flags.Add("MENU");
                        }
                        if (pl.CustomProperties.ContainsKey("platform")) platform = SafeStr(pl.CustomProperties["platform"]);
                        else if (pl.CustomProperties.ContainsKey("didPlatform")) platform = SafeStr(pl.CustomProperties["didPlatform"]);
                    }
                }
                catch { }

                bool flagged = flags.Count > 0;
                string detail = (platform.Length > 0 ? platform + "  ·  " : "") + $"{speed:0} m/s  ·  " + (flagged ? string.Join(" ", flags) : "clean");
                Color col = flagged ? new Color(1f, 0.42f, 0.42f) : new Color(0.7f, 0.86f, 1f);
                list.Add(new ModReport { name = name, detail = detail, color = col });
            }
            return list;
        }

        private static string SafeStr(object o) { try { return o != null ? o.ToString() : ""; } catch { return ""; } }
    }
}
