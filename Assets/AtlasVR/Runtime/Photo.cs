// Right stick click takes a photo: the view from your eyes, 1920 x 1080, saved as a JPEG in the
// app's Photos folder (on the Quest: Android/data/<app id>/files/Photos, reachable over USB),
// with a camera shutter sound. Meta's own capture (Meta button + trigger) still works too.
using System;
using System.IO;
using UnityEngine;

namespace AtlasVR
{
    public class PhotoCapture
    {
        readonly AudioSource _audio;
        readonly AudioClip _shutter;
        Camera _shot;

        public PhotoCapture(Transform eye)
        {
            _audio = eye.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.8f;
            _shutter = Shutter();
        }

        /// Returns the saved file's path, or null.
        public string Capture(Camera eyeCam)
        {
            if (eyeCam == null) return null;
            _audio.PlayOneShot(_shutter);
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                if (_shot == null)
                {
                    var go = new GameObject("Photo camera");
                    _shot = go.AddComponent<Camera>();
                    _shot.enabled = false;
                    _shot.stereoTargetEye = StereoTargetEyeMask.None;
                }
                _shot.CopyFrom(eyeCam);
                _shot.stereoTargetEye = StereoTargetEyeMask.None;
                _shot.transform.SetPositionAndRotation(eyeCam.transform.position, eyeCam.transform.rotation);
                _shot.fieldOfView = 60f;   // vertical; about 90 degrees across at 16:9
                _shot.aspect = 16f / 9f;
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                _shot.targetTexture = rt;
                _shot.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                tex.Apply(false);
                RenderTexture.active = prev;
                _shot.targetTexture = null;
                string dir = Path.Combine(Application.persistentDataPath, "Photos");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "PublicHyperscale_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg");
                File.WriteAllBytes(path, tex.EncodeToJPG(92));
                Debug.Log("[Public Hyperscale] Photo saved: " + path);
                return path;
            }
            catch (Exception e) { Debug.LogWarning("[Public Hyperscale] Photo failed: " + e.Message); return null; }
            finally
            {
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        // A camera shutter, made here: two quick clicks of filtered noise.
        static AudioClip Shutter()
        {
            const int rate = 44100;
            int n = (int)(rate * 0.22f);
            var data = new float[n];
            var rnd = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 90f) + 0.8f * (t > 0.085f ? Mathf.Exp(-(t - 0.085f) * 70f) : 0f);
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.35f;                       // soften the hiss
                data[i] = lp * env * 0.9f + Mathf.Sin(t * 2f * Mathf.PI * 1800f) * env * 0.15f;
            }
            var clip = AudioClip.Create("Shutter", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
