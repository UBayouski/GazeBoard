using UnityEngine;

namespace GazeBoard.Theme
{
    /// <summary>
    /// Everything that changes between visual themes. Created as assets, or built in code by
    /// BoardTheme.Defaults() when no assets are assigned.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardTheme", menuName = "GazeBoard/Board Theme")]
    public class BoardTheme : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Calm";
        [Tooltip("Spoken when the theme is selected.")]
        public string spokenName = "Calm theme";

        [Header("Board")]
        public Color boardBackground = new Color(0.10f, 0.12f, 0.15f, 0.92f);
        public Color sentenceBox = new Color(0.16f, 0.19f, 0.23f, 1f);
        public Color sentenceText = Color.white;

        [Header("Tiles")]
        public Color phraseTile = new Color(0.20f, 0.42f, 0.62f);
        public Color letterTile = new Color(0.26f, 0.30f, 0.36f);
        public Color actionTile = new Color(0.62f, 0.40f, 0.18f);
        public Color categoryTile = new Color(0.22f, 0.48f, 0.42f);
        public Color categoryActiveTile = new Color(0.30f, 0.70f, 0.58f);
        public Color infoTile = new Color(0.20f, 0.22f, 0.26f);
        public Color tileText = Color.white;
        [Tooltip("Multiplier applied to a tile colour while gazed or hovered.")]
        public float highlightBrightness = 1.3f;

        [Header("Tile shape")]
        public bool roundedCorners = false;
        public Color outlineColor = new Color(1f, 1f, 1f, 0f);
        [Range(0f, 8f)] public float outlineWidth = 0f;

        [Header("Dwell")]
        public Color dwellRing = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>Text colour that stays readable on the given tile colour.</summary>
        public Color TextFor(Color tile)
        {
            float luminance = 0.2126f * tile.r + 0.7152f * tile.g + 0.0722f * tile.b;
            float textLuminance = 0.2126f * tileText.r + 0.7152f * tileText.g + 0.0722f * tileText.b;
            bool clash = Mathf.Abs(luminance - textLuminance) < 0.35f;
            if (!clash) return tileText;
            return luminance > 0.5f ? new Color(0.08f, 0.08f, 0.08f) : Color.white;
        }

        public Color Highlight(Color c) => new Color(
            Mathf.Clamp01(c.r * highlightBrightness),
            Mathf.Clamp01(c.g * highlightBrightness),
            Mathf.Clamp01(c.b * highlightBrightness), c.a);

        /// <summary>Built-in themes used when no assets are assigned in ThemeManager.</summary>
        public static BoardTheme[] Defaults()
        {
            var calm = CreateInstance<BoardTheme>();
            calm.name = "Calm";
            calm.displayName = "Calm";
            calm.spokenName = "Calm theme";
            // Defaults above are the calm theme.

            var warm = CreateInstance<BoardTheme>();
            warm.name = "Warm";
            warm.displayName = "Warm";
            warm.spokenName = "Warm theme";
            warm.boardBackground = new Color(0.97f, 0.94f, 0.89f, 0.96f);
            warm.sentenceBox = new Color(1f, 0.99f, 0.96f, 1f);
            warm.sentenceText = new Color(0.25f, 0.18f, 0.14f);
            warm.phraseTile = new Color(0.94f, 0.55f, 0.42f);
            warm.letterTile = new Color(0.93f, 0.80f, 0.62f);
            warm.actionTile = new Color(0.98f, 0.73f, 0.30f);
            warm.categoryTile = new Color(0.55f, 0.74f, 0.60f);
            warm.categoryActiveTile = new Color(0.36f, 0.62f, 0.46f);
            warm.infoTile = new Color(0.90f, 0.86f, 0.80f);
            warm.tileText = new Color(0.22f, 0.15f, 0.12f);
            warm.highlightBrightness = 1.12f;
            warm.roundedCorners = true;
            warm.outlineColor = new Color(1f, 1f, 1f, 0.6f);
            warm.outlineWidth = 2f;
            warm.dwellRing = new Color(1f, 1f, 1f, 0.7f);

            var contrast = CreateInstance<BoardTheme>();
            contrast.name = "HighContrast";
            contrast.displayName = "High contrast";
            contrast.spokenName = "High contrast theme";
            contrast.boardBackground = new Color(0f, 0f, 0f, 1f);
            contrast.sentenceBox = new Color(0f, 0f, 0f, 1f);
            contrast.sentenceText = new Color(1f, 1f, 0.4f);
            contrast.phraseTile = new Color(0f, 0f, 0f);
            contrast.letterTile = new Color(0f, 0f, 0f);
            contrast.actionTile = new Color(0.10f, 0.10f, 0.10f);
            contrast.categoryTile = new Color(0f, 0f, 0f);
            contrast.categoryActiveTile = new Color(1f, 1f, 0.4f);
            contrast.infoTile = new Color(0.08f, 0.08f, 0.08f);
            contrast.tileText = new Color(1f, 1f, 0.4f);
            contrast.highlightBrightness = 3f; // black tiles brighten to grey when gazed
            contrast.roundedCorners = false;
            contrast.outlineColor = new Color(1f, 1f, 0.4f, 1f);
            contrast.outlineWidth = 6f;
            contrast.dwellRing = new Color(1f, 1f, 1f, 0.9f);

            return new[] { calm, warm, contrast };
        }
    }
}
