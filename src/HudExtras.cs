using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>Shared helpers for reading player info off a VRRig.</summary>
    internal static class CasterUtil
    {
        public static bool IsTagged(VRRig r)
            => r != null && r.lavaParticleSystem != null && r.lavaParticleSystem.isPlaying;

        public static string NameOf(VRRig r)
        {
            if (r == null) return "?";
            try
            {
                if (r.Creator != null && !string.IsNullOrEmpty(r.Creator.NickName))
                    return r.Creator.NickName;
            }
            catch { }
            if (!string.IsNullOrEmpty(r.playerNameVisible)) return r.playerNameVisible;
            return r.isOfflineVRRig ? "You" : "Player";
        }

        public static Vector3 HeadPos(VRRig r)
        {
            var t = r.headMesh != null ? r.headMesh.transform : r.transform;
            return t.position;
        }

        public static float Speed(VRRig r)
        {
            if (r == null) return 0f;
            try { return r.LatestVelocity().magnitude; }
            catch { return 0f; }
        }

        public static Vector3 Velocity(VRRig r)
        {
            if (r == null) return Vector3.zero;
            try { return r.LatestVelocity(); }
            catch { return Vector3.zero; }
        }
    }

    /// <summary>Premium floating nametags, overhead minimap, and the mod watermark.</summary>
    internal static class HudExtras
    {
        public static float NametagScale = 1f;

        public static bool Occlude = true;

        public static void DrawNametags(IList<VRRig> rigs, Camera cam, VRRig target, bool showVelocity)
        {
            if (cam == null) return;
            Vector3 camPos = cam.transform.position;
            for (int i = 0; i < rigs.Count; i++)
            {
                var r = rigs[i];
                if (r == null) continue;

                Vector3 headW = CasterUtil.HeadPos(r);
                // occlusion: skip if a wall is between the camera and the player's head
                if (Occlude && r != target)
                {
                    float dist = Vector3.Distance(camPos, headW);
                    if (Physics.Linecast(camPos, headW, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)
                        && hit.distance < dist - 0.5f) continue;
                }

                Vector3 world = headW + Vector3.up * 0.45f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0.2f) continue;

                float guiY = Screen.height - sp.y;
                float s = Mathf.Clamp(2.4f / sp.z, 0.6f, 1.35f) * NametagScale;
                bool it = CasterUtil.IsTagged(r);
                bool sel = r == target;

                string name = CasterUtil.NameOf(r);
                int fs = Mathf.Max(9, Mathf.RoundToInt(13 * s));
                var nameStyle = Styles.ScratchTag;        // reused, no per-frame alloc
                nameStyle.fontSize = fs; nameStyle.normal.textColor = Color.white;
                Vector2 nsz = nameStyle.CalcSize(new GUIContent(name));

                float pad = 9f * s, dot = 9f * s, gap = 6f * s;
                float h = nsz.y + 8f * s;
                float w = pad + dot + gap + nsz.x + pad;

                string spd = showVelocity ? $"{CasterUtil.Speed(r):0.0}" : null;
                var spdStyle = Styles.ScratchTagShadow;   // reused
                spdStyle.fontSize = Mathf.Max(8, fs - 2);
                spdStyle.normal.textColor = Styles.Accent;
                float spdW = 0f;
                if (spd != null) { spdW = spdStyle.CalcSize(new GUIContent(spd)).x + gap; w += spdW; }

                float chipW = 0f;
                if (it) { chipW = 22f * s + gap; w += chipW; }

                var rect = new Rect(sp.x - w / 2f, guiY - h - 6f * s, w, h);

                // shadow + rounded background
                var sh = new Rect(rect.x - 4, rect.y - 2, rect.width + 8, rect.height + 8);
                var pcol = GUI.color; GUI.color = new Color(1, 1, 1, 0.45f); GUI.DrawTexture(sh, Styles.Shadow); GUI.color = pcol;
                Styles.Round(rect, new Color(0.07f, 0.08f, 0.10f, 0.9f), 9f * s);
                if (sel)
                {
                    GUI.DrawTexture(rect, Styles.White, ScaleMode.StretchToFill, true, 0, Styles.Accent, 1.6f * s, 9f * s);
                    Styles.Round(new Rect(rect.x, rect.yMax - 2.5f * s, rect.width, 2.5f * s), Styles.Accent, 2f);
                }

                float x = rect.x + pad;
                // colour dot
                Styles.Round(new Rect(x, rect.y + (h - dot) / 2f, dot, dot), r.playerColor, dot / 2f);
                x += dot + gap;
                // name
                GUI.Label(new Rect(x, rect.y, nsz.x + 2, h), name, nameStyle);
                x += nsz.x + (spd != null ? gap : 0f);
                // speed
                if (spd != null) { GUI.Label(new Rect(x, rect.y, spdStyle.CalcSize(new GUIContent(spd)).x + 2, h), spd, spdStyle); x += spdStyle.CalcSize(new GUIContent(spd)).x + gap; }
                // IT chip
                if (it)
                {
                    var chip = new Rect(rect.xMax - 22f * s - pad, rect.y + (h - 14f * s) / 2f, 22f * s, 14f * s);
                    Styles.Round(chip, Styles.Accent2, 6f * s);
                    var chipStyle = Styles.ScratchLabel;
                    chipStyle.alignment = TextAnchor.MiddleCenter; chipStyle.fontSize = Mathf.Max(8, fs - 3); chipStyle.normal.textColor = Color.white;
                    GUI.Label(chip, "IT", chipStyle);
                }
            }
        }

        public static void DrawWatermark(string text, float opacity)
        {
            if (opacity <= 0.02f || string.IsNullOrEmpty(text)) return;
            var style = new GUIStyle(Styles.Water) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            Vector2 sz = style.CalcSize(new GUIContent(text));
            float w = sz.x + 34, h = 26;
            var r = new Rect(Screen.width - w - 12, Screen.height - h - 10, w, h);

            Styles.Round(r, new Color(0.05f, 0.06f, 0.08f, 0.45f * opacity), 9f);
            Styles.Round(new Rect(r.x + 10, r.y + h / 2f - 4, 8, 8), new Color(Styles.Accent.r, Styles.Accent.g, Styles.Accent.b, opacity), 4f);
            style.normal.textColor = new Color(1, 1, 1, opacity);
            GUI.Label(new Rect(r.x + 24, r.y, sz.x + 6, h), text, style);
        }

        public static void DrawMinimap(Rect area, IList<VRRig> rigs, VRRig target)
        {
            Styles.DrawCard(area, new Color(0.05f, 0.06f, 0.08f, 0.9f), 6f);
            Styles.Round(new Rect(area.x, area.y, area.width, 20f), new Color(0, 0, 0, 0.5f), 9f);
            GUI.Label(new Rect(area.x + 10, area.y + 2, area.width, 16f), "MAP", Styles.Header);
            if (rigs.Count == 0) return;

            Vector3 c = Vector3.zero; int n = 0;
            foreach (var r in rigs) { if (r != null) { c += CasterUtil.HeadPos(r); n++; } }
            if (n == 0) return; c /= n;

            float extent = 6f;
            foreach (var r in rigs)
            {
                if (r == null) continue;
                Vector3 p = CasterUtil.HeadPos(r);
                extent = Mathf.Max(extent, Mathf.Abs(p.x - c.x));
                extent = Mathf.Max(extent, Mathf.Abs(p.z - c.z));
            }
            extent *= 1.2f;
            var inner = new Rect(area.x + 12, area.y + 24f, area.width - 24f, area.height - 32f);

            foreach (var r in rigs)
            {
                if (r == null) continue;
                Vector3 p = CasterUtil.HeadPos(r);
                float px = inner.center.x + (p.x - c.x) / extent * inner.width * 0.5f;
                float py = inner.center.y - (p.z - c.z) / extent * inner.height * 0.5f;
                bool it = CasterUtil.IsTagged(r); bool sel = r == target;
                float d = sel ? 10f : 7f;
                Color col = it ? Styles.Accent2 : (sel ? Color.green : r.playerColor);
                Styles.Round(new Rect(px - d / 2f, py - d / 2f, d, d), col, d / 2f);
            }
        }
    }
}
