using System.Collections.Generic;
using GazeBoard.Environment;
using GazeBoard.Speech;
using GazeBoard.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.Board
{
    /// <summary>
    /// Builds the live board from a BoardDefinition: category tiles down the side, a paged grid
    /// of phrase tiles in the middle, and the sentence bar across the top. All references are
    /// wired by the GazeBoard > Build Board UI editor command.
    /// </summary>
    public class BoardController : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private BoardDefinition _definition;

        [Header("Wiring (set by Build Board UI)")]
        [SerializeField] private BoardTile _tileTemplate;
        [SerializeField] private RectTransform _tileGrid;
        [SerializeField] private GridLayoutGroup _tileGridLayout;
        [SerializeField] private RectTransform _categoryBar;
        [SerializeField] private GridLayoutGroup _categoryGridLayout;
        [SerializeField] private TMP_Text _sentenceText;
        [SerializeField] private BoardTile _speakTile;
        [SerializeField] private BoardTile _backspaceTile;
        [SerializeField] private BoardTile _clearTile;
        [SerializeField] private Image _boardBackground;
        [SerializeField] private Image _sentenceBox;

        private ThemeManager _themes;
        private BoardTheme Theme => _themes != null ? _themes.Current : _fallbackTheme;
        private static BoardTheme _fallbackTheme;

        [Header("Dwell")]
        [SerializeField, Tooltip("Category tiles take this many times the global dwell, so a drifting gaze does not switch pages.")]
        private float _categoryDwellMultiplier = 1.6f;

        private readonly List<BoardTile> _gridTiles = new List<BoardTile>();
        private readonly List<BoardTile> _categoryTiles = new List<BoardTile>();
        private readonly SentenceBuilder _sentence = new SentenceBuilder();
        private int _categoryIndex = -1;
        private int _page;

        public BoardDefinition Definition => _definition;
        public SentenceBuilder Sentence => _sentence;

        private void Start()
        {
            if (_definition == null || _tileTemplate == null)
            {
                Debug.LogError("[GazeBoard] BoardController is missing its definition or tile template. Run GazeBoard > Build Board UI.");
                return;
            }

            _tileTemplate.gameObject.SetActive(false);
            EnsureSettingsCategory();
            _sentence.WhenChanged += text => { if (_sentenceText != null) _sentenceText.text = text; };
            if (_sentenceText != null) _sentenceText.text = "";

            _themes = GetComponent<ThemeManager>();
            if (_themes == null) _fallbackTheme ??= BoardTheme.Defaults()[0];
            else _themes.WhenChanged += HandleThemeChanged;

            BindActionTile(_speakTile, new TileData("Speak", TileKind.SpeakSentence));
            BindActionTile(_backspaceTile, new TileData("Undo", TileKind.Backspace));
            BindActionTile(_clearTile, new TileData("Clear", TileKind.Clear));

            ApplyBoardTheme();
            BuildCategoryBar();
            ShowCategory(0);
        }

        private void OnDestroy()
        {
            if (_themes != null) _themes.WhenChanged -= HandleThemeChanged;
        }

        private void HandleThemeChanged(BoardTheme theme) => Repaint();

        /// <summary>Re-applies the current theme to everything on the board.</summary>
        private void Repaint()
        {
            ApplyBoardTheme();
            BindActionTile(_speakTile, new TileData("Speak", TileKind.SpeakSentence));
            BindActionTile(_backspaceTile, new TileData("Undo", TileKind.Backspace));
            BindActionTile(_clearTile, new TileData("Clear", TileKind.Clear));
            BuildCategoryBar();
            ShowCategory(_categoryIndex);
        }

        private void ApplyBoardTheme()
        {
            if (_boardBackground != null) _boardBackground.color = Theme.boardBackground;
            if (_sentenceBox != null) _sentenceBox.color = Theme.sentenceBox;
            if (_sentenceText != null) _sentenceText.color = Theme.sentenceText;
        }

        private void BindActionTile(BoardTile tile, TileData data)
        {
            if (tile == null) return;
            tile.Bind(data, OnTileSelected);
            tile.ApplyTheme(Theme, Theme.actionTile);
        }

        private void BuildCategoryBar()
        {
            foreach (BoardTile t in _categoryTiles) Destroy(t.gameObject);
            _categoryTiles.Clear();
            if (_categoryBar == null) return;

            // Category tiles use exactly the cell size a standard 4x3 phrase page gets, so every
            // square on the board is the same size.
            int count = _definition.categories.Count;
            if (_categoryGridLayout != null)
            {
                Vector2 phraseCell = ComputeGridCell(4, 3);
                float side = Mathf.Floor(Mathf.Min(phraseCell.x, phraseCell.y));
                _categoryGridLayout.cellSize = new Vector2(side, side);
            }

            for (int i = 0; i < count; i++)
            {
                int index = i;
                BoardTile tile = Instantiate(_tileTemplate, _categoryBar);
                tile.Bind(new TileData(_definition.categories[i].name), _ => ShowCategory(index));
                var dwell = tile.GetComponent<DwellSelectable>();
                if (dwell != null) dwell.DwellMultiplier = _categoryDwellMultiplier;
                _categoryTiles.Add(tile);
            }
        }

        public void ShowCategory(int index)
        {
            if (_definition.categories.Count == 0) return;
            index = Mathf.Clamp(index, 0, _definition.categories.Count - 1);
            if (index != _categoryIndex) _page = 0;
            _categoryIndex = index;

            for (int i = 0; i < _categoryTiles.Count; i++)
            {
                bool active = i == _categoryIndex;
                _categoryTiles[i].ApplyTheme(Theme, active ? Theme.categoryActiveTile : Theme.categoryTile);
            }

            RebuildGrid();
        }

        private void RebuildGrid()
        {
            foreach (BoardTile t in _gridTiles) Destroy(t.gameObject);
            _gridTiles.Clear();

            BoardCategory category = _definition.categories[_categoryIndex];
            if (category.isSettings)
            {
                ApplyGridLayout(4, 3);
                foreach (TileData data in BuildSettingsTiles()) AddGridTile(data);
                return;
            }

            int cells = category.columns * category.rows;
            ApplyGridLayout(category.columns, category.rows);

            int pageCount = Mathf.Max(1, Mathf.CeilToInt(category.tiles.Count / (float)cells));
            bool needsPaging = pageCount > 1;
            int perPage = needsPaging ? cells - 1 : cells;
            if (needsPaging) pageCount = Mathf.CeilToInt(category.tiles.Count / (float)perPage);
            if (_page >= pageCount || _page < 0) _page = 0; // "More" wraps around to the first page

            int start = _page * perPage;
            int end = Mathf.Min(category.tiles.Count, start + perPage);
            for (int i = start; i < end; i++)
            {
                AddGridTile(category.tiles[i]);
            }

            if (needsPaging)
            {
                AddGridTile(new TileData($"More ({_page + 1}/{pageCount})", TileKind.NextPage));
            }
        }

        private void ApplyGridLayout(int columns, int rows)
        {
            if (_tileGridLayout == null || _tileGrid == null) return;

            _tileGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _tileGridLayout.constraintCount = columns;
            _tileGridLayout.cellSize = ComputeGridCell(columns, rows);
        }

        /// <summary>Cell size that makes a columns-by-rows grid fill the tile grid area.</summary>
        private Vector2 ComputeGridCell(int columns, int rows)
        {
            if (_tileGridLayout == null || _tileGrid == null) return new Vector2(200f, 200f);
            Vector2 spacing = _tileGridLayout.spacing;
            RectOffset pad = _tileGridLayout.padding;
            float width = _tileGrid.rect.width - pad.horizontal - spacing.x * (columns - 1);
            float height = _tileGrid.rect.height - pad.vertical - spacing.y * (rows - 1);
            return new Vector2(Mathf.Floor(width / columns), Mathf.Floor(height / rows));
        }

        private void AddGridTile(TileData data)
        {
            BoardTile tile = Instantiate(_tileTemplate, _tileGrid);
            tile.Bind(data, OnTileSelected);
            Color c = data.kind switch
            {
                TileKind.Phrase => Theme.phraseTile,
                TileKind.Letter => Theme.letterTile,
                TileKind.Info => Theme.infoTile,
                _ => Theme.actionTile,
            };
            tile.ApplyTheme(Theme, c);

            if (data.kind == TileKind.Info) tile.SetInteractable(false);
            if (data.kind == TileKind.DwellToggle)
            {
                var dwell = tile.GetComponent<DwellSelectable>();
                if (dwell != null) dwell.AlwaysDwell = true;
            }

            _gridTiles.Add(tile);
        }

        // ---- Settings page ----

        private void EnsureSettingsCategory()
        {
            foreach (BoardCategory c in _definition.categories)
            {
                if (c.isSettings) return;
            }
            // Not saved to the asset; just present at runtime.
            _definition.categories.Add(new BoardCategory("Settings", 4, 3) { isSettings = true });
        }

        private IEnumerable<TileData> BuildSettingsTiles()
        {
            yield return new TileData($"Dwell\n{DwellSettings.DwellSeconds:0.0} s", TileKind.Info);
            yield return new TileData("Slower", TileKind.DwellSlower);
            yield return new TileData("Faster", TileKind.DwellFaster);
            yield return new TileData(DwellSettings.Enabled ? "Gaze select\nON" : "Gaze select\nOFF", TileKind.DwellToggle);

            yield return new TileData($"Voice\n{SpeechService.Rate:0.0}x", TileKind.Info);
            yield return new TileData("Slower", TileKind.VoiceSlower);
            yield return new TileData("Faster", TileKind.VoiceFaster);
            yield return new TileData("Test voice", TileKind.TestVoice);

            yield return new TileData(GazeSourceLabel(), TileKind.Info);
            yield return new TileData("Theme\n" + Theme.displayName, TileKind.Theme);
            var env = EnvironmentManager.Instance;
            yield return new TileData(env == null ? "Room\n(not set up)"
                : env.Mode == EnvironmentMode.Passthrough ? "View\nMy room" : "View\nCalm room", TileKind.Environment);
        }

        private static string GazeSourceLabel()
        {
            var fallback = FindFirstObjectByType<HeadGazeFallback>();
            if (fallback == null) return "Gaze\n?";
            return fallback.UsingHeadGaze ? "Gaze\nHead" : "Gaze\nEyes";
        }

        private void OnTileSelected(BoardTile tile)
        {
            TileData data = tile.Data;
            switch (data.kind)
            {
                case TileKind.Phrase:
                    SpeechService.Speak(data.Text);
                    _sentence.AppendPhrase(data.Text);
                    break;
                case TileKind.Letter:
                    _sentence.AppendRaw(data.Text);
                    break;
                case TileKind.Space:
                    _sentence.AppendRaw(" ");
                    break;
                case TileKind.SpeakSentence:
                    if (!_sentence.IsEmpty) SpeechService.Speak(_sentence.Text);
                    break;
                case TileKind.Backspace:
                    _sentence.Backspace();
                    break;
                case TileKind.Clear:
                    SpeechService.Stop();
                    _sentence.Clear();
                    break;
                case TileKind.NextPage:
                    _page++;
                    RebuildGrid();
                    break;

                // Settings. Each change refreshes the page so the value tiles update.
                case TileKind.DwellSlower:
                    DwellSettings.DwellSeconds += 0.2f;
                    SpeechService.Speak(SpokenPhrases.DwellChanged(DwellSettings.DwellSeconds));
                    RebuildGrid();
                    break;
                case TileKind.DwellFaster:
                    DwellSettings.DwellSeconds -= 0.2f;
                    SpeechService.Speak(SpokenPhrases.DwellChanged(DwellSettings.DwellSeconds));
                    RebuildGrid();
                    break;
                case TileKind.DwellToggle:
                    DwellSettings.Enabled = !DwellSettings.Enabled;
                    SpeechService.Speak(DwellSettings.Enabled ? SpokenPhrases.GazeSelectOn : SpokenPhrases.GazeSelectOff);
                    RebuildGrid();
                    break;
                case TileKind.VoiceSlower:
                    SpeechService.Rate -= 0.1f;
                    SpeechService.Speak(SpokenPhrases.VoiceSpeedChanged);
                    RebuildGrid();
                    break;
                case TileKind.VoiceFaster:
                    SpeechService.Rate += 0.1f;
                    SpeechService.Speak(SpokenPhrases.VoiceSpeedChanged);
                    RebuildGrid();
                    break;
                case TileKind.TestVoice:
                    SpeechService.Speak(SpokenPhrases.TestVoice);
                    break;
                case TileKind.Theme:
                    if (_themes != null)
                    {
                        BoardTheme next = _themes.Next(); // triggers Repaint via WhenChanged
                        SpeechService.Speak(next.spokenName);
                    }
                    break;
                case TileKind.Environment:
                    if (EnvironmentManager.Instance != null)
                    {
                        EnvironmentMode mode = EnvironmentManager.Instance.Toggle();
                        SpeechService.Speak(mode == EnvironmentMode.Passthrough ? SpokenPhrases.EnvironmentPassthrough : SpokenPhrases.EnvironmentRoom);
                        RebuildGrid();
                    }
                    break;
                case TileKind.Info:
                    break;
            }
        }
    }
}
