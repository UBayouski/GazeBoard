using System.Collections.Generic;
using UnityEngine;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Every fixed string the app speaks that is not a board tile, in one place so the
    /// preload step can bake them into the APK for offline playback.
    /// </summary>
    public static class SpokenPhrases
    {
        public const string TestVoice = "Hello, I am your Gaze Board voice.";
        public const string VoiceSpeedChanged = "This is my voice speed now.";
        public const string GazeSelectOn = "Gaze select on";
        public const string GazeSelectOff = "Gaze select off. Pinch to choose tiles.";

        public const string EnvironmentPassthrough = "Showing your room";
        public const string EnvironmentRoom = "Showing the calm room";
        public static readonly string[] ThemeNames = { "Calm theme", "Warm theme", "High contrast theme" };

        public static string DwellChanged(float seconds) => $"Dwell {seconds:0.0} seconds";

        /// <summary>All fixed phrases, including one dwell phrase per reachable dwell value.</summary>
        public static IEnumerable<string> All()
        {
            yield return TestVoice;
            yield return VoiceSpeedChanged;
            yield return GazeSelectOn;
            yield return GazeSelectOff;
            yield return EnvironmentPassthrough;
            yield return EnvironmentRoom;
            foreach (string t in ThemeNames) yield return t;

            // Dwell moves in 0.2 s steps between the configured limits.
            for (float s = DwellSettings.MinDwellSeconds; s <= DwellSettings.MaxDwellSeconds + 0.001f; s += 0.2f)
            {
                yield return DwellChanged(Mathf.Round(s * 10f) / 10f);
            }
        }
    }
}
