// The slide station (title, stats, body, sources, navigation), the marker card the laser
// opens, and the sources view every claim and stat reaches.
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class StationPanel
    {
        public readonly Canvas canvas;
        readonly AtlasPackage _pkg;
        readonly TextMeshProUGUI _eyebrow, _title, _subtitle, _body;
        readonly RectTransform _stats, _bar, _bodyContent, _sourcesContent;
        readonly ScrollRect _bodyScroll, _sourcesScroll;
        readonly Image _image;
        readonly GameObject _sourcesView, _bodyView;
        readonly Button _sourcesBtn;
        readonly List<Button> _explore = new List<Button>();
        PkgSlide _slide;
        MonoBehaviour _host;

        public event Action Next, Back;
        public event Action<string> Explore;
        public event Action<PkgStat> StatPressed;

        const float W = 1000, H = 780;

        public StationPanel(AtlasPackage pkg, Transform parent, Camera cam, MonoBehaviour host)
        {
            _pkg = pkg; _host = host;
            canvas = UI.WorldCanvas("Slide station", parent, new Vector2(W, H), cam);
            var bg = UI.Box("Background", canvas.transform, UI.Panel); UI.Stretch(bg.rectTransform);
            var edge = UI.Box("Accent", canvas.transform, UI.Accent); UI.Place(edge.rectTransform, 0, 0, W, 6);

            _eyebrow = UI.Label("Chapter", canvas.transform, "", 24, UI.Muted, FontStyles.UpperCase | FontStyles.Bold); UI.Place(_eyebrow.rectTransform, 40, 26, 720, 34);
            _title = UI.Label("Title", canvas.transform, "", 52, UI.Text, FontStyles.Bold); UI.Place(_title.rectTransform, 40, 60, 720, 130);
            _title.enableAutoSizing = true; _title.fontSizeMin = 34; _title.fontSizeMax = 52;
            _subtitle = UI.Label("Subtitle", canvas.transform, "", 28, UI.Muted); UI.Place(_subtitle.rectTransform, 40, 190, 720, 44);
            _image = UI.Box("Image", canvas.transform, Color.white); UI.Place(_image.rectTransform, 790, 30, 180, 180);
            _image.preserveAspect = true; _image.gameObject.SetActive(false);

            _stats = UI.Rect("Stats", canvas.transform); UI.Place(_stats, 40, 244, 920, 112);
            var h = _stats.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true; h.childForceExpandHeight = true;

            _bodyView = UI.Rect("Body view", canvas.transform).gameObject; UI.Place((RectTransform)_bodyView.transform, 40, 372, 920, 290);
            _bodyContent = UI.Scroll("Body", _bodyView.transform, out _bodyScroll); UI.Stretch((RectTransform)_bodyScroll.transform);
            _body = UI.Label("Text", _bodyContent, "", 29, UI.Text);
            _body.lineSpacing = 8;
            _body.gameObject.AddComponent<LayoutElement>();

            _sourcesView = UI.Rect("Sources view", canvas.transform).gameObject; UI.Place((RectTransform)_sourcesView.transform, 40, 244, 920, 418);
            _sourcesContent = UI.Scroll("Sources", _sourcesView.transform, out _sourcesScroll); UI.Stretch((RectTransform)_sourcesScroll.transform);
            _sourcesView.SetActive(false);

            _bar = UI.Rect("Navigation", canvas.transform); UI.Place(_bar, 40, 680, 920, 72);
            var hb = _bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            hb.spacing = 12; hb.childControlWidth = true; hb.childControlHeight = true; hb.childForceExpandWidth = false; hb.childForceExpandHeight = true;
            AddBar(UI.Btn("Back", _bar, "Back", 28, () => { if (Back != null) Back(); }), 150);
            _sourcesBtn = UI.Btn("Sources", _bar, "Sources", 28, ToggleSources);
            AddBar(_sourcesBtn, 190);
            var spacer = UI.Rect("Explore", _bar); spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var he = spacer.gameObject.AddComponent<HorizontalLayoutGroup>();
            he.spacing = 10; he.childControlWidth = true; he.childControlHeight = true; he.childForceExpandWidth = false; he.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < 3; i++)
            {
                var b = UI.Btn("Explore " + i, spacer, "", 24, null, new Color(0.22f, 0.12f, 0.24f, 1f));
                b.gameObject.AddComponent<LayoutElement>().preferredWidth = 170;
                b.gameObject.SetActive(false);
                _explore.Add(b);
            }
            AddBar(UI.Btn("Next", _bar, "Next", 28, () => { if (Next != null) Next(); }, new Color(0.15f, 0.3f, 0.42f, 1f)), 170);
        }

        static void AddBar(Button b, float w) { var le = b.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.minWidth = w; }

        public void Show(PkgSlide s, string chapterLine, Func<string, string> trackTitle)
        {
            _slide = s;
            _sourcesView.SetActive(false); _bodyView.SetActive(true); _stats.gameObject.SetActive(true);
            UI.SetText(_sourcesBtn, "Sources");
            _eyebrow.text = chapterLine ?? "";
            _title.text = s.title ?? "";
            _subtitle.text = s.subtitle ?? "";
            var sb = new StringBuilder();
            if (s.segments != null && s.segments.Length > 0)
            {
                foreach (var g in s.segments)
                {
                    string t = Escape(g.text);
                    sb.Append(string.IsNullOrEmpty(g.claim) ? t : "<color=#9BE9F4>" + t + "</color><voffset=0.5em><size=60%><color=#F03E9E>●</color></size></voffset>");
                }
            }
            else sb.Append(Escape(s.body));
            _body.text = sb.ToString();
            _bodyScroll.verticalNormalizedPosition = 1f;

            UI.Clear(_stats);
            int n = s.stats != null ? Math.Min(3, s.stats.Length) : 0;
            _stats.gameObject.SetActive(n > 0);
            for (int i = 0; i < n; i++)
            {
                var st = s.stats[i];
                var b = UI.Btn("Stat " + i, _stats, "", 20, null, new Color(0.07f, 0.13f, 0.2f, 1f));
                var lbl = b.GetComponentInChildren<TextMeshProUGUI>();
                lbl.textWrappingMode = TextWrappingModes.Normal;
                lbl.text = "<size=44><b>" + Escape(st.value) + "</b></size>\n<size=21><color=#A6B6C8>" + Escape(st.label) + "</color></size>";
                var captured = st;
                b.onClick.AddListener(() => { if (StatPressed != null) StatPressed(captured); ShowSources(captured.refs, captured.value + " " + captured.label); });
            }

            for (int i = 0; i < _explore.Count; i++)
            {
                bool on = s.explore != null && i < s.explore.Length;
                _explore[i].gameObject.SetActive(on);
                if (!on) continue;
                string id = s.explore[i];
                UI.SetText(_explore[i], "Explore " + trackTitle(id));
                _explore[i].onClick.RemoveAllListeners();
                _explore[i].onClick.AddListener(() => { if (Explore != null) Explore(id); });
            }

            _image.gameObject.SetActive(false);
            if (s.image != null && !string.IsNullOrEmpty(s.image.src) && _host != null)
                _host.StartCoroutine(_pkg.LoadSprite(s.image.src, sp => { if (sp != null && _slide == s) { _image.sprite = sp; _image.gameObject.SetActive(true); } }));
        }

        void ToggleSources()
        {
            if (_sourcesView.activeSelf) { _sourcesView.SetActive(false); _bodyView.SetActive(true); _stats.gameObject.SetActive(_slide != null && _slide.stats != null && _slide.stats.Length > 0); UI.SetText(_sourcesBtn, "Sources"); return; }
            if (_slide == null) return;
            UI.Clear(_sourcesContent);
            if (_slide.claims != null)
                foreach (var c in _slide.claims) AddSourceBlock(_sourcesContent, c.text, c.refs);
            if (_slide.stats != null)
                foreach (var st in _slide.stats) AddSourceBlock(_sourcesContent, st.value + " " + st.label, st.refs);
            OpenSourcesView();
        }

        public void ShowSources(string[] refs, string heading)
        {
            UI.Clear(_sourcesContent);
            AddSourceBlock(_sourcesContent, heading, refs);
            OpenSourcesView();
        }

        void OpenSourcesView()
        {
            _bodyView.SetActive(false); _stats.gameObject.SetActive(false); _sourcesView.SetActive(true);
            _sourcesScroll.verticalNormalizedPosition = 1f;
            UI.SetText(_sourcesBtn, "Close sources");
        }

        void AddSourceBlock(Transform parent, string heading, string[] refs)
        {
            if (refs == null || refs.Length == 0) return;
            var t = UI.Label("Claim", parent, Escape(heading), 25, UI.Claim, FontStyles.Normal);
            t.gameObject.AddComponent<LayoutElement>();
            var s = UI.Label("Refs", parent, Notes(_pkg, refs), 21, UI.Muted);
            s.gameObject.AddComponent<LayoutElement>();
        }

        public static string Notes(AtlasPackage pkg, string[] refs)
        {
            var sb = new StringBuilder();
            sb.Append(refs.Length == 1 ? "<b>Source</b>\n" : "<b>Sources (" + refs.Length + ")</b>\n");
            foreach (var id in refs)
            {
                var r = pkg.Ref(id);
                if (r == null) continue;
                sb.Append(Escape(r.note));
                if (!string.IsNullOrEmpty(r.url)) sb.Append(" <color=#6C8FB0>").Append(Escape(r.url)).Append("</color>");
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";
        }
    }

    /// The card the laser opens on a marker. It stays on its marker as the world moves.
    public class MarkerCard
    {
        public readonly Canvas canvas;
        readonly AtlasPackage _pkg;
        readonly TextMeshProUGUI _text;
        readonly RectTransform _root;
        PickInfo _info;
        public PickInfo Info { get { return _info; } }
        public bool IsOpen { get { return canvas.gameObject.activeSelf; } }
        public event Action<PickInfo> GoThere;

        public MarkerCard(AtlasPackage pkg, Transform parent, Camera cam)
        {
            _pkg = pkg;
            canvas = UI.WorldCanvas("Marker card", parent, new Vector2(560, 520), cam);
            _root = (RectTransform)canvas.transform;
            var bg = UI.Box("Background", canvas.transform, UI.Panel); UI.Stretch(bg.rectTransform);
            var edge = UI.Box("Accent", canvas.transform, UI.Accent); UI.Place(edge.rectTransform, 0, 0, 560, 5);
            var content = UI.Scroll("Card", canvas.transform, out ScrollRect sr);
            UI.Stretch((RectTransform)sr.transform, 24, 24, 20, 86);
            _text = UI.Label("Text", content, "", 24, UI.Text);
            _text.gameObject.AddComponent<LayoutElement>();
            var close = UI.Btn("Close", canvas.transform, "Close", 24, Close); UI.Place((RectTransform)close.transform, 24, 446, 220, 58);
            var go = UI.Btn("Go there", canvas.transform, "Go there", 24, () => { if (GoThere != null && _info != null) GoThere(_info); }, new Color(0.15f, 0.3f, 0.42f, 1f));
            UI.Place((RectTransform)go.transform, 316, 446, 220, 58);
            canvas.gameObject.SetActive(false);
        }

        public void Open(PickInfo info)
        {
            _info = info;
            var sb = new StringBuilder();
            sb.Append("<size=34><b>").Append(StationPanel.Escape(info.title)).Append("</b></size>\n");
            if (!string.IsNullOrEmpty(info.subtitle)) sb.Append("<color=#A6B6C8>").Append(StationPanel.Escape(info.subtitle)).Append("</color>\n");
            sb.Append('\n');
            foreach (var r in info.rows) sb.Append("<color=#A6B6C8>").Append(StationPanel.Escape(r.Key)).Append("</color>  ").Append(StationPanel.Escape(r.Value)).Append('\n');
            if (info.list.Count > 0) { sb.Append('\n'); foreach (var l in info.list) sb.Append("<color=#F03E9E>●</color> ").Append(StationPanel.Escape(l)).Append('\n'); }
            if (info.refs != null && info.refs.Length > 0) sb.Append("\n<size=20>").Append(StationPanel.Notes(_pkg, info.refs)).Append("</size>");
            _text.text = sb.ToString();
            canvas.gameObject.SetActive(true);
        }

        public void Close() { _info = null; canvas.gameObject.SetActive(false); }

        /// Keep the card beside its marker, facing the viewer, at a readable distance.
        public void Follow(GlobeRig rig, Transform eye)
        {
            if (!IsOpen || _info == null || eye == null) return;
            Vector3 p = rig.WorldOf(_info.lon, _info.lat, 0);
            if (_info.lon == 0 && _info.lat == 0) p = eye.position + eye.forward * 0.9f;
            Vector3 dir = p - eye.position;
            float d = dir.magnitude;
            // Far markers: bring the card in to 0.9 m along the line of sight.
            Vector3 pos = d > 1.1f ? eye.position + dir / d * 0.9f : p + Vector3.up * 0.08f;
            pos += Vector3.up * 0.12f + Vector3.Cross(Vector3.up, dir.normalized) * 0.22f;
            _root.position = pos;
            _root.rotation = Quaternion.LookRotation(pos - eye.position, Vector3.up);
            float s = 0.001f * Mathf.Clamp(Vector3.Distance(pos, eye.position) / 0.9f, 0.6f, 1.4f);
            _root.localScale = Vector3.one * s;
        }
    }
}
