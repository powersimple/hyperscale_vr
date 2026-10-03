// The Earth as the home screen. Zoomed out with the display off:
//   the stories     glass buttons at the Earth's depth, split down its west and east sides
//   the story text  the current slide's chapter, title, and subtitle, under the Earth
//   Begin           a big button below that: clears filters and settings, flies to the first slide
// The controller guides stand at the Earth's southwest and southeast (ControlsGuide). Everything
// fades as the view zooms in. The buttons answer the app's own laser (a hit test on their
// rectangles), so they work at the Earth's distance, past the reach of the toolkit's UI rays.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class OrbitMenu
    {
        readonly Transform _root;
        readonly List<Entry> _buttons = new List<Entry>();
        readonly TextMeshProUGUI _chapter, _title, _subtitle;
        readonly Canvas _story;
        readonly CanvasGroup _group;
        float _alpha;
        Entry _hover;

        class Entry { public RectTransform rt; public Image bg; public Action act; public Color normal, hover; }

        public bool Visible { get; private set; }

        public event Action Begin;
        public event Action<string> OpenTrack;

        static readonly Color GlassBlue = new Color(0.03f, 0.1f, 0.32f, 0.82f), GlassHover = new Color(0.1f, 0.24f, 0.62f, 0.92f);
        static readonly Color BeginGold = new Color(0.86f, 0.62f, 0.16f, 0.92f), BeginHover = new Color(1f, 0.78f, 0.3f, 1f);

        // Layout in Earth radii from the Earth's center, in the plane facing you.
        const float SideX = 1.62f, TopY = 0.8f, StepY = 0.3f, ButtonW = 1.0f, ButtonH = 0.23f;
        const float StoryY = -1.17f, BeginY = -1.55f;

        public OrbitMenu(PkgDeck deck, Camera cam)
        {
            _root = new GameObject("Home: the Earth").transform;
            _group = _root.gameObject.AddComponent<CanvasGroup>();

            var tracks = deck != null && deck.tracks != null ? deck.tracks : new PkgTrack[0];
            int left = (tracks.Length + 1) / 2;
            for (int i = 0; i < tracks.Length; i++)
            {
                var t = tracks[i];
                bool west = i < left;
                int row = west ? i : i - left;
                string label = t.id == "main" ? "Main story" : t.title;
                string id = t.id;
                AddButton(label, west ? -SideX : SideX, TopY - row * StepY, ButtonW, ButtonH, 30, GlassBlue, GlassHover, UI.Text, () => { if (OpenTrack != null) OpenTrack(id); }, cam, true);
            }

            // The story under the Earth.
            _story = UI.WorldCanvas("Story under the Earth", _root, new Vector2(1400, 260), cam);
            UI.UnregisterHitRect((RectTransform)_story.transform);
            _chapter = UI.Label("Chapter", _story.transform, "", 26, UI.Muted, FontStyles.UpperCase | FontStyles.Bold, TextAlignmentOptions.Top);
            UI.Place(_chapter.rectTransform, 0, 0, 1400, 40);
            _title = UI.Label("Title", _story.transform, "", 58, UI.Text, FontStyles.Bold, TextAlignmentOptions.Top);
            UI.Place(_title.rectTransform, 0, 44, 1400, 80);
            _title.enableAutoSizing = true; _title.fontSizeMin = 30; _title.fontSizeMax = 58;
            _subtitle = UI.Label("Subtitle", _story.transform, "", 36, new Color(0.86f, 0.9f, 1f, 1f), FontStyles.Normal, TextAlignmentOptions.Top);
            UI.Place(_subtitle.rectTransform, 0, 130, 1400, 110);
            _subtitle.enableAutoSizing = true; _subtitle.fontSizeMin = 22; _subtitle.fontSizeMax = 36;

            AddButton("Begin", 0f, BeginY, 0.78f, 0.27f, 46, BeginGold, BeginHover, new Color(0.06f, 0.07f, 0.12f), () => { if (Begin != null) Begin(); }, cam, false);
            _root.gameObject.SetActive(false);
        }

        void AddButton(string label, float x, float y, float w, float h, float fontSize, Color normal, Color hover, Color textColor, Action act, Camera cam, bool rim)
        {
            const float Units = 400f;   // canvas units per Earth radius
            var c = UI.WorldCanvas("Button " + label, _root, new Vector2(w * Units, h * Units), cam);
            var rt = (RectTransform)c.transform;
            rt.localPosition = new Vector3(x, y, 0);
            rt.localScale = Vector3.one / Units;
            var bg = UI.Box("Glass", c.transform, normal); UI.Stretch(bg.rectTransform); UI.Round(bg, 22f);
            if (rim) { var ol = bg.gameObject.AddComponent<Outline>(); ol.effectColor = new Color(1f, 0.8f, 0.38f, 0.9f); ol.effectDistance = new Vector2(2f, -2f); }
            var t = UI.Label("Label", c.transform, Hud.Esc(label), fontSize, textColor, FontStyles.Bold, TextAlignmentOptions.Center);
            UI.Stretch(t.rectTransform, 14, 14, 6, 6);
            t.enableAutoSizing = true; t.fontSizeMin = fontSize * 0.55f; t.fontSizeMax = fontSize;
            _buttons.Add(new Entry { rt = rt, bg = bg, act = act, normal = normal, hover = hover });
        }

        /// The current slide's words under the Earth.
        public void SetStory(string chapter, string title, string subtitle)
        {
            _chapter.text = Hud.Esc(chapter);
            _title.text = Hud.Esc(title);
            _subtitle.text = Hud.Esc(subtitle);
        }

        /// How much of the home screen shows at this height: all of it zoomed right out, nothing
        /// by the time the view is in close enough for a chapter.
        public static float Fade(GlobeRig rig)
        {
            if (rig == null || rig.mode != ViewMode.Flight) return 0f;
            double h = Math.Max(1, rig.Height);
            float t = Mathf.Clamp01((float)((Math.Log(h) - Math.Log(7.0e6)) / (Math.Log(1.6e7) - Math.Log(7.0e6))));
            return Mathf.SmoothStep(0f, 1f, t);
        }

        public void Update(GlobeRig rig, Transform eye, bool want)
        {
            float target = want ? Fade(rig) : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, Time.unscaledDeltaTime * 2.5f);
            Visible = _alpha > 0.02f;
            if (_root.gameObject.activeSelf != Visible) _root.gameObject.SetActive(Visible);
            if (!Visible) { SetHover(null); return; }
            _group.alpha = _alpha;
            // In the plane through the Earth's center, facing you, upright.
            Vector3 c = rig.BallCenter;
            Vector3 toEye = eye.position - c; toEye.y = 0;
            if (toEye.sqrMagnitude < 1e-6f) toEye = -Vector3.forward;
            _root.SetPositionAndRotation(c, Quaternion.LookRotation(-toEye.normalized, Vector3.up));
            _root.localScale = Vector3.one * rig.BallRadius;
            var s = (RectTransform)_story.transform;
            s.localPosition = new Vector3(0, StoryY, 0);
            s.localScale = Vector3.one * (1.9f / 1400f);
        }

        /// The app's laser over the home screen: hover feedback, and the button under a trigger press.
        public bool Point(Ray ray, bool pressed, out float distance)
        {
            distance = float.MaxValue;
            if (!Visible || _alpha < 0.5f) { SetHover(null); return false; }
            Entry hit = null;
            foreach (var e in _buttons)
            {
                float d;
                if (Hits(e.rt, ray, out d) && d < distance) { distance = d; hit = e; }
            }
            SetHover(hit);
            if (hit != null && pressed && hit.act != null) { Haptics.Pulse(true, 0.3f, 0.05f); hit.act(); }
            return hit != null;
        }

        void SetHover(Entry e)
        {
            if (e == _hover) return;
            if (_hover != null && _hover.bg != null) _hover.bg.color = _hover.normal;
            _hover = e;
            if (_hover != null && _hover.bg != null) _hover.bg.color = _hover.hover;
        }

        static bool Hits(RectTransform rt, Ray ray, out float distance)
        {
            distance = 0;
            if (rt == null || !rt.gameObject.activeInHierarchy) return false;
            var plane = new Plane(rt.forward, rt.position);
            if (!plane.Raycast(ray, out distance) || distance <= 0) return false;
            Vector3 local = rt.InverseTransformPoint(ray.GetPoint(distance));
            return rt.rect.Contains(new Vector2(local.x, local.y));
        }
    }
}
