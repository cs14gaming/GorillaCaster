using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GorillaCaster
{
    /// <summary>
    /// The in-VR camera phone (Sakuraa-style): wide, screen on the right mirroring the live BROADCAST
    /// camera, a pink button grid on the left (First Person / Selfie / FOV / smoothing / shot / mods),
    /// and a MOD-CHECK board page. Grab with grip, aim the right-hand fingertip laser, pull trigger.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;
        public Camera CastingCam;   // the live broadcast camera the screen mirrors

        public Action<string> OnCommand;
        public Func<string> StatusText;
        public Func<List<ModReport>> ModReports;

        private Camera _preview;
        private RenderTexture _rt;
        private RawImage _vf;
        private Text _status;
        private Image _accentBar, _cursor;
        private RectTransform _canvas;
        private CanvasGroup _rootGroup;
        private GameObject _camPanel, _modPanel;
        private readonly List<Text> _modRows = new List<Text>();
        private LineRenderer _laser;
        private AudioSource _audio;
        private AudioClip _click, _hoverClip;
        private float _popT, _modTimer;
        private int _page;
        private readonly List<Btn> _btns = new List<Btn>();
        private int _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private Vector2 _smoothLp;
        private Vector3 _aimOrigin, _aimDir;
        private int _hoverIdx = -1, _lastHoverSound = -1, _hoverPrev = -1;
        private bool _trigPrev;
        private const float GrabRadius = 0.42f;
        private const int UiLayer = 5;
        public int Layer => UiLayer;
        private const float CW = 940f, CH = 470f, S = 0.00050f;   // wider

        private static readonly Color Pink = new Color(0.84f, 0.30f, 0.58f);
        private static readonly Color PinkDim = new Color(0.55f, 0.22f, 0.42f);

        private class Btn { public RectTransform rt; public Image img; public Vector2 c, half; public Action act; public Color col; public int page; public float scale = 1f, flash, hover; }

        // ============================================================ spawn

        public void EnsureSpawned()
        {
            if (Root != null) return;
            Root = new GameObject("SpooderPhone") { hideFlags = HideFlags.DontSave };

            float w = CW * S, h = CH * S, t = 0.01f;
            Box(Root.transform, Vector3.zero, new Vector3(w + 0.012f, h + 0.012f, t), new Color(0.05f, 0.055f, 0.07f));

            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(0, 0, -0.05f);
            lensGo.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Lens = lensGo.transform;

            BuildAudio();
            BuildCanvas();
            BuildViewfinder();
            BuildLaser();

            SetLayer(Root, UiLayer);
            if (_preview != null) { SetLayer(_preview.gameObject, 0); _preview.cullingMask = ~(1 << UiLayer); }
            _popT = 0f; Held = false;
            SetPage(0);
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
            _canvas.localScale = new Vector3(-S, S, S);   // negative X un-mirrors (canvas +x => visual LEFT)

            MkImage(_canvas, "bg", Round(), new Color(0.09f, 0.10f, 0.13f, 1f), 0, 0, CW, CH);
            _accentBar = MkImage(_canvas, "accent", Round(), Pink, 0, CH / 2f - 8, CW - 30, 5);
            _status = MkText(_canvas, "status", "READY", 26, Color.white, 0, CH / 2f - 34, CW, 32, TextAnchor.MiddleCenter);

            // ---------- CAMERA PAGE ----------  screen at canvas -x (visual right); buttons at canvas +x (visual left)
            _camPanel = MkPanel("camPanel");
            MkImage(_camPanel.transform, "vfbg", Round(), Color.black, -CW * 0.22f, -8, CW * 0.5f + 12, CH * 0.78f + 12);
            _vf = MkRaw(_camPanel.transform, "vf", -CW * 0.22f, -8, CW * 0.5f, CH * 0.78f);

            float[] cx = { 140f, 268f, 396f };
            float[] ry = { 118f, 30f, -58f };
            float bw = 118f, bh = 70f;
            AddBtn(_camPanel.transform, "FPV", cx[0], ry[0], "fp", Pink, 0, bw, bh);
            AddBtn(_camPanel.transform, "SELFIE", cx[1], ry[0], "selfie", Pink, 0, bw, bh);
            AddBtn2(_camPanel.transform, "MODS", cx[2], ry[0], () => SetPage(1), new Color(0.55f, 0.3f, 0.7f), 0, bw, bh);
            AddBtn(_camPanel.transform, "FOV -", cx[0], ry[1], "fov-", PinkDim, 0, bw, bh);
            AddBtn(_camPanel.transform, "FOV +", cx[1], ry[1], "fov+", PinkDim, 0, bw, bh);
            AddBtn(_camPanel.transform, "SHOT", cx[2], ry[1], "shot", PinkDim, 0, bw, bh);
            AddBtn(_camPanel.transform, "SMTH -", cx[0], ry[2], "smooth-", PinkDim, 0, bw, bh);
            AddBtn(_camPanel.transform, "SMTH +", cx[1], ry[2], "smooth+", PinkDim, 0, bw, bh);
            AddBtn(_camPanel.transform, "HIDE", cx[2], ry[2], "hud", PinkDim, 0, bw, bh);

            // ---------- MOD-CHECK PAGE ----------
            _modPanel = MkPanel("modPanel");
            MkImage(_modPanel.transform, "board", Round(), new Color(0.06f, 0.07f, 0.09f, 1f), 0, -14, CW - 40, CH - 96);
            MkText(_modPanel.transform, "modtitle", "MOD CHECKER", 26, Pink, 0, CH / 2f - 60, CW, 30, TextAnchor.MiddleCenter);
            AddBtn2(_modPanel.transform, "BACK", -CW / 2f + 96, CH / 2f - 58, () => SetPage(0), Pink, 1, 150, 46);
            AddBtn2(_modPanel.transform, "REFRESH", CW / 2f - 104, CH / 2f - 58, RefreshMods, PinkDim, 1, 168, 46);
            float ry0 = CH / 2f - 116;
            for (int i = 0; i < 9; i++)
            {
                var row = MkText(_modPanel.transform, "row" + i, "", 24, Color.white, 0, ry0 - i * 38f, CW - 80, 34, TextAnchor.MiddleCenter);
                _modRows.Add(row);
            }

            _cursor = MkImage(_canvas, "cursor", Round(), new Color(1f, 0.5f, 0.75f, 0.95f), 0, 0, 26, 26);
            _cursor.transform.SetAsLastSibling();
            _cursor.gameObject.SetActive(false);
        }

        private GameObject MkPanel(string n)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(_canvas, false);
            SetRect((RectTransform)go.transform, 0, 0, CW, CH);
            return go;
        }

        private void SetPage(int p)
        {
            _page = p;
            if (_camPanel != null) _camPanel.SetActive(p == 0);
            if (_modPanel != null) _modPanel.SetActive(p == 1);
            if (p == 1) RefreshMods();
        }

        private void RefreshMods()
        {
            var reps = ModReports != null ? ModReports() : null;
            for (int i = 0; i < _modRows.Count; i++)
            {
                if (reps != null && i < reps.Count)
                {
                    _modRows[i].text = reps[i].name + "   <color=#9fb2c4>" + reps[i].detail + "</color>";
                    _modRows[i].color = reps[i].color;
                    _modRows[i].gameObject.SetActive(true);
                }
                else _modRows[i].gameObject.SetActive(false);
            }
        }

        private void AddBtn(Transform parent, string label, float x, float y, string cmd, Color col, int page, float w, float h)
            => AddBtn2(parent, label, x, y, () => OnCommand?.Invoke(cmd), col, page, w, h);

        private void AddBtn2(Transform parent, string label, float x, float y, Action act, Color col, int page, float w = 152f, float h = 52f)
        {
            var img = MkImage(parent, "b_" + label, Round(), col, x, y, w, h);
            MkText(img.rectTransform, "t", label, 25, Color.white, 0, 0, w, h, TextAnchor.MiddleCenter);
            _btns.Add(new Btn { rt = img.rectTransform, img = img, c = new Vector2(x, y), half = new Vector2(w / 2f, h / 2f), act = act, col = col, page = page });
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
                _rt = new RenderTexture(540, 320, 16) { name = "SpooderRT", hideFlags = HideFlags.DontSave };
                _rt.Create();
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Root.transform, false);
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
            _laser.startColor = new Color(1f, 0.5f, 0.75f, 0.9f);
            _laser.endColor = new Color(1f, 0.5f, 0.75f, 0.2f);
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
                Root.transform.rotation = Quaternion.LookRotation(hh.position - Root.transform.position, Vector3.up);
            }
            else if (Camera.main != null) Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.55f;
            _popT = 0f; Held = false; SetPage(0);
        }

        public void Despawn()
        {
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null; Lens = null; Held = false; _preview = null; _btns.Clear(); _modRows.Clear(); _canvas = null;
        }

        public void SetFov(float fov) { }

        public void Tick(float fov)
        {
            if (Root == null) return;
            // mirror the live broadcast camera onto the screen
            if (_preview != null && CastingCam != null)
            {
                _preview.transform.SetPositionAndRotation(CastingCam.transform.position, CastingCam.transform.rotation);
                _preview.fieldOfView = CastingCam.fieldOfView;
                if (_preview.enabled != Viewfinder) _preview.enabled = Viewfinder;
            }

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
                _hoverIdx = -1;
                bool ray = false;
                if (holding != 0 && rh != null) ray = LaserRight(rh, poller.rightControllerTriggerButton);
                if (_laser != null) _laser.enabled = ray;
                if (_cursor != null && _cursor.gameObject.activeSelf != ray) _cursor.gameObject.SetActive(ray);
            }

            if (_page == 1) { _modTimer += Time.deltaTime; if (_modTimer > 0.75f) { _modTimer = 0f; RefreshMods(); } }
            Animate();
        }

        private bool LaserRight(Transform rh, bool trigger)
        {
            Transform tip = Tip();
            Vector3 liveOrigin = tip != null ? tip.position : rh.position;
            Vector3 liveDir = tip != null ? (tip.position - rh.position).normalized : rh.forward;
            if (liveDir.sqrMagnitude < 0.0001f) liveDir = rh.forward;
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
                _smoothLp = Vector2.Lerp(_smoothLp, lp2, 0.55f); lp2 = _smoothLp;
                if (_cursor != null) SetRect(_cursor.rectTransform, lp2.x, lp2.y, 26, 26);
                for (int i = 0; i < _btns.Count; i++)
                {
                    var b = _btns[i];
                    if (b.page != _page && b.page >= 0) continue;
                    if (Mathf.Abs(lp2.x - b.c.x) <= b.half.x * 1.25f && Mathf.Abs(lp2.y - b.c.y) <= b.half.y * 1.25f) { hovered = i; break; }
                }
                if (hovered >= 0) { _btns[hovered].hover = 1f; _hoverIdx = hovered; }
            }
            if (trigger && !_trigPrev) { int t = _hoverPrev >= 0 ? _hoverPrev : hovered; if (t >= 0) Click(t); }
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
            if (_accentBar != null) _accentBar.color = Color.Lerp(Pink, new Color(1f, 0.55f, 0.8f), 0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f));

            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i]; if (b.img == null) continue;
                float ts = b.hover > 0.5f ? 1.08f : 1f;
                b.scale = Mathf.Lerp(b.scale, ts, Time.deltaTime * 12f);
                b.rt.localScale = Vector3.one * b.scale;
                Color c = b.col;
                if (b.hover > 0.01f) c = Color.Lerp(b.col, Color.white, 0.16f);
                if (b.flash > 0f) { b.flash -= Time.deltaTime * 4f; c = Color.Lerp(c, Color.white, Mathf.Clamp01(b.flash)); }
                b.img.color = c;
                b.hover = Mathf.MoveTowards(b.hover, 0f, Time.deltaTime * 6f);
            }
            if (_status != null && StatusText != null) _status.text = StatusText();
        }

        private bool Near(Transform hand) => Vector3.Distance(hand.position, Root.transform.position) < GrabRadius;
        private void Attach(int hand, Transform t)
        {
            Held = true; _hand = hand;
            _localPos = t.InverseTransformPoint(Root.transform.position);
            _localRot = Quaternion.Inverse(t.rotation) * Root.transform.rotation;
        }

        private static Vector3 HeadPos() { var t = HeadTransform(); return t != null ? t.position : Vector3.zero; }
        private static Transform HeadTransform() { var t = GorillaTagger.Instance; if (t != null && t.mainCamera != null) return t.mainCamera.transform; return Camera.main != null ? Camera.main.transform : null; }
        private static Transform Tip() { var t = GorillaTagger.Instance; if (t == null) return null; return t.rightHandTriggerCollider != null ? t.rightHandTriggerCollider.transform : null; }

        // ============================================================ uGUI helpers

        private static Sprite _round;
        private static Sprite Round() { if (_round == null) { var t = TextureGen.RoundedRect(40, 16, Color.white); _round = Sprite.Create(t, new Rect(0, 0, 40, 40), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16)); } return _round; }
        private static Font _font;
        private static Font F() { if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } } } return _font; }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h); }
        private static Image MkImage(Transform parent, string n, Sprite sprite, Color col, float x, float y, float w, float h) { var go = new GameObject(n, typeof(Image)); go.transform.SetParent(parent, false); var img = go.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.color = col; img.raycastTarget = false; SetRect(img.rectTransform, x, y, w, h); return img; }
        private static RawImage MkRaw(Transform parent, string n, float x, float y, float w, float h) { var go = new GameObject(n, typeof(RawImage)); go.transform.SetParent(parent, false); var ri = go.GetComponent<RawImage>(); ri.raycastTarget = false; SetRect((RectTransform)go.transform, x, y, w, h); return ri; }
        private static Text MkText(Transform parent, string n, string text, int size, Color col, float x, float y, float w, float h, TextAnchor anchor) { var go = new GameObject(n, typeof(Text)); go.transform.SetParent(parent, false); var tt = go.GetComponent<Text>(); tt.font = F(); tt.text = text; tt.fontSize = size; tt.fontStyle = FontStyle.Bold; tt.alignment = anchor; tt.color = col; tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow; tt.raycastTarget = false; tt.supportRichText = true; SetRect(tt.rectTransform, x, y, w, h); return tt; }
        private static void SetLayer(GameObject go, int layer) { go.layer = layer; foreach (Transform c in go.transform) SetLayer(c.gameObject, layer); }

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size; Paint(go.GetComponent<Renderer>(), col); return go; }
        private static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); }
        private static Shader _shader;
        private static void Paint(Renderer r, Color col) { if (r == null) return; if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default"); var m = new Material(_shader); m.color = col; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col); r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
    }
}
