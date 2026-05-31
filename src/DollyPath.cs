using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// A keyframed camera path ("director" mode). Drop waypoints from the current camera
    /// transform, then play back along a Catmull-Rom spline for smooth cinematic moves.
    /// </summary>
    internal class DollyPath
    {
        private struct Key { public Vector3 pos; public Quaternion rot; }

        private readonly List<Key> _keys = new List<Key>();
        public bool Playing { get; private set; }
        public bool Loop;
        public float SecondsPerSegment = 2.5f;

        private float _t;

        public int Count => _keys.Count;

        public void Add(Transform cam)
        {
            _keys.Add(new Key { pos = cam.position, rot = cam.rotation });
        }

        public void RemoveLast()
        {
            if (_keys.Count > 0) _keys.RemoveAt(_keys.Count - 1);
        }

        public void Clear()
        {
            _keys.Clear();
            Playing = false;
            _t = 0f;
        }

        /// <summary>Snapshot the current keyframes for saving to disk.</summary>
        public List<DollyKey> ExportKeys()
        {
            var list = new List<DollyKey>(_keys.Count);
            foreach (var k in _keys) list.Add(new DollyKey { pos = k.pos, rot = k.rot });
            return list;
        }

        /// <summary>Replace the path with a saved set of keyframes.</summary>
        public void ImportKeys(List<DollyKey> keys)
        {
            _keys.Clear();
            if (keys != null)
                foreach (var k in keys) _keys.Add(new Key { pos = k.pos, rot = k.rot });
            Playing = false;
            _t = 0f;
        }

        public void Play()
        {
            if (_keys.Count < 2) return;
            Playing = true;
            _t = 0f;
        }

        public void Stop() => Playing = false;

        /// <summary>Drive the camera one frame along the path. Returns false when finished.</summary>
        public bool Tick(Transform cam, float dt)
        {
            if (!Playing || _keys.Count < 2) { Playing = false; return false; }

            int segments = _keys.Count - 1;
            _t += dt / Mathf.Max(0.05f, SecondsPerSegment);

            if (_t >= segments)
            {
                if (Loop) _t -= segments;
                else { _t = segments; Sample(cam, segments); Playing = false; return false; }
            }

            Sample(cam, _t);
            return true;
        }

        private void Sample(Transform cam, float u)
        {
            int seg = Mathf.Clamp(Mathf.FloorToInt(u), 0, _keys.Count - 2);
            float local = Mathf.Clamp01(u - seg);

            Vector3 p0 = _keys[Mathf.Max(seg - 1, 0)].pos;
            Vector3 p1 = _keys[seg].pos;
            Vector3 p2 = _keys[seg + 1].pos;
            Vector3 p3 = _keys[Mathf.Min(seg + 2, _keys.Count - 1)].pos;

            cam.position = CatmullRom(p0, p1, p2, p3, local);
            cam.rotation = Quaternion.Slerp(_keys[seg].rot, _keys[seg + 1].rot, Mathf.SmoothStep(0f, 1f, local));
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }
    }
}
