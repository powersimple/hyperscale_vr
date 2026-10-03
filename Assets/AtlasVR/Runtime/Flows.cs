// The deck's flows: arcs that lift off the globe between a power plant and the campus it feeds,
// a coastal city and an inland compute hub, or one builder's sites on different continents.
// Bright pulses race along each arc over a dim base, inside a soft glow, as in the web deck
// (flow_material.js): same colors, arc heights, widths, and pulse speeds.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AtlasVR
{
    public class FlowLayer : IPickable
    {
        readonly PkgFlows _d;
        readonly Mesh _mesh;
        readonly Material _core, _glow;
        readonly List<int> _start = new List<int>(), _end = new List<int>();
        readonly Dictionary<string, string[]> _bySlide = new Dictionary<string, string[]>();
        readonly List<Vector3[]> _pick = new List<Vector3[]>();
        Color[] _colors;
        bool[] _on;
        bool _any;
        const int Steps = 64;

        public FlowLayer(PkgFlows d)
        {
            _d = d;
            var verts = new List<Vector3>(); var lows = new List<Vector3>(); var norms = new List<Vector3>();
            var uvs = new List<Vector2>(); var anim = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            if (d != null && d.flows != null)
                foreach (var f in d.flows)
                {
                    int start = verts.Count;
                    double ang = Wgs84.AngleDeg(f.lon0, f.lat0, f.lon1, f.lat1);
                    double dist = ang * Math.PI / 180.0 * Wgs84.A;
                    double peak = Math.Min(f.maxH, Math.Max(f.minH, dist * f.lift));
                    var pts = new D3[Steps + 1];
                    var pick = new Vector3[9];
                    for (int i = 0; i <= Steps; i++)
                    {
                        double t = (double)i / Steps, lo, la;
                        Wgs84.Slerp(f.lon0, f.lat0, f.lon1, f.lat1, t, out lo, out la);
                        pts[i] = Wgs84.ToEcef(lo, la, Math.Sin(Math.PI * t) * peak);
                        if (i % 8 == 0) pick[i / 8] = pts[i].ToVector3();
                    }
                    _pick.Add(pick);
                    Color col = Hex.Color(f.color, 1f);
                    for (int i = 0; i <= Steps; i++)
                    {
                        D3 prev = pts[Math.Max(0, i - 1)], next = pts[Math.Min(Steps, i + 1)];
                        // The tangent goes in the normal channel; the shader turns the ribbon to face the eye,
                        // so a raised arc keeps its width from any angle.
                        D3 tan = (next - prev).Normalized;
                        Vector3 p, plo, sv = tan.ToVector3();
                        Wgs84.Split(pts[i], out p, out plo);
                        float s = (float)i / Steps;
                        for (int k = -1; k <= 1; k += 2)
                        {
                            verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(k, f.width)); cols.Add(col);
                            anim.Add(new Vector2(s, 0));
                        }
                        if (i > 0)
                        {
                            int a = start + (i - 1) * 2;
                            tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                            tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
                        }
                    }
                    // Pulse settings ride in the second channel's y: speed and count packed.
                    for (int v = start; v < verts.Count; v++) anim[v] = new Vector2(anim[v].x, Mathf.Floor(f.count) * 16f + Mathf.Clamp(f.speed, 0f, 15.9f));
                    _start.Add(start); _end.Add(verts.Count);
                }
            _mesh = new Mesh { name = "Flows", indexFormat = IndexFormat.UInt32 };
            _mesh.SetVertices(verts); _mesh.SetNormals(norms); _mesh.SetUVs(0, uvs); _mesh.SetUVs(1, lows); _mesh.SetUVs(2, anim);
            _colors = cols.ToArray();
            _mesh.colors = _colors;
            _mesh.SetTriangles(tris, 0);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            var sh = Shader.Find("AtlasVR/Flow");
            _glow = new Material(sh) { renderQueue = 2450 };
            _glow.SetFloat("_Glow", 1f);
            _core = new Material(sh) { renderQueue = 2451 };
            _core.SetFloat("_Glow", 0f);
            _on = new bool[_start.Count];
            if (d != null && d.slides != null) foreach (var s in d.slides) if (s.id != null) _bySlide[s.id] = s.flows;
        }

        public bool HasFlows(string slideId) { string[] x; return slideId != null && _bySlide.TryGetValue(slideId, out x) && x != null && x.Length > 0; }

        /// Shows the slide's flows; all of them when the viewer turned flows on for a slide without any; none when off.
        public void SetShow(string slideId, bool on, bool all)
        {
            string[] ids = null;
            if (slideId != null) _bySlide.TryGetValue(slideId, out ids);
            var want = new HashSet<string>(ids ?? new string[0]);
            _any = false;
            for (int i = 0; i < _on.Length; i++)
            {
                _on[i] = on && (all || want.Contains(_d.flows[i].id));
                _any |= _on[i];
                float a = _on[i] ? 1f : 0f;
                for (int v = _start[i]; v < _end[i]; v++) _colors[v].a = a;
            }
            _mesh.colors = _colors;
        }

        public void Draw(GlobeRig rig)
        {
            if (!_any || rig == null) return;
            float w = 0.00034f;   // per web pixel of width
            var b = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
            _glow.SetFloat("_WidthWS", w * 3.2f);
            _core.SetFloat("_WidthWS", w);
            Graphics.RenderMesh(new RenderParams(_glow) { worldBounds = b, shadowCastingMode = ShadowCastingMode.Off }, _mesh, 0, Matrix4x4.identity);
            Graphics.RenderMesh(new RenderParams(_core) { worldBounds = b, shadowCastingMode = ShadowCastingMode.Off }, _mesh, 0, Matrix4x4.identity);
        }

        public PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float tol, out float best)
        {
            best = tol; PickInfo hit = null;
            if (!_any) return null;
            var g = rig.GlobeToWorld;
            for (int i = 0; i < _on.Length; i++)
            {
                if (!_on[i]) continue;
                foreach (var e in _pick[i])
                {
                    Vector3 p = g.MultiplyPoint3x4(e);
                    if (!rig.Visible(p, (p - rig.BallCenter).normalized, eye)) continue;   // behind the Earth
                    float a = Vector3.Angle(ray.direction, p - ray.origin);
                    if (a >= best) continue;
                    best = a;
                    var f = _d.flows[i];
                    bool data = f.kind == "data";
                    double mlon, mlat;
                    Wgs84.Slerp(f.lon0, f.lat0, f.lon1, f.lat1, 0.5, out mlon, out mlat);   // safe across the antimeridian
                    hit = new PickInfo { title = f.from + " → " + f.to, subtitle = !string.IsNullOrEmpty(f.builder) ? f.builder + " footprint" : data ? "Data route" : "Power supply", refs = new string[0],
                        lon = mlon, lat = mlat };
                    hit.list.Add(data ? (!string.IsNullOrEmpty(f.builder) ? "Two sites of one builder on different continents. The arc joins them; it is not a cable." : "Traffic from a coastal city to an inland hub, drawn schematically.")
                                      : "The plant that feeds or sits beside this campus. The line is schematic.");
                }
            }
            return hit;
        }
    }
}
