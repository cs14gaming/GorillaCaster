using UnityEngine;

namespace GorillaCaster
{
    /// <summary>Themed IMGUI widgets used by the caster menu.</summary>
    internal static class UI
    {
        public static void Header(string text)
        {
            GUILayout.Space(6);
            GUILayout.Label(text.ToUpperInvariant(), Styles.Header);
            var r = GUILayoutUtility.GetRect(10, 2, GUILayout.ExpandWidth(true));
            Styles.Fill(new Rect(r.x, r.y, 34, 2), Styles.Accent);
            Styles.Fill(new Rect(r.x + 34, r.y, r.width - 34, 1), new Color(1, 1, 1, 0.08f));
            GUILayout.Space(2);
        }

        /// <summary>Labeled slider with a live value readout. Returns the new value.</summary>
        public static float Slider(string label, float value, float min, float max, string fmt = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Styles.Label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value.ToString(fmt), Styles.Value, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(value, min, max, Styles.Slider, Styles.SliderThumb);
        }

        /// <summary>Pill toggle. Returns the new state.</summary>
        public static bool Toggle(string label, bool value)
        {
            GUILayout.BeginHorizontal(Styles.Pill, GUILayout.Height(26));
            bool clicked = GUILayout.Button(label, Styles.Label, GUILayout.ExpandWidth(true));
            var box = GUILayoutUtility.GetRect(36, 18, GUILayout.Width(36));
            Styles.Fill(box, value ? Styles.Accent : new Color(0.28f, 0.31f, 0.36f));
            float knob = value ? box.xMax - 16 : box.x + 2;
            Styles.Fill(new Rect(knob, box.y + 2, 14, 14), Color.white);
            GUILayout.EndHorizontal();
            return clicked ? !value : value;
        }

        public static bool Button(string label)
        {
            return GUILayout.Button(label, Styles.Btn, GUILayout.Height(28));
        }

        public static bool SmallButton(string label, float width)
        {
            return GUILayout.Button(label, Styles.Btn, GUILayout.Height(26), GUILayout.Width(width));
        }

        public static void Note(string text)
        {
            GUILayout.Label(text, Styles.BoxNote);
        }
    }
}
