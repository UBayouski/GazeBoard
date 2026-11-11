using System;
using System.Collections;
using System.Collections.Generic;
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
        private string _lastSpoken;
        private bool _lastWasUserText;

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

        /// <summary>True when the device reports no network at all.</summary>
        public static bool IsOffline => Application.internetReachability == NetworkReachability.NotReachable;

        /// <summary>
        /// Raised with the text when something could not be spoken: there is no connection and no
        /// clip of it on the device, or the voice service failed. Nothing was heard for that text.
        /// </summary>
        public static event Action<string> WhenUnavailable;

        /// <summary>True when the text can be spoken with no connection (a clip is on the device).</summary>
        public static bool HasOfflineClip(string text)
        {
            var synth = Instance._synth;
            return synth != null && synth.HasOfflineClip(text);
        }

        /// <summary>
        /// Tells the user that the text could not be spoken, instead of staying silent or saying
        /// only part of it: raises WhenUnavailable for the board to show, and says so aloud when
        /// that message itself is on the device.
        /// </summary>
        public static void ReportUnavailable(string text)
        {
            Debug.LogWarning($"[GazeBoard.Speech] Cannot speak {LogText.Of(text)}: no connection and no clip on the device, or the voice service failed.");
            Instance._lastSpoken = text;
            Instance._lastParts = null;
            WhenUnavailable?.Invoke(text);

            var synth = Instance._synth;
            if (synth == null || text == SpokenPhrases.CannotSpeakNow) return;
            if (!synth.HasOfflineClip(SpokenPhrases.CannotSpeakNow)) return;
            if (Instance.AlertPhrasePlaying) return; // the help call matters more than this notice
            synth.Speak(SpokenPhrases.CannotSpeakNow, interrupt: true, userText: false);
        }

        /// <summary>The last text handed to the engine, for the Repeat tile. Null before anything was spoken.</summary>
        public static string LastSpoken => _instance != null ? _instance._lastSpoken : null;

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

        /// <summary>
        /// Speak text, interrupting anything already playing unless interrupt is false.
        /// userText marks sentences the user composed (not baked into the APK), so the backend can
        /// cache them on the device after the first play.
        /// </summary>
        public static void Speak(string text, bool interrupt = true, bool userText = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var synth = Instance._synth;
            Debug.Log($"[GazeBoard.Speech] Speak({LogText.Of(text)}) via {(synth != null ? synth.GetType().Name : "no backend")}, ready={synth?.IsReady}");
            Instance._lastSpoken = text;
            Instance._lastWasUserText = userText;
            Instance._lastParts = null;
            // With no connection only what is already on the device can be heard. Say so rather
            // than hand the engine a request that fails without a sound.
            if (IsOffline && synth != null && !synth.HasOfflineClip(text))
            {
                ReportUnavailable(text);
                return;
            }
            // While the alert phrase itself is playing, queue behind it instead of cutting it off.
            if (interrupt && Instance.AlertPhrasePlaying) interrupt = false;
            synth?.Speak(text, interrupt, userText);
            WhenSpoken?.Invoke(text);
        }

        /// <summary>
        /// Speaks several baked phrases back to back as one utterance. Used offline for a sentence
        /// built from tiles, where no clip exists for the sentence as a whole.
        /// </summary>
        public static void SpeakParts(IReadOnlyList<string> parts)
        {
            if (parts == null || parts.Count == 0) return;
            var synth = Instance._synth;
            Debug.Log($"[GazeBoard.Speech] SpeakParts({parts.Count} parts) via {(synth != null ? synth.GetType().Name : "no backend")}");
            for (int i = 0; i < parts.Count; i++)
            {
                synth?.Speak(parts[i], interrupt: i == 0, userText: false);
            }
            Instance._lastSpoken = string.Join(" ", parts);
            Instance._lastParts = new List<string>(parts);
            WhenSpoken?.Invoke(Instance._lastSpoken);
        }

        private List<string> _lastParts;

        /// <summary>Speaks the last text again. Returns false when nothing has been spoken yet.</summary>
        public static bool RepeatLast()
        {
            if (Instance._lastParts != null)
            {
                SpeakParts(Instance._lastParts);
                return true;
            }
            string last = LastSpoken;
            if (string.IsNullOrEmpty(last)) return false;
            Speak(last, true, Instance._lastWasUserText);
            return true;
        }

        /// <summary>True while a help alert is repeating.</summary>
        public static bool IsAlerting => _instance != null && _instance._alert != null;

        /// <summary>
        /// Attention alert: a two-tone chime, then the text, repeated every interval until
        /// StopAlert is called or the repeat limit is reached. For calling someone in another room.
        /// </summary>
        public static void StartAlert(string text, string repeatText = null, float intervalSeconds = 8f, int repeats = 10)
        {
            StopAlert();
            Stop();
            Instance._alert = Instance.StartCoroutine(Instance.AlertLoop(text, repeatText ?? text, intervalSeconds, repeats));
        }

        public static void StopAlert()
        {
            if (_instance == null || _instance._alert == null) return;
            _instance.StopCoroutine(_instance._alert);
            _instance._alert = null;
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
                var wit = new WitSpeechSynthesizer(witSpeaker);
                // A connection that is there but does not reach the voice service fails here.
                wit.WhenLoadFailed += ReportUnavailable;
                _synth = wit;
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

            // Make the baked clips available as plain files (Android cannot stream them from the APK).
            StartCoroutine(PreloadInstaller.Install());

#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidAudioFocus.Request();
#endif
        }

        // ---- Attention chime: a generated two-tone through a plain AudioSource. ----

        private AudioSource _toneSource;
        private AudioClip _chimeClip;
        private Coroutine _alert;

        private float _alertSpokenAt = -10f;
        private const float AlertPhraseWindow = 3f;

        /// <summary>True while the alert phrase itself is most likely still playing.</summary>
        private bool AlertPhrasePlaying =>
            _alert != null && IsSpeaking && Time.unscaledTime - _alertSpokenAt < AlertPhraseWindow;

        private IEnumerator AlertLoop(string text, string repeatText, float intervalSeconds, int repeats)
        {
            for (int i = 0; i < repeats; i++)
            {
                // Never talk over the user: wait until the voice is idle, then a short gap.
                while (IsSpeaking) yield return new WaitForSecondsRealtime(0.1f);
                yield return new WaitForSecondsRealtime(0.4f);

                PlayChime();
                yield return new WaitForSecondsRealtime(0.75f);
                _alertSpokenAt = Time.unscaledTime;
                _synth?.Speak(i == 0 ? text : repeatText, interrupt: false);
                yield return new WaitForSecondsRealtime(intervalSeconds);
            }
            _alert = null;
        }

        private void PlayChime()
        {
            // Rebuilt every time: AudioSettings.Reset (used after focus gain) invalidates runtime clips.
            if (_chimeClip != null) Destroy(_chimeClip);
            {
                const int sampleRate = 48000;
                float[] notes = { 659.25f, 880f }; // E5, A5
                const float noteSeconds = 0.22f;
                int perNote = (int)(sampleRate * noteSeconds);
                var samples = new float[perNote * notes.Length];
                for (int n = 0; n < notes.Length; n++)
                {
                    for (int i = 0; i < perNote; i++)
                    {
                        float attack = Mathf.Min(1f, i / (sampleRate * 0.01f));
                        float release = Mathf.Min(1f, (perNote - i) / (sampleRate * 0.08f));
                        float t = (float)i / sampleRate;
                        samples[n * perNote + i] = Mathf.Sin(2f * Mathf.PI * notes[n] * t) * 0.45f * attack * release;
                    }
                }
                _chimeClip = AudioClip.Create("AttentionChime", samples.Length, 1, sampleRate, false);
                _chimeClip.SetData(samples, 0);
            }
            if (_toneSource == null)
            {
                _toneSource = gameObject.AddComponent<AudioSource>();
                _toneSource.playOnAwake = false;
                _toneSource.spatialBlend = 0f;
                _toneSource.volume = 1f;
            }
            _toneSource.PlayOneShot(_chimeClip);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private Coroutine _audioRefresh;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) return;
            // The shell takes audio focus back whenever the system menu opens; reclaim it on return.
            AndroidAudioFocus.Request();

            // Horizon OS decides whether an app may play audio (PLAY_AUDIO app-op) and Android
            // applies that decision only when an output stream is created. Unity keeps one stream
            // open for the whole session, so a stream created while muted stays muted even after
            // the OS allows audio again. Re-initialising the audio engine shortly after we gain
            // focus recreates the stream under the current permission.
            if (_audioRefresh != null) StopCoroutine(_audioRefresh);
            _audioRefresh = StartCoroutine(RefreshAudioOutput());
        }

        private IEnumerator RefreshAudioOutput()
        {
            // Several attempts: when the app was launched while the headset lay on the desk, the
            // OS may grant audio only some seconds after the user puts it on.
            foreach (float delay in new[] { 1.5f, 4f, 8f, 12f, 20f })
            {
                yield return new WaitForSecondsRealtime(delay);
                if (_synth != null && _synth.IsSpeaking) continue; // never cut a phrase short
                AudioSettings.Reset(AudioSettings.GetConfiguration());
                Debug.Log("[GazeBoard.Speech] Audio output re-initialised after focus gain.");
            }
            _audioRefresh = null;
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
