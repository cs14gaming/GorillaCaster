using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GorillaCaster
{
    /// <summary>
    /// The in-VR camera phone (Sakuraa-style, glow-up): wide body, gradient theme, header/footer bars,
    /// a rounded glowing screen that mirrors the live broadcast with a viewfinder overlay, and a pink
    /// icon button-grid (First Person / Selfie / Flip / FOV / smoothing / shot / mods). Grab with grip,
    /// aim the right-hand fingertip laser, pull trigger. Mod-check board on page 2.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;
        public Camera CastingCam;
        public string ActiveCmd = "";   // controller sets the active mode command ("fp"/"selfie"/...)

        public Action<string> OnCommand;
        public Func<string> StatusText;
        public Func<List<ModReport>> ModReports;

        private Camera _preview;
        private RenderTexture _rt;
        private RawImage _vf;
        private Text _status, _footer, _screenInfo;
        private Image _accentBar, _cursor, _recDot;
        private RectTransform _canvas;
        private CanvasGroup _rootGroup;
        private GameObject _camPanel, _modPanel;
        private readonly List<Text> _modRows = new List<Text>();
        private LineRenderer _laser;
        private AudioSource _audio;
        private AudioClip _click, _hoverClip;
        private float _popT, _modTimer, _slide;
        private bool _flipped;
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
        private const float CW = 960f, CH = 480f, S = 0.00050f;

        // monochrome theme: dark grey buttons, white accent/glow
        private static readonly Color Pink = new Color(0.165f, 0.168f, 0.180f);    // primary button
        private static readonly Color PinkDim = new Color(0.108f, 0.110f, 0.122f); // secondary button
        private static readonly Color PinkLite = new Color(0.95f, 0.96f, 0.98f);   // white accent / glow
        private static readonly Color Special = new Color(0.225f, 0.230f, 0.250f); // mods/flip buttons

        private class Btn { public RectTransform rt; public Image img, glow; public Vector2 c, half; public Action act; public Color col; public int page; public string cmd; public bool isFlip; public float scale = 1f, flash, hover; }

        // ============================================================ spawn

        public void EnsureSpawned()
        {
            if (Root != null) return;
            Root = new GameObject("SpooderPhone") { hideFlags = HideFlags.DontSave };

            float w = CW * S, h = CH * S, t = 0.01f;
            // body + glowing pink trim + camera bump
            Box(Root.transform, Vector3.zero, new Vector3(w + 0.014f, h + 0.014f, t), new Color(0.04f, 0.045f, 0.06f));
            Box(Root.transform, new Vector3(0, 0, -t * 0.5f), new Vector3(w + 0.02f, h + 0.02f, 0.002f), Pink * 1.4f, true);   // glow trim
            Box(Root.transform, new Vector3(w * 0.36f, h * 0.34f, -0.009f), new Vector3(0.03f, 0.03f, 0.01f), new Color(0.02f, 0.02f, 0.03f)); // camera bump

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
            _canvas.localScale = new Vector3(-S, S, S);

            // gradient background (dark purple -> dark blue)
            var bg = MkRaw(_canvas, "bg", 0, 0, CW, CH);
            bg.texture = Grad();
            bg.color = Color.white;

            // header bar
            MkImage(_canvas, "header", Round(), new Color(0.05f, 0.05f, 0.07f, 0.92f), 0, CH / 2f - 32, CW - 16, 56);
            MkImage(_canvas, "hdot", Round(), Pink, -CW * 0.5f + 50, CH / 2f - 32, 16, 16);
            MkText(_canvas, "brand", "SPOODER  <color=#cfd2d8>CAMERA</color>", 28, Color.white, 0, CH / 2f - 32, CW, 34, TextAnchor.MiddleCenter);
            _accentBar = MkImage(_canvas, "accent", Round(), Pink, 0, CH / 2f - 62, CW - 40, 3);

            // footer bar
            MkImage(_canvas, "footer", Round(), new Color(0.05f, 0.05f, 0.07f, 0.92f), 0, -CH / 2f + 26, CW - 16, 44);
            _footer = MkText(_canvas, "fstatus", "", 22, new Color(0.90f, 0.92f, 0.95f), 0, -CH / 2f + 26, CW, 28, TextAnchor.MiddleCenter);
            _status = _footer;

            // ---------- CAMERA PAGE ----------
            _camPanel = MkPanel("camPanel");
            BuildScreen(_camPanel.transform);

            float[] cx = { 132f, 256f, 380f };
            float[] ry = { 118f, 40f, -38f, -116f };
            float bw = 112f, bh = 70f;
            AddCard(_camPanel.transform, "FPV", "fpv", cx[0], ry[0], bw, bh, Pink, 0, "fp", null);
            AddCard(_camPanel.transform, "SELFIE", "selfie", cx[1], ry[0], bw, bh, Pink, 0, "selfie", null);
            AddCardFlip(_camPanel.transform, "FLIP", "flip", cx[2], ry[0], bw, bh, new Color(0.55f, 0.3f, 0.7f), 0);
            AddCard(_camPanel.transform, "FOV-", "fov-", cx[0], ry[1], bw, bh, PinkDim, 0, "fov-", null);
            AddCard(_camPanel.transform, "FOV+", "fov+", cx[1], ry[1], bw, bh, PinkDim, 0, "fov+", null);
            AddCardAct(_camPanel.transform, "MODS", "mods", cx[2], ry[1], bw, bh, new Color(0.55f, 0.3f, 0.7f), 0, () => SetPage(1));
            AddCard(_camPanel.transform, "SMTH-", "smth-", cx[0], ry[2], bw, bh, PinkDim, 0, "smooth-", null);
            AddCard(_camPanel.transform, "SMTH+", "smth+", cx[1], ry[2], bw, bh, PinkDim, 0, "smooth+", null);
            AddCard(_camPanel.transform, "SHOT", "shot", cx[2], ry[2], bw, bh, PinkDim, 0, "shot", null);
            AddCard(_camPanel.transform, "HIDE", "hide", cx[1], ry[3], bw, bh, PinkDim, 0, "hud", null);

            // ---------- MOD PAGE ----------
            _modPanel = MkPanel("modPanel");
            MkImage(_modPanel.transform, "board", Round(), new Color(0.05f, 0.06f, 0.08f, 0.96f), 0, -16, CW - 60, CH - 150);
            MkText(_modPanel.transform, "modtitle", "MOD CHECKER", 28, Pink, 0, CH / 2f - 96, CW, 32, TextAnchor.MiddleCenter);
            AddCardAct(_modPanel.transform, "BACK", "back", -CW / 2f + 120, CH / 2f - 96, 150, 56, Pink, 1, () => SetPage(0));
            AddCardAct(_modPanel.transform, "REDO", "refresh", CW / 2f - 110, CH / 2f - 96, 150, 56, PinkDim, 1, RefreshMods);
            float ry0 = CH / 2f - 150;
            for (int i = 0; i < 8; i++)
            {
                var row = MkText(_modPanel.transform, "row" + i, "", 24, Color.white, 0, ry0 - i * 38f, CW - 110, 34, TextAnchor.MiddleCenter);
                _modRows.Add(row);
            }

            _cursor = MkImage(_canvas, "cursor", Round(), PinkLite, 0, 0, 24, 24);
            _cursor.transform.SetAsLastSibling();
            _cursor.gameObject.SetActive(false);
        }

        private void BuildScreen(Transform parent)
        {
            float sx = -CW * 0.21f, sy = -6f, sw = CW * 0.5f, sh = CH * 0.66f;
            // glow frame
            var frame = MkImage(parent, "vfglow", Glow(), PinkLite, sx, sy, sw + 16, sh + 16);
            frame.color = new Color(PinkLite.r, PinkLite.g, PinkLite.b, 0.85f);
            // rounded mask + live feed
            var maskGo = new GameObject("vfmask", typeof(Image), typeof(Mask));
            maskGo.transform.SetParent(parent, false);
            var mimg = maskGo.GetComponent<Image>(); mimg.sprite = Round(); mimg.type = Image.Type.Sliced; mimg.color = Color.black;
            maskGo.GetComponent<Mask>().showMaskGraphic = true;
            SetRect(mimg.rectTransform, sx, sy, sw, sh);
            _vf = MkRaw(maskGo.transform, "vf", 0, 0, sw, sh);
            // viewfinder overlay
            _screenInfo = MkText(maskGo.transform, "info", "", 20, new Color(1f, 1f, 1f, 0.9f), 0, sh / 2f - 18, sw, 24, TextAnchor.MiddleCenter);
            _recDot = MkImage(maskGo.transform, "rec", Round(), new Color(1f, 0.25f, 0.3f), sw / 2f - 22, sh / 2f - 18, 14, 14);
            // subtle vignette
            var vig = MkRaw(maskGo.transform, "vig", 0, 0, sw, sh);
            vig.texture = Vig(); vig.color = new Color(1, 1, 1, 0.5f);
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
            if (p != _page) _slide = 1f;
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

        // ---- button cards ----
        private void AddCard(Transform parent, string label, string icon, float x, float y, float w, float h, Color col, int page, string cmd, Action act)
            => Card(parent, label, icon, x, y, w, h, col, page, cmd, act ?? (() => OnCommand?.Invoke(cmd)), false);
        private void AddCardAct(Transform parent, string label, string icon, float x, float y, float w, float h, Color col, int page, Action act)
            => Card(parent, label, icon, x, y, w, h, col, page, null, act, false);
        private void AddCardFlip(Transform parent, string label, string icon, float x, float y, float w, float h, Color col, int page)
            => Card(parent, label, icon, x, y, w, h, col, page, null, ToggleFlip, true);

        private void Card(Transform parent, string label, string icon, float x, float y, float w, float h, Color col, int page, string cmd, Action act, bool isFlip)
        {
            MkImage(parent, "sh", Round(), new Color(0, 0, 0, 0.55f), x, y - 5, w, h);                  // drop shadow
            var glow = MkImage(parent, "glow", Glow(), PinkLite, x, y, w + 12, h + 12);                  // active glow
            glow.color = new Color(PinkLite.r, PinkLite.g, PinkLite.b, 0f);
            var bg = MkImage(parent, "bg", Round(), col, x, y, w, h);                                    // body
            if (icon != null) { var ic = MkIcon(bg.transform, IconGen.Get(icon), 0, 12, 40, 40); }
            MkText(bg.transform, "t", label, 19, Color.white, 0, -20, w, 22, TextAnchor.MiddleCenter);
            _btns.Add(new Btn { rt = bg.rectTransform, img = bg, glow = glow, c = new Vector2(x, y), half = new Vector2(w / 2f, h / 2f), act = act, col = col, page = page, cmd = cmd, isFlip = isFlip });
        }

        private void ToggleFlip()
        {
            _flipped = !_flipped;
            if (Lens != null) Lens.localRotation = _flipped ? Quaternion.identity : Quaternion.Euler(0, 180, 0);
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
                _rt = new RenderTexture(560, 360, 16) { name = "SpooderRT", hideFlags = HideFlags.DontSave };
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
            _laser.startColor = new Color(0.95f, 0.96f, 0.98f, 0.9f);
            _laser.endColor = new Color(0.95f, 0.96f, 0.98f, 0.15f);
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
                if (_cursor != null) SetRect(_cursor.rectTransform, lp2.x, lp2.y, 24, 24);
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
            b.flash = 1f; b.scale = 0.84f;
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
            if (_accentBar != null) _accentBar.color = Color.Lerp(Pink, PinkLite, 0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f));

            // page slide
            if (_slide > 0f)
            {
                _slide = Mathf.MoveTowards(_slide, 0f, Time.deltaTime * 5f);
                var cur = _page == 0 ? _camPanel : _modPanel;
                if (cur != null) { var rt = (RectTransform)cur.transform; rt.anchoredPosition = new Vector2(Mathf.Lerp(0f, 90f, _slide), 0); }
            }

            for (int i = 0; i < _btns.Count; i++)
            {
                var b = _btns[i]; if (b.img == null) continue;
                bool active = (b.cmd != null && b.cmd == ActiveCmd) || (b.isFlip && _flipped);
                float ts = b.hover > 0.5f ? 1.08f : 1f;
                b.scale = Mathf.Lerp(b.scale, ts, Time.deltaTime * 12f);
                b.rt.localScale = Vector3.one * b.scale;
                Color c = active ? Color.Lerp(b.col, Color.white, 0.25f) : b.col;
                if (b.hover > 0.01f) c = Color.Lerp(c, Color.white, 0.16f);
                if (b.flash > 0f) { b.flash -= Time.deltaTime * 4f; c = Color.Lerp(c, Color.white, Mathf.Clamp01(b.flash)); }
                b.img.color = c;
                if (b.glow != null) { float ga = active ? 0.9f : 0f; var gc = b.glow.color; b.glow.color = new Color(PinkLite.r, PinkLite.g, PinkLite.b, Mathf.MoveTowards(gc.a, ga, Time.deltaTime * 6f)); }
                b.hover = Mathf.MoveTowards(b.hover, 0f, Time.deltaTime * 6f);
            }

            if (_footer != null && StatusText != null) _footer.text = StatusText() + (_flipped ? "  ·  SELFIE" : "") + (Held ? "  ·  held" : "");
            if (_screenInfo != null && StatusText != null) _screenInfo.text = StatusText();
            if (_recDot != null) { var p = 0.4f + 0.6f * Mathf.PingPong(Time.time * 2f, 1f); _recDot.color = new Color(1f, 0.25f, 0.3f, p); }
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

        private static Sprite _round, _glow; private static Texture _grad, _vig;
        private static Sprite Round() { if (_round == null) { var t = TextureGen.RoundedRect(40, 16, Color.white); _round = Sprite.Create(t, new Rect(0, 0, 40, 40), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16)); } return _round; }
        private static Sprite Glow() { if (_glow == null) { var t = TextureGen.RoundedOutline(48, 18, 4, Color.white); _glow = Sprite.Create(t, new Rect(0, 0, 48, 48), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(18, 18, 18, 18)); } return _glow; }
        private static Texture Grad() { if (_grad == null) _grad = TextureGen.Gradient(64, new Color(0.085f, 0.088f, 0.098f), new Color(0.045f, 0.047f, 0.053f)); return _grad; }
        private static Texture Vig() { if (_vig == null) _vig = TextureGen.Vignette(96, new Color(0, 0, 0, 0.8f)); return _vig; }
        private static Font _font;
        private static Font F() { if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { } if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } } } return _font; }

        private static void SetRect(RectTransform rt, float x, float y, float w, float h) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); rt.sizeDelta = new Vector2(w, h); }
        private static Image MkImage(Transform parent, string n, Sprite sprite, Color col, float x, float y, float w, float h) { var go = new GameObject(n, typeof(Image)); go.transform.SetParent(parent, false); var img = go.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Sliced; img.color = col; img.raycastTarget = false; SetRect(img.rectTransform, x, y, w, h); return img; }
        private static Image MkIcon(Transform parent, Sprite sprite, float x, float y, float w, float h) { var go = new GameObject("ic", typeof(Image)); go.transform.SetParent(parent, false); var img = go.GetComponent<Image>(); img.sprite = sprite; img.type = Image.Type.Simple; img.color = Color.white; img.raycastTarget = false; SetRect(img.rectTransform, x, y, w, h); return img; }
        private static RawImage MkRaw(Transform parent, string n, float x, float y, float w, float h) { var go = new GameObject(n, typeof(RawImage)); go.transform.SetParent(parent, false); var ri = go.GetComponent<RawImage>(); ri.raycastTarget = false; SetRect((RectTransform)go.transform, x, y, w, h); return ri; }
        private static Text MkText(Transform parent, string n, string text, int size, Color col, float x, float y, float w, float h, TextAnchor anchor) { var go = new GameObject(n, typeof(Text)); go.transform.SetParent(parent, false); var tt = go.GetComponent<Text>(); tt.font = F(); tt.text = text; tt.fontSize = size; tt.fontStyle = FontStyle.Bold; tt.alignment = anchor; tt.color = col; tt.horizontalOverflow = HorizontalWrapMode.Overflow; tt.verticalOverflow = VerticalWrapMode.Overflow; tt.raycastTarget = false; tt.supportRichText = true; SetRect(tt.rectTransform, x, y, w, h); return tt; }
        private static void SetLayer(GameObject go, int layer) { go.layer = layer; foreach (Transform c in go.transform) SetLayer(c.gameObject, layer); }

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col, bool emissive = false) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size; Paint(go.GetComponent<Renderer>(), col, emissive); return go; }
        private static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c); }
        private static Shader _shader;
        private static void Paint(Renderer r, Color col, bool emissive) { if (r == null) return; if (_shader == null) _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default"); var m = new Material(_shader); m.color = col; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col); if (emissive) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col); } r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
    }
}
