using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GorillaCaster
{
    /// <summary>
    /// A grabbable in-VR control TABLET (Yizzi-style: side grip handles, point-and-click). World-space
    /// uGUI with a live viewfinder, multi-page touch UI, animations and sound. Grab a side handle with
    /// grip; aim your free hand's laser at a button and pull the trigger. The screen billboards to your
    /// head so it's always readable.
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
        private int _layer;
        private float _popT;
        private int _page;
        private readonly List<Page> _pages = new List<Page>();
        private readonly List<Btn> _btns = new List<Btn>();
        private int _hand;                 // holding hand (0=R,1=L), -1 none
        private Vector3 _localPos;
        private Quaternion _localRot;
        private int _hoverIdx = -1;        // currently hovered button (for sound edge)
        private bool _trigPrevR, _trigPrevL;
        private const float GrabRadius = 0.42f;   // wider, forgiving grab

        // larger panel
        private const float CW = 780f, CH = 480f, S = 0.00052f;

        private class Page { public string id; public GameObject go; public CanvasGroup cg; public float alpha; }
        private class Btn
        {
            public RectTransform rt; public Image img;
            public Vector2 c, half; public Action act; public Color col;
            public int page; public float scale = 1f, flash, hover; public Func<bool> active;
        }

        // ============================================================ spawn

        public void EnsureSpawned()
        {
            if (Root != null) return;
            _layer = FreeLayer();
            Root = new GameObject("SpooderTablet") { hideFlags = HideFlags.DontSave };

            float w = CW * S, h = CH * S, t = 0.01f;
            Box(Root.transform, Vector3.zero, new Vector3(w + 0.012f, h + 0.012f, t), new Color(0.06f, 0.065f, 0.08f));

            // Yizzi-style side grip handles
            var hl = Cyl(Root.transform, new Vector3(-(w / 2f + 0.022f), 0, 0), 0.016f, 0.055f, new Color(0.95f, 0.78f, 0.12f));
            hl.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var hr = Cyl(Root.transform, new Vector3((w / 2f + 0.022f), 0, 0), 0.016f, 0.055f, new Color(0.95f, 0.78f, 0.12f));
            hr.transform.localRotation = Quaternion.Euler(0, 0, 90);

            // rear lens
            Box(Root.transform, new Vector3(w * 0.4f, h * 0.4f, -0.008f), new Vector3(0.026f, 0.026f, 0.012f), new Color(0.03f, 0.03f, 0.04f));
            var glass = Cyl(Root.transform, new Vector3(w * 0.4f, h * 0.4f, -0.015f), 0.01f, 0.002f, new Color(0.1f, 0.4f, 0.6f), true);
            glass.transform.localRotation = Quaternion.Euler(90, 0, 0);

            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(w * 0.4f, h * 0.4f, -0.04f);
            lensGo.transform.localRotation = Quaternion.Euler(0, 180, 0);
            Lens = lensGo.transform;

            BuildAudio();
            BuildCanvas();
            BuildViewfinder();
            BuildLaser();

            SetLayer(Root, _layer);
            if (_preview != null) _preview.cullingMask = ~(1 << _layer);
            _popT = 0f;
            SetPage(0, true);
            Held = false;
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
            _canvas.localScale = new Vector3(-S, S, S);   // negative X un-mirrors (screen faces head)

            MkImage(_canvas, "bg", Round(), new Color(0.10f, 0.11f, 0.14f, 1f), 0, 0, CW, CH);
            _accentBar = MkImage(_canvas, "accent", Round(), Styles.Accent, 0, CH / 2f - 8, CW - 30, 5);
            MkText(_canvas, "brand", "SPOODER", 28, new Color(0.5f, 0.85f, 1f), -CW / 2f + 110, CH / 2f - 36, 200, 34, TextAnchor.MiddleLeft);
            _status = MkText(_canvas, "status", "READY", 28, Color.white, CW / 2f - 150, CH / 2f - 36, 280, 34, TextAnchor.MiddleRight);

            MkImage(_canvas, "vfbg", Round(), Color.black, -CW * 0.225f, -16, CW * 0.46f + 10, CH * 0.68f + 10);
            _vf = MkRaw(_canvas, "vf", -CW * 0.225f, -16, CW * 0.46f, CH * 0.68f);

            string[] tabs = { "CAM", "CAST", "WORLD", "LOOK", "COMP" };
            float tx0 = 78f, tstep = 62f, ty = CH * 0.36f;
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                MakeBtn(_canvas, tabs[i], tx0 + i * tstep, ty, 58, 38, new Color(0.15f, 0.17f, 0.22f), () => SetPage(idx), -1, 22, () => _page == idx);
            }

            float[] cols = { 96f, 254f };
            float[] rows = { 96f, 28f, -40f };
            AddPage("CAM");
            PageBtn("CAM", "MODE", cols[0], rows[0], "mode");
            PageBtn("CAM", "FREE", cols[1], rows[0], "free");
            PageBtn("CAM", "FOV -", cols[0], rows[1], "fov-");
            PageBtn("CAM", "FOV +", cols[1], rows[1], "fov+");
            PageBtn("CAM", "ORBIT", cols[0], rows[2], "orbit");
            PageBtn("CAM", "1st P", cols[1], rows[2], "fp");

            AddPage("CAST");
            PageBtn("CAST", "NEXT", cols[0], rows[0], "next");
            PageBtn("CAST", "PREV", cols[1], rows[0], "prev");
            PageBtn("CAST", "AUTO", cols[0], rows[1], "auto");
            PageBtn("CAST", "DIRECT", cols[1], rows[1], "dir");

            AddPage("WORLD");
            PageBtn("WORLD", "DAY", cols[0], rows[0], "day");
            PageBtn("WORLD", "NIGHT", cols[1], rows[0], "night");
            PageBtn("WORLD", "RAIN", cols[0], rows[1], "rain");
            PageBtn("WORLD", "CLEAR", cols[1], rows[1], "clear");

            AddPage("LOOK");
            PageBtn("LOOK", "FILTER", cols[0], rows[0], "filter");
            PageBtn("LOOK", "VIGN", cols[1], rows[0], "vignette");
            PageBtn("LOOK", "ASPECT", cols[0], rows[1], "aspect");
            PageBtn("LOOK", "GRID", cols[1], rows[1], "grid");

            AddPage("COMP");
            PageBtn("COMP", "TIMER", cols[0], rows[0], "timer");
            PageBtn("COMP", "SCORE", cols[1], rows[0], "score");
            PageBtn("COMP", "START", cols[0], rows[1], "tstart");
            PageBtn("COMP", "RESET", cols[1], rows[1], "treset");

            MakeBtn(_canvas, "SHOT", cols[0], -106, 150, 50, new Color(0.2f, 0.42f, 0.5f), () => OnCommand?.Invoke("shot"), -1, 26, null);
            MakeBtn(_canvas, "HUD", cols[1], -106, 150, 50, new Color(0.18f, 0.20f, 0.25f), () => OnCommand?.Invoke("hud"), -1, 26, null);

            // laser cursor (on top)
            _cursor = MkImage(_canvas, "cursor", Round(), new Color(Styles.Accent.r, Styles.Accent.g, Styles.Accent.b, 0.9f), 0, 0, 26, 26);
            _cursor.transform.SetAsLastSibling();
            _cursor.gameObject.SetActive(false);
        }

        private void AddPage(string id)
        {
            var go = new GameObject("page_" + id, typeof(CanvasGroup));
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(_canvas, false);
            SetRect(rt, 0, 0, CW, CH);
            _pages.Add(new Page { id = id, go = go, cg = go.GetComponent<CanvasGroup>(), alpha = id == "CAM" ? 1f : 0f });
        }

        private void PageBtn(string page, string label, float x, float y, string cmd)
        {
            int pi = _pages.FindIndex(p => p.id == page);
            MakeBtn(_pages[pi].go.transform, label, x, y, 148, 58, Panel(), () => OnCommand?.Invoke(cmd), pi, 28, null);
        }

        private void MakeBtn(Transform parent, string label, float x, float y, float w, float h, Color col, Action act, int page, int font, Func<bool> active)
        {
            var img = MkImage(parent, "b_" + label, Round(), col, x, y, w, h);
            MkText(img.rectTransform, "t", label, font, Color.white, 0, 0, w, h, TextAnchor.MiddleCenter);
            _btns.Add(new Btn { rt = img.rectTransform, img = img, c = new Vector2(x, y), half = new Vector2(w / 2f, h / 2f), act = act, col = col, page = page, active = active });
        }

        private void BuildAudio()
        {
            _audio = Root.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 1f; _audio.volume = 0.55f; _audio.maxDistance = 8f;
            _click = Blip(950f, 0.05f, 45f);
            _hoverClip = Blip(1500f, 0.02f, 90f);
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
                _rt = new RenderTexture(460, 280, 16) { name = "SpooderRT", hideFlags = HideFlags.DontSave };
                _rt.Create();
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Lens, false);
                camGo.transform.localPosition = new Vector3(0, 0, 0.008f);
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
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            _laser.material = new Material(sh);
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
                Root.transform.rotation = Quaternion.LookRotation(hh.position - Root.transform.position, Vector3.up);
            }
            else if (Camera.main != null) Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.55f;
            _popT = 0f; Held = false;
        }

        public void Despawn()
        {
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); _rt = null; }
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null; Lens = null; Held = false; _preview = null; _btns.Clear(); _pages.Clear(); _canvas = null;
        }

        public void SetFov(float fov) { if (_preview != null) _preview.fieldOfView = Mathf.Clamp(fov, 10f, 120f); }

        public void Tick(float fov)
        {
            if (Root == null) return;
            SetFov(fov);
            if (_preview != null && !_preview.enabled) _preview.enabled = true;   // keep viewfinder alive

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
                    else Root.transform.position = hand.TransformPoint(_localPos);
                    holding = _hand;
                }

                // laser from the free hand(s)
                _hoverIdx = -1; bool anyRay = false;
                if (holding != 0 && rh != null) anyRay |= Laser(0, rh, poller.rightControllerTriggerButton, ref _trigPrevR);
                if (holding != 1 && lh != null) anyRay |= Laser(1, lh, poller.leftControllerTriggerButton, ref _trigPrevL);
                if (_laser != null) _laser.enabled = anyRay;
                if (_cursor != null && _cursor.gameObject.activeSelf != anyRay) _cursor.gameObject.SetActive(anyRay);
            }

            if (Held)
            {
                Vector3 head = HeadPos();
                if (head != Vector3.zero)
                    Root.transform.rotation = Quaternion.Slerp(Root.transform.rotation,
                        Quaternion.LookRotation(head - Root.transform.position, Vector3.up), 0.4f);
            }

            Animate();
        }

        private bool Laser(int handIdx, Transform hand, bool trigger, ref bool trigPrev)
        {
            if (_canvas == null) { trigPrev = trigger; return false; }
            Vector3 origin = hand.position, dir = hand.forward;
            Vector3 nrm = _canvas.transform.forward, cpos = _canvas.position;
            float denom = Vector3.Dot(dir, nrm);
            bool hitScreen = false; Vector2 lp2 = Vector2.zero; Vector3 hitW = origin + dir * 1.5f;
            if (Mathf.Abs(denom) > 1e-4f)
            {
                float dist = Vector3.Dot(cpos - origin, nrm) / denom;
                if (dist > 0.02f && dist < 6f)
                {
                    hitW = origin + dir * dist;
                    Vector3 lp = _canvas.InverseTransformPoint(hitW);
                    if (Mathf.Abs(lp.x) <= CW / 2f && Mathf.Abs(lp.y) <= CH / 2f) { hitScreen = true; lp2 = new Vector2(lp.x, lp.y); }
                }
            }

            // draw laser
            if (_laser != null) { _laser.SetPosition(0, origin); _laser.SetPosition(1, hitScreen ? hitW : origin + dir * 1.2f); }

            if (hitScreen)
            {
                if (_cursor != null) SetRect(_cursor.rectTransform, lp2.x, lp2.y, 26, 26);
                int hovered = -1;
                for (int i = 0; i < _btns.Count; i++)
                {
                    var b = _btns[i]; if (!Visible(b)) continue;
                    if (Mathf.Abs(lp2.x - b.c.x) <= b.half.x && Mathf.Abs(lp2.y - b.c.y) <= b.half.y) { hovered = i; break; }
                }
                if (hovered >= 0)
                {
                    _btns[hovered].hover = 1f; _hoverIdx = hovered;
                    if (trigger && !trigPrev)   // trigger down-edge
                    {
                        var b = _btns[hovered];
                        b.flash = 1f; b.scale = 0.86f;
                        if (_audio != null && _click != null) _audio.PlayOneShot(_click, 0.7f);
                        try { b.act?.Invoke(); } catch (Exception e) { Debug.LogWarning("[GorillaCaster] btn: " + e.Message); }
                        try { if (GorillaTagger.Instance != null) GorillaTagger.Instance.StartVibration(handIdx == 1, 0.6f, 0.05f); } catch { }
                    }
                }
            }
            trigPrev = trigger;
            return hitScreen;
        }

        private bool Visible(Btn b) => b.page < 0 || b.page == _page;

        private int _lastHoverSound = -1;
        private void Animate()
        {
            // hover sound only on enter (fixes spam)
            if (_hoverIdx != _lastHoverSound)
            {
                if (_hoverIdx >= 0 && _audio != null && _hoverClip != null) _audio.PlayOneShot(_hoverClip, 0.2f);
                _lastHoverSound = _hoverIdx;
            }

            if (_popT < 1f) { _popT = Mathf.Min(1f, _popT + Time.deltaTime * 5f); float s = Mathf.SmoothStep(0.7f, 1f, _popT); if (_rootGroup != null) _rootGroup.alpha = _popT; if (_canvas != null) _canvas.localScale = new Vector3(-S, S, S) * s; }

            for (int i = 0; i < _pages.Count; i++)
            {
                var pg = _pages[i]; float target = i == _page ? 1f : 0f;
                pg.alpha = Mathf.MoveTowards(pg.alpha, target, Time.deltaTime * 6f);
                if (pg.cg != null) pg.cg.alpha = pg.alpha;
                bool act = pg.alpha > 0.01f; if (pg.go.activeSelf != act) pg.go.SetActive(act);
            }
            if (_accentBar != null) _accentBar.color = Color.Lerp(Styles.Accent, Styles.Accent2, 0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f));

            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i]; if (b.img == null) continue;
                float targetScale = b.hover > 0.5f ? 1.08f : 1f;
                b.scale = Mathf.Lerp(b.scale, targetScale, Time.deltaTime * 12f);
                b.rt.localScale = Vector3.one * b.scale;
                Color baseCol = (b.active != null && b.active()) ? Styles.Accent : b.col;
                if (b.hover > 0.01f && (b.active == null || !b.active())) baseCol = Color.Lerp(b.col, Color.white, 0.14f);
                if (b.flash > 0f) { b.flash -= Time.deltaTime * 4f; baseCol = Color.Lerp(baseCol, Color.white, Mathf.Clamp01(b.flash)); }
                b.img.color = baseCol;
                b.hover = Mathf.MoveTowards(b.hover, 0f, Time.deltaTime * 6f);
            }
            if (_status != null && StatusText != null) _status.text = StatusText();
        }

        private void SetPage(int i, bool instant = false)
        {
            _page = Mathf.Clamp(i, 0, Mathf.Max(0, _pages.Count - 1));
            if (instant) for (int p = 0; p < _pages.Count; p++) { _pages[p].alpha = p == _page ? 1f : 0f; if (_pages[p].cg != null) _pages[p].cg.alpha = _pages[p].alpha; _pages[p].go.SetActive(p == _page); }
        }

        private bool Near(Transform hand) => Vector3.Distance(hand.position, Root.transform.position) < GrabRadius;
        private void Attach(int hand, Transform t) { Held = true; _hand = hand; _localPos = t.InverseTransformPoint(Root.transform.position); _localRot = Quaternion.Inverse(t.rotation) * Root.transform.rotation; }

        private static Vector3 HeadPos() { var t = HeadTransform(); return t != null ? t.position : Vector3.zero; }
        private static Transform HeadTransform()
        {
            var t = GorillaTagger.Instance;
            if (t != null && t.mainCamera != null) return t.mainCamera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }

        // ============================================================ uGUI helpers

        private static Color Panel() => new Color(0.18f, 0.20f, 0.25f);
        private static Sprite _round;
        private static Sprite Round()
        {
            if (_round == null) { var t = TextureGen.RoundedRect(40, 16, Color.white); _round = Sprite.Create(t, new Rect(0, 0, 40, 40), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16)); }
            return _round;
        }
        private static Font _font;
        private static Font F() { if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } } } return _font; }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h)
        { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h); }
        private static Image MkImage(Transform parent, string n, Sprite sprite, Color col, float x, float y, float w, float h)
        { var go = new GameObject(n, typeof(Image)); go.transform.SetParent(parent, false); var img = go.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.color = col; img.raycastTarget = false; SetRect(img.rectTransform, x, y, w, h); return img; }
        private static RawImage MkRaw(Transform parent, string n, float x, float y, float w, float h)
        { var go = new GameObject(n, typeof(RawImage)); go.transform.SetParent(parent, false); var ri = go.GetComponent<RawImage>(); ri.raycastTarget = false; SetRect((RectTransform)go.transform, x, y, w, h); return ri; }
        private static Text MkText(Transform parent, string n, string text, int size, Color col, float x, float y, float w, float h, TextAnchor anchor)
        { var go = new GameObject(n, typeof(Text)); go.transform.SetParent(parent, false); var tt = go.GetComponent<Text>(); tt.font = F(); tt.text = text; tt.fontSize = size; tt.fontStyle = FontStyle.Bold; tt.alignment = anchor; tt.color = col; tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow; tt.raycastTarget = false; SetRect(tt.rectTransform, x, y, w, h); return tt; }

        private static int FreeLayer() { for (int l = 31; l >= 8; l--) { try { if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l; } catch { } } return 0; }
        private static void SetLayer(GameObject go, int layer) { go.layer = layer; foreach (Transform c in go.transform) SetLayer(c.gameObject, layer); }

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col, bool emissive = false)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size; Paint(go.GetComponent<Renderer>(), col, emissive); return go; }
        private static GameObject Cyl(Transform parent, Vector3 pos, float radius, float halfLen, Color col, bool emissive = false)
        { var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(radius * 2f, halfLen, radius * 2f); Paint(go.GetComponent<Renderer>(), col, emissive); return go; }
        private static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); }

        private static Shader _shader;
        private static void Paint(Renderer r, Color col, bool emissive)
        {
            if (r == null) return;
            if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var m = new Material(_shader); m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (emissive) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col); }
            r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
