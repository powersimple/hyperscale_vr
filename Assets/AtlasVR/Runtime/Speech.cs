// Read aloud: the slide's title, subtitle, and text through the headset's own text-to-speech
// (Android's speech engine), for the comfort and access setting. Nothing happens off Android or
// when the engine is missing; the app reports that once.
using System;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AtlasVR
{
    public class Speech
    {
        AndroidJavaObject _tts;
        bool _ready, _tried;
        string _pending;
        public string Problem { get; private set; }

        class InitListener : AndroidJavaProxy
        {
            readonly Speech _owner;
            public InitListener(Speech owner) : base("android.speech.tts.TextToSpeech$OnInitListener") { _owner = owner; }
            public void onInit(int status) { _owner.OnInit(status == 0); }
        }

        void OnInit(bool ok)
        {
            _ready = ok;
            if (!ok) Problem = "Read aloud is not available on this headset";
            else if (_pending != null) { string p = _pending; _pending = null; Say(p); }
        }

        void Init()
        {
            _tried = true;
            if (Application.platform != RuntimePlatform.Android) { Problem = "Read aloud works in the headset"; return; }
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                {
                    var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                    _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, new InitListener(this));
                }
            }
            catch (Exception e) { Problem = "Read aloud is not available: " + e.Message; _tts = null; }
        }

        static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

        /// Speaks the text, replacing whatever was being read.
        public void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!_tried) Init();
            if (_tts == null) return;
            if (!_ready) { _pending = text; return; }
            try
            {
                string plain = Tags.Replace(text, " ");
                using (var bundle = new AndroidJavaObject("android.os.Bundle"))
                    _tts.Call<int>("speak", plain, 0, bundle, "slide");   // 0: QUEUE_FLUSH
            }
            catch (Exception e) { Problem = "Read aloud failed: " + e.Message; }
        }

        public void Stop()
        {
            _pending = null;
            if (_tts == null || !_ready) return;
            try { _tts.Call<int>("stop"); } catch (Exception) { }
        }

        public void Dispose()
        {
            if (_tts == null) return;
            try { _tts.Call("shutdown"); } catch (Exception) { }
            _tts.Dispose(); _tts = null;
        }
    }
}
