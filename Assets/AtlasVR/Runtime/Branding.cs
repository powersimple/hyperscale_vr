// The Academy marks in the heads-up display.
//   Emblem   the 3D axes emblem at the lower right, tipped back 45 degrees, rocking gently.
//   Logo     the silver wordmark lying flat below you, so looking down finds it on the floor,
//            lit from a low angle so the extruded sides catch the light.
// The meshes are baked from the Academy's glTF models into Resources/*.bytes by
// _vr/export/bake_emblem.py, so the project needs no glTF importer.
using System;
using System.IO;
using UnityEngine;

namespace AtlasVR
{
    public class Branding
    {
        readonly Transform _emblem, _spin, _logo, _root;
        // Lights are set in the display's frame and turned with it, so the lighting stays put as you turn.
        static readonly System.Collections.Generic.List<KeyValuePairLight> _lit = new System.Collections.Generic.List<KeyValuePairLight>();
        struct KeyValuePairLight { public Material m; public Vector3 local; }
        float _t;

        public Branding(Transform hudRoot, Camera cam)
        {
            _root = hudRoot;
            _lit.Clear();
            var emblem = Resources.Load<TextAsset>("AcademyEmblem");
            if (emblem != null)
            {
                _emblem = new GameObject("Academy emblem").transform;
                _emblem.SetParent(hudRoot, false);
                Quaternion q = Quaternion.Euler(40f, 52f, 0);
                _emblem.localPosition = q * Vector3.forward * 1.45f;
                // Facing you, then tipped back 45 degrees.
                _emblem.localRotation = Quaternion.LookRotation(_emblem.localPosition, q * Vector3.up) * Quaternion.Euler(45f, 0, 0);
                _emblem.localScale = Vector3.one * 0.62f;
                _spin = new GameObject("Spin").transform;
                _spin.SetParent(_emblem, false);
                Build(emblem.bytes, _spin, null, 0f, new Vector4(0.4f, 0.8f, -0.45f, 0f));
            }

            var logo = Resources.Load<TextAsset>("AcademyLogo");
            if (logo != null)
            {
                _logo = new GameObject("Academy logo (floor)").transform;
                _logo.SetParent(hudRoot, false);
                _logo.localPosition = Quaternion.Euler(82f, 0, 0) * Vector3.forward * 1.55f;
                // Lying flat, face up, the tops of the letters away from you.
                _logo.localRotation = Quaternion.Euler(90f, 0, 0) * Quaternion.Euler(0, 180f, 0);
                _logo.localScale = Vector3.one * 0.95f;
                // Silver, raked by a low light from the front left.
                Build(logo.bytes, _logo, new Color(0.82f, 0.85f, 0.9f), 0.92f, new Vector4(-0.55f, 0.5f, -0.65f, 0f));
            }
        }

        /// A gentle turn back and forth, so the emblem reads as an object.
        public void Update()
        {
            if (_root != null) foreach (var l in _lit) l.m.SetVector("_LightDir", _root.rotation * l.local);
            if (_spin == null) return;
            _t += Time.deltaTime;
            _spin.localRotation = Quaternion.Euler(0, 180f + 28f * Mathf.Sin(_t * 0.6f), 0);
        }

        // Format: "AEM1", count, then per part: r g b (linear), vertex count, positions, normals, index count, indices.
        static void Build(byte[] data, Transform parent, Color? tint, float metal, Vector4 light)
        {
            var sh = Shader.Find("AtlasVR/Emblem");
            try
            {
                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    if (new string(r.ReadChars(4)) != "AEM1") throw new Exception("not a baked mesh file");
                    int parts = r.ReadInt32();
                    for (int p = 0; p < parts; p++)
                    {
                        var lin = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), 1f);
                        int vc = r.ReadInt32();
                        var v = new Vector3[vc]; var n = new Vector3[vc];
                        for (int i = 0; i < vc; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int i = 0; i < vc; i++) n[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        int ic = r.ReadInt32();
                        var idx = new int[ic];
                        for (int i = 0; i < ic; i++) idx[i] = r.ReadInt32();
                        var mesh = new Mesh { name = parent.name + " part " + p, indexFormat = vc > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                        mesh.vertices = v; mesh.normals = n; mesh.triangles = idx;
                        mesh.RecalculateBounds();
                        var go = new GameObject("Part " + p);
                        go.transform.SetParent(parent, false);
                        go.layer = parent.gameObject.layer;
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mat = new Material(sh);
                        bool bright = lin.maxColorComponent > 0.9f;
                        // The emblem's colors are dark linear values; lift them so they read in the headset.
                        Color c = tint ?? Color.Lerp(lin.gamma, Color.white, bright ? 0f : 0.12f);
                        mat.SetColor("_Color", c);
                        mat.SetFloat("_Metal", tint.HasValue ? metal : (bright ? 0.55f : 0.85f));
                        mat.SetVector("_LightDir", light);
                        _lit.Add(new KeyValuePairLight { m = mat, local = light });
                        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Public Hyperscale] " + parent.name + ": " + e.Message); }
        }
    }
}
