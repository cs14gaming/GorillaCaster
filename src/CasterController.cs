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
    /// so a caster can follow players, free-fly, run cinematic dolly paths, change the time
    /// of day, and overlay match info. The VR headset view is never touched.
    /// </summary>
    public class CasterController : MonoBehaviour
    {
        private enum CamMode { Follow, FreeCam, FirstPerson }

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
        private float _orbitSpeed = 25f;   // deg/sec
        private float _orbitAngle;

        // freecam look state
        private bool _freeInit;
        private float _yaw, _pitch;

        // director
        private readonly DollyPath _dolly = new DollyPath();

        // toggles
        private bool _menuOpen;
        private bool _autoCast;
        private bool _keepAfk = true;
        private bool _lowerThird = true;
        private bool _playerList = true;
        private bool _hud = true;
        private bool _nametags = true;
        private bool _minimap;

        // ui
        private Rect _winRect = new Rect(40, 40, 440, 500);
        private int _tab;
        private readonly string[] _tabs = { "Camera", "Move", "Players", "World", "Director" };
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
            try { DriveCamera(); }
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
            // fps meter
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
            if (Pressed(Key.K)) _dolly.Add(_cam.transform);          // drop keyframe
            if (Pressed(Key.L)) { if (_dolly.Playing) _dolly.Stop(); else _dolly.Play(); }
            HandleNumberKeys(kb);

            if (_brain != null && _brain.enabled) _brain.enabled = false;

            if (_keepAfk && PhotonNetworkController.Instance != null)
                PhotonNetworkController.Instance.disableAFKKick = true;

            RefreshRigs();

            if (_autoCast) AutoPickTarget();
            if (_target == null && _rigs.Count > 0) _target = _rigs[0];
        }

        private void DriveCamera()
        {
            if (_cam == null) return;

            _cam.fieldOfView = _fov;
            _cam.nearClipPlane = _nearClip;

            // director playback overrides everything
            if (_dolly.Playing)
            {
                _dolly.Tick(_cam.transform, Time.deltaTime);
                return;
            }

            if (_mode == CamMode.FreeCam)
            {
                FreeCamMove();
                return;
            }

            _freeInit = false;
            if (_target == null) return;

            Transform head = _target.headMesh != null ? _target.headMesh.transform : _target.transform;
            Vector3 desiredPos;
            Quaternion desiredRot;

            if (_mode == CamMode.FirstPerson)
            {
                desiredPos = head.position + head.forward * 0.06f;
                desiredRot = head.rotation;
            }
            else // Follow (with optional orbit)
            {
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
                Vector3 lookAt = head.position + Vector3.up * 0.05f;
                desiredRot = Quaternion.LookRotation(lookAt - desiredPos, Vector3.up);
            }

            float pk = SmoothK(_moveSmoothing);
            float rk = SmoothK(_rotSmoothing);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, desiredPos, pk);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, desiredRot, rk);
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

        // ============================================================ players

        private void RefreshRigs()
        {
            _rigs.Clear();
            var containers = VRRigCache.ActiveRigContainers;
            if (containers != null)
            {
                foreach (var c in containers)
                    if (c != null && c.Rig != null) _rigs.Add(c.Rig);
            }
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
            _mode = (CamMode)(((int)_mode + 1) % 3);
            _freeInit = false;
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

                if (_nametags && _cam != null) HudExtras.DrawNametags(_rigs, _cam, _target);
                if (_minimap) HudExtras.DrawMinimap(new Rect(Screen.width - 210, 40, 200, 160), _rigs, _target);
                if (_hud) DrawHud();
                if (_playerList) DrawPlayerList();
                if (_lowerThird && _target != null) DrawLowerThird();

                if (_menuOpen)
                    _winRect = GUI.Window(0xCA57, _winRect, DrawWindow, "");
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] OnGUI: " + e.Message); }
        }

        private void DrawHud()
        {
            GUI.Label(new Rect(12, 10, 300, 22), $"<color=#5cc8ff>●</color> {_fps:0} FPS · {_mode}" + (_dolly.Playing ? " · DOLLY" : ""), Styles.Hud);
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
            // color stripe
            Styles.Fill(new Rect(r.x, r.y, 8f, r.height), _target.playerColor);
            // accent base line
            Styles.Fill(new Rect(r.x, r.yMax - 3f, r.width, 3f), it ? new Color(1f, 0.25f, 0.25f) : Styles.BrandAccent);

            GUI.Label(new Rect(r.x + 22, r.y + 8, r.width - 30, 30), CasterUtil.NameOf(_target), Styles.LowerThirdName);
            string sub = it ? "<color=#ff5555>● IT</color>   NOW CASTING" : "NOW CASTING";
            GUI.Label(new Rect(r.x + 22, r.y + 38, r.width - 30, 20), sub, Styles.LowerThirdSub);
        }

        // ----- menu window -----

        private void DrawWindow(int id)
        {
            Styles.Fill(new Rect(0, 0, _winRect.width, _winRect.height), new Color(0.10f, 0.11f, 0.13f, 0.97f));
            Styles.Fill(new Rect(0, 0, _winRect.width, 26), new Color(0.06f, 0.07f, 0.09f, 1f));
            GUI.Label(new Rect(12, 4, 300, 20), "<b>GorillaCaster</b>", Styles.Header);
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
            }
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0, 0, _winRect.width, 26));
        }

        private void CameraTab()
        {
            GUILayout.Label("MODE  (P to cycle)", Styles.Header);
            int m = GUILayout.Toolbar((int)_mode, new[] { "Follow", "FreeCam", "First Person" });
            if (m != (int)_mode) { _mode = (CamMode)m; _freeInit = false; }

            GUILayout.Space(8);
            GUILayout.Label($"Field of View: {_fov:0}");
            _fov = GUILayout.HorizontalSlider(_fov, 10f, 120f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("110")) _fov = 110;
            if (GUILayout.Button("90")) _fov = 90;
            if (GUILayout.Button("60")) _fov = 60;
            if (GUILayout.Button("35")) _fov = 35;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label($"Near Clip: {_nearClip:0.00}");
            _nearClip = GUILayout.HorizontalSlider(_nearClip, 0.01f, 0.6f);

            GUILayout.Space(6);
            GUILayout.Label($"Follow Distance: {_followDistance:0.00}");
            _followDistance = GUILayout.HorizontalSlider(_followDistance, 0f, 4f);
            GUILayout.Label($"Follow Height: {_followHeight:0.00}");
            _followHeight = GUILayout.HorizontalSlider(_followHeight, -1f, 1.5f);

            GUILayout.Space(6);
            _orbit = GUILayout.Toggle(_orbit, "  Auto-orbit target (Q/E manual)");
            if (_orbit)
            {
                GUILayout.Label($"Orbit Speed: {_orbitSpeed:0}°/s");
                _orbitSpeed = GUILayout.HorizontalSlider(_orbitSpeed, -120f, 120f);
            }

            GUILayout.Space(8);
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
            _playerScroll = GUILayout.BeginScrollView(_playerScroll, GUILayout.Height(250));
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

            GUILayout.Space(8);
            GUILayout.Label("WEATHER", Styles.Header);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear")) SetWeather(BetterDayNightManager.WeatherType.None);
            if (GUILayout.Button("Rain")) SetWeather(BetterDayNightManager.WeatherType.Raining);
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("OVERLAYS", Styles.Header);
            _lowerThird = GUILayout.Toggle(_lowerThird, "  Now-casting lower third");
            _playerList = GUILayout.Toggle(_playerList, "  Player list");
            _nametags = GUILayout.Toggle(_nametags, "  Floating nametags");
            _minimap = GUILayout.Toggle(_minimap, "  Overhead minimap");
            _hud = GUILayout.Toggle(_hud, "  FPS / mode readout");
            _keepAfk = GUILayout.Toggle(_keepAfk, "  Disable AFK kick");

            GUILayout.Space(10);
            GUILayout.Label("JOIN ROOM", Styles.Header);
            GUILayout.BeginHorizontal();
            _joinCode = GUILayout.TextField(_joinCode, 12, GUILayout.Width(150));
            if (GUILayout.Button("Join", GUILayout.Width(70))) JoinRoom();
            if (GUILayout.Button("Leave", GUILayout.Width(70))) LeaveRoom();
            GUILayout.EndHorizontal();
        }

        private void DirectorTab()
        {
            GUILayout.Label("CINEMATIC DOLLY", Styles.Header);
            GUILayout.Label($"Keyframes: {_dolly.Count}    (K = add, L = play/stop)");
            GUILayout.Label("Fly to a spot in FreeCam, then Add Keyframe. Repeat, then Play for a smooth shot.", GUI.skin.box);

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
    }
}
