using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Known cheat / mod-menu signatures looked for in players' Photon custom properties.
    /// Seed list of common Gorilla Tag cheat menus + cosmetic spoofers; extend it yourself by
    /// dropping names (one per line) into BepInEx/config/GorillaCaster_cheatkeys.txt.
    /// Matching is case-insensitive and substring-based, so "cosmetx" also catches
    /// "ForeverCosmetx", "cosmetx_v2", etc.
    /// </summary>
    internal static class CheatDatabase
    {
        // Named cheat menus / spoofers + generic property keys cheats stamp on the network.
        private static readonly string[] Seed =
        {
            // cosmetic spoofers / unlockers
            "cosmetx", "forevercosmetx", "cosmeticx", "everycosmetic", "cosmeticunlock",
            // popular paid/free cheat menus
            "saturn", "astre", "xenon", "violet", "aspects", "seralyth", "juul", "hydra",
            "atlas", "monkemenu", "monkimenu", "grappler", "iblastoff", "stickbug", "mintchoco",
            "frostbite", "goldentag", "redwood", "spiderman", "rumblemenu", "obsidian", "phantom",
            "eclipse", "nebula", "voidmenu", "crystal", "matrix", "exodus", "infinity",
            // generic self-announcing keys
            "ismodded", "cheater", "modmenu", "modmenuactive", "menuactive", "crew", "crewp",
            "fly", "flymod", "noclip", "speedhack", "speedmod", "platformlock", "platformspoof",
            "playspace", "psa", "pullmod", "puller", "reachmod", "tagrange", "godmode",
        };

        // Legit keys that look suspicious but are normal — never flag these.
        private static readonly HashSet<string> Allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "platform", "didplatform", "didtutorial", "color", "cosmetics", "usepdate", "release" };

        private static HashSet<string> _user;

        private static string FilePath
        {
            get
            {
                try { return Path.Combine(BepInEx.Paths.ConfigPath, "GorillaCaster_cheatkeys.txt"); }
                catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GorillaCaster_cheatkeys.txt"); }
            }
        }

        /// <summary>Load user-supplied extra signatures (creates a starter file if missing).</summary>
        public static void EnsureLoaded()
        {
            if (_user != null) return;
            _user = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        var s = raw.Trim();
                        if (s.Length > 0 && !s.StartsWith("#")) _user.Add(s);
                    }
                }
                else
                {
                    File.WriteAllText(FilePath,
                        "# GorillaCaster cheat signatures — one per line, case-insensitive substring match.\n" +
                        "# Add new cheat-menu / mod names here as they appear.\n");
                }
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] cheatkeys load: " + e.Message); }
        }

        /// <summary>Is this property key/value a known cheat signature? Returns the matched tag.</summary>
        public static bool Match(string text, out string matched)
        {
            matched = null;
            if (string.IsNullOrEmpty(text)) return false;
            EnsureLoaded();
            string low = text.ToLowerInvariant();
            if (Allow.Contains(low)) return false;
            foreach (var s in Seed) if (low.Contains(s)) { matched = s; return true; }
            foreach (var s in _user) if (low.Contains(s.ToLowerInvariant())) { matched = s; return true; }
            return false;
        }
    }
}
