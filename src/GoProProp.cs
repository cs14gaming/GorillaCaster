using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// A sleek, LIV-style grabbable camera: a handle + a flat screen body that shows a live
    /// viewfinder of what the lens sees, plus a lens on the back. Grab it with grip, drop it to
    /// place it (sticky). When GoPro mode is active the broadcast renders from its lens.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;
        public bool Viewfinder = true;

        private Camera _preview;
        private RenderTexture _rt;
        private Material _screenMat, _recMat;
        private int _hand;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private const float GrabRadius = 0.22f;

        private static readonly Color Dark = new Color(0.05f, 0.055f, 0.07f);
        private static readonly Color Body = new Color(0.09f, 0.10f, 0.12f);

        // ----- spawn / model -----

        public void EnsureSpawned()
        {
            if (Root != null) return;

            Root = new GameObject("SpooderCam") { hideFlags = HideFlags.DontSave };

            // grip handle
            var h = Cyl(Root.transform, new Vector3(0, -0.062f, 0), 0.011f, 0.030f, Dark);
            Box(Root.transform, new Vector3(0, -0.030f, 0), new Vector3(0.024f, 0.030f, 0.024f), Dark);

            // flat phone-style body
            Box(Root.transform, Vector3.zero, new Vector3(0.140f, 0.080f, 0.010f), Body);
            Box(Root.transform, new Vector3(0, 0, -0.0055f), new Vector3(0.128f, 0.070f, 0.002f), Color.black);

            // live screen (thin box wearing the render texture)
            var screen = Box(Root.transform, new Vector3(0, 0, -0.0066f), new Vector3(0.124f, 0.066f, 0.001f), Color.white);

            // lens cluster on the back (+z)
            Box(Root.transform, new Vector3(0.046f, 0.020f, 0.009f), new Vector3(0.030f, 0.030f, 0.012f), Dark);
            var lensCyl = Cyl(Root.transform, new Vector3(0.046f, 0.020f, 0.017f), 0.011f, 0.006f, Color.black);
            lensCyl.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var glass = Cyl(Root.transform, new Vector3(0.046f, 0.020f, 0.023f), 0.008f, 0.002f, new Color(0.1f, 0.4f, 0.6f), true);
            glass.transform.localRotation = Quaternion.Euler(90, 0, 0);

            // rec light
            var rec = Box(Root.transform, new Vector3(-0.052f, 0.030f, -0.006f), new Vector3(0.010f, 0.005f, 0.004f), new Color(1f, 0.15f, 0.15f), true);
            _recMat = rec.GetComponent<Renderer>() != null ? rec.GetComponent<Renderer>().material : null;

            // lens anchor (broadcast viewpoint, faces +z)
            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(0.046f, 0.020f, 0.03f);
            lensGo.transform.localRotation = Quaternion.identity;
            Lens = lensGo.transform;

            BuildViewfinder(screen.transform);
        }

        private void BuildViewfinder(Transform screen)
        {
            try
            {
                _rt = new RenderTexture(360, 200, 16) { name = "SpooderCamRT", hideFlags = HideFlags.DontSave };
                _rt.Create();

                var camGo = new GameObject("PreviewCam");
                camGo.transform.SetParent(Lens, false);
                camGo.transform.localPosition = new Vector3(0, 0, 0.005f);
                _preview = camGo.AddComponent<Camera>();
                _preview.targetTexture = _rt;
                _preview.fieldOfView = 90f;
                _preview.nearClipPlane = 0.02f;
                _preview.farClipPlane = 600f;
                _preview.depth = -10;
                _preview.clearFlags = CameraClearFlags.Skybox;

                var sh = Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
                _screenMat = new Material(sh);
                _screenMat.mainTexture = _rt;
                if (_screenMat.HasProperty("_BaseMap")) _screenMat.SetTexture("_BaseMap", _rt);
                var sr = screen.GetComponent<Renderer>();
                if (sr != null) sr.material = _screenMat;
            }
            catch (System.Exception e) { Debug.LogWarning("[GorillaCaster] viewfinder: " + e.Message); }
        }

        // ----- placement / grab -----

        public void SummonToHand()
        {
            EnsureSpawned();
            var tagger = GorillaTagger.Instance;
            Transform t = tagger != null ? tagger.rightHandTransform : null;
            if (t != null) { Root.transform.position = t.position + t.forward * 0.12f; Root.transform.rotation = t.rotation; }
            else if (Camera.main != null) { Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.5f; Root.transform.rotation = Camera.main.transform.rotation; }
            Held = false;
        }

        public void Despawn()
        {
            if (_rt != null) { _rt.Release(); Object.Destroy(_rt); _rt = null; }
            if (Root != null) Object.Destroy(Root);
            Root = null; Lens = null; Held = false; _preview = null;
        }

        public void SetFov(float fov) { if (_preview != null) _preview.fieldOfView = Mathf.Clamp(fov, 10f, 120f); }

        public void Tick(float fov)
        {
            if (Root == null) return;
            SetFov(fov);
            if (_preview != null && _preview.enabled != Viewfinder) _preview.enabled = Viewfinder;

            var poller = ControllerInputPoller.instance;
            var tagger = GorillaTagger.Instance;
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
                }
            }

            if (_recMat != null)
            {
                float p = 0.4f + 0.6f * Mathf.PingPong(Time.time * 1.6f, 1f);
                var c = new Color(1f, 0.15f, 0.15f) * p;
                _recMat.color = c;
                if (_recMat.HasProperty("_EmissionColor")) _recMat.SetColor("_EmissionColor", c);
            }
        }

        private bool Near(Transform hand) => Vector3.Distance(hand.position, Root.transform.position) < GrabRadius;

        private void Attach(int hand, Transform t)
        {
            Held = true; _hand = hand;
            _localPos = t.InverseTransformPoint(Root.transform.position);
            _localRot = Quaternion.Inverse(t.rotation) * Root.transform.rotation;
        }

        // ----- primitives -----

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = size;
            Paint(go, col, emissive); return go;
        }

        private static GameObject Cyl(Transform parent, Vector3 pos, float radius, float halfLen, Color col, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Strip(go); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localScale = new Vector3(radius * 2f, halfLen, radius * 2f);
            Paint(go, col, emissive); return go;
        }

        private static void Strip(GameObject go) { var c = go.GetComponent<Collider>(); if (c != null) Object.Destroy(c); }

        private static Shader _shader;
        private static void Paint(GameObject go, Color col, bool emissive)
        {
            var r = go.GetComponent<Renderer>(); if (r == null) return;
            if (_shader == null)
                _shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                          ?? Shader.Find("Standard") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var m = new Material(_shader); m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (emissive) { m.EnableKeyword("_EMISSION"); if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col); }
            r.material = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
