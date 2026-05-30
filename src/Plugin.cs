using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GorillaCaster
{
    /// <summary>
    /// BepInEx entry point. Loads config and spawns the persistent CasterController.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.forza.gorillacaster";
        public const string Name = "GorillaCaster";
        public const string Version = "1.11.1";

        public static Plugin Instance { get; private set; }

        // --- config ---
        public static ConfigEntry<Key> MenuKey;
        public static ConfigEntry<Key> ModeKey;
        public static ConfigEntry<Key> ScreenshotKey;
        public static ConfigEntry<float> DefaultFov;
        public static ConfigEntry<bool> NametagsDefault;
        public static ConfigEntry<bool> MinimapDefault;
        public static ConfigEntry<string> WatermarkText;
        public static ConfigEntry<float> WatermarkOpacity;

        private void Awake()
        {
            Instance = this;

            MenuKey = Config.Bind("Keys", "Menu", Key.RightCtrl, "Toggle the caster menu.");
            ModeKey = Config.Bind("Keys", "CycleMode", Key.P, "Cycle camera mode.");
            ScreenshotKey = Config.Bind("Keys", "Screenshot", Key.F11, "Capture a screenshot to your Pictures folder.");
            DefaultFov = Config.Bind("Camera", "DefaultFov", 90f, new ConfigDescription("Starting field of view.", new AcceptableValueRange<float>(10f, 120f)));
            NametagsDefault = Config.Bind("Overlays", "Nametags", true, "Show floating nametags over players by default.");
            MinimapDefault = Config.Bind("Overlays", "Minimap", false, "Show the overhead minimap by default.");
            WatermarkText = Config.Bind("Watermark", "Text", "Spooder's Camera Mod", "Watermark text.");
            WatermarkOpacity = Config.Bind("Watermark", "Opacity", 0.55f, new ConfigDescription("Watermark opacity (always shown).", new AcceptableValueRange<float>(0.25f, 1f)));

            var host = new GameObject("GorillaCaster");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<CasterController>();

            Logger.LogInfo($"{Name} v{Version} loaded. Press {MenuKey.Value} in game to open the caster menu.");
        }
    }
}
