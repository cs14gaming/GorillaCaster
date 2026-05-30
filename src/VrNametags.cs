using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// World-space floating names above every player, rendered as 3D TextMesh so they're visible
    /// in the VR headset (and the broadcast), billboarded to face the local camera.
    /// </summary>
    internal static class VrNametags
    {
        public static bool Enabled;
        public static bool ShowVelocity;
        public static float Size = 1f;

        private static readonly Dictionary<VRRig, GameObject> _tags = new Dictionary<VRRig, GameObject>();
        private static readonly List<VRRig> _stale = new List<VRRig>();
        private static Font _font;

        public static void Tick(IList<VRRig> rigs)
        {
            if (!Enabled) { Clear(); return; }
            EnsureFont();

            var camT = HeadCam();
            Vector3 camPos = camT != null ? camT.position : Vector3.zero;

            // update / create
            for (int i = 0; i < rigs.Count; i++)
            {
                var r = rigs[i];
                if (r == null || r.isOfflineVRRig) continue;   // skip yourself
                if (!_tags.TryGetValue(r, out var go) || go == null)
                {
                    go = Make();
                    _tags[r] = go;
                }
                var tm = go.GetComponent<TextMesh>();
                Vector3 head = CasterUtil.HeadPos(r) + Vector3.up * 0.5f;
                go.transform.position = head;
                if (camPos != Vector3.zero) go.transform.rotation = Quaternion.LookRotation(head - camPos, Vector3.up);
                float sc = Mathf.Clamp(Vector3.Distance(head, camPos) * 0.0016f, 0.004f, 0.05f) * Size;
                go.transform.localScale = Vector3.one * sc;

                bool it = CasterUtil.IsTagged(r);
                tm.text = CasterUtil.NameOf(r) + (ShowVelocity ? $"  {CasterUtil.Speed(r):0.0}" : "") + (it ? "  [IT]" : "");
                tm.color = it ? new Color(1f, 0.35f, 0.4f) : Color.white;
            }

            // cleanup
            _stale.Clear();
            foreach (var kv in _tags) { bool present = false; for (int i = 0; i < rigs.Count; i++) if (rigs[i] == kv.Key) { present = true; break; } if (!present || kv.Key == null) _stale.Add(kv.Key); }
            foreach (var k in _stale) { if (_tags.TryGetValue(k, out var g) && g != null) Object.Destroy(g); _tags.Remove(k); }
        }

        public static void Clear()
        {
            foreach (var kv in _tags) if (kv.Value != null) Object.Destroy(kv.Value);
            _tags.Clear();
        }

        private static GameObject Make()
        {
            var go = new GameObject("VrNametag") { hideFlags = HideFlags.DontSave };
            var tm = go.AddComponent<TextMesh>();
            tm.fontSize = 64; tm.characterSize = 0.1f; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center;
            tm.color = Color.white; tm.fontStyle = FontStyle.Bold;
            if (_font != null) { tm.font = _font; var mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.sharedMaterial = _font.material; }
            return go;
        }

        private static void EnsureFont()
        {
            if (_font != null) return;
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        }

        private static Transform HeadCam()
        {
            var t = GorillaTagger.Instance;
            if (t != null && t.mainCamera != null) return t.mainCamera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }
    }
}
