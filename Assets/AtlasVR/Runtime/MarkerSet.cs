// A set of flat markers drawn with GPU instancing. Instances are fixed in ECEF; the shader
// applies the globe rig's transform relative to the eye (high/low split positions), so
// nothing is recomputed on the CPU when the globe moves and nothing jitters up close.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AtlasVR
{
    public struct MarkerDef
    {
        public double lon, lat;
        public float k;
        public Color color;
        public MarkerDef(double lon, double lat, float k, Color color) { this.lon = lon; this.lat = lat; this.k = k; this.color = color; }
    }

    public class MarkerSet
    {
        const int Batch = 1000;

        public bool visible;
        public float sizeWS = 0.004f;      // marker size, as world meters at 0.8 m from the eye
        public float minLiftWS = 0.002f;   // lift above the surface, same units

        readonly Mesh _mesh;
        readonly Material _mat;
        readonly List<Matrix4x4[]> _mats = new List<Matrix4x4[]>();
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        readonly List<int> _counts = new List<int>();
        Matrix4x4[] _all = new Matrix4x4[0];
        Vector4[] _lows = new Vector4[0];
        Color[] _allColors = new Color[0];

        public int Count { get { return _all.Length; } }
        public int Queue { set { _mat.renderQueue = value; } }

        public MarkerSet(Mesh mesh, bool opaque, int sortingOffset = 0)
        {
            _mesh = mesh;
            _mat = new Material(Shader.Find("AtlasVR/Marker"));
            _mat.enableInstancing = true;
            if (opaque)
            {
                _mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                _mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                _mat.SetFloat("_ZWrite", 1f);
                _mat.renderQueue = (int)RenderQueue.AlphaTest + sortingOffset;
            }
            else
            {
                _mat.renderQueue = (int)RenderQueue.Transparent + sortingOffset;
            }
        }

        public void Set(List<MarkerDef> defs)
        {
            _all = new Matrix4x4[defs.Count];
            _lows = new Vector4[defs.Count];
            _allColors = new Color[defs.Count];
            for (int i = 0; i < defs.Count; i++)
            {
                Vector4 lo;
                _all[i] = Wgs84.Marker(defs[i].lon, defs[i].lat, 0, defs[i].k, out lo);
                _lows[i] = lo;
                _allColors[i] = defs[i].color;
            }
            Rebatch();
        }

        public void SetColor(int i, Color c) { if (i >= 0 && i < _allColors.Length) _allColors[i] = c; }
        public Color GetColor(int i) { return _allColors[i]; }
        public void ApplyColors() { Rebatch(); }

        void Rebatch()
        {
            _mats.Clear(); _blocks.Clear(); _counts.Clear();
            for (int start = 0; start < _all.Length; start += Batch)
            {
                int n = Mathf.Min(Batch, _all.Length - start);
                var m = new Matrix4x4[n];
                // Arrays keep the size they are first set with per block; pad to the batch size.
                var c = new Vector4[Batch];
                var lo = new Vector4[Batch];
                int used = 0;
                for (int i = 0; i < n; i++)
                {
                    Color col = _allColors[start + i];
                    if (col.a <= 0.003f) continue; // hidden instances are left out of the draw
                    m[used] = _all[start + i];
                    c[used] = col;
                    lo[used] = _lows[start + i];
                    used++;
                }
                if (used == 0) continue;
                var b = new MaterialPropertyBlock();
                b.SetVectorArray("_InstColor", c);
                b.SetVectorArray("_InstLow", lo);
                _mats.Add(m); _blocks.Add(b); _counts.Add(used);
            }
        }

        public void Draw(GlobeRig rig)
        {
            if (!visible || rig == null || _mats.Count == 0) return;
            _mat.SetFloat("_SizeWS", sizeWS);
            _mat.SetFloat("_LiftWS", minLiftWS);
            var rp = new RenderParams(_mat);
            rp.worldBounds = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
            rp.shadowCastingMode = ShadowCastingMode.Off;
            rp.receiveShadows = false;
            for (int k = 0; k < _mats.Count; k++)
            {
                rp.matProps = _blocks[k];
                Graphics.RenderMeshInstanced(rp, _mesh, 0, _mats[k], _counts[k]);
            }
        }
    }
}

namespace AtlasVR
{
    /// Markers drawn as the story's icons: camera-facing quads cut from Resources/AtlasIcons.png
    /// (the web deck's SVG icons, rendered to an atlas), placed like MarkerSet's markers.
    public class IconSet
    {
        public static readonly string[] Kinds = { "dc", "planned", "nuclear", "coal", "gas", "hydro", "solar", "wind", "geothermal", "media", "money", "jobs", "anchor", "cable", "cern", "landing", "cloud", "colo", "stream", "tv", "campus" };
        const int Cols = 8, Rows = 3, Batch = 1000;
        static Texture2D _atlas;
        static Mesh _quad;

        public bool visible;
        public float sizeWS = 0.009f;
        public float minLiftWS = 0.004f;
        readonly Material _mat;
        readonly List<Matrix4x4[]> _mats = new List<Matrix4x4[]>();
        readonly List<MaterialPropertyBlock> _blocks = new List<MaterialPropertyBlock>();
        readonly List<int> _counts = new List<int>();

        public static Texture2D Atlas { get { if (_atlas == null) _atlas = Resources.Load<Texture2D>("AtlasIcons"); return _atlas; } }

        /// The atlas cell for a kind, as a UV rectangle (for a RawImage).
        public static UnityEngine.Rect CellRect(string kind)
        {
            int cell = Cell(kind);
            return new UnityEngine.Rect((cell % Cols) / (float)Cols, 1f - (cell / Cols + 1) / (float)Rows, 1f / Cols, 1f / Rows);
        }

        public static int Cell(string kind)
        {
            int i = Array.IndexOf(Kinds, kind);
            return i < 0 ? 0 : i;
        }

        public IconSet(int queueOffset = 0)
        {
            if (_atlas == null) _atlas = Resources.Load<Texture2D>("AtlasIcons");
            if (_quad == null)
            {
                _quad = new Mesh { name = "AtlasIconQuad" };
                _quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                _quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                _quad.bounds = new Bounds(Vector3.zero, Vector3.one);
            }
            _mat = new Material(Shader.Find("AtlasVR/Icon")) { enableInstancing = true };
            if (_atlas != null) _mat.SetTexture("_MainTex", _atlas);
            _mat.renderQueue = (int)RenderQueue.Transparent + 20 + queueOffset;
        }

        /// defs[i].k is the relative size; cells[i] the atlas cell; defs[i].color the tint (alpha fades).
        public void Set(List<MarkerDef> defs, List<int> cells)
        {
            _mats.Clear(); _blocks.Clear(); _counts.Clear();
            for (int start = 0; start < defs.Count; start += Batch)
            {
                int n = Mathf.Min(Batch, defs.Count - start);
                var m = new Matrix4x4[n];
                var c = new Vector4[Batch]; var lo = new Vector4[Batch]; var uv = new Vector4[Batch];
                for (int i = 0; i < n; i++)
                {
                    var d = defs[start + i];
                    Vector4 low;
                    m[i] = Wgs84.Marker(d.lon, d.lat, 0, d.k, out low);
                    lo[i] = low; c[i] = d.color;
                    int cell = cells[start + i];
                    uv[i] = new Vector4((cell % Cols) / (float)Cols, 1f - (cell / Cols + 1) / (float)Rows, 1f / Cols, 1f / Rows);
                }
                var b = new MaterialPropertyBlock();
                b.SetVectorArray("_InstColor", c); b.SetVectorArray("_InstLow", lo); b.SetVectorArray("_InstUV", uv);
                _mats.Add(m); _blocks.Add(b); _counts.Add(n);
            }
        }

        public void Draw(GlobeRig rig)
        {
            if (!visible || rig == null || _mats.Count == 0) return;
            _mat.SetFloat("_SizeWS", sizeWS);
            _mat.SetFloat("_LiftWS", minLiftWS);
            var rp = new RenderParams(_mat);
            rp.worldBounds = new Bounds(rig.BallCenter, Vector3.one * Mathf.Max(10f, rig.BallRadius * 3f));
            rp.shadowCastingMode = ShadowCastingMode.Off;
            rp.receiveShadows = false;
            for (int k = 0; k < _mats.Count; k++)
            {
                rp.matProps = _blocks[k];
                Graphics.RenderMeshInstanced(rp, _quad, 0, _mats[k], _counts[k]);
            }
        }
    }
}
