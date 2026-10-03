// Country and state borders, for bearings: thin ribbons of constant angular width drawn with
// the cable shader. Country lines (and coasts) show at every height; state and province lines
// fade in as you come down. Data: Natural Earth admin-1 polygons (public domain).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AtlasVR
{
    public class BorderLayer
    {
        public bool countriesOn = true, statesOn = true;
        readonly Mesh _countries, _states;
        readonly Material _cMat, _sMat;
        const double StepDeg = 1.0;

        public BorderLayer(PkgBorders d)
        {
            _countries = Build("Country borders", d != null ? d.countries : null);
            _states = Build("State borders", d != null ? d.states : null);
            var sh = Shader.Find("AtlasVR/Ribbon");
            _cMat = new Material(sh) { renderQueue = 2430 };
            _sMat = new Material(sh) { renderQueue = 2429 };
        }

        static Mesh Build(string name, PkgLine[] lines)
        {
            if (lines == null || lines.Length == 0) return null;
            var verts = new List<Vector3>(); var lows = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            var pts = new List<D3>();
            foreach (var ln in lines)
            {
                if (ln.lonlat == null || ln.lonlat.Length < 4) continue;
                pts.Clear();
                int n = ln.lonlat.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    double lon = ln.lonlat[i * 2], lat = ln.lonlat[i * 2 + 1];
                    if (i == 0) { pts.Add(Wgs84.ToEcef(lon, lat, 0)); continue; }
                    double lonP = ln.lonlat[(i - 1) * 2], latP = ln.lonlat[(i - 1) * 2 + 1];
                    double ang = Wgs84.AngleDeg(lonP, latP, lon, lat);
                    if (ang < 1e-7) continue;
                    int steps = Math.Max(1, (int)Math.Ceiling(ang / StepDeg)); // long straight borders follow the surface
                    for (int k = 1; k <= steps; k++)
                    {
                        double lo, la;
                        Wgs84.Slerp(lonP, latP, lon, lat, (double)k / steps, out lo, out la);
                        pts.Add(Wgs84.ToEcef(lo, la, 0));
                    }
                }
                if (pts.Count < 2) continue;
                int baseV = verts.Count;
                for (int i = 0; i < pts.Count; i++)
                {
                    D3 prev = pts[Math.Max(0, i - 1)], next = pts[Math.Min(pts.Count - 1, i + 1)];
                    D3 side = D3.Cross(pts[i].Normalized, (next - prev).Normalized).Normalized;
                    Vector3 p, plo, sv = side.ToVector3();
                    Wgs84.Split(pts[i], out p, out plo);
                    verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(-1, 1));
                    verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(1, 1));
                    if (i > 0)
                    {
                        int a = baseV + (i - 1) * 2;
                        tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                        tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
                    }
                }
            }
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetUVs(1, lows);
            var cols = new Color[verts.Count];
            for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            m.colors = cols;
            m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            return m;
        }

        public void Draw(GlobeRig rig)
        {
            if (rig == null || rig.mode != ViewMode.Flight) return;
            double vh = rig.ViewHeight;
            // Fade out low over the ground, where the imagery itself gives the bearings.
            float low = Mathf.InverseLerp(4000f, 25000f, (float)vh);
            var bounds = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
            if (countriesOn && _countries != null && low > 0f)
            {
                _cMat.SetFloat("_WidthWS", 0.0011f);
                _cMat.SetFloat("_LiftWS", 0.0008f);
                _cMat.SetColor("_Tint", new Color(1f, 0.96f, 0.82f, 0.6f * low));
                Render(_cMat, _countries, bounds);
            }
            // States come in below about 4,000 km.
            float st = Mathf.InverseLerp(4.5e6f, 2.0e6f, (float)vh) * low;
            if (statesOn && _states != null && st > 0f)
            {
                _sMat.SetFloat("_WidthWS", 0.0007f);
                _sMat.SetFloat("_LiftWS", 0.0008f);
                _sMat.SetColor("_Tint", new Color(0.85f, 0.9f, 1f, 0.38f * st));
                Render(_sMat, _states, bounds);
            }
        }

        static void Render(Material mat, Mesh mesh, Bounds b)
        {
            var rp = new RenderParams(mat) { worldBounds = b, shadowCastingMode = ShadowCastingMode.Off };
            Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
        }
    }
}
