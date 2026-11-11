using GazeBoard.Board;
using UnityEngine;

namespace GazeBoard.Theme
{
    /// <summary>
    /// Everything that changes between visual themes. Created as assets, or built in code by
    /// BoardTheme.Defaults() when no assets are assigned. Tile colours follow a Fitzgerald-style
    /// key: each category role has its own hue so the board reads at a glance.
    /// </summary>
    [CreateAssetMenu(fileName = "BoardTheme", menuName = "GazeBoard/Board Theme")]
    public class BoardTheme : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Calm";
        [Tooltip("Spoken when the theme is selected.")]
        public string spokenName = "Calm theme";

        [Header("Board")]
        public Color boardBackground = new Color(0.09f, 0.10f, 0.13f, 0.92f);
        public Color panelShadow = new Color(0f, 0f, 0f, 0.45f);
        public Color sentenceBox = new Color(0.15f, 0.17f, 0.21f, 1f);
        public Color sentenceBoxSpeaking = new Color(0.17f, 0.30f, 0.26f, 1f);
        public Color sentenceText = Color.white;
        public Color gazeCursor = new Color(1f, 1f, 1f, 0.55f);

        // The Calm colours are chosen so white text reaches a contrast ratio of at least 4.5:1 on
        // every tile at rest and 3.5:1 while it is gazed (WCAG relative luminance). People is a
        // dark gold rather than a yellow for the same reason, so every category has white text.
        [Header("Tiles by role")]
        public Color socialTile = new Color(0.53f, 0.33f, 0.69f);
        public Color needsTile = new Color(0.70f, 0.38f, 0.14f);
        public Color feelingsTile = new Color(0.20f, 0.44f, 0.74f);
        public Color peopleTile = new Color(0.55f, 0.44f, 0.10f);
        public Color personalTile = new Color(0.13f, 0.47f, 0.44f);
        // A slate with some blue in it rather than a flat grey: a grey so close to the board
        // barely changed between the open and the resting category tile.
        public Color letterTile = new Color(0.36f, 0.41f, 0.50f);

        [Header("Tiles by action")]
        public Color actionTile = new Color(0.34f, 0.38f, 0.45f);
        public Color speakTile = new Color(0.13f, 0.48f, 0.31f);
        public Color yesTile = new Color(0.15f, 0.48f, 0.29f);
        public Color noTile = new Color(0.72f, 0.28f, 0.28f);
        public Color helpTile = new Color(0.74f, 0.29f, 0.16f);
        public Color infoTile = new Color(0.17f, 0.19f, 0.23f);
        public Color recentTile = new Color(0.26f, 0.40f, 0.42f);
        public Color tileText = Color.white;
        [Tooltip("Multiplier applied to a tile colour while gazed or hovered.")]
        public float highlightBrightness = 1.15f;

        [Header("Tile shape")]
        public bool roundedCorners = true;
        public Color outlineColor = new Color(1f, 1f, 1f, 0.10f);
        [Range(0f, 8f)] public float outlineWidth = 1.5f;
        [Tooltip("Outline of the selected category and the open settings tile.")]
        public Color activeOutlineColor = new Color(1f, 1f, 1f, 0.95f);
        [Range(0f, 8f)] public float activeOutlineWidth = 3f;

        [Header("Dwell")]
        public Color dwellRing = new Color(1f, 1f, 1f, 0.5f);

        public Color RoleColor(CategoryRole role) => role switch
        {
            CategoryRole.Social => socialTile,
            CategoryRole.Needs => needsTile,
            CategoryRole.Feelings => feelingsTile,
            CategoryRole.People => peopleTile,
            CategoryRole.Personal => personalTile,
            CategoryRole.Spelling => letterTile,
            _ => socialTile,
        };

        /// <summary>Text colour that stays readable on the given tile colour.</summary>
        public Color TextFor(Color tile)
        {
            // The theme's own text colour wherever it is readable enough; otherwise whichever of
            // near-black and white contrasts more with this tile.
            if (Contrast(tile, tileText) >= MinTextContrast) return tileText;
            var dark = new Color(0.08f, 0.08f, 0.08f);
            return Contrast(tile, dark) >= Contrast(tile, Color.white) ? dark : Color.white;
        }

        /// <summary>Contrast ratio the theme text must reach on a tile before another colour is used.</summary>
        public const float MinTextContrast = 4.5f;

        /// <summary>WCAG contrast ratio between two opaque colours, from 1 (same) to 21 (black on white).</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = Luminance(a), lb = Luminance(b);
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }

        /// <summary>WCAG relative luminance of an sRGB colour.</summary>
        public static float Luminance(Color c)
        {
            return 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);
        }

        private static float Linear(float channel) =>
            channel <= 0.04045f ? channel / 12.92f : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);

        [Tooltip("Added to a tile colour while gazed or hovered. Needed for black tiles, which a multiplier cannot brighten.")]
        public float highlightLift = 0f;

        [Tooltip("The open category's tile is lightened until it is at least this bright (WCAG relative luminance), which is bright enough to carry dark text. 0 leaves it at the gazed colour.")]
        public float openCategoryLuminance = 0.26f;

        /// <summary>
        /// Fill of the category that is open (and of the settings tile while settings are open).
        /// In a theme with light text it is bright enough that its label turns dark, so the open
        /// category stands apart by fill and by text, whatever its own colour is. A grey tile
        /// brightened only by the gaze multiplier stayed too dark for that.
        /// </summary>
        public Color OpenCategory(Color role)
        {
            Color c = Highlight(role);
            for (int i = 0; i < 25 && Luminance(c) < openCategoryLuminance; i++)
            {
                c = new Color(Mathf.Lerp(c.r, 1f, 0.04f), Mathf.Lerp(c.g, 1f, 0.04f), Mathf.Lerp(c.b, 1f, 0.04f), c.a);
            }
            return c;
        }

        public Color Highlight(Color c) => new Color(
            Mathf.Clamp01(c.r * highlightBrightness + highlightLift),
            Mathf.Clamp01(c.g * highlightBrightness + highlightLift),
            Mathf.Clamp01(c.b * highlightBrightness + highlightLift), c.a);

        /// <summary>Built-in themes used when no assets are assigned in ThemeManager.</summary>
        public static BoardTheme[] Defaults()
        {
            // Calm: dark glass panel in the style of Horizon OS, coloured tiles, white text.
            var calm = CreateInstance<BoardTheme>();
            calm.name = "Calm";
            calm.displayName = "Calm";
            calm.spokenName = "Calm theme";
            // Field defaults above are the calm theme.

            // Warm: cream panel, soft saturated tiles, dark text.
            var warm = CreateInstance<BoardTheme>();
            warm.name = "Warm";
            warm.displayName = "Warm";
            warm.spokenName = "Warm theme";
            warm.boardBackground = new Color(0.97f, 0.94f, 0.89f, 0.96f);
            warm.panelShadow = new Color(0.25f, 0.15f, 0.08f, 0.35f);
            warm.sentenceBox = new Color(1f, 0.99f, 0.96f, 1f);
            warm.sentenceBoxSpeaking = new Color(0.90f, 0.97f, 0.90f, 1f);
            warm.sentenceText = new Color(0.25f, 0.18f, 0.14f);
            warm.gazeCursor = new Color(0.25f, 0.18f, 0.14f, 0.5f);
            warm.socialTile = new Color(0.86f, 0.50f, 0.64f);
            warm.needsTile = new Color(0.95f, 0.60f, 0.36f);
            warm.feelingsTile = new Color(0.46f, 0.66f, 0.88f);
            warm.peopleTile = new Color(0.96f, 0.80f, 0.38f);
            warm.personalTile = new Color(0.48f, 0.74f, 0.64f);
            warm.letterTile = new Color(0.93f, 0.84f, 0.70f);
            warm.actionTile = new Color(0.84f, 0.78f, 0.70f);
            warm.speakTile = new Color(0.42f, 0.70f, 0.48f);
            warm.yesTile = new Color(0.48f, 0.74f, 0.48f);
            warm.noTile = new Color(0.92f, 0.52f, 0.48f);
            warm.helpTile = new Color(0.95f, 0.45f, 0.36f);
            warm.infoTile = new Color(0.92f, 0.88f, 0.82f);
            warm.recentTile = new Color(0.70f, 0.82f, 0.78f);
            warm.tileText = new Color(0.22f, 0.15f, 0.12f);
            warm.highlightBrightness = 1.10f;
            warm.openCategoryLuminance = 0f; // text is dark already
            warm.roundedCorners = true;
            warm.outlineColor = new Color(1f, 1f, 1f, 0.55f);
            warm.outlineWidth = 2f;
            warm.activeOutlineColor = new Color(0.25f, 0.18f, 0.14f, 0.9f);
            warm.activeOutlineWidth = 3f;
            warm.dwellRing = new Color(0.25f, 0.18f, 0.14f, 0.45f);

            // High contrast: black tiles, yellow text and outlines, square corners.
            var contrast = CreateInstance<BoardTheme>();
            contrast.name = "HighContrast";
            contrast.displayName = "High contrast";
            contrast.spokenName = "High contrast theme";
            Color black = Color.black;
            Color yellow = new Color(1f, 1f, 0.4f);
            contrast.boardBackground = new Color(0f, 0f, 0f, 1f);
            contrast.panelShadow = new Color(0f, 0f, 0f, 0f);
            contrast.sentenceBox = black;
            contrast.sentenceBoxSpeaking = new Color(0.25f, 0.25f, 0.05f);
            contrast.sentenceText = yellow;
            contrast.gazeCursor = new Color(1f, 1f, 1f, 0.9f);
            contrast.socialTile = black;
            contrast.needsTile = black;
            contrast.feelingsTile = black;
            contrast.peopleTile = black;
            contrast.personalTile = black;
            contrast.letterTile = black;
            contrast.actionTile = new Color(0.10f, 0.10f, 0.10f);
            contrast.speakTile = new Color(0.10f, 0.10f, 0.10f);
            contrast.yesTile = black;
            contrast.noTile = black;
            contrast.helpTile = black;
            contrast.infoTile = new Color(0.08f, 0.08f, 0.08f);
            contrast.recentTile = new Color(0.08f, 0.08f, 0.08f);
            contrast.tileText = yellow;
            // Pure black times any multiplier stays black, so the gazed tile is lifted to grey instead.
            contrast.highlightBrightness = 1f;
            contrast.highlightLift = 0.30f;
            contrast.openCategoryLuminance = 0f; // the white outline marks the open category
            contrast.roundedCorners = false;
            contrast.outlineColor = new Color(1f, 1f, 0.4f, 1f);
            contrast.outlineWidth = 5f;
            contrast.activeOutlineColor = Color.white;
            contrast.activeOutlineWidth = 8f;
            contrast.dwellRing = new Color(1f, 1f, 1f, 0.75f);

            return new[] { calm, warm, contrast };
        }
    }
}
