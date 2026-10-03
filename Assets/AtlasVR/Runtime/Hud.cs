// The heads-up display. Panels sit around the edges of the view and the center stays clear.
//   Look up        the deck line, the slide's title and subtitle
//   Top of view    story navigation: the stories menu, where you are, altitude (small)
//   Left           the story text with its Explore links; the stat boxes beneath it
//   Upper right    filters: a checkbox list with full labels; sub-filters expand in place
//   Lower right    data on the selected marker (hidden when nothing is selected)
//   Bottom of view Back, the progress bar and slider, Next
//   Look down      the sources, then the imagery credits below them
// The frame follows the head's heading lazily: turn the head up to 60 degrees to read across
// the panels; turn further and it comes along. A hides and shows it all; nothing is lost.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
        readonly Camera _cam;
        float _yaw;
        bool _snap = true, _following;

        readonly Canvas _location;
        readonly TextMeshProUGUI _locationText;
        string _locationShown;
        readonly Canvas _titleBand, _nav, _left, _stats, _filtersPanel, _info, _bottom, _sources, _loading;
        public Canvas creditsCanvas;
        readonly TextMeshProUGUI _deckLine, _title, _subtitle, _chapter, _body, _infoText, _sourcesText, _status, _storyLine, _progressLabel, _loadingText;
        readonly ScrollRect _bodyScroll, _sourcesScroll;
        readonly RectTransform _statRow, _explore, _stories, _filterList;
        readonly Image _image, _progressFill, _loadingFill;
        readonly Slider _slider;
        readonly Dictionary<string, Image> _checks = new Dictionary<string, Image>();
        readonly Dictionary<string, GameObject> _subRows = new Dictionary<string, GameObject>();   // sub-filter row -> parent key
        readonly Dictionary<string, string> _parentOf = new Dictionary<string, string>();
        readonly Dictionary<string, TextMeshProUGUI> _arrows = new Dictionary<string, TextMeshProUGUI>();
        readonly HashSet<string> _expanded = new HashSet<string>();
        readonly List<GameObject> _menus = new List<GameObject>();
        bool _suppress, _hasSelection;
        int _loadingShown = -1;
        PkgSlide _slide;

        public event Action Next, Back;
        public event Action<string> Explore, OpenTrack;
        public event Action<int> Scrub;
        public event Action<PkgStat> StatPressed;

        const float Dist = 1.5f, RowH = 32f, FilterW = 340f;
        public float followDeadZone = 60f;
        static readonly Color CheckOn = UI.Accent, CheckOff = new Color(0.1f, 0.15f, 0.22f, 1f);
        static readonly Regex Url = new Regex(@"\s*(https?://|www\.)\S+", RegexOptions.Compiled);

        public Hud(AtlasPackage pkg, Filters filters, Camera cam, MonoBehaviour host)
        {
            _pkg = pkg; _filters = filters; _host = host; _cam = cam;
            root = new GameObject("Heads-up display").transform;

            // Look up: the deck, the slide's title and subtitle.
            _titleBand = Panel("Title", new Vector2(1200, 196), 0f, 37f);
            _deckLine = UI.Label("Deck", _titleBand.transform, "", 16, UI.Muted, FontStyles.UpperCase | FontStyles.Bold, TextAlignmentOptions.Top);
            UI.Place(_deckLine.rectTransform, 30, 14, 1140, 24);
            if (pkg.deck != null) _deckLine.text = Esc(pkg.deck.title) + (string.IsNullOrEmpty(pkg.deck.byline) ? "" : "   ·   " + Esc(pkg.deck.byline));
            _title = UI.Label("Title", _titleBand.transform, "", 44, UI.Text, FontStyles.Bold, TextAlignmentOptions.Top);
            UI.Place(_title.rectTransform, 30, 42, 1140, 62);
            _title.enableAutoSizing = true; _title.fontSizeMin = 28; _title.fontSizeMax = 44;
            _subtitle = UI.Label("Subtitle", _titleBand.transform, "", 23, UI.Muted, FontStyles.Normal, TextAlignmentOptions.Top);
            UI.Place(_subtitle.rectTransform, 40, 112, 1120, 72);
            _subtitle.enableAutoSizing = true; _subtitle.fontSizeMin = 17; _subtitle.fontSizeMax = 23;

            // Top of the view: story navigation, small.
            _nav = Panel("Story navigation", new Vector2(920, 54), 0f, 21.5f);
            var storiesBtn = UI.Btn("Stories", _nav.transform, "Stories  ▼", 15, () => ToggleMenu(_stories.gameObject));
            UI.Place((RectTransform)storiesBtn.transform, 10, 9, 130, 36);
            _storyLine = UI.Label("Story", _nav.transform, "", 15, UI.Text, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            UI.Place(_storyLine.rectTransform, 154, 9, 470, 36);
            _storyLine.textWrappingMode = TextWrappingModes.NoWrap; _storyLine.overflowMode = TextOverflowModes.Ellipsis;
            _progressLabel = UI.Label("Count", _nav.transform, "", 15, UI.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            UI.Place(_progressLabel.rectTransform, 628, 9, 90, 36);
            _status = UI.Label("Status", _nav.transform, "", 14, UI.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            UI.Place(_status.rectTransform, 726, 9, 184, 36);

            _stories = UI.Rect("Stories list", _nav.transform);
            _stories.anchorMin = _stories.anchorMax = new Vector2(0, 0); _stories.pivot = new Vector2(0, 1);
            _stories.anchoredPosition = new Vector2(10, -6);
            int tracks = pkg.deck != null && pkg.deck.tracks != null ? pkg.deck.tracks.Length : 0;
            _stories.sizeDelta = new Vector2(560, Mathf.CeilToInt(tracks / 3f) * 44 + 16);
            _stories.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.065f, 0.1f, 0.98f);
            var g = _stories.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(176, 38); g.spacing = new Vector2(6, 6); g.padding = new RectOffset(8, 8, 8, 8);
            if (tracks > 0)
                foreach (var t in pkg.deck.tracks)
                {
                    string id = t.id;
                    UI.Btn("Track " + id, _stories, t.id == "main" ? "Main story" : t.title, 14, () => { _stories.gameObject.SetActive(false); if (OpenTrack != null) OpenTrack(id); });
                }
            Overlay(_stories.gameObject);
            _stories.gameObject.SetActive(false);
            _menus.Add(_stories.gameObject);

            // Left: the story, then the stats beneath it.
            _left = Panel("Story", new Vector2(540, 560), -46f, 5f);
            _chapter = UI.Label("Chapter", _left.transform, "", 16, UI.Muted, FontStyles.UpperCase | FontStyles.Bold); UI.Place(_chapter.rectTransform, 24, 18, 380, 44);
            _image = UI.Box("Image", _left.transform, Color.white); UI.Place(_image.rectTransform, 416, 16, 100, 100);
            _image.preserveAspect = true; _image.gameObject.SetActive(false);
            var bodyRoot = UI.Rect("Body", _left.transform); UI.Place(bodyRoot, 24, 70, 492, 410);
            var content = UI.Scroll("Scroll", bodyRoot, out _bodyScroll); UI.Stretch((RectTransform)_bodyScroll.transform);
            _body = UI.Label("Text", content, "", 21, UI.Text); _body.lineSpacing = 6;
            _body.gameObject.AddComponent<LayoutElement>();
            _explore = UI.Rect("Explore", _left.transform); UI.Place(_explore, 24, 494, 492, 48);
            var he = _explore.gameObject.AddComponent<HorizontalLayoutGroup>();
            he.spacing = 8; he.childControlWidth = true; he.childControlHeight = true; he.childForceExpandWidth = false;

            _stats = Panel("Stats", new Vector2(540, 150), -46f, -18f);
            _statRow = UI.Rect("Boxes", _stats.transform); UI.Stretch(_statRow, 14, 14, 14, 14);
            var hs = _statRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hs.spacing = 10; hs.childControlWidth = true; hs.childControlHeight = true; hs.childForceExpandWidth = true; hs.childForceExpandHeight = true;

            // Upper right: the filters, a checkbox list.
            _filtersPanel = Panel("Filters", new Vector2(FilterW, 200), 40f, 26f, true);
            _filterList = UI.Rect("List", _filtersPanel.transform); UI.Stretch(_filterList, 12, 12, 12, 12);
            var vl = _filterList.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 2; vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            var head = UI.Label("Heading", _filterList, "Show on the globe", 15, UI.Muted, FontStyles.UpperCase | FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            foreach (var c in filters.categories) AddFilter(c);
            AddFilter(new FilterItem("panels", "Panels") { children = filters.panels }, false);

            // Lower right: the selection's data.
            _info = Panel("Selection", new Vector2(390, 330), 46f, -25f);
            var infoContent = UI.Scroll("Scroll", _info.transform, out ScrollRect infoScroll); UI.Stretch((RectTransform)infoScroll.transform, 18, 18, 16, 16);
            _infoText = UI.Label("Text", infoContent, "", 18, UI.Text); _infoText.gameObject.AddComponent<LayoutElement>();

            // Bottom of the view: Back, progress, Next.
            _bottom = Panel("Navigation", new Vector2(1000, 84), 0f, -25f);
            var back = UI.Btn("Back", _bottom.transform, "◄  Back", 21, () => { if (Back != null) Back(); }); UI.Place((RectTransform)back.transform, 12, 12, 150, 60);
            var next = UI.Btn("Next", _bottom.transform, "Next  ►", 21, () => { if (Next != null) Next(); }, new Color(0.15f, 0.3f, 0.42f, 1f)); UI.Place((RectTransform)next.transform, 838, 12, 150, 60);
            var track = UI.Box("Progress", _bottom.transform, new Color(0.12f, 0.18f, 0.26f, 1f)); UI.Place(track.rectTransform, 182, 18, 636, 8);
            _progressFill = UI.Box("Fill", track.transform, UI.Accent);
            _progressFill.rectTransform.anchorMin = Vector2.zero; _progressFill.rectTransform.anchorMax = new Vector2(0, 1);
            _progressFill.rectTransform.offsetMin = Vector2.zero; _progressFill.rectTransform.offsetMax = Vector2.zero;
            _slider = UI.Slider("Slider", _bottom.transform, 1, val => { if (!_suppress && Scrub != null) Scrub((int)val); }); UI.Place((RectTransform)_slider.transform, 182, 34, 636, 34);

            // Look down: the sources (plain text, no links), then the credits.
            _sources = Panel("Sources", new Vector2(1000, 96), 0f, -35.5f);
            var srcContent = UI.Scroll("Scroll", _sources.transform, out _sourcesScroll); UI.Stretch((RectTransform)_sourcesScroll.transform, 18, 18, 10, 10);
            _sourcesText = UI.Label("Text", srcContent, "", 14, UI.Muted); _sourcesText.gameObject.AddComponent<LayoutElement>();

            // Upper right, above the filters: the place under you and its coordinates.
            // Same width and center as the filter panel, so the right-aligned text ends at its right edge;
            // a long name runs out to the left.
            _location = UI.WorldCanvas("Location", root, new Vector2(FilterW, 58), _cam);
            ((RectTransform)_location.transform).pivot = new Vector2(0.5f, 0f);
            _locationText = UI.Label("Text", _location.transform, "", 17, UI.Text, FontStyles.Bold, TextAlignmentOptions.BottomRight);
            UI.Stretch(_locationText.rectTransform, 0, 4, 0, 0);
            _locationText.textWrappingMode = TextWrappingModes.NoWrap; _locationText.overflowMode = TextOverflowModes.Overflow;
            Place(_location.transform, 40f, 27f);
            _location.gameObject.SetActive(false);

            // While the Earth loads.
            _loading = Panel("Loading", new Vector2(520, 70), 0f, 8f);
            _loadingText = UI.Label("Text", _loading.transform, "Loading the Earth", 20, UI.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            UI.Place(_loadingText.rectTransform, 20, 10, 480, 32);
            var lt = UI.Box("Bar", _loading.transform, new Color(0.12f, 0.18f, 0.26f, 1f)); UI.Place(lt.rectTransform, 40, 48, 440, 8);
            _loadingFill = UI.Box("Fill", lt.transform, new Color(0.35f, 0.75f, 0.85f, 1f));
            _loadingFill.rectTransform.anchorMin = Vector2.zero; _loadingFill.rectTransform.anchorMax = new Vector2(0, 1);
            _loadingFill.rectTransform.offsetMin = Vector2.zero; _loadingFill.rectTransform.offsetMax = Vector2.zero;
            _loading.gameObject.SetActive(false);

            filters.Changed += Refresh;
            Refresh();
        }

        Canvas Panel(string name, Vector2 size, float yaw, float pitch, bool topPivot = false)
        {
            var c = UI.WorldCanvas(name, root, size, _cam);
            if (topPivot) ((RectTransform)c.transform).pivot = new Vector2(0.5f, 1f);
            var bg = UI.Box("Background", c.transform, UI.Panel); UI.Stretch(bg.rectTransform);
            var edge = UI.Box("Accent", c.transform, UI.Accent);
            edge.rectTransform.anchorMin = new Vector2(0, 1); edge.rectTransform.anchorMax = new Vector2(1, 1);
            edge.rectTransform.pivot = new Vector2(0.5f, 1); edge.rectTransform.sizeDelta = new Vector2(0, 3); edge.rectTransform.anchoredPosition = Vector2.zero;
            UI.Slab(c);
            Place(c.transform, yaw, pitch);
            return c;
        }

        public void PlaceCredits(Canvas credits)
        {
            creditsCanvas = credits;
            credits.transform.SetParent(root, false);
            UI.Slab(credits);
            Place(credits.transform, 0f, -44f);
        }

        static void Place(Transform t, float yaw, float pitch)
        {
            Quaternion q = Quaternion.Euler(-pitch, yaw, 0);
            t.localPosition = q * Vector3.forward * Dist;
            t.localRotation = Quaternion.LookRotation(t.localPosition, q * Vector3.up);
            t.localScale = Vector3.one * 0.001f * Dist;
        }

        // ------------------------------------------------------------ filters
        void AddFilter(FilterItem item, bool checkable = true)
        {
            AddRow(item.key, item.label, 0, checkable, item.children.Count > 0);
            foreach (var c in item.children)
            {
                var row = AddRow(c.key, c.label, 30, true, false);
                _subRows[c.key] = row;
                _parentOf[c.key] = item.key;
                row.SetActive(false);
            }
        }

        GameObject AddRow(string key, string label, float indent, bool checkable, bool expandable)
        {
            var row = UI.Rect("Row " + key, _filterList);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = indent > 0 ? RowH - 4 : RowH;
            // The whole row is the hit target: checking a box, or for a heading without one, expanding.
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = indent > 0 ? new Color(1, 1, 1, 0.02f) : new Color(1, 1, 1, 0.045f);
            var b = row.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(2.6f, 2.6f, 2.6f, 1f); colors.colorMultiplier = 2.6f; b.colors = colors;
            b.targetGraphic = hit;
            float x = 8 + indent;
            if (checkable)
            {
                var box = UI.Box("Box", row, CheckOff);
                box.raycastTarget = false;
                box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0, 0.5f);
                box.rectTransform.pivot = new Vector2(0, 0.5f);
                box.rectTransform.sizeDelta = new Vector2(20, 20); box.rectTransform.anchoredPosition = new Vector2(x, 0);
                var ol = box.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.55f, 0.65f, 0.78f, 0.9f); ol.effectDistance = new Vector2(1.5f, -1.5f);
                var tick = UI.Box("Tick", box.transform, Color.white);
                tick.raycastTarget = false;
                UI.Stretch(tick.rectTransform, 5, 5, 5, 5);
                _checks[key] = box;
                x += 32;
            }
            var t = UI.Label("Label", row, label, indent > 0 ? 16 : 18, indent > 0 ? UI.Muted : UI.Text, indent > 0 ? FontStyles.Normal : FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = new Vector2(0, 0); t.rectTransform.anchorMax = new Vector2(1, 1);
            t.rectTransform.offsetMin = new Vector2(x, 0); t.rectTransform.offsetMax = new Vector2(expandable ? -44 : -8, 0);
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;

            if (expandable)
            {
                var ex = UI.Btn("Expand", row, "", 15, () => ToggleExpand(key), new Color(0.1f, 0.16f, 0.24f, 1f));
                var rt = (RectTransform)ex.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f); rt.pivot = new Vector2(1, 0.5f);
                rt.sizeDelta = new Vector2(34, 28); rt.anchoredPosition = new Vector2(-4, 0);
                _arrows[key] = ex.GetComponentInChildren<TextMeshProUGUI>();
            }
            if (checkable) b.onClick.AddListener(() => _filters.Toggle(key));
            else if (expandable) b.onClick.AddListener(() => ToggleExpand(key));
            return row.gameObject;
        }

        void ToggleExpand(string key)
        {
            // One group open at a time keeps the list clear of the selection panel below it.
            bool open = !_expanded.Contains(key);
            _expanded.Clear();
            if (open) _expanded.Add(key);
            LayoutFilters();
        }

        void LayoutFilters()
        {
            int rows = 0, subs = 0;
            foreach (var kv in _subRows)
            {
                bool show = _expanded.Contains(_parentOf[kv.Key]);
                if (kv.Value.activeSelf != show) kv.Value.SetActive(show);
                if (show) subs++;
            }
            foreach (var c in _filters.categories) rows++;
            rows++; // Panels
            foreach (var kv in _arrows) kv.Value.text = _expanded.Contains(kv.Key) ? "▼" : "◄";
            float h = 24 + 28 + rows * (RowH + 2) + subs * (RowH - 2);
            var rt = (RectTransform)_filtersPanel.transform;
            if (Mathf.Abs(rt.sizeDelta.y - h) > 0.5f)
            {
                rt.sizeDelta = new Vector2(FilterW, h);
                UI.Slab(_filtersPanel);
            }
        }

        void ToggleMenu(GameObject m)
        {
            bool open = !m.activeSelf;
            foreach (var x in _menus) x.SetActive(false);
            m.SetActive(open);
        }

        public void CloseMenus() { foreach (var x in _menus) x.SetActive(false); }

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

        void Refresh()
        {
            foreach (var kv in _checks)
            {
                bool on = _filters.On(kv.Key);
                kv.Value.color = on ? CheckOn : CheckOff;
                var tick = kv.Value.transform.Find("Tick");
                if (tick != null) tick.gameObject.SetActive(on);
            }
            LayoutFilters();
            bool story = _filters.On("panel.story");
            _left.gameObject.SetActive(story);
            _titleBand.gameObject.SetActive(story);
            _stats.gameObject.SetActive(_filters.On("panel.stats") && _slide != null && _slide.stats != null && _slide.stats.Length > 0);
            _info.gameObject.SetActive(_filters.On("panel.info") && _hasSelection);
            _sources.gameObject.SetActive(_filters.On("panel.sources") && _sourcesText.text.Length > 0);
        }

        // ------------------------------------------------------------ the slide
        public void Show(PkgSlide s, string chapterLine, Func<string, string> trackTitle)
        {
            _slide = s;
            _chapter.text = Esc(chapterLine);
            _title.text = Esc(s.title);
            _subtitle.text = Esc(s.subtitle);
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

            UI.Clear(_statRow);
            int n = s.stats != null ? Math.Min(3, s.stats.Length) : 0;
            for (int i = 0; i < n; i++)
            {
                var st = s.stats[i];
                var b = UI.Btn("Stat " + i, _statRow, "", 18, null, new Color(0.08f, 0.15f, 0.23f, 1f));
                var lbl = b.GetComponentInChildren<TextMeshProUGUI>();
                lbl.textWrappingMode = TextWrappingModes.Normal;
                lbl.overflowMode = TextOverflowModes.Truncate;
                lbl.text = "<size=32><b>" + Esc(st.value) + "</b></size>\n<size=15><color=#A6B6C8>" + Esc(st.label) + "</color></size>";
                var captured = st;
                b.onClick.AddListener(() => { if (StatPressed != null) StatPressed(captured); ShowSources(captured.value + " " + captured.label, captured.refs); });
            }

            UI.Clear(_explore);
            if (s.explore != null)
                foreach (var id in s.explore)
                {
                    string tid = id;
                    var b = UI.Btn("Explore " + id, _explore, "Explore " + trackTitle(id), 16, () => { if (Explore != null) Explore(tid); }, new Color(0.22f, 0.12f, 0.24f, 1f));
                    b.gameObject.AddComponent<LayoutElement>().preferredWidth = 160;
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
            if (_slide.claims != null) foreach (var c in _slide.claims) AppendList(sb, seen, c.refs);
            if (_slide.stats != null) foreach (var st in _slide.stats) AppendList(sb, seen, st.refs);
            _sourcesText.text = sb.Length > 0 ? "<b>Sources</b>   " + sb.ToString() : "";
            _sourcesScroll.verticalNormalizedPosition = 1f;
            _sources.gameObject.SetActive(_filters.On("panel.sources") && sb.Length > 0);
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
                sb.Append("<color=#F03E9E>●</color> ").Append(Esc(Plain(r.note)));
            }
        }

        /// A citation as plain text: no web addresses.
        static string Plain(string note)
        {
            if (string.IsNullOrEmpty(note)) return "";
            return Url.Replace(note, "").Trim();
        }

        public void ShowSources(string heading, string[] refs)
        {
            var sb = new StringBuilder();
            AppendList(sb, new HashSet<string>(), refs);
            _sourcesText.text = "<b>" + Esc(heading) + "</b>   " + sb.ToString();
            _sourcesScroll.verticalNormalizedPosition = 1f;
            _sources.gameObject.SetActive(_filters.On("panel.sources"));
        }

        public void ResetSources() { ShowSlideSources(); }

        public void SetPosition(string storyTitle, int index, int count)
        {
            _suppress = true;
            _slider.maxValue = Mathf.Max(1, count - 1);
            _slider.value = index;
            _suppress = false;
            _storyLine.text = Esc(storyTitle);
            _progressLabel.text = (index + 1) + " of " + count;
            float f = count <= 1 ? 1f : (float)index / (count - 1);
            _progressFill.rectTransform.anchorMax = new Vector2(f, 1);
        }

        public void SetStatus(string s) { if (_status.text != s) _status.text = s; }

        /// The place under you (small) with its coordinates beneath (smaller); null hides it.
        public void SetLocation(string place, string coords)
        {
            bool on = place != null;
            if (_location.gameObject.activeSelf != on) _location.gameObject.SetActive(on);
            if (!on) return;
            string s = Esc(place) + "\n<size=13><color=#A6B6C8>" + Esc(coords) + "</color></size>";
            if (s != _locationShown) { _locationText.text = s; _locationShown = s; }
        }

        /// Shows the loading card with a percentage, or hides it (pass a negative value).
        public void SetLoading(float percent)
        {
            bool on = percent >= 0f;
            if (_loading.gameObject.activeSelf != on) _loading.gameObject.SetActive(on);
            if (!on) return;
            int pct = Mathf.RoundToInt(percent);
            if (pct == _loadingShown) return;
            _loadingShown = pct;
            _loadingText.text = "Loading the Earth   " + pct + "%";
            _loadingFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(percent / 100f), 1);
        }

        // ------------------------------------------------------------ selection data
        /// The data panel shows only while something is selected.
        public void ShowInfo(PickInfo hover, PickInfo selected)
        {
            _hasSelection = selected != null;
            _info.gameObject.SetActive(_filters.On("panel.info") && _hasSelection);
            if (selected == null) return;
            var p = selected;
            var sb = new StringBuilder();
            sb.Append("<size=13><color=#F03E9E>SELECTED</color></size>\n");
            sb.Append("<size=24><b>").Append(Esc(p.title)).Append("</b></size>\n");
            if (!string.IsNullOrEmpty(p.subtitle)) sb.Append("<color=#A6B6C8>").Append(Esc(p.subtitle)).Append("</color>\n");
            foreach (var r in p.rows) sb.Append("<color=#A6B6C8>").Append(Esc(r.Key)).Append("</color>  ").Append(Esc(r.Value)).Append('\n');
            if (p.list.Count > 0) foreach (var l in p.list) sb.Append("<color=#F03E9E>●</color> ").Append(Esc(l)).Append('\n');
            if (p.refs != null && p.refs.Length > 0)
            {
                sb.Append("<size=14><color=#7A8A9C>").Append(p.refs.Length == 1 ? "Source: " : "Sources: ");
                bool first = true;
                foreach (var id in p.refs)
                {
                    var r = _pkg.Ref(id);
                    if (r == null) continue;
                    if (!first) sb.Append("; ");
                    sb.Append(Esc(Plain(r.note)));
                    first = false;
                }
                sb.Append("</color></size>");
            }
            sb.Append("\n<size=13><color=#7A8A9C>B flies there. Trigger on empty space clears.</color></size>");
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
                if (Mathf.Abs(d) > followDeadZone) _following = true;
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
