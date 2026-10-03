// Input, laser, comfort, and haptics.
//
// Drone mapping:
//   Right stick   forward, back, strafe left and right
//   Left stick    forward: zoom in (descend); back: zoom out (climb); left/right: yaw
//   Right trigger the laser: select a marker (its data and sources show); pull on empty space to clear
//   A             show or hide the HUD (selection and filters are kept)
//   B             fly in to the selected marker (or wherever the laser points)
//   Y             turn to face north    X   previous slide (Next is on the HUD)
//   Right grip    hold for turbo (4x)   Left grip   hold for precision (1/4x)
//   Left stick click or Menu   recenter the HUD in front of you
// Keyboard and gamepad mirror it in the editor and for a presenter at the PC.
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace AtlasVR
{
    public class Controls : IDisposable
    {
        public readonly InputAction rightStick, leftStick, trigger, aBtn, bBtn, xBtn, yBtn, menu, leftClick, rightGrip, leftGrip;
        public readonly InputAction rightAimPos, rightAimRot, rightGripPos, rightGripRot, rightHandPos, rightHandRot, leftPos, leftRot, leftHandPos, leftHandRot;
        public readonly InputAction kbMove, kbClimb, kbYaw, next, prev, blank, spectator, toggleHud, teleport, select, north;

        public Controls()
        {
            rightStick = new InputAction("Right stick", InputActionType.Value, expectedControlType: "Vector2");
            rightStick.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
            rightStick.AddBinding("<XRController>{RightHand}/thumbstick");
            rightStick.AddBinding("<Gamepad>/rightStick");

            leftStick = new InputAction("Left stick", InputActionType.Value, expectedControlType: "Vector2");
            leftStick.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
            leftStick.AddBinding("<XRController>{LeftHand}/thumbstick");
            leftStick.AddBinding("<Gamepad>/leftStick");

            trigger = Button("Trigger", "<XRController>{RightHand}/{TriggerButton}", "<XRController>{RightHand}/triggerPressed", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            aBtn = Button("A", "<XRController>{RightHand}/{PrimaryButton}", "<XRController>{RightHand}/primaryButton");
            bBtn = Button("B", "<XRController>{RightHand}/{SecondaryButton}", "<XRController>{RightHand}/secondaryButton");
            xBtn = Button("X", "<XRController>{LeftHand}/{PrimaryButton}", "<XRController>{LeftHand}/primaryButton");
            yBtn = Button("Y", "<XRController>{LeftHand}/{SecondaryButton}", "<XRController>{LeftHand}/secondaryButton");
            menu = Button("Menu", "<XRController>{LeftHand}/{MenuButton}", "<XRController>{LeftHand}/menu", "<XRController>{LeftHand}/menuButton");
            leftClick = Button("Left stick click", "<XRController>{LeftHand}/{Primary2DAxisClick}", "<XRController>{LeftHand}/thumbstickClicked", "<Keyboard>/p");
            rightGrip = Button("Right grip", "<XRController>{RightHand}/{GripButton}", "<XRController>{RightHand}/gripPressed");
            leftGrip = Button("Left grip", "<XRController>{LeftHand}/{GripButton}", "<XRController>{LeftHand}/gripPressed");

            // One binding per action (pass-through), so an action never flips between the aim and grip poses.
            rightAimPos = Pose("Right aim position", "Vector3", "<XRController>{RightHand}/pointerPosition");
            rightAimRot = Pose("Right aim rotation", "Quaternion", "<XRController>{RightHand}/pointerRotation");
            rightGripPos = Pose("Right grip position", "Vector3", "<XRController>{RightHand}/devicePosition");
            rightGripRot = Pose("Right grip rotation", "Quaternion", "<XRController>{RightHand}/deviceRotation");
            rightHandPos = Pose("Right hand position", "Vector3", "<XRHandDevice>{RightHand}/devicePosition");
            rightHandRot = Pose("Right hand rotation", "Quaternion", "<XRHandDevice>{RightHand}/deviceRotation");
            leftPos = Pose("Left position", "Vector3", "<XRController>{LeftHand}/devicePosition");
            leftRot = Pose("Left rotation", "Quaternion", "<XRController>{LeftHand}/deviceRotation");
            leftHandPos = Pose("Left hand position", "Vector3", "<XRHandDevice>{LeftHand}/devicePosition");
            leftHandRot = Pose("Left hand rotation", "Quaternion", "<XRHandDevice>{LeftHand}/deviceRotation");

            kbMove = new InputAction("Keyboard move", InputActionType.Value, expectedControlType: "Vector2");
            kbMove.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            kbClimb = new InputAction("Keyboard climb", InputActionType.Value, expectedControlType: "Axis");
            kbClimb.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/e").With("Negative", "<Keyboard>/q");
            kbYaw = new InputAction("Keyboard yaw", InputActionType.Value, expectedControlType: "Axis");
            kbYaw.AddCompositeBinding("1DAxis").With("Positive", "<Keyboard>/l").With("Negative", "<Keyboard>/j");

            next = Button("Next", "<Keyboard>/rightArrow", "<Keyboard>/pageDown", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            prev = Button("Previous", "<Keyboard>/leftArrow", "<Keyboard>/pageUp", "<Keyboard>/backspace", "<Gamepad>/buttonWest");
            blank = Button("Blank", "<Keyboard>/b", "<Keyboard>/period");
            spectator = Button("Spectator", "<Keyboard>/c");
            toggleHud = Button("Toggle HUD", "<Keyboard>/h", "<Gamepad>/buttonNorth");
            teleport = Button("Teleport", "<Keyboard>/t", "<Gamepad>/buttonEast");
            select = Button("Select", "<Keyboard>/enter");
            north = Button("North", "<Keyboard>/n");

            foreach (var a in All()) a.Enable();
        }

        static InputAction Button(string name, params string[] bindings)
        {
            var a = new InputAction(name, InputActionType.Button);
            foreach (var b in bindings) a.AddBinding(b);
            return a;
        }

        static InputAction Pose(string name, string type, string binding)
        {
            var a = new InputAction(name, InputActionType.PassThrough, expectedControlType: type);
            a.AddBinding(binding);
            return a;
        }

        InputAction[] All()
        {
            return new[] { rightStick, leftStick, trigger, aBtn, bBtn, xBtn, yBtn, menu, leftClick, rightGrip, leftGrip, rightAimPos, rightAimRot, rightGripPos, rightGripRot, rightHandPos, rightHandRot, leftPos, leftRot, leftHandPos, leftHandRot, kbMove, kbClimb, kbYaw, next, prev, blank, spectator, toggleHud, teleport, select, north };
        }

        public static Vector2 Dead(Vector2 v, float dz = 0.15f)
        {
            float m = v.magnitude;
            if (m < dz) return Vector2.zero;
            return v / m * Mathf.Pow((m - dz) / (1 - dz), 1.6f); // a soft curve: fine control near center, full speed at the edge
        }

        public void Dispose() { foreach (var a in All()) { a.Disable(); a.Dispose(); } }
    }

    public static class Haptics
    {
        // The XR input-device path is the one OpenXR honors for haptics on Touch controllers.
        public static void Pulse(bool right, float amplitude, float seconds)
        {
            try
            {
                var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(right ? UnityEngine.XR.XRNode.RightHand : UnityEngine.XR.XRNode.LeftHand);
                if (dev.isValid) dev.SendHapticImpulse(0, Mathf.Clamp01(amplitude), seconds);
            }
            catch (Exception) { }
        }
    }

    /// The comfort vignette (narrows the view while moving) and the full-view fade.
    public class Comfort
    {
        public enum Mode { Vignette, Fade, Off }
        public Mode mode = Mode.Vignette;

        readonly GameObject _vignette, _fade;
        readonly Material _vMat, _fMat;
        float _v, _fadeTarget, _fadeNow;
        public float FadeAlpha { get { return _fadeNow; } }

        public Comfort(Transform cam)
        {
            _vignette = new GameObject("Comfort vignette");
            _vignette.transform.SetParent(cam, false);
            _vignette.transform.localPosition = new Vector3(0, 0, 1f);
            _vignette.AddComponent<MeshFilter>().sharedMesh = FullScreenTriangle();
            _vMat = new Material(Shader.Find("AtlasVR/Vignette"));
            var r = _vignette.AddComponent<MeshRenderer>(); r.sharedMaterial = _vMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            _fade = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _fade.name = "Fade";
            UnityEngine.Object.Destroy(_fade.GetComponent<Collider>());
            _fade.transform.SetParent(cam, false);
            _fade.transform.localScale = Vector3.one * 0.3f;
            _fMat = new Material(Shader.Find("AtlasVR/Fade"));
            _fade.GetComponent<MeshRenderer>().sharedMaterial = _fMat;
            _fade.SetActive(false);
        }

        /// speed01: how hard the viewer is moving or turning (0 still, 1 full).
        public void Tick(float speed01, float dt)
        {
            float target = mode == Mode.Vignette ? Mathf.Clamp01(speed01) : 0f;
            _v = Mathf.MoveTowards(_v, target, dt * (target > _v ? 3f : 1.5f));
            bool on = _v > 0.01f;
            if (_vignette.activeSelf != on) _vignette.SetActive(on);
            if (on)
            {
                _vMat.SetFloat("_Inner", Mathf.Lerp(1.05f, 0.55f, _v)); // clear radius, in each eye's normalized view
                _vMat.SetFloat("_Alpha", Mathf.Lerp(0.3f, 1f, _v));
            }
            _fadeNow = Mathf.MoveTowards(_fadeNow, _fadeTarget, dt * 4.5f);
            bool f = _fadeNow > 0.001f;
            if (_fade.activeSelf != f) _fade.SetActive(f);
            if (f) _fMat.SetColor("_Color", new Color(0, 0, 0, _fadeNow));
        }

        public void FadeTo(float a) { _fadeTarget = Mathf.Clamp01(a); }

        // One triangle that covers the whole view; the shader places it in clip space.
        static Mesh FullScreenTriangle()
        {
            var m = new Mesh { name = "Vignette", vertices = new[] { new Vector3(-1, -1, 0), new Vector3(3, -1, 0), new Vector3(-1, 3, 0) }, triangles = new[] { 0, 1, 2 } };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1.0e6f); // never culled
            return m;
        }
    }

    /// The laser from the right hand, with a reticle where it meets the Earth or a marker.
    public class Laser
    {
        readonly LineRenderer _line;
        readonly Transform _dot;
        public Laser(Transform parent)
        {
            var go = new GameObject("Laser");
            go.transform.SetParent(parent, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.widthCurve = new AnimationCurve(new Keyframe(0, 0.0035f), new Keyframe(1, 0.0012f));
            var mat = new Material(Shader.Find("AtlasVR/VertexColor"));
            _line.sharedMaterial = mat;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.4f, 0.75f), 0), new GradientColorKey(new Color(0.6f, 0.92f, 0.97f), 1) },
                      new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0.15f, 1) });
            _line.colorGradient = g;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "Reticle";
            UnityEngine.Object.Destroy(dot.GetComponent<Collider>());
            dot.transform.SetParent(parent, false);
            var dm = new Material(Shader.Find("AtlasVR/Fade"));
            dm.SetColor("_Color", new Color(1f, 0.85f, 0.95f, 0.9f));
            dot.GetComponent<MeshRenderer>().sharedMaterial = dm;
            _dot = dot.transform;
        }

        public void Set(bool on, Vector3 from, Vector3 to, bool hit, Transform eye)
        {
            _line.enabled = on;
            _dot.gameObject.SetActive(on && hit);
            if (!on) return;
            _line.SetPosition(0, from);
            _line.SetPosition(1, to);
            if (hit && eye != null)
            {
                _dot.position = to;
                _dot.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(eye.position, to) * 0.012f, 0.004f, 50f);
            }
        }
    }
}
