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

        public void Speak(string text, bool interrupt = true)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!IsReady)
            {
                Debug.LogError($"{Tag} Wit speaker has no TTSService; cannot speak \"{text}\"");
                return;
            }
            Debug.Log($"{Tag} Wit speak request: \"{text}\" (interrupt={interrupt})");
            // Preload: play from the clips baked into the APK when present (offline), otherwise the
            // SDK streams from Wit.ai. See GazeBoard > Preload Board Phrases.
            if (interrupt) _speaker.Speak(text, PreloadCache);
            else _speaker.SpeakQueued(text, PreloadCache);
        }

        private static readonly Meta.WitAi.TTS.Data.TTSDiskCacheSettings PreloadCache =
            new Meta.WitAi.TTS.Data.TTSDiskCacheSettings { DiskCacheLocation = Meta.WitAi.TTS.Data.TTSDiskCacheLocation.Preload };

        public void Stop()
        {
            if (_speaker != null) _speaker.Stop();
        }

        public void SetRate(float rate)
        {
            if (_speaker == null) return;
            int speed = Mathf.RoundToInt(Mathf.Clamp(rate, 0.5f, 2f) * 100f); // Wit speed is a percentage, 50 to 200

            // The speaker normally uses a preset voice; override it with a copy that carries the new speed.
            if (_speaker.VoiceSettings is Meta.WitAi.TTS.Integrations.TTSWitVoiceSettings current)
            {
                if (current.speed == speed) return;
                _speaker.SetVoiceOverride(new Meta.WitAi.TTS.Integrations.TTSWitVoiceSettings
                {
                    SettingsId = current.SettingsId + "@" + speed,
                    PrependedText = current.PrependedText,
                    AppendedText = current.AppendedText,
                    voice = current.voice,
                    style = current.style,
                    pitch = current.pitch,
                    speed = speed,
                });
            }
            else if (_speaker.customWitVoiceSettings != null)
            {
                _speaker.customWitVoiceSettings.speed = speed;
            }
        }

        public void Tick() { }

        public void Dispose() { }
    }
}
