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
    }

    [Serializable]
    public class TileData
    {
        [Tooltip("Text shown on the tile.")]
        public string label;

        [Tooltip("Text spoken or appended. Leave empty to use the label.")]
        public string text;

        public TileKind kind = TileKind.Phrase;

        public string Text => string.IsNullOrEmpty(text) ? label : text;

        public TileData() { }

        public TileData(string label, TileKind kind = TileKind.Phrase, string text = null)
        {
            this.label = label;
            this.kind = kind;
            this.text = text;
        }
    }

    [Serializable]
    public class BoardCategory
    {
        public string name;

        [Tooltip("Grid columns for this category. Fewer columns means bigger tiles.")]
        [Range(2, 8)] public int columns = 4;

        [Tooltip("Grid rows for this category.")]
        [Range(2, 7)] public int rows = 3;

        public List<TileData> tiles = new List<TileData>();

        [Tooltip("If true, the controller fills this category with the settings page instead of these tiles.")]
        public bool isSettings;

        public BoardCategory() { }

        public BoardCategory(string name, int columns, int rows)
        {
            this.name = name;
            this.columns = columns;
            this.rows = rows;
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

        /// <summary>Fills this asset with a sensible starter board for first-time use.</summary>
        public void PopulateDefaults()
        {
            categories.Clear();

            var quick = new BoardCategory("Quick", 4, 3);
            foreach (string s in new[]
            {
                "Yes", "No", "Maybe", "Help",
                "Thank you", "Please", "Stop", "Wait",
                "I don't know", "More", "Again", "I'm done",
            }) quick.tiles.Add(new TileData(s));
            categories.Add(quick);

            var needs = new BoardCategory("Needs", 4, 3);
            foreach (string s in new[]
            {
                "I need water", "I'm hungry", "I'm in pain", "Bathroom",
                "I'm tired", "I'm cold", "I'm hot", "Medicine",
                "Call someone", "I'm uncomfortable", "Please move me", "Blanket",
            }) needs.tiles.Add(new TileData(s));
            categories.Add(needs);

            var feelings = new BoardCategory("Feelings", 4, 3);
            foreach (string s in new[]
            {
                "I'm happy", "I'm sad", "I'm scared", "I'm angry",
                "I'm okay", "I'm bored", "I'm excited", "I'm lonely",
                "I'm frustrated", "I'm calm", "I feel sick", "I love you",
            }) feelings.tiles.Add(new TileData(s));
            categories.Add(feelings);

            var people = new BoardCategory("People", 4, 3);
            foreach (string s in new[]
            {
                "Mom", "Dad", "Nurse", "Doctor",
                "Friend", "Family", "Me", "You",
                "Everyone", "Nobody", "Who is it?", "Come here",
            }) people.tiles.Add(new TileData(s));
            categories.Add(people);

            var letters = new BoardCategory("Spell", 6, 5);
            for (char c = 'A'; c <= 'Z'; c++)
            {
                letters.tiles.Add(new TileData(c.ToString(), TileKind.Letter, c.ToString().ToLowerInvariant()));
            }
            letters.tiles.Add(new TileData("Space", TileKind.Space, " "));
            letters.tiles.Add(new TileData("?", TileKind.Letter, "?"));
            letters.tiles.Add(new TileData(".", TileKind.Letter, "."));
            letters.tiles.Add(new TileData("Speak", TileKind.SpeakSentence));
            categories.Add(letters);
        }
    }
}
