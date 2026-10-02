// A set of flat markers drawn with GPU instancing. Instances are fixed in ECEF; the shader
// applies the globe rig's transform relative to the eye (high/low split positions), so
// nothing is recomputed on the CPU when the globe moves and nothing jitters up close.
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
