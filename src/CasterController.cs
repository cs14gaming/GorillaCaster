using System;
using System.Collections.Generic;
using System.IO;
using Photon.Pun;
using GorillaNetworking;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GorillaCaster
{
    /// <summary>
    /// Drives Gorilla Tag's third-person / shoulder camera (the desktop / spectator view) for
    /// casting: six camera modes incl. a physical grabbable GoPro, cinematic dolly paths, an
    /// instant-replay system, time-of-day control, and a full stream-overlay HUD. The VR
    /// headset view is never touched.
    /// </summary>
    public class CasterController : MonoBehaviour
    {
        private enum CamMode { Follow, FreeCam, FirstPerson, GoPro, Tripod, Selfie }

        private static readonly string[] ModeNames =
            { "Follow", "FreeCam", "First Person", "GoPro", "Tripod", "Selfie" };
        private static readonly string[] TabNames =
            { "Camera", "GoPro", "Players", "Replay", "Director", "World", "Overlays" };

        // ---- camera ----
        private Camera _cam;
        private CinemachineBrain _brain;
        private CamMode _mode = CamMode.Follow;
        private VRRig _target;

        // tunables
        private float _fov = 90f;
        private float _nearClip = 0.05f;
        private float _fpNearClip = 0.30f;     // first-person clip to hide own cosmetics
        private bool _fpHideSelf = true;
        private float _followDistance = 1.4f;
        private float _followHeight = 0.25f;
        private float _moveSmoothing = 0.45f;
        private float _rotSmoothing = 0.45f;
        private float _freeSpeed = 6f;

        // orbit / selfie / tripod
        private bool _orbit;
        private float _orbitSpeed = 25f, _orbitAngle;
        private float _selfieDist = 0.6f;
        private Vector3 _tripodPos;
        private bool _tripodSet;

        // freecam look
        private bool _freeInit;
        private float _yaw, _pitch;

        // modules
        private readonly DollyPath _dolly = new DollyPath();
        private readonly ReplayRecorder _replay = new ReplayRecorder();
        private readonly GoProProp _goPro = new GoProProp();

        // toggles
        private bool _menuOpen;
        private bool _autoCast;
        private bool _keepAfk = true;
        private bool _lowerThird = true, _playerList = true, _hud = true;
        private bool _nametags = true, _nametagVelocity, _minimap;
        private bool _letterbox, _hudHidden;

        // ui
        private Rect _winRect = new Rect(60, 60, 640, 500);
        private int _tab;
        private Vector2 _playerScroll, _contentScroll;
        private string _joinCode = "";

        // fps
        private float _fpsAccum; private int _fpsFrames; private float _fps, _fpsTimer;

        private readonly List<VRRig> _rigs = new List<VRRig>();

        // ============================================================ lifecycle

        private void Start()
        {
            try
            {
                _fov = Plugin.DefaultFov != null ? Plugin.DefaultFov.Value : 90f;
                if (Plugin.NametagsDefault != null) _nametags = Plugin.NametagsDefault.Value;
                if (Plugin.MinimapDefault != null) _minimap = Plugin.MinimapDefault.Value;
            }
            catch { }
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] Update: " + e.Message); }
        }

        private void LateUpdate()
        {
            try
            {
                if (_replay.Playing) _replay.ApplyPlayback(Time.deltaTime);
                DriveCamera();
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] LateUpdate: " + e.Message); }
        }

        // ============================================================ core

        private bool EnsureCamera()
        {
            if (_cam != null) return true;
            var tagger = GorillaTagger.Instance;
            if (tagger == null || tagger.thirdPersonCamera == null) return false;
            _cam = tagger.thirdPersonCamera.GetComponentInChildren<Camera>(true);
            if (_cam == null) return false;
            _brain = _cam.GetComponent<CinemachineBrain>();
            return true;
        }

        private static bool Pressed(Key k) { var kb = Keyboard.current; return kb != null && kb[k].wasPressedThisFrame; }

        private void Tick()
        {
            _fpsAccum += Time.unscaledDeltaTime; _fpsFrames++; _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f) { _fps = _fpsFrames / Mathf.Max(_fpsAccum, 0.0001f); _fpsAccum = 0; _fpsFrames = 0; _fpsTimer = 0; }

            var kb = Keyboard.current;
            if (kb == null) return;

            Key menuKey = Plugin.MenuKey != null ? Plugin.MenuKey.Value : Key.RightCtrl;
            Key modeKey = Plugin.ModeKey != null ? Plugin.ModeKey.Value : Key.P;
            Key shotKey = Plugin.ScreenshotKey != null ? Plugin.ScreenshotKey.Value : Key.F11;

            if (Pressed(menuKey)) _menuOpen = !_menuOpen;
            if (!EnsureCamera()) return;

            if (Pressed(modeKey)) CycleMode();
            if (Pressed(shotKey)) Screenshot();
            if (Pressed(Key.N)) CycleTarget(1);
            if (Pressed(Key.B)) CycleTarget(-1);
            if (Pressed(Key.K)) _dolly.Add(_cam.transform);
            if (Pressed(Key.L)) { if (_dolly.Playing) _dolly.Stop(); else _dolly.Play(); }
            if (Pressed(Key.F8)) _hudHidden = !_hudHidden;
            if (Pressed(Key.F6)) ToggleRecording();
            if (Pressed(Key.F7)) ToggleReplay();
            HandleNumberKeys(kb);

            if (_brain != null && _brain.enabled) _brain.enabled = false;
            if (_keepAfk && PhotonNetworkController.Instance != null)
                PhotonNetworkController.Instance.disableAFKKick = true;

            RefreshRigs();
            _replay.Sample(_rigs, Time.deltaTime);
            _goPro.Tick();

            if (_autoCast) AutoPickTarget();
            if (_target == null && _rigs.Count > 0) _target = _rigs[0];
        }

        private void DriveCamera()
        {
            if (_cam == null) return;
            _cam.fieldOfView = _fov;
            _cam.nearClipPlane = (_mode == CamMode.FirstPerson && _fpHideSelf) ? _fpNearClip : _nearClip;

            if (_dolly.Playing) { _dolly.Tick(_cam.transform, Time.deltaTime); return; }
            if (_mode == CamMode.FreeCam) { FreeCamMove(); return; }
            _freeInit = false;

            // GoPro = follow the physical prop's lens
            if (_mode == CamMode.GoPro)
            {
                if (!_goPro.Spawned) _goPro.SummonToHand();
                if (_goPro.Lens != null)
                    _cam.transform.SetPositionAndRotation(_goPro.Lens.position, _goPro.Lens.rotation);
                return;
            }

            if (_target == null) return;
            Transform head = _target.headMesh != null ? _target.headMesh.transform : _target.transform;
            Vector3 desiredPos; Quaternion desiredRot;

            switch (_mode)
            {
                case CamMode.FirstPerson:
                    desiredPos = head.position + head.forward * 0.06f;
                    desiredRot = head.rotation;
                    break;
                case CamMode.Selfie:
                    desiredPos = head.position + head.forward * _selfieDist + Vector3.up * 0.03f;
                    desiredRot = Quaternion.LookRotation(head.position - desiredPos, Vector3.up);
                    break;
                case CamMode.Tripod:
                    if (!_tripodSet) { _tripodPos = _cam.transform.position; _tripodSet = true; }
                    desiredPos = _tripodPos;
                    desiredRot = Quaternion.LookRotation((head.position + Vector3.up * 0.05f) - _tripodPos, Vector3.up);
                    break;
                default: // Follow + orbit
                    if (kbHeld(Key.Q)) _orbitAngle -= 60f * Time.deltaTime;
                    if (kbHeld(Key.E)) _orbitAngle += 60f * Time.deltaTime;
                    if (_orbit) _orbitAngle += _orbitSpeed * Time.deltaTime;
                    Vector3 behind = Quaternion.AngleAxis(_orbitAngle, Vector3.up) * (-head.forward);
                    behind.y = 0f;
                    if (behind.sqrMagnitude < 0.001f) behind = -head.forward;
                    behind.Normalize();
                    desiredPos = head.position + behind * _followDistance + Vector3.up * _followHeight;
                    desiredRot = Quaternion.LookRotation((head.position + Vector3.up * 0.05f) - desiredPos, Vector3.up);
                    break;
            }

            float pk = SmoothK(_moveSmoothing), rk = SmoothK(_rotSmoothing);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, desiredPos, pk);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, desiredRot, rk);
        }

        private static bool kbHeld(Key k) { var kb = Keyboard.current; return kb != null && kb[k].isPressed; }

        private static float SmoothK(float s)
        {
            s = Mathf.Clamp(s, 0f, 0.999f);
            return s <= 0f ? 1f : 1f - Mathf.Pow(s, Time.deltaTime * 60f);
        }

        private void FreeCamMove()
        {
            var t = _cam.transform;
            if (!_freeInit) { Vector3 e = t.rotation.eulerAngles; _pitch = e.x > 180f ? e.x - 360f : e.x; _yaw = e.y; _freeInit = true; }

            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null) return;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * 0.15f; _pitch = Mathf.Clamp(_pitch - d.y * 0.15f, -89f, 89f);
            }
            t.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            float speed = _freeSpeed * Time.deltaTime;
            if (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed) speed *= 3f;
            if (kb.leftAltKey.isPressed) speed *= 0.25f;
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += t.forward;
            if (kb.sKey.isPressed) move -= t.forward;
            if (kb.dKey.isPressed) move += t.right;
            if (kb.aKey.isPressed) move -= t.right;
            if (kb.spaceKey.isPressed) move += Vector3.up;
            if (kb.leftCtrlKey.isPressed) move -= Vector3.up;
            t.position += move * speed;

            if (mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _fov = Mathf.Clamp(_fov - scroll * 0.02f, 10f, 120f);
            }
        }

        // ============================================================ players / replay

        private void RefreshRigs()
        {
            _rigs.Clear();
            var containers = VRRigCache.ActiveRigContainers;
            if (containers != null)
                foreach (var c in containers)
                    if (c != null && c.Rig != null) _rigs.Add(c.Rig);
            var local = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;
            if (local != null && !_rigs.Contains(local)) _rigs.Add(local);
        }

        private void HandleNumberKeys(Keyboard kb)
        {
            var keys = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key,
                               kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key, kb.digit0Key };
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].wasPressedThisFrame) { SelectIndex(i); return; }
        }

        private void SelectIndex(int i) { if (i >= 0 && i < _rigs.Count) { _target = _rigs[i]; _autoCast = false; } }

        private void CycleTarget(int dir)
        {
            if (_rigs.Count == 0) return;
            int idx = _rigs.IndexOf(_target);
            idx = ((idx + dir) % _rigs.Count + _rigs.Count) % _rigs.Count;
            _target = _rigs[idx]; _autoCast = false;
        }

        private void AutoPickTarget() { foreach (var r in _rigs) if (CasterUtil.IsTagged(r)) { _target = r; return; } }

        private void SetMode(CamMode m)
        {
            _mode = m; _freeInit = false;
            if (m == CamMode.Tripod) _tripodSet = false;
            if (m == CamMode.GoPro && !_goPro.Spawned) _goPro.SummonToHand();
        }

        private void CycleMode() => SetMode((CamMode)(((int)_mode + 1) % ModeNames.Length));

        private void ToggleRecording() { if (_replay.Recording) _replay.StopRecording(); else _replay.StartRecording(); }
        private void ToggleReplay() { if (_replay.Playing) _replay.Stop(); else _replay.Play(0f); }

        private void Screenshot()
        {
            try
            {
                string dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                string path = Path.Combine(dir, $"GorillaCaster_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                ScreenCapture.CaptureScreenshot(path, 1);
                Debug.Log("[GorillaCaster] Screenshot -> " + path);
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] screenshot: " + e.Message); }
        }

        // ============================================================ world

        private void SetTime(int i) { var dn = BetterDayNightManager.instance; if (dn != null) dn.SetTimeOfDay(i); }

        private void SetWeather(BetterDayNightManager.WeatherType w)
        {
            var dn = BetterDayNightManager.instance; if (dn == null) return;
            if (w == BetterDayNightManager.WeatherType.None) dn.ClearFixedWeather(); else dn.SetFixedWeather(w);
        }

        private void JoinRoom()
        {
            string code = (_joinCode ?? "").ToUpperInvariant().Trim();
            if (code.Length == 0 || PhotonNetworkController.Instance == null) return;
            PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(code, JoinType.Solo);
        }

        private void LeaveRoom() { if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom(); }

        // ============================================================ GUI

        private void OnGUI()
        {
            try
            {
                Styles.Ensure();
                if (_letterbox) DrawLetterbox();
                if (!_hudHidden)
                {
                    if (_nametags && _cam != null) HudExtras.DrawNametags(_rigs, _cam, _target, _nametagVelocity);
                    if (_minimap) HudExtras.DrawMinimap(new Rect(Screen.width - 210, 40, 200, 160), _rigs, _target);
                    if (_hud) DrawHud();
                    if (_playerList) DrawPlayerList();
                    if (_lowerThird && _target != null) DrawLowerThird();
                    if (_replay.Recording) DrawRecIndicator();
                    if (_replay.Playing || _replay.Length > 0.01f) DrawReplayBar();
                }
                if (_menuOpen) _winRect = GUI.Window(0xCA57, _winRect, DrawWindow, "");
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] OnGUI: " + e.Message); }
        }

        private void DrawLetterbox()
        {
            float bar = Screen.height * 0.11f;
            Styles.Fill(new Rect(0, 0, Screen.width, bar), Color.black);
            Styles.Fill(new Rect(0, Screen.height - bar, Screen.width, bar), Color.black);
        }

        private void DrawHud()
        {
            string spd = _target != null ? $" · {CasterUtil.Speed(_target):0.0} m/s" : "";
            string extra = _mode == CamMode.GoPro ? (_goPro.Held ? " · HELD" : " · placed") : (_dolly.Playing ? " · DOLLY" : "");
            GUI.Label(new Rect(12, 10, 420, 22), $"<color=#5cc8ff>●</color> {_fps:0} FPS · {_mode}{spd}{extra}", Styles.Hud);
        }

        private void DrawPlayerList()
        {
            if (_rigs.Count == 0) return;
            float y = 40;
            GUI.Label(new Rect(12, y, 220, 18), "PLAYERS", Styles.Header); y += 20;
            for (int i = 0; i < _rigs.Count && i < 12; i++)
            {
                var r = _rigs[i]; bool sel = r == _target; bool it = CasterUtil.IsTagged(r);
                Styles.Fill(new Rect(8, y, 8, 14), r.playerColor);
                var st = new GUIStyle(Styles.Hud) { fontStyle = sel ? FontStyle.Bold : FontStyle.Normal, fontSize = 12 };
                st.normal.textColor = sel ? Color.green : (it ? new Color(1f, 0.5f, 0.5f) : Color.white);
                GUI.Label(new Rect(20, y - 2, 240, 18), $"[{(i + 1) % 10}] {CasterUtil.NameOf(r)}" + (it ? "  [IT]" : ""), st);
                y += 18;
            }
        }

        private void DrawLowerThird()
        {
            bool it = CasterUtil.IsTagged(_target);
            float w = 420, h = 64;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - h - 28, w, h);
            Styles.Fill(r, new Color(0.07f, 0.08f, 0.10f, 0.92f));
            Styles.Fill(new Rect(r.x, r.y, 8f, r.height), _target.playerColor);
            Styles.Fill(new Rect(r.x, r.yMax - 3f, r.width, 3f), it ? Styles.Accent2 : Styles.Accent);
            GUI.Label(new Rect(r.x + 22, r.y + 8, r.width - 30, 30), CasterUtil.NameOf(_target), Styles.LowerThirdName);
            string sub = it ? "<color=#ff5577>● IT</color>   NOW CASTING" : "NOW CASTING";
            GUI.Label(new Rect(r.x + 22, r.y + 38, r.width - 30, 20), sub, Styles.LowerThirdSub);
        }

        private void DrawRecIndicator()
        {
            GUI.Label(new Rect(Screen.width - 120, 12, 110, 22), $"<color=#ff4040>● REC</color> {_replay.Length:0}s",
                new GUIStyle(Styles.Hud) { fontSize = 14 });
        }

        private void DrawReplayBar()
        {
            float w = 560, h = 46;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - h - 6, w, h);
            Styles.Fill(r, new Color(0.05f, 0.06f, 0.08f, 0.9f));
            Styles.Fill(new Rect(r.x, r.y, w, 2f), Styles.Accent);
            if (GUI.Button(new Rect(r.x + 8, r.y + 10, 60, 26), _replay.Playing ? "Pause" : "Play", Styles.Btn))
                { if (_replay.Playing) _replay.Stop(); else _replay.Play(_replay.Playhead >= _replay.Length ? 0f : _replay.Playhead); }
            float v = GUI.HorizontalSlider(new Rect(r.x + 78, r.y + 22, w - 230, 12), _replay.Playhead, 0f, Mathf.Max(0.01f, _replay.Length), Styles.Slider, Styles.SliderThumb);
            if (Mathf.Abs(v - _replay.Playhead) > 0.005f) { _replay.Stop(); _replay.Playhead = v; _replay.ApplyAt(v); }
            GUI.Label(new Rect(r.xMax - 140, r.y + 14, 70, 20), $"{_replay.Playhead:0.0}/{_replay.Length:0.0}s", Styles.Hud);
            if (GUI.Button(new Rect(r.xMax - 64, r.y + 10, 56, 26), "Live", Styles.Btn)) { _replay.Stop(); _replay.Playhead = 0f; }
        }

        // ----- menu window with sidebar rail -----

        private void DrawWindow(int id)
        {
            float W = _winRect.width, H = _winRect.height;
            Styles.Fill(new Rect(0, 0, W, H), Styles.BgCol);

            // title bar
            Styles.Fill(new Rect(0, 0, W, 30), new Color(0.04f, 0.05f, 0.06f, 1f));
            GUI.Label(new Rect(14, 6, 300, 20), "GORILLA<color=#5cc8ff>CASTER</color>  <size=10>v" + Plugin.Version + "</size>", Styles.Brand);
            if (GUI.Button(new Rect(W - 30, 4, 24, 22), "✕", Styles.Btn)) _menuOpen = false;
            Styles.Fill(new Rect(0, 30, W, 2), Styles.Accent);

            // sidebar rail
            float railW = 130, top = 38;
            Styles.Fill(new Rect(0, 32, railW, H - 32), Styles.RailCol);
            for (int i = 0; i < TabNames.Length; i++)
            {
                var br = new Rect(6, top + i * 38, railW - 12, 32);
                bool on = _tab == i;
                if (GUI.Button(br, TabNames[i], on ? Styles.RailBtnOn : Styles.RailBtn)) { _tab = i; _contentScroll = Vector2.zero; }
                if (on) Styles.Fill(new Rect(0, br.y, 3, br.height), Styles.Accent);
            }

            // content
            var content = new Rect(railW + 12, top, W - railW - 24, H - top - 12);
            GUILayout.BeginArea(content);
            _contentScroll = GUILayout.BeginScrollView(_contentScroll);
            switch (_tab)
            {
                case 0: CameraTab(); break;
                case 1: GoProTab(); break;
                case 2: PlayersTab(); break;
                case 3: ReplayTab(); break;
                case 4: DirectorTab(); break;
                case 5: WorldTab(); break;
                case 6: OverlaysTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.DragWindow(new Rect(0, 0, W, 30));
        }

        private void CameraTab()
        {
            UI.Header("Mode  ·  P to cycle");
            int m = GUILayout.SelectionGrid((int)_mode, ModeNames, 3, Styles.Btn, GUILayout.Height(60));
            if (m != (int)_mode) SetMode((CamMode)m);

            UI.Header("Lens");
            _fov = UI.Slider("Field of View", _fov, 10f, 120f, "0");
            GUILayout.BeginHorizontal();
            if (UI.SmallButton("110", 56)) _fov = 110;
            if (UI.SmallButton("90", 56)) _fov = 90;
            if (UI.SmallButton("60", 56)) _fov = 60;
            if (UI.SmallButton("35", 56)) _fov = 35;
            GUILayout.EndHorizontal();
            _nearClip = UI.Slider("Near Clip", _nearClip, 0.01f, 0.6f);

            if (_mode == CamMode.Follow)
            {
                UI.Header("Follow");
                _followDistance = UI.Slider("Distance", _followDistance, 0f, 4f);
                _followHeight = UI.Slider("Height", _followHeight, -1f, 1.5f);
                _orbit = UI.Toggle("Auto-orbit  (Q/E manual)", _orbit);
                if (_orbit) _orbitSpeed = UI.Slider("Orbit Speed", _orbitSpeed, -120f, 120f, "0");
            }
            else if (_mode == CamMode.FirstPerson)
            {
                UI.Header("First Person");
                _fpHideSelf = UI.Toggle("Hide my cosmetics (clip near)", _fpHideSelf);
                if (_fpHideSelf) _fpNearClip = UI.Slider("Hide strength", _fpNearClip, 0.1f, 0.6f);
                UI.Note("Bumps the casting camera's near-clip so your own head cosmetics vanish from the broadcast — your VR view is untouched.");
            }
            else if (_mode == CamMode.Selfie)
            {
                UI.Header("Selfie");
                _selfieDist = UI.Slider("Distance", _selfieDist, 0.2f, 2f);
            }
            else if (_mode == CamMode.Tripod)
            {
                UI.Header("Tripod");
                if (UI.Button("Re-plant here (current view)")) _tripodSet = false;
                UI.Note("Stays put and tracks the cast player.");
            }
            else if (_mode == CamMode.GoPro)
            {
                UI.Note("GoPro camera renders from the grabbable prop — see the GoPro tab.");
            }

            GUILayout.Space(6);
            if (UI.Button("📸  Screenshot  (F11)")) Screenshot();
        }

        private void GoProTab()
        {
            UI.Header("Grabbable GoPro");
            UI.Note("A physical GoPro you can grab with grip and place anywhere. Drop it for a static shot, or hold it for a moving one. Switch the camera to GoPro mode to broadcast its view.");

            GUILayout.Space(4);
            GUILayout.Label(_goPro.Spawned ? (_goPro.Held ? "Status:  <color=#5cf08a>held</color>" : "Status:  <color=#5cc8ff>placed</color>") : "Status:  not spawned", Styles.Hud);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (UI.SmallButton(_goPro.Spawned ? "Summon to hand" : "Spawn", 150)) _goPro.SummonToHand();
            if (UI.SmallButton("Despawn", 110)) _goPro.Despawn();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            if (UI.Button(_mode == CamMode.GoPro ? "● Broadcasting GoPro" : "Use GoPro as camera")) SetMode(CamMode.GoPro);

            UI.Header("How to grab");
            UI.Note("Reach a hand to the GoPro and squeeze GRIP to pick it up. Release grip to drop it — it stays floating exactly where you let go.");
        }

        private void PlayersTab()
        {
            _autoCast = UI.Toggle("Auto-cast the tagged player", _autoCast);
            UI.Header("Cast  ·  1-0 / N / B");
            _playerScroll = GUILayout.BeginScrollView(_playerScroll, GUILayout.Height(300));
            for (int i = 0; i < _rigs.Count; i++)
            {
                var r = _rigs[i]; bool sel = r == _target;
                GUILayout.BeginHorizontal();
                Styles.Fill(GUILayoutUtility.GetRect(10, 22, GUILayout.Width(10)), r.playerColor);
                string tag = CasterUtil.IsTagged(r) ? "  <color=#ff5577>[IT]</color>" : "";
                var st = new GUIStyle(Styles.Btn); if (sel) st.normal.textColor = new Color(0.36f, 0.94f, 0.54f);
                if (GUILayout.Button($"[{(i + 1) % 10}] {CasterUtil.NameOf(r)}{tag}" + (sel ? "  ◄" : ""), st, GUILayout.Height(26)))
                    SelectIndex(i);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void ReplayTab()
        {
            UI.Header("Instant Replay  ·  F6 / F7");
            GUILayout.Label(_replay.Recording ? $"<color=#ff5577>● Recording</color> — {_replay.Length:0}s"
                : (_replay.Length > 0.01f ? $"{_replay.Length:0}s buffered" : "Not recording."), Styles.Hud);
            UI.Note("Buffers every player's motion, then replays it so you can re-show and slow-mo a moment while flying the camera freely.");

            GUILayout.BeginHorizontal();
            if (UI.SmallButton(_replay.Recording ? "Stop Rec" : "Record", 120)) ToggleRecording();
            if (UI.SmallButton("Clear", 90)) _replay.Clear();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (UI.SmallButton(_replay.Playing ? "Stop" : "Play", 120)) ToggleReplay();
            if (UI.SmallButton("Restart", 90)) _replay.Play(0f);
            GUILayout.EndHorizontal();

            UI.Header("Speed");
            _replay.Speed = UI.Slider("Playback", _replay.Speed, 0.1f, 2f);
            GUILayout.BeginHorizontal();
            if (UI.SmallButton("0.25x", 70)) _replay.Speed = 0.25f;
            if (UI.SmallButton("0.5x", 70)) _replay.Speed = 0.5f;
            if (UI.SmallButton("1x", 70)) _replay.Speed = 1f;
            GUILayout.EndHorizontal();

            _replay.BufferSeconds = UI.Slider("Buffer length (s)", _replay.BufferSeconds, 5f, 90f, "0");
            UI.Note("During playback, drag the bar at the bottom of the screen to scrub.");
        }

        private void DirectorTab()
        {
            UI.Header("Cinematic Dolly  ·  K / L");
            GUILayout.Label($"Keyframes: {_dolly.Count}", Styles.Hud);
            UI.Note("Fly to a spot in FreeCam, Add Keyframe, repeat, then Play for a smooth camera move.");
            GUILayout.BeginHorizontal();
            if (UI.SmallButton("Add", 80)) _dolly.Add(_cam != null ? _cam.transform : transform);
            if (UI.SmallButton("Undo", 80)) _dolly.RemoveLast();
            if (UI.SmallButton("Clear", 80)) _dolly.Clear();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (UI.SmallButton(_dolly.Playing ? "Stop" : "Play", 100)) { if (_dolly.Playing) _dolly.Stop(); else _dolly.Play(); }
            GUILayout.EndHorizontal();
            _dolly.Loop = UI.Toggle("Loop", _dolly.Loop);
            _dolly.SecondsPerSegment = UI.Slider("Seconds / segment", _dolly.SecondsPerSegment, 0.3f, 8f, "0.0");
        }

        private void WorldTab()
        {
            UI.Header("Time of Day");
            GUILayout.BeginHorizontal();
            if (UI.SmallButton("Night", 80)) SetTime(0);
            if (UI.SmallButton("Morning", 80)) SetTime(1);
            if (UI.SmallButton("Noon", 80)) SetTime(3);
            if (UI.SmallButton("Evening", 80)) SetTime(7);
            GUILayout.EndHorizontal();

            UI.Header("Weather");
            GUILayout.BeginHorizontal();
            if (UI.SmallButton("Clear", 90)) SetWeather(BetterDayNightManager.WeatherType.None);
            if (UI.SmallButton("Rain", 90)) SetWeather(BetterDayNightManager.WeatherType.Raining);
            GUILayout.EndHorizontal();

            UI.Header("Join Room");
            GUILayout.BeginHorizontal();
            _joinCode = GUILayout.TextField(_joinCode, 12, GUILayout.Width(150), GUILayout.Height(26));
            if (UI.SmallButton("Join", 70)) JoinRoom();
            if (UI.SmallButton("Leave", 70)) LeaveRoom();
            GUILayout.EndHorizontal();
            _keepAfk = UI.Toggle("Disable AFK kick", _keepAfk);
        }

        private void OverlaysTab()
        {
            UI.Header("Overlays  ·  F8 hides all");
            _lowerThird = UI.Toggle("Now-casting lower third", _lowerThird);
            _playerList = UI.Toggle("Player list", _playerList);
            _nametags = UI.Toggle("Floating nametags", _nametags);
            _nametagVelocity = UI.Toggle("Speed on nametags", _nametagVelocity);
            _minimap = UI.Toggle("Overhead minimap", _minimap);
            _hud = UI.Toggle("FPS / mode / speed readout", _hud);
            _letterbox = UI.Toggle("Cinematic letterbox bars", _letterbox);

            UI.Header("Movement Smoothing");
            _moveSmoothing = UI.Slider("Position", _moveSmoothing, 0f, 0.95f);
            _rotSmoothing = UI.Slider("Rotation", _rotSmoothing, 0f, 0.95f);
            _freeSpeed = UI.Slider("FreeCam fly speed", _freeSpeed, 1f, 25f, "0.0");
        }
    }
}
