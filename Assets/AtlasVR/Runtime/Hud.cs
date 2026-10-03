// The heads-up display: panels around the edges of the view, the center left clear.
//   Top       filter checkboxes, each with its sub-menu; panel toggles; altitude
//   Left      chapter, headline, subtitle, description, with the substory buttons beneath
//   Right     stat boxes, and below them the pointer info (what the laser is on, or the selection)
//   Bottom    Back at the lower left, Next at the lower right, the story's progress and slider
//             between them with the stories menu; the sources just above
// The frame follows the head's heading lazily, so turning the head looks across the panels
// and turning the body brings them along. A hides and shows it all; nothing is lost.
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class Hud
    {
        public readonly Transform root;
        readonly AtlasPackage _pkg;
        readonly Filters _filters;
        readonly MonoBehaviour _host;
        float _yaw;
        bool _snap = true, _following;

        // Panels.
        readonly Canvas _top, _left, _right, _info, _bottom, _sources;
        public Canvas creditsCanvas;
        readonly TextMeshProUGUI _chapter, _title, _subtitle, _body, _infoText, _sourcesText, _status, _storyLine, _progressLabel;
        readonly ScrollRect _bodyScroll, _sourcesScroll;
        readonly RectTransform _stats, _explore, _stories;
        readonly Image _image, _progressFill;
        readonly Slider _slider;
        readonly Dictionary<string, Button> _boxes = new Dictionary<string, Button>();
        readonly Dictionary<string, string> _labels = new Dictionary<string, string>();
        readonly List<GameObject> _menus = new List<GameObject>();
        bool _suppress;
        PkgSlide _slide;

        public event Action Next, Back;
        public event Action<string> Explore, OpenTrack;
        public event Action<int> Scrub;
        public event Action<PkgStat> StatPressed;

        const float Dist = 1.5f;
        static readonly string Box = "■", Unbox = "□", Down = "▼";

        public Hud(AtlasPackage pkg, Filters filters, Camera cam, MonoBehaviour host)
        {
            _pkg = pkg; _filters = filters; _host = host;
            root = new GameObject("Heads-up display").transform;

            // Top: filters.
            _top = Panel("Filters", cam, new Vector2(1180, 64), 0f, 23f);
            var row = UI.Rect("Row", _top.transform); UI.Stretch(row, 8, 8, 8, 8);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            foreach (var c in filters.categories) AddFilter(row, cam, c);
            AddPanelsMenu(row, cam);
            var stretch = UI.Rect("Spacer", row); stretch.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            _status = UI.Label("Status", row, "", 15, UI.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            _status.gameObject.AddComponent<LayoutElement>().preferredWidth = 190;

            // Left: the slide.
            _left = Panel("Story", cam, new Vector2(560, 640), -41f, 2f);
            _chapter = UI.Label("Chapter", _left.transform, "", 17, UI.Muted, FontStyles.UpperCase | FontStyles.Bold); UI.Place(_chapter.rectTransform, 24, 18, 512, 26);
            _title = UI.Label("Title", _left.transform, "", 36, UI.Text, FontStyles.Bold); UI.Place(_title.rectTransform, 24, 44, 400, 96);
            _title.enableAutoSizing = true; _title.fontSizeMin = 24; _title.fontSizeMax = 36;
            _image = UI.Box("Image", _left.transform, Color.white); UI.Place(_image.rectTransform, 436, 44, 100, 100);
            _image.preserveAspect = true; _image.gameObject.SetActive(false);
            _subtitle = UI.Label("Subtitle", _left.transform, "", 20, UI.Muted); UI.Place(_subtitle.rectTransform, 24, 144, 512, 54);
            var bodyRoot = UI.Rect("Body", _left.transform); UI.Place(bodyRoot, 24, 204, 512, 350);
            var content = UI.Scroll("Scroll", bodyRoot, out _bodyScroll); UI.Stretch((RectTransform)_bodyScroll.transform);
            _body = UI.Label("Text", content, "", 21, UI.Text); _body.lineSpacing = 6;
            _body.gameObject.AddComponent<LayoutElement>();
            _explore = UI.Rect("Explore", _left.transform); UI.Place(_explore, 24, 566, 512, 54);
            var he = _explore.gameObject.AddComponent<HorizontalLayoutGroup>();
            he.spacing = 8; he.childControlWidth = true; he.childControlHeight = true; he.childForceExpandWidth = false;

            // Right: stats, then the pointer info.
            _right = Panel("Stats", cam, new Vector2(380, 330), 41f, 12f);
            _stats = UI.Rect("Boxes", _right.transform); UI.Stretch(_stats, 14, 14, 14, 14);
            var v = _stats.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 10; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandHeight = true;
            _info = Panel("Pointer info", cam, new Vector2(380, 300), 41f, -13f);
            var infoContent = UI.Scroll("Scroll", _info.transform, out ScrollRect infoScroll); UI.Stretch((RectTransform)infoScroll.transform, 16, 16, 14, 14);
            _infoText = UI.Label("Text", infoContent, "", 18, UI.Text); _infoText.gameObject.AddComponent<LayoutElement>();

            // Bottom: navigation.
            _bottom = Panel("Navigation", cam, new Vector2(1100, 96), 0f, -27f);
            var back = UI.Btn("Back", _bottom.transform, "◄  Back", 22, () => { if (Back != null) Back(); }); UI.Place((RectTransform)back.transform, 12, 12, 150, 72);
            var next = UI.Btn("Next", _bottom.transform, "Next  ►", 22, () => { if (Next != null) Next(); }, new Color(0.15f, 0.3f, 0.42f, 1f)); UI.Place((RectTransform)next.transform, 938, 12, 150, 72);
            var storiesBtn = UI.Btn("Stories", _bottom.transform, "Stories " + "▲", 17, () => ToggleMenu(_stories.gameObject)); UI.Place((RectTransform)storiesBtn.transform, 176, 12, 130, 34);
            _storyLine = UI.Label("Story", _bottom.transform, "", 17, UI.Muted, FontStyles.Bold); UI.Place(_storyLine.rectTransform, 316, 14, 520, 30);
            _progressLabel = UI.Label("Count", _bottom.transform, "", 16, UI.Muted, FontStyles.Normal, TextAlignmentOptions.TopRight); UI.Place(_progressLabel.rectTransform, 836, 14, 92, 30);
            var track = UI.Box("Progress", _bottom.transform, new Color(0.12f, 0.18f, 0.26f, 1f)); UI.Place(track.rectTransform, 176, 54, 752, 8);
            _progressFill = UI.Box("Fill", track.transform, UI.Accent);
            _progressFill.rectTransform.anchorMin = Vector2.zero; _progressFill.rectTransform.anchorMax = new Vector2(0, 1);
            _progressFill.rectTransform.offsetMin = Vector2.zero; _progressFill.rectTransform.offsetMax = Vector2.zero;
            _slider = UI.Slider("Slider", _bottom.transform, 1, val => { if (!_suppress && Scrub != null) Scrub((int)val); }); UI.Place((RectTransform)_slider.transform, 176, 62, 752, 28);

            _stories = UI.Rect("Stories list", _bottom.transform);
            _stories.anchorMin = _stories.anchorMax = new Vector2(0, 1); _stories.pivot = new Vector2(0, 0);
            _stories.anchoredPosition = new Vector2(176, 4); _stories.sizeDelta = new Vector2(560, 240);
            _stories.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.09f, 0.98f);
            var g = _stories.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(176, 40); g.spacing = new Vector2(8, 8); g.padding = new RectOffset(8, 8, 8, 8);
            if (pkg.deck != null && pkg.deck.tracks != null)
                foreach (var t in pkg.deck.tracks)
                {
                    string id = t.id;
                    UI.Btn("Track " + id, _stories, t.id == "main" ? "Main story" : t.title, 15, () => { _stories.gameObject.SetActive(false); if (OpenTrack != null) OpenTrack(id); });
                }
            Overlay(_stories.gameObject);
            _stories.gameObject.SetActive(false);
            _menus.Add(_stories.gameObject);

            // Sources, just above the navigation.
            _sources = Panel("Sources", cam, new Vector2(1100, 118), 0f, -19.5f);
            var srcContent = UI.Scroll("Scroll", _sources.transform, out _sourcesScroll); UI.Stretch((RectTransform)_sourcesScroll.transform, 16, 16, 8, 8);
            _sourcesText = UI.Label("Text", srcContent, "", 14, UI.Muted); _sourcesText.gameObject.AddComponent<LayoutElement>();

            filters.Changed += Refresh;
            Refresh();
        }

        Canvas Panel(string name, Camera cam, Vector2 size, float yaw, float pitch)
        {
            var c = UI.WorldCanvas(name, root, size, cam);
            var bg = UI.Box("Background", c.transform, UI.Panel); UI.Stretch(bg.rectTransform);
            var edge = UI.Box("Accent", c.transform, UI.Accent);
            edge.rectTransform.anchorMin = new Vector2(0, 1); edge.rectTransform.anchorMax = new Vector2(1, 1);
            edge.rectTransform.pivot = new Vector2(0.5f, 1); edge.rectTransform.sizeDelta = new Vector2(0, 3); edge.rectTransform.anchoredPosition = Vector2.zero;
            Place(c.transform, yaw, pitch);
            return c;
        }

        public void PlaceCredits(Canvas credits)
        {
            creditsCanvas = credits;
            credits.transform.SetParent(root, false);
            Place(credits.transform, 0f, -32.5f);
        }

        static void Place(Transform t, float yaw, float pitch)
        {
            Quaternion q = Quaternion.Euler(-pitch, yaw, 0);
            t.localPosition = q * Vector3.forward * Dist;
            t.localRotation = Quaternion.LookRotation(t.localPosition, q * Vector3.up);
            t.localScale = Vector3.one * 0.001f * Dist;
        }

        // ------------------------------------------------------------ filters
        void AddFilter(RectTransform row, Camera cam, FilterItem item)
        {
            var group = UI.Rect(item.label, row);
            var hl = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 1; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false;
            var b = UI.Btn(item.key, group, "", 15, () => _filters.Toggle(item.key), new Color(0.07f, 0.12f, 0.19f, 1f));
            b.gameObject.AddComponent<LayoutElement>().preferredWidth = 22 + item.label.Length * 8.6f;
            _boxes[item.key] = b; _labels[item.key] = item.label;
            if (item.children.Count == 0) return;
            var drop = UI.Btn(item.key + " menu", group, Down, 11, null, new Color(0.07f, 0.12f, 0.19f, 1f));
            drop.gameObject.AddComponent<LayoutElement>().preferredWidth = 24;
            var menu = SubMenu(item.key + " sub-menu", group, item.children);
            drop.onClick.AddListener(() => ToggleMenu(menu));
        }

        void AddPanelsMenu(RectTransform row, Camera cam)
        {
            var group = UI.Rect("Panels", row);
            var hl = group.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false;
            var b = UI.Btn("Panels", group, "Panels " + Down, 15, null, new Color(0.12f, 0.1f, 0.2f, 1f));
            b.gameObject.AddComponent<LayoutElement>().preferredWidth = 90;
            var menu = SubMenu("Panels sub-menu", group, _filters.panels);
            b.onClick.AddListener(() => ToggleMenu(menu));
        }

        GameObject SubMenu(string name, RectTransform under, List<FilterItem> items)
        {
            var m = UI.Rect(name, under);
            var le = m.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            m.anchorMin = m.anchorMax = new Vector2(0, 0); m.pivot = new Vector2(0, 1);
            m.anchoredPosition = new Vector2(0, -6);
            m.sizeDelta = new Vector2(250, items.Count * 38 + 12);
            m.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.05f, 0.09f, 0.98f);
            var v = m.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(6, 6, 6, 6); v.spacing = 4; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandHeight = false;
            foreach (var it in items)
            {
                var key = it.key;
                var b = UI.Btn(key, m, "", 15, () => _filters.Toggle(key), new Color(0.07f, 0.12f, 0.19f, 1f));
                b.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
                var t = b.GetComponentInChildren<TextMeshProUGUI>(); t.alignment = TextAlignmentOptions.MidlineLeft;
                _boxes[key] = b; _labels[key] = it.label;
            }
            Overlay(m.gameObject);
            m.gameObject.SetActive(false);
            _menus.Add(m.gameObject);
            return m.gameObject;
        }

        // A drop-down draws over its neighbours and takes its own clicks.
        static void Overlay(GameObject go)
        {
            var c = go.AddComponent<Canvas>();
            c.overrideSorting = true;
            c.sortingOrder = 40;
            go.AddComponent<GraphicRaycaster>();
            var xri = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (xri != null) go.AddComponent(xri);
            UI.RegisterHitRect((RectTransform)go.transform);
        }

        void ToggleMenu(GameObject m)
        {
            bool open = !m.activeSelf;
            foreach (var x in _menus) x.SetActive(false);
            m.SetActive(open);
        }

        public void CloseMenus() { foreach (var x in _menus) x.SetActive(false); }

        void Refresh()
        {
            foreach (var kv in _boxes)
            {
                bool on = _filters.On(kv.Key);
                UI.SetText(kv.Value, (on ? "<color=#F03E9E>" + Box + "</color> " : "<color=#5C6B7A>" + Unbox + "</color> ") + _labels[kv.Key]);
            }
            _left.gameObject.SetActive(_filters.On("panel.story"));
            _right.gameObject.SetActive(_filters.On("panel.stats") && _slide != null && _slide.stats != null && _slide.stats.Length > 0);
            _info.gameObject.SetActive(_filters.On("panel.info"));
            _sources.gameObject.SetActive(_filters.On("panel.sources"));
        }

        // ------------------------------------------------------------ the slide
        public void Show(PkgSlide s, string chapterLine, Func<string, string> trackTitle)
        {
            _slide = s;
            _chapter.text = chapterLine ?? "";
            _title.text = s.title ?? "";
            _subtitle.text = s.subtitle ?? "";
            var sb = new StringBuilder();
            if (s.segments != null && s.segments.Length > 0)
                foreach (var seg in s.segments)
                {
                    string t = Esc(seg.text);
                    sb.Append(string.IsNullOrEmpty(seg.claim) ? t : "<color=#9BE9F4>" + t + "</color><voffset=0.5em><size=60%><color=#F03E9E>●</color></size></voffset>");
                }
            else sb.Append(Esc(s.body));
            _body.text = sb.ToString();
            _bodyScroll.verticalNormalizedPosition = 1f;

            UI.Clear(_stats);
            int n = s.stats != null ? Math.Min(3, s.stats.Length) : 0;
            for (int i = 0; i < n; i++)
            {
                var st = s.stats[i];
                var b = UI.Btn("Stat " + i, _stats, "", 18, null, new Color(0.07f, 0.13f, 0.2f, 1f));
                var lbl = b.GetComponentInChildren<TextMeshProUGUI>();
                lbl.textWrappingMode = TextWrappingModes.Normal;
                lbl.text = "<size=36><b>" + Esc(st.value) + "</b></size>\n<size=17><color=#A6B6C8>" + Esc(st.label) + "</color></size>";
                var captured = st;
                b.onClick.AddListener(() => { if (StatPressed != null) StatPressed(captured); ShowSources(captured.value + " " + captured.label, captured.refs); });
            }

            UI.Clear(_explore);
            if (s.explore != null)
                foreach (var id in s.explore)
                {
                    string tid = id;
                    var b = UI.Btn("Explore " + id, _explore, "Explore " + trackTitle(id), 16, () => { if (Explore != null) Explore(tid); }, new Color(0.22f, 0.12f, 0.24f, 1f));
                    b.gameObject.AddComponent<LayoutElement>().preferredWidth = 165;
                }

            _image.gameObject.SetActive(false);
            if (s.image != null && !string.IsNullOrEmpty(s.image.src) && _host != null)
                _host.StartCoroutine(_pkg.LoadSprite(s.image.src, sp => { if (sp != null && _slide == s) { _image.sprite = sp; _image.gameObject.SetActive(true); } }));

            ShowSlideSources();
            Refresh();
        }

        void ShowSlideSources()
        {
            if (_slide == null) return;
            var sb = new StringBuilder();
            var seen = new HashSet<string>();
            AppendRefs(sb, seen, _slide.claims, null);
            if (_slide.stats != null) foreach (var st in _slide.stats) AppendList(sb, seen, st.refs);
            _sourcesText.text = sb.Length > 0 ? "<b>Sources</b>   " + sb.ToString() : "";
            _sourcesScroll.verticalNormalizedPosition = 1f;
        }

        void AppendRefs(StringBuilder sb, HashSet<string> seen, PkgClaim[] claims, string _)
        {
            if (claims == null) return;
            foreach (var c in claims) AppendList(sb, seen, c.refs);
        }

        void AppendList(StringBuilder sb, HashSet<string> seen, string[] refs)
        {
            if (refs == null) return;
            foreach (var id in refs)
            {
                if (!seen.Add(id)) continue;
                var r = _pkg.Ref(id);
                if (r == null) continue;
                if (sb.Length > 0) sb.Append("   ");
                sb.Append("<color=#F03E9E>●</color> ").Append(Esc(r.note));
            }
        }

        public void ShowSources(string heading, string[] refs)
        {
            var sb = new StringBuilder();
            AppendList(sb, new HashSet<string>(), refs);
            _sourcesText.text = "<b>" + Esc(heading) + "</b>   " + sb.ToString();
            _sourcesScroll.verticalNormalizedPosition = 1f;
        }

        public void ResetSources() { ShowSlideSources(); }

        public void SetPosition(string storyTitle, int index, int count)
        {
            _suppress = true;
            _slider.maxValue = Mathf.Max(1, count - 1);
            _slider.value = index;
            _suppress = false;
            _storyLine.text = Esc(storyTitle);
            _progressLabel.text = (index + 1) + " / " + count;
            float f = count <= 1 ? 1f : (float)index / (count - 1);
            _progressFill.rectTransform.anchorMax = new Vector2(f, 1);
        }

        public void SetStatus(string s) { _status.text = s; }

        // ------------------------------------------------------------ pointer info
        public void ShowInfo(PickInfo hover, PickInfo selected)
        {
            var sb = new StringBuilder();
            var p = selected ?? hover;
            if (p == null)
            {
                _infoText.text = "<color=#7A8A9C>Point the laser at a marker. Trigger selects it; B flies there.</color>";
                return;
            }
            sb.Append(selected != null ? "<size=13><color=#F03E9E>SELECTED</color></size>\n" : "<size=13><color=#7A8A9C>POINTING AT</color></size>\n");
            sb.Append("<size=24><b>").Append(Esc(p.title)).Append("</b></size>\n");
            if (!string.IsNullOrEmpty(p.subtitle)) sb.Append("<color=#A6B6C8>").Append(Esc(p.subtitle)).Append("</color>\n");
            foreach (var r in p.rows) sb.Append("<color=#A6B6C8>").Append(Esc(r.Key)).Append("</color>  ").Append(Esc(r.Value)).Append('\n');
            if (p.list.Count > 0) foreach (var l in p.list) sb.Append("<color=#F03E9E>●</color> ").Append(Esc(l)).Append('\n');
            if (p.refs != null && p.refs.Length > 0)
            {
                sb.Append("<size=14><color=#7A8A9C>").Append(p.refs.Length == 1 ? "Source: " : "Sources: ");
                for (int i = 0; i < p.refs.Length; i++)
                {
                    var r = _pkg.Ref(p.refs[i]);
                    if (r == null) continue;
                    if (i > 0) sb.Append("; ");
                    sb.Append(Esc(r.note));
                }
                sb.Append("</color></size>");
            }
            if (selected != null) sb.Append("\n<size=13><color=#7A8A9C>B flies there. Trigger on empty space clears.</color></size>");
            _infoText.text = sb.ToString();
        }

        // ------------------------------------------------------------ placement
        public bool Visible { get { return root.gameObject.activeSelf; } set { root.gameObject.SetActive(value); if (!value) CloseMenus(); } }

        public void Recenter() { _snap = true; _following = false; }

        /// Follow the head: position always, heading only once it turns past the dead zone.
        public void Follow(Transform eye)
        {
            if (eye == null) return;
            root.position = eye.position;
            Vector3 f = eye.forward; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) return;
            float headYaw = Quaternion.LookRotation(f.normalized, Vector3.up).eulerAngles.y;
            float d = Mathf.DeltaAngle(_yaw, headYaw);
            if (_snap) { _yaw = headYaw; _snap = false; }
            else
            {
                // Past the dead zone, ease all the way back to straight ahead.
                if (Mathf.Abs(d) > 32f) _following = true;
                else if (Mathf.Abs(d) < 2f) _following = false;
                if (_following) _yaw += d * Mathf.Clamp01(Time.deltaTime * 3f);
            }
            root.rotation = Quaternion.Euler(0, _yaw, 0);
        }

        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return "<noparse>" + s.Replace("</noparse>", "") + "</noparse>";
        }
    }
}
