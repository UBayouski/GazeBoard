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
        void Speak(string text, bool interrupt = true);

        /// <summary>Stop any current or queued speech.</summary>
        void Stop();

        /// <summary>Speech rate multiplier; 1 is normal.</summary>
        void SetRate(float rate);

        /// <summary>Called once per frame from the Unity main thread.</summary>
        void Tick();
    }
}
