using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Dark theme for the caster HUD + menu: solid-color textures and pre-built GUIStyles.
    /// </summary>
    internal static class Styles
    {
        private static bool _built;

        // palette
        public static readonly Color Accent = new Color(0.36f, 0.78f, 1f);
        public static readonly Color Accent2 = new Color(1f, 0.33f, 0.46f);
        public static readonly Color BgCol = new Color(0.082f, 0.090f, 0.110f, 0.98f);
        public static readonly Color RailCol = new Color(0.055f, 0.062f, 0.078f, 1f);
        public static readonly Color PanelCol = new Color(0.130f, 0.145f, 0.170f, 1f);
        public static readonly Color PanelHi = new Color(0.180f, 0.200f, 0.230f, 1f);
        public static readonly Color SubText = new Color(0.66f, 0.71f, 0.78f);

        // textures
        public static Texture2D White, Bg, Rail, Panel, PanelHover, AccentTex, AccentDim, Track, Thumb, ToggleOff;

        // styles
        public static GUIStyle Title, Brand, Header, Label, Value, Sub, Hud, Btn, RailBtn, RailBtnOn, Slider, SliderThumb, Pill, Tag, TagShadow, LowerThirdName, LowerThirdSub, BoxNote;

        public static void Ensure()
        {
            if (_built) return;
            _built = true;

            White = Solid(Color.white);
            Bg = Solid(BgCol);
            Rail = Solid(RailCol);
            Panel = Solid(PanelCol);
            PanelHover = Solid(PanelHi);
            AccentTex = Solid(Accent);
            AccentDim = Solid(new Color(Accent.r, Accent.g, Accent.b, 0.22f));
            Track = Solid(new Color(0.25f, 0.28f, 0.33f, 1f));
            Thumb = Solid(Accent);
            ToggleOff = Solid(new Color(0.28f, 0.31f, 0.36f, 1f));

            Brand = Mk(15, FontStyle.Bold, Accent);
            Title = Mk(13, FontStyle.Bold, Color.white);
            Header = Mk(12, FontStyle.Bold, Accent); Header.margin = new RectOffset(0, 0, 8, 4);
            Label = Mk(12, FontStyle.Normal, Color.white);
            Sub = Mk(11, FontStyle.Normal, SubText);
            Value = Mk(12, FontStyle.Bold, SubText); Value.alignment = TextAnchor.MiddleRight;
            Hud = Mk(13, FontStyle.Bold, Color.white); Hud.richText = true;

            Btn = new GUIStyle(GUI.skin.button) { fontSize = 12, fontStyle = FontStyle.Bold, richText = true };
            Btn.normal.background = Panel; Btn.normal.textColor = Color.white;
            Btn.hover.background = PanelHover; Btn.hover.textColor = Color.white;
            Btn.active.background = AccentDim; Btn.active.textColor = Color.white;
            Btn.border = new RectOffset(6, 6, 6, 6); Btn.padding = new RectOffset(8, 8, 6, 6);
            Btn.margin = new RectOffset(2, 2, 2, 2);

            RailBtn = Mk(13, FontStyle.Bold, SubText);
            RailBtn.alignment = TextAnchor.MiddleLeft; RailBtn.padding = new RectOffset(14, 4, 0, 0);
            RailBtn.normal.background = Rail; RailBtn.hover.background = Panel; RailBtn.hover.textColor = Color.white;

            RailBtnOn = new GUIStyle(RailBtn) { fontStyle = FontStyle.Bold };
            RailBtnOn.normal.background = Panel; RailBtnOn.normal.textColor = Accent;
            RailBtnOn.hover.background = Panel; RailBtnOn.hover.textColor = Accent;

            Slider = new GUIStyle(GUI.skin.horizontalSlider);
            Slider.normal.background = Track; Slider.fixedHeight = 6; Slider.border = new RectOffset(3, 3, 3, 3);
            Slider.margin = new RectOffset(0, 0, 8, 8);

            SliderThumb = new GUIStyle(GUI.skin.horizontalSliderThumb);
            SliderThumb.normal.background = Thumb; SliderThumb.active.background = Thumb;
            SliderThumb.fixedWidth = 14; SliderThumb.fixedHeight = 14; SliderThumb.border = new RectOffset(7, 7, 7, 7);

            Pill = Mk(12, FontStyle.Bold, Color.white); Pill.alignment = TextAnchor.MiddleLeft;
            Pill.padding = new RectOffset(8, 8, 4, 4); Pill.margin = new RectOffset(2, 2, 2, 2);
            Pill.normal.background = Panel; Pill.hover.background = PanelHover; Pill.hover.textColor = Color.white;

            BoxNote = Mk(11, FontStyle.Normal, SubText); BoxNote.wordWrap = true;
            BoxNote.normal.background = Solid(new Color(1, 1, 1, 0.04f)); BoxNote.padding = new RectOffset(8, 8, 6, 6);
            BoxNote.margin = new RectOffset(0, 0, 4, 4);

            Tag = Mk(14, FontStyle.Bold, Color.white); Tag.alignment = TextAnchor.MiddleCenter; Tag.richText = true;
            TagShadow = new GUIStyle(Tag); TagShadow.normal.textColor = new Color(0, 0, 0, 0.85f);

            LowerThirdName = Mk(22, FontStyle.Bold, Color.white); LowerThirdName.richText = true;
            LowerThirdSub = Mk(13, FontStyle.Bold, new Color(0.8f, 0.85f, 0.9f)); LowerThirdSub.richText = true;
        }

        private static GUIStyle Mk(int size, FontStyle fs, Color c)
        {
            var s = new GUIStyle { fontSize = size, fontStyle = fs, richText = true };
            s.normal.textColor = c;
            return s;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        public static void Fill(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, White); GUI.color = prev;
        }
    }
}
