// Hyperscale in the Public Interest, in a headset. The one component the scene needs: it
// finds the XR rig (or makes a desktop one), loads the package, builds the globe, the layers,
// the station, the wrist menu, and runs the flight, the laser, and the deck.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace AtlasVR
{
    [DefaultExecutionOrder(100)]
    public class AtlasVRApp : MonoBehaviour
    {
        [Header("Start")]
        public ViewMode startMode = ViewMode.Flight;
        public Posture startPosture = Posture.Standing;
        [Tooltip("Open the deck with a descent from deep space to the first slide.")]
        public bool introDescent = true;

        [Header("Flight (drone mapping)")]
        [Tooltip("Horizontal speed at full stick, as a multiple of height above ground per second.")]
        public float flySpeed = 1.0f;
        [Tooltip("Climb and dive rate at full stick (e-folds per second).")]
        public float climbRate = 1.2f;
        [Tooltip("Yaw rate at full stick, degrees per second.")]
        public float yawRate = 75f;
        [Tooltip("Left stick up descends instead of climbing.")]
        public bool invertClimb = false;
        [Tooltip("Turn in 30-degree steps instead of smoothly.")]
        public bool snapTurn = false;
        [Tooltip("Height above ground (m) a B-button teleport lands at.")]
        public float snapHeight = 900f;
        public Comfort.Mode comfortMode = Comfort.Mode.Vignette;
        [Tooltip("A light hum in the right controller while flying fast.")]
        public bool engineRumble = true;

        [Header("Display")]
        public bool photorealCloseUps = true;
        [Range(0.7f, 1.4f)] public float renderScale = 1.0f;
        public float questRefreshRate = 90f;

        AtlasPackage _pkg;
        GlobeRig _rig;
        Controls _in;
        Comfort _comfort;
        Laser _laser;
        StationPanel _panel;
        MarkerCard _card;
        WristMenu _wrist;
        VRCredits _credits;
        Spectator _spectator;
        ComputeLayer _compute;
        CableLayer _cables;
        SitesLayer _sites;
        readonly List<IPickable> _pickables = new List<IPickable>();

        Camera _cam;
        Transform _eye, _tracking, _floor;
        bool _xr;

        // Deck position and the trail for Back across stories.
        string _track = "main";
        int _index;
        readonly Stack<KeyValuePair<string, int>> _history = new Stack<KeyValuePair<string, int>>();
        PkgSlide _slide;

        // Flight state.
        Vector2 _vel;           // smoothed right stick (forward, strafe)
        float _climbVel, _yawVel;
        bool _snapArmed = true, _snapFaded;
        Travel _travel;
        Vector3 _gripPrev, _gripPrevL;
        float _gripDist;

        class Travel
        {
            public double lon0, lat0, h0, hd0, lon1, lat1, h1, hd1, peakLog;
            public float t, dur;
            public bool overview, fade, landed;
            public Task<CesiumSampleHeightResult> ground;
            public double groundAdd;
        }

        IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.runInBackground = false;

            FindRig();
            Perf.Apply(renderScale, questRefreshRate);
            DisableTemplateLocomotion();
            UI.EnsureEventSystem();

            _in = new Controls();
            _comfort = new Comfort(_eye) { mode = comfortMode };
            _laser = new Laser(transform);

            yield return AtlasPackage.Load(p => _pkg = p);
            if (_pkg.error != null) { ShowError(_pkg.error); yield break; }

            var cfg = AtlasPackage.ReadLocalConfig();
            _rig = new GameObject("Globe rig").AddComponent<GlobeRig>();
            _rig.mode = startMode;
            _rig.posture = startPosture;
            _rig.photorealCloseUps = photorealCloseUps;
            _rig.Build(cfg.ionToken, _cam, _floor);

            var layerRoot = new GameObject("Layers").transform;
            if (_pkg.compute != null) { _compute = new ComputeLayer(_pkg.compute, layerRoot); _pickables.Add(_compute); }
            if (_pkg.tele != null) { _cables = new CableLayer(_pkg.tele); _pickables.Add(_cables); }
            if (_pkg.sites != null) { _sites = new SitesLayer(_pkg.sites, layerRoot); _pickables.Add(_sites); }

            var ui = new GameObject("Interface").transform;
            _panel = new StationPanel(_pkg, ui, _cam, this);
            _panel.Next += Next; _panel.Back += Prev; _panel.Explore += Explore; _panel.StatPressed += StatView;
            _card = new MarkerCard(_pkg, ui, _cam);
            _card.GoThere += info => TeleportTo(info.lon, info.lat);
            _wrist = new WristMenu(ui, _cam, _pkg.deck);
            WireWrist();
            _credits = new VRCredits(ui, _cam, "Data centers: PeeringDB. AI compute: Epoch AI. Submarine cables: TeleGeography (CC BY-NC-SA 3.0).");
            _spectator = new Spectator(_eye);
            SubscribeRecenter();

            Recenter();
            if (introDescent && _rig.mode == ViewMode.Flight) _rig.SetPose(-30, 20, 4.0e7, 0);
            GoTo("main", 0, true);
        }

        // ---------------------------------------------------------------- rig
        void FindRig()
        {
            _cam = Camera.main;
            _xr = XRSettings.isDeviceActive;
            var originType = Type.GetType("Unity.XR.CoreUtils.XROrigin, Unity.XR.CoreUtils");
            UnityEngine.Object origin = originType != null ? FindAnyObjectByType(originType) : null;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                _cam = go.AddComponent<Camera>();
                go.transform.position = new Vector3(0, 1.6f, 0);
                go.AddComponent<DesktopLook>();
            }
            else if (!_xr && _cam.GetComponent<DesktopLook>() == null) _cam.gameObject.AddComponent<DesktopLook>();
            _eye = _cam.transform;
            _tracking = _eye.parent != null ? _eye.parent : _eye;
            _floor = origin != null ? ((Component)origin).transform : (_eye.parent != null ? _eye.parent : new GameObject("Floor").transform);
            _cam.nearClipPlane = 0.05f;
        }

        void DisableTemplateLocomotion()
        {
            // The XR template's move, turn, teleport, climb, and gravity providers would fight the drone
            // controls; its rays and pokes stay on for the menus.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb == this) continue;
                string ns = mb.GetType().Namespace ?? "";
                string n = mb.GetType().Name;
                if (ns.StartsWith("UnityEngine.XR.Interaction.Toolkit.Locomotion") || n == "DynamicMoveProvider" || n == "TeleportationProvider" || n == "GravityProvider"
                    || n == "ControllerInputActionManager")   // the Starter Assets' teleport-mode switch on the thumbstick
                    mb.enabled = false;
                if (mb.gameObject.name.Contains("Teleport Interactor") || mb.gameObject.name.Contains("Teleport Stabilized")) mb.gameObject.SetActive(false);
            }
            var cc = _floor != null ? _floor.GetComponent<CharacterController>() : null;
            if (cc != null) cc.enabled = false;
        }

        void SubscribeRecenter()
        {
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            foreach (var s in subs) s.trackingOriginUpdated += _ => StartCoroutine(RecenterNextFrame());
        }

        IEnumerator RecenterNextFrame() { yield return null; Recenter(); }

        void Recenter()
        {
            if (_rig == null) return;
            Vector3 f = _eye.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
            _rig.Place(_eye.position, f, _floor.position.y);
            Vector3 left = Quaternion.Euler(0, -34f, 0) * f;
            float eyeY = _rig.posture == Posture.Seated ? _floor.position.y + 1.15f : Mathf.Max(_eye.position.y, _floor.position.y + 1.45f);
            Vector3 p = new Vector3(_eye.position.x, eyeY - 0.12f, _eye.position.z) + left * 1.15f;
            var pt = _panel.canvas.transform;
            pt.position = p;
            pt.rotation = Quaternion.LookRotation(p - new Vector3(_eye.position.x, eyeY, _eye.position.z), Vector3.up);
            var ct = _credits.canvas.transform;
            ct.position = p - pt.up * 0.43f;
            ct.rotation = pt.rotation;
        }

        // ---------------------------------------------------------------- deck
        PkgTrack Track(string id) { return _pkg.Track(id) ?? _pkg.Track("main"); }

        void GoTo(string track, int index, bool instant = false)
        {
            var t = Track(track);
            if (t == null || t.beats == null || t.beats.Length == 0) return;
            _track = t.id;
            _index = Mathf.Clamp(index, 0, t.beats.Length - 1);
            var beat = t.beats[_index];
            _slide = _pkg.Slide(beat.id);
            if (_slide == null) return;

            var ch = _pkg.Chapter(beat.chapter);
            string line = _track == "main" ? (_slide.chapter ?? "") : (ch != null && ch.title != t.title ? t.title + " · " + ch.title : t.title);
            _panel.Show(_slide, line, id => { var x = _pkg.Track(id); return x != null ? x.title : id; });
            _card.Close();
            ApplyShow(_slide.show);

            var main = Track("main");
            int mainIndex = _track == "main" ? _index : MainIndexInHistory();
            _wrist.SetPositions(mainIndex, main != null && main.beats != null ? main.beats.Length : 1, t.title, _index, _track == "main" ? 0 : t.beats.Length);
            _wrist.SetLegend(_slide.show);
            _wrist.SetModes(_rig, _comfort);

            var c = _slide.camera;
            if (HasCamera(c)) Fly(c, instant && !introDescent);
            Haptics.Pulse(true, 0.15f, 0.04f);
        }

        void ApplyShow(PkgShow s)
        {
            if (_compute != null) _compute.SetShow(s != null ? s.compute : null);
            if (_cables != null) _cables.SetShow(s != null ? s.telecables : null);
            if (_sites != null) _sites.SetShow(s);
            _rig.SetSlideLayers(s != null ? s.night : 0, s != null && s.photoreal, s != null && s.spin && _rig.mode != ViewMode.Flight);
        }

        void Next()
        {
            var t = Track(_track);
            if (_index < t.beats.Length - 1) { GoTo(_track, _index + 1); return; }
            if (_history.Count > 0) { var h = _history.Pop(); GoTo(h.Key, h.Value + 1); }
        }

        void Prev()
        {
            if (_index > 0) { GoTo(_track, _index - 1); return; }
            if (_history.Count > 0) { var h = _history.Pop(); GoTo(h.Key, h.Value); }
        }

        void Explore(string track)
        {
            if (_pkg.Track(track) == null) return;
            _history.Push(new KeyValuePair<string, int>(_track, _index));
            GoTo(track, 0);
        }

        int MainIndexInHistory()
        {
            foreach (var h in _history) if (h.Key == "main") return h.Value;
            return 0;
        }

        void OpenTrack(string track)
        {
            int mainIndex = _track == "main" ? _index : MainIndexInHistory();
            _history.Clear();
            if (track == "main") { GoTo("main", mainIndex); return; }
            _history.Push(new KeyValuePair<string, int>("main", mainIndex));
            GoTo(track, 0);
        }

        // A stat opens its master view: the compute stats isolate their layer, as on the web.
        void StatView(PkgStat st)
        {
            if (_slide == null || _slide.show == null || _slide.show.compute == null || !_slide.show.compute.on || _compute == null) return;
            string refs = string.Join(" ", st.refs ?? new string[0]);
            var copy = new PkgComputeShow { on = true, dcs = _slide.show.compute.dcs, ai = _slide.show.compute.ai, countries = _slide.show.compute.countries };
            if (refs.Contains("peeringdb")) { copy.dcs = true; copy.ai = false; }
            else if (refs.Contains("epoch")) { copy.dcs = false; copy.ai = true; }
            _compute.SetShow(copy);
        }

        // ---------------------------------------------------------------- travel
        void Fly(PkgCamera c, bool instant)
        {
            double lon1, lat1, h1, hd1 = c.heading;
            if (_rig.mode == ViewMode.Flight)
            {
                double L = c.viewHeight;
                double pitch = Math.Abs(c.pitch) < 1 ? 45 : Math.Abs(c.pitch);
                if (pitch >= 80) { lon1 = c.lon; lat1 = c.lat; h1 = L * 0.9; }
                else
                {
                    double back = Math.Min(L * Math.Cos(pitch * Math.PI / 180), 2.5e6);
                    h1 = Math.Max(400, L * Math.Sin(pitch * Math.PI / 180));
                    Wgs84.Destination(c.lon, c.lat, hd1 + 180, back, out lon1, out lat1);
                    hd1 = Wgs84.Bearing(lon1, lat1, c.lon, c.lat);
                }
            }
            else { lon1 = c.lon; lat1 = c.lat; h1 = c.viewHeight; }
            StartTravel(lon1, lat1, h1, hd1, instant, null);
        }

        /// B: fly to a place and snap in close, facing it.
        void TeleportTo(double lon, double lat)
        {
            if (_rig.mode != ViewMode.Flight)
            {
                // From the overview, dive into the full-scale Earth.
                _rig.SetMode(ViewMode.Flight);
                ApplyShow(_slide != null ? _slide.show : null);
                _wrist.SetModes(_rig, _comfort);
            }
            // The approach course at the target: the back-azimuth from the target toward where we are.
            double bearing = Wgs84.AngleDeg(_rig.Lon, _rig.Lat, lon, lat) < 0.01 ? _rig.Heading : (Wgs84.Bearing(lon, lat, _rig.Lon, _rig.Lat) + 180) % 360;
            double lon1, lat1;
            Wgs84.Destination(lon, lat, bearing + 180, snapHeight * 1.5, out lon1, out lat1);
            StartTravel(lon1, lat1, snapHeight, bearing, false, _rig.SampleAt(lon, lat));
            _card.Close();
            Haptics.Pulse(true, 0.5f, 0.08f);
        }

        void StartTravel(double lon1, double lat1, double h1, double hd1, bool instant, Task<CesiumSampleHeightResult> ground)
        {
            _rig.StopSpin();
            if (instant) { _rig.SetPose(lon1, lat1, h1, hd1); _travel = null; return; }
            var tr = new Travel
            {
                lon0 = _rig.Lon, lat0 = _rig.Lat, h0 = _rig.Height, hd0 = _rig.Heading,
                lon1 = lon1, lat1 = lat1, h1 = h1, hd1 = hd1,
                overview = _rig.mode != ViewMode.Flight,
                fade = _comfort.mode == Comfort.Mode.Fade,
                ground = ground,
            };
            double dist = Wgs84.AngleDeg(tr.lon0, tr.lat0, lon1, lat1) * Math.PI / 180 * Wgs84.A;
            tr.dur = Mathf.Clamp(1.6f + 0.55f * (float)Math.Log10(1 + dist / 1000.0), 1.6f, 5f);
            if (tr.overview) tr.dur = Mathf.Min(tr.dur, 2.2f);
            double hiLog = Math.Max(Math.Log(Math.Max(1, tr.h0)), Math.Log(Math.Max(1, h1)));
            double peak = tr.overview ? 0 : Math.Log(Math.Max(1, dist * 0.45));
            tr.peakLog = Math.Max(0, peak - hiLog);
            if (tr.fade) tr.dur = 0.9f;
            _travel = tr;
        }

        float TickTravel(float dt)
        {
            var tr = _travel;
            if (tr == null) return 0f;
            float motion = 0f;
            if (tr.ground != null)
            {
                double gh;
                if (GlobeRig.SampleResult(tr.ground, out gh)) { tr.groundAdd = Math.Max(0, gh); tr.ground = null; }
                else if (tr.ground.IsCompleted) tr.ground = null;
            }
            tr.t += dt / tr.dur;
            float t = Mathf.Clamp01(tr.t);
            double h1 = tr.h1 + tr.groundAdd;
            if (tr.fade)
            {
                _comfort.FadeTo(t < 0.5f ? 1f : 0f);
                if (t >= 0.45f && !tr.landed) { _rig.SetPose(tr.lon1, tr.lat1, h1, tr.hd1); tr.landed = true; }
            }
            else
            {
                double e = t * t * (3 - 2 * t);           // ease in and out
                double lon, lat;
                Wgs84.Slerp(tr.lon0, tr.lat0, tr.lon1, tr.lat1, e, out lon, out lat);
                double logH = Math.Log(Math.Max(1, tr.h0)) * (1 - e) + Math.Log(Math.Max(1, h1)) * e + tr.peakLog * Math.Sin(Math.PI * t);
                double dh = ((tr.hd1 - tr.hd0 + 540) % 360) - 180;
                _rig.SetPose(lon, lat, Math.Exp(logH), tr.hd0 + dh * e);
                motion = Mathf.Sin(Mathf.PI * t);
            }
            if (tr.t >= 1f)
            {
                if (!tr.fade) _rig.SetPose(tr.lon1, tr.lat1, h1, tr.hd1);
                _travel = null;
                Haptics.Pulse(true, 0.35f, 0.06f);
                if (_slide != null && _slide.show != null && _slide.show.spin && _rig.mode != ViewMode.Flight) _rig.SetSlideLayers(_slide.show.night, _slide.show.photoreal, true);
            }
            return motion;
        }

        // ---------------------------------------------------------------- frame
        void Update()
        {
            if (_rig == null || _in == null) return;
            float dt = Time.deltaTime;

            // Buttons.
            if (_in.aBtn.WasPressedThisFrame() || _in.next.WasPressedThisFrame()) Next();
            if (_in.xBtn.WasPressedThisFrame() || _in.prev.WasPressedThisFrame()) Prev();
            if (_in.yBtn.WasPressedThisFrame() || _in.overview.WasPressedThisFrame()) ToggleFlight();
            if (_in.leftClick.WasPressedThisFrame()) _panel.canvas.gameObject.SetActive(!_panel.canvas.gameObject.activeSelf);
            if (_in.menu.WasPressedThisFrame()) _wrist.pinned = !_wrist.pinned;
            if (_in.blank.WasPressedThisFrame()) _comfort.FadeTo(_comfort.FadeAlpha > 0.5f ? 0f : 1f);
            if (_in.spectator.WasPressedThisFrame()) _spectator.Toggle();

            // Sticks.
            Vector2 r = Controls.Dead(_in.rightStick.ReadValue<Vector2>()) + _in.kbMove.ReadValue<Vector2>();
            Vector2 l = Controls.Dead(_in.leftStick.ReadValue<Vector2>());
            l.y += _in.kbClimb.ReadValue<float>();
            l.x += _in.kbYaw.ReadValue<float>();
            r = Vector2.ClampMagnitude(r, 1f); l.x = Mathf.Clamp(l.x, -1f, 1f); l.y = Mathf.Clamp(l.y, -1f, 1f);
            bool userMoving = r.sqrMagnitude > 0.0001f || Mathf.Abs(l.x) > 0.01f || Mathf.Abs(l.y) > 0.01f;
            if (userMoving) { if (_travel != null && _travel.fade) _comfort.FadeTo(0f); _travel = null; _rig.StopSpin(); }

            // Drone inertia: the craft eases up to speed and glides to a stop.
            float k = 1f - Mathf.Exp(-dt * 5f);
            _vel = Vector2.Lerp(_vel, r, k);
            _climbVel = Mathf.Lerp(_climbVel, l.y, k);
            _yawVel = Mathf.Lerp(_yawVel, l.x, 1f - Mathf.Exp(-dt * 8f));

            if (_travel == null)
            {
                Vector3 fwd = Flat(_eye.forward), right = Flat(_eye.right);
                double h = Math.Max(30, _rig.ViewHeight);
                double rate = _rig.mode == ViewMode.Flight ? flySpeed * h : 0.8 * _rig.Height;
                if (_vel.sqrMagnitude > 1e-6f) _rig.Travel((fwd * _vel.y + right * _vel.x).normalized, rate * _vel.magnitude * dt);
                // Drone mode 2: left stick up climbs (zooms out), down descends (zooms in).
                if (Mathf.Abs(_climbVel) > 1e-4f) _rig.Climb(Math.Exp((invertClimb ? -_climbVel : _climbVel) * climbRate * dt));
                if (snapTurn)
                {
                    if (Mathf.Abs(l.x) > 0.7f && _snapArmed) { _rig.Turn(Mathf.Sign(l.x) * 30f); _snapArmed = false; _comfort.FadeTo(0.6f); _snapFaded = true; }
                    if (Mathf.Abs(l.x) < 0.3f) { _snapArmed = true; if (_snapFaded) { _comfort.FadeTo(0f); _snapFaded = false; } }
                }
                else if (Mathf.Abs(_yawVel) > 1e-3f) _rig.Turn(_yawVel * yawRate * dt);

                float motion = Mathf.Clamp01(_vel.magnitude * 0.9f + Mathf.Abs(_climbVel) * 0.5f + (snapTurn ? 0f : Mathf.Abs(_yawVel)));
                _comfort.Tick(motion, dt);
                Rumble(motion, dt);
                if (_rig.mode != ViewMode.Flight) Grips();
            }
            else
            {
                float motion = TickTravel(dt);
                _comfort.Tick(motion, dt);
                Rumble(motion, dt);
            }

            Pointer();
        }

        // A light engine hum in the right hand at speed.
        float _rumbleNext;
        void Rumble(float motion, float dt)
        {
            if (!engineRumble || _rig.mode != ViewMode.Flight || motion < 0.55f || Time.time < _rumbleNext) return;
            _rumbleNext = Time.time + 0.12f;
            Haptics.Pulse(true, 0.04f + 0.1f * (motion - 0.55f), 0.1f);
        }

        void Grips()
        {
            Vector3 rp, lp; Quaternion rr, lr;
            bool rt = HandPose(true, out rp, out rr), lt = HandPose(false, out lp, out lr);
            bool rg = rt && _in.rightGrip.IsPressed(), lg = lt && _in.leftGrip.IsPressed();
            if (rg && lg)
            {
                float d = Vector3.Distance(rp, lp);
                if (_gripDist > 0.01f && d > 0.01f) _rig.Climb(_gripDist / d);
                _gripDist = d;
            }
            else
            {
                _gripDist = 0;
                if (rg && _gripPrev != Vector3.zero) { Vector3 dlt = rp - _gripPrev; dlt.y = 0; _rig.DragWorld(dlt); }
                else if (lg && _gripPrevL != Vector3.zero) { Vector3 dlt = lp - _gripPrevL; dlt.y = 0; _rig.DragWorld(dlt); }
            }
            _gripPrev = rg ? rp : Vector3.zero;
            _gripPrevL = lg ? lp : Vector3.zero;
        }

        /// The hand's pose in the world: for the right hand the aim pose (the laser), else the grip,
        /// else a tracked hand; for the left hand the grip, else a tracked hand.
        InputAction[] _rightPoses, _leftPoses;
        bool HandPose(bool right, out Vector3 pos, out Quaternion rot)
        {
            if (_rightPoses == null)
            {
                _rightPoses = new[] { _in.rightAimPos, _in.rightAimRot, _in.rightGripPos, _in.rightGripRot, _in.rightHandPos, _in.rightHandRot };
                _leftPoses = new[] { _in.leftPos, _in.leftRot, _in.leftHandPos, _in.leftHandRot };
            }
            var pairs = right ? _rightPoses : _leftPoses;
            for (int i = 0; i < pairs.Length; i += 2)
            {
                Quaternion q = pairs[i + 1].ReadValue<Quaternion>();
                if (q.x == 0 && q.y == 0 && q.z == 0 && q.w == 0) continue;
                pos = _tracking.TransformPoint(pairs[i].ReadValue<Vector3>());
                rot = _tracking.rotation * q;
                return true;
            }
            pos = _eye.position; rot = _eye.rotation;
            return false;
        }

        Ray PointerRay(out bool fromHand)
        {
            Vector3 p; Quaternion q;
            fromHand = HandPose(true, out p, out q);
            if (fromHand) return new Ray(p, q * Vector3.forward);
            if (Mouse.current != null && _cam != null && !_xr) return _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            return new Ray(_eye.position, _eye.forward);
        }

        void Pointer()
        {
            bool fromHand;
            Ray ray = PointerRay(out fromHand);
            float uiDist;
            bool overUi = UI.RayHitsCanvas(ray, out uiDist);
            double lon = 0, lat = 0;
            Vector3 hitWorld = Vector3.zero;
            bool earth = !overUi && _rig.RayToEarth(ray, out lon, out lat, out hitWorld);
            Vector3 end = overUi ? ray.GetPoint(uiDist) : (earth ? hitWorld : ray.GetPoint(3f));
            _laser.Set(fromHand, ray.origin, end, earth, _eye);

            if (!overUi && (_in.trigger.WasPressedThisFrame() || _in.select.WasPressedThisFrame()))
            {
                var hit = PickAt(ray);
                if (hit != null)
                {
                    if (_card.IsOpen && _card.Info != null && _card.Info.title == hit.title) _card.Close();
                    else { _card.Open(hit); Haptics.Pulse(true, 0.3f, 0.05f); }
                }
                else _card.Close();
            }
            if (_in.bBtn.WasPressedThisFrame() || _in.teleport.WasPressedThisFrame())
            {
                var hit = overUi ? null : PickAt(ray);
                if (hit != null && (hit.lon != 0 || hit.lat != 0)) TeleportTo(hit.lon, hit.lat);
                else if (earth) TeleportTo(lon, lat);
                else if (_card.IsOpen && _card.Info != null) TeleportTo(_card.Info.lon, _card.Info.lat);
            }
        }

        PickInfo PickAt(Ray ray)
        {
            PickInfo best = null;
            float bestAngle = _xr ? 1.4f : 0.9f;
            foreach (var p in _pickables)
            {
                float a;
                var h = p.Pick(ray, _rig, _eye.position, bestAngle, out a);
                if (h != null && a < bestAngle) { best = h; bestAngle = a; }
            }
            return best;
        }

        void LateUpdate()
        {
            if (_rig == null) return;
            if (_compute != null) _compute.Draw(_rig, _eye);
            if (_cables != null) _cables.Draw(_rig);
            if (_sites != null) _sites.Draw(_rig, _eye);
            _card.Follow(_rig, _eye);
            Vector3 lp; Quaternion lr;
            bool lt = HandPose(false, out lp, out lr);
            _wrist.Follow(lp, lr, lt, _eye);
            _credits.Tick();
            _spectator.Tick(_cam);
            if (Time.unscaledTime >= _statusNext) { _statusNext = Time.unscaledTime + 0.25f; _wrist.SetStatus(Status()); }
        }

        float _statusNext;
        string Status()
        {
            double h = _rig.ViewHeight;
            string alt = h >= 1e5 ? (h / 1000).ToString("N0") + " km" : h >= 1e4 ? (h / 1000).ToString("0.0") + " km" : h.ToString("N0") + " m";
            return (_rig.mode == ViewMode.Flight ? "Altitude " : "View ") + alt + "   " + Math.Abs(_rig.Lat).ToString("0.00") + (_rig.Lat >= 0 ? "N " : "S ") + Math.Abs(_rig.Lon).ToString("0.00") + (_rig.Lon >= 0 ? "E" : "W");
        }

        // ---------------------------------------------------------------- menu
        void WireWrist()
        {
            _wrist.Next += Next;
            _wrist.Back += Prev;
            _wrist.Recenter += Recenter;
            _wrist.TogglePanel += () => _panel.canvas.gameObject.SetActive(!_panel.canvas.gameObject.activeSelf);
            _wrist.ToggleFlight += ToggleFlight;
            _wrist.ToggleScale += () => { _rig.SetMode(_rig.mode == ViewMode.Room ? ViewMode.Tabletop : ViewMode.Room); Recenter(); AfterModeChange(); };
            _wrist.TogglePosture += () => { _rig.posture = _rig.posture == Posture.Seated ? Posture.Standing : Posture.Seated; Recenter(); _wrist.SetModes(_rig, _comfort); };
            _wrist.TogglePhotoreal += () => { _rig.photorealCloseUps = !_rig.photorealCloseUps; _wrist.SetModes(_rig, _comfort); };
            _wrist.CycleComfort += () => { _comfort.mode = (Comfort.Mode)(((int)_comfort.mode + 1) % 3); _wrist.SetModes(_rig, _comfort); };
            _wrist.MainSlide += i => { _history.Clear(); GoTo("main", i); };
            _wrist.SubSlide += i => GoTo(_track, i);
            _wrist.OpenTrack += OpenTrack;
        }

        void ToggleFlight()
        {
            _rig.SetMode(_rig.mode == ViewMode.Flight ? ViewMode.Tabletop : ViewMode.Flight);
            Recenter();
            AfterModeChange();
        }

        void AfterModeChange()
        {
            ApplyShow(_slide != null ? _slide.show : null);
            _wrist.SetModes(_rig, _comfort);
            if (_slide != null && HasCamera(_slide.camera)) Fly(_slide.camera, false);
        }

        void ShowError(string message)
        {
            UI.EnsureEventSystem();
            var c = UI.WorldCanvas("Message", null, new Vector2(900, 300), _cam);
            var bg = UI.Box("Background", c.transform, UI.Panel); UI.Stretch(bg.rectTransform);
            var t = UI.Label("Text", c.transform, message, 34, UI.Text); UI.Stretch(t.rectTransform, 40, 40, 40, 40);
            c.transform.position = _eye.position + Flat(_eye.forward) * 1.4f;
            c.transform.rotation = Quaternion.LookRotation(c.transform.position - _eye.position, Vector3.up);
            Debug.LogError("[Public Hyperscale] " + message);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-6f ? Vector3.forward : v.normalized; }

        // JsonUtility builds every nested object, so a slide without a camera has an empty one.
        static bool HasCamera(PkgCamera c) { return c != null && !string.IsNullOrEmpty(c.type) && c.type != "none" && c.viewHeight > 0; }

        void OnDestroy() { if (_in != null) _in.Dispose(); Perf.Restore(); }
    }

    /// Mouse look for testing in the editor without a headset: hold the right button and move.
    public class DesktopLook : MonoBehaviour
    {
        float _yaw, _pitch;
        void Update()
        {
            if (XRSettings.isDeviceActive || Mouse.current == null) return;
            if (Mouse.current.rightButton.isPressed)
            {
                Vector2 d = Mouse.current.delta.ReadValue() * 0.15f;
                _yaw += d.x; _pitch = Mathf.Clamp(_pitch - d.y, -85f, 85f);
                transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0);
            }
        }
    }

    /// Frame-rate settings for the headset: foveation, refresh rate, render scale.
    public static class Perf
    {
        static float _editorRenderScale = -1f;

        /// In the editor the URP asset is a project file; put its render scale back on exit.
        public static void Restore()
        {
            if (!Application.isEditor || _editorRenderScale < 0f) return;
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                var prop = rp != null ? rp.GetType().GetProperty("renderScale") : null;
                if (prop != null && prop.CanWrite) prop.SetValue(rp, _editorRenderScale, null);
            }
            catch (Exception) { }
        }

        public static void Apply(float renderScale, float refreshRate)
        {
            // URP drives the eye-texture scale from its asset each frame; set it there.
            try
            {
                var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                var prop = rp != null ? rp.GetType().GetProperty("renderScale") : null;
                if (prop != null && prop.CanWrite)
                {
                    if (Application.isEditor) _editorRenderScale = (float)prop.GetValue(rp, null);
                    prop.SetValue(rp, renderScale, null);
                }
                else XRSettings.eyeTextureResolutionScale = renderScale;
            }
            catch (Exception) { }
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
            {
                try
                {
                    d.foveatedRenderingLevel = 1.0f;
                    d.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
                }
                catch (Exception) { }
                TrySetRefreshRate(d, refreshRate);
            }
        }

        // OpenXR's Meta support exposes the refresh rate through a helper whose namespace has moved
        // between package versions; find it by name.
        static void TrySetRefreshRate(XRDisplaySubsystem d, float hz)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!asm.GetName().Name.StartsWith("Unity.XR")) continue;
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name != "DisplayUtilities") continue;
                        foreach (var name in new[] { "TryRequestDisplayRefreshRate", "TrySetDisplayRefreshRate" })
                        {
                            var m = t.GetMethod(name, new[] { typeof(XRDisplaySubsystem), typeof(float) });
                            if (m != null) { m.Invoke(null, new object[] { d, hz }); return; }
                        }
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
