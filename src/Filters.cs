using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Broadcast look: colour-grade filters, vignette, aspect-ratio framing guides and a
    /// rule-of-thirds grid — drawn over the spectator view (monitor / OBS capture only).
    /// </summary>
    internal static class Filters
    {
        public static readonly string[] Names = { "None", "Warm", "Cool", "Noir", "Vibrant", "Vintage", "Dream", "Horror" };
        public static readonly string[] AspectNames = { "Full", "16:9", "2.39:1", "4:3", "9:16 (vert)", "1:1" };

        // tint colour + base alpha + vignette boost per filter
        private static readonly Color[] Tint =
        {
            new Color(0,0,0,0),
            new Color(1.0f, 0.78f, 0.50f, 0.16f),  // warm
            new Color(0.55f, 0.72f, 1.0f, 0.16f),  // cool
            new Color(0.20f, 0.23f, 0.28f, 0.34f),  // noir
            new Color(1.0f, 0.55f, 0.85f, 0.12f),  // vibrant
            new Color(0.85f, 0.70f, 0.42f, 0.24f),  // vintage (sepia)
            new Color(0.80f, 0.85f, 1.0f, 0.20f),  // dream
            new Color(0.45f, 0.02f, 0.04f, 0.24f),  // horror
        };
        private static readonly float[] Vig = { 0f, 0.15f, 0.15f, 0.55f, 0.10f, 0.40f, 0.25f, 0.65f };

        private static Texture2D _vig;

        public static void Draw(int filter, float strength, float vignette, int aspect, bool thirds)
        {
            Rect frame = FrameRect(aspect);

            // letterbox / pillarbox bars outside the frame
            if (aspect != 0)
            {
                Styles.Fill(new Rect(0, 0, Screen.width, frame.y), Color.black);
                Styles.Fill(new Rect(0, frame.yMax, Screen.width, Screen.height - frame.yMax), Color.black);
                Styles.Fill(new Rect(0, frame.y, frame.x, frame.height), Color.black);
                Styles.Fill(new Rect(frame.xMax, frame.y, Screen.width - frame.xMax, frame.height), Color.black);
            }

            // colour grade
            filter = Mathf.Clamp(filter, 0, Tint.Length - 1);
            if (filter > 0 && strength > 0.01f)
            {
                Color c = Tint[filter];
                c.a *= strength;
                Styles.Fill(frame, c);
            }

            // vignette
            float v = vignette + Vig[filter] * strength;
            if (v > 0.02f)
            {
                if (_vig == null) _vig = TextureGen.Vignette(128, Color.black);
                var p = GUI.color; GUI.color = new Color(1, 1, 1, Mathf.Clamp01(v)); GUI.DrawTexture(frame, _vig); GUI.color = p;
            }

            // rule-of-thirds grid
            if (thirds)
            {
                var line = new Color(1, 1, 1, 0.22f);
                for (int i = 1; i <= 2; i++)
                {
                    float x = frame.x + frame.width * i / 3f;
                    float y = frame.y + frame.height * i / 3f;
                    Styles.Fill(new Rect(x, frame.y, 1, frame.height), line);
                    Styles.Fill(new Rect(frame.x, y, frame.width, 1), line);
                }
            }
        }

        /// <summary>Largest rect of the given aspect centred on screen.</summary>
        public static Rect FrameRect(int aspect)
        {
            float sw = Screen.width, sh = Screen.height;
            if (aspect == 0) return new Rect(0, 0, sw, sh);
            float ar = aspect == 1 ? 16f / 9f : aspect == 2 ? 2.39f : aspect == 3 ? 4f / 3f : aspect == 4 ? 9f / 16f : 1f;
            float w = sw, h = sw / ar;
            if (h > sh) { h = sh; w = sh * ar; }
            return new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
        }
    }
}
