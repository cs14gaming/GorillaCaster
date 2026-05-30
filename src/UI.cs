using UnityEngine;

namespace GorillaCaster
{
    /// <summary>Premium themed IMGUI widgets used by the caster menu.</summary>
    internal static class UI
    {
        public static void Header(string text)
        {
            GUILayout.Space(8);
            GUILayout.Label(text.ToUpperInvariant(), Styles.Header);
            var r = GUILayoutUtility.GetRect(10, 3, GUILayout.ExpandWidth(true));
            Styles.Round(new Rect(r.x, r.y, 26, 3), Styles.Accent, 1.5f);
            Styles.Fill(new Rect(r.x + 30, r.y + 1, r.width - 30, 1), new Color(1, 1, 1, 0.07f));
            GUILayout.Space(3);
        }

        public static float Slider(string label, float value, float min, float max, string fmt = "0.00")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Styles.Label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(value.ToString(fmt), Styles.Value, GUILayout.Width(64));
            GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(value, min, max, Styles.SliderS, Styles.ThumbS);
        }

        /// <summary>iOS-style switch toggle.</summary>
        public static bool Toggle(string label, bool value)
        {
            var rect = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            GUI.Label(new Rect(rect.x + 4, rect.y, rect.width - 60, rect.height),
                label, new GUIStyle(Styles.Label) { alignment = TextAnchor.MiddleLeft });

            var sw = new Rect(rect.xMax - 46, rect.y + 6, 40, 18);
            GUI.DrawTexture(sw, value ? Styles.SwitchOn : Styles.SwitchOff, ScaleMode.StretchToFill, true, 0, Color.white, 0, 9);
            float kx = value ? sw.xMax - 17 : sw.x + 1;
            GUI.DrawTexture(new Rect(kx, sw.y + 1, 16, 16), Styles.Knob);
            return clicked ? !value : value;
        }

        public static bool Button(string label)
            => GUILayout.Button(label, Styles.BtnS, GUILayout.Height(30));

        public static bool Primary(string label)
            => GUILayout.Button(label, Styles.BtnPrimaryS, GUILayout.Height(32));

        public static bool SmallButton(string label, float width)
            => GUILayout.Button(label, Styles.BtnS, GUILayout.Height(28), GUILayout.Width(width));

        public static void Note(string text) => GUILayout.Label(text, Styles.Note);
    }
}
