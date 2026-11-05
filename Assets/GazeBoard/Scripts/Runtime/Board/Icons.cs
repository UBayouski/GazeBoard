using System.Collections.Generic;

namespace GazeBoard.Board
{
    /// <summary>
    /// Lucide icon glyphs (https://lucide.dev, ISC licence) as code points in the Lucide icon font.
    /// Generated from lucide.css; add a name here and the glyph is available to tiles.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<string, string> Glyphs = new Dictionary<string, string>
        {
            { "star", "" },
            { "zap", "" },
            { "hand-helping", "" },
            { "smile", "" },
            { "users", "" },
            { "keyboard", "" },
            { "settings", "" },
            { "volume-2", "" },
            { "delete", "" },
            { "trash-2", "" },
            { "arrow-right", "" },
            { "arrow-left", "" },
            { "space", "" },
            { "repeat", "" },
            { "bookmark-plus", "" },
            { "siren", "" },
            { "check", "" },
            { "x", "" },
            { "clock", "" },
            { "minus", "" },
            { "plus", "" },
            { "eye", "" },
            { "eye-off", "" },
            { "audio-lines", "" },
            { "scan-eye", "" },
            { "palette", "" },
            { "house", "" },
            { "camera", "" },
            { "hand", "" },
            { "door-open", "" },
            { "circle-help", "" },
            { "heart-handshake", "" },
            { "hand-heart", "" },
            { "ban", "" },
            { "hourglass", "" },
            { "message-circle-question", "" },
            { "check-check", "" },
            { "pause", "" },
            { "glass-water", "" },
            { "utensils", "" },
            { "heart-pulse", "" },
            { "bath", "" },
            { "bed", "" },
            { "snowflake", "" },
            { "flame", "" },
            { "pill", "" },
            { "phone", "" },
            { "frown", "" },
            { "move", "" },
            { "layers", "" },
            { "ghost", "" },
            { "angry", "" },
            { "thumbs-up", "" },
            { "meh", "" },
            { "party-popper", "" },
            { "user", "" },
            { "cloud-lightning", "" },
            { "leaf", "" },
            { "thermometer", "" },
            { "heart", "" },
            { "user-round", "" },
            { "cross", "" },
            { "stethoscope", "" },
            { "users-round", "" },
            { "user-x", "" },
            { "person-standing", "" },
            { "bookmark", "" },
            { "history", "" },
            { "timer", "" },
            { "mic", "" },
            { "speech", "" },
            { "bell-ring", "" },
            { "square-pen", "" },
            { "circle-user", "" },
            { "smartphone", "" },
            { "phone-off", "" },
            { "send", "" },
            { "key-round", "" },
            { "sparkles", "" },
        };

        /// <summary>The glyph string for an icon name, or an empty string when unknown.</summary>
        public static string Glyph(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return Glyphs.TryGetValue(name, out string glyph) ? glyph : "";
        }

        /// <summary>Every known glyph, for pre-baking the icon font atlas.</summary>
        public static IEnumerable<string> AllGlyphs() => Glyphs.Values;
    }
}
