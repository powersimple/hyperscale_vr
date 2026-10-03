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
            cables.children.Add(new FilterItem("cables.systems", "Cable systems"));
            cables.children.Add(new FilterItem("cables.landings", "Landing points"));
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
            categories.AddRange(new[] { dc, ai, cables, power, flows, named, borders, labels, night, photo });

            panels.Add(new FilterItem("panel.story", "Story and title"));
            panels.Add(new FilterItem("panel.stats", "Stats"));
            panels.Add(new FilterItem("panel.info", "Selection data"));
            panels.Add(new FilterItem("panel.sources", "Sources"));

            // Sub-items and panels start on; labels and 3D cities start on.
            foreach (var c in categories) foreach (var s in c.children) _on[s.key] = true;
            foreach (var p in panels) _on[p.key] = true;
            _on["labels"] = true;
            _on["borders"] = true;
            _on["photoreal"] = true;
        }

        public bool On(string key) { bool v; return _on.TryGetValue(key, out v) && v; }
        public bool Forced(string key) { return _forced.Contains(key); }

        /// Sets a starting value without counting as a viewer change.
        public void Seed(string key, bool on) { _on[key] = on; }

        /// A viewer's change.
        public void Toggle(string key)
        {
            _on[key] = !On(key);
            _forced.Add(key);
            Raise();
        }

        /// A slide's starting state.
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
            foreach (var f in fuels) LegendFilter.Set("fuel:" + f, !On("power." + f));
            if (Changed != null) Changed();
        }
    }
}
