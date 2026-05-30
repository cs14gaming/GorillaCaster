using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>A saved camera/look preset.</summary>
    [Serializable]
    public class CamPreset
    {
        public string name = "Preset";
        public float fov = 90f, nearClip = 0.05f, followDist = 1.4f, followHeight = 0.25f, moveSmooth = 0.45f, rotSmooth = 0.45f;
        public int mode = 0, filter = 0, aspect = 0;
        public float vignette = 0f, filterStrength = 0.8f;
        public bool nametags = true, lowerThird = true, minimap = false, letterbox = false, thirds = false;
    }

    [Serializable]
    internal class PresetFile { public List<CamPreset> presets = new List<CamPreset>(); }

    /// <summary>Load/save camera presets to BepInEx/config as JSON (via Unity JsonUtility).</summary>
    internal static class Presets
    {
        private static string FilePath
        {
            get
            {
                try { return Path.Combine(BepInEx.Paths.ConfigPath, "GorillaCaster_presets.json"); }
                catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GorillaCaster_presets.json"); }
            }
        }

        public static List<CamPreset> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var pf = JsonUtility.FromJson<PresetFile>(File.ReadAllText(FilePath));
                    if (pf != null && pf.presets != null && pf.presets.Count > 0) return pf.presets;
                }
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] presets load: " + e.Message); }

            var def = Defaults();
            Save(def);
            return def;
        }

        public static void Save(List<CamPreset> list)
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new PresetFile { presets = list }, true)); }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] presets save: " + e.Message); }
        }

        private static List<CamPreset> Defaults() => new List<CamPreset>
        {
            new CamPreset { name = "Cinematic Follow", fov = 70, followDist = 1.8f, followHeight = 0.35f, moveSmooth = 0.6f, rotSmooth = 0.6f, filter = 1, vignette = 0.2f, aspect = 2, letterbox = false },
            new CamPreset { name = "Close Action", fov = 95, followDist = 1.0f, followHeight = 0.15f, moveSmooth = 0.3f, rotSmooth = 0.3f, filter = 4 },
            new CamPreset { name = "Wide Caster", fov = 100, followDist = 2.6f, followHeight = 0.6f, moveSmooth = 0.5f, rotSmooth = 0.5f, aspect = 1, nametags = true },
            new CamPreset { name = "Vertical Clip", fov = 80, followDist = 1.4f, aspect = 4, filter = 6, vignette = 0.3f },
            new CamPreset { name = "Horror Night", fov = 85, followDist = 1.3f, filter = 7, vignette = 0.5f },
        };
    }
}
