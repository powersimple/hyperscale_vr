// Data attributions in the headset. Cesium's own credit system draws a screen overlay that
// a headset never shows, so this reads the same credits (text and logo images, Google's
// included) and draws them on a strip under the slide station.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CesiumForUnity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class VRCredits
    {
        public readonly Canvas canvas;
        readonly TextMeshProUGUI _text;
        readonly RectTransform _logos;
        readonly List<RawImage> _pool = new List<RawImage>();
        float _next;
        string _last = "";
        readonly string _static;

        public VRCredits(Transform parent, Camera cam, string staticCredits)
        {
            _static = staticCredits;
            canvas = UI.WorldCanvas("Credits", parent, new Vector2(1060, 96), cam);
            var bg = UI.Box("Background", canvas.transform, new Color(0.02f, 0.045f, 0.14f, 0.55f)); UI.Stretch(bg.rectTransform); UI.Round(bg, 22f);
            _logos = UI.Rect("Logos", canvas.transform); UI.Place(_logos, 14, 24, 230, 48);
            var h = _logos.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childControlWidth = false; h.childControlHeight = false; h.childAlignment = TextAnchor.MiddleLeft;
            // Small type so every credit fits.
            _text = UI.Label("Text", canvas.transform, "", 10.5f, UI.Muted); UI.Place(_text.rectTransform, 254, 8, 792, 80);
            _text.overflowMode = TextOverflowModes.Ellipsis; _text.lineSpacing = -6;
        }

        public void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            var cs = CesiumCreditSystem.GetDefaultCreditSystem();
            var sb = new StringBuilder();
            var imgs = new List<Texture2D>();
            if (cs != null)
            {
                var images = Get<List<Texture2D>>(cs, "images");
                foreach (var listName in new[] { "onScreenCredits", "popupCredits" })
                {
                    // CesiumCredit and its components are internal to the package: read them by reflection.
                    var list = Get<IEnumerable>(cs, listName);
                    if (list == null) continue;
                    foreach (var credit in list)
                    {
                        var comps = credit != null ? Get<IEnumerable>(credit, "components") : null;
                        if (comps == null) continue;
                        foreach (var c in comps)
                        {
                            if (c == null) continue;
                            string text = Get<string>(c, "text");
                            object idObj = GetValue(c, "imageId");
                            int imageId = idObj is int ? (int)idObj : -1;
                            if (imageId >= 0 && images != null && imageId < images.Count && images[imageId] != null) { if (!imgs.Contains(images[imageId])) imgs.Add(images[imageId]); }
                            else if (!string.IsNullOrEmpty(text)) { string t = text.Trim(); if (sb.ToString().IndexOf(t, StringComparison.Ordinal) < 0) { if (sb.Length > 0) sb.Append(" \u00B7 "); sb.Append(t); } }
                        }
                    }
                }
            }
            if (!string.IsNullOrEmpty(_static)) { if (sb.Length > 0) sb.Append(" · "); sb.Append(_static); }
            string s = sb.ToString();
            if (s != _last) { _text.text = s; _last = s; }
            for (int i = 0; i < Math.Max(imgs.Count, _pool.Count); i++)
            {
                if (i >= _pool.Count)
                {
                    var r = UI.Rect("Logo", _logos).gameObject.AddComponent<RawImage>();
                    r.raycastTarget = false;
                    _pool.Add(r);
                }
                var ri = _pool[i];
                bool on = i < imgs.Count;
                ri.gameObject.SetActive(on);
                if (!on) continue;
                ri.texture = imgs[i];
                float hgt = 34f, w = imgs[i].height > 0 ? hgt * imgs[i].width / imgs[i].height : hgt;
                ri.rectTransform.sizeDelta = new Vector2(Mathf.Min(w, 200f), hgt);
            }
        }

        static T Get<T>(object o, string name) where T : class { return GetValue(o, name) as T; }

        static object GetValue(object o, string name)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var p = o.GetType().GetProperty(name, f);
            if (p != null) return p.GetValue(o, null);
            var fi = o.GetType().GetField("_" + name, f);
            return fi != null ? fi.GetValue(o) : null;
        }
    }

    /// For PC VR: a smoothed third-person camera on the desktop window, so an audience sees
    /// the presenter's view steadied and a little wider. Quest casting needs nothing extra.
    public class Spectator
    {
        readonly Camera _cam;
        readonly Transform _eye;
        Vector3 _pos;
        Quaternion _rot;
        public bool On { get { return _cam.enabled; } }

        public Spectator(Transform eye)
        {
            _eye = eye;
            var go = new GameObject("Spectator camera");
            _cam = go.AddComponent<Camera>();
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.targetDisplay = 0;
            _cam.depth = 50;
            _cam.fieldOfView = 70;
            _cam.nearClipPlane = 0.05f;
            _cam.enabled = false;
            _pos = eye.position; _rot = eye.rotation;
        }

        public void Toggle() { _cam.enabled = !_cam.enabled; }

        public void Tick(Camera main)
        {
            if (!_cam.enabled || _eye == null) return;
            Vector3 target = _eye.position - _eye.forward * 0.35f + Vector3.up * 0.12f;
            _pos = Vector3.Lerp(_pos, target, Time.deltaTime * 3f);
            _rot = Quaternion.Slerp(_rot, Quaternion.LookRotation(_eye.forward, Vector3.up), Time.deltaTime * 2.5f);
            _cam.transform.SetPositionAndRotation(_pos, _rot);
            if (main != null) { _cam.farClipPlane = main.farClipPlane; _cam.clearFlags = main.clearFlags; _cam.backgroundColor = main.backgroundColor; _cam.cullingMask = main.cullingMask; }
        }
    }
}
