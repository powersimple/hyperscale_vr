// The globe: Cesium World Terrain with Bing imagery (or Earth at Night), and Google
// Photorealistic 3D Tiles for close views, all through Cesium ion.
//
// The viewer never moves in Unity space; the world moves around them. The Cesium origin is
// kept at the point that matters (the viewer's feet in flight, the slide's focus in the
// overview), which keeps precision high everywhere on Earth.
//
// Flight: real scale near the ground, the world scaled down smoothly above 2 km so depth
// precision holds all the way to orbit; in space the Earth tilts up into view.
// Overview: the whole Earth rests on a table as a ball; closer in, the view becomes a round
// map lens on the table top and tiles outside it are excluded. Room scale is the same,
// larger, with the lens on the floor.
using System;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

namespace AtlasVR
{
    public enum ViewMode { Flight, Tabletop, Room }
    public enum Posture { Standing, Seated }

    public class GlobeRig : MonoBehaviour
    {
        // Cesium ion assets.
        public const long WorldTerrain = 1, BingAerial = 2, EarthAtNight = 3812, GooglePhotoreal = 2275207;
        const double FullGlobeViewHeight = 2.3e7;
        const double RealScaleBelow = 2000;   // flight altitude (m) under which the world is at real scale

        public ViewMode mode = ViewMode.Flight;
        public Posture posture = Posture.Standing;
        [Tooltip("Use Google Photorealistic 3D Tiles when the view comes close to the ground.")]
        public bool photorealCloseUps = true;
        [Tooltip("Height (m) below which photorealistic tiles replace terrain and imagery.")]
        public double photorealBelow = 60000;

        public CesiumGeoreference Georef { get; private set; }
        public Cesium3DTileset Terrain { get; private set; }
        public Cesium3DTileset Photoreal { get; private set; }

        // View state. In flight, Lon/Lat/Height are the viewer's position (height above the
        // ellipsoid); in the overview they are the focus point and the deck's view height.
        public double Lon { get; private set; }
        public double Lat { get; private set; }
        public double Height { get; private set; }
        public double Heading { get; private set; }
        public double GroundHeight { get; private set; }
        public double ViewHeight { get { return mode == ViewMode.Flight ? Math.Max(1, Height - GroundHeight) : Height; } }

        public Matrix4x4 GlobeToWorld { get; private set; }
        public Matrix4x4 WorldToGlobe { get; private set; }
        public float GlobeScale { get; private set; }
        public bool LensActive { get; private set; }
        public Vector3 LensCenter { get; private set; }
        public float LensRadius { get; private set; }
        public Vector3 Up { get { return Vector3.up; } }
        public Vector3 BallCenter { get; private set; }
        public float BallRadius { get; private set; }
        public bool PhotorealShowing { get { return _photorealOn; } }
        public D3 CameraEcef { get; private set; }

        // Tilesets are hidden by camera layer rather than disabled: disabling a Cesium tileset
        // destroys it, and the reload would leave the globe blank for seconds.
        public const int TerrainLayer = 30, PhotorealLayer = 31;

        CesiumIonRasterOverlay _imagery, _night;
        LensExcluder _lens;
        GameObject _table;
        Camera _cam;
        Transform _floor;                 // the XR Origin (floor reference)
        Vector3 _placeFloor;              // overview: floor point under the table
        Quaternion _placeRot = Quaternion.identity;
        bool _slidePhotoreal, _nightOn, _photorealOn, _terrainShown = true;
        float _photoSince;
        double _appliedScale = -1, _oLon = double.NaN, _oLat, _oH;
        float _spinDegPerSec;
        Task<CesiumSampleHeightResult> _sample;
        float _sampleStarted;
        float _nextSample;
        Atmosphere _atmo;

        float TableTop { get { return posture == Posture.Seated ? 0.68f : 0.86f; } }
        float BallMax { get { return mode == ViewMode.Room ? 1.0f : 0.30f; } }
        float LensMax { get { return mode == ViewMode.Room ? 2.6f : 0.46f; } }

        public void Build(string ionToken, Camera cam, Transform floor)
        {
            _cam = cam;
            _floor = floor;
            bool quest = Application.platform == RuntimePlatform.Android;

            var g = new GameObject("Globe (CesiumGeoreference)");
            g.transform.SetParent(transform, false);
            Georef = g.AddComponent<CesiumGeoreference>();
            Georef.SetOriginLongitudeLatitudeHeight(0, 0, 0);
            _lens = g.AddComponent<LensExcluder>();

            var t = new GameObject("Cesium World Terrain");
            t.transform.SetParent(g.transform, false);
            Terrain = t.AddComponent<Cesium3DTileset>();
            Terrain.tilesetSource = CesiumDataSource.FromCesiumIon;
            Terrain.ionAssetID = WorldTerrain;
            if (!string.IsNullOrEmpty(ionToken)) Terrain.ionAccessToken = ionToken;
            Tune(Terrain, quest);
            t.layer = TerrainLayer;

            _imagery = t.AddComponent<CesiumIonRasterOverlay>();
            _imagery.ionAssetID = BingAerial;
            if (!string.IsNullOrEmpty(ionToken)) _imagery.ionAccessToken = ionToken;
            _imagery.maximumTextureSize = quest ? 1024 : 2048;

            _night = t.AddComponent<CesiumIonRasterOverlay>();
            _night.ionAssetID = EarthAtNight;
            if (!string.IsNullOrEmpty(ionToken)) _night.ionAccessToken = ionToken;
            _night.enabled = false;

            var p = new GameObject("Google Photorealistic 3D Tiles");
            p.transform.SetParent(g.transform, false);
            Photoreal = p.AddComponent<Cesium3DTileset>();
            Photoreal.tilesetSource = CesiumDataSource.FromCesiumIon;
            Photoreal.ionAssetID = GooglePhotoreal;
            if (!string.IsNullOrEmpty(ionToken)) Photoreal.ionAccessToken = ionToken;
            Tune(Photoreal, quest);
            p.layer = PhotorealLayer;
            Photoreal.suspendUpdate = true;      // loads nothing until the view comes close
            if (_cam != null) _cam.cullingMask &= ~(1 << PhotorealLayer);

            _table = new GameObject("Table");
            _table.transform.SetParent(transform, false);
            _table.AddComponent<MeshFilter>().sharedMesh = Meshes.Cylinder();
            var tm = new Material(Shader.Find("AtlasVR/Solid"));
            tm.SetColor("_Color", new Color(0.06f, 0.09f, 0.13f, 1f));
            _table.AddComponent<MeshRenderer>().sharedMaterial = tm;
            _table.SetActive(false);

            _atmo = new Atmosphere();

            Lon = -40; Lat = 25; Height = 1.6e7; Heading = 0;
        }

        static void Tune(Cesium3DTileset ts, bool quest)
        {
            ts.showCreditsOnScreen = false;          // credits are drawn in the headset by VRCredits
            ts.createPhysicsMeshes = false;          // ground height comes from height sampling instead
            ts.maximumScreenSpaceError = quest ? 24f : 12f;
            ts.maximumSimultaneousTileLoads = quest ? 14u : 24u;
            ts.maximumCachedBytes = quest ? 256L * 1024 * 1024 : 1024L * 1024 * 1024; // two tilesets stay resident
            ts.preloadSiblings = !quest;
            ts.forbidHoles = false;
        }

        /// Stand the table (or the room globe) in front of the viewer.
        public void Place(Vector3 head, Vector3 forward, float floorY)
        {
            forward.y = 0;
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
            forward.Normalize();
            float ahead = mode == ViewMode.Room ? 2.4f : (posture == Posture.Seated ? 0.62f : 0.78f);
            _placeFloor = new Vector3(head.x, floorY, head.z) + forward * ahead;
            _placeRot = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void SetPose(double lon, double lat, double height, double heading)
        {
            while (lon > 180) lon -= 360;
            while (lon < -180) lon += 360;
            Lon = lon;
            Lat = Math.Max(-89.5, Math.Min(89.5, lat));
            double min = mode == ViewMode.Flight ? GroundHeight + 25 : 800;
            Height = Math.Max(min, Math.Min(4.2e7, height));
            Heading = ((heading % 360) + 360) % 360;
        }

        public void SetMode(ViewMode m)
        {
            if (m == mode) return;
            if (m == ViewMode.Flight) { mode = m; SetPose(Lon, Lat, Math.Max(3000, Height * 0.6), Heading); }
            else
            {
                double vh = mode == ViewMode.Flight ? Math.Max(5000, ViewHeight * 1.6) : Height;
                mode = m; SetPose(Lon, Lat, vh, Heading);
            }
        }

        public void SetSlideLayers(float night, bool photoreal, bool spin)
        {
            _nightOn = night >= 0.5f;
            _slidePhotoreal = photoreal;
            _spinDegPerSec = spin ? 2.5f : 0f;
        }

        public void StopSpin() { _spinDegPerSec = 0f; }

        /// The rotation that carries the map's east-up-north frame at the origin into the world.
        Quaternion WorldRotation()
        {
            Quaternion yaw = Quaternion.Euler(0, -(float)Heading, 0);
            if (mode != ViewMode.Flight) return _placeRot * yaw;
            float up = 55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8e5f, 8e6f, (float)Height));
            return Quaternion.Euler(-up, 0, 0) * yaw;
        }

        /// Ground distance (east, north, in meters) for a horizontal world-space direction.
        public void Travel(Vector3 worldDir, double meters)
        {
            Vector3 local = Quaternion.Inverse(Quaternion.Euler(0, -(float)Heading, 0) * (mode == ViewMode.Flight ? Quaternion.identity : _placeRot)) * worldDir;
            local.y = 0;
            if (local.sqrMagnitude < 1e-8f) return;
            local.Normalize();
            double east = local.x * meters, north = local.z * meters;
            double lat = Lat + north / Wgs84.A * 180.0 / Math.PI;
            double lon = Lon + east / (Wgs84.A * Math.Max(0.05, Math.Cos(Lat * Math.PI / 180.0))) * 180.0 / Math.PI;
            SetPose(lon, lat, Height, Heading);
        }

        /// Overview: drag the map by a world-space displacement on the table.
        public void DragWorld(Vector3 worldDelta)
        {
            if (GlobeScale <= 0) return;
            Travel(-worldDelta, worldDelta.magnitude / GlobeScale);
        }

        public void Climb(double factor) { SetPose(Lon, Lat, mode == ViewMode.Flight ? GroundHeight + (Height - GroundHeight) * factor : Height * factor, Heading); }
        public void Turn(double degrees) { SetPose(Lon, Lat, Height, Heading + degrees); }

        void LateUpdate()
        {
            if (Georef == null) return;
            if (_spinDegPerSec > 0f) SetPose(Lon + _spinDegPerSec * Time.deltaTime, Lat, Height, Heading);
            if (mode == ViewMode.Flight) SampleGround();

            Vector3 floor = _floor != null ? _floor.position : Vector3.zero;
            Vector3 eye = _cam != null ? _cam.transform.position : floor + Vector3.up * 1.6f;
            Vector3 pivot = floor + Vector3.up * (posture == Posture.Seated ? 1.2f : 1.6f); // fixed, so head motion keeps its parallax
            Quaternion rot = WorldRotation();
            double s, oH;
            Vector3 anchor;

            if (mode == ViewMode.Flight)
            {
                s = Height <= RealScaleBelow ? 1.0 : RealScaleBelow / Height;
                oH = Height;
                // The origin is the point at the viewer's feet (the play area's center); in space the
                // world pivots about eye height above it.
                anchor = pivot + rot * Quaternion.Inverse(Quaternion.Euler(0, -(float)Heading, 0)) * (floor - pivot);
                LensActive = false;
                BallRadius = (float)(Wgs84.A * s);
                BallCenter = anchor - (rot * Vector3.up) * (float)((Wgs84.A + Height) * s);
            }
            else
            {
                s = BallMax * (FullGlobeViewHeight / Wgs84.A) / Height;
                oH = 0;
                float R = (float)(Wgs84.A * s), rb = BallMax;
                float t = Mathf.Clamp01((R - rb) / (rb * 2f));
                float anchorY, lensY;
                if (mode == ViewMode.Tabletop)
                {
                    float top = TableTop;
                    lensY = top + 0.004f;
                    // Lift the lens by the Earth's curvature across it, so its rim does not sink into the table.
                    float sag = R - Mathf.Sqrt(Mathf.Max(0f, R * R - LensMax * LensMax));
                    anchorY = Mathf.Lerp(top + 0.01f + 2f * Mathf.Min(R, rb), lensY + Mathf.Min(sag, LensMax), t);
                }
                else
                {
                    float center = posture == Posture.Seated ? 1.0f : 1.25f;
                    lensY = 0.015f;
                    float sag = R - Mathf.Sqrt(Mathf.Max(0f, R * R - LensMax * LensMax));
                    anchorY = Mathf.Lerp(center + Mathf.Min(R, rb), lensY + Mathf.Min(sag, 1f), t);
                }
                anchor = _placeFloor + Vector3.up * anchorY;
                BallRadius = R;
                BallCenter = anchor - Vector3.up * R;
                LensActive = R > rb * 1.02f;
                LensRadius = LensMax;
                LensCenter = _placeFloor + Vector3.up * lensY;
            }
            s = Math.Max(1.2e-8, s);

            if (Lon != _oLon || Lat != _oLat || oH != _oH)
            {
                Georef.SetOriginLongitudeLatitudeHeight(Lon, Lat, oH);
                _oLon = Lon; _oLat = Lat; _oH = oH;
            }
            // Each scale change moves the origin again (a pass over every tile); step it by half a percent.
            if (Math.Abs(s - _appliedScale) > _appliedScale * 5e-3) { Georef.scale = s; _appliedScale = s; }
            Georef.transform.SetPositionAndRotation(anchor, rot);
            Georef.transform.localScale = Vector3.one;

            _lens.radius = LensRadius;
            _lens.active = LensActive;

            bool table = mode == ViewMode.Tabletop;
            if (_table.activeSelf != table) _table.SetActive(table);
            if (table)
            {
                _table.transform.SetPositionAndRotation(_placeFloor, _placeRot);
                _table.transform.localScale = new Vector3((LensMax + 0.06f) * 2f, TableTop, (LensMax + 0.06f) * 2f);
            }

            // Day or night imagery; photorealistic tiles for close views.
            if (_night.enabled != _nightOn) { _night.enabled = _nightOn; _imagery.enabled = !_nightOn; }
            UpdatePhotoreal();

            // One matrix carries ECEF into the world for every layer this frame.
            GlobeToWorld = Georef.transform.localToWorldMatrix * ToMatrix(Georef.ecefToLocalMatrix);
            WorldToGlobe = ToMatrix(Georef.localToEcefMatrix) * Georef.transform.worldToLocalMatrix;
            GlobeScale = (float)s;
            double3 camE = Georef.TransformUnityPositionToEarthCenteredEarthFixed(ToD3(Georef.transform.InverseTransformPoint(eye)));
            CameraEcef = new D3(camE.x, camE.y, camE.z);
            Vector3 hi, lo;
            Wgs84.Split(CameraEcef, out hi, out lo);
            Shader.SetGlobalMatrix("_AtlasGlobeToWorld", GlobeToWorld);
            Shader.SetGlobalFloat("_AtlasGlobeScale", GlobeScale);
            Shader.SetGlobalVector("_AtlasLens", new Vector4(LensCenter.x, LensCenter.y, LensCenter.z, LensRadius));
            Shader.SetGlobalVector("_AtlasLensUp", new Vector4(0, 1, 0, LensActive ? 1f : 0f));
            Shader.SetGlobalVector("_AtlasRefWorld", new Vector4(eye.x, eye.y, eye.z, 1));
            Shader.SetGlobalVector("_AtlasRefEcefHigh", new Vector4(hi.x, hi.y, hi.z, 0));
            Shader.SetGlobalVector("_AtlasRefEcefLow", new Vector4(lo.x, lo.y, lo.z, 0));

            UpdateCameraAndSky(eye);
        }

        void UpdateCameraAndSky(Vector3 eye)
        {
            if (_cam == null) return;
            double h = mode == ViewMode.Flight ? ViewHeight : 0;
            if (mode == ViewMode.Flight)
            {
                double horizon = Math.Sqrt(Math.Max(0, Height) * (2 * Wgs84.A + Math.Max(0, Height)));
                _cam.farClipPlane = Mathf.Clamp((float)((horizon + Height) * GlobeScale * 1.6), 3000f, 2.0e6f);
            }
            else _cam.farClipPlane = Mathf.Max(200f, BallRadius * 4f);
            _cam.nearClipPlane = 0.05f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            Color sky = new Color(0.42f, 0.62f, 0.88f), space = new Color(0.005f, 0.008f, 0.02f), room = new Color(0.02f, 0.03f, 0.05f);
            _cam.backgroundColor = mode == ViewMode.Flight ? Color.Lerp(sky, space, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15000f, 120000f, (float)h))) : room;
            _atmo.Draw(this, eye, mode == ViewMode.Flight ? Mathf.InverseLerp(130000f, 500000f, (float)h) : (LensActive ? 0f : 0.6f));
        }

        // Photorealistic tiles take over below the threshold (with hysteresis); terrain stays on
        // screen until enough of them have arrived, then pauses. Nothing is destroyed either way.
        void UpdatePhotoreal()
        {
            double vh = ViewHeight;
            bool want = _photorealOn
                ? (photorealCloseUps || _slidePhotoreal) && vh < photorealBelow * 1.25
                : (photorealCloseUps && vh < photorealBelow * 0.8) || (_slidePhotoreal && vh < 3.0e5);
            if (want != _photorealOn)
            {
                _photorealOn = want;
                _photoSince = Time.time;
                if (want) { Photoreal.suspendUpdate = false; SetLayerVisible(PhotorealLayer, true); }
                else { Terrain.suspendUpdate = false; SetLayerVisible(TerrainLayer, true); _terrainShown = true; SetLayerVisible(PhotorealLayer, false); }
            }
            if (_photorealOn && _terrainShown)
            {
                float progress = 0f;
                try { progress = Photoreal.ComputeLoadProgress(); } catch (Exception) { }
                // Progress reads stale until the tileset has updated a few frames after resuming.
                float since = Time.time - _photoSince;
                if ((since > 0.75f && progress >= 92f) || since > 5f)
                {
                    SetLayerVisible(TerrainLayer, false);
                    Terrain.suspendUpdate = true;
                    _terrainShown = false;
                }
            }
            if (!_photorealOn && Time.time - _photoSince > 1.5f && !Photoreal.suspendUpdate)
                Photoreal.suspendUpdate = true;
        }

        void SetLayerVisible(int layer, bool on)
        {
            if (_cam == null) return;
            if (on) _cam.cullingMask |= 1 << layer; else _cam.cullingMask &= ~(1 << layer);
        }

        public int CullingMask { get { return _cam != null ? _cam.cullingMask : ~0; } }

        void SampleGround()
        {
            if (_sample != null && _sample.IsCompleted)
            {
                try
                {
                    var r = _sample.Status == TaskStatus.RanToCompletion ? _sample.Result : null;
                    if (r != null && r.sampleSuccess != null && r.sampleSuccess.Length > 0 && r.sampleSuccess[0])
                        GroundHeight = r.longitudeLatitudeHeightPositions[0].z;
                }
                catch (Exception) { }
                _sample = null;
                _sampleStarted = 0;
                if (Height < GroundHeight + 25) Height = GroundHeight + 25;
            }
            // A sample on a tileset that was then paused never completes; drop it.
            if (_sample != null && Time.time - _sampleStarted > 3f) _sample = null;
            if (_sample == null && Time.time >= _nextSample && Height < 60000)
            {
                _nextSample = Time.time + 0.35f;
                var ts = _photorealOn && !_terrainShown ? Photoreal : Terrain;
                if (ts != null && ts.isActiveAndEnabled)
                {
                    try { _sample = ts.SampleHeightMostDetailed(new double3(Lon, Lat, 0)); _sampleStarted = Time.time; }
                    catch (Exception) { _sample = null; }
                }
            }
            if (Height >= 60000) GroundHeight = 0;
        }

        /// Starts a ground-height sample at a place (for landing a flight above the terrain there).
        public Task<CesiumSampleHeightResult> SampleAt(double lon, double lat)
        {
            var ts = _photorealOn && !_terrainShown ? Photoreal : Terrain;
            if (ts == null || !ts.isActiveAndEnabled) return null;
            try { return ts.SampleHeightMostDetailed(new double3(lon, lat, 0)); }
            catch (Exception) { return null; }
        }

        public static bool SampleResult(Task<CesiumSampleHeightResult> t, out double height)
        {
            height = 0;
            if (t == null || !t.IsCompleted || t.Status != TaskStatus.RanToCompletion) return false;
            var r = t.Result;
            if (r == null || r.sampleSuccess == null || r.sampleSuccess.Length == 0 || !r.sampleSuccess[0]) return false;
            height = r.longitudeLatitudeHeightPositions[0].z;
            return true;
        }

        /// World position of a lon/lat/height point under the current view.
        public Vector3 WorldOf(double lon, double lat, double h)
        {
            // In double through the georeference, so it agrees with the exact markers up close.
            D3 e = Wgs84.ToEcef(lon, lat, h);
            double3 u = Georef.TransformEarthCenteredEarthFixedPositionToUnity(new double3(e.x, e.y, e.z));
            return Georef.transform.TransformPoint(new Vector3((float)u.x, (float)u.y, (float)u.z));
        }

        /// True when a world point is on the visible side of the globe and inside the lens.
        public bool Visible(Vector3 world, Vector3 surfaceUpWorld, Vector3 eye)
        {
            if (LensActive)
            {
                Vector3 d = world - LensCenter;
                d.y = 0;
                return d.magnitude <= LensRadius;
            }
            // Hidden when the eye is below the point's tangent plane: e . (C - e) < 0.
            Vector3 ef = WorldToGlobe.MultiplyPoint3x4(world);
            D3 e = new D3(ef.x, ef.y, ef.z), c = CameraEcef;
            return D3.Dot(e, c - e) >= 0;
        }

        /// Where a world-space ray meets the ground (the WGS84 ellipsoid, raised to the ground
        /// height under the viewer), as lon/lat. False when it misses or starts below the ground.
        public bool RayToEarth(Ray ray, out double lon, out double lat)
        {
            Vector3 hit;
            return RayToEarth(ray, out lon, out lat, out hit);
        }

        public bool RayToEarth(Ray ray, out double lon, out double lat, out Vector3 hitWorld)
        {
            lon = lat = 0;
            hitWorld = ray.origin;
            const double B = 6356752.314245;
            double raise = mode == ViewMode.Flight ? Math.Max(0, GroundHeight) : 0;
            double a = Wgs84.A + raise, b = B + raise;
            Vector3 of = WorldToGlobe.MultiplyPoint3x4(ray.origin);
            Vector3 df = WorldToGlobe.MultiplyVector(ray.direction);
            // Scale to a unit sphere.
            D3 O = new D3(of.x / a, of.y / a, of.z / b), Dd = new D3(df.x / a, df.y / a, df.z / b);
            double dd = D3.Dot(Dd, Dd), bb = D3.Dot(O, Dd), cc = D3.Dot(O, O) - 1.0;
            if (cc < 0) return false;                  // the ray starts inside
            double disc = bb * bb - dd * cc;
            if (disc < 0) return false;
            double t = (-bb - Math.Sqrt(disc)) / dd;
            if (t < 0) return false;
            D3 p = O + Dd * t;
            p = new D3(p.x * a, p.y * a, p.z * b);
            double r = Math.Sqrt(p.x * p.x + p.y * p.y);
            // Geodetic latitude from the ellipsoid point (Bowring's formula, one step is plenty here).
            double e2 = 1 - (B * B) / (Wgs84.A * Wgs84.A), ep2 = (Wgs84.A * Wgs84.A) / (B * B) - 1;
            double th = Math.Atan2(p.z * Wgs84.A, r * B);
            double phi = Math.Atan2(p.z + ep2 * B * Math.Pow(Math.Sin(th), 3), r - e2 * Wgs84.A * Math.Pow(Math.Cos(th), 3));
            lat = phi * 180.0 / Math.PI;
            lon = Math.Atan2(p.y, p.x) * 180.0 / Math.PI;
            hitWorld = WorldOf(lon, lat, raise);
            return true;
        }

        static double3 ToD3(Vector3 v) { return new double3(v.x, v.y, v.z); }

        static Matrix4x4 ToMatrix(double4x4 d)
        {
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4((float)d.c0.x, (float)d.c0.y, (float)d.c0.z, (float)d.c0.w));
            m.SetColumn(1, new Vector4((float)d.c1.x, (float)d.c1.y, (float)d.c1.z, (float)d.c1.w));
            m.SetColumn(2, new Vector4((float)d.c2.x, (float)d.c2.y, (float)d.c2.z, (float)d.c2.w));
            m.SetColumn(3, new Vector4((float)d.c3.x, (float)d.c3.y, (float)d.c3.z, (float)d.c3.w));
            return m;
        }
    }

    /// Keeps tiles outside the round lens from loading or drawing when the view is a map on the table.
    public class LensExcluder : CesiumTileExcluder
    {
        public bool active;
        public float radius = 0.5f;

        public override bool ShouldExclude(Cesium3DTile tile)
        {
            if (!active) return false;
            Bounds b = tile.bounds; // in this object's local frame: origin at the focus, +Y up
            Vector2 c = new Vector2(b.center.x, b.center.z);
            float reach = new Vector2(b.extents.x, b.extents.z).magnitude;
            return c.magnitude - reach > radius;
        }
    }

    /// A thin glowing shell at the limb of the Earth, and a starfield behind it.
    public class Atmosphere
    {
        readonly Mesh _sphere, _stars;
        readonly Material _shell, _starMat;

        public Atmosphere()
        {
            _sphere = UvSphere(48, 24);
            _shell = new Material(Shader.Find("AtlasVR/Atmosphere"));
            _stars = Stars(2400);
            _starMat = new Material(Shader.Find("AtlasVR/Stars"));
        }

        public void Draw(GlobeRig rig, Vector3 eye, float strength)
        {
            if (strength > 0.01f)
            {
                _shell.SetFloat("_Strength", strength);
                var m = rig.GlobeToWorld * Matrix4x4.Scale(Vector3.one * (float)(Wgs84.A * 1.018));
                var rp = new RenderParams(_shell) { worldBounds = new Bounds(rig.BallCenter, Vector3.one * rig.BallRadius * 3f) };
                Graphics.RenderMesh(rp, _sphere, 0, m);
            }
            if (rig.mode != ViewMode.Flight || rig.ViewHeight > 50000)
            {
                float far = Camera.main != null ? Camera.main.farClipPlane * 0.9f : 1000f;
                Quaternion r = Quaternion.LookRotation(rig.GlobeToWorld.GetColumn(2), rig.GlobeToWorld.GetColumn(1));
                var m = Matrix4x4.TRS(eye, r, Vector3.one * far);
                var rp = new RenderParams(_starMat) { worldBounds = new Bounds(eye, Vector3.one * far * 2.2f) };
                Graphics.RenderMesh(rp, _stars, 0, m);
            }
        }

        static Mesh UvSphere(int lonSeg, int latSeg)
        {
            var v = new Vector3[(lonSeg + 1) * (latSeg + 1)];
            var t = new int[lonSeg * latSeg * 6];
            int k = 0;
            for (int y = 0; y <= latSeg; y++)
                for (int x = 0; x <= lonSeg; x++)
                {
                    float la = Mathf.PI * y / latSeg - Mathf.PI / 2f, lo = 2f * Mathf.PI * x / lonSeg;
                    v[y * (lonSeg + 1) + x] = new Vector3(Mathf.Cos(la) * Mathf.Cos(lo), Mathf.Cos(la) * Mathf.Sin(lo), Mathf.Sin(la));
                }
            for (int y = 0; y < latSeg; y++)
                for (int x = 0; x < lonSeg; x++)
                {
                    int a = y * (lonSeg + 1) + x, b = a + lonSeg + 1;
                    t[k++] = a; t[k++] = b; t[k++] = a + 1;
                    t[k++] = a + 1; t[k++] = b; t[k++] = b + 1;
                }
            var m = new Mesh { name = "Atmosphere", vertices = v, triangles = t };
            m.normals = v;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        static Mesh Stars(int n)
        {
            var rnd = new System.Random(7);
            var v = new Vector3[n];
            var c = new Color[n];
            var idx = new int[n];
            for (int i = 0; i < n; i++)
            {
                double z = rnd.NextDouble() * 2 - 1, a = rnd.NextDouble() * Math.PI * 2, r = Math.Sqrt(1 - z * z);
                v[i] = new Vector3((float)(r * Math.Cos(a)), (float)z, (float)(r * Math.Sin(a)));
                float b = Mathf.Pow((float)rnd.NextDouble(), 3f) * 0.85f + 0.15f;
                c[i] = new Color(b, b, b * (0.9f + 0.1f * (float)rnd.NextDouble()), 1f);
                idx[i] = i;
            }
            var m = new Mesh { name = "Stars", vertices = v, colors = c };
            m.SetIndices(idx, MeshTopology.Points, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }
    }
}
