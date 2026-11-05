using Meta.WitAi.TTS.Integrations;
using Meta.WitAi.TTS.Utilities;
using UnityEngine;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Text-to-speech through the Meta Voice SDK (Wit.ai) TTSSpeaker already placed in the scene.
    /// Online for new text; phrases that were preloaded or cached on disk play offline.
    /// </summary>
    public sealed class WitSpeechSynthesizer : ISpeechSynthesizer
    {
        private const string Tag = "[GazeBoard.Speech]";
        private readonly TTSSpeaker _speaker;

        // Speed override in effect, or null at the preset's own speed. Baked clips exist only at
        // the preset speed, so offline we fall back to it.
        private TTSWitVoiceSettings _override;

        public WitSpeechSynthesizer(TTSSpeaker speaker)
        {
            _speaker = speaker;

            // Voice SDK hides all its logs in release builds; we need them on device while bringing this up.
            Meta.WitAi.VLog.SuppressLogs = false;

            var ev = _speaker.Events;
            ev.OnLoadBegin.AddListener((s, clip) => Debug.Log($"{Tag} Wit load begin: \"{clip.textToSpeak}\""));
            ev.OnLoadFailed.AddListener((s, clip, error) => Debug.LogError($"{Tag} Wit load FAILED: \"{clip.textToSpeak}\" ({error})"));
            ev.OnLoadSuccess.AddListener((s, clip) => Debug.Log($"{Tag} Wit load ok: \"{clip.textToSpeak}\""));
            ev.OnPlaybackStart.AddListener((s, clip) => Debug.Log($"{Tag} Wit playback start: \"{clip.textToSpeak}\""));
            ev.OnPlaybackComplete.AddListener((s, clip) => Debug.Log($"{Tag} Wit playback finished: \"{clip.textToSpeak}\""));

            var service = _speaker.TTSService;

            // Audio type stays as configured on the TTS service, so runtime clip ids and extensions
            // match the preloaded files in StreamingAssets/TTS.
            Debug.Log($"{Tag} Using Voice SDK TTSSpeaker '{speaker.name}' (voice preset: {speaker.VoiceID}, " +
                      $"service: {(service != null ? service.name : "NONE")}, audio source: {(speaker.AudioSource != null ? speaker.AudioSource.name : "NONE")}).");
        }

        public bool IsReady => _speaker != null && _speaker.TTSService != null;
        public bool IsSpeaking => _speaker != null && _speaker.IsActive;

        public void Speak(string text, bool interrupt = true, bool userText = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!IsReady)
            {
                Debug.LogError($"{Tag} Wit speaker has no TTSService; cannot speak \"{text}\"");
                return;
            }

            // Board phrases: play from the clips baked into the APK (offline), otherwise stream.
            // User sentences: stream once, then keep the clip in persistent storage for offline replay.
            var cache = userText ? PersistentCache : PreloadCache;

            // The baked clips were made at the preset's speed. Offline, a speed override would make
            // every clip id miss and the phrase would fail silently, so speak at the preset speed.
            bool offline = Application.internetReachability == NetworkReachability.NotReachable;
            bool dropOverride = offline && _override != null;
            if (dropOverride) _speaker.ClearVoiceOverride();

            Debug.Log($"{Tag} Wit speak request: \"{text}\" (interrupt={interrupt}, userText={userText}" +
                      $"{(dropOverride ? ", offline: preset speed" : "")})");
            if (interrupt) _speaker.Speak(text, cache);
            else _speaker.SpeakQueued(text, cache);

            if (dropOverride) _speaker.SetVoiceOverride(_override);
        }

        private static readonly Meta.WitAi.TTS.Data.TTSDiskCacheSettings PreloadCache =
            new Meta.WitAi.TTS.Data.TTSDiskCacheSettings { DiskCacheLocation = Meta.WitAi.TTS.Data.TTSDiskCacheLocation.Preload };

        private static readonly Meta.WitAi.TTS.Data.TTSDiskCacheSettings PersistentCache =
            new Meta.WitAi.TTS.Data.TTSDiskCacheSettings { DiskCacheLocation = Meta.WitAi.TTS.Data.TTSDiskCacheLocation.Persistent };

        public void Stop()
        {
            if (_speaker != null) _speaker.Stop();
        }

        public void SetRate(float rate)
        {
            if (_speaker == null) return;
            int speed = Mathf.RoundToInt(Mathf.Clamp(rate, 0.5f, 2f) * 100f); // Wit speed is a percentage, 50 to 200

            // The preset is the voice the clips were baked with. Any other speed is an override
            // built from a copy of it; back at the preset speed the override is cleared so the
            // clip ids match the baked files again.
            var preset = _speaker.TTSService != null && !string.IsNullOrEmpty(_speaker.VoiceID)
                ? _speaker.TTSService.GetPresetVoiceSettings(_speaker.VoiceID) as TTSWitVoiceSettings
                : null;

            if (preset == null)
            {
                if (_speaker.customWitVoiceSettings != null) _speaker.customWitVoiceSettings.speed = speed;
                return;
            }

            if (speed == preset.speed)
            {
                _speaker.ClearVoiceOverride();
                _override = null;
                return;
            }
            if (_override != null && _override.speed == speed) return;

            _override = new TTSWitVoiceSettings
            {
                SettingsId = preset.SettingsId + "@" + speed,
                PrependedText = preset.PrependedText,
                AppendedText = preset.AppendedText,
                voice = preset.voice,
                style = preset.style,
                pitch = preset.pitch,
                speed = speed,
            };
            _speaker.SetVoiceOverride(_override);
        }

        public void Tick() { }

        public void Dispose() { }
    }
}
