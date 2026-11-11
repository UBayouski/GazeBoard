using System;

namespace GazeBoard.Speech
{
    /// <summary>Platform-specific text-to-speech backend used by SpeechService.</summary>
    public interface ISpeechSynthesizer : IDisposable
    {
        /// <summary>True once the engine can accept Speak calls.</summary>
        bool IsReady { get; }

        /// <summary>True while audio is being produced.</summary>
        bool IsSpeaking { get; }

        /// <summary>Speak the text. When interrupt is true, anything queued or playing is dropped first.</summary>
        void Speak(string text, bool interrupt = true, bool userText = false);

        /// <summary>
        /// Whether this text can be spoken with no internet connection: a clip for it is already
        /// on the device. Backends that never need a connection return true.
        /// </summary>
        bool HasOfflineClip(string text);

        /// <summary>Stop any current or queued speech.</summary>
        void Stop();

        /// <summary>Speech rate multiplier; 1 is normal.</summary>
        void SetRate(float rate);

        /// <summary>Called once per frame from the Unity main thread.</summary>
        void Tick();
    }
}
