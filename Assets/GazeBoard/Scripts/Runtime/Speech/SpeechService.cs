using System;
using UnityEngine;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Single entry point for speaking text. Creates itself on first use, picks the platform
    /// backend, and survives scene loads. Call SpeechService.Speak("hello") from anywhere.
    /// </summary>
    public sealed class SpeechService : MonoBehaviour
    {
        private const string RateKey = "gazeboard.speechRate";
        public const float MinRate = 0.5f;
        public const float MaxRate = 2f;

        private static SpeechService _instance;
        private ISpeechSynthesizer _synth;
        private float _rate = 1f;

        /// <summary>Raised on the Unity thread for every phrase handed to the engine.</summary>
        public static event Action<string> WhenSpoken;

        public static SpeechService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("SpeechService");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<SpeechService>();
                }
                return _instance;
            }
        }

        public static bool IsReady => _instance != null && _instance._synth != null && _instance._synth.IsReady;
        public static bool IsSpeaking => _instance != null && _instance._synth != null && _instance._synth.IsSpeaking;

        /// <summary>Speech rate multiplier, persisted. 1 is normal.</summary>
        public static float Rate
        {
            get => Instance._rate;
            set
            {
                float clamped = Mathf.Clamp(value, MinRate, MaxRate);
                Instance._rate = clamped;
                Instance._synth?.SetRate(clamped);
                PlayerPrefs.SetFloat(RateKey, clamped);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Speak text, interrupting anything already playing unless interrupt is false.</summary>
        public static void Speak(string text, bool interrupt = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var synth = Instance._synth;
            Debug.Log($"[GazeBoard.Speech] Speak(\"{text}\") via {(synth != null ? synth.GetType().Name : "no backend")}, ready={synth?.IsReady}");
            if (DebugBeep) Instance.PlayDebugBeep();
            synth?.Speak(text, interrupt);
            WhenSpoken?.Invoke(text);
        }

        // ---- Bring-up diagnostic: a plain generated tone through a plain AudioSource. ----
        // If this is audible on the headset but the voice is not, the problem is the Wit clip.
        // If neither is audible, Unity audio output itself is not reaching the speakers.
        private const bool DebugBeep = false;
        private AudioSource _beepSource;
        private AudioClip _beepClip;

        private void PlayDebugBeep()
        {
            if (_beepClip == null)
            {
                const int sampleRate = 48000;
                const float seconds = 0.15f;
                const float frequency = 660f;
                int count = (int)(sampleRate * seconds);
                var samples = new float[count];
                for (int i = 0; i < count; i++)
                {
                    float envelope = Mathf.Min(1f, Mathf.Min(i, count - i) / (sampleRate * 0.01f));
                    samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / sampleRate) * 0.4f * envelope;
                }
                _beepClip = AudioClip.Create("DebugBeep", count, 1, sampleRate, false);
                _beepClip.SetData(samples, 0);

                _beepSource = gameObject.AddComponent<AudioSource>();
                _beepSource.playOnAwake = false;
                _beepSource.spatialBlend = 0f;
                _beepSource.volume = 1f;
            }
            _beepSource.PlayOneShot(_beepClip);
            Debug.Log("[GazeBoard.Speech] Debug beep played.");
        }

        public static void Stop() => _instance?._synth?.Stop();

        /// <summary>Create the service early so the engine is initialised before the first tile is chosen.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Warmup() => _ = Instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            _rate = PlayerPrefs.GetFloat(RateKey, 1f);

            // Prefer the Voice SDK speaker when one is in the scene; it has the only voice
            // confirmed to work on Quest. Fall back to the platform engine otherwise.
            var witSpeaker = FindFirstObjectByType<Meta.WitAi.TTS.Utilities.TTSSpeaker>(FindObjectsInactive.Exclude);
            if (witSpeaker != null)
            {
                _synth = new WitSpeechSynthesizer(witSpeaker);
            }
            else
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Debug.LogWarning("[GazeBoard.Speech] No TTSSpeaker in scene; falling back to the Android system engine, which does not work on current Horizon OS.");
                _synth = new AndroidSpeechSynthesizer();
#else
                _synth = new EditorSpeechSynthesizer();
#endif
            }
            _synth.SetRate(_rate);

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidAudioFocus.Request();
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnApplicationFocus(bool hasFocus)
        {
            // The shell takes audio focus back whenever the system menu opens; reclaim it on return.
            if (hasFocus) AndroidAudioFocus.Request();
        }
#endif

        private void Update() => _synth?.Tick();

        private void OnDestroy()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidAudioFocus.Abandon();
#endif
            _synth?.Dispose();
            _synth = null;
            if (_instance == this) _instance = null;
        }
    }
}
