// One legend for the globe, the filter list, and the filter state row: each category's gem
// sphere color, or its story icon.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public static class Legend
    {
        // Rich gemstone colors for the spheres.
        public static readonly Color Sapphire = new Color(0.25f, 0.5f, 1f, 1f);
        public static readonly Color Ruby = new Color(1f, 0.16f, 0.36f, 1f);
        public static readonly Color Topaz = new Color(1f, 0.74f, 0.22f, 1f);
        public static readonly Color Emerald = new Color(0.1f, 0.85f, 0.5f, 1f);
        public static readonly Color Aquamarine = new Color(0.45f, 0.95f, 0.92f, 1f);
        public static readonly Color Amethyst = new Color(0.72f, 0.42f, 1f, 1f);

        static readonly Dictionary<string, Color> Spheres = new Dictionary<string, Color>
        {
            { "dc", Sapphire }, { "dc.points", Sapphire }, { "dc.countries", Emerald },
            { "ai", Ruby }, { "ai.operating", Ruby }, { "ai.building", Topaz },
            { "cables.landings", Aquamarine },
        };

        public static string IconFor(string key)
        {
            if (key.StartsWith("power.")) return key.Substring(6);
            switch (key)
            {
                case "cables": case "cables.systems": case "cables.land": return "cable";
                case "footprints": return "cloud";
                case "power": return "gas";
                case "flows": return "stream";
                case "sites": return "dc";
                default: return null;
            }
        }

        public static bool Has(string key) { return Spheres.ContainsKey(key) || IconFor(key) != null; }

        /// A swatch for the key: a shaded gem circle or the icon. Null when the key has none.
        public static RectTransform Swatch(string key, Transform parent, float size)
        {
            Color c;
            if (Spheres.TryGetValue(key, out c))
            {
                var ball = UI.Box("Swatch", parent, c);
                ball.sprite = UI.Circle; ball.raycastTarget = false;
                var shine = UI.Box("Shine", ball.transform, new Color(1, 1, 1, 0.5f));
                shine.sprite = UI.Circle; shine.raycastTarget = false;
                var srt = shine.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0.33f, 0.68f);
                srt.sizeDelta = new Vector2(size * 0.24f, size * 0.24f);
                var edge = ball.gameObject.AddComponent<Outline>(); edge.effectColor = new Color(0, 0, 0, 0.5f); edge.effectDistance = new Vector2(1f, -1f);
                ball.rectTransform.sizeDelta = new Vector2(size, size);
                return ball.rectTransform;
            }
            string kind = IconFor(key);
            if (kind == null || IconSet.Atlas == null) return null;
            var ic = UI.Rect("Swatch", parent).gameObject.AddComponent<RawImage>();
            ic.texture = IconSet.Atlas; ic.uvRect = IconSet.CellRect(kind); ic.raycastTarget = false;
            ic.rectTransform.sizeDelta = new Vector2(size, size);
            return ic.rectTransform;
        }
    }
}
