using GazeBoard.Speech;
using UnityEditor;
using UnityEngine;

namespace GazeBoard.EditorTools
{
    /// <summary>Play-mode helper: speaks a test phrase through whatever backend SpeechService picked.</summary>
    public static class SpeechTestMenu
    {
        [MenuItem("GazeBoard/Test Speech (Play Mode)")]
        private static void TestSpeech()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog("GazeBoard", "Enter Play mode first, then run this command.", "OK");
                return;
            }
            SpeechService.Speak("Hello, this is Gaze Board speaking.");
        }

        [MenuItem("GazeBoard/Test Speech (Play Mode)", true)]
        private static bool TestSpeechValidate() => Application.isPlaying;
    }
}
