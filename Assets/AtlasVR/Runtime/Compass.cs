// Where you are and which way you face, for low flight.
//   Compass   a small flat 3D compass at the lower right of the view. The needle points the way
//             you face; the dial turns under it so N stays on geographic north. It shows only
//             while you are low (the horizon near the middle of the view) and moving, and folds
//             away a couple of seconds after you stop.
//   Regions   the state or province, and country, under you (Natural Earth admin-1 areas).
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AtlasVR
{
    public class Compass
    {
        public const double ShowBelow = 20000;   // view height (m)
        readonly Transform _root, _dial, _needle;
        readonly TextMeshPro _heading;
        float _shown, _lastMotion = -100f;
        static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        public Compass(Transform hudRoot)
        {
            var sh = Shader.Find("AtlasVR/Emblem");
            var cyl = Meshes.Cylinder(48);
            _root = new GameObject("Compass").transform;
            _root.SetParent(hudRoot, false);
            // Lower right, about 1.1 m out, lying flat and tipped toward you so it reads.
            Quaternion q = Quaternion.Euler(33f, 26f, 0);
            _root.localPosition = q * Vector3.forward * 1.1f;
            _root.localRotation = Quaternion.AngleAxis(-38f, Vector3.right);  // face tipped toward the viewer; needle forward

            // Body: a dark puck with a light rim.
            Part("Rim", _root, cyl, sh, new Color(0.62f, 0.68f, 0.76f), 0.85f, new Vector3(0.124f, 0.012f, 0.124f), Vector3.down * 0.012f);
            Part("Face", _root, cyl, sh, new Color(0.05f, 0.08f, 0.13f), 0.3f, new Vector3(0.112f, 0.0125f, 0.112f), Vector3.down * 0.0115f);

            // Dial: ticks and the cardinal letters, turning together.
            _dial = new GameObject("Dial").transform;
            _dial.SetParent(_root, false);
            _dial.localPosition = Vector3.up * 0.0012f;
            var tick = Meshes.Cylinder(6);
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f;
                bool major = i % 6 == 0;
                var t = Part("Tick " + i, _dial, tick, sh, major ? Color.white : new Color(0.55f, 0.62f, 0.72f), 0.3f,
                    new Vector3(0.0018f, 0.0012f, major ? 0.009f : 0.005f), Vector3.zero);
                t.localRotation = Quaternion.Euler(0, a, 0);
                t.localPosition = t.localRotation * Vector3.forward * (major ? 0.044f : 0.047f);
            }
            string[] card = { "N", "E", "S", "W" };
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject(card[i]);
                go.transform.SetParent(_dial, false);
                var tm = go.AddComponent<TextMeshPro>();
                tm.text = card[i];
                tm.fontSize = 10f; tm.fontStyle = FontStyles.Bold;
                tm.alignment = TextAlignmentOptions.Center;
                tm.color = i == 0 ? new Color(1f, 0.32f, 0.36f) : Color.white;
                Quaternion r = Quaternion.Euler(0, i * 90f, 0);
                go.transform.localPosition = r * Vector3.forward * 0.031f + Vector3.up * 0.0006f;
                go.transform.localRotation = r * Quaternion.Euler(90f, 0, 0);    // lying on the dial, tops outward
                go.transform.localScale = Vector3.one * 0.0085f;
            }

            // Needle: points the way you face; red tip forward, pale tail.
            _needle = new GameObject("Needle").transform;
            _needle.SetParent(_root, false);
            _needle.localPosition = Vector3.up * 0.004f;
            var prism = Meshes.Cylinder(4);
            Part("Tip", _needle, prism, sh, new Color(0.95f, 0.3f, 0.2f), 0.7f, new Vector3(0.008f, 0.003f, 0.05f), Vector3.forward * 0.017f);   // a 4-sided prism stretched along z is a diamond
            Part("Tail", _needle, prism, sh, new Color(0.85f, 0.88f, 0.92f), 0.7f, new Vector3(0.006f, 0.0028f, 0.03f), Vector3.back * 0.013f);
            Part("Hub", _needle, cyl, sh, new Color(0.75f, 0.78f, 0.82f), 0.9f, new Vector3(0.008f, 0.004f, 0.008f), Vector3.zero);

            // Heading, just beyond the dial.
            var hg = new GameObject("Heading");
            hg.transform.SetParent(_root, false);
            _heading = hg.AddComponent<TextMeshPro>();
            _heading.fontSize = 10f; _heading.alignment = TextAlignmentOptions.Center;
            _heading.color = new Color(0.85f, 0.9f, 0.96f);
            hg.transform.localPosition = new Vector3(0, 0.002f, 0.08f);
            hg.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            hg.transform.localScale = Vector3.one * 0.0075f;

            _root.localScale = Vector3.zero;
            _root.gameObject.SetActive(false);
        }

        static Transform Part(string name, Transform parent, Mesh mesh, Shader sh, Color c, float metal, Vector3 scale, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var m = new Material(sh);
            m.SetColor("_Color", c); m.SetFloat("_Metal", metal);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        string _lastHeading;

        public void Update(GlobeRig rig, Transform eye, Transform hudRoot, bool moving)
        {
            if (rig == null || eye == null) return;
            bool low = rig.mode == ViewMode.Flight && rig.ViewHeight < ShowBelow;
            if (moving) _lastMotion = Time.unscaledTime;
            bool want = low && Time.unscaledTime - _lastMotion < 2.2f;
            _shown = Mathf.MoveTowards(_shown, want ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            bool active = _shown > 0.001f;
            if (_root.gameObject.activeSelf != active) _root.gameObject.SetActive(active);
            if (!active) return;
            _root.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, _shown);

            // Angles in the display's frame, clockwise from its forward.
            Vector3 north = hudRoot.InverseTransformDirection(rig.NorthWorld);
            // Facing: the gaze, flattened; looking nearly straight down, the top of the view stands in.
            Vector3 face = hudRoot.InverseTransformDirection(eye.forward); face.y = 0;
            Vector3 top = hudRoot.InverseTransformDirection(eye.up); top.y = 0;
            float hm = face.magnitude;
            if (hm < 0.35f && top.sqrMagnitude > 1e-6f) face = Vector3.Lerp(top.normalized, hm > 1e-6f ? face / hm : top.normalized, hm / 0.35f);
            float aNorth = Mathf.Atan2(north.x, north.z) * Mathf.Rad2Deg;
            float aFace = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            _dial.localRotation = Quaternion.Euler(0, aNorth, 0);
            _needle.localRotation = Quaternion.Euler(0, aFace, 0);
            float bearing = Mathf.Repeat(aFace - aNorth, 360f);
            string h = Mathf.RoundToInt(bearing) % 360 + "°  " + Points[Mathf.RoundToInt(bearing / 45f) % 8];
            if (h != _lastHeading) { _heading.text = h; _lastHeading = h; }
        }
    }

    /// The state or province and country at a point.
    public class Regions
    {
        readonly PkgRegion[] _r;
        public Regions(PkgRegions d) { _r = d != null ? d.regions : null; }
        public bool HasData { get { return _r != null && _r.Length > 0; } }

        public bool Lookup(double lon, double lat, out string place)
        {
            place = null;
            if (_r == null) return false;
            foreach (var r in _r)
            {
                var b = r.bbox;
                if (b == null || b.Length < 4 || lon < b[0] || lon > b[2] || lat < b[1] || lat > b[3]) continue;
                if (!Inside(r.rings, lon, lat)) continue;
                place = string.IsNullOrEmpty(r.name) || r.name == r.country ? r.country : r.name + ", " + r.country;
                return true;
            }
            return false;
        }

        // Even-odd rule across all rings (holes cancel).
        static bool Inside(PkgLine[] rings, double x, double y)
        {
            bool inside = false;
            if (rings == null) return false;
            foreach (var ring in rings)
            {
                var p = ring.lonlat;
                if (p == null) continue;
                int n = p.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    double xi = p[i * 2], yi = p[i * 2 + 1], xj = p[j * 2], yj = p[j * 2 + 1];
                    if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
                }
            }
            return inside;
        }
    }
}
