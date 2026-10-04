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
        /// Forget every listener and setting (the scene is ending).
        public static void Reset() { Changed = null; Off.Clear(); }
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
        readonly List<Vector2> _lonlat = new List<Vector2>();
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
            _labels.Clear(); _ecef.Clear(); _up.Clear(); _lonlat.Clear();
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
            UI.OverText(t);
            go.transform.localScale = Vector3.one * scale;
            _labels.Add(t);
            _ecef.Add(Wgs84.ToEcef(lon, lat, 0));
            _up.Add(Wgs84.Up(lon, lat).ToVector3());
            _lonlat.Add(new Vector2((float)lon, (float)lat));
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
                bool vis = rig.Visible(p, up, eye.position) && !LabelFocus.Near(_lonlat[i].x, _lonlat[i].y);
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
        readonly MarkerSet _ctyRim, _ctyFill;
        readonly MarkerSet _dcGems, _aiGems, _buildGems;   // small shiny spheres in gem colors
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

            // Data centers: small sapphire spheres.
            var ball = Meshes.Ball();
            _dcGems = new MarkerSet(ball, true, 0, "AtlasVR/Gem") { sizeWS = 0.0026f, minLiftWS = 0.0015f };
            var dd = new List<MarkerDef>();
            foreach (var p in d.dcs) dd.Add(new MarkerDef(p.lon, p.lat, 1f, Legend.Sapphire));
            _dcGems.Set(dd);

            dc = Legend.Emerald;   // the per-country count discs: an emerald rim
            _ctyRim = new MarkerSet(discFine, true, 0) { sizeWS = 0.0062f };
            _ctyFill = new MarkerSet(discFine, true, 1) { sizeWS = 0.0062f, minLiftWS = 0.0024f };
            _counts = new LabelPool(parent, "Data center counts") { heightWS = 0.006f, liftWS = 0.004f };
            var rr = new List<MarkerDef>(); var ff = new List<MarkerDef>();
            foreach (var c in d.countries)
            {
                float k = Mathf.Min(2.6f, (32f + Mathf.Sqrt(c.count) * 1.5f) / 32f);
                rr.Add(new MarkerDef(c.lon, c.lat, k * 1.16f, dc));   // the rim: the disc drawn larger beneath the fill
                ff.Add(new MarkerDef(c.lon, c.lat, k, fill));
                _counts.Add(c.count.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), c.lon, c.lat, Color.white, 1f);
            }
            _ctyRim.Set(rr); _ctyFill.Set(ff);

            // AI compute: ruby spheres sized by compute; under construction, topaz.
            _aiGems = new MarkerSet(ball, true, 2, "AtlasVR/Gem") { sizeWS = 0.0034f, minLiftWS = 0.0019f };
            _buildGems = new MarkerSet(ball, true, 1, "AtlasVR/Gem") { sizeWS = 0.0034f, minLiftWS = 0.0019f };
            var km = new List<MarkerDef>(); var bm = new List<MarkerDef>();
            var order = new List<int>();
            for (int i = 0; i < d.ai.Length; i++) order.Add(i);
            order.Sort((a, b) => d.ai[b].h100e.CompareTo(d.ai[a].h100e));
            foreach (int i in order)
            {
                var s = d.ai[i];
                if (s.building)
                {
                    bm.Add(new MarkerDef(s.lon, s.lat, 0.9f, Legend.Topaz)); _buildIndex.Add(i);
                    continue;
                }
                float px = Mathf.Clamp(4f + Mathf.Sqrt((float)(s.h100e > 0 ? s.h100e : 500)) / 22f, 5f, 40f);
                float k = px / 10f;
                km.Add(new MarkerDef(s.lon, s.lat, Mathf.Clamp(0.6f + k * 0.35f, 0.7f, 2.0f), Legend.Ruby));
                _aiIndex.Add(i);
            }
            _aiGems.Set(km); _buildGems.Set(bm);
            CompanyFocus.Changed += Recolor;
        }

        public void SetShow(PkgComputeShow s) { _show = s; }

        // On a players or clouds slide, each AI site of a company the slide names takes that
        // company's color (the legend's dot); the rest keep the ruby of AI compute.
        readonly Dictionary<string, Color> _companies = new Dictionary<string, Color>();

        public void SetLegend(PkgLegendItem[] legend)
        {
            _companies.Clear();
            if (legend != null)
                foreach (var it in legend)
                    if (!string.IsNullOrEmpty(it.company) && !_companies.ContainsKey(it.company)) _companies[it.company] = Hex.Color(it.color, 1f);
            Recolor();
        }

        void Recolor()
        {
            for (int j = 0; j < _aiIndex.Count; j++) _aiGems.SetColor(j, ColorFor(_d.ai[_aiIndex[j]], Legend.Ruby));
            for (int j = 0; j < _buildIndex.Count; j++) _buildGems.SetColor(j, ColorFor(_d.ai[_buildIndex[j]], Legend.Topaz));
            _aiGems.ApplyColors(); _buildGems.ApplyColors();
        }

        Color ColorFor(PkgAi s, Color dflt)
        {
            Color c;
            string co = s.company ?? "";
            if (co.Length > 0 && _companies.TryGetValue(co, out c)) c.a = 0.95f; else c = dflt;
            return CompanyFocus.Apply(c, co);
        }

        public void Draw(GlobeRig rig, Transform eye)
        {
            bool on = _show != null && _show.on;
            bool far = rig.ViewHeight > CountriesAbove;
            bool dcs = on && _show.dcs;
            bool cty = dcs && _show.countries && far && !LegendFilter.IsOff("compute:countries");
            bool pts = dcs && !cty && !LegendFilter.IsOff("compute:points");
            bool ai = on && _show.ai && !LegendFilter.IsOff("compute:ai");
            bool build = on && _show.ai && !LegendFilter.IsOff("compute:building");

            float zoom = Mathf.Clamp((float)(2.0e7 / rig.ViewHeight), 1f, 6f);
            float ptScale = Mathf.Lerp(0.9f, 1.8f, (zoom - 1f) / 5f);
            _dcGems.sizeWS = 0.0026f * ptScale; _dcGems.minLiftWS = 0.0015f * ptScale;
            _dcGems.visible = pts;
            _ctyRim.visible = _ctyFill.visible = cty; _counts.visible = cty;
            _aiGems.visible = ai;
            _buildGems.visible = build;

            _dcGems.Draw(rig); _ctyRim.Draw(rig); _ctyFill.Draw(rig);
            _buildGems.Draw(rig); _aiGems.Draw(rig);
            _counts.Update(rig, eye);
            PointsVisible = pts; AiVisible = ai; BuildingVisible = build;
        }

        public PkgComputeLayer Data { get { return _d; } }

        // A one-degree grid of the 5,274 data centers, so the laser and the nearby labels test the
        // ones close by instead of all of them.
        Dictionary<int, List<int>> _grid;
        readonly List<int> _near = new List<int>();
        static int Cell(int lonI, int latI) { return ((lonI % 360 + 360) % 360) * 1000 + latI; }

        void BuildGrid()
        {
            _grid = new Dictionary<int, List<int>>();
            for (int i = 0; i < _d.dcs.Length; i++)
            {
                int k = Cell((int)Math.Floor(_d.dcs[i].lon), (int)Math.Floor(_d.dcs[i].lat));
                List<int> l;
                if (!_grid.TryGetValue(k, out l)) { l = new List<int>(); _grid[k] = l; }
                l.Add(i);
            }
        }

        /// The data centers within about radiusDeg of a place, or null when that would be most of
        /// them anyway (the caller then walks the whole list). The list is reused: read it at once.
        public List<int> DcsNear(double lon, double lat, double radiusDeg)
        {
            if (radiusDeg > 25) return null;
            if (_grid == null) BuildGrid();
            _near.Clear();
            double lonR = radiusDeg / Math.Max(0.05, Math.Cos(Math.Min(85.0, Math.Abs(lat)) * Math.PI / 180.0));
            if (lonR > 179) lonR = 179;
            int lat0 = (int)Math.Floor(lat - radiusDeg), lat1 = (int)Math.Floor(lat + radiusDeg);
            int lon0 = (int)Math.Floor(lon - lonR), lon1 = (int)Math.Floor(lon + lonR);
            for (int a = lon0; a <= lon1; a++)
                for (int b = Math.Max(-90, lat0); b <= Math.Min(89, lat1); b++)
                {
                    List<int> l;
                    if (_grid.TryGetValue(Cell(a, b), out l)) _near.AddRange(l);
                }
            return _near;
        }
        public bool PointsVisible { get; private set; }
        public bool AiVisible { get; private set; }
        public bool BuildingVisible { get; private set; }

        public PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float tol, out float best)
        {
            best = tol;
            PickInfo hit = null;
            if (_show == null || !_show.on) return null;
            var g = rig.GlobeToWorld;
            bool far = rig.ViewHeight > CountriesAbove;
            if (_show.ai)
            {
                foreach (var s in _d.ai)
                {
                    if (s.building ? LegendFilter.IsOff("compute:building") : LegendFilter.IsOff("compute:ai")) continue;
                    float a = Angle(ray, rig, g, eye, s.lon, s.lat);
                    if (a < best) { best = a; hit = AiCard(s); }
                }
            }
            if (_show.dcs && hit == null)
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
                else if (!LegendFilter.IsOff("compute:points"))
                {
                    // Only the data centers around where the laser meets the Earth.
                    List<int> near = null;
                    double hl, ht; Vector3 hw;
                    if (rig.RayToEarth(ray, out hl, out ht, out hw))
                    {
                        // The pick cone (up to tol degrees) spreads along the ground as the ray grazes it.
                        Vector3 upW = g.MultiplyVector(Wgs84.Up(hl, ht).ToVector3()).normalized;
                        double sinG = Math.Abs(Vector3.Dot(ray.direction.normalized, upW));
                        if (sinG >= 0.3)
                        {
                            double range = Math.Max(rig.ViewHeight, Vector3.Distance(ray.origin, hw) / Math.Max(1e-9, rig.GlobeScale));
                            double meters = range * Math.Tan(tol * Math.PI / 180.0) / sinG * 1.5;
                            near = DcsNear(hl, ht, meters / 111000.0 + 0.1);
                        }
                    }
                    int count = near != null ? near.Count : _d.dcs.Length;
                    for (int k = 0; k < count; k++)
                    {
                        var p = _d.dcs[near != null ? near[k] : k];
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
        public bool LandingsVisible { get; private set; }
        public PkgTeleLayer Data { get { return _d; } }
        readonly PkgTeleLayer _d;
        readonly Mesh _mesh;
        readonly Material _mat;
        readonly MarkerSet _landRim, _landCore;
        readonly List<Vector3[]> _pickPts = new List<Vector3[]>();             // coarse ECEF points per cable, for picking
        PkgTeleShow _show;
        const double StepDeg = 0.5;

        // Overland telecom lines (OpenStreetMap), drawn as thinner amber ribbons.
        Mesh _landMesh;
        Material _landMat;
        public bool HasLand { get { return _landMesh != null; } }
        static readonly Color LandColor = new Color(1f, 0.66f, 0.3f, 0.85f);

        public CableLayer(PkgTeleLayer d, PkgLandLines land = null) : this(d)
        {
            if (land == null || land.lines == null || land.lines.Length == 0) return;
            var verts = new List<Vector3>(); var lows = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var ln in land.lines) AddRibbon(ln.lonlat, LandColor, 0.7f, verts, lows, norms, uvs, cols, tris);
            if (verts.Count == 0) return;
            _landMesh = new Mesh { name = "Land telecom lines", indexFormat = IndexFormat.UInt32 };
            _landMesh.SetVertices(verts); _landMesh.SetNormals(norms); _landMesh.SetUVs(0, uvs); _landMesh.SetUVs(1, lows); _landMesh.SetColors(cols);
            _landMesh.SetTriangles(tris, 0);
            _landMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            _landMat = new Material(Shader.Find("AtlasVR/Ribbon")) { renderQueue = 2433 };
        }

        /// One polyline as a ribbon: densified along great circles, two vertices per point.
        static void AddRibbon(double[] lonlat, Color col, float width, List<Vector3> verts, List<Vector3> lows, List<Vector3> norms, List<Vector2> uvs, List<Color> cols, List<int> tris)
        {
            if (lonlat == null || lonlat.Length < 4) return;
            var pts = new List<D3>();
            int n = lonlat.Length / 2;
            for (int i = 0; i < n; i++)
            {
                double lon0 = lonlat[i * 2], lat0 = lonlat[i * 2 + 1];
                if (i == 0) { pts.Add(Wgs84.ToEcef(lon0, lat0, 0)); continue; }
                double lonP = lonlat[(i - 1) * 2], latP = lonlat[(i - 1) * 2 + 1];
                double ang = Wgs84.AngleDeg(lonP, latP, lon0, lat0);
                if (ang < 1e-7) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(ang / StepDeg));
                for (int k = 1; k <= steps; k++)
                {
                    double lo, la;
                    Wgs84.Slerp(lonP, latP, lon0, lat0, (double)k / steps, out lo, out la);
                    pts.Add(Wgs84.ToEcef(lo, la, 0));
                }
            }
            if (pts.Count < 2) return;
            int baseV = verts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                D3 prev = pts[Math.Max(0, i - 1)], next = pts[Math.Min(pts.Count - 1, i + 1)];
                D3 side = D3.Cross(pts[i].Normalized, (next - prev).Normalized).Normalized;
                Vector3 p, plo, sv = side.ToVector3();
                Wgs84.Split(pts[i], out p, out plo);
                verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(-1, width)); cols.Add(col);
                verts.Add(p); lows.Add(plo); norms.Add(sv); uvs.Add(new Vector2(1, width)); cols.Add(col);
                if (i > 0)
                {
                    int a = baseV + (i - 1) * 2;
                    tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                    tris.Add(a + 1); tris.Add(a + 3); tris.Add(a + 2);
                }
            }
        }

        public CableLayer(PkgTeleLayer d)
        {
            _d = d;
            // One static ribbon mesh for every system, the way the borders draw (which always
            // showed); emphasis comes from a second, small mesh of the highlighted systems and a
            // tint on the base, so no mesh is rewritten while the app runs.
            var verts = new List<Vector3>(); var lows = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var c in d.cables)
            {
                Color col = Hex.Color(c.color, 0.9f);
                var pick = new List<Vector3>();
                if (c.lines != null)
                    foreach (var ln in c.lines)
                    {
                        if (ln.lonlat == null || ln.lonlat.Length < 4) continue;
                        for (int i = 0; i < ln.lonlat.Length / 2; i++) pick.Add(Wgs84.ToEcef(ln.lonlat[i * 2], ln.lonlat[i * 2 + 1], 0).ToVector3());
                        AddRibbon(ln.lonlat, col, 1f, verts, lows, norms, uvs, cols, tris);
                    }
                _pickPts.Add(pick.ToArray());
            }
            _mesh = MakeMesh("TeleGeography cables", verts, lows, norms, uvs, cols, tris);
            _mat = new Material(Shader.Find("AtlasVR/Ribbon")) { renderQueue = 2431 };   // just after the country borders
            _hiMat = new Material(Shader.Find("AtlasVR/Ribbon")) { renderQueue = 2432 };

            // Landing points: small aquamarine spheres.
            _landRim = new MarkerSet(Meshes.Ball(), true, 4, "AtlasVR/Gem") { sizeWS = 0.0022f, minLiftWS = 0.0013f };
            _landCore = new MarkerSet(Meshes.Disc(8), true, 5);   // unused; kept empty
            var a1 = new List<MarkerDef>();
            foreach (var l in d.landings) a1.Add(new MarkerDef(l.lon, l.lat, 1f, Legend.Aquamarine));
            _landRim.Set(a1); _landCore.Set(new List<MarkerDef>());
        }

        static Mesh MakeMesh(string name, List<Vector3> verts, List<Vector3> lows, List<Vector3> norms, List<Vector2> uvs, List<Color> cols, List<int> tris)
        {
            if (verts.Count == 0) return null;
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.SetUVs(1, lows); m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.0e7f);
            return m;
        }

        Mesh _hiMesh;
        readonly Material _hiMat;
        string _hiKey = "";
        bool _anyHighlight;

        public void SetShow(PkgTeleShow s)
        {
            _show = s;
            if (s == null || !s.on) return;
            var hi = s.highlight ?? new string[0];
            string key = string.Join("|", hi);
            if (key == _hiKey) return;
            _hiKey = key;
            _anyHighlight = hi.Length > 0;
            if (_hiMesh != null) { UnityEngine.Object.Destroy(_hiMesh); _hiMesh = null; }
            if (!_anyHighlight) return;
            var set = new HashSet<string>(hi);
            var verts = new List<Vector3>(); var lows = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var tris = new List<int>();
            foreach (var c in _d.cables)
            {
                if (!set.Contains(c.id) || c.lines == null) continue;
                Color col = Hex.Color(c.color, 1f);
                foreach (var ln in c.lines) AddRibbon(ln.lonlat, col, 2f, verts, lows, norms, uvs, cols, tris);
            }
            _hiMesh = MakeMesh("Highlighted cables", verts, lows, norms, uvs, cols, tris);
        }

        public void Draw(GlobeRig rig)
        {
            bool on = _show != null && _show.on;
            if (on && !LegendFilter.IsOff("tc:cables"))
            {
                _mat.SetFloat("_WidthWS", rig.mode == ViewMode.Flight ? 0.0016f : Mathf.Clamp(0.0011f * Mathf.Pow((float)(2.0e7 / rig.ViewHeight), 0.25f), 0.0009f, 0.003f));
                _mat.SetFloat("_LiftWS", 0.0015f);
                _mat.SetColor("_Tint", new Color(1f, 1f, 1f, _anyHighlight ? 0.2f : 1f));
                var bounds = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
                if (_mesh != null) Graphics.RenderMesh(new RenderParams(_mat) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off }, _mesh, 0, Matrix4x4.identity);
                if (_hiMesh != null)
                {
                    _hiMat.SetFloat("_WidthWS", _mat.GetFloat("_WidthWS"));
                    _hiMat.SetFloat("_LiftWS", 0.0016f);
                    _hiMat.SetColor("_Tint", Color.white);
                    Graphics.RenderMesh(new RenderParams(_hiMat) { worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off }, _hiMesh, 0, Matrix4x4.identity);
                }
            }
            if (on && _landMesh != null && !LegendFilter.IsOff("tc:land"))
            {
                _landMat.SetFloat("_WidthWS", rig.mode == ViewMode.Flight ? 0.0016f : Mathf.Clamp(0.0011f * Mathf.Pow((float)(2.0e7 / rig.ViewHeight), 0.25f), 0.0009f, 0.003f));
                _landMat.SetFloat("_LiftWS", 0.0015f);
                _landMat.SetColor("_Tint", Color.white);
                var lp = new RenderParams(_landMat);
                lp.worldBounds = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
                lp.shadowCastingMode = ShadowCastingMode.Off;
                Graphics.RenderMesh(lp, _landMesh, 0, Matrix4x4.identity);
            }
            _landRim.visible = _landCore.visible = on && _show.landings && !LegendFilter.IsOff("tc:landings");
            LandingsVisible = _landRim.visible;
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
        readonly IconSet _icons;
        readonly LabelPool _labels;
        readonly List<PkgSite> _shownDc = new List<PkgSite>();
        readonly List<PkgPlant> _shownPw = new List<PkgPlant>();
        static readonly Color DcColor = Hex.Color("#4dabf7"), Highlight = Hex.Color("#ffd43b");

        public List<PkgSite> ShownDataCenters { get { return _shownDc; } }
        public List<PkgPlant> ShownPlants { get { return _shownPw; } }
        /// Ids that already carry a site label (proximity labels skip them).
        public readonly HashSet<string> Labeled = new HashSet<string>();
        /// Category gates from the filter row; highlighted ids of a gated category stay hidden.
        public bool dataCentersOn = true, plantsOn = true;

        public SitesLayer(PkgSites d, Transform parent)
        {
            _d = d;
            _icons = new IconSet();   // the story's icons: server for data centers, the fuel's glyph for plants
            _labels = new LabelPool(parent, "Site labels") { heightWS = 0.0062f, liftWS = 0.009f };
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
            _shownDc.Clear(); _shownPw.Clear(); _labels.Clear(); Labeled.Clear();
            var hl = new HashSet<string>(show != null && show.highlight != null ? show.highlight : new string[0]);
            var lab = new HashSet<string>(show != null && show.labels != null ? show.labels : new string[0]);
            var defs = new List<MarkerDef>(); var cells = new List<int>();
            if (show != null && _d != null)
            {
                if (dataCentersOn) foreach (var s in _d.datacenters)
                {
                    if (!Matches(show.dc, s.id, s.region, s.kind) && !hl.Contains(s.id)) continue;
                    _shownDc.Add(s);
                    bool h = hl.Contains(s.id);
                    // The web deck's sizing: 24 px plus up to 26 more with capacity; highlighted ones larger.
                    float k = (24f + Mathf.Min(26f, Mathf.Sqrt(Mathf.Max(0f, (float)s.mw)) * 0.45f)) / 32f * (h ? 1.35f : 1f);
                    defs.Add(new MarkerDef(s.lon, s.lat, k, Color.white)); cells.Add(IconSet.Cell(s.kind == "planned" ? "planned" : "dc"));
                    if (lab.Contains(s.id) || h) { _labels.Add(s.name, s.lon, s.lat, Color.white); Labeled.Add(s.id); }
                }
                if (plantsOn) foreach (var p in _d.plants)
                {
                    if (!Matches(show.power, p.id, p.fuel, null) && !hl.Contains(p.id)) continue;
                    if (!string.IsNullOrEmpty(p.fuel) && LegendFilter.IsOff("fuel:" + p.fuel)) continue;
                    _shownPw.Add(p);
                    bool h = hl.Contains(p.id);
                    float k = (24f + Mathf.Min(22f, Mathf.Sqrt(Mathf.Max(0f, (float)p.mw)) * 0.35f)) / 32f * (h ? 1.35f : 1f);
                    defs.Add(new MarkerDef(p.lon, p.lat, k, Color.white)); cells.Add(IconSet.Cell(p.fuel));
                    if (lab.Contains(p.id) || h) { _labels.Add(p.name, p.lon, p.lat, Color.white); Labeled.Add(p.id); }
                }
            }
            _icons.Set(defs, cells);
            _icons.visible = defs.Count > 0;
            _labels.visible = true;
        }

        public void Draw(GlobeRig rig, Transform eye)
        {
            _icons.Draw(rig);
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
