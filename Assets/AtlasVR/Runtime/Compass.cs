// Where you are and which way you face, for low flight.
//   Compass   a small 3D compass at the right end of the location line in the bottom box:
//             polished metal rim, see-through glass face. The needle points the way you face;
//             the dial turns under it so N stays on geographic north. Shown below orbit.
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

        const float Size = 0.62f;   // the compass's scale in meters-per-unit terms (about 8 cm across)
        float _scale;

        /// The compass sits on an anchor in a HUD canvas; hudRoot gives the frame for its angles.
        public Compass(Transform hudRoot, Transform anchor)
        {
            var sh = Shader.Find("AtlasVR/Emblem");
            var cyl = Meshes.Cylinder(48);
            _root = new GameObject("Compass").transform;
            _root.SetParent(anchor != null ? anchor : hudRoot, false);
            // In the canvas: a little in front of it, the face tipped up toward you, the needle pointing ahead.
            _scale = anchor != null ? Size / Mathf.Max(1e-6f, anchor.lossyScale.x) : Size;
            _root.localPosition = new Vector3(0, 0, anchor != null ? -40f : 0f);
            _root.localRotation = Quaternion.Euler(-60f, 0, 0);

            // Body: a polished metal rim around a see-through glass face.
            Part("Rim", _root, cyl, sh, new Color(0.86f, 0.89f, 0.95f), 0.92f, new Vector3(0.128f, 0.014f, 0.128f), Vector3.down * 0.013f);
            Glass("Face", _root, cyl, new Color(0.03f, 0.07f, 0.2f, 0.45f), new Color(0.45f, 0.65f, 1f, 0.7f), 2.2f, new Vector3(0.116f, 0.0135f, 0.116f), Vector3.down * 0.0118f);

            // Dial: ticks and the cardinal letters, turning together.
            _dial = new GameObject("Dial").transform;
            _dial.SetParent(_root, false);
            _dial.localPosition = Vector3.up * 0.0016f;
            var tick = Meshes.Cylinder(6);
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f;
                bool major = i % 6 == 0;
                var t = Part("Tick " + i, _dial, tick, sh, major ? new Color(1f, 0.82f, 0.35f) : new Color(0.45f, 0.6f, 1f), 0.3f,
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
                tm.color = i == 0 ? new Color(1f, 0.25f, 0.42f) : new Color(1f, 0.9f, 0.7f);
                Quaternion r = Quaternion.Euler(0, i * 90f, 0);
                go.transform.localPosition = r * Vector3.forward * 0.031f + Vector3.up * 0.0006f;
                go.transform.localRotation = r * Quaternion.Euler(90f, 0, 0);    // lying on the dial, tops outward
                go.transform.localScale = Vector3.one * 0.017f;
            }

            // Needle: points the way you face; red tip forward, pale tail.
            _needle = new GameObject("Needle").transform;
            _needle.SetParent(_root, false);
            _needle.localPosition = Vector3.up * 0.004f;
            var prism = Meshes.Cylinder(4);
            Part("Tip", _needle, prism, sh, new Color(0.95f, 0.12f, 0.32f), 0.8f, new Vector3(0.008f, 0.003f, 0.05f), Vector3.forward * 0.017f);   // a 4-sided prism stretched along z is a diamond
            Part("Tail", _needle, prism, sh, new Color(1f, 0.8f, 0.3f), 0.85f, new Vector3(0.006f, 0.0028f, 0.03f), Vector3.back * 0.013f);
            Part("Hub", _needle, cyl, sh, new Color(1f, 0.82f, 0.4f), 0.9f, new Vector3(0.008f, 0.004f, 0.008f), Vector3.zero);

            // Heading, just beyond the dial.
            var hg = new GameObject("Heading");
            hg.transform.SetParent(_root, false);
            _heading = hg.AddComponent<TextMeshPro>();
            _heading.fontSize = 10f; _heading.alignment = TextAlignmentOptions.Center;
            _heading.color = new Color(1f, 0.85f, 0.5f);
            hg.transform.localPosition = new Vector3(0, 0.002f, -0.095f);   // on the near side, toward you
            hg.transform.localRotation = Quaternion.Euler(90f, 0, 0);
            hg.transform.localScale = Vector3.one * 0.016f;

            // Draw after the HUD canvases (sorting order 10), which would otherwise paint over the glass and text.
            foreach (var r in _root.GetComponentsInChildren<Renderer>(true)) r.sortingOrder = 11;
            _root.localScale = Vector3.zero;
            _root.gameObject.SetActive(false);
        }

        static Transform Glass(string name, Transform parent, Mesh mesh, Color body, Color rim, float power, Vector3 scale, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var m = new Material(Shader.Find("AtlasVR/Glass"));
            m.SetColor("_Color", body); m.SetColor("_RimColor", rim); m.SetFloat("_RimPower", power);
            m.renderQueue = 2995;   // before the letters (3000), which write no depth
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
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
            Lit.Add(m);
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go.transform;
        }

        static readonly List<Material> Lit = new List<Material>();
        static readonly Vector3 LocalLight = new Vector3(0.4f, 0.8f, -0.45f);
        string _lastHeading;

        /// The heading you face, like "045° NE".
        public string HeadingText { get { return _lastHeading ?? ""; } }

        public void Update(GlobeRig rig, Transform eye, Transform hudRoot, bool show)
        {
            if (rig == null || eye == null) return;
            _shown = Mathf.MoveTowards(_shown, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            bool active = _shown > 0.001f;
            if (_root.gameObject.activeSelf != active) _root.gameObject.SetActive(active);
            _root.localScale = Vector3.one * (_scale * Mathf.SmoothStep(0f, 1f, _shown));
            Vector3 lw = hudRoot.rotation * LocalLight;   // lit in the display's frame, so the light turns with you
            foreach (var m in Lit) m.SetVector("_LightDir", lw);

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
            if (h != _lastHeading) { _lastHeading = h; _heading.text = h; }
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
