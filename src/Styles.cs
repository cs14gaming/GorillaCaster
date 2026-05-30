using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Lazily-built IMGUI styles + solid-color textures for the caster HUD.
    /// </summary>
    internal static class Styles
    {
        private static bool _built;

        public static GUIStyle Header;        // section title
        public static GUIStyle Tag;           // floating nametag
        public static GUIStyle TagShadow;     // nametag drop shadow
        public static GUIStyle LowerThirdName;
        public static GUIStyle LowerThirdSub;
        public static GUIStyle Hud;           // small white hud text

        public static Texture2D Black;         // 70% black
        public static Texture2D Panel;         // dark panel
        public static Texture2D White;
        public static Texture2D Accent;        // brand accent

        public static readonly Color BrandAccent = new Color(0.36f, 0.78f, 1f);

        public static void Ensure()
        {
            if (_built) return;
            _built = true;

            Black = Solid(new Color(0f, 0f, 0f, 0.72f));
            Panel = Solid(new Color(0.10f, 0.11f, 0.13f, 0.94f));
            White = Solid(Color.white);
            Accent = Solid(BrandAccent);

            Header = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, richText = true };
            Header.normal.textColor = BrandAccent;

            Hud = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, richText = true };
            Hud.normal.textColor = Color.white;

            Tag = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            Tag.normal.textColor = Color.white;

            TagShadow = new GUIStyle(Tag);
            TagShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);

            LowerThirdName = new GUIStyle { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, richText = true };
            LowerThirdName.normal.textColor = Color.white;

            LowerThirdSub = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, richText = true };
            LowerThirdSub.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Fill a rect with a flat color.</summary>
        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = prev;
        }
    }
}
