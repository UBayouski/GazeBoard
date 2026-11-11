using System;
using System.Collections.Generic;
using UnityEngine;

namespace GazeBoard.Board
{
    /// <summary>What a tile does when selected.</summary>
    public enum TileKind
    {
        /// <summary>Speaks immediately and appends to the sentence bar.</summary>
        Phrase,
        /// <summary>Appends a single character to the sentence bar without speaking.</summary>
        Letter,
        /// <summary>Speaks the current sentence.</summary>
        SpeakSentence,
        /// <summary>Removes the last word or letter from the sentence.</summary>
        Backspace,
        /// <summary>Clears the sentence bar.</summary>
        Clear,
        /// <summary>Appends a space.</summary>
        Space,
        /// <summary>Shows the next page of the current category.</summary>
        NextPage,

        /// <summary>Read-only tile that displays a value. Not selectable.</summary>
        Info,
        /// <summary>Settings actions. Handled by the controller.</summary>
        DwellSlower,
        DwellFaster,
        DwellToggle,
        VoiceSlower,
        VoiceFaster,
        TestVoice,
        /// <summary>Cycles the visual theme.</summary>
        Theme,
        /// <summary>Toggles passthrough versus the virtual room.</summary>
        Environment,

        /// <summary>Opens the settings page.</summary>
        Settings,
        /// <summary>Speaks the last thing that was spoken again.</summary>
        Repeat,
        /// <summary>Saves the current sentence as a personal phrase tile.</summary>
        SavePhrase,
        /// <summary>Plays an attention chime and then asks for help.</summary>
        Help,
        /// <summary>A predicted word: completes the word being spelled.</summary>
        Suggestion,
        /// <summary>A saved personal phrase: speaks and appends like a Phrase.</summary>
        SavedPhrase,
        /// <summary>A recently spoken sentence: speaks and appends like a Phrase.</summary>
        RecentPhrase,
        /// <summary>Removes one character from the sentence. For spelling; Backspace removes words.</summary>
        DeleteLetter,
        /// <summary>Toggles edit mode on the Mine page, where saved phrases can be removed.</summary>
        EditPhrases,
        /// <summary>A saved phrase shown in edit mode: selecting it removes it.</summary>
        RemovePhrase,
        /// <summary>Opens the recent sentences sub-page of Mine.</summary>
        ShowRecent,
        /// <summary>Returns from the recent sentences sub-page to the saved phrases.</summary>
        BackToSaved,
        /// <summary>Turns forwarding of spoken sentences to a paired phone on or off.</summary>
        PhoneToggle,
        /// <summary>Reads the phone pairing code aloud, character by character.</summary>
        PhoneCode,
        /// <summary>Sends a test message to the paired phone.</summary>
        PhoneTest,
        /// <summary>Toggles whether phrase tiles are spoken when chosen or only build the sentence.</summary>
        SpeakTilesToggle,
        /// <summary>Board placement: after a short pause, moves the board in front of where the user looks.</summary>
        BoardRecenter,
        BoardCloser,
        BoardFarther,
        BoardSmaller,
        BoardBigger,
        /// <summary>Default distance, height and size, in front of the user.</summary>
        BoardReset,
        /// <summary>Shows the previous page of the current category.</summary>
        PrevPage,
        PhoneNewCode,
    }

    /// <summary>
    /// What a category holds. Drives its colour (a Fitzgerald-style key: people, actions,
    /// feelings and needs each get a hue) and, for Personal, its runtime content.
    /// </summary>
    public enum CategoryRole
    {
        Social,
        Needs,
        Feelings,
        People,
        Spelling,
        /// <summary>Filled at runtime with saved phrases and recent sentences.</summary>
        Personal,
    }

    [Serializable]
    public class TileData
    {
        [Tooltip("Text shown on the tile.")]
        public string label;

        [Tooltip("Text spoken or appended. Leave empty to use the label.")]
        public string text;

        [Tooltip("Lucide icon name shown above the label. Leave empty for no icon.")]
        public string icon;

        public TileKind kind = TileKind.Phrase;

        public string Text => string.IsNullOrEmpty(text) ? label : text;

        public TileData() { }

        public TileData(string label, TileKind kind = TileKind.Phrase, string text = null, string icon = null)
        {
            this.label = label;
            this.kind = kind;
            this.text = text;
            this.icon = icon;
        }
    }

    [Serializable]
    public class BoardCategory
    {
        public string name;

        [Tooltip("Lucide icon name shown on the category tile.")]
        public string icon;

        public CategoryRole role = CategoryRole.Social;

        [Tooltip("Grid columns for this category. Fewer columns means bigger tiles.")]
        [Range(2, 8)] public int columns = 4;

        [Tooltip("Grid rows for this category.")]
        [Range(2, 7)] public int rows = 3;

        public List<TileData> tiles = new List<TileData>();

        [Tooltip("If true, the controller fills this category with the settings page instead of these tiles.")]
        public bool isSettings;

        public BoardCategory() { }

        public BoardCategory(string name, int columns, int rows, CategoryRole role = CategoryRole.Social, string icon = null)
        {
            this.name = name;
            this.columns = columns;
            this.rows = rows;
            this.role = role;
            this.icon = icon;
        }
    }

    /// <summary>
    /// The content of the communication board: categories, each with a grid of tiles.
    /// Edit in the Inspector, or regenerate the default with GazeBoard > Create Default Board.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardDefinition", menuName = "GazeBoard/Board Definition")]
    public class BoardDefinition : ScriptableObject
    {
        public List<BoardCategory> categories = new List<BoardCategory>();

        /// <summary>Icon used for a phrase when the asset predates icons; keyed by label.</summary>
        private static readonly Dictionary<string, string> DefaultIcons = new Dictionary<string, string>
        {
            // Quick
            { "Hello", "hand" }, { "Goodbye", "door-open" }, { "Maybe", "circle-help" }, { "Thank you", "heart-handshake" },
            { "Please", "hand-heart" }, { "Stop", "ban" }, { "Wait", "hourglass" }, { "I don't know", "message-circle-question" },
            { "More", "plus" }, { "Again", "repeat" }, { "I'm done", "check-check" }, { "I need a break", "pause" },
            { "Yes", "check" }, { "No", "x" }, { "Help", "siren" },
            // Needs
            { "I need water", "glass-water" }, { "I'm hungry", "utensils" }, { "I'm in pain", "heart-pulse" }, { "Bathroom", "bath" },
            { "I'm tired", "bed" }, { "I'm cold", "snowflake" }, { "I'm hot", "flame" }, { "Medicine", "pill" },
            { "Call someone", "phone" }, { "I'm uncomfortable", "frown" }, { "Please move me", "move" }, { "Blanket", "layers" },
            // Feelings
            { "I'm happy", "smile" }, { "I'm sad", "frown" }, { "I'm scared", "ghost" }, { "I'm angry", "angry" },
            { "I'm okay", "thumbs-up" }, { "I'm bored", "meh" }, { "I'm excited", "party-popper" }, { "I'm lonely", "user" },
            { "I'm frustrated", "cloud-lightning" }, { "I'm calm", "leaf" }, { "I feel sick", "thermometer" }, { "I love you", "heart" },
            // People
            { "Mom", "user-round" }, { "Dad", "user" }, { "Nurse", "cross" }, { "Doctor", "stethoscope" },
            { "Friend", "users" }, { "Family", "house" }, { "Me", "person-standing" }, { "You", "hand" },
            { "Everyone", "users-round" }, { "Nobody", "user-x" }, { "Who is it?", "circle-help" }, { "Come here", "hand" },
            // Spelling
            { "Space", "space" }, { "Speak", "volume-2" },
        };

        private static readonly Dictionary<string, string> DefaultCategoryIcons = new Dictionary<string, string>
        {
            { "Mine", "star" }, { "Quick", "zap" }, { "Needs", "hand-helping" }, { "Feelings", "smile" },
            { "People", "users" }, { "Spell", "keyboard" }, { "Settings", "settings" },
        };

        private static readonly Dictionary<string, CategoryRole> DefaultCategoryRoles = new Dictionary<string, CategoryRole>
        {
            { "Mine", CategoryRole.Personal }, { "Quick", CategoryRole.Social }, { "Needs", CategoryRole.Needs },
            { "Feelings", CategoryRole.Feelings }, { "People", CategoryRole.People }, { "Spell", CategoryRole.Spelling },
        };

        public static string IconFor(TileData tile)
        {
            if (!string.IsNullOrEmpty(tile.icon)) return tile.icon;
            return DefaultIcons.TryGetValue(tile.label ?? "", out string icon) ? icon : null;
        }

        public static string IconFor(BoardCategory category)
        {
            if (!string.IsNullOrEmpty(category.icon)) return category.icon;
            return DefaultCategoryIcons.TryGetValue(category.name ?? "", out string icon) ? icon : null;
        }

        /// <summary>
        /// Older assets carry no role, so Quick, Needs, Feelings and People would all read as Social.
        /// Infer the role from the name in that case.
        /// </summary>
        public static CategoryRole RoleFor(BoardCategory category)
        {
            if (category.role != CategoryRole.Social) return category.role;
            return DefaultCategoryRoles.TryGetValue(category.name ?? "", out CategoryRole role) ? role : category.role;
        }

        /// <summary>Fills this asset with a sensible starter board for first-time use.</summary>
        public void PopulateDefaults()
        {
            categories.Clear();

            // Personal phrases and recent sentences; filled at runtime.
            categories.Add(new BoardCategory("Mine", 4, 3, CategoryRole.Personal, "star"));

            // Yes, No and Help live in the fixed row under the grid, so Quick holds the rest.
            var quick = new BoardCategory("Quick", 4, 3, CategoryRole.Social, "zap");
            foreach (string s in new[]
            {
                "Hello", "Goodbye", "Maybe", "Thank you",
                "Please", "Stop", "Wait", "I don't know",
                "More", "Again", "I'm done", "I need a break",
            }) quick.tiles.Add(Phrase(s));
            categories.Add(quick);

            var needs = new BoardCategory("Needs", 4, 3, CategoryRole.Needs, "hand-helping");
            foreach (string s in new[]
            {
                "I need water", "I'm hungry", "I'm in pain", "Bathroom",
                "I'm tired", "I'm cold", "I'm hot", "Medicine",
                "Call someone", "I'm uncomfortable", "Please move me", "Blanket",
            }) needs.tiles.Add(Phrase(s));
            categories.Add(needs);

            var feelings = new BoardCategory("Feelings", 4, 3, CategoryRole.Feelings, "smile");
            foreach (string s in new[]
            {
                "I'm happy", "I'm sad", "I'm scared", "I'm angry",
                "I'm okay", "I'm bored", "I'm excited", "I'm lonely",
                "I'm frustrated", "I'm calm", "I feel sick", "I love you",
            }) feelings.tiles.Add(Phrase(s));
            categories.Add(feelings);

            var people = new BoardCategory("People", 4, 3, CategoryRole.People, "users");
            foreach (string s in new[]
            {
                "Mom", "Dad", "Nurse", "Doctor",
                "Friend", "Family", "Me", "You",
                "Everyone", "Nobody", "Who is it?", "Come here",
            }) people.tiles.Add(Phrase(s));
            categories.Add(people);

            var letters = new BoardCategory("Spell", 6, 5, CategoryRole.Spelling, "keyboard");
            for (char c = 'A'; c <= 'Z'; c++)
            {
                letters.tiles.Add(new TileData(c.ToString(), TileKind.Letter, c.ToString().ToLowerInvariant()));
            }
            letters.tiles.Add(new TileData("Space", TileKind.Space, " ", "space"));
            letters.tiles.Add(new TileData("?", TileKind.Letter, "?"));
            letters.tiles.Add(new TileData(".", TileKind.Letter, "."));
            // Letter-level delete; the sentence bar's Undo removes whole words and Speak speaks.
            letters.tiles.Add(new TileData("Delete", TileKind.DeleteLetter, null, "delete"));
            categories.Add(letters);
        }

        private static TileData Phrase(string label) =>
            new TileData(label, TileKind.Phrase, null, DefaultIcons.TryGetValue(label, out string icon) ? icon : null);
    }
}
