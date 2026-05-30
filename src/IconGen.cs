using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>Procedurally-drawn white icon sprites for the tablet buttons (no external assets).</summary>
    internal static class IconGen
    {
        private const int Sz = 64;
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        public static Sprite Get(string id)
        {
            if (_cache.TryGetValue(id, out var s)) return s;
            var t = new Texture2D(Sz, Sz, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var clear = new Color[Sz * Sz];
            t.SetPixels(clear);
            Draw(t, id);
            t.Apply();
            s = Sprite.Create(t, new Rect(0, 0, Sz, Sz), new Vector2(0.5f, 0.5f), 100f);
            _cache[id] = s;
            return s;
        }

        private static void Draw(Texture2D t, string id)
        {
            switch (id)
            {
                case "fpv":   // first person: eye
                    Ring(t, 32, 32, 17, 13); Disc(t, 32, 32, 6); break;
                case "selfie": // person
                    Disc(t, 32, 22, 9); Bar(t, 32, 52, 32, 38, 13); break;
                case "mods":  // magnifier
                    Ring(t, 27, 37, 12, 8); Line(t, 36, 28, 50, 14, 4); break;
                case "fov+": Line(t, 32, 14, 32, 50, 5); Line(t, 14, 32, 50, 32, 5); break;
                case "fov-": Line(t, 14, 32, 50, 32, 5); break;
                case "shot": // camera
                    Bar(t, 26, 17, 38, 17, 5); Rect(t, 12, 22, 52, 48, 4); Ring(t, 32, 35, 11, 7); break;
                case "smth+": Wave(t); Line(t, 50, 14, 50, 30, 4); Line(t, 42, 22, 58, 22, 4); break;
                case "smth-": Wave(t); Line(t, 42, 22, 58, 22, 4); break;
                case "hide": // eye with slash
                    Ring(t, 32, 32, 15, 11); Disc(t, 32, 32, 5); Line(t, 16, 48, 48, 16, 4); break;
                case "flip": // two opposing arrows
                    Tri(t, 14, 32, 28, 23, 28, 41); Tri(t, 50, 32, 36, 23, 36, 41); Bar(t, 28, 32, 36, 32, 3); break;
                case "back": Tri(t, 18, 32, 34, 22, 34, 42); Bar(t, 30, 32, 48, 32, 4); break;
                case "refresh": Ring(t, 32, 32, 14, 10); Tri(t, 44, 14, 44, 30, 56, 22); break;
                default: Disc(t, 32, 32, 10); break;
            }
        }

        // ---- primitives (white, anti-aliased, union by max alpha) ----
        private static void Px(Texture2D t, int x, int y, float a)
        {
            if (x < 0 || y < 0 || x >= Sz || y >= Sz || a <= 0f) return;
            var c = t.GetPixel(x, y);
            if (a > c.a) t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a)));
        }

        private static void Disc(Texture2D t, float cx, float cy, float r)
        {
            int x0 = Mathf.FloorToInt(cx - r - 1), x1 = Mathf.CeilToInt(cx + r + 1), y0 = Mathf.FloorToInt(cy - r - 1), y1 = Mathf.CeilToInt(cy + r + 1);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); Px(t, x, y, Mathf.Clamp01(r - d + 0.5f)); }
        }
        private static void Ring(Texture2D t, float cx, float cy, float ro, float ri)
        {
            int x0 = Mathf.FloorToInt(cx - ro - 1), x1 = Mathf.CeilToInt(cx + ro + 1), y0 = Mathf.FloorToInt(cy - ro - 1), y1 = Mathf.CeilToInt(cy + ro + 1);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) { float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); float a = Mathf.Min(Mathf.Clamp01(ro - d + 0.5f), Mathf.Clamp01(d - ri + 0.5f)); Px(t, x, y, a); }
        }
        private static void Line(Texture2D t, float ax, float ay, float bx, float by, float w) => Bar(t, ax, ay, bx, by, w);
        private static void Bar(Texture2D t, float ax, float ay, float bx, float by, float half)
        {
            for (int y = 0; y < Sz; y++) for (int x = 0; x < Sz; x++)
            {
                float d = SegDist(x, y, ax, ay, bx, by);
                Px(t, x, y, Mathf.Clamp01(half - d + 0.5f));
            }
        }
        private static void Rect(Texture2D t, float x0, float y0, float x1, float y1, float w)
        {
            Bar(t, x0, y0, x1, y0, w); Bar(t, x0, y1, x1, y1, w); Bar(t, x0, y0, x0, y1, w); Bar(t, x1, y0, x1, y1, w);
        }
        private static void Tri(Texture2D t, float ax, float ay, float bx, float by, float cx, float cy)
        {
            int x0 = (int)Mathf.Min(ax, Mathf.Min(bx, cx)) - 1, x1 = (int)Mathf.Max(ax, Mathf.Max(bx, cx)) + 1;
            int y0 = (int)Mathf.Min(ay, Mathf.Min(by, cy)) - 1, y1 = (int)Mathf.Max(ay, Mathf.Max(by, cy)) + 1;
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) if (InTri(x + 0.5f, y + 0.5f, ax, ay, bx, by, cx, cy)) Px(t, x, y, 1f);
        }
        private static void Wave(Texture2D t)
        {
            for (int x = 10; x <= 38; x++) { float y = 32 + 8f * Mathf.Sin((x - 10) / 28f * Mathf.PI * 2f); Disc(t, x, y, 2.2f); }
        }

        private static float SegDist(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay; float l2 = dx * dx + dy * dy;
            float tt = l2 <= 0.0001f ? 0f : Mathf.Clamp01(((px - ax) * dx + (py - ay) * dy) / l2);
            float qx = ax + tt * dx, qy = ay + tt * dy;
            return Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
        }
        private static bool InTri(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = Sign(px, py, ax, ay, bx, by), d2 = Sign(px, py, bx, by, cx, cy), d3 = Sign(px, py, cx, cy, ax, ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        private static float Sign(float px, float py, float ax, float ay, float bx, float by) => (px - bx) * (ay - by) - (ax - bx) * (py - by);
    }
}
