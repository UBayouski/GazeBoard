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
        // Worded so it does not end on "room": this voice drops that word into a low growl at the
        // end of a sentence ("Showing the calm room" and "Showing the virtual room" both did).
        public const string EnvironmentRoom = "Showing the beach house";
        public static readonly string[] ThemeNames = { "Calm theme", "Warm theme", "High contrast theme" };

        // Fixed row under the grid.
        public const string Yes = "Yes";
        public const string No = "No";
        public const string HelpCall = "I need help. Please come here.";
        /// <summary>Short repeat of the alert, so it rarely collides with what the user is saying.</summary>
        public const string HelpRepeat = "Help!";

        // Personal phrases.
        public const string PhraseSaved = "Saved to my phrases";
        public const string NothingToSave = "Nothing to save yet";
        public const string NothingToRepeat = "Nothing to repeat yet";
        public const string PhraseRemoved = "Removed";
        public const string EditPhrasesOn = "Choose a phrase to remove";
        public const string EditPhrasesOff = "Done editing";

        // Phone alerts (ntfy).
        public const string PhoneOn = "Phone alerts on";
        public const string PhoneOff = "Phone alerts off";
        public const string PhoneTestSent = "Test message sent to your phone";
        public const string PhoneSendFailed = "Could not reach your phone. Check the internet connection.";
        public const string PhoneNewCode = "New phone code. Pair your phone again.";

        // Said when a sentence cannot be spoken: no connection and no clip of it on the device.
        // The listener hears why nothing came, and can read the sentence on the board instead.
        public const string CannotSpeakNow = "No internet. I cannot say that right now.";

        // Speak-on-select setting.
        public const string SpeakTilesOn = "Tiles speak when chosen";
        public const string SpeakTilesOff = "Tiles are silent. Choose Speak to say the sentence.";

        // Board placement.
        public const string BoardRecenterPrompt = "Look where you want the board.";
        public const string BoardMoved = "Board moved";
        public const string BoardCloser = "Closer";
        public const string BoardFarther = "Farther";
        public const string BoardSmaller = "Smaller";
        public const string BoardBigger = "Bigger";
        public const string BoardReset = "Board reset";

        public const string MineEmptyHint = "Spell a sentence, then choose Save. It will appear here.";

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
            yield return Yes;
            yield return No;
            yield return HelpCall;
            yield return HelpRepeat;
            yield return PhraseSaved;
            yield return NothingToSave;
            yield return NothingToRepeat;
            yield return PhraseRemoved;
            yield return EditPhrasesOn;
            yield return EditPhrasesOff;
            yield return PhoneOn;
            yield return PhoneOff;
            yield return PhoneTestSent;
            yield return PhoneSendFailed;
            yield return PhoneNewCode;
            yield return CannotSpeakNow;
            yield return SpeakTilesOn;
            yield return SpeakTilesOff;
            yield return BoardRecenterPrompt;
            yield return BoardMoved;
            yield return BoardCloser;
            yield return BoardFarther;
            yield return BoardSmaller;
            yield return BoardBigger;
            yield return BoardReset;

            // Dwell moves in 0.2 s steps between the configured limits.
            for (float s = DwellSettings.MinDwellSeconds; s <= DwellSettings.MaxDwellSeconds + 0.001f; s += 0.2f)
            {
                yield return DwellChanged(Mathf.Round(s * 10f) / 10f);
            }
        }
    }
}
