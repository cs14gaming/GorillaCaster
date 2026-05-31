using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>One keyframe of a saved dolly shot (camera pose).</summary>
    [Serializable]
    public class DollyKey { public Vector3 pos; public Quaternion rot; }

    /// <summary>A named, reusable cinematic camera move.</summary>
    [Serializable]
    public class DollyShot
    {
        public string name = "Shot";
        public float secondsPerSegment = 2.5f;
        public bool loop = false;
        public List<DollyKey> keys = new List<DollyKey>();
    }

    [Serializable]
    internal class DollyFile { public List<DollyShot> shots = new List<DollyShot>(); }

    /// <summary>Load/save named dolly shots to BepInEx/config as JSON (via Unity JsonUtility).</summary>
    internal static class DollyStore
    {
        private static string FilePath
        {
            get
            {
                try { return Path.Combine(BepInEx.Paths.ConfigPath, "GorillaCaster_dolly.json"); }
                catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GorillaCaster_dolly.json"); }
            }
        }

        public static List<DollyShot> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var df = JsonUtility.FromJson<DollyFile>(File.ReadAllText(FilePath));
                    if (df != null && df.shots != null) return df.shots;
                }
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] dolly load: " + e.Message); }
            return new List<DollyShot>();
        }

        public static void Save(List<DollyShot> list)
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new DollyFile { shots = list ?? new List<DollyShot>() }, true)); }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] dolly save: " + e.Message); }
        }
    }
}
