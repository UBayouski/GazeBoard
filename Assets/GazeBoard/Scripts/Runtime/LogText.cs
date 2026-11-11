using UnityEngine;

namespace GazeBoard
{
    /// <summary>
    /// What the user says is private. Development builds and the editor log it in full, because
    /// that is how speech problems are found; a release build logs only its length.
    /// </summary>
    public static class LogText
    {
        /// <summary>True where logs may contain what the user said.</summary>
        public static bool Verbose => Debug.isDebugBuild || Application.isEditor;

        /// <summary>The text in quotes, or a placeholder with its length in a release build.</summary>
        public static string Of(string text)
        {
            if (text == null) return "(null)";
            return Verbose ? "\"" + text + "\"" : "(" + text.Length + " characters)";
        }
    }
}
