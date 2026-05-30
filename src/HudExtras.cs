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
    }

    /// <summary>World-space floating nametags + an overhead minimap, drawn in OnGUI.</summary>
    internal static class HudExtras
    {
        public static void DrawNametags(IList<VRRig> rigs, Camera cam, VRRig target)
        {
            if (cam == null) return;
            for (int i = 0; i < rigs.Count; i++)
            {
                var r = rigs[i];
                if (r == null) continue;

                Vector3 world = CasterUtil.HeadPos(r) + Vector3.up * 0.42f;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0f) continue; // behind camera

                float guiY = Screen.height - sp.y;
                bool it = CasterUtil.IsTagged(r);
                bool sel = r == target;

                string label = CasterUtil.NameOf(r);
                if (it) label += "  <color=#ff5555>[IT]</color>";

                var size = Styles.Tag.CalcSize(new GUIContent(label));
                float w = Mathf.Max(size.x + 14f, 40f);
                var rect = new Rect(sp.x - w / 2f, guiY - 24f, w, 22f);

                // backing chip in the player's color (dim) for the selected target, else dark
                Color chip = sel ? new Color(r.playerColor.r, r.playerColor.g, r.playerColor.b, 0.55f)
                                  : new Color(0f, 0f, 0f, 0.5f);
                Styles.Fill(rect, chip);
                if (sel) Styles.Fill(new Rect(rect.x, rect.yMax, rect.width, 2f), Styles.BrandAccent);

                // shadow + text
                var sh = rect; sh.x += 1; sh.y += 1;
                GUI.Label(sh, label, Styles.TagShadow);
                GUI.Label(rect, label, Styles.Tag);
            }
        }

        public static void DrawMinimap(Rect area, IList<VRRig> rigs, VRRig target)
        {
            Styles.Fill(area, new Color(0.05f, 0.06f, 0.08f, 0.85f));
            Styles.Fill(new Rect(area.x, area.y, area.width, 18f), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(area.x + 6, area.y + 1, area.width, 16f), "MAP", Styles.Hud);

            if (rigs.Count == 0) return;

            // centroid + extent on the XZ plane
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var r in rigs) { if (r != null) { c += CasterUtil.HeadPos(r); n++; } }
            if (n == 0) return;
            c /= n;

            float extent = 6f;
            foreach (var r in rigs)
            {
                if (r == null) continue;
                Vector3 p = CasterUtil.HeadPos(r);
                extent = Mathf.Max(extent, Mathf.Abs(p.x - c.x));
                extent = Mathf.Max(extent, Mathf.Abs(p.z - c.z));
            }
            extent *= 1.2f;

            var pad = 10f;
            var inner = new Rect(area.x + pad, area.y + 22f, area.width - pad * 2f, area.height - 30f);

            foreach (var r in rigs)
            {
                if (r == null) continue;
                Vector3 p = CasterUtil.HeadPos(r);
                float nx = (p.x - c.x) / extent;          // -1..1
                float nz = (p.z - c.z) / extent;
                float px = inner.center.x + nx * inner.width * 0.5f;
                float py = inner.center.y - nz * inner.height * 0.5f; // north = up

                bool it = CasterUtil.IsTagged(r);
                bool sel = r == target;
                float s = sel ? 9f : 6f;
                Color col = it ? new Color(1f, 0.3f, 0.3f) : (sel ? Color.green : r.playerColor);
                Styles.Fill(new Rect(px - s / 2f, py - s / 2f, s, s), col);
            }
        }
    }
}
