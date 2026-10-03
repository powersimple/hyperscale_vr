// The companies on the players and clouds slides.
//   CompanyFocus    the company the legend picks out (pointing at a name, or holding it with a click)
//   FootprintLayer  cloud regions, owned campuses, and colocation sites, each in its company's color
//   LogoLayer       logo cards standing over their places, one size, facing the viewer
//   HoverRelay      pointer enter and exit on a uGUI element, for the legend's hover
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AtlasVR
{
    /// The legend's pick: pointing at a name highlights that company's sites; a click holds it
    /// so the laser can go to its sites; a second click lets it go. A new slide clears it.
    public static class CompanyFocus
    {
        public static string Hover { get; private set; }
        public static string Held { get; private set; }
        public static string Active { get { return !string.IsNullOrEmpty(Held) ? Held : Hover; } }
        public static event Action Changed;

        public static void SetHover(string company)
        {
            if (company == Hover) return;
            string before = Active;
            Hover = company;
            if (Active != before && Changed != null) Changed();
        }

        public static void ToggleHeld(string company)
        {
            string before = Active;
            Held = Held == company ? null : company;
            if (Active != before && Changed != null) Changed();
        }

        public static void Clear()
        {
            if (Hover == null && Held == null) return;
            Hover = null; Held = null;
            if (Changed != null) Changed();
        }

        /// A marker's color under the current focus: full when it is the company in focus (or
        /// nothing is), faint otherwise.
        public static Color Apply(Color c, string company)
        {
            string a = Active;
            if (string.IsNullOrEmpty(a) || a == company) return c;
            return new Color(c.r * 0.5f, c.g * 0.5f, c.b * 0.5f, c.a * 0.28f);
        }
    }

    public class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Action enter, exit;
        public void OnPointerEnter(PointerEventData e) { if (enter != null) enter(); }
        public void OnPointerExit(PointerEventData e) { if (exit != null) exit(); }
        void OnDisable() { if (exit != null) exit(); }
    }

    public class FootprintLayer : IPickable
    {
        readonly PkgFootprints _d;
        readonly Dictionary<string, PkgProvider> _prov = new Dictionary<string, PkgProvider>();
        readonly MarkerSet _gems;
        readonly List<PkgFpSite> _shown = new List<PkgFpSite>();
        readonly List<Color> _base = new List<Color>();
        bool _on;
        string _key = "";

        public FootprintLayer(PkgFootprints d)
        {
            _d = d;
            if (d.providers != null) foreach (var p in d.providers) if (!string.IsNullOrEmpty(p.key)) _prov[p.key] = p;
            _gems = new MarkerSet(Meshes.Ball(), true, 3, "AtlasVR/Gem") { sizeWS = 0.0030f, minLiftWS = 0.0017f };
            CompanyFocus.Changed += Recolor;
        }

        public string CompanyOf(string provider) { PkgProvider p; return _prov.TryGetValue(provider ?? "", out p) ? p.company : ""; }

        /// providers: the slide's list ("all" for every cloud; colocation only when named).
        public void SetShow(bool on, string[] providers)
        {
            _on = on;
            string key = on ? string.Join("|", providers ?? new string[0]) : "";
            if (key == _key) return;
            _key = key;
            _shown.Clear(); _base.Clear();
            var defs = new List<MarkerDef>();
            if (on && _d.sites != null)
            {
                var want = new HashSet<string>(providers ?? new string[0]);
                bool all = want.Count == 0 || want.Contains("all");
                bool colo = want.Contains("colo") || want.Contains("fp_colo");
                foreach (var s in _d.sites)
                {
                    bool isColo = s.provider != null && s.provider.StartsWith("colo_");
                    if (isColo ? !(colo || want.Contains(s.provider)) : !(all || want.Contains(s.provider))) continue;
                    PkgProvider p;
                    Color c = _prov.TryGetValue(s.provider ?? "", out p) ? Hex.Color(p.color, 0.95f) : new Color(0.7f, 0.75f, 0.8f, 0.95f);
                    float k = isColo ? 0.7f : s.kind == "cloud_region" ? 1.15f : 1f;
                    _shown.Add(s); _base.Add(c);
                    defs.Add(new MarkerDef(s.lon, s.lat, k, CompanyFocus.Apply(c, p != null ? p.company : "")));
                }
            }
            _gems.Set(defs);
        }

        void Recolor()
        {
            if (_shown.Count == 0) return;
            for (int i = 0; i < _shown.Count; i++) _gems.SetColor(i, CompanyFocus.Apply(_base[i], CompanyOf(_shown[i].provider)));
            _gems.ApplyColors();
        }

        public void Draw(GlobeRig rig)
        {
            float zoom = Mathf.Clamp((float)(2.0e7 / rig.ViewHeight), 1f, 6f);
            float s = Mathf.Lerp(0.9f, 1.8f, (zoom - 1f) / 5f);
            _gems.sizeWS = 0.0030f * s; _gems.minLiftWS = 0.0017f * s;
            _gems.visible = _on && _shown.Count > 0;
            _gems.Draw(rig);
        }

        public PickInfo Pick(Ray ray, GlobeRig rig, Vector3 eye, float tol, out float best)
        {
            best = tol; PickInfo hit = null;
            if (!_on) return null;
            var g = rig.GlobeToWorld;
            foreach (var s in _shown)
            {
                Vector3 p = g.MultiplyPoint3x4(Wgs84.ToEcef(s.lon, s.lat, 0).ToVector3());
                Vector3 up = g.MultiplyVector(Wgs84.Up(s.lon, s.lat).ToVector3()).normalized;
                if (!rig.Visible(p, up, eye)) continue;
                float a = Vector3.Angle(ray.direction, p - ray.origin);
                if (a >= best) continue;
                best = a;
                PkgProvider pr;
                _prov.TryGetValue(s.provider ?? "", out pr);
                string kind = s.kind == "cloud_region" ? "Cloud region" : s.kind == "colo" ? "Colocation site" : "Data center campus";
                hit = new PickInfo { title = s.name, subtitle = (pr != null ? pr.label + " · " : "") + kind, refs = pr != null && pr.refs != null ? pr.refs : new string[0], lon = s.lon, lat = s.lat };
                string place = string.IsNullOrEmpty(s.city) ? Fmt.Region(s.cc) : s.city + ", " + Fmt.Region(s.cc);
                if (!string.IsNullOrEmpty(place)) hit.rows.Add(new KeyValuePair<string, string>("Place", s.approx ? place + " (placed at the city)" : place));
                if (s.azs > 0) hit.rows.Add(new KeyValuePair<string, string>("Availability zones", s.azs.ToString()));
                if (!string.IsNullOrEmpty(s.status) && s.status != "live") hit.rows.Add(new KeyValuePair<string, string>("Status", char.ToUpper(s.status[0]) + s.status.Substring(1)));
                if (s.year > 0) hit.rows.Add(new KeyValuePair<string, string>("Opened", s.year.ToString()));
            }
            return hit;
        }
    }

    /// The logos the web deck stands over its places (the players slides): white cards with the
    /// mark, or the name where there is no mark, a thin rim in the company's color.
    public class LogoLayer
    {
        readonly Transform _root;
        readonly AtlasPackage _pkg;
        readonly MonoBehaviour _host;
        readonly List<Card> _cards = new List<Card>();
        string _key = "";
        public const float HeightWS = 0.016f, LiftWS = 0.03f;

        class Card { public Transform t; public D3 ecef; public Vector3 up; public string company; public CanvasGroup group; }

        public LogoLayer(AtlasPackage pkg, MonoBehaviour host)
        {
            _pkg = pkg; _host = host;
            _root = new GameObject("Logos over their places").transform;
            CompanyFocus.Changed += Refocus;
        }

        public void SetSlide(PkgSlide s)
        {
            var logos = s != null && s.logos != null ? s.logos : new PkgLogo[0];
            string key = s != null ? s.id : "";
            if (key == _key) return;
            _key = key;
            foreach (var c in _cards) if (c.t != null) UnityEngine.Object.Destroy(c.t.gameObject);
            _cards.Clear();
            foreach (var l in logos) _cards.Add(Make(l));
        }

        Card Make(PkgLogo l)
        {
            var go = new GameObject("Logo " + l.text);
            go.transform.SetParent(_root, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(240, 96);
            var group = go.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            var rim = UI.Box("Rim", go.transform, Hex.Color(l.color, 1f)); UI.Stretch(rim.rectTransform); UI.Round(rim, 18f); rim.raycastTarget = false;
            var card = UI.Box("Card", go.transform, new Color(1f, 1f, 1f, 0.96f)); UI.Stretch(card.rectTransform, 5, 5, 5, 5); UI.Round(card, 14f); card.raycastTarget = false;
            var name = UI.Label("Name", go.transform, Hud.Esc(l.text), 34, new Color(0.04f, 0.07f, 0.13f), FontStyles.Bold, TextAlignmentOptions.Center);
            UI.Stretch(name.rectTransform, 16, 16, 10, 10);
            name.enableAutoSizing = true; name.fontSizeMin = 14; name.fontSizeMax = 38;
            if (!string.IsNullOrEmpty(l.image) && _host != null)
            {
                var mark = UI.Box("Mark", go.transform, Color.white); UI.Stretch(mark.rectTransform, 18, 18, 14, 14);
                mark.preserveAspect = true; mark.raycastTarget = false; mark.gameObject.SetActive(false);
                _host.StartCoroutine(_pkg.LoadSprite(l.image, sp => { if (sp != null && mark != null) { mark.sprite = sp; mark.gameObject.SetActive(true); name.gameObject.SetActive(false); } }));
            }
            go.SetActive(false);
            return new Card { t = go.transform, ecef = Wgs84.ToEcef(l.lon, l.lat, 0), up = Wgs84.Up(l.lon, l.lat).ToVector3(), company = CompanyOfKey(l.key, l.text), group = group };
        }

        static string CompanyOfKey(string key, string text)
        {
            string k = (key ?? "").ToLowerInvariant();
            return k == "aws" ? "amazon" : k == "azure" ? "microsoft" : k == "gcp" || k == "google_dc" ? "google" : k;
        }

        void Refocus()
        {
            foreach (var c in _cards) if (c.group != null) c.group.alpha = string.IsNullOrEmpty(CompanyFocus.Active) || CompanyFocus.Active == c.company ? 1f : 0.3f;
        }

        public void Update(GlobeRig rig, Transform eye, bool show)
        {
            if (rig == null || eye == null) return;
            var g = rig.GlobeToWorld;
            foreach (var c in _cards)
            {
                Vector3 p = g.MultiplyPoint3x4(c.ecef.ToVector3());
                Vector3 up = g.MultiplyVector(c.up).normalized;
                bool vis = show && rig.Visible(p, up, eye.position);
                if (c.t.gameObject.activeSelf != vis) c.t.gameObject.SetActive(vis);
                if (!vis) continue;
                float d = Vector3.Distance(eye.position, p) / 0.8f;
                c.t.position = p + up * (LiftWS * d);
                c.t.rotation = Quaternion.LookRotation(c.t.position - eye.position, Vector3.up);
                c.t.localScale = Vector3.one * (HeightWS * d / 96f);
            }
        }
    }
}
