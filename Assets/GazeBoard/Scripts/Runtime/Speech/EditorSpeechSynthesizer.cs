#if UNITY_EDITOR || !UNITY_ANDROID
using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Stand-in used in the editor and on non-Android players. Logs every phrase, and on a
    /// Windows editor also speaks it through the built-in SAPI voice so the simulator is audible.
    /// </summary>
    public sealed class EditorSpeechSynthesizer : ISpeechSynthesizer
    {
        private const string Tag = "[GazeBoard.Speech]";
        private Process _process;
        private float _rate = 1f;

        public bool IsReady => true;
        public bool IsSpeaking => _process != null && !_process.HasExited;

        public void Speak(string text, bool interrupt = true, bool userText = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Debug.Log($"{Tag} Speak: \"{text}\"");

            if (interrupt) Stop();

#if UNITY_EDITOR_WIN
            try
            {
                // SAPI rate is -10..10; map 0.5x..2x roughly onto -5..5.
                int sapiRate = Mathf.RoundToInt(Mathf.Clamp((_rate - 1f) * 5f, -10f, 10f));
                string escaped = text.Replace("'", "''");
                string script =
                    "Add-Type -AssemblyName System.Speech; " +
                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                    $"$s.Rate = {sapiRate}; $s.Speak('{escaped}')";

                _process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"" + script.Replace("\"", "\\\"") + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Windows voice unavailable: {e.Message}");
                _process = null;
            }
#endif
        }

        public void Stop()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited) _process.Kill();
            }
            catch { /* already gone */ }
            _process.Dispose();
            _process = null;
        }

        public void SetRate(float rate) => _rate = Mathf.Clamp(rate, 0.5f, 2f);

        public void Tick()
        {
            if (_process != null && _process.HasExited)
            {
                _process.Dispose();
                _process = null;
            }
        }

        public void Dispose() => Stop();
    }
}
#endif
