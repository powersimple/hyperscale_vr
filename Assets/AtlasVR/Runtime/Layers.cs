// The layers of the compute-map chapter: PeeringDB's data centers (points up close, one
// count per country from afar), Epoch AI's compute sites (magenta discs sized by H100
// equivalents, rings for campuses under construction), TeleGeography's cables and landing
// points, and the named sites a slide calls out. Every marker can be picked for its card.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace AtlasVR
{
    /// What a pick returns: enough to fill the tooltip card.
    public class PickInfo
    {
        public string title, subtitle;
        public List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
        public List<string> list = new List<string>();
        public string[] refs = new string[0];
        public double lon, lat;
    }

    public interface IPickable
    {
        /// Nearest visible item to the ray within the angular tolerance, or null.
        PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float toleranceDeg, out float angle);
    }

    /// Filters the legend palette can switch off, keyed as the web legend keys them.
    public static class LegendFilter
    {
        static readonly HashSet<string> Off = new HashSet<string>();
        public static event Action Changed;
        public static bool IsOff(string key) { return Off.Contains(key); }
        public static void Set(string key, bool off)
        {
            if (off ? Off.Add(key) : Off.Remove(key)) { if (Changed != null) Changed(); }
        }
        public static void Clear() { if (Off.Count == 0) return; Off.Clear(); if (Changed != null) Changed(); }
    }

    static class Fmt
    {
        public static string N(double v) { return Math.Round(v).ToString("N0", System.Globalization.CultureInfo.InvariantCulture); }
        public static string Region(string cc)
        {
            switch (cc)
            {
                case "US": return "United States"; case "GB": return "United Kingdom"; case "DE": return "Germany"; case "FR": return "France";
                case "NL": return "Netherlands"; case "BR": return "Brazil"; case "IN": return "India"; case "JP": return "Japan"; case "CN": return "China";
                case "CA": return "Canada"; case "AU": return "Australia"; case "SG": return "Singapore"; case "HK": return "Hong Kong"; case "IE": return "Ireland";
                case "ES": return "Spain"; case "IT": return "Italy"; case "SE": return "Sweden"; case "PL": return "Poland"; case "RU": return "Russia";
                case "ZA": return "South Africa"; case "MX": return "Mexico"; case "AR": return "Argentina"; case "KR": return "South Korea"; case "TW": return "Taiwan";
                case "ID": return "Indonesia"; case "MY": return "Malaysia"; case "TR": return "Türkiye"; case "IL": return "Israel"; case "AE": return "United Arab Emirates";
                case "SA": return "Saudi Arabia"; case "EG": return "Egypt"; case "NG": return "Nigeria"; case "KE": return "Kenya"; case "CH": return "Switzerland";
                case "AT": return "Austria"; case "BE": return "Belgium"; case "DK": return "Denmark"; case "NO": return "Norway"; case "FI": return "Finland";
                case "PT": return "Portugal"; case "CZ": return "Czechia"; case "RO": return "Romania"; case "UA": return "Ukraine"; case "NZ": return "New Zealand";
                case "CL": return "Chile"; case "CO": return "Colombia"; case "PE": return "Peru"; case "TH": return "Thailand"; case "VN": return "Vietnam"; case "PH": return "Philippines";
                default: return cc;
            }
        }
    }

    /// World-space text labels that face the viewer and hide on the far side or outside the lens.
    public class LabelPool
    {
        readonly Transform _root;
        readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        readonly List<D3> _ecef = new List<D3>();
        readonly List<Vector3> _up = new List<Vector3>();
        public bool visible;
        public float heightWS = 0.012f;
        public float liftWS = 0.012f;

        public LabelPool(Transform parent, string name)
        {
            _root = new GameObject(name).transform;
            _root.SetParent(parent, false);
        }

        public void Clear()
        {
            foreach (var l in _labels) if (l != null) UnityEngine.Object.Destroy(l.gameObject);
            _labels.Clear(); _ecef.Clear(); _up.Clear();
        }

        public void Add(string text, double lon, double lat, Color color, float scale = 1f)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(_root, false);
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = 10f;    // 3D TextMeshPro: about one unit of line height at scale 1
            t.fontStyle = FontStyles.Bold;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            go.transform.localScale = Vector3.one * scale;
            _labels.Add(t);
            _ecef.Add(Wgs84.ToEcef(lon, lat, 0));
            _up.Add(Wgs84.Up(lon, lat).ToVector3());
        }

        public void Update(GlobeRig rig, Transform eye)
        {
            bool on = visible && rig != null && eye != null;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) return;
            var g = rig.GlobeToWorld;
            for (int i = 0; i < _labels.Count; i++)
            {
                Vector3 p = g.MultiplyPoint3x4(_ecef[i].ToVector3());
                Vector3 up = g.MultiplyVector(_up[i]).normalized;
                bool vis = rig.Visible(p, up, eye.position);
                var l = _labels[i];
                if (l.gameObject.activeSelf != vis) l.gameObject.SetActive(vis);
                if (!vis) continue;
                l.transform.position = p + up * liftWS;
                l.transform.rotation = Quaternion.LookRotation(l.transform.position - eye.position, Vector3.up);
                float s = heightWS * Mathf.Clamp(Vector3.Distance(eye.position, l.transform.position) / 0.8f, 0.6f, 3f);
                l.transform.localScale = Vector3.one * s;
            }
        }
    }

    public class ComputeLayer : IPickable
    {
        readonly PkgComputeLayer _d;
        readonly MarkerSet _dcRim, _dcCore, _ctyRim, _ctyFill, _aiGlow, _aiRim, _aiCore, _aiBuild;
        readonly LabelPool _counts;
        readonly List<int> _aiIndex = new List<int>(), _buildIndex = new List<int>();
        PkgComputeShow _show;
        public const double CountriesAbove = 4.5e6; // view height where points give way to country counts

        public ComputeLayer(PkgComputeLayer d, Transform parent)
        {
            _d = d;
            Func<string, string, string> pick = (v, dflt) => string.IsNullOrEmpty(v) ? dflt : v;
            Color dc = Hex.Color(pick(d.colors != null ? d.colors.dc : null, "#99e9f2"), 0.95f);
            Color ai = Hex.Color(pick(d.colors != null ? d.colors.ai : null, "#f03e9e"), 0.95f);
            Color fill = Hex.Color(pick(d.colors != null ? d.colors.countryFill : null, "#0b1d33"), 0.9f);
            Color edge = new Color(0.04f, 0.11f, 0.2f, 0.9f);
            Mesh disc = Meshes.Disc(16), discFine = Meshes.Disc(32), ring = Meshes.Ring(0.36f, 40);

            _dcRim = new MarkerSet(disc, true, 0) { sizeWS = 0.0034f };
            _dcCore = new MarkerSet(disc, true, 1) { sizeWS = 0.0026f, minLiftWS = 0.0023f };
            var cr = new List<MarkerDef>(); var cc = new List<MarkerDef>();
            foreach (var p in d.dcs) { cr.Add(new MarkerDef(p.lon, p.lat, 1f, edge)); cc.Add(new MarkerDef(p.lon, p.lat, 1f, dc)); }
            _dcRim.Set(cr); _dcCore.Set(cc);

            _ctyRim = new MarkerSet(discFine, true, 0) { sizeWS = 0.0062f };
            _ctyFill = new MarkerSet(discFine, true, 1) { sizeWS = 0.0062f, minLiftWS = 0.0024f };
            _counts = new LabelPool(parent, "Data center counts") { heightWS = 0.0075f, liftWS = 0.004f };
            var rr = new List<MarkerDef>(); var ff = new List<MarkerDef>();
            foreach (var c in d.countries)
            {
                float k = Mathf.Min(2.6f, (32f + Mathf.Sqrt(c.count) * 1.5f) / 32f);
                rr.Add(new MarkerDef(c.lon, c.lat, k * 1.16f, dc));   // the rim: the disc drawn larger beneath the fill
                ff.Add(new MarkerDef(c.lon, c.lat, k, fill));
                _counts.Add(c.count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), c.lon, c.lat, Color.white, 1f);
            }
            _ctyRim.Set(rr); _ctyFill.Set(ff);

            _aiGlow = new MarkerSet(discFine, false, 0) { sizeWS = 0.004f, Queue = 2445 };
            _aiRim = new MarkerSet(discFine, true, 2) { sizeWS = 0.004f, minLiftWS = 0.0026f };
            _aiCore = new MarkerSet(discFine, true, 3) { sizeWS = 0.004f, minLiftWS = 0.0029f };
            _aiBuild = new MarkerSet(ring, true, 2) { sizeWS = 0.004f, minLiftWS = 0.0026f };
            var gm = new List<MarkerDef>(); var rm = new List<MarkerDef>(); var km = new List<MarkerDef>(); var bm = new List<MarkerDef>();
            var order = new List<int>();
            for (int i = 0; i < d.ai.Length; i++) order.Add(i);
            order.Sort((a, b) => d.ai[b].h100e.CompareTo(d.ai[a].h100e));
            foreach (int i in order)
            {
                var s = d.ai[i];
                if (s.building)
                {
                    bm.Add(new MarkerDef(s.lon, s.lat, 1.3f, ai)); _buildIndex.Add(i);
                    continue;
                }
                float px = Mathf.Clamp(4f + Mathf.Sqrt((float)(s.h100e > 0 ? s.h100e : 500)) / 22f, 5f, 40f);
                float k = px / 10f;
                gm.Add(new MarkerDef(s.lon, s.lat, k * 1.9f, new Color(ai.r, ai.g, ai.b, 0.22f)));
                rm.Add(new MarkerDef(s.lon, s.lat, k * 1.15f, new Color(1, 1, 1, 0.9f)));
                km.Add(new MarkerDef(s.lon, s.lat, k, ai));
                _aiIndex.Add(i);
            }
            _aiGlow.Set(gm); _aiRim.Set(rm); _aiCore.Set(km); _aiBuild.Set(bm);
        }

        public void SetShow(PkgComputeShow s) { _show = s; }

        public void Draw(GlobeRig rig, Transform eye)
        {
            bool on = _show != null && _show.on;
            bool far = rig.ViewHeight > CountriesAbove;
            bool dcs = on && _show.dcs && !LegendFilter.IsOff("compute:dcs");
            bool cty = dcs && _show.countries && far && !LegendFilter.IsOff("compute:countries");
            bool pts = dcs && (!far || !_show.countries || LegendFilter.IsOff("compute:countries"));
            bool ai = on && _show.ai && !LegendFilter.IsOff("compute:ai");
            bool build = on && _show.ai && !LegendFilter.IsOff("compute:building");

            float zoom = Mathf.Clamp((float)(2.0e7 / rig.ViewHeight), 1f, 6f);
            float ptScale = Mathf.Lerp(0.9f, 1.8f, (zoom - 1f) / 5f);
            _dcRim.sizeWS = 0.0034f * ptScale; _dcCore.sizeWS = 0.0026f * ptScale;
            _dcRim.visible = _dcCore.visible = pts;
            _ctyRim.visible = _ctyFill.visible = cty; _counts.visible = cty;
            _aiGlow.visible = _aiRim.visible = _aiCore.visible = ai;
            _aiBuild.visible = build;

            _dcRim.Draw(rig); _dcCore.Draw(rig); _ctyRim.Draw(rig); _ctyFill.Draw(rig);
            _aiGlow.Draw(rig); _aiRim.Draw(rig); _aiCore.Draw(rig); _aiBuild.Draw(rig);
            _counts.Update(rig, eye);
        }

        public PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float tol, out float best)
        {
            best = tol;
            PickInfo hit = null;
            if (_show == null || !_show.on) return null;
            var g = rig.GlobeToWorld;
            bool far = rig.ViewHeight > CountriesAbove;
            if (_show.ai && !LegendFilter.IsOff("compute:ai"))
            {
                foreach (var s in _d.ai)
                {
                    if (s.building && LegendFilter.IsOff("compute:building")) continue;
                    float a = Angle(ray, rig, g, eye, s.lon, s.lat);
                    if (a < best) { best = a; hit = AiCard(s); }
                }
            }
            if (_show.dcs && !LegendFilter.IsOff("compute:dcs") && hit == null)
            {
                if (far && _show.countries && !LegendFilter.IsOff("compute:countries"))
                {
                    foreach (var c in _d.countries)
                    {
                        float a = Angle(ray, rig, g, eye, c.lon, c.lat);
                        if (a < best)
                        {
                            best = a;
                            hit = new PickInfo { title = Fmt.Region(c.cc), subtitle = Fmt.N(c.count) + (c.count == 1 ? " data center in PeeringDB" : " data centers in PeeringDB"), refs = _d.dcRefs, lon = c.lon, lat = c.lat };
                        }
                    }
                }
                else
                {
                    foreach (var p in _d.dcs)
                    {
                        float a = Angle(ray, rig, g, eye, p.lon, p.lat);
                        if (a < best)
                        {
                            best = a;
                            var info = new PickInfo { title = p.name, subtitle = (string.IsNullOrEmpty(p.org) ? "Data center" : p.org) + " · Data center", refs = _d.dcRefs, lon = p.lon, lat = p.lat };
                            info.rows.Add(new KeyValuePair<string, string>("Place", string.IsNullOrEmpty(p.city) ? p.cc : p.city + ", " + p.cc));
                            if (p.nets > 0) info.rows.Add(new KeyValuePair<string, string>("Networks present", Fmt.N(p.nets)));
                            hit = info;
                        }
                    }
                }
            }
            return hit;
        }

        static float Angle(Ray ray, GlobeRig rig, Matrix4x4 g, Vector3 eye, double lon, double lat)
        {
            Vector3 p = g.MultiplyPoint3x4(Wgs84.ToEcef(lon, lat, 0).ToVector3());
            Vector3 up = g.MultiplyVector(Wgs84.Up(lon, lat).ToVector3()).normalized;
            if (!rig.Visible(p, up, eye)) return float.MaxValue;
            return Vector3.Angle(ray.direction, p - ray.origin);
        }

        static PickInfo AiCard(PkgAi s)
        {
            var i = new PickInfo { title = s.name, refs = s.refs ?? new string[0], lon = s.lon, lat = s.lat };
            bool many = s.members != null && s.members.Length > 0;
            i.subtitle = many ? (s.kind == "frontier" ? "AI data centers at one place" : "GPU clusters Epoch places at one point") : (s.kind == "frontier" ? "AI data center" : "AI GPU cluster");
            Action<string, string> row = (k, v) => i.rows.Add(new KeyValuePair<string, string>(k, v));
            row("Owner", string.IsNullOrEmpty(s.owner) ? "Not disclosed" : s.owner);
            if (!string.IsNullOrEmpty(s.users) && s.users != s.owner) row("Users", s.users);
            if (!string.IsNullOrEmpty(s.place)) row("Place", s.near ? "Near " + s.place : s.place);
            if (!string.IsNullOrEmpty(s.country)) row("Country", s.country);
            if (s.building) row("Status", "Under construction");
            if (s.chips > 0) row("AI chips", Fmt.N(s.chips) + (string.IsNullOrEmpty(s.chip) ? "" : " (" + s.chip + ")"));
            else if (!string.IsNullOrEmpty(s.chip)) row("Chips", s.chip.Replace(",", ", "));
            if (s.h100e > 0) row("H100 equivalents", Fmt.N(s.h100e));
            if (s.mw > 0) row("Power", Fmt.N(s.mw) + " MW");
            if (s.costBn > 0) row("Capital cost", "$" + s.costBn.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " billion");
            if (!string.IsNullOrEmpty(s.date)) row("Operational", s.date);
            if (many) for (int k = 0; k < s.members.Length && k < 14; k++) i.list.Add(s.members[k]);
            return i;
        }
    }

    public class CableLayer : IPickable
    {
        readonly PkgTeleLayer _d;
        readonly Mesh _mesh;
        readonly Material _mat;
        readonly MarkerSet _landRim, _landCore;
        readonly List<int> _start = new List<int>(), _end = new List<int>(); // vertex range per cable
        readonly List<Vector3[]> _pickPts = new List<Vector3[]>();             // coarse ECEF points per cable, for picking
        Color[] _colors;
        Vector2[] _uvs;
        PkgTeleShow _show;
        const double StepDeg = 0.5;

        public CableLayer(PkgTeleLayer d)
        {
            _d = d;
            var verts = new List<Vector3>(); var lows = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var c in d.cables)
            {
                int start = verts.Count;
                Color col = Hex.Color(c.color, 0.9f);
                var pick = new List<Vector3>();
                foreach (var ln in c.lines)
                {
                    if (ln.lonlat == null || ln.lonlat.Length < 4) continue;
                    // Densify along great circles so long spans follow the surface.
                    var pts = new List<D3>();
                    int n = ln.lonlat.Length / 2;
                    for (int i = 0; i < n; i++)
                    {
                        double lon0 = ln.lonlat[i * 2], lat0 = ln.lonlat[i * 2 + 1];
                        if (i == 0) { pts.Add(Wgs84.ToEcef(lon0, lat0, 0)); pick.Add(pts[0].ToVector3()); continue; }
                        double lonP = ln.lonlat[(i - 1) * 2], latP = ln.lonlat[(i - 1) * 2 + 1];
                        double ang = Wgs84.AngleDeg(lonP, latP, lon0, lat0);
                        int steps = Math.Max(1, (int)Math.Ceiling(ang / StepDeg));
                        if (ang < 1e-7) continue; // repeated point: no length, no tangent
                        for (int k = 1; k <= steps; k++)
                        {
                            double lo, la;
                            Wgs84.Slerp(lonP, latP, lon0, lat0, (double)k / steps, out lo, out la);
                            pts.Add(Wgs84.ToEcef(lo, la, 0));
                        }
                        pick.Add(Wgs84.ToEcef(lon0, lat0, 0).ToVector3());
                    }
                    if (pts.Count < 2) continue;
                    int baseV = verts.Count;
                    for (int i = 0; i < pts.Count; i++)
                    {
                        D3 prev = pts[Math.Max(0, i - 1)], next = pts[Math.Min(pts.Count - 1, i + 1)];
                        D3 tan = (next - prev).Normalized;
                        D3 side = D3.Cross(pts[i].Normalized, tan).Normalized;
                        Vector3 p, plo, sv = side.ToVector3();
                        Wgs84.Split(pts[i], out p, out plo);
                        verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(-1, 1)); cols.Add(col);
                        verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(1, 1)); cols.Add(col);
                        if (i > 0)
                        {
                            int a = baseV + (i - 1) * 2;
                            tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                            tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
                        }
                    }
                }
                _start.Add(start); _end.Add(verts.Count);
                _pickPts.Add(pick.ToArray());
            }
            _mesh = new Mesh { name = "TeleGeography cables", indexFormat = IndexFormat.UInt32 };
            _mesh.SetVertices(verts); _mesh.SetNormals(norms); _mesh.SetUVs(0, uvs); _mesh.SetUVs(1, lows); _mesh.SetColors(cols);
            _mesh.SetTriangles(tris, 0);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            _colors = cols.ToArray();
            _uvs = uvs.ToArray();
            _mat = new Material(Shader.Find("AtlasVR/Ribbon"));
            _mat.renderQueue = 2440; // before the opaque markers, so markers sit on top of the cables

            var disc = Meshes.Disc(12);
            _landRim = new MarkerSet(disc, true, 4) { sizeWS = 0.0024f };
            _landCore = new MarkerSet(disc, true, 5) { sizeWS = 0.0018f, minLiftWS = 0.0023f };
            var a1 = new List<MarkerDef>(); var a2 = new List<MarkerDef>();
            foreach (var l in d.landings) { a1.Add(new MarkerDef(l.lon, l.lat, 1f, new Color(0, 0, 0, 0.6f))); a2.Add(new MarkerDef(l.lon, l.lat, 1f, new Color(1, 1, 1, 0.85f))); }
            _landRim.Set(a1); _landCore.Set(a2);
        }

        public void SetShow(PkgTeleShow s)
        {
            _show = s;
            if (s == null || !s.on) return;
            var hi = new HashSet<string>(s.highlight ?? new string[0]);
            bool any = hi.Count > 0;
            for (int c = 0; c < _d.cables.Length; c++)
            {
                bool on = !any || hi.Contains(_d.cables[c].id);
                float alpha = any ? (on ? 1f : 0.18f) : 0.9f;
                float width = any ? (on ? 2.0f : 0.62f) : 1f;
                Color baseCol = Hex.Color(_d.cables[c].color, alpha);
                for (int v = _start[c]; v < _end[c]; v++)
                {
                    _colors[v] = baseCol;
                    _uvs[v] = new Vector2(_uvs[v].x, width);
                }
            }
            _mesh.colors = _colors;
            _mesh.uv = _uvs;
        }

        public void Draw(GlobeRig rig)
        {
            bool on = _show != null && _show.on;
            if (on && !LegendFilter.IsOff("tc:cables"))
            {
                _mat.SetFloat("_WidthWS", rig.mode == ViewMode.Flight ? 0.0016f : Mathf.Clamp(0.0011f * Mathf.Pow((float)(2.0e7 / rig.ViewHeight), 0.25f), 0.0009f, 0.003f));
                _mat.SetFloat("_LiftWS", 0.0015f);
                var rp = new RenderParams(_mat);
                rp.worldBounds = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
                rp.shadowCastingMode = ShadowCastingMode.Off;
                Graphics.RenderMesh(rp, _mesh, 0, Matrix4x4.identity);
            }
            _landRim.visible = _landCore.visible = on && _show.landings && !LegendFilter.IsOff("tc:landings");
            _landRim.Draw(rig); _landCore.Draw(rig);
        }

        public PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float tol, out float best)
        {
            best = tol;
            PickInfo hit = null;
            if (_show == null || !_show.on) return null;
            var g = rig.GlobeToWorld;
            if (_show.landings && !LegendFilter.IsOff("tc:landings"))
            {
                foreach (var l in _d.landings)
                {
                    Vector3 p = g.MultiplyPoint3x4(Wgs84.ToEcef(l.lon, l.lat, 0).ToVector3());
                    Vector3 up = g.MultiplyVector(Wgs84.Up(l.lon, l.lat).ToVector3()).normalized;
                    if (!rig.Visible(p, up, eye)) continue;
                    float a = Vector3.Angle(ray.direction, p - ray.origin);
                    if (a < best) { best = a; hit = new PickInfo { title = l.name, subtitle = "Cable landing point", refs = _d.refs, lon = l.lon, lat = l.lat }; }
                }
            }
            if (hit != null || LegendFilter.IsOff("tc:cables")) return hit;
            for (int c = 0; c < _d.cables.Length; c++)
            {
                foreach (var e in _pickPts[c])
                {
                    Vector3 p = g.MultiplyPoint3x4(e);
                    Vector3 up = g.MultiplyVector(e.normalized).normalized;
                    if (!rig.Visible(p, up, eye)) continue;
                    float a = Vector3.Angle(ray.direction, p - ray.origin) * 1.4f; // landings win ties
                    if (a < best) { best = a; hit = new PickInfo { title = _d.cables[c].name, subtitle = "Submarine cable system", refs = _d.refs }; }
                }
            }
            return hit;
        }
    }

    /// The data centers and plants a slide names (show.dc, show.power, highlight, labels).
    public class SitesLayer : IPickable
    {
        readonly PkgSites _d;
        readonly MarkerSet _rim, _core;
        readonly LabelPool _labels;
        readonly List<PkgSite> _shownDc = new List<PkgSite>();
        readonly List<PkgPlant> _shownPw = new List<PkgPlant>();
        static readonly Color DcColor = Hex.Color("#4dabf7"), Highlight = Hex.Color("#ffd43b");

        public SitesLayer(PkgSites d, Transform parent)
        {
            _d = d;
            var disc = Meshes.Disc(24);
            _rim = new MarkerSet(disc, true, 6) { sizeWS = 0.005f };
            _core = new MarkerSet(disc, true, 7) { sizeWS = 0.005f, minLiftWS = 0.0024f };
            _labels = new LabelPool(parent, "Site labels") { heightWS = 0.0095f, liftWS = 0.012f };
        }

        static Color FuelColor(string f)
        {
            switch (f)
            {
                case "nuclear": return Hex.Color("#be4bdb");
                case "gas": return Hex.Color("#fd7e14");
                case "coal": return Hex.Color("#868e96");
                case "hydro": return Hex.Color("#228be6");
                case "solar": return Hex.Color("#fab005");
                case "wind": return Hex.Color("#20c997");
                case "geothermal": return Hex.Color("#e8590c");
                default: return Hex.Color("#adb5bd");
            }
        }

        // The web legend's matcher: true shows all, a list names ids (or regions, kinds, fuels).
        static bool Matches(string[] spec, string id, string a, string b)
        {
            if (spec == null || spec.Length == 0) return false;
            foreach (var s in spec) if (s == "all" || s == id || s == a || s == b) return true;
            return false;
        }

        public void SetShow(PkgShow show)
        {
            _shownDc.Clear(); _shownPw.Clear(); _labels.Clear();
            var hl = new HashSet<string>(show != null && show.highlight != null ? show.highlight : new string[0]);
            var lab = new HashSet<string>(show != null && show.labels != null ? show.labels : new string[0]);
            var rc = new List<MarkerDef>(); var cc = new List<MarkerDef>();
            if (show != null && _d != null)
            {
                foreach (var s in _d.datacenters)
                {
                    if (!Matches(show.dc, s.id, s.region, s.kind) && !hl.Contains(s.id)) continue;
                    _shownDc.Add(s);
                    bool h = hl.Contains(s.id);
                    rc.Add(new MarkerDef(s.lon, s.lat, h ? 1.5f * 1.24f : 1.24f, h ? Highlight : Color.white)); cc.Add(new MarkerDef(s.lon, s.lat, h ? 1.5f : 1f, DcColor));
                    if (lab.Contains(s.id) || h) _labels.Add(s.name, s.lon, s.lat, Color.white);
                }
                foreach (var p in _d.plants)
                {
                    if (!Matches(show.power, p.id, p.fuel, null) && !hl.Contains(p.id)) continue;
                    _shownPw.Add(p);
                    bool h = hl.Contains(p.id);
                    rc.Add(new MarkerDef(p.lon, p.lat, (h ? 1.4f : 0.9f) * 1.24f, h ? Highlight : new Color(0.05f, 0.05f, 0.05f, 1f))); cc.Add(new MarkerDef(p.lon, p.lat, h ? 1.4f : 0.9f, FuelColor(p.fuel)));
                    if (lab.Contains(p.id) || h) _labels.Add(p.name, p.lon, p.lat, Color.white);
                }
            }
            _rim.Set(rc); _core.Set(cc);
            _rim.visible = _core.visible = cc.Count > 0;
            _labels.visible = true;
        }

        public void Draw(GlobeRig rig, Transform eye)
        {
            _rim.Draw(rig); _core.Draw(rig);
            _labels.Update(rig, eye);
        }

        public PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float tol, out float best)
        {
            best = tol; PickInfo hit = null;
            var g = rig.GlobeToWorld;
            foreach (var s in _shownDc)
            {
                float a = Ang(ray, rig, g, eye, s.lon, s.lat);
                if (a >= best) continue;
                best = a;
                hit = new PickInfo { title = s.name, subtitle = "Data center", refs = s.refs ?? new string[0], lon = s.lon, lat = s.lat };
                if (s.mw > 0 && s.refs != null && s.refs.Length > 0) hit.rows.Add(new KeyValuePair<string, string>("Capacity", Fmt.N(s.mw) + " MW"));
            }
            foreach (var p in _shownPw)
            {
                float a = Ang(ray, rig, g, eye, p.lon, p.lat);
                if (a >= best) continue;
                best = a;
                hit = new PickInfo { title = p.name, subtitle = "Power plant", refs = p.refs ?? new string[0], lon = p.lon, lat = p.lat };
                if (!string.IsNullOrEmpty(p.fuel)) hit.rows.Add(new KeyValuePair<string, string>("Fuel", char.ToUpper(p.fuel[0]) + p.fuel.Substring(1)));
                if (p.mw > 0 && p.refs != null && p.refs.Length > 0) hit.rows.Add(new KeyValuePair<string, string>("Capacity", Fmt.N(p.mw) + " MW"));
            }
            return hit;
        }

        static float Ang(Ray ray, GlobeRig rig, Matrix4x4 g, Vector3 eye, double lon, double lat)
        {
            Vector3 p = g.MultiplyPoint3x4(Wgs84.ToEcef(lon, lat, 0).ToVector3());
            Vector3 up = g.MultiplyVector(Wgs84.Up(lon, lat).ToVector3()).normalized;
            if (!rig.Visible(p, up, eye)) return float.MaxValue;
            return Vector3.Angle(ray.direction, p - ray.origin);
        }
    }
}
