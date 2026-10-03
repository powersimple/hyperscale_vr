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
        public Posture startPosture = Posture.Standing;
        [Tooltip("Open the deck with a descent from deep space to the first slide.")]
        public bool introDescent = true;

        [Header("Flight")]
        [Tooltip("Horizontal speed at full stick, as a multiple of height above ground per second.")]
        public float flySpeed = 1.0f;
        [Tooltip("Zoom rate at full stick (e-folds per second).")]
        public float climbRate = 1.2f;
        [Tooltip("Yaw rate at full stick, degrees per second.")]
        public float yawRate = 75f;
        [Tooltip("Left stick forward climbs instead of zooming in.")]
        public bool forwardClimbs = false;
        [Tooltip("Turn in 30-degree steps instead of smoothly.")]
        public bool snapTurn = false;
        [Tooltip("Height above ground (m) that B flies in to.")]
        public float snapHeight = 900f;
        [Tooltip("Speed multiplier while the right grip is held.")]
        public float turbo = 4f;
        [Tooltip("Speed multiplier while the left grip is held.")]
        public float precision = 0.25f;
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
        Hud _hud;
        Filters _filters;
        VRCredits _credits;
        Spectator _spectator;
        ComputeLayer _compute;
        CableLayer _cables;
        SitesLayer _sites;
        ProximityLabels _proximity;
        readonly List<IPickable> _pickables = new List<IPickable>();

        Camera _cam;
        Transform _eye, _tracking, _floor;
        bool _xr;

        // Deck position and the trail for Back across stories.
        string _track = "main";
        int _index;
        readonly Stack<KeyValuePair<string, int>> _history = new Stack<KeyValuePair<string, int>>();
        PkgSlide _slide;

        // Pointer state.
        PickInfo _hover, _selected;
        float _hoverNext;

        // Flight state.
        Vector2 _vel;           // smoothed right stick (forward, strafe)
        float _climbVel, _yawVel;
        bool _snapArmed = true, _snapFaded;
        Travel _travel;

        class Travel
        {
            public double lon0, lat0, h0, hd0, lon1, lat1, h1, hd1, peakLog;
            public float t, dur;
            public bool fade, landed;
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
            _rig.mode = ViewMode.Flight;
            _rig.posture = startPosture;
            _rig.photorealCloseUps = photorealCloseUps;
            _rig.Build(cfg.ionToken, _cam, _floor);

            var layerRoot = new GameObject("Layers").transform;
            if (_pkg.compute != null) { _compute = new ComputeLayer(_pkg.compute, layerRoot); _pickables.Add(_compute); }
            if (_pkg.tele != null) { _cables = new CableLayer(_pkg.tele); _pickables.Add(_cables); }
            if (_pkg.sites != null) { _sites = new SitesLayer(_pkg.sites, layerRoot); _pickables.Add(_sites); }
            _proximity = new ProximityLabels(layerRoot);

            _filters = new Filters(_pkg.sites);
            _filters.Seed("photoreal", photorealCloseUps);
            _hud = new Hud(_pkg, _filters, _cam, this);
            _hud.Next += Next; _hud.Back += Prev; _hud.Explore += Explore; _hud.OpenTrack += OpenTrack;
            _hud.Scrub += i => GoTo(_track, i);
            _hud.StatPressed += StatView;
            _filters.Changed += ApplyFilters;
            _credits = new VRCredits(null, _cam, "Data centers: PeeringDB. AI compute: Epoch AI. Submarine cables: TeleGeography (CC BY-NC-SA 3.0).");
            _hud.PlaceCredits(_credits.canvas);
            _hud.ShowInfo(null, null);
            _spectator = new Spectator(_eye);
            SubscribeRecenter();

            Recenter();
            if (introDescent) _rig.SetPose(-30, 20, 4.0e7, 0);
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
            // The XR template's move, turn, teleport, climb, and gravity providers would fight the
            // flight controls; its rays and pokes stay on for the panels.
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
            if (_hud != null) _hud.Recenter();
        }

        // ---------------------------------------------------------------- deck
        PkgTrack Track(string id)
        {
            var t = _pkg.Track(id) ?? _pkg.Track("main");
            if (t == null && _pkg.deck != null && _pkg.deck.tracks != null && _pkg.deck.tracks.Length > 0) t = _pkg.deck.tracks[0];
            return t;
        }

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
            _hud.Show(_slide, line, id => { var x = _pkg.Track(id); return x != null ? x.title : id; });
            _hud.SetPosition(_track == "main" ? (_pkg.deck != null ? _pkg.deck.title : "Main story") : t.title, _index, t.beats.Length);
            _selected = null;
            _hud.ShowInfo(_hover, null);
            _filters.FromSlide(_slide.show);   // raises Changed, which applies the layers

            var c = _slide.camera;
            if (HasCamera(c)) Fly(c, instant && !introDescent);
            Haptics.Pulse(true, 0.15f, 0.04f);
        }

        /// The layers follow the filters: the slide's own settings for what it shows, and all of a
        /// category's data when the viewer turns on something the slide left off.
        void ApplyFilters()
        {
            var s = _slide != null ? _slide.show : null;
            if (_compute != null)
            {
                bool dc = _filters.On("dc"), ai = _filters.On("ai");
                _compute.SetShow(new PkgComputeShow { on = dc || ai, dcs = dc, ai = ai, countries = true });
            }
            if (_cables != null)
            {
                var hi = s != null && s.telecables != null && s.telecables.on && !_filters.Forced("cables") ? s.telecables.highlight : new string[0];
                _cables.SetShow(new PkgTeleShow { on = _filters.On("cables"), landings = true, highlight = hi });
            }
            if (_sites != null)
            {
                bool sitesOn = _filters.On("sites"), powerOn = _filters.On("power");
                string[] slideDc = s != null && s.dc != null ? s.dc : new string[0];
                string[] slidePw = s != null && s.power != null ? s.power : new string[0];
                var shown = new PkgShow
                {
                    // A slide that only highlights sites shows just those, unless the viewer asked for all.
                    dc = sitesOn ? (_filters.Forced("sites") ? new[] { "all" } : slideDc) : new string[0],
                    power = powerOn ? (slidePw.Length > 0 && !_filters.Forced("power") ? slidePw : new[] { "all" }) : new string[0],
                    highlight = s != null && s.highlight != null ? s.highlight : new string[0],
                    labels = s != null && s.labels != null ? s.labels : new string[0],
                };
                _sites.dataCentersOn = sitesOn;
                _sites.plantsOn = powerOn;
                _sites.SetShow(shown);
            }
            _rig.SetSlideLayers(_filters.On("night") ? 1f : 0f, s != null && s.photoreal && _filters.On("photoreal"), false);
            _rig.photorealCloseUps = _filters.On("photoreal");
            _proximity.enabled = _filters.On("labels");
        }

        void Next()
        {
            var t = Track(_track);
            if (t == null || t.beats == null) return;
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
            string refs = string.Join(" ", st.refs ?? new string[0]);
            bool pdb = refs.Contains("peeringdb"), epoch = refs.Contains("epoch");
            if (!pdb && !epoch) return;
            if (pdb != _filters.On("dc")) _filters.Toggle("dc");
            if (epoch != _filters.On("ai")) _filters.Toggle("ai");
        }

        // ---------------------------------------------------------------- travel
        /// A slide's view, flown so the place sits ahead and a little below, not under your feet.
        void Fly(PkgCamera c, bool instant)
        {
            double L = c.viewHeight, lon1, lat1, h1, hd1 = c.heading;
            if (L < 2.5e6)
            {
                const double depress = 20.0 * Math.PI / 180.0;
                h1 = Math.Max(400, L * Math.Sin(depress));
                double back = L * Math.Cos(depress);
                Wgs84.Destination(c.lon, c.lat, hd1 + 180, back, out lon1, out lat1);
                hd1 = Wgs84.Bearing(lon1, lat1, c.lon, c.lat);
            }
            else { lon1 = c.lon; lat1 = c.lat; h1 = L * 0.85; }   // from orbit, the globe tilts up ahead
            StartTravel(lon1, lat1, h1, hd1, instant, null);
        }

        /// B: fly in to a place, facing it, the place a little below the horizon.
        void FlyTo(double lon, double lat)
        {
            // The approach course at the target: the back-azimuth from the target toward where we are.
            double bearing = Wgs84.AngleDeg(_rig.Lon, _rig.Lat, lon, lat) < 0.01 ? _rig.Heading : (Wgs84.Bearing(lon, lat, _rig.Lon, _rig.Lat) + 180) % 360;
            double lon1, lat1;
            Wgs84.Destination(lon, lat, bearing + 180, snapHeight * 2.7, out lon1, out lat1);
            StartTravel(lon1, lat1, snapHeight, bearing, false, _rig.SampleAt(lon, lat));
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
                fade = _comfort.mode == Comfort.Mode.Fade,
                ground = ground,
            };
            double dist = Wgs84.AngleDeg(tr.lon0, tr.lat0, lon1, lat1) * Math.PI / 180 * Wgs84.A;
            tr.dur = Mathf.Clamp(1.6f + 0.55f * (float)Math.Log10(1 + dist / 1000.0), 1.6f, 5f);
            double hiLog = Math.Max(Math.Log(Math.Max(1, tr.h0)), Math.Log(Math.Max(1, h1)));
            double peak = Math.Log(Math.Max(1, dist * 0.45));
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
            }
            return motion;
        }

        // ---------------------------------------------------------------- frame
        void Update()
        {
            if (_rig == null || _in == null) return;
            float dt = Time.deltaTime;

            // Buttons.
            //   A          heads-up display on and off (state kept)
            //   B          fly in to the selected marker (else the one pointed at, else the laser point)
            //   X / Y      previous / next slide
            //   Left stick click, Menu   bring the display back in front
            if (_in.aBtn.WasPressedThisFrame() || _in.toggleHud.WasPressedThisFrame()) _hud.Visible = !_hud.Visible;
            if (_in.yBtn.WasPressedThisFrame() || _in.next.WasPressedThisFrame()) Next();
            if (_in.xBtn.WasPressedThisFrame() || _in.prev.WasPressedThisFrame()) Prev();
            if (_in.leftClick.WasPressedThisFrame() || _in.menu.WasPressedThisFrame()) { _hud.Recenter(); _hud.Visible = true; }
            if (_in.blank.WasPressedThisFrame()) _comfort.FadeTo(_comfort.FadeAlpha > 0.5f ? 0f : 1f);
            if (_in.spectator.WasPressedThisFrame()) _spectator.Toggle();

            // Sticks.
            Vector2 r = Controls.Dead(_in.rightStick.ReadValue<Vector2>()) + _in.kbMove.ReadValue<Vector2>();
            Vector2 l = Controls.Dead(_in.leftStick.ReadValue<Vector2>());
            l.y -= _in.kbClimb.ReadValue<float>();
            l.x += _in.kbYaw.ReadValue<float>();
            r = Vector2.ClampMagnitude(r, 1f); l.x = Mathf.Clamp(l.x, -1f, 1f); l.y = Mathf.Clamp(l.y, -1f, 1f);
            bool userMoving = r.sqrMagnitude > 0.0001f || Mathf.Abs(l.x) > 0.01f || Mathf.Abs(l.y) > 0.01f;
            if (userMoving) { if (_travel != null && _travel.fade) _comfort.FadeTo(0f); _travel = null; _rig.StopSpin(); }

            // Grips: right holds turbo, left holds fine control.
            float mult = 1f;
            if (_in.rightGrip.IsPressed()) mult *= turbo;
            if (_in.leftGrip.IsPressed()) mult *= precision;

            // Drone inertia: the craft eases up to speed and glides to a stop.
            float k = 1f - Mathf.Exp(-dt * 5f);
            _vel = Vector2.Lerp(_vel, r, k);
            _climbVel = Mathf.Lerp(_climbVel, l.y, k);
            _yawVel = Mathf.Lerp(_yawVel, l.x, 1f - Mathf.Exp(-dt * 8f));

            if (_travel == null)
            {
                Vector3 fwd = Flat(_eye.forward), right = Flat(_eye.right);
                double h = Math.Max(30, _rig.ViewHeight);
                double rate = flySpeed * h * mult;
                if (_vel.sqrMagnitude > 1e-6f) _rig.Travel((fwd * _vel.y + right * _vel.x).normalized, rate * _vel.magnitude * dt);
                // Left stick forward zooms in (descends), back zooms out.
                float zoom = forwardClimbs ? _climbVel : -_climbVel;
                if (Mathf.Abs(zoom) > 1e-4f) _rig.Climb(Math.Exp(zoom * climbRate * Mathf.Sqrt(mult) * dt));
                if (snapTurn)
                {
                    if (Mathf.Abs(l.x) > 0.7f && _snapArmed) { _rig.Turn(Mathf.Sign(l.x) * 30f); _snapArmed = false; _comfort.FadeTo(0.6f); _snapFaded = true; }
                    if (Mathf.Abs(l.x) < 0.3f) { _snapArmed = true; if (_snapFaded) { _comfort.FadeTo(0f); _snapFaded = false; } }
                }
                else if (Mathf.Abs(_yawVel) > 1e-3f) _rig.Turn(_yawVel * yawRate * dt);

                float motion = Mathf.Clamp01((_vel.magnitude * 0.9f + Mathf.Abs(_climbVel) * 0.5f + (snapTurn ? 0f : Mathf.Abs(_yawVel))) * Mathf.Min(1.5f, mult));
                _comfort.Tick(motion, dt);
                Rumble(motion, dt);
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
            if (!engineRumble || motion < 0.55f || Time.time < _rumbleNext) return;
            _rumbleNext = Time.time + 0.12f;
            Haptics.Pulse(true, 0.04f + 0.1f * (motion - 0.55f), 0.1f);
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
            float uiDist = 0;
            bool overUi = _hud.Visible && UI.RayHitsCanvas(ray, out uiDist);
            double lon = 0, lat = 0;
            Vector3 hitWorld = Vector3.zero;
            bool earth = !overUi && _rig.RayToEarth(ray, out lon, out lat, out hitWorld);
            Vector3 end = overUi ? ray.GetPoint(uiDist) : (earth ? hitWorld : ray.GetPoint(3f));
            _laser.Set(fromHand, ray.origin, end, earth, _eye);

            // What the laser is on, ten times a second, for the pointer info panel.
            if (Time.unscaledTime >= _hoverNext)
            {
                _hoverNext = Time.unscaledTime + 0.1f;
                var h = overUi ? null : PickAt(ray);
                if ((h == null) != (_hover == null) || (h != null && _hover != null && h.title != _hover.title))
                {
                    _hover = h;
                    _hud.ShowInfo(_hover, _selected);
                }
            }

            if (!overUi && (_in.trigger.WasPressedThisFrame() || _in.select.WasPressedThisFrame()))
            {
                var hit = PickAt(ray);
                _selected = hit;                       // trigger on empty space clears the selection
                if (hit != null) { Haptics.Pulse(true, 0.3f, 0.05f); _hud.ShowSources(hit.title, hit.refs); }
                else _hud.ResetSources();
                _hud.CloseMenus();
                _hud.ShowInfo(_hover, _selected);
            }
            if (_in.bBtn.WasPressedThisFrame() || _in.teleport.WasPressedThisFrame())
            {
                if (_selected != null && (_selected.lon != 0 || _selected.lat != 0)) FlyTo(_selected.lon, _selected.lat);
                else
                {
                    var hit = overUi ? null : PickAt(ray);
                    if (hit != null && (hit.lon != 0 || hit.lat != 0)) FlyTo(hit.lon, hit.lat);
                    else if (earth) FlyTo(lon, lat);
                }
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
            _proximity.Update(_rig, _eye, _compute, _cables, _sites);
            _hud.Follow(_eye);
            _credits.Tick();
            _spectator.Tick(_cam);
            if (Time.unscaledTime >= _statusNext) { _statusNext = Time.unscaledTime + 0.25f; _hud.SetStatus(Status()); }
        }

        float _statusNext;
        string Status()
        {
            double h = _rig.ViewHeight;
            string alt = h >= 1e5 ? (h / 1000).ToString("N0") + " km" : h >= 1e4 ? (h / 1000).ToString("0.0") + " km" : h.ToString("N0") + " m";
            return alt + "   " + Math.Abs(_rig.Lat).ToString("0.0") + (_rig.Lat >= 0 ? "N " : "S ") + Math.Abs(_rig.Lon).ToString("0.0") + (_rig.Lon >= 0 ? "E" : "W");
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
