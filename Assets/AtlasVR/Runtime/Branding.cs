// The Academy marks in the heads-up display: the 3D emblem at the lower right, turning slowly,
// and the wordmark at the lower left. The emblem mesh is baked from the Academy's glTF model
// into Resources/AcademyEmblem.bytes (see the README), so no glTF importer package is needed.
using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace AtlasVR
{
    public class Branding
    {
        readonly Transform _emblem, _spin;
        float _t;

        public Branding(Transform hudRoot, Camera cam)
        {
            // Emblem.
            var bytes = Resources.Load<TextAsset>("AcademyEmblem");
            if (bytes != null)
            {
                _emblem = new GameObject("Academy emblem").transform;
                _emblem.SetParent(hudRoot, false);
                Place(_emblem, 52f, -40f, 1.45f);
                _emblem.localScale = Vector3.one * 0.62f;
                _spin = new GameObject("Spin").transform;
                _spin.SetParent(_emblem, false);
                try { Build(bytes.bytes, _spin); }
                catch (Exception e) { Debug.LogWarning("[Public Hyperscale] Emblem: " + e.Message); }
            }

            // Wordmark.
            var tex = Resources.Load<Texture2D>("AcademyWordmark");
            if (tex != null)
            {
                float w = 380f, h = w * tex.height / Mathf.Max(1, tex.width);
                var c = UI.WorldCanvas("Academy wordmark", hudRoot, new Vector2(w + 36, h + 32), cam);
                var bg = UI.Box("Background", c.transform, UI.Panel); UI.Stretch(bg.rectTransform); UI.Round(bg, 22f);
                var img = UI.Rect("Wordmark", c.transform).gameObject.AddComponent<RawImage>();
                img.texture = tex; img.raycastTarget = false;
                UI.Stretch(img.rectTransform, 18, 18, 16, 16);
                UI.Slab(c);
                Place(c.transform, -52f, -40f, 1.5f);
                c.transform.localScale = Vector3.one * 0.001f * 1.5f;
            }
        }

        static void Place(Transform t, float yaw, float pitch, float dist)
        {
            Quaternion q = Quaternion.Euler(-pitch, yaw, 0);
            t.localPosition = q * Vector3.forward * dist;
            t.localRotation = Quaternion.LookRotation(t.localPosition, q * Vector3.up);
        }

        /// A gentle turn back and forth, so the emblem reads as an object.
        public void Update()
        {
            if (_spin == null) return;
            _t += Time.deltaTime;
            // The model faces +Z in glTF; turned to face the viewer, then rocked.
            _spin.localRotation = Quaternion.Euler(0, 180f + 28f * Mathf.Sin(_t * 0.6f), 0);
        }

        // Format: "AEM1", count, then per part: r g b (linear), vertex count, positions, normals, index count, indices.
        static void Build(byte[] data, Transform parent)
        {
            var sh = Shader.Find("AtlasVR/Emblem");
            using (var r = new BinaryReader(new MemoryStream(data)))
            {
                if (new string(r.ReadChars(4)) != "AEM1") throw new Exception("not an emblem file");
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
                    var mesh = new Mesh { name = "Emblem part " + p, indexFormat = vc > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    mesh.vertices = v; mesh.normals = n; mesh.triangles = idx;
                    mesh.RecalculateBounds();
                    var go = new GameObject("Part " + p);
                    go.transform.SetParent(parent, false);
                    go.layer = parent.gameObject.layer;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mat = new Material(sh);
                    // The model's colors are dark linear values; lift them so they read in the headset.
                    Color c = lin.gamma;
                    c = Color.Lerp(c, Color.white, lin.maxColorComponent > 0.9f ? 0f : 0.12f);
                    mat.SetColor("_Color", c);
                    mat.SetFloat("_Metal", lin.maxColorComponent > 0.9f ? 0.55f : 0.85f);
                    go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                }
            }
        }
    }
}
