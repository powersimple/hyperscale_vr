// World-space uGUI built in code: panels, labels, buttons, sliders, and scroll areas sized
// for a headset (canvas units are millimeters). Every canvas gets the XR Interaction
// Toolkit's tracked-device raycaster when that package is present, so the controller rays
// and hand pinches work on it, plus the standard raycaster for the mouse in the editor.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AtlasVR
{
    public static class UI
    {
        public static readonly Color Panel = new Color(0.025f, 0.05f, 0.085f, 0.6f);   // glass: the Earth shows through
        public static readonly Color PanelEdge = new Color(0.2f, 0.32f, 0.45f, 1f);
        public static readonly Color ButtonBg = new Color(0.14f, 0.24f, 0.36f, 0.72f);
        public static readonly Color ButtonHover = new Color(0.16f, 0.27f, 0.4f, 1f);
        public static readonly Color Accent = new Color(0.94f, 0.24f, 0.62f, 1f);
        public static readonly Color Text = new Color(0.94f, 0.96f, 0.98f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.7f, 0.8f, 1f);
        public static readonly Color Claim = new Color(0.6f, 0.92f, 0.97f, 1f);

        static readonly List<RectTransform> Canvases = new List<RectTransform>();

        public static Canvas WorldCanvas(string name, Transform parent, Vector2 sizeMm, Camera eventCamera)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = eventCamera;
            c.sortingOrder = 10;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeMm;
            rt.localScale = Vector3.one * 0.001f;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            go.AddComponent<GraphicRaycaster>();
            var xri = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");
            if (xri != null) go.AddComponent(xri);
            Canvases.Add(rt);
            return c;
        }

        /// True when a world ray passes through one of the visible canvases built here.
        public static bool RayHitsCanvas(Ray ray, out float distance)
        {
            distance = float.MaxValue;
            bool hit = false;
            foreach (var rt in Canvases)
            {
                if (rt == null || !rt.gameObject.activeInHierarchy) continue;
                var plane = new Plane(rt.forward, rt.position);
                float d;
                if (!plane.Raycast(ray, out d) || d > distance) continue;
                Vector3 local = rt.InverseTransformPoint(ray.GetPoint(d));
                if (rt.rect.Contains(new Vector2(local.x, local.y))) { distance = d; hit = true; }
            }
            return hit;
        }

        /// Counts another rect (a drop-down that hangs outside its panel) as UI for the laser.
        public static void RegisterHitRect(RectTransform rt) { if (rt != null && !Canvases.Contains(rt)) Canvases.Add(rt); }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent.gameObject.layer;
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float l = 0, float r = 0, float t = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
        }

        public static Image Box(string name, Transform parent, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static TextMeshProUGUI Label(string name, Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = style;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        public static UnityEngine.UI.Button Btn(string name, Transform parent, string text, float size, UnityAction onClick, Color? bg = null)
        {
            var img = Box(name, parent, bg ?? ButtonBg);
            var b = img.gameObject.AddComponent<UnityEngine.UI.Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.35f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.colorMultiplier = 1.4f;
            b.colors = colors;
            b.targetGraphic = img;
            Round(img, 10f);
            Raise(img.gameObject);
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Label("Text", img.transform, text, size, Text, FontStyles.Bold, TextAlignmentOptions.Center);
            Stretch(t.rectTransform, 8, 8, 4, 4);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return b;
        }

        /// A soft shadow and a hairline light edge: a control floating a little off its glass panel.
        public static void Raise(GameObject go, float depth = 3f)
        {
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0.02f, 0.05f, 0.35f);
            sh.effectDistance = new Vector2(0f, -depth);
            var ol = go.AddComponent<Outline>();
            ol.effectColor = new Color(0.6f, 0.85f, 1f, 0.22f);
            ol.effectDistance = new Vector2(1f, -1f);
        }

        static Sprite _rounded, _circle;

        /// A rounded-corner, nine-sliced sprite drawn once at startup, so panels and buttons get soft
        /// corners without texture assets. radius is in canvas units.
        public static void Round(Image img, float radius)
        {
            if (_rounded == null) _rounded = MakeRound(64, 24, true);
            img.sprite = _rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = 24f / Mathf.Max(1f, radius);
        }

        public static Sprite Circle { get { if (_circle == null) _circle = MakeRound(64, 32, false); return _circle; } }

        static Sprite MakeRound(int size, int radius, bool sliced)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance to the rounded rectangle's edge, for a one-pixel antialiased rim.
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        static Material _overlayText;

        /// Labels in the world draw over the terrain and the 3D tiles rather than sinking into them.
        public static void OverText(TMPro.TMP_Text t)
        {
            if (t == null || t.fontSharedMaterial == null) return;
            if (_overlayText == null)
            {
                _overlayText = new Material(t.fontSharedMaterial) { name = "Label (over terrain)" };
                _overlayText.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
                _overlayText.renderQueue = 2999;   // after the terrain, before the HUD panels
            }
            t.fontSharedMaterial = _overlayText;
        }

        static Material _slabMat;

        /// A rounded glass block behind a world canvas: a faint body with a bright rim, so the panel
        /// reads as an object in space while the Earth shows through. Call again after a resize.
        public static void Slab(Canvas c, float depth = 16f, float radius = 22f)
        {
            if (_slabMat == null)
            {
                _slabMat = new Material(Shader.Find("AtlasVR/Glass"));
                _slabMat.SetColor("_Color", new Color(0.06f, 0.11f, 0.18f, 0.22f));
                _slabMat.SetColor("_RimColor", new Color(0.4f, 0.78f, 1f, 0.75f));
                _slabMat.SetFloat("_RimPower", 2.2f);
                _slabMat.renderQueue = 2990;   // before the canvases (3000)
            }
            var rt = (RectTransform)c.transform;
            var t = rt.Find("Slab");
            if (t == null)
            {
                var go = new GameObject("Slab");
                go.transform.SetParent(rt, false);
                go.layer = rt.gameObject.layer;
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>().sharedMaterial = _slabMat;
                t = go.transform;
            }
            var r = rt.rect;
            var mf = t.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) UnityEngine.Object.Destroy(mf.sharedMesh);
            mf.sharedMesh = Meshes.RoundedSlab(r.xMin - 4, r.yMin - 4, r.xMax + 4, r.yMax + 4, 2f, depth, radius + 4);
        }

        public static void SetText(UnityEngine.UI.Button b, string text)
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null) t.text = text;
        }

        public static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            // Top-left anchored placement in canvas millimeters.
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static UnityEngine.UI.Slider Slider(string name, Transform parent, int max, UnityAction<float> onChange)
        {
            var root = Rect(name, parent);
            var s = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            var bg = Box("Track", root, new Color(0.3f, 0.45f, 0.6f, 0.35f)); Stretch(bg.rectTransform, 0, 0, 13, 13); Round(bg, 4f);
            var fillArea = Rect("Fill Area", root); Stretch(fillArea, 0, 0, 13, 13);
            var fill = Box("Fill", fillArea, new Color(0.4f, 0.8f, 0.95f, 0.9f)); Stretch(fill.rectTransform); Round(fill, 4f);
            var handleArea = Rect("Handle Area", root); Stretch(handleArea, 13, 13, 0, 0);
            var handle = Box("Handle", handleArea, Color.white);
            handle.sprite = Circle; handle.preserveAspect = true;   // a round knob
            handle.rectTransform.anchorMin = new Vector2(0, 0.5f); handle.rectTransform.anchorMax = new Vector2(0, 0.5f); // the slider drives x only
            handle.rectTransform.sizeDelta = new Vector2(26, 26);
            var glow = handle.gameObject.AddComponent<Outline>(); glow.effectColor = new Color(0.4f, 0.8f, 1f, 0.5f); glow.effectDistance = new Vector2(2f, -2f);
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.wholeNumbers = true;
            s.minValue = 0; s.maxValue = Mathf.Max(1, max);
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        /// A vertical scroll area; returns the content transform to fill.
        public static RectTransform Scroll(string name, Transform parent, out ScrollRect scroll)
        {
            var root = Rect(name, parent);
            root.gameObject.AddComponent<RectMask2D>();
            var img = root.gameObject.AddComponent<Image>();
            img.color = new Color(0, 0, 0, 0.001f); // catches drags
            scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            var content = Rect("Content", root);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero; content.offsetMax = Vector2.zero;
            var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            v.spacing = 10;
            var f = content.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = root;
            return content;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        /// The EventSystem the scene needs, unless the XR template already brought one.
        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            var xri = Type.GetType("UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit");
            if (xri != null) go.AddComponent(xri);
            else go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
