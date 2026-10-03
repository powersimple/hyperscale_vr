// Seen from orbit:
//   OrbitTitle     the deck's title and subtitle, in Raleway, floating above the North Pole.
//   ControlsGuide  with the display off, a picture of each controller's face beside the Earth,
//                  every button and the sticks labelled, with arrowheads for stick directions.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class OrbitTitle
    {
        readonly Transform _root;
        readonly TextMeshPro _title, _sub;
        float _shown;

        public OrbitTitle(string title, string subtitle)
        {
            _root = new GameObject("Title over the globe").transform;
            var bold = Raleway("Raleway-Bold");
            var medium = Raleway("Raleway-Medium");
            _title = Make("Title", title, bold, 14f, new Vector3(0, 1.3f, 0));
            _sub = Make("Subtitle", subtitle, medium ?? bold, 10.5f, Vector3.zero);   // 75% of the title
            _root.gameObject.SetActive(false);
        }

        static TMP_FontAsset Raleway(string name)
        {
            var f = Resources.Load<Font>("Fonts/" + name);
            if (f == null) return null;
            try { return TMP_FontAsset.CreateFontAsset(f); }
            catch (Exception e) { Debug.LogWarning("[Public Hyperscale] Raleway: " + e.Message); return null; }
        }

        TextMeshPro Make(string name, string text, TMP_FontAsset font, float size, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            var t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.text = Hud.Esc(text);
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Bottom;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.color = Color.white;
            t.rectTransform.pivot = new Vector2(0.5f, 0f);    // the text's bottom on its transform
            t.rectTransform.sizeDelta = new Vector2(60f, 0f);
            return t;
        }

        public void Update(GlobeRig rig, Transform eye)
        {
            // Full zoomed right out; it fades as you zoom in, gone before it would sink into the Earth.
            float want = OrbitMenu.Fade(rig);
            _shown = Mathf.MoveTowards(_shown, want, Time.unscaledDeltaTime * 2.5f);
            bool on = _shown > 0.001f;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) return;
            // Above the globe on the side of the North Pole: zoomed out with the equator level, the
            // pole is straight up; for other latitudes, stay above the globe's top edge.
            Vector3 axis = rig.GlobeToWorld.MultiplyVector(Vector3.forward).normalized;   // ECEF +Z
            Vector3 up = Vector3.Slerp(Vector3.up, axis, 0.25f).normalized;
            if (up.y < 0.6f) up = Vector3.up;
            _root.position = rig.BallCenter + up * (rig.BallRadius * 1.12f);
            _root.rotation = Quaternion.LookRotation(_root.position - eye.position, Vector3.up);
            _root.localScale = Vector3.one * (rig.BallRadius * 0.085f);
            var c = new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, _shown));
            _title.color = c; _sub.color = new Color(0.86f, 0.9f, 1f, c.a);
        }
    }

    public class ControlsGuide
    {
        public bool visible;
        readonly Canvas _left, _right;
        readonly Transform _root, _follow;   // its own root, so it shows while the display is hidden
        static readonly Color Line = new Color(1f, 0.82f, 0.4f, 0.85f), Body = new Color(0.08f, 0.12f, 0.24f, 0.85f), Key = new Color(0.86f, 0.89f, 0.95f, 1f);

        public ControlsGuide(Transform hudRoot, Camera cam)
        {
            _follow = hudRoot;
            _root = new GameObject("Controls guide").transform;
            _left = Side(_root, cam, false);
            _right = Side(_root, cam, true);
            _left.gameObject.SetActive(false); _right.gameObject.SetActive(false);
        }

        /// At the Earth's southwest and southeast, at its depth, facing you; fading as you zoom in.
        public void Update(GlobeRig rig, Transform eye)
        {
            float a = visible ? OrbitMenu.Fade(rig) : 0f;
            bool on = a > 0.02f;
            if (_left.gameObject.activeSelf != on) { _left.gameObject.SetActive(on); _right.gameObject.SetActive(on); }
            if (!on || rig == null || eye == null) return;
            Vector3 c = rig.BallCenter;
            Vector3 toEye = eye.position - c; toEye.y = 0;
            if (toEye.sqrMagnitude < 1e-6f) toEye = -Vector3.forward;
            _root.SetPositionAndRotation(c, Quaternion.LookRotation(-toEye.normalized, Vector3.up));
            _root.localScale = Vector3.one * rig.BallRadius;
            Set(_left, -1.62f, a); Set(_right, 1.62f, a);
        }

        static void Set(Canvas c, float x, float alpha)
        {
            var t = c.transform;
            t.localPosition = new Vector3(x, -1.55f, 0);
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * (0.95f / 600f);
            var g = c.GetComponent<CanvasGroup>();
            if (g == null) g = c.gameObject.AddComponent<CanvasGroup>();
            g.alpha = alpha;
        }

        // One controller: its face toward the Earth, the labels on the outer side.
        Canvas Side(Transform hudRoot, Camera cam, bool right)
        {
            const float W = 520, H = 600;
            var c = UI.WorldCanvas(right ? "Controls, right hand" : "Controls, left hand", hudRoot, new Vector2(W, H), cam);
            var bg = UI.Box("Background", c.transform, UI.Panel); UI.Stretch(bg.rectTransform); UI.Round(bg, 22f);
            UI.Slab(c);
            float yaw = right ? 33f : -33f;
            Quaternion q = Quaternion.Euler(2f, yaw, 0);
            c.transform.localPosition = q * Vector3.forward * 1.5f;
            c.transform.localRotation = Quaternion.LookRotation(c.transform.localPosition, q * Vector3.up);
            c.transform.localScale = Vector3.one * 0.0015f;

            var t = c.transform;
            Text(t, 0, 16, W, 30, right ? "RIGHT CONTROLLER" : "LEFT CONTROLLER", 16, TextAlignmentOptions.Center, UI.Muted);
            // The face: buttons toward the middle, the stick toward the outside.
            float fx = right ? 150 : W - 150, fy = 230;
            var face = UI.Box("Face", t, Body); face.sprite = UI.Circle; Place(face.rectTransform, fx, fy, 230, 270);
            var ring = face.gameObject.AddComponent<Outline>(); ring.effectColor = new Color(0.55f, 0.7f, 1f, 0.6f); ring.effectDistance = new Vector2(2, -2);
            float sx = right ? fx + 45 : fx - 45, sy = fy - 40;           // stick
            float bx = right ? fx - 50 : fx + 50;                         // buttons
            float b1y = fy - 70, b2y = fy + 5;                            // upper button, lower button
            Dot(t, sx, sy, 40, new Color(0.18f, 0.24f, 0.4f, 1f), "");
            Dot(t, sx, sy, 24, new Color(0.3f, 0.38f, 0.58f, 1f), "");
            Arrowheads(t, sx, sy, 34);
            Dot(t, bx, b1y, 24, Key, right ? "B" : "Y");
            Dot(t, bx, b2y, 24, Key, right ? "A" : "X");
            float mx = right ? sx - 10 : sx + 10, my = fy + 70;
            Dot(t, mx, my, 9, Key, "");

            // Labels down the outer side, each with a line to its control.
            float lx = right ? W - 175 : 175;                            // where lines meet the label column
            var items = right
                ? new[] {
                    Item(bx, b1y, "B", "Fly to the selection"),
                    Item(bx, b2y, "A", "Display on or off (from orbit, off shows this guide)"),
                    Item(sx, sy, "Stick", "▲ ▼ fly forward and back    ◄ ► slide"),
                    Item(sx, sy + 30, "Stick press", "Take a photo"),
                    Item(mx, my, "Meta", "Quest menu (the app holds still)") }
                : new[] {
                    Item(bx, b1y, "Y", "Next slide"),
                    Item(bx, b2y, "X", "Previous slide"),
                    Item(sx, sy, "Stick ▲ ▼", "Zoom in and out"),
                    Item(sx, sy + 30, "Stick ◄ ►", "Zoomed out: spin the Earth\nZoomed in: turn"),
                    Item(mx, my, "Menu", "This intro: the whole Earth, display off") };
            float ly = 70;
            foreach (var it in items)
            {
                float tx = right ? W - 165 : 10;
                Text(t, tx, ly, 155, 20, it.key, 13, right ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight, new Color(1f, 0.82f, 0.4f));
                Text(t, tx, ly + 18, 155, 48, it.what, 12, right ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.TopRight, UI.Text);
                Segment(t, right ? lx - 12 : lx + 12, ly + 12, it.x, it.y);
                ly += 80;
            }
            Text(t, 20, H - 70, W - 40, 50, right ? "Trigger: point and select.   Grip (hold): turbo." : "Stick press: display back in front.   Grip (hold): precision.", 12, TextAlignmentOptions.Center, UI.Muted);
            return c;
        }

        struct Entry { public float x, y; public string key, what; }
        static Entry Item(float x, float y, string key, string what) { return new Entry { x = x, y = y, key = key, what = what }; }

        static void Place(RectTransform rt, float cx, float cy, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(cx, -cy);
        }

        static void Dot(Transform t, float x, float y, float r, Color c, string letter)
        {
            var d = UI.Box("Control", t, c); d.sprite = UI.Circle; d.raycastTarget = false;
            Place(d.rectTransform, x, y, r * 2, r * 2);
            if (!string.IsNullOrEmpty(letter))
            {
                var l = UI.Label("Letter", d.transform, letter, r, new Color(0.06f, 0.1f, 0.2f), FontStyles.Bold, TextAlignmentOptions.Center);
                UI.Stretch(l.rectTransform);
            }
        }

        static void Arrowheads(Transform t, float x, float y, float d)
        {
            var g = new[] { "▲", "▼", "◄", "►" };
            var o = new[] { new Vector2(0, -d), new Vector2(0, d), new Vector2(-d, 0), new Vector2(d, 0) };
            for (int i = 0; i < 4; i++)
            {
                var l = UI.Label("Arrow", t, g[i], 14, new Color(1f, 0.82f, 0.4f), FontStyles.Normal, TextAlignmentOptions.Center);
                Place(l.rectTransform, x + o[i].x, y + o[i].y, 20, 20);
            }
        }

        static void Text(Transform t, float x, float y, float w, float h, string s, float size, TextAlignmentOptions a, Color c)
        {
            var l = UI.Label("Text", t, s, size, c, size >= 13 ? FontStyles.Bold : FontStyles.Normal, a);
            var rt = l.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, -y);
        }

        static void Segment(Transform t, float x0, float y0, float x1, float y1)
        {
            var img = UI.Box("Line", t, Line); img.raycastTarget = false;
            var rt = img.rectTransform;
            float dx = x1 - x0, dy = y1 - y0, len = Mathf.Sqrt(dx * dx + dy * dy);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(len, 1.6f);
            rt.anchoredPosition = new Vector2((x0 + x1) * 0.5f, -(y0 + y1) * 0.5f);
            rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
        }
    }
}
