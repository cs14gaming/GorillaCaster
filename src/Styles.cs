using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Premium dark theme: rounded panels, soft shadows, switch toggles, rounded sliders.
    /// </summary>
    internal static class Styles
    {
        private static bool _built;

        // palette — clean monochrome (black / grey / white) with a red alert accent
        public static readonly Color Accent = new Color(0.95f, 0.96f, 0.97f);   // white
        public static readonly Color Accent2 = new Color(0.86f, 0.32f, 0.36f);  // red (alerts / IT)
        public static readonly Color BgCol = new Color(0.055f, 0.056f, 0.060f, 0.99f);
        public static readonly Color RailCol = new Color(0.038f, 0.039f, 0.042f, 1f);
        public static readonly Color CardCol = new Color(0.094f, 0.096f, 0.102f, 1f);
        public static readonly Color PanelCol = new Color(0.135f, 0.138f, 0.146f, 1f);
        public static readonly Color PanelHi = new Color(0.205f, 0.210f, 0.222f, 1f);
        public static readonly Color SubText = new Color(0.60f, 0.62f, 0.66f);

        // textures
        public static Texture2D White, Card, Btn, BtnHi, BtnAccent, Pill, TrackTex, ThumbTex, Shadow, RailPillTex, SwitchOn, SwitchOff, Knob, TagBg, TagGlow;

        // styles
        public static GUIStyle Brand, Title, Header, Label, Value, Sub, Hud, BtnS, BtnPrimaryS, RailS, RailOnS, SliderS, ThumbS, PillS, Tag, TagShadow, LowerThirdName, LowerThirdSub, Note, Water;

        // reusable scratch styles (mutate fontSize/color before use — avoids per-frame GC)
        public static GUIStyle ScratchTag, ScratchTagShadow, ScratchLabel, ScratchHud;

        public static void Ensure()
        {
            if (_built) return;
            _built = true;

            White = TextureGen.Solid(Color.white);
            Card = TextureGen.RoundedRect(40, 14, CardCol);
            Btn = TextureGen.RoundedRect(26, 9, PanelCol);
            BtnHi = TextureGen.RoundedRect(26, 9, PanelHi);
            BtnAccent = TextureGen.RoundedRect(26, 9, Accent);
            Pill = TextureGen.RoundedRect(28, 12, new Color(1, 1, 1, 0.05f));
            TrackTex = TextureGen.RoundedRect(12, 5, new Color(0.26f, 0.29f, 0.35f));
            ThumbTex = TextureGen.Circle(18, Accent);
            Shadow = TextureGen.SoftShadow(48, new Color(0, 0, 0, 0.55f));
            RailPillTex = TextureGen.RoundedRect(26, 10, PanelCol);
            SwitchOn = TextureGen.RoundedRect(28, 13, Accent);
            SwitchOff = TextureGen.RoundedRect(28, 13, new Color(0.26f, 0.29f, 0.35f));
            Knob = TextureGen.Circle(20, Color.white);
            TagBg = TextureGen.RoundedRect(24, 10, new Color(0.06f, 0.07f, 0.09f, 0.86f));
            TagGlow = TextureGen.RoundedOutline(28, 12, 2, Accent);

            Brand = Mk(15, FontStyle.Bold, Color.white);
            Title = Mk(13, FontStyle.Bold, Color.white);
            Header = Mk(11, FontStyle.Bold, Accent); Header.margin = new RectOffset(2, 0, 10, 4);
            Label = Mk(12, FontStyle.Normal, new Color(0.88f, 0.91f, 0.95f));
            Sub = Mk(11, FontStyle.Normal, SubText);
            Value = Mk(12, FontStyle.Bold, Accent); Value.alignment = TextAnchor.MiddleRight;
            Hud = Mk(13, FontStyle.Bold, Color.white);
            Water = Mk(13, FontStyle.Bold, Color.white); Water.alignment = TextAnchor.MiddleRight;

            BtnS = Btn9(Btn, BtnHi, BtnAccent, Color.white);
            BtnPrimaryS = Btn9(BtnAccent, BtnAccent, BtnAccent, new Color(0.03f, 0.05f, 0.07f));

            RailS = Mk(13, FontStyle.Bold, SubText);
            RailS.alignment = TextAnchor.MiddleLeft; RailS.padding = new RectOffset(16, 4, 0, 0);
            RailS.hover.textColor = Color.white;
            RailOnS = new GUIStyle(RailS);
            RailOnS.normal.background = RailPillTex; RailOnS.border = new RectOffset(10, 10, 10, 10);
            RailOnS.normal.textColor = Color.white; RailOnS.hover.textColor = Color.white;

            SliderS = new GUIStyle(GUI.skin.horizontalSlider);
            SliderS.normal.background = TrackTex; SliderS.fixedHeight = 8; SliderS.border = new RectOffset(5, 5, 5, 5);
            SliderS.margin = new RectOffset(0, 0, 8, 10);
            ThumbS = new GUIStyle(GUI.skin.horizontalSliderThumb);
            ThumbS.normal.background = ThumbTex; ThumbS.active.background = ThumbTex; ThumbS.fixedWidth = 16; ThumbS.fixedHeight = 16; ThumbS.border = new RectOffset(8, 8, 8, 8);

            PillS = Mk(12, FontStyle.Bold, Color.white); PillS.alignment = TextAnchor.MiddleLeft;
            PillS.padding = new RectOffset(12, 12, 6, 6); PillS.margin = new RectOffset(0, 0, 3, 3);
            PillS.normal.background = Pill; PillS.border = new RectOffset(12, 12, 12, 12);

            Note = Mk(11, FontStyle.Normal, SubText); Note.wordWrap = true;
            Note.normal.background = TextureGen.RoundedRect(20, 8, new Color(1, 1, 1, 0.04f));
            Note.border = new RectOffset(8, 8, 8, 8); Note.padding = new RectOffset(10, 10, 7, 7); Note.margin = new RectOffset(0, 0, 4, 6);

            Tag = Mk(13, FontStyle.Bold, Color.white); Tag.alignment = TextAnchor.MiddleCenter;
            TagShadow = new GUIStyle(Tag); TagShadow.normal.textColor = new Color(0, 0, 0, 0.85f);
            LowerThirdName = Mk(22, FontStyle.Bold, Color.white);
            LowerThirdSub = Mk(13, FontStyle.Bold, new Color(0.8f, 0.85f, 0.9f));

            ScratchTag = new GUIStyle(Tag) { alignment = TextAnchor.MiddleLeft };
            ScratchTagShadow = new GUIStyle(TagShadow) { alignment = TextAnchor.MiddleLeft };
            ScratchLabel = new GUIStyle(Hud) { fontSize = 12 };
            ScratchHud = new GUIStyle(Hud);
        }

        private static GUIStyle Btn9(Texture2D n, Texture2D h, Texture2D a, Color text)
        {
            var s = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, richText = true, alignment = TextAnchor.MiddleCenter };
            s.normal.background = n; s.normal.textColor = text;
            s.hover.background = h; s.hover.textColor = Color.white;
            s.active.background = a; s.active.textColor = text;
            s.border = new RectOffset(9, 9, 9, 9); s.padding = new RectOffset(8, 8, 6, 6); s.margin = new RectOffset(3, 3, 3, 3);
            return s;
        }

        private static GUIStyle Mk(int size, FontStyle fs, Color c)
        {
            var s = new GUIStyle { fontSize = size, fontStyle = fs, richText = true };
            s.normal.textColor = c;
            return s;
        }

        public static void Fill(Rect r, Color c)
        {
            var p = GUI.color; GUI.color = c; GUI.DrawTexture(r, White); GUI.color = p;
        }

        /// <summary>Rounded panel with a soft drop shadow.</summary>
        public static void DrawCard(Rect r, Color col, float shadow = 8f)
        {
            if (shadow > 0f)
            {
                var sr = new Rect(r.x - shadow, r.y - shadow + 3, r.width + shadow * 2, r.height + shadow * 2);
                var p = GUI.color; GUI.color = new Color(1, 1, 1, 0.5f); GUI.DrawTexture(sr, Shadow); GUI.color = p;
            }
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, col, 0f, 14f);
        }

        /// <summary>Rounded filled rect (no shadow).</summary>
        public static void Round(Rect r, Color col, float radius = 10f)
        {
            GUI.DrawTexture(r, White, ScaleMode.StretchToFill, true, 0f, col, 0f, radius);
        }
    }
}
