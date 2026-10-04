// Mixed reality: your room in place of space. With it on, the camera clears to transparent and
// the headset's passthrough shows through wherever the Earth, its sky, and the panels do not
// cover the view; the star field steps aside. Off by default, and nothing changes until it is
// turned on. It needs the Meta OpenXR camera feature (Public Hyperscale > Configure for Meta
// Quest turns it on); without it the switch reports that passthrough is not available.
// AR Foundation is reached by name, so the app builds and runs the same without it.
using System;
using UnityEngine;

namespace AtlasVR
{
    public class MixedReality
    {
        Behaviour _cameraManager;
        GameObject _session;
        bool _tried;
        float _checkAt = -1f;

        public bool On { get; private set; }
        public string Problem { get; private set; }

        /// Turns passthrough on or off; false when it could not be started.
        public bool Set(bool on, Camera cam)
        {
            if (on == On) return true;
            if (on && !_tried) Init(cam);
            if (_cameraManager == null) { On = false; return false; }
            if (on && _session != null && !_session.activeSelf) _session.SetActive(true);
            _cameraManager.enabled = on;
            On = on;
            if (on) _checkAt = Time.unscaledTime + 1.5f;
            return true;
        }

        /// A moment after turning on: if the camera subsystem never started, passthrough is not
        /// available on this build; returns the problem once, and turns itself off.
        public string Check()
        {
            if (!On || _checkAt < 0f || Time.unscaledTime < _checkAt) return null;
            _checkAt = -1f;
            object sub = null;
            try
            {
                var p = _cameraManager.GetType().GetProperty("subsystem");
                sub = p != null ? p.GetValue(_cameraManager, null) : null;
            }
            catch (Exception) { }
            if (sub != null) return null;
            Problem = "Passthrough is not available in this build: turn on Meta Quest: Camera (Passthrough) in the OpenXR settings";
            _cameraManager.enabled = false;
            On = false;
            return Problem;
        }

        void Init(Camera cam)
        {
            _tried = true;
            var tm = Type.GetType("UnityEngine.XR.ARFoundation.ARCameraManager, Unity.XR.ARFoundation");
            var ts = Type.GetType("UnityEngine.XR.ARFoundation.ARSession, Unity.XR.ARFoundation");
            if (tm == null || ts == null || cam == null) { Problem = "Passthrough needs AR Foundation"; return; }
            var existing = UnityEngine.Object.FindAnyObjectByType(ts) as Component;
            if (existing != null) _session = existing.gameObject;
            else { _session = new GameObject("AR Session (passthrough)"); _session.AddComponent(ts); }
            var c = cam.GetComponent(tm);
            if (c == null) c = cam.gameObject.AddComponent(tm);
            _cameraManager = c as Behaviour;
            if (_cameraManager != null) _cameraManager.enabled = false;
        }
    }
}
