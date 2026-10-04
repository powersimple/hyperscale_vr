// The filter state behind the checkboxes across the top of the view. Each slide sets the
// starting state from what it shows; the viewer's changes hold until the next slide. A
// category the viewer turns on that the slide did not show draws all of its data.
using System;
using System.Collections.Generic;

namespace AtlasVR
{
    public class FilterItem
    {
        public string key, label;
        public List<FilterItem> children = new List<FilterItem>();
        public FilterItem(string key, string label) { this.key = key; this.label = label; }
    }

    public class Filters
    {
        readonly Dictionary<string, bool> _on = new Dictionary<string, bool>();
        readonly HashSet<string> _forced = new HashSet<string>();
        public readonly List<FilterItem> categories = new List<FilterItem>();
        public readonly List<FilterItem> panels = new List<FilterItem>();
        /// Comfort and access: the viewer's own settings, kept across slides and Begin.
        public readonly List<FilterItem> access = new List<FilterItem>();
        public readonly List<string> fuels = new List<string>();
        public event Action Changed;

        public Filters(PkgSites sites)
        {
            var dc = new FilterItem("dc", "Data centers");
            dc.children.Add(new FilterItem("dc.points", "Each data center"));
            dc.children.Add(new FilterItem("dc.countries", "Count per country, from afar"));
            var ai = new FilterItem("ai", "AI compute");
            ai.children.Add(new FilterItem("ai.operating", "Operating"));
            ai.children.Add(new FilterItem("ai.building", "Under construction"));
            var cables = new FilterItem("cables", "Cables");
            cables.children.Add(new FilterItem("cables.systems", "Sea cables"));
            cables.children.Add(new FilterItem("cables.land", "Land lines"));
            cables.children.Add(new FilterItem("cables.landings", "Landing points"));
            var clouds = new FilterItem("footprints", "Cloud regions and campuses");
            var power = new FilterItem("power", "Power");
            if (sites != null && sites.plants != null)
                foreach (var p in sites.plants)
                    if (!string.IsNullOrEmpty(p.fuel) && !fuels.Contains(p.fuel)) fuels.Add(p.fuel);
            fuels.Sort();
            foreach (var f in fuels) power.children.Add(new FilterItem("power." + f, char.ToUpper(f[0]) + f.Substring(1)));
            var flows = new FilterItem("flows", "Power and data connections");
            var named = new FilterItem("sites", "Named sites");
            var borders = new FilterItem("borders", "Borders");
            borders.children.Add(new FilterItem("borders.countries", "Countries and coasts"));
            borders.children.Add(new FilterItem("borders.states", "States and provinces"));
            var labels = new FilterItem("labels", "Place labels up close");
            var night = new FilterItem("night", "Night lights");
            var photo = new FilterItem("photoreal", "3D cities up close");
            var room = new FilterItem("mr", "Mixed reality: your room around the Earth");
            categories.AddRange(new[] { dc, ai, clouds, cables, power, flows, named, borders, labels, night, photo, room });

            panels.Add(new FilterItem("panel.story", "Story and title"));
            panels.Add(new FilterItem("panel.stats", "Stats"));
            panels.Add(new FilterItem("panel.info", "Selection data"));
            panels.Add(new FilterItem("panel.sources", "Sources"));

            access.Add(new FilterItem("access.large", "Larger text and buttons"));
            access.Add(new FilterItem("access.contrast", "High contrast panels"));
            access.Add(new FilterItem("access.speak", "Read each slide aloud"));
            access.Add(new FilterItem("access.fade", "Fade between places instead of flying"));
            access.Add(new FilterItem("access.still", "Hold animations still"));
            access.Add(new FilterItem("access.snap", "Turn in steps"));
            access.Add(new FilterItem("access.onehand", "One hand: the right controller does it all"));

            // Sub-items and panels start on; labels and 3D cities start on.
            foreach (var c in categories) foreach (var s in c.children) _on[s.key] = true;
            foreach (var p in panels) _on[p.key] = true;
            _on["labels"] = true;
            _on["borders"] = true;
            _on["photoreal"] = true;
            // Comfort and access settings are remembered on the headset between sessions.
            foreach (var a in access) _on[a.key] = UnityEngine.PlayerPrefs.GetInt("AtlasVR." + a.key, 0) == 1;
        }

        public bool On(string key) { bool v; return _on.TryGetValue(key, out v) && v; }
        public bool Forced(string key) { return _forced.Contains(key); }

        /// Sets a starting value without counting as a viewer change; it is also the value Begin restores.
        public void Seed(string key, bool on) { _on[key] = on; _seeded[key] = on; }
        readonly Dictionary<string, bool> _seeded = new Dictionary<string, bool>();

        /// Begin: the settings back to how the app starts (panels, labels, borders, 3D cities).
        public void Defaults()
        {
            foreach (var p in panels) _on[p.key] = true;
            foreach (var c in categories) foreach (var sub in c.children) _on[sub.key] = true;
            _on["labels"] = true;
            _on["borders"] = true;
            _on["photoreal"] = true;
            foreach (var kv in _seeded) _on[kv.Key] = kv.Value;
            _forced.Clear();
        }

        /// A setting changed by the app itself (a switch that could not take effect goes back off).
        public void Set(string key, bool on) { if (On(key) == on) return; _on[key] = on; Raise(); }

        /// A viewer's change.
        public void Toggle(string key)
        {
            _on[key] = !On(key);
            _forced.Add(key);
            if (key.StartsWith("access.")) { UnityEngine.PlayerPrefs.SetInt("AtlasVR." + key, On(key) ? 1 : 0); UnityEngine.PlayerPrefs.Save(); }
            Raise();
        }

        /// The categories that put data on the globe (Clear turns these off).
        public static readonly string[] DataKeys = { "dc", "ai", "footprints", "cables", "power", "flows", "sites", "night" };

        /// Clear: everything off that draws data; the viewer's choice until the next slide.
        public void ClearAll()
        {
            foreach (var k in DataKeys) { _on[k] = false; _forced.Add(k); }
            Raise();
        }

        /// A slide's starting state: the layers the export set for it, or else what its show block names.
        public void FromSlide(PkgSlide slide, bool flows = false)
        {
            var f = slide != null ? slide.filters : null;
            if (f == null || !f.set) { FromSlide(slide != null ? slide.show : null, flows); return; }
            _forced.Clear();
            foreach (var c in categories) foreach (var sub in c.children) _on[sub.key] = true;
            var s = slide.show;
            _on["dc"] = f.dc;
            _on["dc.countries"] = s == null || s.compute == null || !s.compute.on || s.compute.countries;
            _on["ai"] = f.ai;
            _on["footprints"] = f.footprints;
            _on["cables"] = f.cables || f.land;
            _on["cables.systems"] = f.cables || !f.land;
            _on["cables.land"] = f.land;
            _on["cables.landings"] = f.landings || !f.cables;
            _on["power"] = f.power;
            _on["sites"] = f.sites;
            _on["night"] = s != null && s.night >= 0.5f;
            _on["flows"] = flows;
            Raise();
        }

        /// The older path: the slide's show block alone.
        public void FromSlide(PkgShow s, bool flows = false)
        {
            _forced.Clear();
            // Sub-items go back on each slide; the viewer's sub-filter changes last one slide.
            foreach (var c in categories) foreach (var sub in c.children) _on[sub.key] = true;
            bool comp = s != null && s.compute != null && s.compute.on;
            _on["dc"] = comp && s.compute.dcs;
            _on["dc.countries"] = !comp || s.compute.countries;
            _on["ai"] = comp && s.compute.ai;
            _on["cables"] = s != null && s.telecables != null && s.telecables.on;
            _on["cables.landings"] = s == null || s.telecables == null || !s.telecables.on || s.telecables.landings;
            _on["power"] = s != null && s.power != null && s.power.Length > 0;
            _on["sites"] = s != null && ((s.dc != null && s.dc.Length > 0) || (s.highlight != null && s.highlight.Length > 0));
            _on["night"] = s != null && s.night >= 0.5f;
            _on["flows"] = flows;
            Raise();
        }

        void Raise()
        {
            // Sub-item keys mirror into the legend filter the layers read.
            LegendFilter.Set("compute:points", !On("dc.points"));
            LegendFilter.Set("compute:countries", !On("dc.countries"));
            LegendFilter.Set("compute:ai", !On("ai.operating"));
            LegendFilter.Set("compute:building", !On("ai.building"));
            LegendFilter.Set("tc:cables", !On("cables.systems"));
            LegendFilter.Set("tc:landings", !On("cables.landings"));
            LegendFilter.Set("tc:land", !On("cables.land"));
            foreach (var f in fuels) LegendFilter.Set("fuel:" + f, !On("power." + f));
            if (Changed != null) Changed();
        }
    }
}
