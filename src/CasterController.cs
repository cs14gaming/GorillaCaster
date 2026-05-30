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
    /// Drives Gorilla Tag's third-person / shoulder camera (the desktop / spectator view)
    /// for casting: follow / freecam / first-person / GoPro / tripod / selfie camera modes,
    /// cinematic dolly paths, an instant-replay system, time-of-day control, and a full set
    /// of stream overlays. The VR headset view is never touched.
    /// </summary>
    public class CasterController : MonoBehaviour
    {
        private enum CamMode { Follow, FreeCam, FirstPerson, GoPro, Tripod, Selfie }
        private enum Mount { Head, RightHand, LeftHand, Body }

        private static readonly string[] ModeNames =
            { "Follow", "FreeCam", "First Person", "GoPro", "Tripod", "Selfie" };

        // ---- camera ----
        private Camera _cam;
        private CinemachineBrain _brain;
        private CamMode _mode = CamMode.Follow;
        private VRRig _target;

        // tunables
        private float _fov = 90f;
        private float _nearClip = 0.05f;
        private float _followDistance = 1.4f;
        private float _followHeight = 0.25f;
        private float _moveSmoothing = 0.45f;
        private float _rotSmoothing = 0.45f;
        private float _freeSpeed = 6f;

        // orbit
        private bool _orbit;
        private float _orbitSpeed = 25f;
        private float _orbitAngle;

        // gopro / tripod / selfie
        private Mount _goProMount = Mount.Head;
        private float _goProFwd = 0.12f, _goProUp = 0.0f, _goProSide = 0.0f;
        private Vector3 _tripodPos;
        private bool _tripodSet;
        private float _selfieDist = 0.6f;

        // freecam look
        private bool _freeInit;
        private float _yaw, _pitch;

        // director + replay
        private readonly DollyPath _dolly = new DollyPath();
        private readonly ReplayRecorder _replay = new ReplayRecorder();

        // toggles
        private bool _menuOpen;
        private bool _autoCast;
        private bool _keepAfk = true;
        private bool _lowerThird = true;
        private bool _playerList = true;
        private bool _hud = true;
        private bool _nametags = true;
        private bool _nametagVelocity;
        private bool _minimap;
        private bool _letterbox;
        private bool _hudHidden;       // F8 master hide for clean capture

        // ui
        private Rect _winRect = new Rect(40, 40, 470, 520);
        private int _tab;
        private readonly string[] _tabs = { "Camera", "Move", "Players", "World", "Director", "Replay" };
        private Vector2 _playerScroll;
        private string _joinCode = "";

        // fps
        private float _fpsAccum;
        private int _fpsFrames;
        private float _fps;
        private float _fpsTimer;

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

        private static bool Pressed(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].wasPressedThisFrame;
        }

        private void Tick()
        {
            _fpsAccum += Time.unscaledDeltaTime;
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrames / Mathf.Max(_fpsAccum, 0.0001f);
                _fpsAccum = 0f; _fpsFrames = 0; _fpsTimer = 0f;
            }

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

            if (_autoCast) AutoPickTarget();
            if (_target == null && _rigs.Count > 0) _target = _rigs[0];
        }

        private void DriveCamera()
        {
            if (_cam == null) return;
            _cam.fieldOfView = _fov;
            _cam.nearClipPlane = _nearClip;

            if (_dolly.Playing) { _dolly.Tick(_cam.transform, Time.deltaTime); return; }
            if (_mode == CamMode.FreeCam) { FreeCamMove(); return; }
            _freeInit = false;
            if (_target == null) return;

            Transform head = _target.headMesh != null ? _target.headMesh.transform : _target.transform;

            // GoPro = hard-mounted to a player bone, no smoothing
            if (_mode == CamMode.GoPro)
            {
                Transform m = MountFor(_target) ?? head;
                _cam.transform.position = m.position + m.forward * _goProFwd + m.up * _goProUp + m.right * _goProSide;
                _cam.transform.rotation = m.rotation;
                return;
            }

            Vector3 desiredPos;
            Quaternion desiredRot;

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

                default: // Follow with optional orbit
                    var kb = Keyboard.current;
                    if (kb != null)
                    {
                        if (kb.qKey.isPressed) _orbitAngle -= 60f * Time.deltaTime;
                        if (kb.eKey.isPressed) _orbitAngle += 60f * Time.deltaTime;
                    }
                    if (_orbit) _orbitAngle += _orbitSpeed * Time.deltaTime;

                    Vector3 behind = Quaternion.AngleAxis(_orbitAngle, Vector3.up) * (-head.forward);
                    behind.y = 0f;
                    if (behind.sqrMagnitude < 0.001f) behind = -head.forward;
                    behind.Normalize();
                    desiredPos = head.position + behind * _followDistance + Vector3.up * _followHeight;
                    desiredRot = Quaternion.LookRotation((head.position + Vector3.up * 0.05f) - desiredPos, Vector3.up);
                    break;
            }

            float pk = SmoothK(_moveSmoothing);
            float rk = SmoothK(_rotSmoothing);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, desiredPos, pk);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, desiredRot, rk);
        }

        private Transform MountFor(VRRig r)
        {
            switch (_goProMount)
            {
                case Mount.RightHand: return r.rightHandTransform;
                case Mount.LeftHand: return r.leftHandTransform;
                case Mount.Body: return r.bodyTransform;
                default: return r.headMesh != null ? r.headMesh.transform : r.transform;
            }
        }

        private static float SmoothK(float smoothing)
        {
            smoothing = Mathf.Clamp(smoothing, 0f, 0.999f);
            if (smoothing <= 0f) return 1f;
            return 1f - Mathf.Pow(smoothing, Time.deltaTime * 60f);
        }

        private void FreeCamMove()
        {
            var t = _cam.transform;
            if (!_freeInit)
            {
                Vector3 e = t.rotation.eulerAngles;
                _pitch = e.x > 180f ? e.x - 360f : e.x;
                _yaw = e.y;
                _freeInit = true;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (mouse != null && mouse.rightButton.isPressed)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * 0.15f;
                _pitch = Mathf.Clamp(_pitch - d.y * 0.15f, -89f, 89f);
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
                if (Mathf.Abs(scroll) > 0.01f)
                    _fov = Mathf.Clamp(_fov - scroll * 0.02f, 10f, 120f);
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
            var keys = new[]
            {
                kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key,
                kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key, kb.digit0Key
            };
            for (int i = 0; i < keys.Length; i++)
                if (keys[i].wasPressedThisFrame) { SelectIndex(i); return; }
        }

        private void SelectIndex(int i)
        {
            if (i >= 0 && i < _rigs.Count) { _target = _rigs[i]; _autoCast = false; }
        }

        private void CycleTarget(int dir)
        {
            if (_rigs.Count == 0) return;
            int idx = _rigs.IndexOf(_target);
            idx = ((idx + dir) % _rigs.Count + _rigs.Count) % _rigs.Count;
            _target = _rigs[idx];
            _autoCast = false;
        }

        private void AutoPickTarget()
        {
            foreach (var r in _rigs)
                if (CasterUtil.IsTagged(r)) { _target = r; return; }
        }

        private void CycleMode()
        {
            _mode = (CamMode)(((int)_mode + 1) % ModeNames.Length);
            _freeInit = false;
            if (_mode == CamMode.Tripod) _tripodSet = false; // re-plant where we are
        }

        private void ToggleRecording()
        {
            if (_replay.Recording) _replay.StopRecording();
            else _replay.StartRecording();
        }

        private void ToggleReplay()
        {
            if (_replay.Playing) _replay.Stop();
            else _replay.Play(0f);
        }

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

        private void SetTime(int index)
        {
            var dn = BetterDayNightManager.instance;
            if (dn != null) dn.SetTimeOfDay(index);
        }

        private void SetWeather(BetterDayNightManager.WeatherType w)
        {
            var dn = BetterDayNightManager.instance;
            if (dn == null) return;
            if (w == BetterDayNightManager.WeatherType.None) dn.ClearFixedWeather();
            else dn.SetFixedWeather(w);
        }

        private void JoinRoom()
        {
            string code = (_joinCode ?? "").ToUpperInvariant().Trim();
            if (code.Length == 0 || PhotonNetworkController.Instance == null) return;
            PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(code, JoinType.Solo);
        }

        private void LeaveRoom()
        {
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        }

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

                if (_menuOpen)
                    _winRect = GUI.Window(0xCA57, _winRect, DrawWindow, "");
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
            GUI.Label(new Rect(12, 10, 360, 22),
                $"<color=#5cc8ff>●</color> {_fps:0} FPS · {_mode}{spd}" + (_dolly.Playing ? " · DOLLY" : ""), Styles.Hud);
        }

        private void DrawPlayerList()
        {
            if (_rigs.Count == 0) return;
            float y = 40;
            GUI.Label(new Rect(12, y, 220, 18), "PLAYERS", Styles.Header);
            y += 20;
            for (int i = 0; i < _rigs.Count && i < 12; i++)
            {
                var r = _rigs[i];
                bool sel = r == _target;
                bool it = CasterUtil.IsTagged(r);
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
            Styles.Fill(new Rect(r.x, r.yMax - 3f, r.width, 3f), it ? new Color(1f, 0.25f, 0.25f) : Styles.BrandAccent);
            GUI.Label(new Rect(r.x + 22, r.y + 8, r.width - 30, 30), CasterUtil.NameOf(_target), Styles.LowerThirdName);
            string sub = it ? "<color=#ff5555>● IT</color>   NOW CASTING" : "NOW CASTING";
            GUI.Label(new Rect(r.x + 22, r.y + 38, r.width - 30, 20), sub, Styles.LowerThirdSub);
        }

        private void DrawRecIndicator()
        {
            var s = new GUIStyle(Styles.Hud) { fontSize = 14 };
            GUI.Label(new Rect(Screen.width - 110, 12, 100, 22), $"<color=#ff4040>● REC</color> {_replay.Length:0}s", s);
        }

        private void DrawReplayBar()
        {
            float w = 560, h = 46;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - h - 6, w, h);
            Styles.Fill(r, new Color(0.05f, 0.06f, 0.08f, 0.9f));
            Styles.Fill(new Rect(r.x, r.y, w, 2f), Styles.BrandAccent);

            if (GUI.Button(new Rect(r.x + 8, r.y + 10, 60, 26), _replay.Playing ? "Pause" : "Play"))
                { if (_replay.Playing) _replay.Stop(); else _replay.Play(_replay.Playhead >= _replay.Length ? 0f : _replay.Playhead); }

            float v = GUI.HorizontalSlider(new Rect(r.x + 78, r.y + 22, w - 230, 12), _replay.Playhead, 0f, Mathf.Max(0.01f, _replay.Length));
            if (Mathf.Abs(v - _replay.Playhead) > 0.005f) { _replay.Stop(); _replay.Playhead = v; _replay.ApplyAt(v); }

            GUI.Label(new Rect(r.xMax - 140, r.y + 14, 70, 20), $"{_replay.Playhead:0.0}/{_replay.Length:0.0}s", Styles.Hud);
            if (GUI.Button(new Rect(r.xMax - 64, r.y + 10, 56, 26), "Live")) { _replay.Stop(); _replay.Playhead = 0f; }
        }

        // ----- menu window -----

        private void DrawWindow(int id)
        {
            Styles.Fill(new Rect(0, 0, _winRect.width, _winRect.height), new Color(0.10f, 0.11f, 0.13f, 0.97f));
            Styles.Fill(new Rect(0, 0, _winRect.width, 26), new Color(0.06f, 0.07f, 0.09f, 1f));
            GUI.Label(new Rect(12, 4, 320, 20), "<b>GorillaCaster</b>  <size=10>v" + Plugin.Version + "</size>", Styles.Header);
            Styles.Fill(new Rect(0, 26, _winRect.width, 2), Styles.BrandAccent);

            _tab = GUI.Toolbar(new Rect(10, 34, _winRect.width - 20, 26), _tab, _tabs);
            GUILayout.BeginArea(new Rect(14, 68, _winRect.width - 28, _winRect.height - 78));
            switch (_tab)
            {
                case 0: CameraTab(); break;
                case 1: MoveTab(); break;
                case 2: PlayersTab(); break;
                case 3: WorldTab(); break;
                case 4: DirectorTab(); break;
                case 5: ReplayTab(); break;
            }
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, _winRect.width, 26));
        }

        private void CameraTab()
        {
            GUILayout.Label("MODE  (P to cycle)", Styles.Header);
            int m = GUILayout.SelectionGrid((int)_mode, ModeNames, 3);
            if (m != (int)_mode) { _mode = (CamMode)m; _freeInit = false; if (_mode == CamMode.Tripod) _tripodSet = false; }

            GUILayout.Space(6);
            GUILayout.Label($"Field of View: {_fov:0}");
            _fov = GUILayout.HorizontalSlider(_fov, 10f, 120f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("110")) _fov = 110;
            if (GUILayout.Button("90")) _fov = 90;
            if (GUILayout.Button("60")) _fov = 60;
            if (GUILayout.Button("35")) _fov = 35;
            GUILayout.EndHorizontal();

            GUILayout.Label($"Near Clip: {_nearClip:0.00}");
            _nearClip = GUILayout.HorizontalSlider(_nearClip, 0.01f, 0.6f);

            if (_mode == CamMode.Follow)
            {
                GUILayout.Label($"Follow Distance: {_followDistance:0.00}");
                _followDistance = GUILayout.HorizontalSlider(_followDistance, 0f, 4f);
                GUILayout.Label($"Follow Height: {_followHeight:0.00}");
                _followHeight = GUILayout.HorizontalSlider(_followHeight, -1f, 1.5f);
                _orbit = GUILayout.Toggle(_orbit, "  Auto-orbit (Q/E manual)");
                if (_orbit) { GUILayout.Label($"Orbit Speed: {_orbitSpeed:0}"); _orbitSpeed = GUILayout.HorizontalSlider(_orbitSpeed, -120f, 120f); }
            }
            else if (_mode == CamMode.GoPro)
            {
                GUILayout.Label("Mount point:");
                _goProMount = (Mount)GUILayout.SelectionGrid((int)_goProMount, new[] { "Head", "R.Hand", "L.Hand", "Body" }, 4);
                GUILayout.Label($"Forward: {_goProFwd:0.00}"); _goProFwd = GUILayout.HorizontalSlider(_goProFwd, -0.5f, 0.5f);
                GUILayout.Label($"Up: {_goProUp:0.00}"); _goProUp = GUILayout.HorizontalSlider(_goProUp, -0.5f, 0.5f);
                GUILayout.Label($"Side: {_goProSide:0.00}"); _goProSide = GUILayout.HorizontalSlider(_goProSide, -0.5f, 0.5f);
            }
            else if (_mode == CamMode.Selfie)
            {
                GUILayout.Label($"Distance: {_selfieDist:0.00}");
                _selfieDist = GUILayout.HorizontalSlider(_selfieDist, 0.2f, 2f);
            }
            else if (_mode == CamMode.Tripod)
            {
                if (GUILayout.Button("Re-plant tripod here (current view)")) _tripodSet = false;
                GUILayout.Label("Tripod stays put and tracks the cast player.", GUI.skin.box);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Screenshot (F11)")) Screenshot();
        }

        private void MoveTab()
        {
            GUILayout.Label("SMOOTHING", Styles.Header);
            GUILayout.Label($"Position: {_moveSmoothing:0.00}");
            _moveSmoothing = GUILayout.HorizontalSlider(_moveSmoothing, 0f, 0.95f);
            GUILayout.Label($"Rotation: {_rotSmoothing:0.00}");
            _rotSmoothing = GUILayout.HorizontalSlider(_rotSmoothing, 0f, 0.95f);

            GUILayout.Space(10);
            GUILayout.Label("FREECAM", Styles.Header);
            GUILayout.Label($"Fly Speed: {_freeSpeed:0.0}");
            _freeSpeed = GUILayout.HorizontalSlider(_freeSpeed, 1f, 25f);
            GUILayout.Label("WASD move · Space/Ctrl up-down · Shift x3 · Alt slow", GUI.skin.box);
            GUILayout.Label("Hold RIGHT-MOUSE to look · scroll = zoom", GUI.skin.box);
        }

        private void PlayersTab()
        {
            _autoCast = GUILayout.Toggle(_autoCast, "  Auto-cast the tagged player");
            GUILayout.Label("Click to cast (or press 1-0):");
            _playerScroll = GUILayout.BeginScrollView(_playerScroll, GUILayout.Height(260));
            for (int i = 0; i < _rigs.Count; i++)
            {
                var r = _rigs[i];
                bool sel = r == _target;
                var style = new GUIStyle(GUI.skin.button);
                if (sel) style.normal.textColor = Color.green;
                string tag = CasterUtil.IsTagged(r) ? "  [IT]" : "";
                if (GUILayout.Button($"[{(i + 1) % 10}] {CasterUtil.NameOf(r)}{tag}" + (sel ? "  ◄" : ""), style))
                    SelectIndex(i);
            }
            GUILayout.EndScrollView();
            GUILayout.Label("N / B cycle next / previous", GUI.skin.box);
        }

        private void WorldTab()
        {
            GUILayout.Label("TIME OF DAY", Styles.Header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Night")) SetTime(0);
            if (GUILayout.Button("Morning")) SetTime(1);
            if (GUILayout.Button("Noon")) SetTime(3);
            if (GUILayout.Button("Evening")) SetTime(7);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("WEATHER", Styles.Header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear")) SetWeather(BetterDayNightManager.WeatherType.None);
            if (GUILayout.Button("Rain")) SetWeather(BetterDayNightManager.WeatherType.Raining);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("OVERLAYS  (F8 hides all)", Styles.Header);
            _lowerThird = GUILayout.Toggle(_lowerThird, "  Now-casting lower third");
            _playerList = GUILayout.Toggle(_playerList, "  Player list");
            _nametags = GUILayout.Toggle(_nametags, "  Floating nametags");
            _nametagVelocity = GUILayout.Toggle(_nametagVelocity, "  Show speed on nametags");
            _minimap = GUILayout.Toggle(_minimap, "  Overhead minimap");
            _hud = GUILayout.Toggle(_hud, "  FPS / mode / speed readout");
            _letterbox = GUILayout.Toggle(_letterbox, "  Cinematic letterbox bars");
            _keepAfk = GUILayout.Toggle(_keepAfk, "  Disable AFK kick");

            GUILayout.Space(8);
            GUILayout.Label("JOIN ROOM", Styles.Header);
            GUILayout.BeginHorizontal();
            _joinCode = GUILayout.TextField(_joinCode, 12, GUILayout.Width(160));
            if (GUILayout.Button("Join", GUILayout.Width(70))) JoinRoom();
            if (GUILayout.Button("Leave", GUILayout.Width(70))) LeaveRoom();
            GUILayout.EndHorizontal();
        }

        private void DirectorTab()
        {
            GUILayout.Label("CINEMATIC DOLLY", Styles.Header);
            GUILayout.Label($"Keyframes: {_dolly.Count}    (K add · L play/stop)");
            GUILayout.Label("Fly to a spot in FreeCam, Add Keyframe, repeat, then Play.", GUI.skin.box);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Keyframe")) _dolly.Add(_cam != null ? _cam.transform : transform);
            if (GUILayout.Button("Undo")) _dolly.RemoveLast();
            if (GUILayout.Button("Clear")) _dolly.Clear();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_dolly.Playing ? "Stop" : "Play")) { if (_dolly.Playing) _dolly.Stop(); else _dolly.Play(); }
            _dolly.Loop = GUILayout.Toggle(_dolly.Loop, "  Loop");
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUILayout.Label($"Seconds per segment: {_dolly.SecondsPerSegment:0.0}");
            _dolly.SecondsPerSegment = GUILayout.HorizontalSlider(_dolly.SecondsPerSegment, 0.3f, 8f);
        }

        private void ReplayTab()
        {
            GUILayout.Label("INSTANT REPLAY", Styles.Header);
            GUILayout.Label(_replay.Recording
                ? $"<color=#ff5555>● Recording</color> — {_replay.Length:0}s buffered"
                : (_replay.Length > 0.01f ? $"{_replay.Length:0}s buffered" : "Not recording."), Styles.Hud);
            GUILayout.Label("Records every player's motion into a rolling buffer, then replays it so you can re-show and slow-mo a moment. Fly the camera freely during playback.", GUI.skin.box);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_replay.Recording ? "Stop Rec (F6)" : "Record (F6)")) ToggleRecording();
            if (GUILayout.Button("Clear")) _replay.Clear();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_replay.Playing ? "Stop (F7)" : "Play (F7)")) ToggleReplay();
            if (GUILayout.Button("Restart")) _replay.Play(0f);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label($"Playback speed: {_replay.Speed:0.00}x");
            _replay.Speed = GUILayout.HorizontalSlider(_replay.Speed, 0.1f, 2f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.25x")) _replay.Speed = 0.25f;
            if (GUILayout.Button("0.5x")) _replay.Speed = 0.5f;
            if (GUILayout.Button("1x")) _replay.Speed = 1f;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label($"Buffer length: {_replay.BufferSeconds:0}s");
            _replay.BufferSeconds = GUILayout.HorizontalSlider(_replay.BufferSeconds, 5f, 90f);
            GUILayout.Label("Tip: during playback, drag the bar at the bottom to scrub.", GUI.skin.box);
        }
    }
}
