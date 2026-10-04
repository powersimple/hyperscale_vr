// The Academy marks in the heads-up display.
//   Emblem   the 3D axes emblem at the lower right of the display, square to you and tipped
//            forward 45 degrees, its top toward you.
//   Logo     the silver wordmark lying flat below you, so looking down finds it on the floor,
//            lit from a low angle so the extruded sides catch the light. It stands outside the
//            display, so it is there with the display off; zoomed all the way out, the build
//            version reads beneath it.
// The meshes are baked from the Academy's glTF models into Resources/*.bytes by
// _vr/export/bake_emblem.py, so the project needs no glTF importer.
using System;
using System.IO;
using UnityEngine;

namespace AtlasVR
{
    public class Branding
    {
        readonly Transform _emblem, _spin, _logo, _root, _floor;
        readonly TMPro.TextMeshPro _version;
        // Lights are set in the display's frame and turned with it, so the lighting stays put as you turn.
        static readonly System.Collections.Generic.List<KeyValuePairLight> _lit = new System.Collections.Generic.List<KeyValuePairLight>();
        struct KeyValuePairLight { public Material m; public Vector3 local; }

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
                // Facing you, tipped forward 45 degrees (its top toward you).
                _emblem.localRotation = Quaternion.LookRotation(_emblem.localPosition, q * Vector3.up) * Quaternion.Euler(-45f, 0, 0);
                _emblem.localScale = Vector3.one * 0.62f;
                _spin = new GameObject("Spin").transform;
                _spin.SetParent(_emblem, false);
                _spin.localRotation = Quaternion.Euler(0, 180f, 0);   // square to you, held still
                Build(emblem.bytes, _spin, null, 0f, new Vector4(0.4f, 0.8f, -0.45f, 0f));
            }

            var logo = Resources.Load<TextAsset>("AcademyLogo");
            if (logo != null)
            {
                // Its own root, following the display's position and heading but not its on and off.
                _floor = new GameObject("Academy logo root").transform;
                _logo = new GameObject("Academy logo (floor)").transform;
                _logo.SetParent(_floor, false);
                _logo.localPosition = Quaternion.Euler(82f, 0, 0) * Vector3.forward * 1.55f;
                // Lying flat, face up, the tops of the letters away from you.
                _logo.localRotation = Quaternion.Euler(90f, 0, 0) * Quaternion.Euler(0, 180f, 0);
                _logo.localScale = Vector3.one * 0.95f;
                // Silver, raked by a low light from the front left.
                Build(logo.bytes, _logo, new Color(0.82f, 0.85f, 0.9f), 0.92f, new Vector4(-0.55f, 0.5f, -0.65f, 0f));

                var vgo = new GameObject("Build version");
                vgo.transform.SetParent(_floor, false);
                // Just past the logo on the floor, lying flat like it, read from where you stand.
                vgo.transform.localPosition = Quaternion.Euler(70f, 0, 0) * Vector3.forward * 1.62f;
                vgo.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                vgo.transform.localScale = Vector3.one * 0.012f;
                _version = vgo.AddComponent<TMPro.TextMeshPro>();
                _version.fontSize = 6f;
                _version.alignment = TMPro.TextAlignmentOptions.Center;
                _version.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                _version.color = new Color(0.82f, 0.86f, 0.95f, 0f);
                _version.rectTransform.sizeDelta = new Vector2(60f, 8f);
                vgo.SetActive(false);
            }
        }

        public void SetVersion(string text) { if (_version != null) _version.text = Hud.Esc(text); }

        /// The logo follows the display's frame whether or not the display shows; the version
        /// shows only zoomed all the way out (fade 0 to 1).
        public void Follow(Transform hudRoot, float versionFade)
        {
            if (_floor != null && hudRoot != null) _floor.SetPositionAndRotation(hudRoot.position, hudRoot.rotation);
            if (_version == null) return;
            bool on = versionFade > 0.02f;
            if (_version.gameObject.activeSelf != on) _version.gameObject.SetActive(on);
            if (on) { var c = _version.color; c.a = versionFade; _version.color = c; }
        }

        /// A gentle turn back and forth, so the emblem reads as an object.
        public void Update()
        {
            if (_root != null) foreach (var l in _lit) l.m.SetVector("_LightDir", _root.rotation * l.local);
            // The emblem holds still, square to you (the gentle rocking is gone).
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
