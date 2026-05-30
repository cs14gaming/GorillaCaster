using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// A physical, grabbable GoPro prop. Builds its own model from primitives, can be grabbed
    /// out of the air with either hand (grip), and stays where you drop it (sticky placement).
    /// When GoPro camera mode is active, the casting camera renders from this prop's lens.
    /// </summary>
    internal class GoProProp
    {
        public GameObject Root { get; private set; }
        public Transform Lens { get; private set; }
        public bool Held { get; private set; }
        public bool Spawned => Root != null;

        private Renderer _recLight;
        private Material _recMat;
        private int _hand;                 // 0 = right, 1 = left
        private Vector3 _localPos;
        private Quaternion _localRot;
        private const float GrabRadius = 0.20f;

        // ----- spawn / model -----

        public void EnsureSpawned()
        {
            if (Root != null) return;

            Root = new GameObject("GoPro_Caster");
            Root.hideFlags = HideFlags.DontSave;

            // body
            var body = Box(Root.transform, new Vector3(0, 0, 0), new Vector3(0.10f, 0.072f, 0.040f),
                new Color(0.06f, 0.06f, 0.07f));
            // front lens shroud
            var shroud = Box(Root.transform, new Vector3(0.022f, 0f, 0.024f), new Vector3(0.044f, 0.05f, 0.022f),
                new Color(0.10f, 0.10f, 0.12f));
            // lens (cylinder pointing forward +z)
            var lens = Cyl(Root.transform, new Vector3(0.022f, 0f, 0.04f), 0.015f, 0.010f, new Color(0.02f, 0.02f, 0.03f));
            lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // glass
            var glass = Cyl(Root.transform, new Vector3(0.022f, 0f, 0.047f), 0.011f, 0.003f, new Color(0.10f, 0.35f, 0.55f), true);
            glass.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // rec light
            var rec = Box(Root.transform, new Vector3(-0.03f, 0.041f, 0f), new Vector3(0.012f, 0.006f, 0.012f),
                new Color(1f, 0.15f, 0.15f), true);
            _recLight = rec.GetComponent<Renderer>();
            _recMat = _recLight != null ? _recLight.material : null;
            // back screen
            Box(Root.transform, new Vector3(0f, 0f, -0.021f), new Vector3(0.07f, 0.05f, 0.004f),
                new Color(0.05f, 0.18f, 0.22f), true);

            // lens anchor the camera will copy (looks down +z)
            var lensGo = new GameObject("Lens");
            lensGo.transform.SetParent(Root.transform, false);
            lensGo.transform.localPosition = new Vector3(0.022f, 0f, 0.05f);
            lensGo.transform.localRotation = Quaternion.identity;
            Lens = lensGo.transform;
        }

        public void SummonToHand()
        {
            EnsureSpawned();
            var tagger = GorillaTagger.Instance;
            Transform h = tagger != null ? tagger.rightHandTransform : null;
            if (h != null)
            {
                Root.transform.position = h.position + h.forward * 0.12f;
                Root.transform.rotation = h.rotation;
            }
            else if (Camera.main != null)
            {
                Root.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.5f;
                Root.transform.rotation = Camera.main.transform.rotation;
            }
            Held = false;
        }

        public void Despawn()
        {
            if (Root != null) Object.Destroy(Root);
            Root = null; Lens = null; Held = false;
        }

        // ----- per-frame grab + effects -----

        public void Tick()
        {
            if (Root == null) return;

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
                    if (!grabbing || hand == null)
                        Held = false;                                   // drop, stays in place
                    else
                        Root.transform.SetPositionAndRotation(hand.TransformPoint(_localPos), hand.rotation * _localRot);
                }
            }

            // pulse the rec light
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

        // ----- primitive builders -----

        private static GameObject Box(Transform parent, Vector3 pos, Vector3 size, Color col, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(go); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            Paint(go, col, emissive);
            return go;
        }

        private static GameObject Cyl(Transform parent, Vector3 pos, float radius, float halfLen, Color col, bool emissive = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Strip(go); go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = new Vector3(radius * 2f, halfLen, radius * 2f);
            Paint(go, col, emissive);
            return go;
        }

        private static void Strip(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
        }

        private static Shader _shader;
        private static void Paint(GameObject go, Color col, bool emissive)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (_shader == null)
                _shader = Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                          ?? Shader.Find("Standard")
                          ?? Shader.Find("Unlit/Color")
                          ?? Shader.Find("Sprites/Default");
            var m = new Material(_shader);
            m.color = col;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", col);
            }
            r.material = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
