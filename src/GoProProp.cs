using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GorillaCaster
{
    /// <summary>
    /// A simple grabbable in-VR camera tablet. World-space uGUI with a live viewfinder and a small
    /// set of mode buttons (1st/3rd person, freecam, flip, next/prev, shot). Grab it with grip; aim
    /// your RIGHT-hand fingertip laser at a button and pull the trigger. Screen billboards to you.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;

        public Action<string> OnCommand;
        public Func<string> StatusText;

        private Camera _preview;
        private RenderTexture _rt;
        private RawImage _vf;
        private Text _status;
        private Image _accentBar, _cursor;
        private RectTransform _canvas;
        private CanvasGroup _rootGroup;
        private LineRenderer _laser;
        private AudioSource _audio;
        private AudioClip _click, _hoverClip;
        private float _popT;
        private bool _flip;
        private readonly List<Btn> _btns = new List<Btn>();
        private int _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private Vector2 _smoothLp;
        private Vector3 _aimOrigin, _aimDir;
        private bool _trigPrev;
        private int _hoverIdx = -1, _lastHoverSound = -1, _hoverPrev = -1;
        private const float GrabRadius = 0.42f;
        private const int UiLayer = 5;     // built-in UI layer (excluded from the preview + casting cameras)
        public int Layer => UiLayer;
        private const float CW = 780f, CH = 470f, S = 0.00052f;

        private class Btn
        {
            public RectTransform rt; public Image img;
            public Vector2 c, half; public Action act; public Color col; public float scale = 1f, flash, hover;
        }

        // ============================================================ spawn

        public void EnsureSpawned()
        {
            if (Root != null) return;
            Root = new GameObject("SpooderTablet") { hideFlags = HideFlags.DontSave };

            float w = CW * S, h = CH * S, t = 0.01f;
            Box(Root.transform, Vector3.zero, new Vector3(w + 0.012f, h + 0.012f, t), new Color(0.06f, 0.065f, 0.08f));

            // lens out the BACK (-z), well clear of the body so it never films the tablet
            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(0, 0, -0.05f);
            lensGo.transform.localRotation = Quaternion.Euler(0, 180, 0);   // faces away from holder
            Lens = lensGo.transform;

            BuildAudio();
            BuildCanvas();
            BuildViewfinder();
            BuildLaser();

            SetLayer(Root, UiLayer);
            if (_preview != null) { SetLayer(_preview.gameObject, 0); _preview.cullingMask = ~(1 << UiLayer); }
            _popT = 0f; Held = false;
        }

        private void BuildCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(Root.transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            _rootGroup = go.GetComponent<CanvasGroup>();
            _canvas = (RectTransform)go.transform;
            _canvas.sizeDelta = new Vector2(CW, CH);
            _canvas.localPosition = new Vector3(0, 0, 0.0085f);
            _canvas.localScale = new Vector3(-S, S, S);   // negative X un-mirrors (faces head)

            MkImage(_canvas, "bg", Round(), new Color(0.10f, 0.11f, 0.14f, 1f), 0, 0, CW, CH);
            _accentBar = MkImage(_canvas, "accent", Round(), Styles.Accent, 0, CH / 2f - 8, CW - 30, 5);
            MkText(_canvas, "brand", "SPOODER", 28, new Color(0.5f, 0.85f, 1f), -CW / 2f + 110, CH / 2f - 36, 200, 34, TextAnchor.MiddleLeft);
            _status = MkText(_canvas, "status", "READY", 28, Color.white, CW / 2f - 150, CH / 2f - 36, 280, 34, TextAnchor.MiddleRight);

            MkImage(_canvas, "vfbg", Round(), Color.black, -CW * 0.235f, -10, CW * 0.45f + 10, CH * 0.74f + 10);
            _vf = MkRaw(_canvas, "vf", -CW * 0.235f, -10, CW * 0.45f, CH * 0.74f);

            float[] cx = { 120f, 288f };
            float[] ry = { 132f, 64f, -6f };
            AddBtn("1st P", cx[0], ry[0], "fp", Panel());
            AddBtn("3rd P", cx[1], ry[0], "third", Panel());
            AddBtn("FREE", cx[0], ry[1], "free", Panel());
            AddBtn("FLIP", cx[1], ry[1], null, new Color(0.32f, 0.27f, 0.55f));   // internal flip
            AddBtn("NEXT", cx[0], ry[2], "next", new Color(0.18f, 0.42f, 0.5f));
            AddBtn("PREV", cx[1], ry[2], "prev", new Color(0.18f, 0.42f, 0.5f));
            AddBtn("SHOT", 204f, -80f, "shot", new Color(0.2f, 0.42f, 0.5f), 312f);

            _cursor = MkImage(_canvas, "cursor", Round(), new Color(Styles.Accent.r, Styles.Accent.g, Styles.Accent.b, 0.9f), 0, 0, 26, 26);
            _cursor.transform.SetAsLastSibling();
            _cursor.gameObject.SetActive(false);
        }

        private void AddBtn(string label, float x, float y, string cmd, Color col, float width = 152f)
        {
            var img = MkImage(_canvas, "b_" + label, Round(), col, x, y, width, 58);
            MkText(img.rectTransform, "t", label, 28, Color.white, 0, 0, width, 58, TextAnchor.MiddleCenter);
            Action act = cmd != null ? (Action)(() => OnCommand?.Invoke(cmd)) : ToggleFlip;
            _btns.Add(new Btn { rt = img.rectTransform, img = img, c = new Vector2(x, y), half = new Vector2(width / 2f, 29f), act = act, col = col });
        }

        private void ToggleFlip()
        {
            _flip = !_flip;
            if (Lens != null) Lens.localRotation = _flip ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
        }

        private void BuildAudio()
        {
            _audio = Root.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 1f; _audio.volume = 0.55f; _audio.maxDistance = 8f;
            _click = Blip(950f, 0.05f, 45f); _hoverClip = Blip(1500f, 0.02f, 90f);
        }
        private static AudioClip Blip(float freq, float dur, float decay)
        {
            int sr = 44100, len = Mathf.Max(8, (int)(sr * dur)); var data = new float[len];
            for (int i = 0; i < len; i++) { float ti = i / (float)sr; data[i] = Mathf.Sin(2f * Mathf.PI * freq * ti) * Mathf.Exp(-ti * decay) * 0.5f; }
            var c = AudioClip.Create("blip", len, 1, sr, false); c.SetData(data, 0); return c;
        }

        private void BuildViewfinder()
        {
            try
            {
                _rt = new RenderTexture(460, 300, 16) { name = "SpooderRT", hideFlags = HideFlags.DontSave };
                _rt.Create();
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Lens, false);
                camGo.transform.localPosition = new Vector3(0, 0, 0.02f);   // pushed out in front of the lens
                _preview = camGo.AddComponent<Camera>();
                _preview.targetTexture = _rt; _preview.fieldOfView = 90f; _preview.nearClipPlane = 0.02f; _preview.farClipPlane = 700f;
                _preview.depth = -20; _preview.clearFlags = CameraClearFlags.Skybox; _preview.allowMSAA = false;
                if (_vf != null) _vf.texture = _rt;
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] viewfinder: " + e.Message); }
        }

        private void BuildLaser()
        {
            var go = new GameObject("Laser");
            go.transform.SetParent(Root.transform, false);
            _laser = go.AddComponent<LineRenderer>();
            _laser.useWorldSpace = true; _laser.positionCount = 2; _laser.numCapVertices = 4;
            _laser.startWidth = 0.004f; _laser.endWidth = 0.004f;
            _laser.material = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            _laser.startColor = new Color(Styles.Accent.r, Styles.Accent.g, Styles.Accent.b, 0.9f);
            _laser.endColor = new Color(Styles.Accent.r, Styles.Accent.g, Styles.Accent.b, 0.2f);
            _laser.enabled = false;
        }

        // ============================================================ placement / grab / laser

        public void SummonToHand()
        {
            EnsureSpawned();
            Transform hh = HeadTransform();
            if (hh != null)
            {
                Root.transform.position = hh.position + hh.forward * 0.5f - hh.up * 0.08f;
                Root.transform.rotation = Quaternion.LookRotation(Root.transform.position - hh.position, Vector3.up);
            }
            else if (Camera.main != null) Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.55f;
            _popT = 0f; Held = false;
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
            if (_preview != null && !_preview.enabled) _preview.enabled = true;

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
                    else Root.transform.SetPositionAndRotation(hand.TransformPoint(_localPos), hand.rotation * _localRot); // full 6DOF, no flip
                    holding = _hand;
                }

                // laser from RIGHT-hand fingertip only (when the right hand isn't the one holding)
                _hoverIdx = -1;
                bool ray = false;
                if (holding != 0 && rh != null) ray = LaserRight(rh, poller.rightControllerTriggerButton);
                if (_laser != null) _laser.enabled = ray;
                if (_cursor != null && _cursor.gameObject.activeSelf != ray) _cursor.gameObject.SetActive(ray);
            }

            Animate();
        }

        private bool LaserRight(Transform rh, bool trigger)
        {
            Transform tip = Tip();
            Vector3 liveOrigin = tip != null ? tip.position : rh.position;
            Vector3 liveDir = tip != null ? (tip.position - rh.position).normalized : rh.forward;
            if (liveDir.sqrMagnitude < 0.0001f) liveDir = rh.forward;
            // freeze the aim while the trigger is held so the finger-curl can't bend the laser
            if (!trigger || _aimDir.sqrMagnitude < 0.0001f) { _aimOrigin = liveOrigin; _aimDir = liveDir; }
            Vector3 origin = _aimOrigin, dir = _aimDir;

            Vector3 nrm = _canvas.transform.forward, cpos = _canvas.position;
            float denom = Vector3.Dot(dir, nrm);
            bool hit = false; Vector2 lp2 = Vector2.zero; Vector3 hitW = origin + dir * 1.2f;
            if (Mathf.Abs(denom) > 1e-4f)
            {
                float dist = Vector3.Dot(cpos - origin, nrm) / denom;
                if (dist > 0.02f && dist < 6f)
                {
                    hitW = origin + dir * dist;
                    Vector3 lp = _canvas.InverseTransformPoint(hitW);
                    if (Mathf.Abs(lp.x) <= CW / 2f && Mathf.Abs(lp.y) <= CH / 2f) { hit = true; lp2 = new Vector2(lp.x, lp.y); }
                }
            }
            if (_laser != null) { _laser.SetPosition(0, origin); _laser.SetPosition(1, hit ? hitW : origin + dir * 1f); }

            int hovered = -1;
            if (hit)
            {
                _smoothLp = Vector2.Lerp(_smoothLp, lp2, 0.55f); lp2 = _smoothLp;   // de-jitter the pointer
                if (_cursor != null) SetRect(_cursor.rectTransform, lp2.x, lp2.y, 26, 26);
                for (int i = 0; i < _btns.Count; i++)
                {
                    var b = _btns[i];
                    if (Mathf.Abs(lp2.x - b.c.x) <= b.half.x * 1.25f && Mathf.Abs(lp2.y - b.c.y) <= b.half.y * 1.25f) { hovered = i; break; }
                }
                if (hovered >= 0) { _btns[hovered].hover = 1f; _hoverIdx = hovered; }
            }

            // click the button we were aiming at BEFORE the trigger pull (so the finger-curl drift doesn't matter)
            if (trigger && !_trigPrev)
            {
                int target = _hoverPrev >= 0 ? _hoverPrev : hovered;
                if (target >= 0) Click(target);
            }
            _hoverPrev = hovered;
            _trigPrev = trigger;
            return hit;
        }

        private void Click(int i)
        {
            if (i < 0 || i >= _btns.Count) return;
            var b = _btns[i];
            b.flash = 1f; b.scale = 0.86f;
            if (_audio != null && _click != null) _audio.PlayOneShot(_click, 0.7f);
            try { b.act?.Invoke(); } catch (Exception e) { Debug.LogWarning("[GorillaCaster] btn: " + e.Message); }
            try { if (GorillaTagger.Instance != null) GorillaTagger.Instance.StartVibration(false, 0.6f, 0.05f); } catch { }
        }

        private void Animate()
        {
            if (_hoverIdx != _lastHoverSound)
            {
                if (_hoverIdx >= 0 && _audio != null && _hoverClip != null) _audio.PlayOneShot(_hoverClip, 0.2f);
                _lastHoverSound = _hoverIdx;
            }
            if (_popT < 1f) { _popT = Mathf.Min(1f, _popT + Time.deltaTime * 5f); float s = Mathf.SmoothStep(0.7f, 1f, _popT); if (_rootGroup != null) _rootGroup.alpha = _popT; if (_canvas != null) _canvas.localScale = new Vector3(-S, S, S) * s; }
            if (_accentBar != null) _accentBar.color = Color.Lerp(Styles.Accent, Styles.Accent2, 0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f));

            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i]; if (b.img == null) continue;
                float ts = b.hover > 0.5f ? 1.08f : 1f;
                b.scale = Mathf.Lerp(b.scale, ts, Time.deltaTime * 12f);
                b.rt.localScale = Vector3.one * b.scale;
                Color c = b.col;
                if (b.hover > 0.01f) c = Color.Lerp(b.col, Color.white, 0.14f);
                if (b.flash > 0f) { b.flash -= Time.deltaTime * 4f; c = Color.Lerp(c, Color.white, Mathf.Clamp01(b.flash)); }
                b.img.color = c;
                b.hover = Mathf.MoveTowards(b.hover, 0f, Time.deltaTime * 6f);
            }
            if (_status != null && StatusText != null) _status.text = StatusText() + (_flip ? "  (selfie)" : "");
        }

        private bool Near(Transform hand) => Vector3.Distance(hand.position, Root.transform.position) < GrabRadius;
        private void Attach(int hand, Transform t)
        {
            Held = true; _hand = hand;
            _localPos = t.InverseTransformPoint(Root.transform.position);
            _localRot = Quaternion.Inverse(t.rotation) * Root.transform.rotation;   // keep current orientation (no flip on grab)
        }

        private static Vector3 HeadPos() { var t = HeadTransform(); return t != null ? t.position : Vector3.zero; }
        private static Transform HeadTransform() { var t = GorillaTagger.Instance; if (t != null && t.mainCamera != null) return t.mainCamera.transform; return Camera.main != null ? Camera.main.transform : null; }
        private static Transform Tip() { var t = GorillaTagger.Instance; if (t == null) return null; return t.rightHandTriggerCollider != null ? t.rightHandTriggerCollider.transform : null; }

        // ============================================================ uGUI helpers

        private static Color Panel() => new Color(0.18f, 0.20f, 0.25f);
        private static Sprite _round;
        private static Sprite Round() { if (_round == null) { var t = TextureGen.RoundedRect(40, 16, Color.white); _round = Sprite.Create(t, new Rect(0, 0, 40, 40), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16)); } return _round; }
        private static Font _font;
        private static Font F() { if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } } } return _font; }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h); }
        private static Image MkImage(Transform parent, string n, Sprite sprite, Color col, float x, float y, float w, float h) { var go = new GameObject(n, typeof(Image)); go.transform.SetParent(parent, false); var img = go.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.color = col; img.raycastTarget = false; SetRect(img.rectTransform, x, y, w, h); return img; }
        private static RawImage MkRaw(Transform parent, string n, float x, float y, float w, float h) { var go = new GameObject(n, typeof(RawImage)); go.transform.SetParent(parent, false); var ri = go.GetComponent<RawImage>(); ri.raycastTarget = false; SetRect((RectTransform)go.transform, x, y, w, h); return ri; }
        private static Text MkText(Transform parent, string n, string text, int size, Color col, float x, float y, float w, float h, TextAnchor anchor) { var go = new GameObject(n, typeof(Text)); go.transform.SetParent(parent, false); var tt = go.GetComponent<Text>(); tt.font = F(); tt.text = text; tt.fontSize = size; tt.fontStyle = FontStyle.Bold; tt.alignment = anchor; tt.color = col; tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow; tt.raycastTarget = false; SetRect(tt.rectTransform, x, y, w, h); return tt; }
        private static void SetLayer(GameObject go, int layer) { go.layer = layer; foreach (Transform c in go.transform) SetLayer(c.gameObject, layer); }

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size; Paint(go.GetComponent<Renderer>(), col); return go; }
        private static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); }
        private static Shader _shader;
        private static void Paint(Renderer r, Color col) { if (r == null) return; if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default"); var m = new Material(_shader); m.color = col; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col); r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
    }
}
