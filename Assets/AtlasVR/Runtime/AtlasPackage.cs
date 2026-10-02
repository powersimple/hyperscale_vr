// Loads the package from StreamingAssets/AtlasPackage (copied there by the editor's
// "Public Hyperscale > Pull package" command). On Android (Quest) StreamingAssets lives inside
// the APK, so every read goes through UnityWebRequest there.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace AtlasVR
{
    public class AtlasPackage
    {
        public const string Folder = "AtlasPackage";

        public PkgManifest manifest;
        public PkgDeck deck;
        public PkgSlide[] slides;
        public PkgRef[] refs;
        public PkgComputeLayer compute;
        public PkgTeleLayer tele;
        public PkgSites sites;
        public string error;

        readonly Dictionary<string, PkgSlide> _slides = new Dictionary<string, PkgSlide>();
        readonly Dictionary<string, PkgRef> _refs = new Dictionary<string, PkgRef>();
        readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        public PkgSlide Slide(string id) { PkgSlide s; return id != null && _slides.TryGetValue(id, out s) ? s : null; }
        public PkgRef Ref(string id) { PkgRef r; return id != null && _refs.TryGetValue(id, out r) ? r : null; }
        public PkgTrack Track(string id)
        {
            if (deck == null || deck.tracks == null) return null;
            foreach (var t in deck.tracks) if (t.id == id) return t;
            return null;
        }
        public PkgChapter Chapter(string id)
        {
            if (deck == null || deck.chapters == null) return null;
            foreach (var c in deck.chapters) if (c.id == id) return c;
            return null;
        }

        static string PathOf(string rel) { return Path.Combine(Application.streamingAssetsPath, Folder, rel).Replace('\\', '/'); }

        static bool NeedsWebRequest(string p) { return p.Contains("://") || Application.platform == RuntimePlatform.Android; }

        public static IEnumerator ReadText(string rel, Action<string, string> done)
        {
            string p = PathOf(rel);
            if (!NeedsWebRequest(p))
            {
                if (!File.Exists(p)) { done(null, "missing " + rel); yield break; }
                done(File.ReadAllText(p), null);
                yield break;
            }
            using (var req = UnityWebRequest.Get(p))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) done(null, rel + ": " + req.error);
                else done(req.downloadHandler.text, null);
            }
        }

        public static IEnumerator ReadBytes(string rel, Action<byte[], string> done)
        {
            string p = PathOf(rel);
            if (!NeedsWebRequest(p))
            {
                if (!File.Exists(p)) { done(null, "missing " + rel); yield break; }
                done(File.ReadAllBytes(p), null);
                yield break;
            }
            using (var req = UnityWebRequest.Get(p))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) done(null, rel + ": " + req.error);
                else done(req.downloadHandler.data, null);
            }
        }

        public static IEnumerator Load(Action<AtlasPackage> done)
        {
            var pkg = new AtlasPackage();
            string err = null;
            string txt = null;
            Action<string, string> take = (t, e) => { txt = t; if (e != null && err == null) err = e; };

            yield return ReadText("manifest.json", take); if (txt != null) pkg.manifest = JsonUtility.FromJson<PkgManifest>(txt); txt = null;
            yield return ReadText("deck.json", take); if (txt != null) pkg.deck = JsonUtility.FromJson<PkgDeck>(txt); txt = null;
            yield return ReadText("slides.json", take); if (txt != null) pkg.slides = JsonUtility.FromJson<PkgSlides>(txt).slides; txt = null;
            yield return ReadText("refs.json", take); if (txt != null) pkg.refs = JsonUtility.FromJson<PkgRefs>(txt).refs; txt = null;
            // Layers are optional: a missing one leaves its overlay out, nothing more.
            Action<string, string> optional = (t, e) => { txt = t; if (e != null) Debug.LogWarning("[Public Hyperscale] " + e); };
            yield return ReadText("layers/compute.json", optional); if (txt != null) pkg.compute = JsonUtility.FromJson<PkgComputeLayer>(txt); txt = null;
            yield return ReadText("layers/telecables.json", optional); if (txt != null) pkg.tele = JsonUtility.FromJson<PkgTeleLayer>(txt); txt = null;
            yield return ReadText("layers/sites.json", optional); if (txt != null) pkg.sites = JsonUtility.FromJson<PkgSites>(txt); txt = null;

            if (pkg.slides != null) foreach (var s in pkg.slides) pkg._slides[s.id] = s;
            if (pkg.refs != null) foreach (var r in pkg.refs) pkg._refs[r.id] = r;
            if (pkg.deck == null || pkg.slides == null) err = err ?? "The package is missing. In the editor, run Public Hyperscale > Pull package.";
            pkg.error = err;
            done(pkg);
        }

        /// The slide's image as a sprite, loaded on first use. Calls back with null when there is none.
        public IEnumerator LoadSprite(string src, Action<Sprite> done)
        {
            if (string.IsNullOrEmpty(src)) { done(null); yield break; }
            Sprite sp;
            if (_sprites.TryGetValue(src, out sp)) { done(sp); yield break; }
            byte[] bytes = null;
            yield return ReadBytes(src, (b, e) => bytes = b);
            if (bytes == null) { done(null); yield break; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);
            tex.wrapMode = TextureWrapMode.Clamp;
            sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1000f);
            _sprites[src] = sp;
            done(sp);
        }

        public static LocalConfig ReadLocalConfig()
        {
            var ta = Resources.Load<TextAsset>("AtlasVRLocal");
            if (ta == null) return new LocalConfig();
            try { return JsonUtility.FromJson<LocalConfig>(ta.text) ?? new LocalConfig(); }
            catch (Exception) { return new LocalConfig(); }
        }
    }
}
