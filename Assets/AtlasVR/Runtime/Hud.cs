// The heads-up display. Nothing sits in the straight-ahead view.
//   Look up        the deck line, the slide's title and subtitle
//   Left           the story text with its Explore links; the stat boxes beneath it
//   Upper right    filters, which double as the legend (each row's gem sphere or icon)
//   Lower right    data on the selected marker (hidden when nothing is selected)
//   Look down      what the laser points at; then one box with the filter state, the location
//                  line and compass, the stories slider and the slides slider, previous and next
//                  arrows at its sides; below it the sources, the credits, the logo on the floor
// The frame turns with you only past 80 degrees of head turn. A hides and shows it all.
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

        readonly Canvas _titleBand, _left, _stats, _filtersPanel, _info, _bottom, _sources, _loading, _pointing;
        readonly TextMeshProUGUI _locText, _pointText;
        readonly RectTransform _filterRow, _mainNotches;
        readonly Slider _mainSlider;
        public readonly RectTransform compassAnchor;
        string _locShown, _pointShown;
        public Canvas creditsCanvas;
        readonly TextMeshProUGUI _deckLine, _title, _subtitle, _chapter, _body, _infoText, _sourcesText, _storyLine, _loadingText;
        readonly ScrollRect _bodyScroll, _sourcesScroll;
        readonly RectTransform _statRow, _explore, _filterList;
        readonly Image _image, _loadingFill;
        readonly RectTransform _notches;
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
        readonly RectTransform _legendRow;
        readonly TextMeshProUGUI _companyName;
        readonly List<KeyValuePair<string, Image>> _legendEntries = new List<KeyValuePair<string, Image>>();
        bool _hasLegend;
        // The side panels and where they sit normally (yaw, pitch); in orbit they close in beside the Earth.
        readonly List<Spot> _spots = new List<Spot>();
        struct Spot { public Transform t; public float yaw, pitch, halfDeg; public int side; }
        float _orbitBlend;

        public event Action Next, Back;
        public event Action<string> Explore, OpenTrack;
        public event Action<int> Scrub;
        public event Action<PkgStat> StatPressed;

        const float Dist = 1.5f, RowH = 32f, FilterW = 340f;
        // The bottom box: 15% wider than before, and the bars beneath it close up.
        const float BoxW = 1220f, BoxH = 300f, SliderW = 1000f, DegPerUnit = 0.0573f;   // degrees per canvas unit at 1.5 m
        const float BoxPitch = -49f, SourcesPitch = BoxPitch - BoxH * DegPerUnit * 0.5f - 0.6f - 104f * DegPerUnit * 0.5f;
        public const float CreditsPitch = SourcesPitch - 104f * DegPerUnit * 0.5f - 0.6f - 96f * DegPerUnit * 0.5f;
        public float followDeadZone = 80f;   // the side panels sit about 60 degrees out
        static readonly Color CheckOn = UI.Emerald, CheckOff = new Color(0.05f, 0.1f, 0.28f, 0.9f);
        static readonly Regex Url = new Regex(@"\s*(https?://|www\.)\S+", RegexOptions.Compiled);

        public Hud(AtlasPackage pkg, Filters filters, Camera cam, MonoBehaviour host)
        {
            _pkg = pkg; _filters = filters; _host = host; _cam = cam;
            root = new GameObject("Heads-up display").transform;

            // Look up: the deck, the slide's title and subtitle.
            _titleBand = Panel("Title", new Vector2(960, 196), 0f, 46.5f);
            _deckLine = UI.Label("Deck", _titleBand.transform, "", 16, UI.Muted, FontStyles.UpperCase | FontStyles.Bold, TextAlignmentOptions.Top);
            UI.Place(_deckLine.rectTransform, 30, 14, 900, 24);
            if (pkg.deck != null) _deckLine.text = Esc(pkg.deck.title) + (string.IsNullOrEmpty(pkg.deck.byline) ? "" : "   ·   " + Esc(pkg.deck.byline));
            _title = UI.Label("Title", _titleBand.transform, "", 44, UI.Text, FontStyles.Bold, TextAlignmentOptions.Top);
            UI.Place(_title.rectTransform, 30, 42, 900, 62);
            _title.enableAutoSizing = true; _title.fontSizeMin = 28; _title.fontSizeMax = 44;
            _subtitle = UI.Label("Subtitle", _titleBand.transform, "", 23, UI.Muted, FontStyles.Normal, TextAlignmentOptions.Top);
            UI.Place(_subtitle.rectTransform, 40, 112, 880, 72);
            _subtitle.enableAutoSizing = true; _subtitle.fontSizeMin = 17; _subtitle.fontSizeMax = 23;

            // Left: the story, then the stats beneath it.
            _left = Panel("Story", new Vector2(540, 560), -64f, 1f);
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

            _stats = Panel("Stats", new Vector2(540, 150), -65f, -21.5f);
            _statRow = UI.Rect("Boxes", _stats.transform); UI.Stretch(_statRow, 14, 14, 14, 14);
            var hs = _statRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hs.spacing = 10; hs.childControlWidth = true; hs.childControlHeight = true; hs.childForceExpandWidth = true; hs.childForceExpandHeight = true;

            // Upper right: the filters, a checkbox list.
            _filtersPanel = Panel("Filters", new Vector2(FilterW, 200), 59f, 22f, true);
            _filterList = UI.Rect("List", _filtersPanel.transform); UI.Stretch(_filterList, 22, 22, 18, 18);
            var vl = _filterList.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4; vl.childControlWidth = true; vl.childControlHeight = true; vl.childForceExpandHeight = false; vl.childForceExpandWidth = true;
            var head = UI.Label("Heading", _filterList, "Show on the globe", 15, UI.Muted, FontStyles.UpperCase | FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            head.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;
            foreach (var c in filters.categories) AddFilter(c);
            AddFilter(new FilterItem("panels", "Panels") { children = filters.panels }, false);
            // Clear: every layer that draws data off, until the next slide.
            var clear = UI.Btn("Clear", _filterList, "Clear", 17, () => _filters.ClearAll(), new Color(0.05f, 0.14f, 0.38f, 0.85f));
            clear.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            var clearText = clear.GetComponentInChildren<TextMeshProUGUI>(); if (clearText != null) clearText.color = UI.Gold;

            // Lower right: the selection's data.
            _info = Panel("Selection", new Vector2(390, 330), 64f, -23f);
            var infoContent = UI.Scroll("Scroll", _info.transform, out ScrollRect infoScroll); UI.Stretch((RectTransform)infoScroll.transform, 18, 18, 16, 16);
            _infoText = UI.Label("Text", infoContent, "", 18, UI.Text); _infoText.gameObject.AddComponent<LayoutElement>();

            // Looking down: one box with, from the top, the filter state, the location line (with
            // the compass at its right end), the stories slider, and the slides slider; the
            // previous and next arrows at its sides. Above it, what the laser points at; below it,
            // the sources, then the credits, then the silver logo on the floor.
            _bottom = Panel("Navigation", new Vector2(BoxW, BoxH), 0f, BoxPitch);
            _filterRow = UI.Rect("Filter state", _bottom.transform); UI.Place(_filterRow, 110, 12, BoxW - 220, 30);
            var fr = _filterRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            fr.spacing = 14; fr.childControlWidth = true; fr.childControlHeight = true; fr.childForceExpandWidth = false; fr.childForceExpandHeight = false; fr.childAlignment = TextAnchor.MiddleLeft;
            // The players and clouds legend shares the top row: the company a slide is about at the
            // left, the companies it names at the right, each with its color dot.
            _companyName = UI.Label("Company", _bottom.transform, "", 20, UI.Gold, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            UI.Place(_companyName.rectTransform, 110, 10, 300, 34);
            _companyName.textWrappingMode = TextWrappingModes.NoWrap; _companyName.overflowMode = TextOverflowModes.Ellipsis;
            _legendRow = UI.Rect("Legend", _bottom.transform); UI.Place(_legendRow, 110, 10, BoxW - 220, 34);
            var lr = _legendRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            lr.spacing = 10; lr.childControlWidth = true; lr.childControlHeight = true; lr.childForceExpandWidth = false; lr.childForceExpandHeight = false; lr.childAlignment = TextAnchor.MiddleRight;
            CompanyFocus.Changed += MarkLegend;
            _locText = UI.Label("Location", _bottom.transform, "", 13, UI.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            UI.Place(_locText.rectTransform, 110, 46, BoxW - 330, 26);
            _locText.textWrappingMode = TextWrappingModes.NoWrap; _locText.overflowMode = TextOverflowModes.Ellipsis;
            compassAnchor = UI.Rect("Compass", _bottom.transform); UI.Place(compassAnchor, BoxW - 210, 30, 60, 60);
            compassAnchor.pivot = new Vector2(0.5f, 0.5f); compassAnchor.anchoredPosition = new Vector2(BoxW - 165, -66);
            // Stories.
            _mainNotches = UI.Rect("Story notches", _bottom.transform); UI.Place(_mainNotches, 110, 78, SliderW, 50);
            _mainSlider = UI.Slider("Stories", _bottom.transform, 1, val => { if (!_suppress) PickTrack((int)val); }); UI.Place((RectTransform)_mainSlider.transform, 110, 126, SliderW, 36);
            // Slides of the current story.
            _notches = UI.Rect("Slide notches", _bottom.transform); UI.Place(_notches, 110, 168, SliderW, 72);
            _slider = UI.Slider("Slides", _bottom.transform, 1, val => { if (!_suppress && Scrub != null) Scrub((int)val); }); UI.Place((RectTransform)_slider.transform, 110, 238, SliderW, 36);
            _storyLine = UI.Label("Position", _bottom.transform, "", 12, UI.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineRight);
            UI.Place(_storyLine.rectTransform, 110, 276, SliderW, 20);
            // The arrows are the buttons: 3D gold prisms, no boxes.
            Arrow(_bottom, 55, BoxH * 0.55f, false, () => { if (Back != null) Back(); });
            Arrow(_bottom, BoxW - 55, BoxH * 0.55f, true, () => { if (Next != null) Next(); });
            if (pkg.deck != null && pkg.deck.tracks != null) BuildStoryNotches(pkg.deck.tracks);

            // What the laser points at, just above the box.
            _pointing = UI.WorldCanvas("Pointing at", root, new Vector2(BoxW, 40), _cam);
            UI.UnregisterHitRect((RectTransform)_pointing.transform);   // the laser passes through it
            _pointText = UI.Label("Text", _pointing.transform, "", 16, UI.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            UI.Stretch(_pointText.rectTransform);
            _pointText.textWrappingMode = TextWrappingModes.NoWrap; _pointText.overflowMode = TextOverflowModes.Ellipsis;
            Place(_pointing.transform, 0f, BoxPitch + BoxH * DegPerUnit * 0.5f + 1.6f);
            _pointing.gameObject.SetActive(false);

            // The sources (plain text, no links), right below the box.
            _sources = Panel("Sources", new Vector2(BoxW, 104), 0f, SourcesPitch);
            var srcContent = UI.Scroll("Scroll", _sources.transform, out _sourcesScroll); UI.Stretch((RectTransform)_sourcesScroll.transform, 20, 20, 12, 12);
            _sourcesText = UI.Label("Text", srcContent, "", 14, UI.Muted); _sourcesText.gameObject.AddComponent<LayoutElement>();

            // While the Earth loads.
            _loading = Panel("Loading", new Vector2(520, 70), 0f, 8f);
            _loadingText = UI.Label("Text", _loading.transform, "Loading the Earth", 20, UI.Text, FontStyles.Bold, TextAlignmentOptions.Center);
            UI.Place(_loadingText.rectTransform, 20, 10, 480, 32);
            var lt = UI.Box("Bar", _loading.transform, new Color(0.3f, 0.45f, 0.6f, 0.3f)); UI.Place(lt.rectTransform, 40, 48, 440, 6); UI.Round(lt, 3f);
            _loadingFill = UI.Box("Fill", lt.transform, new Color(0.4f, 0.8f, 0.95f, 0.95f)); UI.Round(_loadingFill, 3f);
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
            var bg = UI.Box("Background", c.transform, UI.Panel); UI.Stretch(bg.rectTransform); UI.Round(bg, 22f);
            // A thin glowing line along the top, inset from the rounded corners.
            var edge = UI.Box("Accent", c.transform, new Color(UI.Accent.r, UI.Accent.g, UI.Accent.b, 0.85f));
            edge.rectTransform.anchorMin = new Vector2(0, 1); edge.rectTransform.anchorMax = new Vector2(1, 1);
            edge.rectTransform.pivot = new Vector2(0.5f, 1); edge.rectTransform.sizeDelta = new Vector2(-56, 2); edge.rectTransform.anchoredPosition = new Vector2(0, -2);
            UI.Round(edge, 1f);
            UI.Slab(c);
            Place(c.transform, yaw, pitch);
            if (Mathf.Abs(yaw) > 30f) _spots.Add(new Spot { t = c.transform, yaw = yaw, pitch = pitch, halfDeg = size.x * DegPerUnit * 0.5f, side = yaw < 0 ? -1 : 1 });
            return c;
        }

        public void PlaceCredits(Canvas credits)
        {
            creditsCanvas = credits;
            credits.transform.SetParent(root, false);
            UI.Slab(credits);
            Place(credits.transform, 0f, CreditsPitch);
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
            // Clear glass behind the label; a soft tint only while the laser is on the row.
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = Color.white;
            UI.Round(hit, 8f);
            var b = row.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(0.35f, 0.55f, 1f, 0.22f);
            colors.pressedColor = new Color(0.35f, 0.55f, 1f, 0.35f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            colors.colorMultiplier = 1f;
            b.colors = colors;
            b.targetGraphic = hit;
            float x = 8 + indent;
            if (checkable)
            {
                var box = UI.Box("Box", row, CheckOff);
                box.raycastTarget = false;
                box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0, 0.5f);
                box.rectTransform.pivot = new Vector2(0, 0.5f);
                box.rectTransform.sizeDelta = new Vector2(20, 20); box.rectTransform.anchoredPosition = new Vector2(x, 0);
                UI.Round(box, 5f);
                var ol = box.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(0.45f, 0.65f, 1f, 0.8f); ol.effectDistance = new Vector2(1f, -1f);
                var tick = UI.Box("Tick", box.transform, Color.white);
                tick.raycastTarget = false;
                UI.Round(tick, 3f);
                UI.Stretch(tick.rectTransform, 5, 5, 5, 5);
                _checks[key] = box;
                x += 32;
            }
            // The story's icon for the category, as on the globe.
            // The legend: how the category looks on the globe (its gem sphere or its icon).
            float sz = indent > 0 ? 20f : 24f;
            var sw = Legend.Swatch(key, row, sz);
            if (sw != null)
            {
                sw.anchorMin = sw.anchorMax = new Vector2(0, 0.5f); sw.pivot = new Vector2(0, 0.5f);
                sw.anchoredPosition = new Vector2(x, 0);
                x += sz + 6f;
            }
            var t = UI.Label("Label", row, label, indent > 0 ? 16 : 18, indent > 0 ? UI.Muted : UI.Text, indent > 0 ? FontStyles.Normal : FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            t.rectTransform.anchorMin = new Vector2(0, 0); t.rectTransform.anchorMax = new Vector2(1, 1);
            t.rectTransform.offsetMin = new Vector2(x, 0); t.rectTransform.offsetMax = new Vector2(expandable ? -44 : -8, 0);
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;

            if (expandable)
            {
                var ex = UI.Btn("Expand", row, "", 15, () => ToggleExpand(key), new Color(0.1f, 0.3f, 0.82f, 0.5f));
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
            float h = 36 + 28 + rows * (RowH + 4) + subs * RowH + 48;   // padding, heading, rows, Clear
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
            // The filter state in the bottom box: what is on, with its legend swatch.
            UI.Clear(_filterRow);
            _filterRow.gameObject.SetActive(!_hasLegend);
            foreach (var c in _filters.categories)
            {
                if (!_filters.On(c.key) || !Legend.Has(c.key)) continue;
                var chip = UI.Rect("Chip " + c.key, _filterRow);
                var h = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 5; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false; h.childAlignment = TextAnchor.MiddleLeft;
                var sw = Legend.Swatch(c.key, chip, 18f);
                if (sw != null) { var le = sw.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 18; le.preferredHeight = 18; }
                var t = UI.Label("Label", chip, c.label, 12, UI.Muted, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.gameObject.AddComponent<LayoutElement>().preferredWidth = t.preferredWidth + 2;
            }
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
                    sb.Append(string.IsNullOrEmpty(seg.claim) ? t : "<color=#FFD36B>" + t + "</color><voffset=0.5em><size=60%><color=#FF4F7B>●</color></size></voffset>");
                }
            else sb.Append(Esc(s.body));
            _body.text = sb.ToString();
            _bodyScroll.verticalNormalizedPosition = 1f;

            UI.Clear(_statRow);
            int n = s.stats != null ? Math.Min(3, s.stats.Length) : 0;
            for (int i = 0; i < n; i++)
            {
                var st = s.stats[i];
                var b = UI.Btn("Stat " + i, _statRow, "", 18, null, new Color(0.06f, 0.22f, 0.62f, 0.5f));
                var lbl = b.GetComponentInChildren<TextMeshProUGUI>();
                lbl.textWrappingMode = TextWrappingModes.Normal;
                lbl.overflowMode = TextOverflowModes.Truncate;
                lbl.text = "<size=32><b>" + Esc(st.value) + "</b></size>\n<size=15><color=#BCCBFF>" + Esc(st.label) + "</color></size>";
                var captured = st;
                b.onClick.AddListener(() => { if (StatPressed != null) StatPressed(captured); ShowSources(captured.value + " " + captured.label, captured.refs); });
            }

            UI.Clear(_explore);
            if (s.explore != null)
                foreach (var id in s.explore)
                {
                    string tid = id;
                    var b = UI.Btn("Explore " + id, _explore, "Explore " + trackTitle(id), 16, () => { if (Explore != null) Explore(tid); }, new Color(0.05f, 0.14f, 0.38f, 0.85f));
                    b.gameObject.AddComponent<LayoutElement>().preferredWidth = 160;
                    var et = b.GetComponentInChildren<TextMeshProUGUI>(); if (et != null) et.color = UI.Gold;
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
                sb.Append("<color=#FF4F7B>●</color> ").Append(Esc(Plain(r.note)));
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

        PkgTrack[] _tracks;
        string _trackShown, _layoutTrack;

        public void SetPosition(string storyTitle, int index, int count)
        {
            _suppress = true;
            _slider.maxValue = Mathf.Max(1, count - 1);
            _slider.value = index;
            _suppress = false;
            _storyLine.text = Esc(storyTitle) + "   " + (index + 1) + " of " + count;
        }

        /// The stories slider follows the story you are in.
        public void SetTrack(string id)
        {
            if (id == _trackShown || _tracks == null) return;
            _trackShown = id;
            _suppress = true;
            for (int i = 0; i < _tracks.Length; i++) if (_tracks[i].id == id) _mainSlider.value = i;
            _suppress = false;
        }

        void PickTrack(int i)
        {
            if (_tracks == null || i < 0 || i >= _tracks.Length || _tracks[i].id == _trackShown) return;
            if (OpenTrack != null) OpenTrack(_tracks[i].id);
        }

        void BuildStoryNotches(PkgTrack[] tracks)
        {
            _tracks = tracks;
            _mainSlider.maxValue = Mathf.Max(1, tracks.Length - 1);
            var labels = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < tracks.Length; i++) labels.Add(new KeyValuePair<int, string>(i, tracks[i].id == "main" ? "Main story" : tracks[i].title));
            Notches(_mainNotches, tracks.Length, labels, true);
        }

        /// The slides slider: a notch per slide, the chapter names (small) where each chapter starts.
        public void SetTrackLayout(PkgTrack t)
        {
            if (t == null || t.beats == null || t.id == _layoutTrack) return;
            _layoutTrack = t.id;
            int n = t.beats.Length;
            var labels = new List<KeyValuePair<int, string>>();
            for (int i = 0; i < n; i++)
                if (i == 0 || t.beats[i].chapter != t.beats[i - 1].chapter)
                {
                    var ch = _pkg.Chapter(t.beats[i].chapter);
                    labels.Add(new KeyValuePair<int, string>(i, ch != null ? (!string.IsNullOrEmpty(ch.label) ? ch.label : ch.title) : ""));
                }
            Notches(_notches, n, labels, false);
        }

        /// Notches across a slider's travel; labelled notches are taller and gold. Centered labels
        /// sit over their notch (the stories); left-aligned ones run to the next label (chapters).
        void Notches(RectTransform parent, int n, List<KeyValuePair<int, string>> labels, bool centered)
        {
            UI.Clear(parent);
            float w = SliderW, inset = 13f, span = w - inset * 2f;
            Func<int, float> X = i => inset + (n <= 1 ? 0f : span * i / (n - 1));
            var major = new HashSet<int>();
            foreach (var l in labels) major.Add(l.Key);
            for (int i = 0; i < n; i++)
            {
                bool m = major.Contains(i);
                var tick = UI.Box("Notch " + i, parent, m ? new Color(1f, 0.82f, 0.35f, 0.9f) : new Color(0.7f, 0.8f, 1f, 0.45f));
                tick.raycastTarget = false;
                var rt = tick.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(0.5f, 0);
                rt.sizeDelta = new Vector2(m ? 3f : 2f, m ? 16f : 8f); rt.anchoredPosition = new Vector2(X(i), 0);
            }
            for (int k = 0; k < labels.Count; k++)
            {
                int i = labels[k].Key, j = k + 1 < labels.Count ? labels[k + 1].Key : n;
                float x0 = X(i), x1 = j >= n ? w : X(j);
                float width = centered ? Mathf.Max(60f, span / Mathf.Max(1, n - 1) - 4f) : x1 - x0 - 6f;
                if (!centered && width < 34f) continue;   // too narrow to label
                var lbl = UI.Label("Label " + k, parent, Esc(labels[k].Value), 11, UI.Muted, FontStyles.Normal,
                    centered ? TextAlignmentOptions.Bottom : TextAlignmentOptions.BottomLeft);
                var rt = lbl.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0); rt.pivot = new Vector2(centered ? 0.5f : 0f, 0);
                rt.sizeDelta = new Vector2(width, centered ? 30f : 50f); rt.anchoredPosition = new Vector2(centered ? x0 : x0 - 1f, 19f);
                lbl.textWrappingMode = TextWrappingModes.Normal; lbl.overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        /// The location line: region, country, coordinates, heading, altitude.
        public void SetLocation(string line)
        {
            string s = line ?? "";
            if (s != _locShown) { _locText.text = Esc(s); _locShown = s; }
        }

        /// What the laser is on, above the box ("Nuclear power plant, Susquehanna, Pennsylvania, USA").
        public void SetPointing(string line)
        {
            bool on = !string.IsNullOrEmpty(line);
            if (_pointing.gameObject.activeSelf != on) _pointing.gameObject.SetActive(on);
            if (on && line != _pointShown) { _pointText.text = Esc(line); _pointShown = line; }
        }

        // A previous/next button: a gold 3D prism over an invisible hit area.
        void Arrow(Canvas c, float x, float y, bool right, UnityEngine.Events.UnityAction onClick)
        {
            var hit = UI.Box(right ? "Next" : "Back", c.transform, new Color(1, 1, 1, 0.001f));
            var rt = hit.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(90, 110); rt.anchoredPosition = new Vector2(x, -y);
            var b = hit.gameObject.AddComponent<Button>();
            var colors = b.colors; colors.highlightedColor = new Color(1, 1, 1, 30f); b.colors = colors;   // a faint plate on hover
            b.targetGraphic = hit;
            b.onClick.AddListener(onClick);
            var go = new GameObject("Arrow");
            go.transform.SetParent(rt, false);
            go.layer = c.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = _prism ?? (_prism = Meshes.Cylinder(3));
            var mat = new Material(Shader.Find("AtlasVR/Emblem"));
            mat.SetColor("_Color", new Color(1f, 0.8f, 0.35f));
            mat.SetFloat("_Metal", 0.85f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.sortingOrder = 11;   // after the canvas
            // The prism's axis points at you, one corner toward its direction.
            go.transform.localRotation = Quaternion.Euler(0, 0, right ? 0f : 180f) * Quaternion.Euler(-90f, 0, 0);
            go.transform.localPosition = new Vector3(right ? -6f : 6f, 0, -2f);
            go.transform.localScale = new Vector3(78f, 16f, 78f);
        }
        static Mesh _prism;

        /// Shows the loading card with a percentage, or hides it (pass a negative value).
        float _noticeUntil;

        /// A short message in the loading card for a while (an imagery problem, say).
        public void SetNotice(string text, float seconds)
        {
            _noticeUntil = Time.unscaledTime + seconds;
            _loading.gameObject.SetActive(true);
            _loadingText.text = Esc(text);
            _loadingText.fontSize = 15;
            _loadingText.overflowMode = TextOverflowModes.Ellipsis;
            _loadingText.rectTransform.sizeDelta = new Vector2(480, 54);
            _loadingFill.transform.parent.gameObject.SetActive(false);
            _loadingShown = -2;
        }

        public void SetLoading(float percent)
        {
            if (Time.unscaledTime < _noticeUntil) return;
            if (_loadingShown == -2) { _loadingText.rectTransform.sizeDelta = new Vector2(480, 32); _loadingText.fontSize = 20; _loadingFill.transform.parent.gameObject.SetActive(true); _loadingShown = -1; }
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
            sb.Append("<size=13><color=#FF4F7B>SELECTED</color></size>\n");
            sb.Append("<size=24><b>").Append(Esc(p.title)).Append("</b></size>\n");
            if (!string.IsNullOrEmpty(p.subtitle)) sb.Append("<color=#BCCBFF>").Append(Esc(p.subtitle)).Append("</color>\n");
            foreach (var r in p.rows) sb.Append("<color=#BCCBFF>").Append(Esc(r.Key)).Append("</color>  ").Append(Esc(r.Value)).Append('\n');
            if (p.list.Count > 0) foreach (var l in p.list) sb.Append("<color=#FF4F7B>●</color> ").Append(Esc(l)).Append('\n');
            if (p.refs != null && p.refs.Length > 0)
            {
                sb.Append("<size=14><color=#8FA3E0>").Append(p.refs.Length == 1 ? "Source: " : "Sources: ");
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
            sb.Append("\n<size=13><color=#8FA3E0>B flies there. Trigger on empty space clears.</color></size>");
            _infoText.text = sb.ToString();
        }

        // ------------------------------------------------------------ the companies legend
        /// The slide's companies on the top row of the bottom box (right-aligned), each a color dot
        /// and its logo or name. Pointing at one picks out its sites; a click holds it; a second
        /// click lets go. A slide about one company shows its name at the row's left.
        public void SetLegend(PkgLegendItem[] items)
        {
            UI.Clear(_legendRow);
            _legendEntries.Clear();
            _hasLegend = items != null && items.Length > 0;
            _companyName.text = items != null && items.Length == 1 ? Esc(items[0].name) : "";
            _legendRow.gameObject.SetActive(_hasLegend);
            _filterRow.gameObject.SetActive(!_hasLegend);
            if (!_hasLegend) return;
            foreach (var it in items)
            {
                string company = string.IsNullOrEmpty(it.company) ? it.key : it.company;
                var bg = UI.Box("Company " + it.name, _legendRow, new Color(0.05f, 0.12f, 0.32f, 0.6f));
                UI.Round(bg, 10f);
                var h = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
                h.padding = new RectOffset(8, 10, 4, 4); h.spacing = 6;
                h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false; h.childAlignment = TextAnchor.MiddleLeft;
                var dot = UI.Box("Dot", bg.transform, Hex.Color(it.color, 1f)); dot.sprite = UI.Circle; dot.raycastTarget = false;
                var dl = dot.gameObject.AddComponent<LayoutElement>(); dl.preferredWidth = 14; dl.preferredHeight = 14;
                var name = UI.Label("Name", bg.transform, Esc(it.name), 15, UI.Text, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.gameObject.AddComponent<LayoutElement>().preferredWidth = name.preferredWidth + 2;
                if (!string.IsNullOrEmpty(it.image) && _host != null)
                {
                    // The mark on a small white plate (most marks are dark); the name gives way to it.
                    var plate = UI.Box("Plate", bg.transform, new Color(1, 1, 1, 0.95f)); UI.Round(plate, 5f); plate.raycastTarget = false;
                    var pl = plate.gameObject.AddComponent<LayoutElement>(); pl.preferredWidth = 64; pl.preferredHeight = 24;
                    var mark = UI.Box("Mark", plate.transform, Color.white); UI.Stretch(mark.rectTransform, 4, 4, 3, 3); mark.preserveAspect = true; mark.raycastTarget = false;
                    plate.gameObject.SetActive(false);
                    var nameGo = name.gameObject;
                    _host.StartCoroutine(_pkg.LoadSprite(it.image, sp => { if (sp != null && mark != null) { mark.sprite = sp; plate.gameObject.SetActive(true); nameGo.SetActive(false); } }));
                }
                var b = bg.gameObject.AddComponent<Button>();
                var colors = b.colors; colors.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1.4f); colors.colorMultiplier = 1.6f; b.colors = colors;
                b.targetGraphic = bg;
                b.onClick.AddListener(() => CompanyFocus.ToggleHeld(company));
                var relay = bg.gameObject.AddComponent<HoverRelay>();
                relay.enter = () => CompanyFocus.SetHover(company);
                relay.exit = () => { if (CompanyFocus.Hover == company) CompanyFocus.SetHover(null); };
                _legendEntries.Add(new KeyValuePair<string, Image>(company, bg));
            }
            MarkLegend();
        }

        void MarkLegend()
        {
            foreach (var kv in _legendEntries)
            {
                if (kv.Value == null) continue;
                bool held = CompanyFocus.Held == kv.Key;
                var ol = kv.Value.GetComponent<Outline>();
                if (held && ol == null) { ol = kv.Value.gameObject.AddComponent<Outline>(); ol.effectColor = UI.Gold; ol.effectDistance = new Vector2(2f, -2f); }
                if (ol != null) ol.enabled = held;
            }
        }

        /// From orbit with the display on, the side panels close in beside the Earth so the story
        /// and its data read next to it; zooming in, they ease back out to their usual places.
        public void Orbit(GlobeRig rig, Transform eye)
        {
            if (rig == null || eye == null || !Visible) return;
            float target = rig.mode == ViewMode.Flight ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, rig.OrbitBlend)) : 0f;
            float before = _orbitBlend;
            _orbitBlend = Mathf.MoveTowards(_orbitBlend, target, Time.unscaledDeltaTime * 1.6f);
            if (_orbitBlend < 0.001f && before < 0.001f) return;
            // The Earth's bearing and angular radius in the display's frame.
            Vector3 to = rig.BallCenter - eye.position;
            float dist = to.magnitude;
            if (dist < 1e-3f) return;
            Vector3 local = Quaternion.Inverse(root.rotation) * to;
            float earthYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float half = Mathf.Asin(Mathf.Clamp01(rig.BallRadius / dist)) * Mathf.Rad2Deg;
            float e = Mathf.SmoothStep(0f, 1f, _orbitBlend);
            foreach (var sp in _spots)
            {
                float beside = earthYaw + sp.side * (half + sp.halfDeg + 3f);
                // Never closer in than beside the Earth, never farther out than the usual place.
                float yaw = Mathf.Lerp(sp.yaw, sp.side < 0 ? Mathf.Max(sp.yaw, beside) : Mathf.Min(sp.yaw, beside), e);
                Place(sp.t, yaw, sp.pitch);
            }
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
