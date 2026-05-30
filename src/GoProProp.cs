using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GorillaCaster
{
    /// <summary>
    /// A clean, grabbable in-VR camera TABLET built with world-space uGUI (crisp text + rounded
    /// buttons) — a big live viewfinder on the left, a labelled control grid on the right.
    /// Operate it with your free hand (poke a button, or hover + squeeze trigger). Grab with grip,
    /// drop to place (sticky). Tablet camera mode broadcasts from its rear lens.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;

        public Action OnRecord, OnPlay, OnCycleMode, OnToggleView, OnFovUp, OnFovDown, OnTime, OnHide, OnDirector, OnShot;
        public Func<string> StatusText;
        public Func<bool> IsRecording;

        private Camera _preview;
        private RenderTexture _rt;
        private RawImage _vf;
        private Text _status;
        private RectTransform _canvas;
        private Material _recMat;
        private readonly List<TBtn> _btns = new List<TBtn>();
        private int _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private const float GrabRadius = 0.28f;

        // canvas dimensions (px) and metres-per-px scale
        private const float CW = 600f, CH = 380f, S = 0.00041f;

        private class TBtn
        {
            public Vector2 c, half;
            public Action act;
            public Color col;
            public Image img;
            public float flash;
            public int recIndex; // 0 = REC button (pulses while recording)
        }

        // ============================================================ model + canvas

        public void EnsureSpawned()
        {
            if (Root != null) return;
            Root = new GameObject("SpooderTablet") { hideFlags = HideFlags.DontSave };

            float w = CW * S, h = CH * S, t = 0.008f;
            // body slab
            Box(Root.transform, new Vector3(0, 0, 0.002f), new Vector3(w + 0.008f, h + 0.008f, t), new Color(0.07f, 0.075f, 0.09f));

            // rear lens (-z) + glow
            Box(Root.transform, new Vector3(w * 0.4f, h * 0.4f, -0.006f), new Vector3(0.022f, 0.022f, 0.01f), new Color(0.03f, 0.03f, 0.04f));
            var glass = Cyl(Root.transform, new Vector3(w * 0.4f, h * 0.4f, -0.012f), 0.008f, 0.002f, new Color(0.1f, 0.4f, 0.6f), true);
            glass.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var rec = Box(Root.transform, new Vector3(-w * 0.42f, h * 0.42f, -0.006f), new Vector3(0.008f, 0.004f, 0.004f), new Color(1f, 0.15f, 0.15f), true);
            _recMat = rec.GetComponent<Renderer>() != null ? rec.GetComponent<Renderer>().material : null;

            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(w * 0.4f, h * 0.4f, -0.03f);
            lensGo.transform.localRotation = Quaternion.Euler(0, 180, 0); // forward = -z (films away from holder)
            Lens = lensGo.transform;

            BuildCanvas();
            BuildViewfinder();
            Held = false;
        }

        private void BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas));
            go.transform.SetParent(Root.transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            _canvas = (RectTransform)go.transform;
            _canvas.sizeDelta = new Vector2(CW, CH);
            _canvas.localPosition = new Vector3(0, 0, -0.0046f); // front (+? screen faces -z toward holder)
            _canvas.localScale = Vector3.one * S;
            _canvas.localRotation = Quaternion.Euler(0, 180, 0); // face the holder

            // background
            var bg = MkImage(_canvas, "bg", Round(), new Color(0.10f, 0.11f, 0.14f, 1f));
            SetRect(bg.rectTransform, 0, 0, CW, CH);
            var accent = MkImage(_canvas, "accent", Round(), Styles.Accent);
            SetRect(accent.rectTransform, 0, CH / 2f - 5, CW - 24, 4);

            // status
            _status = MkText(_canvas, "status", "READY", 24, new Color(0.8f, 0.9f, 1f));
            SetRect(_status.rectTransform, 0, CH / 2f - 28, CW - 30, 30);

            // viewfinder (left)
            var vfBg = MkImage(_canvas, "vfbg", Round(), Color.black);
            SetRect(vfBg.rectTransform, -CW * 0.205f, -8, CW * 0.52f, CH * 0.78f);
            _vf = MkRaw(_canvas, "vf");
            SetRect(_vf.rectTransform, -CW * 0.205f, -8, CW * 0.52f - 10, CH * 0.78f - 10);

            // control grid (right): 2 cols x 5 rows
            float colL = CW * 0.16f, colR = CW * 0.36f, bw = CW * 0.18f, bh = CH * 0.13f;
            float[] rows = { CH * 0.30f, CH * 0.15f, 0f, -CH * 0.15f, -CH * 0.30f };
            AddBtn(colL, rows[0], bw, bh, "REC", new Color(0.85f, 0.22f, 0.28f), () => OnRecord?.Invoke(), 0);
            AddBtn(colR, rows[0], bw, bh, "MODE", Panel(), () => OnCycleMode?.Invoke());
            AddBtn(colL, rows[1], bw, bh, "PLAY", new Color(0.20f, 0.45f, 0.32f), () => OnPlay?.Invoke());
            AddBtn(colR, rows[1], bw, bh, "DIR", new Color(0.32f, 0.27f, 0.55f), () => OnDirector?.Invoke());
            AddBtn(colL, rows[2], bw, bh, "FOV-", Panel(), () => OnFovDown?.Invoke());
            AddBtn(colR, rows[2], bw, bh, "FOV+", Panel(), () => OnFovUp?.Invoke());
            AddBtn(colL, rows[3], bw, bh, "VIEW", Panel(), () => OnToggleView?.Invoke());
            AddBtn(colR, rows[3], bw, bh, "TIME", Panel(), () => OnTime?.Invoke());
            AddBtn(colL, rows[4], bw, bh, "HUD", Panel(), () => OnHide?.Invoke());
            AddBtn(colR, rows[4], bw, bh, "SHOT", Panel(), () => OnShot?.Invoke());
        }

        private void AddBtn(float x, float y, float w, float h, string label, Color col, Action act, int recIndex = -1)
        {
            var img = MkImage(_canvas, "btn_" + label, Round(), col);
            SetRect(img.rectTransform, x, y, w, h);
            var txt = MkText(img.rectTransform, "t", label, 26, Color.white);
            SetRect(txt.rectTransform, 0, 0, w, h);
            _btns.Add(new TBtn { c = new Vector2(x, y), half = new Vector2(w / 2f, h / 2f), act = act, col = col, img = img, recIndex = recIndex });
        }

        private void BuildViewfinder()
        {
            try
            {
                _rt = new RenderTexture(512, 320, 16) { name = "SpooderTabletRT", hideFlags = HideFlags.DontSave };
                _rt.Create();
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Lens, false);
                camGo.transform.localPosition = new Vector3(0, 0, 0.006f);
                _preview = camGo.AddComponent<Camera>();
                _preview.targetTexture = _rt;
                _preview.fieldOfView = 90f; _preview.nearClipPlane = 0.02f; _preview.farClipPlane = 700f;
                _preview.depth = -10; _preview.clearFlags = CameraClearFlags.Skybox;
                if (_vf != null) _vf.texture = _rt;
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] viewfinder: " + e.Message); }
        }

        // ============================================================ placement / grab / touch

        public void SummonToHand()
        {
            EnsureSpawned();
            var tagger = GorillaTagger.Instance;
            Transform t = tagger != null ? tagger.leftHandTransform : null;
            if (t != null) { Root.transform.position = t.position + t.up * 0.06f; Root.transform.rotation = t.rotation; }
            else if (Camera.main != null) { Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.6f; Root.transform.rotation = Camera.main.transform.rotation; }
            Held = false;
        }

        public void Despawn()
        {
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null; Lens = null; Held = false; _preview = null; _btns.Clear(); _canvas = null;
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

            bool recOn = IsRecording != null && IsRecording();
            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i];
                if (b.img == null) continue;
                Color target = b.col;
                if (b.recIndex == 0 && recOn) target = new Color(1f, 0.3f, 0.35f);
                if (b.flash > 0f) { b.flash -= Time.deltaTime * 4f; target = Color.Lerp(target, Styles.Accent, Mathf.Clamp01(b.flash)); }
                b.img.color = target;
            }
            if (_status != null && StatusText != null) _status.text = StatusText();

            if (_recMat != null)
            {
                float p = recOn ? (0.4f + 0.6f * Mathf.PingPong(Time.time * 2.4f, 1f)) : 0.18f;
                var c = new Color(1f, 0.15f, 0.15f) * p;
                _recMat.color = c;
                if (_recMat.HasProperty("_EmissionColor")) _recMat.SetColor("_EmissionColor", c);
            }
        }

        private void Touch(Vector3 worldHand, bool isLeft, bool trigger)
        {
            if (_canvas == null) return;
            Vector3 lp = _canvas.InverseTransformPoint(worldHand);   // canvas-local px; z = depth from screen
            if (lp.z < -70f || lp.z > 70f) return;                   // not near the screen
            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i];
                if (Mathf.Abs(lp.x - b.c.x) <= b.half.x && Mathf.Abs(lp.y - b.c.y) <= b.half.y)
                {
                    bool poke = lp.z >= -10f && lp.z <= 45f;
                    bool click = trigger && lp.z <= 60f;
                    if (b.flash <= 0f && (poke || click))
                    {
                        b.flash = 1f;
                        try { b.act?.Invoke(); } catch (Exception e) { Debug.LogWarning("[GorillaCaster] btn: " + e.Message); }
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

        // ============================================================ uGUI builders

        private static Color Panel() => new Color(0.18f, 0.20f, 0.25f);

        private static Sprite _round;
        private static Sprite Round()
        {
            if (_round == null)
            {
                var t = TextureGen.RoundedRect(36, 14, Color.white);
                _round = Sprite.Create(t, new Rect(0, 0, 36, 36), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(14, 14, 14, 14));
            }
            return _round;
        }

        private static Font _font;
        private static Font F()
        {
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
            }
            return _font;
        }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Image MkImage(Transform parent, string n, Sprite sprite, Color col)
        {
            var go = new GameObject(n, typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite; img.type = Image.Type.Sliced; img.color = col; img.raycastTarget = false;
            return img;
        }

        private static RawImage MkRaw(Transform parent, string n)
        {
            var go = new GameObject(n, typeof(RawImage));
            go.transform.SetParent(parent, false);
            var ri = go.GetComponent<RawImage>(); ri.raycastTarget = false;
            return ri;
        }

        private static Text MkText(Transform parent, string n, string text, int size, Color col)
        {
            var go = new GameObject(n, typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = F(); t.text = text; t.fontSize = size; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter; t.color = col;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        // ============================================================ primitives

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
            if (_shader == null)
                _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                          ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var m = new Material(_shader); m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (emissive) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col); }
            r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
