using System;
using System.IO;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>Every persisted menu toggle + tunable. Saved to config so the mod
    /// remembers your setup between sessions (like a paid mod).</summary>
    [Serializable]
    public class CasterSettings
    {
        public int mode = 0;
        // lens
        public float fov = 90f, nearClip = 0.05f, fpNearClip = 0.30f;
        public bool fpHideSelf = false, fpHideCosmetics = true;
        public Vector3 fpOffset = new Vector3(0f, 0f, 0.06f);
        // follow / orbit
        public float followDistance = 1.4f, followHeight = 0.25f, followLead = 0f;
        public float moveSmoothing = 0.45f, rotSmoothing = 0.45f, freeSpeed = 6f;
        public bool orbit = false; public float orbitSpeed = 25f, orbitPitch = 0f;
        // rig
        public bool collision = true; public float collisionRadius = 0.18f;
        public float roll = 0f; public bool rollLock = false;
        public bool angleClamp = false; public float fpMaxPitch = 75f;
        public float shake = 0f, selfieDist = 0.6f;
        public float goProStabilize = 0f; public bool goProAutoLevel = false;
        // look / grade
        public int filter = 0, aspect = 0; public float filterStrength = 0.85f, vignette = 0f;
        public bool thirds = false, crosshair = false, letterbox = false;
        // green screen
        public bool greenScreen = false; public Color greenColor = new Color(0f, 0.7f, 0.1f);
        // overlays
        public bool nametags = true, nametagVelocity = false, minimap = false;
        public bool lowerThird = true, playerList = true, hud = true;
        public bool nametagOcclude = true; public float nametagScale = 1f;
        public bool leaderboard = false;
        public bool vrNametags = false, vrNametagVel = false; public float vrNametagSize = 1f;
        public float rigLerp = 1f, watermarkOpacity = 0.55f;
        // behaviour
        public bool autoCast = false, autoDirector = false, keepAfk = true;
    }

    /// <summary>Load/save the full settings blob to BepInEx/config as JSON.</summary>
    internal static class SettingsStore
    {
        private static string FilePath
        {
            get
            {
                try { return Path.Combine(BepInEx.Paths.ConfigPath, "GorillaCaster_settings.json"); }
                catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GorillaCaster_settings.json"); }
            }
        }

        public static CasterSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = JsonUtility.FromJson<CasterSettings>(File.ReadAllText(FilePath));
                    if (s != null) return s;
                }
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] settings load: " + e.Message); }
            return null;
        }

        public static void Save(CasterSettings s)
        {
            if (s == null) return;
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(s, true)); }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] settings save: " + e.Message); }
        }
    }
}
