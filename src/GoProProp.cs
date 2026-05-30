using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GorillaCaster
{
    /// <summary>
    /// A premium, grabbable in-VR control TABLET built with world-space uGUI. Multi-page touch UI
    /// (Camera / Cast / World / Look / Comp), a live viewfinder, dynamic button animations and
    /// click sounds. You poke it with your fingertip (the game's hand trigger collider). The screen
    /// always billboards to your head so it's never mirrored. Grab with grip, drop to place.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;

        public Action<string> OnCommand;   // single command bus ("mode","fov+","day",...)
        public Func<string> StatusText;

        private Camera _preview;
        private RenderTexture _rt;
        private RawImage _vf;
        private Text _status;
        private Image _accentBar;
        private RectTransform _canvas;
        private CanvasGroup _rootGroup;
        private AudioSource _audio;
        private AudioClip _click, _hover;
        private int _layer;
        private float _popT;            // spawn pop-in
        private int _page;
        private readonly List<Page> _pages = new List<Page>();
        private readonly List<Btn> _btns = new List<Btn>();   // all buttons (tabs + page buttons)
        private int _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private int _pressR = -1, _pressL = -1;   // edge-detect: button currently pressed per hand
        private const float GrabRadius = 0.30f;
        private const float CW = 660f, CH = 410f, S = 0.00040f;

        private class Page { public string id; public GameObject go; public CanvasGroup cg; public float alpha; }
        private class Btn
        {
            public RectTransform rt; public Image img; public Text txt;
            public Vector2 c, half; public Action act; public Color col;
            public int page;       // -1 = always visible
            public float scale = 1f, flash, hover;
            public Func<bool> active; // optional on/off state for toggle buttons
        }

        // ============================================================ spawn

        public void EnsureSpawned()
        {
            if (Root != null) return;
            _layer = FreeLayer();
            Root = new GameObject("SpooderTablet") { hideFlags = HideFlags.DontSave };

            float w = CW * S, h = CH * S, t = 0.008f;
            Box(Root.transform, Vector3.zero, new Vector3(w + 0.01f, h + 0.01f, t), new Color(0.06f, 0.065f, 0.08f));
            // rear lens (-z)
            Box(Root.transform, new Vector3(w * 0.4f, h * 0.4f, -0.006f), new Vector3(0.022f, 0.022f, 0.01f), new Color(0.03f, 0.03f, 0.04f));
            var glass = Cyl(Root.transform, new Vector3(w * 0.4f, h * 0.4f, -0.012f), 0.008f, 0.002f, new Color(0.1f, 0.4f, 0.6f), true);
            glass.transform.localRotation = Quaternion.Euler(90, 0, 0);

            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(w * 0.4f, h * 0.4f, -0.03f);
            lensGo.transform.localRotation = Quaternion.Euler(0, 180, 0);   // films away from holder
            Lens = lensGo.transform;

            BuildAudio();
            BuildCanvas();
            BuildViewfinder();

            SetLayer(Root, _layer);
            if (_preview != null) _preview.cullingMask = ~(1 << _layer);   // never film the tablet (no feedback freeze)
            _popT = 0f;
            SetPage(0, instant: true);
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
            _canvas.localPosition = new Vector3(0, 0, 0.0075f);
            _canvas.localScale = Vector3.one * S;

            MkImage(_canvas, "bg", Round(), new Color(0.10f, 0.11f, 0.14f, 1f), 0, 0, CW, CH);
            _accentBar = MkImage(_canvas, "accent", Round(), Styles.Accent, 0, CH / 2f - 6, CW - 26, 4);
            MkText(_canvas, "brand", "SPOODER", 22, new Color(0.5f, 0.85f, 1f), -CW / 2f + 90, CH / 2f - 30, 170, 28, TextAnchor.MiddleLeft);
            _status = MkText(_canvas, "status", "READY", 22, Color.white, CW / 2f - 130, CH / 2f - 30, 240, 28, TextAnchor.MiddleRight);

            // viewfinder (left)
            MkImage(_canvas, "vfbg", Round(), Color.black, -CW * 0.225f, -14, CW * 0.46f + 8, CH * 0.7f + 8);
            _vf = MkRaw(_canvas, "vf", -CW * 0.225f, -14, CW * 0.46f, CH * 0.7f);

            // tab row (top of right panel)
            string[] tabs = { "CAM", "CAST", "WORLD", "LOOK", "COMP" };
            float tx0 = 60f, tstep = 52f, ty = CH * 0.34f;
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                AddBtn(tabs[i], tx0 + i * tstep, ty, 48, 30, new Color(0.15f, 0.17f, 0.22f), () => SetPage(idx), -1, 20,
                       () => _page == idx);
            }

            // pages
            AddPage("CAM");
            float[] cols = { 90f, 222f };
            float[] rowsA = { 78f, 22f, -34f };
            PageBtn("CAM", "MODE", cols[0], rowsA[0], "mode", Panel());
            PageBtn("CAM", "VIEW", cols[1], rowsA[0], "view", Panel());
            PageBtn("CAM", "FOV -", cols[0], rowsA[1], "fov-", Panel());
            PageBtn("CAM", "FOV +", cols[1], rowsA[1], "fov+", Panel());
            PageBtn("CAM", "ORBIT", cols[0], rowsA[2], "orbit", Panel());
            PageBtn("CAM", "1st P", cols[1], rowsA[2], "fp", Panel());

            AddPage("CAST");
            PageBtn("CAST", "NEXT", cols[0], rowsA[0], "next", new Color(0.18f, 0.42f, 0.5f));
            PageBtn("CAST", "PREV", cols[1], rowsA[0], "prev", new Color(0.18f, 0.42f, 0.5f));
            PageBtn("CAST", "AUTO", cols[0], rowsA[1], "auto", Panel());
            PageBtn("CAST", "DIRECT", cols[1], rowsA[1], "dir", new Color(0.32f, 0.27f, 0.55f));

            AddPage("WORLD");
            PageBtn("WORLD", "DAY", cols[0], rowsA[0], "day", Panel());
            PageBtn("WORLD", "NIGHT", cols[1], rowsA[0], "night", Panel());
            PageBtn("WORLD", "RAIN", cols[0], rowsA[1], "rain", Panel());
            PageBtn("WORLD", "CLEAR", cols[1], rowsA[1], "clear", Panel());

            AddPage("LOOK");
            PageBtn("LOOK", "FILTER", cols[0], rowsA[0], "filter", Panel());
            PageBtn("LOOK", "VIGN", cols[1], rowsA[0], "vignette", Panel());
            PageBtn("LOOK", "ASPECT", cols[0], rowsA[1], "aspect", Panel());
            PageBtn("LOOK", "GRID", cols[1], rowsA[1], "grid", Panel());

            AddPage("COMP");
            PageBtn("COMP", "TIMER", cols[0], rowsA[0], "timer", Panel());
            PageBtn("COMP", "SCORE", cols[1], rowsA[0], "score", Panel());
            PageBtn("COMP", "START", cols[0], rowsA[1], "tstart", new Color(0.2f, 0.45f, 0.32f));
            PageBtn("COMP", "RESET", cols[1], rowsA[1], "treset", new Color(0.5f, 0.3f, 0.22f));

            // always-visible SHOT + HUD
            AddBtn("SHOT", 90, -96, 120, 40, new Color(0.2f, 0.42f, 0.5f), () => OnCommand?.Invoke("shot"), -1, 14, null);
            AddBtn("HUD", 222, -96, 120, 40, Panel(), () => OnCommand?.Invoke("hud"), -1, 14, null);
        }

        private void AddPage(string id)
        {
            var go = new GameObject("page_" + id, typeof(CanvasGroup));
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(_canvas, false);
            SetRect(rt, 0, 0, CW, CH);
            _pages.Add(new Page { id = id, go = go, cg = go.GetComponent<CanvasGroup>(), alpha = id == "CAM" ? 1f : 0f });
        }

        private void PageBtn(string page, string label, float x, float y, string cmd, Color col)
        {
            int pi = _pages.FindIndex(p => p.id == page);
            var parent = _pages[pi].go.transform;
            MakeBtn(parent, label, x, y, 122, 46, col, () => OnCommand?.Invoke(cmd), pi, 24, null);
        }

        private void AddBtn(string label, float x, float y, float w, float h, Color col, Action act, int page, int font, Func<bool> active)
        {
            MakeBtn(_canvas, label, x, y, w, h, col, act, page, font, active);
        }

        private Btn MakeBtn(Transform parent, string label, float x, float y, float w, float h, Color col, Action act, int page, int font, Func<bool> active)
        {
            var img = MkImage(parent, "b_" + label, Round(), col, x, y, w, h);
            var txt = MkText(img.rectTransform, "t", label, font, Color.white, 0, 0, w, h, TextAnchor.MiddleCenter);
            var b = new Btn { rt = img.rectTransform, img = img, txt = txt, c = new Vector2(x, y), half = new Vector2(w / 2f, h / 2f), act = act, col = col, page = page, active = active };
            _btns.Add(b);
            return b;
        }

        private void BuildAudio()
        {
            _audio = Root.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 1f; _audio.volume = 0.6f; _audio.maxDistance = 8f;
            _click = Blip(950f, 0.05f, 45f);
            _hover = Blip(1500f, 0.025f, 80f);
        }

        private static AudioClip Blip(float freq, float dur, float decay)
        {
            int sr = 44100; int len = Mathf.Max(8, (int)(sr * dur));
            var data = new float[len];
            for (int i = 0; i < len; i++)
            {
                float ti = i / (float)sr;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * ti) * Mathf.Exp(-ti * decay) * 0.5f;
            }
            var c = AudioClip.Create("blip", len, 1, sr, false);
            c.SetData(data, 0);
            return c;
        }

        private void BuildViewfinder()
        {
            try
            {
                _rt = new RenderTexture(420, 260, 16) { name = "SpooderRT", hideFlags = HideFlags.DontSave };
                _rt.Create();
                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Lens, false);
                camGo.transform.localPosition = new Vector3(0, 0, 0.006f);
                _preview = camGo.AddComponent<Camera>();
                _preview.targetTexture = _rt; _preview.fieldOfView = 90f; _preview.nearClipPlane = 0.02f; _preview.farClipPlane = 700f;
                _preview.depth = -20; _preview.clearFlags = CameraClearFlags.Skybox; _preview.allowMSAA = false;
                if (_vf != null) _vf.texture = _rt;
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] viewfinder: " + e.Message); }
        }

        // ============================================================ placement / grab / touch

        public void SummonToHand()
        {
            EnsureSpawned();
            Transform h = HeadTransform();
            if (h != null)
            {
                // float it out in front of you, facing you, so it's easy to grab
                Root.transform.position = h.position + h.forward * 0.45f - h.up * 0.08f;
                Root.transform.rotation = Quaternion.LookRotation(h.position - Root.transform.position, Vector3.up);
            }
            else if (Camera.main != null)
            {
                Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.5f;
            }
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
                    else Root.transform.position = hand.TransformPoint(_localPos);
                    holding = _hand;
                }

                // fingertip touch (skip the holding hand)
                if (holding != 0) Touch(0, Tip(true));
                if (holding != 1) Touch(1, Tip(false));
            }

            // billboard the screen toward the head ONLY while held (so it's readable & not mirrored).
            // When not held it stays put so you can actually reach out and grab it.
            if (Held)
            {
                Vector3 head = HeadPos();
                if (head != Vector3.zero)
                    Root.transform.rotation = Quaternion.Slerp(Root.transform.rotation,
                        Quaternion.LookRotation(head - Root.transform.position, Vector3.up), 0.4f);
            }

            Animate();
        }

        private void Touch(int handIdx, Transform tip)
        {
            int hovered = -1, pressed = -1;
            if (tip != null && _canvas != null)
            {
                Vector3 lp = _canvas.InverseTransformPoint(tip.position);
                if (lp.z > -40f && lp.z < 160f)
                {
                    for (int i = 0; i < _btns.Count; i++)
                    {
                        var b = _btns[i];
                        if (!Visible(b)) continue;
                        Vector2 p = ToCanvas(b, lp);
                        if (Mathf.Abs(p.x - b.c.x) <= b.half.x && Mathf.Abs(p.y - b.c.y) <= b.half.y)
                        {
                            hovered = i;
                            if (lp.z <= 28f) pressed = i;
                            break;
                        }
                    }
                }
            }
            if (hovered >= 0) _btns[hovered].hover = 1f;

            int last = handIdx == 0 ? _pressR : _pressL;
            if (pressed >= 0 && pressed != last)   // press down-edge
            {
                var b = _btns[pressed];
                b.flash = 1f; b.scale = 0.86f;
                if (_audio != null && _click != null) _audio.PlayOneShot(_click, 0.7f);
                try { b.act?.Invoke(); } catch (Exception e) { Debug.LogWarning("[GorillaCaster] btn: " + e.Message); }
                try { if (GorillaTagger.Instance != null) GorillaTagger.Instance.StartVibration(handIdx == 1, 0.6f, 0.06f); } catch { }
            }
            else if (hovered >= 0 && hovered != last && pressed < 0)
            {
                if (_audio != null && _hover != null) _audio.PlayOneShot(_hover, 0.25f);
            }
            if (handIdx == 0) _pressR = pressed; else _pressL = pressed;
        }

        // a button may live inside a page panel (which is itself centered on the canvas), so its
        // local coords are already canvas-space here since panels fill the canvas at (0,0).
        private Vector2 ToCanvas(Btn b, Vector3 lp) => new Vector2(lp.x, lp.y);

        private bool Visible(Btn b) => b.page < 0 || b.page == _page;

        private void Animate()
        {
            // pop-in
            if (_popT < 1f) { _popT = Mathf.Min(1f, _popT + Time.deltaTime * 5f); float s = Mathf.SmoothStep(0.7f, 1f, _popT); if (_rootGroup != null) _rootGroup.alpha = _popT; if (_canvas != null) _canvas.localScale = Vector3.one * S * s; }

            // page fades
            for (int i = 0; i < _pages.Count; i++)
            {
                var pg = _pages[i];
                float target = i == _page ? 1f : 0f;
                pg.alpha = Mathf.MoveTowards(pg.alpha, target, Time.deltaTime * 6f);
                if (pg.cg != null) pg.cg.alpha = pg.alpha;
                bool act = pg.alpha > 0.01f;
                if (pg.go.activeSelf != act) pg.go.SetActive(act);
            }

            // accent pulse
            if (_accentBar != null) _accentBar.color = Color.Lerp(Styles.Accent, Styles.Accent2, 0.5f + 0.5f * Mathf.Sin(Time.time * 1.5f));

            // buttons: hover/press scale + flash + toggle state tint
            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i];
                if (b.img == null) continue;
                float targetScale = b.hover > 0.5f ? 1.08f : 1f;
                b.scale = Mathf.Lerp(b.scale, targetScale, Time.deltaTime * 12f);
                b.rt.localScale = Vector3.one * b.scale;

                Color baseCol = (b.active != null && b.active()) ? Styles.Accent : b.col;
                if (b.hover > 0.01f && (b.active == null || !b.active())) baseCol = Color.Lerp(b.col, Color.white, 0.12f);
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

        private static Vector3 HeadPos()
        {
            var t = HeadTransform();
            return t != null ? t.position : Vector3.zero;
        }
        private static Transform HeadTransform()
        {
            var t = GorillaTagger.Instance;
            if (t != null && t.mainCamera != null) return t.mainCamera.transform;
            return Camera.main != null ? Camera.main.transform : null;
        }
        private static Transform Tip(bool right)
        {
            var t = GorillaTagger.Instance; if (t == null) return null;
            var go = right ? t.rightHandTriggerCollider : t.leftHandTriggerCollider;
            if (go != null) return go.transform;
            return right ? t.rightHandTransform : t.leftHandTransform;
        }

        // ============================================================ uGUI helpers

        private static Color Panel() => new Color(0.18f, 0.20f, 0.25f);

        private static Sprite _round;
        private static Sprite Round()
        {
            if (_round == null)
            {
                var t = TextureGen.RoundedRect(40, 16, Color.white);
                _round = Sprite.Create(t, new Rect(0, 0, 40, 40), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
            }
            return _round;
        }

        private static Font _font;
        private static Font F()
        {
            if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } } }
            return _font;
        }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h);
        }

        private static Image MkImage(Transform parent, string n, Sprite sprite, Color col, float x, float y, float w, float h)
        {
            var go = new GameObject(n, typeof(Image)); go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.color = col; img.raycastTarget = false;
            SetRect(img.rectTransform, x, y, w, h); return img;
        }
        private static RawImage MkRaw(Transform parent, string n, float x, float y, float w, float h)
        {
            var go = new GameObject(n, typeof(RawImage)); go.transform.SetParent(parent, false);
            var ri = go.GetComponent<RawImage>(); ri.raycastTarget = false; SetRect((RectTransform)go.transform, x, y, w, h); return ri;
        }
        private static Text MkText(Transform parent, string n, string text, int size, Color col, float x, float y, float w, float h, TextAnchor anchor)
        {
            var go = new GameObject(n, typeof(Text)); go.transform.SetParent(parent, false);
            var tt = go.GetComponent<Text>(); tt.font = F(); tt.text = text; tt.fontSize = size; tt.fontStyle = FontStyle.Bold;
            tt.alignment = anchor; tt.color = col; tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow; tt.raycastTarget = false;
            SetRect(tt.rectTransform, x, y, w, h); return tt;
        }

        private static int FreeLayer()
        {
            for (int l = 31; l >= 8; l--) { try { if (string.IsNullOrEmpty(LayerMask.LayerToName(l))) return l; } catch { } }
            return 0;
        }
        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
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
            if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            var m = new Material(_shader); m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (emissive) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col); }
            r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
