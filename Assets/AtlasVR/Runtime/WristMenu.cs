// The wrist menu on the off hand: navigation, both sliders, the stories, modes, comfort,
// and the legend as a filter palette. It shows when the wrist turns toward the face, or
// stays up when pinned with the menu button.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class WristMenu
    {
        public readonly Canvas canvas;
        readonly RectTransform _root, _legend, _stories;
        readonly Slider _main, _sub;
        readonly TextMeshProUGUI _mainLabel, _subLabel, _status;
        readonly Button _modeBtn, _scaleBtn, _postureBtn, _photoBtn, _comfortBtn;
        bool _suppress;
        public bool pinned;

        public event Action Next, Back, Recenter, TogglePanel, ToggleFlight, ToggleScale, TogglePosture, TogglePhotoreal, CycleComfort;
        public event Action<int> MainSlide, SubSlide;
        public event Action<string> OpenTrack;

        const float W = 300, H = 590;

        public WristMenu(Transform parent, Camera cam, PkgDeck deck)
        {
            canvas = UI.WorldCanvas("Wrist menu", parent, new Vector2(W, H), cam);
            _root = (RectTransform)canvas.transform;
            var bg = UI.Box("Background", canvas.transform, UI.Panel); UI.Stretch(bg.rectTransform);
            var edge = UI.Box("Accent", canvas.transform, UI.Accent); UI.Place(edge.rectTransform, 0, 0, W, 4);

            var back = UI.Btn("Back", canvas.transform, "Back", 20, () => { if (Back != null) Back(); }); UI.Place((RectTransform)back.transform, 12, 14, 134, 44);
            var next = UI.Btn("Next", canvas.transform, "Next", 20, () => { if (Next != null) Next(); }, new Color(0.15f, 0.3f, 0.42f, 1f)); UI.Place((RectTransform)next.transform, 154, 14, 134, 44);

            _mainLabel = UI.Label("Main label", canvas.transform, "", 15, UI.Muted); UI.Place(_mainLabel.rectTransform, 14, 66, 272, 20);
            _main = UI.Slider("Main slider", canvas.transform, 1, v => { if (!_suppress && MainSlide != null) MainSlide((int)v); }); UI.Place((RectTransform)_main.transform, 14, 86, 272, 36);
            _subLabel = UI.Label("Substory label", canvas.transform, "", 15, UI.Muted); UI.Place(_subLabel.rectTransform, 14, 124, 272, 20);
            _sub = UI.Slider("Substory slider", canvas.transform, 1, v => { if (!_suppress && SubSlide != null) SubSlide((int)v); }); UI.Place((RectTransform)_sub.transform, 14, 144, 272, 36);

            var storiesBtn = UI.Btn("Stories", canvas.transform, "Stories", 18, () => _stories.gameObject.SetActive(!_stories.gameObject.activeSelf)); UI.Place((RectTransform)storiesBtn.transform, 12, 188, 134, 40);
            var panelBtn = UI.Btn("Panel", canvas.transform, "Slide panel", 18, () => { if (TogglePanel != null) TogglePanel(); }); UI.Place((RectTransform)panelBtn.transform, 154, 188, 134, 40);
            _modeBtn = UI.Btn("Flight", canvas.transform, "Overview", 18, () => { if (ToggleFlight != null) ToggleFlight(); }); UI.Place((RectTransform)_modeBtn.transform, 12, 234, 134, 40);
            _scaleBtn = UI.Btn("Scale", canvas.transform, "Room scale", 18, () => { if (ToggleScale != null) ToggleScale(); }); UI.Place((RectTransform)_scaleBtn.transform, 154, 234, 134, 40);
            _postureBtn = UI.Btn("Posture", canvas.transform, "Seated", 18, () => { if (TogglePosture != null) TogglePosture(); }); UI.Place((RectTransform)_postureBtn.transform, 12, 280, 134, 40);
            _photoBtn = UI.Btn("Photoreal", canvas.transform, "3D city tiles: on", 16, () => { if (TogglePhotoreal != null) TogglePhotoreal(); }); UI.Place((RectTransform)_photoBtn.transform, 154, 280, 134, 40);
            _comfortBtn = UI.Btn("Comfort", canvas.transform, "Comfort: vignette", 16, () => { if (CycleComfort != null) CycleComfort(); }); UI.Place((RectTransform)_comfortBtn.transform, 12, 326, 134, 40);
            var re = UI.Btn("Recenter", canvas.transform, "Recenter", 18, () => { if (Recenter != null) Recenter(); }); UI.Place((RectTransform)re.transform, 154, 326, 134, 40);

            var lt = UI.Label("Legend title", canvas.transform, "Legend", 15, UI.Muted, FontStyles.Bold | FontStyles.UpperCase); UI.Place(lt.rectTransform, 14, 374, 272, 20);
            _legend = UI.Rect("Legend", canvas.transform); UI.Place(_legend, 12, 396, 276, 160);
            var lg = _legend.gameObject.AddComponent<VerticalLayoutGroup>();
            lg.spacing = 4; lg.childControlHeight = true; lg.childControlWidth = true; lg.childForceExpandHeight = false;
            _status = UI.Label("Status", canvas.transform, "", 13, UI.Muted); UI.Place(_status.rectTransform, 14, 562, 272, 20);

            _stories = UI.Rect("Stories list", canvas.transform); UI.Place(_stories, 12, 64, 276, 300);
            var sbg = _stories.gameObject.AddComponent<Image>(); sbg.color = new Color(0.03f, 0.05f, 0.09f, 0.98f);
            var g = _stories.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(134, 40); g.spacing = new Vector2(8, 8); g.padding = new RectOffset(0, 0, 4, 4);
            if (deck != null && deck.tracks != null)
                foreach (var t in deck.tracks)
                {
                    string id = t.id;
                    UI.Btn("Track " + id, _stories, t.id == "main" ? "Main story" : t.title, 15, () => { _stories.gameObject.SetActive(false); if (OpenTrack != null) OpenTrack(id); });
                }
            _stories.gameObject.SetActive(false);
        }

        public void SetPositions(int mainIndex, int mainCount, string trackTitle, int subIndex, int subCount)
        {
            _suppress = true;
            _main.maxValue = Mathf.Max(1, mainCount - 1); _main.value = mainIndex;
            _mainLabel.text = "Main story";
            bool sub = subCount > 0;
            _sub.gameObject.SetActive(sub); _subLabel.gameObject.SetActive(sub);
            if (sub) { _sub.maxValue = Mathf.Max(1, subCount - 1); _sub.value = subIndex; _subLabel.text = trackTitle; }
            _suppress = false;
        }

        public void SetModes(GlobeRig rig, Comfort comfort)
        {
            UI.SetText(_modeBtn, rig.mode == ViewMode.Flight ? "Overview" : "Fly");
            UI.SetText(_scaleBtn, rig.mode == ViewMode.Room ? "Tabletop" : "Room scale");
            UI.SetText(_postureBtn, rig.posture == Posture.Seated ? "Standing" : "Seated");
            UI.SetText(_photoBtn, rig.photorealCloseUps ? "3D city tiles: on" : "3D city tiles: off");
            UI.SetText(_comfortBtn, comfort.mode == Comfort.Mode.Vignette ? "Comfort: vignette" : comfort.mode == Comfort.Mode.Fade ? "Comfort: fades" : "Comfort: off");
        }

        public void SetStatus(string s) { _status.text = s; }

        /// Legend rows for the layers this slide draws; each row switches its layer off and on.
        public void SetLegend(PkgShow show)
        {
            UI.Clear(_legend);
            if (show == null) return;
            if (show.compute != null && show.compute.on)
            {
                if (show.compute.ai) { Row("AI compute, sized by its chips", "compute:ai", new Color(0.94f, 0.24f, 0.62f)); Row("AI campus under construction", "compute:building", new Color(0.94f, 0.24f, 0.62f)); }
                if (show.compute.dcs) { if (show.compute.countries) Row("Data centers in a country", "compute:countries", new Color(0.6f, 0.91f, 0.95f)); Row("Data center", "compute:dcs", new Color(0.6f, 0.91f, 0.95f)); }
            }
            if (show.telecables != null && show.telecables.on)
            {
                Row("Submarine cable system", "tc:cables", new Color(0.3f, 0.67f, 0.97f));
                if (show.telecables.landings) Row("Cable landing point", "tc:landings", Color.white);
            }
        }

        void Row(string label, string key, Color swatch)
        {
            var b = UI.Btn(key, _legend, "", 14, null, new Color(0.06f, 0.1f, 0.16f, 1f));
            b.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            t.alignment = TextAlignmentOptions.MidlineLeft;
            Action refresh = () => t.text = "<color=#" + ColorUtility.ToHtmlStringRGB(swatch) + ">●</color>  " + (LegendFilter.IsOff(key) ? "<color=#5C6B7A><s>" + label + "</s></color>" : label);
            refresh();
            b.onClick.AddListener(() => { LegendFilter.Set(key, !LegendFilter.IsOff(key)); refresh(); });
        }

        /// Hold the menu above the left wrist; show it when the wrist faces the eyes.
        public void Follow(Vector3 handPos, Quaternion handRot, bool tracked, Transform eye)
        {
            if (eye == null) return;
            bool show = pinned;
            if (tracked)
            {
                Vector3 pos = handPos + handRot * new Vector3(0.0f, 0.05f, -0.02f);
                Vector3 toEye = (eye.position - pos).normalized;
                Vector3 palmOut = handRot * Vector3.right; // the inside of the left wrist faces +X when turned up
                if (!pinned) show = Vector3.Dot(palmOut, toEye) > 0.45f;
                if (show)
                {
                    _root.position = pos + toEye * 0.02f + Vector3.up * 0.13f;
                    _root.rotation = Quaternion.LookRotation(_root.position - eye.position, Vector3.up);
                }
            }
            else if (pinned)
            {
                _root.position = eye.position + Flat(eye.forward) * 0.55f + Vector3.down * 0.25f - Vector3.Cross(Vector3.up, Flat(eye.forward)) * 0.25f;
                _root.rotation = Quaternion.LookRotation(_root.position - eye.position, Vector3.up);
            }
            if (canvas.gameObject.activeSelf != show) canvas.gameObject.SetActive(show);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized; }
    }
}
