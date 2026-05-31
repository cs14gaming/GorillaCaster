using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Runtime texture generation for a premium look: anti-aliased rounded rectangles
    /// (9-sliceable), circles, and soft drop shadows. No external assets needed.
    /// </summary>
    internal static class TextureGen
    {
        /// <summary>Solid 1x1.</summary>
        public static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>Anti-aliased rounded rectangle. Use border = radius for 9-slice scaling.</summary>
        public static Texture2D RoundedRect(int size, int radius, Color col)
        {
            size = Mathf.Max(size, radius * 2 + 2);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float a = CornerAlpha(x + 0.5f, y + 0.5f, size, radius);
                t.SetPixel(x, y, new Color(col.r, col.g, col.b, col.a * a));
            }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>Rounded rect outline ring (transparent centre).</summary>
        public static Texture2D RoundedOutline(int size, int radius, int thickness, Color col)
        {
            size = Mathf.Max(size, radius * 2 + 2);
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float outer = CornerAlpha(x + 0.5f, y + 0.5f, size, radius);
                float inner = CornerAlpha(x + 0.5f, y + 0.5f, size, radius, thickness);
                float a = Mathf.Clamp01(outer - inner);
                t.SetPixel(x, y, new Color(col.r, col.g, col.b, col.a * a));
            }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            return t;
        }

        public static Texture2D Circle(int size, Color col) => RoundedRect(size, size / 2, col);

        /// <summary>A stylised banana crescent (transparent background), tilted ~40°.
        /// Built as the area inside one disc and outside an offset disc.</summary>
        public static Texture2D Banana(int size, Color light, Color dark)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) / 2f;
            float ca = Mathf.Cos(-40f * Mathf.Deg2Rad), sa = Mathf.Sin(-40f * Mathf.Deg2Rad);
            // discs in normalised, rotated space
            Vector2 bigC = new Vector2(-0.05f, -0.12f); float bigR = 0.92f;
            Vector2 cutC = new Vector2(0.02f, 0.46f); float cutR = 0.90f;
            float edge = 2.2f / size;   // ~1px feather
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x - c) / c, ny = (y - c) / c;
                float rx = nx * ca - ny * sa, ry = nx * sa + ny * ca;
                float d1 = Vector2.Distance(new Vector2(rx, ry), bigC);
                float d2 = Vector2.Distance(new Vector2(rx, ry), cutC);
                float aOuter = Mathf.Clamp01((bigR - d1) / edge);
                float aInner = Mathf.Clamp01((d2 - cutR) / edge);
                float a = Mathf.Min(aOuter, aInner);
                Color col = Color.Lerp(dark, light, Mathf.Clamp01((ry + 0.6f) / 1.2f)); // darker lower edge
                t.SetPixel(x, y, new Color(col.r, col.g, col.b, a));
            }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>Vertical gradient (bottom -> top).</summary>
        public static Texture2D Gradient(int h, Color bottom, Color top)
        {
            var t = new Texture2D(1, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / (float)(h - 1)));
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>Vignette: transparent centre fading to opaque dark at the edges/corners.</summary>
        public static Texture2D Vignette(int size, Color edge)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x - c) / c, ny = (y - c) / c;
                float d = Mathf.Sqrt(nx * nx + ny * ny) / 1.41421f; // 0 centre .. 1 corner
                float a = Mathf.Clamp01((d - 0.45f) / 0.55f);
                a = a * a;
                t.SetPixel(x, y, new Color(edge.r, edge.g, edge.b, edge.a * a));
            }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            return t;
        }

        /// <summary>Soft radial shadow blob (opaque centre fading to transparent).</summary>
        public static Texture2D SoftShadow(int size, Color col)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float c = size / 2f, r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c + 0.5f) * (x - c + 0.5f) + (y - c + 0.5f) * (y - c + 0.5f));
                float a = Mathf.Clamp01(1f - d / r);
                a = a * a; // ease
                t.SetPixel(x, y, new Color(col.r, col.g, col.b, col.a * a));
            }
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave; t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
            return t;
        }

        private static float CornerAlpha(float fx, float fy, int size, int radius, int inset = 0)
        {
            float r = radius - inset;
            float lo = radius, hi = size - radius;
            float cx = Mathf.Clamp(fx, lo, hi);
            float cy = Mathf.Clamp(fy, lo, hi);
            if (inset > 0)
            {
                // shrink the straight region too
                cx = Mathf.Clamp(fx, lo, hi);
                cy = Mathf.Clamp(fy, lo, hi);
            }
            float dx = fx - cx, dy = fy - cy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r - dist + 0.5f);
        }
    }
}
