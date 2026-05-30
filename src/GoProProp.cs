using System;
using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// A smooth, grabbable in-VR camera TABLET (Sakuraa-style). A big live viewfinder on the
    /// left, a touch control panel on the right. Operate it with your free hand — either poke a
    /// button or hover it and squeeze the trigger. Grab with grip, drop to place (sticky).
    /// Tablet/Phone camera mode broadcasts from its lens.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;

        public Action OnRecord, OnPlay, OnCycleMode, OnToggleView, OnFovUp, OnFovDown, OnTime, OnHide;
        public Func<string> StatusText;
        public Func<bool> IsRecording;

        private Camera _preview;
        private RenderTexture _rt;
        private Material _screenMat, _recMat;
        private TextMesh _status;
        private readonly List<PButton> _buttons = new List<PButton>();
        private int _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private const float GrabRadius = 0.26f;

        private static readonly Color Body = new Color(0.085f, 0.09f, 0.105f);
        private static Font _font;

        private class PButton
        {
            public Vector2 c, half;
            public Action act;
            public Color col;
            public Renderer rend;
            public float flash;
        }

        // ============================================================ model

        public void EnsureSpawned()
        {
            if (Root != null) return;
            EnsureFont();
            Root = new GameObject("SpooderTablet") { hideFlags = HideFlags.DontSave };

            float w = 0.212f, h = 0.146f, t = 0.0075f;
            float fz = -t * 0.5f - 0.0016f;

            Box(Root.transform, Vector3.zero, new Vector3(w, h, t), Body);                                  // body
            Box(Root.transform, new Vector3(0, 0, -t * 0.5f - 0.0004f), new Vector3(w - 0.008f, h - 0.008f, 0.0012f), Color.black); // bezel
            Box(Root.transform, new Vector3(0, -h * 0.5f + 0.006f, fz - 0.0006f), new Vector3(0.05f, 0.0035f, 0.001f), new Color(0.3f, 0.34f, 0.4f)); // home bar

            // rear lens
            Box(Root.transform, new Vector3(w * 0.5f - 0.022f, h * 0.5f - 0.016f, 0.005f), new Vector3(0.02f, 0.02f, 0.009f), new Color(0.04f, 0.04f, 0.05f));
            var glass = Cyl(Root.transform, new Vector3(w * 0.5f - 0.022f, h * 0.5f - 0.016f, 0.011f), 0.007f, 0.002f, new Color(0.1f, 0.4f, 0.6f), true);
            glass.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var rec = Box(Root.transform, new Vector3(-w * 0.5f + 0.014f, h * 0.5f - 0.012f, 0.005f), new Vector3(0.008f, 0.004f, 0.004f), new Color(1f, 0.15f, 0.15f), true);
            _recMat = rec.GetComponent<Renderer>() != null ? rec.GetComponent<Renderer>().material : null;

            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(w * 0.5f - 0.022f, h * 0.5f - 0.016f, 0.02f);
            Lens = lensGo.transform;

            // big viewfinder (left)
            var vf = Quad(Root.transform, new Vector3(-0.052f, 0.004f, fz), new Vector3(0.104f, 0.122f, 1f));
            BuildViewfinder(vf);

            // right-side control panel
            float bx = 0.064f;
            _status = Label(Root.transform, new Vector3(bx, 0.062f, fz - 0.0006f), "READY", 0.0013f, new Color(0.78f, 0.88f, 1f));
            AddButton(bx, 0.044f, 0.036f, 0.013f, "● REC", new Color(0.82f, 0.21f, 0.27f), () => OnRecord?.Invoke());
            AddButton(bx, 0.014f, 0.036f, 0.013f, "MODE", Panel(), () => OnCycleMode?.Invoke());
            AddButton(0.042f, -0.016f, 0.016f, 0.013f, "FOV-", Panel(), () => OnFovDown?.Invoke());
            AddButton(0.086f, -0.016f, 0.016f, 0.013f, "FOV+", Panel(), () => OnFovUp?.Invoke());
            AddButton(bx, -0.046f, 0.036f, 0.013f, "VIEW", Panel(), () => OnToggleView?.Invoke());
            AddButton(0.042f, -0.064f, 0.016f, 0.011f, "TIME", Panel(), () => OnTime?.Invoke());
            AddButton(0.086f, -0.064f, 0.016f, 0.011f, "HUD", Panel(), () => OnHide?.Invoke());
            Held = false;
        }

        private static Color Panel() => new Color(0.17f, 0.19f, 0.235f);

        private void AddButton(float x, float y, float hx, float hy, string label, Color col, Action act)
        {
            float fz = -0.0075f * 0.5f - 0.0016f;
            var q = Quad(Root.transform, new Vector3(x, y, fz - 0.0003f), new Vector3(hx * 2f, hy * 2f, 1f), col, true);
            Label(Root.transform, new Vector3(x, y, fz - 0.0009f), label, 0.0012f, Color.white);
            _buttons.Add(new PButton { c = new Vector2(x, y), half = new Vector2(hx, hy), act = act, col = col, rend = q.GetComponent<Renderer>() });
        }

        private void BuildViewfinder(GameObject screen)
        {
            try
            {
                _rt = new RenderTexture(480, 270, 16) { name = "SpooderTabletRT", hideFlags = HideFlags.DontSave };
                _rt.Create();
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Lens, false);
                camGo.transform.localPosition = new Vector3(0, 0, 0.006f);
                _preview = camGo.AddComponent<Camera>();
                _preview.targetTexture = _rt;
                _preview.fieldOfView = 90f; _preview.nearClipPlane = 0.02f; _preview.farClipPlane = 700f;
                _preview.depth = -10; _preview.clearFlags = CameraClearFlags.Skybox;
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
                _screenMat = new Material(sh) { mainTexture = _rt };
                var sr = screen.GetComponent<Renderer>();
                if (sr != null) sr.material = _screenMat;
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] viewfinder: " + e.Message); }
        }

        // ============================================================ placement / grab / touch

        public void SummonToHand()
        {
            EnsureSpawned();
            var tagger = GorillaTagger.Instance;
            Transform t = tagger != null ? tagger.leftHandTransform : null;
            if (t != null) { Root.transform.position = t.position + t.up * 0.05f; Root.transform.rotation = t.rotation; }
            else if (Camera.main != null) { Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.55f; Root.transform.rotation = Camera.main.transform.rotation; }
            Held = false;
        }

        public void Despawn()
        {
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null; Lens = null; Held = false; _preview = null; _buttons.Clear();
        }

        public void SetFov(float fov) { if (_preview != null) _preview.fieldOfView = Mathf.Clamp(fov, 10f, 120f); }

        public void Tick(float fov)
        {
            if (Root == null) return;
            SetFov(fov);
            if (_preview != null && _preview.enabled != Viewfinder) _preview.enabled = Viewfinder;

            var poller = ControllerInputPoller.instance;
            var tagger = GorillaTagger.Instance;
            int holding = -1;
            if (poller != null && tagger != null)
            {
                Transform rh = tagger.rightHandTransform, lh = tagger.leftHandTransform;
                if (!Held)
                {
                    if (poller.rightGrab && rh != null && Near(rh)) Attach(0, rh);
                    else if (poller.leftGrab && lh != null && Near(lh)) Attach(1, lh);
                }
                else
                {
                    Transform hand = _hand == 0 ? rh : lh;
                    bool grabbing = _hand == 0 ? poller.rightGrab : poller.leftGrab;
                    if (!grabbing || hand == null) Held = false;
                    else Root.transform.SetPositionAndRotation(hand.TransformPoint(_localPos), hand.rotation * _localRot);
                    holding = _hand;
                }
                if (rh != null && holding != 0) Touch(rh.position, false, poller.rightControllerTriggerButton);
                if (lh != null && holding != 1) Touch(lh.position, true, poller.leftControllerTriggerButton);
            }

            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (b.rend == null) continue;
                Color target = b.col;
                if (i == 0 && IsRecording != null && IsRecording()) target = new Color(1f, 0.27f, 0.32f);
                if (b.flash > 0f) { b.flash -= Time.deltaTime * 4f; target = Color.Lerp(target, Styles.Accent, Mathf.Clamp01(b.flash)); }
                Paint(b.rend, target, true);
            }
            if (_status != null && StatusText != null) _status.text = StatusText();

            if (_recMat != null)
            {
                bool on = IsRecording != null && IsRecording();
                float p = on ? (0.4f + 0.6f * Mathf.PingPong(Time.time * 2.4f, 1f)) : 0.18f;
                var c = new Color(1f, 0.15f, 0.15f) * p;
                _recMat.color = c;
                if (_recMat.HasProperty("_EmissionColor")) _recMat.SetColor("_EmissionColor", c);
            }
        }

        private void Touch(Vector3 worldHand, bool isLeft, bool trigger)
        {
            Vector3 lp = Root.transform.InverseTransformPoint(worldHand);
            if (lp.z > 0.02f || lp.z < -0.05f) return;                 // not near the screen
            for (int i = 0; i < _buttons.Count; i++)
            {
                var b = _buttons[i];
                if (Mathf.Abs(lp.x - b.c.x) <= b.half.x && Mathf.Abs(lp.y - b.c.y) <= b.half.y)
                {
                    bool poke = lp.z <= -0.002f && lp.z >= -0.04f;
                    bool click = trigger && lp.z <= 0.02f;
                    if (b.flash <= 0f && (poke || click))
                    {
                        b.flash = 1f;
                        try { b.act?.Invoke(); } catch (Exception e) { Debug.LogWarning("[GorillaCaster] button: " + e.Message); }
                        try { if (GorillaTagger.Instance != null) GorillaTagger.Instance.StartVibration(isLeft, 0.55f, 0.06f); } catch { }
                    }
                    return;
                }
            }
        }

        private bool Near(Transform hand) => Vector3.Distance(hand.position, Root.transform.position) < GrabRadius;
        private void Attach(int hand, Transform t)
        {
            Held = true; _hand = hand;
            _localPos = t.InverseTransformPoint(Root.transform.position);
            _localRot = Quaternion.Inverse(t.rotation) * Root.transform.rotation;
        }

        // ============================================================ primitives

        private static void EnsureFont()
        {
            if (_font != null) return;
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            if (_font == null) { try { _font = Font.CreateDynamicFontFromOSFont("Arial", 48); } catch { } }
        }

        private static TextMesh Label(Transform parent, Vector3 pos, string text, float size, Color col)
        {
            var go = new GameObject("lbl");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.fontSize = 48; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = col;
            if (_font != null) { tm.font = _font; var mr = go.GetComponent<MeshRenderer>(); if (mr != null) mr.sharedMaterial = _font.material; }
            return tm;
        }

        private static GameObject Quad(Transform parent, Vector3 pos, Vector3 size, Color col = default, bool tinted = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            if (tinted)
            {
                var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
                go.GetComponent<Renderer>().material = new Material(sh) { color = col };
            }
            return go;
        }

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            Paint(go.GetComponent<Renderer>(), col, emissive); return go;
        }

        private static GameObject Cyl(Transform parent, Vector3 pos, float radius, float halfLen, Color col, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(radius * 2f, halfLen, radius * 2f);
            Paint(go.GetComponent<Renderer>(), col, emissive); return go;
        }

        private static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); }

        private static Shader _shader;
        private static void Paint(Renderer r, Color col, bool emissive)
        {
            if (r == null) return;
            if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.Contains("Sprites"))
            { r.material.color = col; return; }
            if (_shader == null)
                _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                          ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var m = r.material; if (m.shader != _shader) m = new Material(_shader);
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (emissive) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col); }
            r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
