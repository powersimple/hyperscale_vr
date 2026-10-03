// Labels that pop up over the places you fly near. Below a few hundred kilometers, the
// nearest visible markers ahead of you (AI sites, named data centers and plants, data
// centers, landing points) each get a small card with the name and one line beneath it.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AtlasVR
{
    public class ProximityLabels
    {
        public bool enabled = true;
        const int Max = 12;
        const double ShowBelow = 350000;   // view height (m)

        readonly Transform _root;
        readonly List<TextMeshPro> _pool = new List<TextMeshPro>();
        readonly List<Candidate> _shown = new List<Candidate>();
        readonly List<Candidate> _scratch = new List<Candidate>();
        float _next;

        struct Candidate
        {
            public double lon, lat;
            public string title, line;
            public float score;
        }

        public ProximityLabels(Transform parent)
        {
            _root = new GameObject("Proximity labels").transform;
            _root.SetParent(parent, false);
            for (int i = 0; i < Max; i++)
            {
                var go = new GameObject("Label " + i);
                go.transform.SetParent(_root, false);
                var t = go.AddComponent<TextMeshPro>();
                t.fontSize = 10f;
                t.alignment = TextAlignmentOptions.Bottom;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.color = Color.white;
                UI.OverText(t);
                go.SetActive(false);
                _pool.Add(t);
            }
        }

        public void Update(GlobeRig rig, Transform eye, ComputeLayer compute, CableLayer cables, SitesLayer sites)
        {
            bool on = enabled && rig != null && eye != null && rig.mode == ViewMode.Flight && rig.ViewHeight < ShowBelow;
            if (!on) { Hide(); return; }
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.25f;
                Gather(rig, eye, compute, cables, sites);
            }
            Place(rig, eye);
        }

        void Hide() { foreach (var t in _pool) if (t.gameObject.activeSelf) t.gameObject.SetActive(false); _shown.Clear(); }

        // Per-gather state, kept in fields so the 4 Hz gather allocates nothing.
        Matrix4x4 _g; Vector3 _fwd, _pos; float _reach; double _ground; GlobeRig _rig;

        void Consider(double lon, double lat, string title, string line, float weight)
        {
            Vector3 w = _g.MultiplyPoint3x4(Wgs84.ToEcef(lon, lat, _ground).ToVector3());
            Vector3 d = w - _pos;
            float dist = d.magnitude;
            if (dist > _reach || dist < 1e-4f) return;
            if (Vector3.Angle(_fwd, d) > 42f) return;
            Vector3 up = _g.MultiplyVector(Wgs84.Up(lon, lat).ToVector3()).normalized;
            if (!_rig.Visible(w, up, _pos)) return;
            _scratch.Add(new Candidate { lon = lon, lat = lat, title = title, line = line, score = dist / weight });
        }

        void Gather(GlobeRig rig, Transform eye, ComputeLayer compute, CableLayer cables, SitesLayer sites)
        {
            _scratch.Clear();
            _rig = rig;
            _g = rig.GlobeToWorld;
            _fwd = eye.forward; _pos = eye.position;
            _ground = Math.Max(0, rig.GroundHeight);
            _reach = (float)Math.Max(25000, rig.ViewHeight * 7.0) * rig.GlobeScale; // world units

            if (compute != null && compute.Data != null)
            {
                if (compute.AiVisible || compute.BuildingVisible)
                    foreach (var s in compute.Data.ai)
                    {
                        if (s.building ? !compute.BuildingVisible : !compute.AiVisible) continue;
                        string line = s.building ? "AI campus, under construction" : (string.IsNullOrEmpty(s.owner) ? "AI compute" : s.owner + (s.mw > 0 ? " · " + Fmt.N(s.mw) + " MW" : ""));
                        Consider(s.lon, s.lat, s.name, line, 3f);
                    }
                if (compute.PointsVisible)
                    foreach (var p in compute.Data.dcs)
                        Consider(p.lon, p.lat, p.name, string.IsNullOrEmpty(p.org) ? "Data center" : p.org, 1f);
            }
            if (sites != null)
            {
                // Sites that already carry a label from the slide are left alone.
                foreach (var s in sites.ShownDataCenters) if (!sites.Labeled.Contains(s.id)) Consider(s.lon, s.lat, s.name, "Data center", 2.5f);
                foreach (var p in sites.ShownPlants) if (!sites.Labeled.Contains(p.id)) Consider(p.lon, p.lat, p.name, string.IsNullOrEmpty(p.fuel) ? "Power plant" : char.ToUpper(p.fuel[0]) + p.fuel.Substring(1) + " power plant", 2.5f);
            }
            if (cables != null && cables.LandingsVisible && cables.Data != null)
                foreach (var l in cables.Data.landings) Consider(l.lon, l.lat, l.name, "Cable landing point", 1.2f);

            _scratch.Sort((a, b) => a.score.CompareTo(b.score));
            _shown.Clear();
            for (int i = 0; i < _scratch.Count && _shown.Count < Max; i++)
            {
                // Skip near-duplicates (several records at one campus).
                bool dup = false;
                foreach (var s in _shown) if (Math.Abs(s.lon - _scratch[i].lon) < 1e-4 && Math.Abs(s.lat - _scratch[i].lat) < 1e-4) { dup = true; break; }
                if (!dup) _shown.Add(_scratch[i]);
            }
            for (int i = 0; i < _pool.Count; i++)
            {
                bool use = i < _shown.Count;
                if (_pool[i].gameObject.activeSelf != use) _pool[i].gameObject.SetActive(use);
                if (use) SetText(_pool[i], "<b>" + Hud.Esc(_shown[i].title) + "</b>\n<size=70%><color=#A6B6C8>" + Hud.Esc(_shown[i].line) + "</color></size>");
            }
        }

        static void SetText(TextMeshPro t, string s) { if (t.text != s) t.text = s; }

        void Place(GlobeRig rig, Transform eye)
        {
            for (int i = 0; i < _shown.Count && i < _pool.Count; i++)
            {
                var c = _shown[i];
                Vector3 w = rig.WorldOf(c.lon, c.lat, Math.Max(0, rig.GroundHeight));
                var t = _pool[i].transform;
                float dist = Vector3.Distance(eye.position, w);
                float s = 0.0105f * dist;          // about the same angular size at any distance
                t.position = w + (w - rig.BallCenter).normalized * s * 1.1f;   // just above the marker, along the local up
                t.rotation = Quaternion.LookRotation(t.position - eye.position, Vector3.up);
                t.localScale = Vector3.one * s;
            }
        }
    }
}
