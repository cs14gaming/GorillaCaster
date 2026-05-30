using System.Collections.Generic;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// Instant-replay system (Sakuraa-style). Continuously samples every active rig's
    /// head / hands / body transforms into a rolling buffer, then plays them back by
    /// writing the recorded transforms each frame — so a caster can rewind and re-show
    /// a moment, scrub, and slow it down, all while flying the camera freely.
    ///
    /// Rigs are matched by reference, so a clip is valid while those players are present.
    /// Experimental: transform write-back ordering may need a tweak after live testing.
    /// </summary>
    internal class ReplayRecorder
    {
        private struct Pose
        {
            public VRRig rig;
            public Vector3 hp; public Quaternion hr;   // head
            public Vector3 lp; public Quaternion lr;   // left hand
            public Vector3 rp; public Quaternion rr;   // right hand
            public Vector3 bp; public Quaternion br;   // body
            public bool it;
        }

        private struct Frame { public float t; public List<Pose> poses; }

        private readonly List<Frame> _frames = new List<Frame>();

        public bool Recording { get; private set; }
        public bool Playing { get; private set; }
        public float BufferSeconds = 30f;     // rolling window length
        public float Speed = 1f;
        public float Playhead;                // seconds into the clip [0..Length]

        private float _sinceSample;
        private const float SampleHz = 30f;

        public float Length => _frames.Count < 2 ? 0f : _frames[_frames.Count - 1].t - _frames[0].t;
        public int FrameCount => _frames.Count;

        // ----- recording -----

        public void StartRecording() { Recording = true; }
        public void StopRecording() { Recording = false; }

        public void Clear()
        {
            _frames.Clear();
            Playing = false;
            Playhead = 0f;
        }

        public void Sample(IList<VRRig> rigs, float dt)
        {
            if (!Recording) return;
            _sinceSample += dt;
            if (_sinceSample < 1f / SampleHz) return;
            _sinceSample = 0f;

            var poses = new List<Pose>(rigs.Count);
            for (int i = 0; i < rigs.Count; i++)
            {
                var r = rigs[i];
                if (r == null || r.headMesh == null) continue;
                var head = r.headMesh.transform;
                var p = new Pose { rig = r, it = CasterUtil.IsTagged(r) };
                p.hp = head.position; p.hr = head.rotation;
                if (r.leftHandTransform != null) { p.lp = r.leftHandTransform.position; p.lr = r.leftHandTransform.rotation; }
                if (r.rightHandTransform != null) { p.rp = r.rightHandTransform.position; p.rr = r.rightHandTransform.rotation; }
                if (r.bodyTransform != null) { p.bp = r.bodyTransform.position; p.br = r.bodyTransform.rotation; }
                poses.Add(p);
            }

            float now = Time.time;
            _frames.Add(new Frame { t = now, poses = poses });

            // trim to the rolling window
            while (_frames.Count > 2 && now - _frames[0].t > BufferSeconds)
                _frames.RemoveAt(0);
        }

        // ----- playback -----

        /// <summary>Begin playing the buffered clip from the start (or from a given offset).</summary>
        public void Play(float fromSeconds = 0f)
        {
            if (_frames.Count < 2) return;
            Playing = true;
            Playhead = Mathf.Clamp(fromSeconds, 0f, Length);
        }

        public void Stop() => Playing = false;

        /// <summary>Advance the playhead and apply recorded poses. Call from LateUpdate.</summary>
        public void ApplyPlayback(float dt)
        {
            if (!Playing || _frames.Count < 2) { Playing = false; return; }

            Playhead += dt * Speed;
            if (Playhead >= Length) { Playhead = Length; Playing = false; }
            if (Playhead < 0f) Playhead = 0f;

            ApplyAt(Playhead);
        }

        /// <summary>Apply the pose at an absolute playhead time without advancing (for scrubbing).</summary>
        public void ApplyAt(float seconds)
        {
            if (_frames.Count < 2) return;
            float target = _frames[0].t + Mathf.Clamp(seconds, 0f, Length);

            int b = 1;
            while (b < _frames.Count - 1 && _frames[b].t < target) b++;
            int a = b - 1;

            Frame fa = _frames[a], fb = _frames[b];
            float span = Mathf.Max(0.0001f, fb.t - fa.t);
            float f = Mathf.Clamp01((target - fa.t) / span);

            foreach (var pa in fa.poses)
            {
                if (pa.rig == null || pa.rig.headMesh == null) continue;
                // find the same rig in frame b
                Pose pb = pa; bool found = false;
                foreach (var cand in fb.poses) { if (cand.rig == pa.rig) { pb = cand; found = true; break; } }
                float ff = found ? f : 0f;

                SetTR(pa.rig.headMesh.transform, Vector3.Lerp(pa.hp, pb.hp, ff), Quaternion.Slerp(pa.hr, pb.hr, ff));
                SetTR(pa.rig.leftHandTransform, Vector3.Lerp(pa.lp, pb.lp, ff), Quaternion.Slerp(pa.lr, pb.lr, ff));
                SetTR(pa.rig.rightHandTransform, Vector3.Lerp(pa.rp, pb.rp, ff), Quaternion.Slerp(pa.rr, pb.rr, ff));
                SetTR(pa.rig.bodyTransform, Vector3.Lerp(pa.bp, pb.bp, ff), Quaternion.Slerp(pa.br, pb.br, ff));
            }
        }

        private static void SetTR(Transform t, Vector3 pos, Quaternion rot)
        {
            if (t != null) t.SetPositionAndRotation(pos, rot);
        }
    }
}
