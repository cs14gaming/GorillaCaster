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
            { "Follow", "FreeCam", "First Person", "Tablet", "Tripod", "Selfie" };
        private static readonly string[] TabNames =
            { "Camera", "Tablet", "Players", "Comp", "Replay", "Director", "Look", "World", "Overlays", "Presets" };

        // ---- camera ----
        private Camera _cam;
        private CinemachineBrain _brain;
        private CamMode _mode = CamMode.Follow;
        private VRRig _target;

        // tunables
        private float _fov = 90f;
        private float _nearClip = 0.05f;
        private float _fpNearClip = 0.30f;     // first-person clip (secondary)
        private bool _fpHideSelf = false;
        private bool _fpHideCosmetics = true;  // Pokruk-style: disable worn face/hat cosmetics
        private Vector3 _fpOffset = new Vector3(0f, 0f, 0.06f);
        private bool _nametagOcclude = true;
        private float _followDistance = 1.4f;
        private float _followHeight = 0.25f;
        private float _followLead = 0f;
        private float _moveSmoothing = 0.45f;
        private float _rotSmoothing = 0.45f;
        private float _freeSpeed = 6f;

        // orbit / selfie / tripod
        private bool _orbit;
        private float _orbitSpeed = 25f, _orbitAngle, _orbitPitch;

        // collision + dutch roll
        private bool _collision = true;
        private float _collisionRadius = 0.18f;
        private float _roll = 0f;
        private float _selfieDist = 0.6f;
        private Vector3 _tripodPos;
        private bool _tripodSet;

        // gopro camera options
        private float _goProStabilize = 0f;
        private bool _goProAutoLevel;

        // freecam look
        private bool _freeInit;
        private float _yaw, _pitch;

        // modules
        private readonly DollyPath _dolly = new DollyPath();
        private readonly ReplayRecorder _replay = new ReplayRecorder();
        private readonly GoProProp _goPro = new GoProProp();
        private readonly CompHud _comp = new CompHud();

        // look / grading
        private int _filter, _aspect;
        private float _filterStrength = 0.85f, _vignette = 0f;
        private bool _thirds;

        // presets
        private List<CamPreset> _presets;
        private int _presetIdx;
        private string _newPresetName = "My Preset";

        // auto-director
        private bool _autoDirector;

        // killcam / help / framing
        private bool _killcam;
        private int _kcPrev = -1;
        private float _kcCooldown, _kcSeconds = 5f, _kcSpeed = 0.5f;
        private bool _cheatsheet, _crosshair;

        // toggles
        private bool _menuOpen;
        private bool _autoCast;
        private bool _keepAfk = true;
        private bool _lowerThird = true, _playerList = true, _hud = true;
        private bool _nametags = true, _nametagVelocity, _minimap;
        private bool _letterbox, _hudHidden;
        private float _nametagScale = 1f;

        // watermark
        private bool _watermark = true;
        private string _watermarkText = "Spooder's Camera Mod";
        private float _watermarkOpacity = 0.55f;

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
                if (Plugin.WatermarkEnabled != null) _watermark = Plugin.WatermarkEnabled.Value;
                if (Plugin.WatermarkText != null) _watermarkText = Plugin.WatermarkText.Value;
                if (Plugin.WatermarkOpacity != null) _watermarkOpacity = Plugin.WatermarkOpacity.Value;
                WirePhone();
                _presets = Presets.Load();
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

            if (Pressed(Key.LeftBracket)) CyclePreset(-1);
            if (Pressed(Key.RightBracket)) CyclePreset(1);

            RefreshRigs();
            _replay.Sample(_rigs, Time.deltaTime);
            _goPro.Tick(_fov);
            _comp.Update(Time.deltaTime, _rigs);
            HudExtras.NametagScale = _nametagScale;
            HudExtras.Occlude = _nametagOcclude;
            if (Pressed(Key.F1)) _cheatsheet = !_cheatsheet;
            UpdateKillcam();

            if (_autoDirector) AutoDirect();
            else if (_autoCast) AutoPickTarget();
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
                {
                    Vector3 p = _goPro.Lens.position;
                    Quaternion rot = _goPro.Lens.rotation;
                    if (_goProAutoLevel) rot = Quaternion.LookRotation(rot * Vector3.forward, Vector3.up);
                    if (_goProStabilize > 0.01f)
                    {
                        float k = SmoothK(_goProStabilize);
                        _cam.transform.position = Vector3.Lerp(_cam.transform.position, p, k);
                        _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, rot, k);
                    }
                    else _cam.transform.SetPositionAndRotation(p, rot);
                }
                return;
            }

            if (_target == null) return;
            Transform head = _target.headMesh != null ? _target.headMesh.transform : _target.transform;
            Vector3 desiredPos; Quaternion desiredRot;

            switch (_mode)
            {
                case CamMode.FirstPerson:
                    desiredPos = head.TransformPoint(_fpOffset);
                    desiredRot = head.rotation;
                    if (_fpHideCosmetics && !FirstPerson.Hidden) FirstPerson.Hide();
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
                    behind = Quaternion.AngleAxis(-_orbitPitch, Vector3.Cross(behind, Vector3.up)) * behind; // pitch
                    Vector3 lead = Vector3.ClampMagnitude(CasterUtil.Velocity(_target), 9f) * (_followLead * 0.10f);
                    Vector3 lookAt = head.position + Vector3.up * 0.05f + lead;
                    desiredPos = head.position + behind * _followDistance + Vector3.up * _followHeight - lead * 0.25f;
                    desiredRot = Quaternion.LookRotation(lookAt - desiredPos, Vector3.up);
                    break;
            }

            // camera collision: don't clip through walls between the target and the camera
            if (_collision && _target != null)
            {
                Vector3 pivot = head.position + Vector3.up * 0.05f;
                Vector3 d = desiredPos - pivot; float dist = d.magnitude;
                if (dist > 0.05f && Physics.SphereCast(pivot, _collisionRadius, d / dist, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
                    desiredPos = pivot + d / dist * Mathf.Max(0.1f, hit.distance - 0.04f);
            }
            // dutch / roll tilt
            if (Mathf.Abs(_roll) > 0.01f) desiredRot = desiredRot * Quaternion.Euler(0, 0, _roll);

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
            if (kb.qKey.isPressed) _roll -= 30f * Time.deltaTime;
            if (kb.eKey.isPressed) _roll += 30f * Time.deltaTime;
            t.rotation = Quaternion.Euler(_pitch, _yaw, _roll);

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

        // Frame the action: cast the survivor closest to an infected (the likely next tag).
        private void AutoDirect()
        {
            VRRig bestSurvivor = null, anyInfected = null;
            float best = float.MaxValue;
            for (int i = 0; i < _rigs.Count; i++)
            {
                var s = _rigs[i];
                if (s == null || CasterUtil.IsTagged(s)) { if (s != null && CasterUtil.IsTagged(s)) anyInfected = s; continue; }
                for (int j = 0; j < _rigs.Count; j++)
                {
                    var inf = _rigs[j];
                    if (inf == null || !CasterUtil.IsTagged(inf)) continue;
                    float d = Vector3.Distance(CasterUtil.HeadPos(s), CasterUtil.HeadPos(inf));
                    if (d < best) { best = d; bestSurvivor = s; }
                }
            }
            if (bestSurvivor != null) _target = bestSurvivor;
            else if (anyInfected != null) _target = anyInfected;
        }

        // Auto-replay the moment someone gets tagged, in slow-mo.
        private void UpdateKillcam()
        {
            if (!_killcam) { _kcPrev = -1; return; }
            if (!_replay.Recording) _replay.StartRecording();
            int tagged = 0;
            for (int i = 0; i < _rigs.Count; i++) if (CasterUtil.IsTagged(_rigs[i])) tagged++;
            if (_kcPrev >= 0 && tagged > _kcPrev && !_replay.Playing && Time.time > _kcCooldown && _replay.Length > 2f)
            {
                _replay.Speed = _kcSpeed;
                _replay.Play(Mathf.Max(0f, _replay.Length - _kcSeconds));
                _kcCooldown = Time.time + 7f;
            }
            _kcPrev = tagged;
        }

        // ----- presets -----
        private void CyclePreset(int dir)
        {
            if (_presets == null || _presets.Count == 0) return;
            _presetIdx = ((_presetIdx + dir) % _presets.Count + _presets.Count) % _presets.Count;
            ApplyPreset(_presets[_presetIdx]);
        }

        private void ApplyPreset(CamPreset p)
        {
            if (p == null) return;
            _fov = p.fov; _nearClip = p.nearClip;
            _followDistance = p.followDist; _followHeight = p.followHeight; _followLead = p.followLead;
            _moveSmoothing = p.moveSmooth; _rotSmoothing = p.rotSmooth;
            SetMode((CamMode)Mathf.Clamp(p.mode, 0, ModeNames.Length - 1));
            _filter = p.filter; _filterStrength = p.filterStrength; _vignette = p.vignette; _aspect = p.aspect; _thirds = p.thirds;
            _nametags = p.nametags; _lowerThird = p.lowerThird; _minimap = p.minimap; _letterbox = p.letterbox;
        }

        private CamPreset Capture(string name) => new CamPreset
        {
            name = name, fov = _fov, nearClip = _nearClip, followDist = _followDistance, followHeight = _followHeight, followLead = _followLead,
            moveSmooth = _moveSmoothing, rotSmooth = _rotSmoothing, mode = (int)_mode,
            filter = _filter, filterStrength = _filterStrength, vignette = _vignette, aspect = _aspect, thirds = _thirds,
            nametags = _nametags, lowerThird = _lowerThird, minimap = _minimap, letterbox = _letterbox
        };

        private void SaveCurrentAsNew()
        {
            if (_presets == null) _presets = new List<CamPreset>();
            _presets.Add(Capture(string.IsNullOrEmpty(_newPresetName) ? "Preset " + (_presets.Count + 1) : _newPresetName));
            _presetIdx = _presets.Count - 1;
            Presets.Save(_presets);
        }

        private void SetMode(CamMode m)
        {
            bool wasFp = _mode == CamMode.FirstPerson;
            _mode = m; _freeInit = false;
            if (m == CamMode.Tripod) _tripodSet = false;
            if (m == CamMode.GoPro && !_goPro.Spawned) _goPro.SummonToHand();
            if (wasFp && m != CamMode.FirstPerson) FirstPerson.Restore();
            if (m == CamMode.FirstPerson && _fpHideCosmetics) FirstPerson.Hide();
        }

        private void CycleMode() => SetMode((CamMode)(((int)_mode + 1) % ModeNames.Length));

        private static readonly int[] TimePresets = { 0, 1, 3, 7 };
        private int _timeIdx = 1;
        private void CycleTimeOfDay() { _timeIdx = (_timeIdx + 1) % TimePresets.Length; SetTime(TimePresets[_timeIdx]); }

        // Wire the in-VR phone's on-screen buttons to mod actions.
        private void WirePhone()
        {
            _goPro.OnRecord = ToggleRecording;
            _goPro.OnPlay = ToggleReplay;
            _goPro.OnCycleMode = CycleMode;
            _goPro.OnToggleView = () => _goPro.Viewfinder = !_goPro.Viewfinder;
            _goPro.OnFovUp = () => _fov = Mathf.Clamp(_fov + 5f, 10f, 120f);
            _goPro.OnFovDown = () => _fov = Mathf.Clamp(_fov - 5f, 10f, 120f);
            _goPro.OnTime = CycleTimeOfDay;
            _goPro.OnHide = () => _hudHidden = !_hudHidden;
            _goPro.OnDirector = () => _autoDirector = !_autoDirector;
            _goPro.OnShot = Screenshot;
            _goPro.StatusText = () => $"{ModeNames[(int)_mode]}   {(int)_fov}°" + (_replay.Recording ? "   REC" : "");
            _goPro.IsRecording = () => _replay.Recording;
        }

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
                Filters.Draw(_filter, _filterStrength, _vignette, _aspect, _thirds && !_hudHidden);
                if (_letterbox) DrawLetterbox();
                if (_watermark) HudExtras.DrawWatermark(_watermarkText, _watermarkOpacity);
                if (!_hudHidden)
                {
                    if (_nametags && _cam != null) HudExtras.DrawNametags(_rigs, _cam, _target, _nametagVelocity);
                    if (_minimap) HudExtras.DrawMinimap(new Rect(Screen.width - 210, 40, 200, 160), _rigs, _target);
                    if (_hud) DrawHud();
                    if (_playerList) DrawPlayerList();
                    _comp.Draw(_rigs);
                    if (_lowerThird && _target != null) DrawLowerThird();
                    if (_crosshair) DrawCrosshair();
                    if (_replay.Recording) DrawRecIndicator();
                    if (_replay.Playing || _replay.Length > 0.01f) DrawReplayBar();
                }
                if (_cheatsheet) DrawCheatsheet();
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
            float w = 440, h = 66;
            var r = new Rect((Screen.width - w) / 2f, Screen.height - h - 28, w, h);
            Styles.DrawCard(r, new Color(0.07f, 0.08f, 0.10f, 0.95f), 10f);
            Styles.Round(new Rect(r.x + 6, r.y + 8, 7f, r.height - 16), _target.playerColor, 3.5f);
            Styles.Round(new Rect(r.x + 16, r.yMax - 5f, r.width - 32, 3f), it ? Styles.Accent2 : Styles.Accent, 1.5f);
            GUI.Label(new Rect(r.x + 24, r.y + 8, r.width - 30, 30), CasterUtil.NameOf(_target), Styles.LowerThirdName);
            string sub = it ? "<color=#ff4d8d>● IT</color>   NOW CASTING" : "NOW CASTING";
            GUI.Label(new Rect(r.x + 24, r.y + 38, r.width - 30, 20), sub, Styles.LowerThirdSub);
        }

        private void DrawCrosshair()
        {
            Rect f = Filters.FrameRect(_aspect);
            float cx = f.center.x, cy = f.center.y;
            var c = new Color(1, 1, 1, 0.5f);
            Styles.Fill(new Rect(cx - 1, cy - 7, 2, 5), c);
            Styles.Fill(new Rect(cx - 1, cy + 2, 2, 5), c);
            Styles.Fill(new Rect(cx - 7, cy - 1, 5, 2), c);
            Styles.Fill(new Rect(cx + 2, cy - 1, 5, 2), c);
            Styles.Round(new Rect(cx - 1.5f, cy - 1.5f, 3, 3), Styles.Accent, 1.5f);
        }

        private static readonly string[][] CheatRows =
        {
            new[]{ "Right Ctrl", "Open / close menu" },
            new[]{ "P", "Cycle camera mode" },
            new[]{ "1-0 / N / B", "Cast player / cycle" },
            new[]{ "Q / E", "Orbit (Follow)" },
            new[]{ "K / L", "Dolly keyframe / play" },
            new[]{ "F6 / F7", "Replay record / play" },
            new[]{ "[ / ]", "Previous / next preset" },
            new[]{ "F8", "Hide all overlays" },
            new[]{ "F1", "This cheatsheet" },
            new[]{ "F11", "Screenshot" },
        };

        private void DrawCheatsheet()
        {
            float w = 320, h = 30 + CheatRows.Length * 22 + 12;
            var r = new Rect(Screen.width - w - 16, (Screen.height - h) / 2f, w, h);
            Styles.DrawCard(r, new Color(0.07f, 0.08f, 0.10f, 0.95f), 10f);
            Styles.Round(new Rect(r.x, r.y, r.width, 3), Styles.Accent, 1.5f);
            GUI.Label(new Rect(r.x + 14, r.y + 8, r.width, 18), "HOTKEYS  <size=10>· F1</size>", Styles.Header);
            float y = r.y + 32;
            foreach (var row in CheatRows)
            {
                GUI.Label(new Rect(r.x + 14, y, 110, 18), row[0], new GUIStyle(Styles.Hud) { fontSize = 12 });
                GUI.Label(new Rect(r.x + 128, y, w - 138, 18), row[1], new GUIStyle(Styles.Sub) { fontSize = 12 });
                y += 22;
            }
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
            if (GUI.Button(new Rect(r.x + 8, r.y + 10, 60, 26), _replay.Playing ? "Pause" : "Play", Styles.BtnS))
                { if (_replay.Playing) _replay.Stop(); else _replay.Play(_replay.Playhead >= _replay.Length ? 0f : _replay.Playhead); }
            float v = GUI.HorizontalSlider(new Rect(r.x + 78, r.y + 22, w - 230, 12), _replay.Playhead, 0f, Mathf.Max(0.01f, _replay.Length), Styles.SliderS, Styles.ThumbS);
            if (Mathf.Abs(v - _replay.Playhead) > 0.005f) { _replay.Stop(); _replay.Playhead = v; _replay.ApplyAt(v); }
            GUI.Label(new Rect(r.xMax - 140, r.y + 14, 70, 20), $"{_replay.Playhead:0.0}/{_replay.Length:0.0}s", Styles.Hud);
            if (GUI.Button(new Rect(r.xMax - 64, r.y + 10, 56, 26), "Live", Styles.BtnS)) { _replay.Stop(); _replay.Playhead = 0f; }
        }

        // ----- menu window with sidebar rail -----

        private void DrawWindow(int id)
        {
            float W = _winRect.width, H = _winRect.height;
            Styles.DrawCard(new Rect(0, 0, W, H), Styles.BgCol, 0f);

            // title bar
            Styles.Round(new Rect(0, 0, W, 34), new Color(0.035f, 0.04f, 0.052f, 1f), 14f);
            Styles.Round(new Rect(14, 12, 9, 9), Styles.Accent, 4.5f);
            GUI.Label(new Rect(30, 8, 320, 22), "Spooder's <color=#46c8ff>Camera Mod</color>  <size=10>v" + Plugin.Version + "</size>", Styles.Brand);
            if (GUI.Button(new Rect(W - 32, 7, 24, 22), "✕", Styles.BtnS)) _menuOpen = false;

            // sidebar rail
            float railW = 132, top = 44;
            Styles.Round(new Rect(8, top, railW - 12, H - top - 10), Styles.RailCol, 12f);
            for (int i = 0; i < TabNames.Length; i++)
            {
                var br = new Rect(12, top + 6 + i * 38, railW - 20, 32);
                bool on = _tab == i;
                if (GUI.Button(br, "  " + TabNames[i], on ? Styles.RailOnS : Styles.RailS)) { _tab = i; _contentScroll = Vector2.zero; }
                if (on) Styles.Round(new Rect(br.x - 4, br.y + 6, 3, br.height - 12), Styles.Accent, 1.5f);
            }

            // content card
            var card = new Rect(railW + 6, top, W - railW - 16, H - top - 10);
            Styles.DrawCard(card, Styles.CardCol, 0f);
            var content = new Rect(card.x + 12, card.y + 8, card.width - 22, card.height - 16);
            GUILayout.BeginArea(content);
            _contentScroll = GUILayout.BeginScrollView(_contentScroll);
            switch (_tab)
            {
                case 0: CameraTab(); break;
                case 1: GoProTab(); break;
                case 2: PlayersTab(); break;
                case 3: CompTab(); break;
                case 4: ReplayTab(); break;
                case 5: DirectorTab(); break;
                case 6: LookTab(); break;
                case 7: WorldTab(); break;
                case 8: OverlaysTab(); break;
                case 9: PresetsTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUI.DragWindow(new Rect(0, 0, W, 30));
        }

        private void CameraTab()
        {
            UI.Header("Mode  ·  P to cycle");
            int m = GUILayout.SelectionGrid((int)_mode, ModeNames, 3, Styles.BtnS, GUILayout.Height(58));
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
                _followLead = UI.Slider("Lead (anticipate motion)", _followLead, 0f, 1f);
                _orbit = UI.Toggle("Auto-orbit  (Q/E manual)", _orbit);
                if (_orbit) _orbitSpeed = UI.Slider("Orbit Speed", _orbitSpeed, -120f, 120f, "0");
                _orbitPitch = UI.Slider("Orbit pitch", _orbitPitch, -60f, 80f, "0");
            }
            else if (_mode == CamMode.FirstPerson)
            {
                UI.Header("First Person  ·  Pokruk-style");
                bool hc = UI.Toggle("Hide my hat / face cosmetics", _fpHideCosmetics);
                if (hc != _fpHideCosmetics) { _fpHideCosmetics = hc; if (hc) FirstPerson.Hide(); else FirstPerson.Restore(); }
                UI.Note("Disables your worn hat & face cosmetics locally so they don't block the first-person shot (restored when you leave FP).");
                GUILayout.Space(4);
                _fpOffset.x = UI.Slider("Offset X", _fpOffset.x, -0.3f, 0.3f);
                _fpOffset.y = UI.Slider("Offset Y", _fpOffset.y, -0.3f, 0.3f);
                _fpOffset.z = UI.Slider("Offset Z (forward)", _fpOffset.z, -0.2f, 0.4f);
                _fpHideSelf = UI.Toggle("Also clip near plane", _fpHideSelf);
                if (_fpHideSelf) _fpNearClip = UI.Slider("Clip strength", _fpNearClip, 0.1f, 0.6f);
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
                UI.Note("Broadcast renders from the grabbable phone's lens — see the Phone tab.");
            }

            UI.Header("Rig");
            _collision = UI.Toggle("Camera collision (no wall clip)", _collision);
            if (_collision) _collisionRadius = UI.Slider("Collision radius", _collisionRadius, 0.05f, 0.5f);
            _roll = UI.Slider("Dutch roll", _roll, -45f, 45f, "0");
            if (UI.Button("Frame all players")) FrameAll();

            GUILayout.Space(6);
            if (UI.Button("📸  Screenshot  (F11)")) Screenshot();
        }

        private void FrameAll()
        {
            if (_rigs.Count == 0 || _cam == null) return;
            Vector3 c = Vector3.zero; int n = 0;
            foreach (var r in _rigs) { if (r != null) { c += CasterUtil.HeadPos(r); n++; } }
            if (n == 0) return; c /= n;
            float ext = 2f;
            foreach (var r in _rigs) { if (r != null) ext = Mathf.Max(ext, Vector3.Distance(CasterUtil.HeadPos(r), c)); }
            SetMode(CamMode.FreeCam);
            float d = ext / Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad) + ext * 0.6f;
            _cam.transform.position = c + new Vector3(0, ext * 0.5f, -d);
            _cam.transform.LookAt(c);
            _freeInit = false;
        }

        private void GoProTab()
        {
            UI.Header("Camera Phone");
            UI.Note("A real in-VR phone with a live viewfinder and on-screen buttons. Grab it with GRIP, then poke the screen with your free hand to record, change mode, FOV, viewfinder and time — control everything without the PC.");

            GUILayout.Space(2);
            GUILayout.Label(_goPro.Spawned ? (_goPro.Held ? "Status:  <color=#5cf08a>● held</color>" : "Status:  <color=#46c8ff>● placed</color>") : "Status:  not spawned", Styles.Hud);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (UI.SmallButton(_goPro.Spawned ? "Summon to hand" : "Spawn phone", 148)) _goPro.SummonToHand();
            if (UI.SmallButton("Despawn", 110)) _goPro.Despawn();
            GUILayout.EndHorizontal();
            if (UI.Primary(_mode == CamMode.GoPro ? "● Broadcasting phone" : "Broadcast phone camera")) SetMode(CamMode.GoPro);

            UI.Header("Camera Options");
            _goPro.Viewfinder = UI.Toggle("Live viewfinder screen", _goPro.Viewfinder);
            _goProAutoLevel = UI.Toggle("Auto-level horizon", _goProAutoLevel);
            _goProStabilize = UI.Slider("Stabilization", _goProStabilize, 0f, 0.92f);
            _fov = UI.Slider("Camera FOV", _fov, 10f, 120f, "0");
            UI.Note("On-screen buttons: REC · MODE · FOV-/FOV+ · VIEW · TIME. Stabilization smooths shaky hands; auto-level keeps the horizon flat.");

            UI.Header("How to use");
            UI.Note("Reach a hand to the phone and squeeze GRIP to hold it. Hold it in one hand and poke its screen with the other. Release grip to drop it — it floats where you let go.");
        }

        private void PlayersTab()
        {
            _autoDirector = UI.Toggle("Auto-director (frame the action)", _autoDirector);
            _autoCast = UI.Toggle("Auto-cast the tagged player", _autoCast);
            if (_autoDirector) UI.Note("Automatically casts the survivor about to be tagged — the most exciting angle.");
            UI.Header("Cast  ·  1-0 / N / B");
            _playerScroll = GUILayout.BeginScrollView(_playerScroll, GUILayout.Height(300));
            for (int i = 0; i < _rigs.Count; i++)
            {
                var r = _rigs[i]; bool sel = r == _target;
                GUILayout.BeginHorizontal();
                Styles.Fill(GUILayoutUtility.GetRect(10, 22, GUILayout.Width(10)), r.playerColor);
                string tag = CasterUtil.IsTagged(r) ? "  <color=#ff5577>[IT]</color>" : "";
                var st = new GUIStyle(Styles.BtnS); if (sel) st.normal.textColor = new Color(0.36f, 0.94f, 0.54f);
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

            UI.Header("Killcam");
            _killcam = UI.Toggle("Auto slow-mo replay on every tag", _killcam);
            if (_killcam)
            {
                _kcSeconds = UI.Slider("Clip length (s)", _kcSeconds, 2f, 12f, "0");
                _kcSpeed = UI.Slider("Slow-mo speed", _kcSpeed, 0.1f, 1f);
                UI.Note("Keeps recording and auto-replays the moment someone is tagged.");
            }
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

        private void CompTab()
        {
            UI.Header("Competitive Overlay");
            _comp.ShowTimer = UI.Toggle("Round timer (3:00 cap)", _comp.ShowTimer);
            _comp.ShowScoreboard = UI.Toggle("Scoreboard (survivors / infected)", _comp.ShowScoreboard);
            _comp.AutoTimer = UI.Toggle("Auto start/stop timer", _comp.AutoTimer);
            UI.Note("Reads live infection state. Timer auto-starts on first tag and ends on a wipe or the 3:00 cap.");

            UI.Header("Timer (manual)");
            GUILayout.BeginHorizontal();
            if (UI.SmallButton(_comp.Running ? "Stop" : "Start", 90)) { if (_comp.Running) _comp.StopTimer(); else _comp.StartTimer(); }
            if (UI.SmallButton("Reset", 90)) _comp.ResetTimer();
            GUILayout.EndHorizontal();

            UI.Header("Team Scores");
            _comp.ShowTeams = UI.Toggle("Show team scores", _comp.ShowTeams);
            GUILayout.BeginHorizontal();
            GUILayout.Label("A", Styles.Label, GUILayout.Width(14));
            if (UI.SmallButton("-", 40)) _comp.TeamA = Mathf.Max(0, _comp.TeamA - 1);
            GUILayout.Label(_comp.TeamA.ToString(), Styles.Value, GUILayout.Width(28));
            if (UI.SmallButton("+", 40)) _comp.TeamA++;
            GUILayout.Space(10);
            GUILayout.Label("B", Styles.Label, GUILayout.Width(14));
            if (UI.SmallButton("-", 40)) _comp.TeamB = Mathf.Max(0, _comp.TeamB - 1);
            GUILayout.Label(_comp.TeamB.ToString(), Styles.Value, GUILayout.Width(28));
            if (UI.SmallButton("+", 40)) _comp.TeamB++;
            GUILayout.EndHorizontal();
        }

        private void LookTab()
        {
            UI.Header("Color Grade");
            _filter = GUILayout.SelectionGrid(_filter, Filters.Names, 4, Styles.BtnS, GUILayout.Height(56));
            _filterStrength = UI.Slider("Filter strength", _filterStrength, 0f, 1f);
            _vignette = UI.Slider("Vignette", _vignette, 0f, 1f);

            UI.Header("Framing");
            _aspect = GUILayout.SelectionGrid(_aspect, Filters.AspectNames, 3, Styles.BtnS, GUILayout.Height(56));
            _thirds = UI.Toggle("Rule-of-thirds grid", _thirds);
            UI.Note("Aspect guides letterbox/pillarbox the view for cinematic or vertical clips. Grade & vignette apply to the broadcast (monitor) only.");
        }

        private void PresetsTab()
        {
            UI.Header("Presets  ·  [ ]");
            UI.Note("One-tap camera + look setups. Saved to BepInEx/config/GorillaCaster_presets.json — edit them by hand too.");
            if (_presets != null)
            {
                for (int i = 0; i < _presets.Count; i++)
                {
                    var p = _presets[i];
                    bool sel = i == _presetIdx;
                    var st = new GUIStyle(Styles.BtnS); if (sel) st.normal.textColor = new Color(0.36f, 0.94f, 0.54f);
                    if (GUILayout.Button((sel ? "● " : "") + p.name, st, GUILayout.Height(28))) { _presetIdx = i; ApplyPreset(p); }
                }
            }
            UI.Header("Save current");
            GUILayout.BeginHorizontal();
            _newPresetName = GUILayout.TextField(_newPresetName, 28, GUILayout.Height(26));
            if (UI.SmallButton("Save", 70)) SaveCurrentAsNew();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (UI.SmallButton("Reload file", 110)) { _presets = Presets.Load(); }
            if (_presets != null && _presets.Count > 0 && UI.SmallButton("Delete", 90)) { _presets.RemoveAt(Mathf.Clamp(_presetIdx, 0, _presets.Count - 1)); Presets.Save(_presets); _presetIdx = 0; }
            GUILayout.EndHorizontal();
        }

        private void OverlaysTab()
        {
            UI.Header("Overlays  ·  F8 hides all");
            _lowerThird = UI.Toggle("Now-casting lower third", _lowerThird);
            _playerList = UI.Toggle("Player list", _playerList);
            _nametags = UI.Toggle("Floating nametags", _nametags);
            _nametagOcclude = UI.Toggle("Hide nametags behind walls", _nametagOcclude);
            _nametagVelocity = UI.Toggle("Speed on nametags", _nametagVelocity);
            _minimap = UI.Toggle("Overhead minimap", _minimap);
            _hud = UI.Toggle("FPS / mode / speed readout", _hud);
            _letterbox = UI.Toggle("Cinematic letterbox bars", _letterbox);
            _crosshair = UI.Toggle("Center crosshair (framing)", _crosshair);
            _cheatsheet = UI.Toggle("Hotkey cheatsheet (F1)", _cheatsheet);
            _nametagScale = UI.Slider("Nametag size", _nametagScale, 0.6f, 1.8f);

            UI.Header("Watermark");
            _watermark = UI.Toggle("Show watermark", _watermark);
            _watermarkText = GUILayout.TextField(_watermarkText, 40, GUILayout.Height(26));
            _watermarkOpacity = UI.Slider("Opacity", _watermarkOpacity, 0f, 1f);

            UI.Header("Movement Smoothing");
            _moveSmoothing = UI.Slider("Position", _moveSmoothing, 0f, 0.95f);
            _rotSmoothing = UI.Slider("Rotation", _rotSmoothing, 0f, 0.95f);
            _freeSpeed = UI.Slider("FreeCam fly speed", _freeSpeed, 1f, 25f, "0.0");
        }
    }
}
