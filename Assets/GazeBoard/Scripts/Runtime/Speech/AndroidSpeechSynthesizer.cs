#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Threading;
using UnityEngine;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Text-to-speech through the Android system engine (android.speech.tts.TextToSpeech),
    /// reached with AndroidJavaObject so no Java plugin is needed. On Quest the default
    /// engine is Meta's on-device synthesis service.
    /// </summary>
    public sealed class AndroidSpeechSynthesizer : ISpeechSynthesizer
    {
        private const string Tag = "[GazeBoard.Speech]";
        private const int QueueFlush = 0;
        private const int QueueAdd = 1;
        private const int StatusSuccess = 0;

        private readonly AndroidJavaObject _tts;
        private readonly InitListener _listener;
        private AndroidJavaObject _emptyParams;
        private int _initStatus = int.MinValue; // written from the Android main thread
        private bool _initHandled;
        private bool _wasSpeaking;
        private int _utteranceCounter;
        private float _pendingRate = 1f;

        public bool IsReady { get; private set; }
        public bool IsSpeaking { get; private set; }

        /// <summary>Raised on the Unity thread when initialisation finishes. Argument is success.</summary>
        public event Action<bool> WhenInitialised;

        public AndroidSpeechSynthesizer()
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                _listener = new InitListener(this);
                _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, _listener);
                _emptyParams = new AndroidJavaObject("android.os.Bundle");
                Debug.Log($"{Tag} TextToSpeech created, waiting for onInit.");
            }
            catch (Exception e)
            {
                Debug.LogError($"{Tag} Failed to create TextToSpeech: {e.Message}");
                _initStatus = -1;
            }
        }

        public void Tick()
        {
            if (!_initHandled && _initStatus != int.MinValue)
            {
                _initHandled = true;
                HandleInit(_initStatus);
            }

            if (!IsReady) return;

            bool speaking = SafeCall(() => _tts.Call<bool>("isSpeaking"), false);
            IsSpeaking = speaking;
            _wasSpeaking = speaking;
        }

        private void HandleInit(int status)
        {
            if (status != StatusSuccess || _tts == null)
            {
                Debug.LogError($"{Tag} TextToSpeech init failed with status {status}. " +
                               "Check the manifest <queries> entry and that a TTS engine is installed.");
                WhenInitialised?.Invoke(false);
                return;
            }

            string engine = SafeCall(() => _tts.Call<string>("getDefaultEngine"), "unknown");
            int langResult = SafeCall(() =>
            {
                using var localeClass = new AndroidJavaClass("java.util.Locale");
                using var locale = localeClass.CallStatic<AndroidJavaObject>("getDefault");
                return _tts.Call<int>("setLanguage", locale);
            }, -2);

            // LANG_MISSING_DATA = -1, LANG_NOT_SUPPORTED = -2. Anything >= 0 is usable.
            if (langResult < 0)
            {
                Debug.LogWarning($"{Tag} Default locale not supported by engine {engine} (code {langResult}); using engine default.");
            }

            SafeCall(() => _tts.Call<int>("setSpeechRate", _pendingRate), 0);
            IsReady = true;
            Debug.Log($"{Tag} TextToSpeech ready. Engine: {engine}, language result: {langResult}.");
            WhenInitialised?.Invoke(true);
        }

        public void Speak(string text, bool interrupt = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!IsReady)
            {
                Debug.LogWarning($"{Tag} Speak called before engine ready: \"{text}\"");
                return;
            }

            string utteranceId = "gb-" + (++_utteranceCounter);
            int result = SafeCall(() => _tts.Call<int>("speak", text, interrupt ? QueueFlush : QueueAdd, _emptyParams, utteranceId), -1);
            if (result != StatusSuccess)
            {
                Debug.LogWarning($"{Tag} speak() returned {result} for \"{text}\"");
            }
            else
            {
                IsSpeaking = true;
            }
        }

        public void Stop()
        {
            if (!IsReady) return;
            SafeCall(() => _tts.Call<int>("stop"), 0);
            IsSpeaking = false;
        }

        public void SetRate(float rate)
        {
            _pendingRate = Mathf.Clamp(rate, 0.5f, 2f);
            if (IsReady) SafeCall(() => _tts.Call<int>("setSpeechRate", _pendingRate), 0);
        }

        public void Dispose()
        {
            if (_tts != null)
            {
                SafeCall(() => _tts.Call<int>("stop"), 0);
                SafeCall(() => { _tts.Call("shutdown"); return 0; }, 0);
                _tts.Dispose();
            }
            _emptyParams?.Dispose();
            IsReady = false;
        }

        private static T SafeCall<T>(Func<T> call, T fallback)
        {
            try { return call(); }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} JNI call failed: {e.Message}");
                return fallback;
            }
        }

        private void OnInitFromJava(int status)
        {
            // Runs on the Android main thread. Only store the value; Tick() handles it on the Unity thread.
            Interlocked.Exchange(ref _initStatus, status);
        }

        private sealed class InitListener : AndroidJavaProxy
        {
            private readonly AndroidSpeechSynthesizer _owner;

            public InitListener(AndroidSpeechSynthesizer owner)
                : base("android.speech.tts.TextToSpeech$OnInitListener")
            {
                _owner = owner;
            }

            // Called by Java: void onInit(int status)
            public void onInit(int status)
            {
                _owner.OnInitFromJava(status);
            }
        }
    }
}
#endif
