// The package the export script writes (_build/vr/export_vr_package.mjs in the Hyperscale site),
// read with JsonUtility. Field names match the package JSON exactly.
using System;

namespace AtlasVR
{
    [Serializable] public class PkgManifest { public int format; public string package; public string created; public string deckVersion; public PkgCounts counts; }
    [Serializable] public class PkgCounts { public int tracks, chapters, slides, beats, claims, stats, refs, dcs, dcCountries, aiSites, cables, landings, images; }

    [Serializable] public class PkgDeck { public int format; public string id, title, subtitle, byline, deckVersion; public PkgTrack[] tracks; public PkgChapter[] chapters; public string[] groups; }
    [Serializable] public class PkgTrack { public string id, title, group; public PkgBeat[] beats; }
    [Serializable] public class PkgBeat { public string id, chapter; public bool card; }
    [Serializable] public class PkgChapter { public string id, title, label, eyebrow, track; public string[] slides; }

    [Serializable] public class PkgSlides { public PkgSlide[] slides; }
    [Serializable]
    public class PkgSlide
    {
        public string id, title, subtitle, chapter;
        public bool card, terminal;
        public string body;
        public PkgSegment[] segments;
        public PkgClaim[] claims;
        public PkgStat[] stats;
        public PkgCamera camera;
        public float duration;
        public int holdMs;
        public string advance;
        public PkgShow show;
        public PkgImage image;
        public string entity;
        public string[] explore;
        public string chart;
        public PkgFilters filters;         // the layers this slide turns on (set by the export)
        public PkgLegendItem[] legend;     // the companies this slide names, in their colors
        public PkgLogo[] logos;            // logo cards standing over their places
    }
    [Serializable]
    public class PkgFilters
    {
        public bool set;
        public bool dc, ai, cables, landings, land, power, sites, footprints;
        public string[] providers;
    }
    [Serializable] public class PkgLegendItem { public string key, name, color, image, company; }
    [Serializable] public class PkgLogo { public string key, text, color, image; public double lon, lat; }
    [Serializable] public class PkgSegment { public string text, claim; }
    [Serializable] public class PkgClaim { public string id, text; public string[] refs; }
    [Serializable] public class PkgStat { public string value, label; public string[] refs; }
    [Serializable] public class PkgCamera { public string type; public double lon, lat, viewHeight, heading, pitch; }
    [Serializable]
    public class PkgShow
    {
        public PkgComputeShow compute;
        public PkgTeleShow telecables;
        public float night;
        public bool spin, photoreal;
        public string[] dc, power, flows, highlight, labels, keys;
        public string raw;
    }
    [Serializable] public class PkgComputeShow { public bool on, dcs, ai, countries; }
    [Serializable] public class PkgTeleShow { public bool on, landings; public string[] highlight; }
    [Serializable] public class PkgImage { public string src, alt, credit; }

    [Serializable] public class PkgRefs { public string style; public PkgRef[] refs; }
    [Serializable] public class PkgRef { public string id, note, url, title, author, date; }

    [Serializable] public class PkgComputeLayer { public PkgDc[] dcs; public PkgCountry[] countries; public PkgAi[] ai; public string[] dcRefs; public PkgColors colors; }
    [Serializable] public class PkgDc { public double lat, lon; public string name, org, city, cc; public int nets; }
    [Serializable] public class PkgCountry { public string cc; public double lat, lon; public int count; }
    [Serializable]
    public class PkgAi
    {
        public string name, owner, users;
        public double lat, lon, h100e, mw, costBn;
        public string chip;
        public double chips;
        public string country, place;
        public bool near, building;
        public string kind;
        public string[] members;
        public string date;
        public string[] refs;
        public string company;
    }
    [Serializable] public class PkgColors { public string dc, ai, countryFill; }

    [Serializable] public class PkgTeleLayer { public PkgCable[] cables; public PkgLanding[] landings; public string[] refs; }
    [Serializable] public class PkgCable { public string id, name, color; public PkgLine[] lines; }
    [Serializable] public class PkgLine { public double[] lonlat; }
    [Serializable] public class PkgBorders { public PkgLine[] countries; public PkgLine[] states; }
    [Serializable] public class PkgRegions { public PkgRegion[] regions; }
    [Serializable] public class PkgFlows { public PkgFlow[] flows; public PkgSlideFlows[] slides; }
    [Serializable] public class PkgFlow { public string id, kind, from, to, color, builder; public double lon0, lat0, lon1, lat1, lift, minH, maxH; public float width, speed, count; }
    [Serializable] public class PkgSlideFlows { public string id; public string[] flows; }
    [Serializable] public class PkgRegion { public string name, country, type; public double[] bbox; public PkgLine[] rings; }
    [Serializable] public class PkgLanding { public double lon, lat; public string name; }

    [Serializable] public class PkgFootprints { public PkgProvider[] providers; public PkgFpSite[] sites; }
    [Serializable] public class PkgProvider { public string key, label, @short, color, image, company; public string[] refs; }
    [Serializable] public class PkgFpSite { public string provider, kind, name, city, cc, status; public double lat, lon; public int azs, year; public bool approx; }
    [Serializable] public class PkgLandLines { public PkgLandLine[] lines; public string credit; }
    [Serializable] public class PkgLandLine { public string name, op; public double[] lonlat; }

    [Serializable] public class PkgSites { public PkgSite[] datacenters; public PkgPlant[] plants; }
    [Serializable] public class PkgSite { public string id, name; public double lon, lat; public string kind, region; public double mw; public bool approx; public string[] refs; }
    [Serializable] public class PkgPlant { public string id, name; public double lon, lat; public string fuel; public double mw; public string[] refs; }

    /// Machine-local settings written by the editor's "Pull package" command (never part of the package).
    [Serializable] public class LocalConfig { public string ionToken = ""; public string packageName = ""; public string pulledFrom = ""; }
}
