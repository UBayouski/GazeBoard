using System.Collections.Generic;
using GazeBoard.Environment;
using GazeBoard.Notify;
using GazeBoard.Speech;
using GazeBoard.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.Board
{
    /// <summary>
    /// Builds the live board from a BoardDefinition: category tiles down the side, a paged grid
    /// of phrase tiles in the middle, the sentence bar across the top and the fixed quick row
    /// (Yes, No, Help, Repeat, Save) along the bottom. In the Spell category a suggestion bar
    /// above the grid predicts the word being typed. All references are wired by the
    /// GazeBoard > Build Board UI editor command.
    /// </summary>
    public class BoardController : MonoBehaviour
    {
        private const int SuggestionCount = 3;

        [Header("Content")]
        [SerializeField] private BoardDefinition _definition;

        [Header("Wiring (set by Build Board UI)")]
        [SerializeField] private BoardTile _tileTemplate;
        [SerializeField] private RectTransform _tileGrid;
        [SerializeField] private GridLayoutGroup _tileGridLayout;
        [SerializeField] private RectTransform _categoryBar;
        [SerializeField] private GridLayoutGroup _categoryGridLayout;
        [SerializeField] private RectTransform _suggestionBar;
        [SerializeField] private float _suggestionBarHeight = 90f;
        [SerializeField] private TMP_Text _sentenceText;
        [SerializeField] private BoardTile _speakTile;
        [SerializeField] private BoardTile _backspaceTile;
        [SerializeField] private BoardTile _clearTile;
        [SerializeField] private BoardTile _settingsTile;
        [SerializeField] private BoardTile _yesTile;
        [SerializeField] private BoardTile _noTile;
        [SerializeField] private BoardTile _helpTile;
        [SerializeField] private BoardTile _repeatTile;
        [SerializeField] private BoardTile _recentTile;
        [SerializeField] private BoardTile _saveTile;
        [SerializeField] private Image _boardBackground;
        [SerializeField] private Image _panelShadow;
        [SerializeField] private Image _sentenceBox;
        [SerializeField] private GazeCursor _gazeCursor;

        private ThemeManager _themes;
        private BoardTheme Theme => _themes != null ? _themes.Current : _fallbackTheme;
        private static BoardTheme _fallbackTheme;

        [Header("Dwell")]
        [SerializeField, Tooltip("Category and settings tiles take this many times the global dwell, so a drifting gaze does not switch pages.")]
        private float _categoryDwellMultiplier = 1.6f;
        [SerializeField, Tooltip("Help, Clear and Save take this many times the global dwell; they are costly to hit by accident.")]
        private float _carefulDwellMultiplier = 1.4f;

        private readonly List<BoardTile> _gridTiles = new List<BoardTile>();
        private readonly List<BoardTile> _categoryTiles = new List<BoardTile>();
        private readonly List<BoardTile> _suggestionTiles = new List<BoardTile>();
        private readonly SentenceBuilder _sentence = new SentenceBuilder();
        private WordPredictor _predictor;
        private UserPhrases _userPhrases;
        private readonly HashSet<string> _bakedPhrases = new HashSet<string>();

        /// <summary>Speaks user content: baked clip when the text is a board phrase, per-user cache otherwise.</summary>
        private void SpeakUser(string text) => SpeechService.Speak(text, true, userText: !_bakedPhrases.Contains(text));

        /// <summary>
        /// Speaks the sentence bar. Online, the whole sentence streams as one natural utterance.
        /// Offline, no clip exists for a new combination, so the sentence is split back into the
        /// baked phrases it was built from and those play back to back. Spelled words have no
        /// offline clip and are skipped.
        /// </summary>
        private void SpeakSentence(string text)
        {
            bool offline = Application.internetReachability == NetworkReachability.NotReachable;
            if (!offline || _bakedPhrases.Contains(text))
            {
                SpeakUser(text);
                return;
            }

            List<string> parts = SplitIntoBakedPhrases(text);
            if (parts.Count == 0)
            {
                Debug.Log("[GazeBoard] Offline and no baked phrase in the sentence; nothing to play.");
                return;
            }
            SpeechService.SpeakParts(parts);
        }

        /// <summary>Greedy longest match of the sentence's words against the baked phrases.</summary>
        private List<string> SplitIntoBakedPhrases(string text)
        {
            var parts = new List<string>();
            string[] words = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            while (i < words.Length)
            {
                string found = null;
                for (int len = words.Length - i; len >= 1; len--)
                {
                    string candidate = string.Join(" ", words, i, len);
                    if (_bakedPhrases.Contains(candidate)) { found = candidate; i += len; break; }
                }
                if (found != null) parts.Add(found);
                else i++; // a spelled word: no offline clip
            }
            return parts;
        }
        private int _categoryIndex = -1;
        private int _page;
        private float _gridTopOffset;
        private float _gridFullHeight;

        private enum PersonalView { Saved, Recent }
        private PersonalView _personalView = PersonalView.Saved;
        private bool _editMode;

        public BoardDefinition Definition => _definition;
        public SentenceBuilder Sentence => _sentence;

        private BoardCategory Current => _categoryIndex >= 0 ? _definition.categories[_categoryIndex] : null;

        private void Start()
        {
            if (_definition == null || _tileTemplate == null)
            {
                Debug.LogError("[GazeBoard] BoardController is missing its definition or tile template. Run GazeBoard > Build Board UI.");
                return;
            }

            _tileTemplate.gameObject.SetActive(false);
            EnsurePersonalCategory();
            EnsureSettingsCategory();

            _predictor = new WordPredictor();
            _userPhrases = new UserPhrases();

            // Everything the preload step bakes into the APK. A recent or saved sentence that
            // matches one of these must play from the baked clip, or it would fail offline.
            foreach (BoardCategory category in _definition.categories)
            {
                foreach (TileData tile in category.tiles)
                {
                    if (tile.kind == TileKind.Phrase) _bakedPhrases.Add(tile.Text);
                }
            }
            foreach (string s in SpokenPhrases.All()) _bakedPhrases.Add(s);

            if (_tileGrid != null)
            {
                _gridTopOffset = _tileGrid.offsetMax.y;
                _gridFullHeight = _tileGrid.rect.height;
            }

            _sentence.WhenChanged += HandleSentenceChanged;
            if (_sentenceText != null) _sentenceText.text = "";

            _themes = GetComponent<ThemeManager>();
            if (_themes == null) _fallbackTheme ??= BoardTheme.Defaults()[0];
            else _themes.WhenChanged += HandleThemeChanged;

            BindFixedTiles();
            ApplyBoardTheme();
            BuildCategoryBar();
            ShowCategory(StartCategory());
        }

        private void OnDestroy()
        {
            if (_themes != null) _themes.WhenChanged -= HandleThemeChanged;
        }

        private void Update()
        {
            if (_themes == null && _fallbackTheme == null) return;

            // The alert stops by itself after its repeat limit; put the Help tile back.
            if (_helpShowsStop && !SpeechService.IsAlerting) BindHelpTile();

            // While the alert runs, the Stop tile pulses slowly so it reads as "alarm active".
            if (_helpTile != null && _helpTile.Background != null)
            {
                float pulse = _helpShowsStop ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
                _helpTile.Background.color = new Color(pulse, pulse, pulse, 1f);
            }

            // The sentence box glows while the voice is speaking so the user sees the output happen.
            if (_sentenceBox == null) return;
            Color target = SpeechService.IsSpeaking ? Theme.sentenceBoxSpeaking : Theme.sentenceBox;
            _sentenceBox.color = Color.Lerp(_sentenceBox.color, target, Time.unscaledDeltaTime * 8f);
        }

        private int StartCategory()
        {
            for (int i = 0; i < _definition.categories.Count; i++)
            {
                BoardCategory c = _definition.categories[i];
                if (c.isSettings) continue;
                if (BoardDefinition.RoleFor(c) == CategoryRole.Personal && _userPhrases.IsEmpty) continue;
                return i;
            }
            return 0;
        }

        private void HandleThemeChanged(BoardTheme theme) => Repaint();

        private void HandleSentenceChanged(string text)
        {
            if (_sentenceText != null) _sentenceText.text = text;
            if (Current != null && !Current.isSettings && BoardDefinition.RoleFor(Current) == CategoryRole.Spelling)
            {
                RefreshSuggestions();
            }
        }

        /// <summary>Re-applies the current theme to everything on the board.</summary>
        private void Repaint()
        {
            ApplyBoardTheme();
            BindFixedTiles();
            BuildCategoryBar();
            ShowCategory(_categoryIndex);
        }

        private void ApplyBoardTheme()
        {
            if (_boardBackground != null) _boardBackground.color = Theme.boardBackground;
            if (_panelShadow != null) _panelShadow.color = Theme.panelShadow;
            if (_sentenceBox != null) _sentenceBox.color = Theme.sentenceBox;
            if (_sentenceText != null) _sentenceText.color = Theme.sentenceText;
            if (_gazeCursor != null) _gazeCursor.SetColor(Theme.gazeCursor);
        }

        private void BindFixedTiles()
        {
            BindActionTile(_speakTile, new TileData("Speak", TileKind.SpeakSentence, null, "volume-2"), Theme.speakTile);
            BindActionTile(_backspaceTile, new TileData("Undo", TileKind.Backspace, null, "delete"), Theme.actionTile);
            BindActionTile(_clearTile, new TileData("Clear", TileKind.Clear, null, "trash-2"), Theme.actionTile, _carefulDwellMultiplier);
            BindActionTile(_settingsTile, new TileData("Settings", TileKind.Settings, null, "settings"), Theme.actionTile, _categoryDwellMultiplier);

            BindActionTile(_yesTile, new TileData("Yes", TileKind.Phrase, SpokenPhrases.Yes, "check"), Theme.yesTile);
            BindActionTile(_noTile, new TileData("No", TileKind.Phrase, SpokenPhrases.No, "x"), Theme.noTile);
            BindHelpTile();
            BindActionTile(_repeatTile, new TileData("Repeat", TileKind.Repeat, null, "repeat"), Theme.actionTile);
            BindActionTile(_recentTile, new TileData("Recent", TileKind.ShowRecent, null, "history"), Theme.recentTile);
            BindActionTile(_saveTile, new TileData("Save", TileKind.SavePhrase, null, "bookmark-plus"), Theme.personalTile, _carefulDwellMultiplier);
        }

        private bool _helpShowsStop;

        /// <summary>Help starts a repeating alert; while it runs the same tile reads Stop.</summary>
        private void BindHelpTile()
        {
            _helpShowsStop = SpeechService.IsAlerting;
            if (_helpShowsStop)
            {
                BindActionTile(_helpTile, new TileData("Stop", TileKind.Help, SpokenPhrases.HelpCall, "siren"), Theme.noTile);
            }
            else
            {
                BindActionTile(_helpTile, new TileData("Help", TileKind.Help, SpokenPhrases.HelpCall, "siren"), Theme.helpTile, _carefulDwellMultiplier);
            }
        }

        private void BindActionTile(BoardTile tile, TileData data, Color color, float dwellMultiplier = 1f)
        {
            if (tile == null) return;
            tile.Bind(data, OnTileSelected);
            tile.ApplyTheme(Theme, color);
            var dwell = tile.GetComponent<DwellSelectable>();
            if (dwell != null) dwell.DwellMultiplier = dwellMultiplier;
        }

        private void BuildCategoryBar()
        {
            foreach (BoardTile t in _categoryTiles) Destroy(t.gameObject);
            _categoryTiles.Clear();
            if (_categoryBar == null) return;

            // Category tiles use exactly the cell size a standard 4x3 phrase page gets, so every
            // square on the board is the same size.
            if (_categoryGridLayout != null)
            {
                Vector2 phraseCell = ComputeGridCell(4, 3, _gridFullHeight);
                float side = Mathf.Floor(Mathf.Min(phraseCell.x, phraseCell.y));
                _categoryGridLayout.cellSize = new Vector2(side, side);
            }

            for (int i = 0; i < _definition.categories.Count; i++)
            {
                BoardCategory category = _definition.categories[i];
                if (category.isSettings) continue; // settings open from the gear tile in the sentence bar

                int index = i;
                BoardTile tile = Instantiate(_tileTemplate, _categoryBar);
                tile.Bind(new TileData(category.name, TileKind.Phrase, null, BoardDefinition.IconFor(category)), _ => ShowCategory(index, resetView: true));
                var dwell = tile.GetComponent<DwellSelectable>();
                if (dwell != null) dwell.DwellMultiplier = _categoryDwellMultiplier;
                _categoryTiles.Add(tile);
            }
        }

        public void ShowCategory(int index, bool resetView = false)
        {
            if (_definition.categories.Count == 0) return;
            index = Mathf.Clamp(index, 0, _definition.categories.Count - 1);
            if (index != _categoryIndex || resetView)
            {
                _page = 0;
                _editMode = false;
                _personalView = PersonalView.Saved;
            }
            _categoryIndex = index;
            BoardCategory current = Current;

            // Highlight the open category (or the gear while settings are open).
            int tileIndex = 0;
            for (int i = 0; i < _definition.categories.Count; i++)
            {
                BoardCategory category = _definition.categories[i];
                if (category.isSettings) continue;
                if (tileIndex >= _categoryTiles.Count) break;
                bool active = i == _categoryIndex;
                Color role = Theme.RoleColor(BoardDefinition.RoleFor(category));
                _categoryTiles[tileIndex].ApplyTheme(Theme, active ? Theme.Highlight(role) : role, active);
                tileIndex++;
            }
            if (_settingsTile != null)
            {
                _settingsTile.ApplyTheme(Theme, current.isSettings ? Theme.Highlight(Theme.actionTile) : Theme.actionTile, current.isSettings);
            }

            SetSuggestionBarVisible(!current.isSettings && BoardDefinition.RoleFor(current) == CategoryRole.Spelling);
            RebuildGrid();
        }

        private void SetSuggestionBarVisible(bool visible)
        {
            if (_suggestionBar == null || _tileGrid == null) return;
            _suggestionBar.gameObject.SetActive(visible);
            float spacing = _tileGridLayout != null ? _tileGridLayout.spacing.y : 0f;
            float top = _gridTopOffset - (visible ? _suggestionBarHeight + spacing : 0f);
            _tileGrid.offsetMax = new Vector2(_tileGrid.offsetMax.x, top);
            if (visible) RefreshSuggestions();
        }

        private void RefreshSuggestions()
        {
            foreach (BoardTile t in _suggestionTiles) Destroy(t.gameObject);
            _suggestionTiles.Clear();
            if (_suggestionBar == null || !_suggestionBar.gameObject.activeSelf) return;

            List<string> words = _predictor.Suggest(_sentence.CurrentWord, SuggestionCount);

            // Always the same number of slots so the targets never shift under the gaze.
            for (int i = 0; i < SuggestionCount; i++)
            {
                BoardTile tile = Instantiate(_tileTemplate, _suggestionBar);
                bool filled = i < words.Count;
                tile.Bind(new TileData(filled ? words[i] : "", TileKind.Suggestion, filled ? words[i] : ""), OnTileSelected);
                tile.ApplyTheme(Theme, Theme.actionTile);
                var group = tile.GetComponent<CanvasGroup>();
                if (group == null) group = tile.gameObject.AddComponent<CanvasGroup>();
                group.alpha = filled ? 1f : 0f;
                group.blocksRaycasts = filled;
                tile.SetInteractable(filled);
                _suggestionTiles.Add(tile);
            }
        }

        private void RebuildGrid()
        {
            foreach (BoardTile t in _gridTiles) Destroy(t.gameObject);
            _gridTiles.Clear();

            BoardCategory category = Current;
            List<TileData> tiles = category.tiles;
            var tail = new List<TileData>(); // action tiles that stay on every page (Edit)
            if (category.isSettings) tiles = new List<TileData>(BuildSettingsTiles()); // paged like any category
            else if (BoardDefinition.RoleFor(category) == CategoryRole.Personal) tiles = BuildPersonalTiles(tail);

            int cells = category.columns * category.rows;
            ApplyGridLayout(category.columns, category.rows);

            int free = cells - tail.Count;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(tiles.Count / (float)free));
            bool needsPaging = pageCount > 1;
            int perPage = needsPaging ? free - 1 : free;
            if (needsPaging) pageCount = Mathf.CeilToInt(tiles.Count / (float)perPage);
            if (_page >= pageCount || _page < 0) _page = 0; // "More" wraps around to the first page

            int start = _page * perPage;
            int end = Mathf.Min(tiles.Count, start + perPage);
            for (int i = start; i < end; i++)
            {
                AddGridTile(tiles[i], category);
            }

            if (needsPaging)
            {
                // Pad so the tail tiles keep their place at the end of the grid on every page.
                for (int i = end - start; i < perPage; i++) AddGridTile(new TileData("", TileKind.Info), category, placeholder: true);
                // The last page wraps around to the first, so its tile points back.
                bool last = _page == pageCount - 1;
                AddGridTile(new TileData(last ? $"Back ({_page + 1}/{pageCount})" : $"More ({_page + 1}/{pageCount})",
                    TileKind.NextPage, null, last ? "arrow-left" : "arrow-right"), category);
            }
            else if (tail.Count > 0)
            {
                for (int i = tiles.Count; i < free; i++) AddGridTile(new TileData("", TileKind.Info), category, placeholder: true);
            }

            foreach (TileData data in tail) AddGridTile(data, category);
        }

        private void ApplyGridLayout(int columns, int rows)
        {
            if (_tileGridLayout == null || _tileGrid == null) return;

            _tileGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _tileGridLayout.constraintCount = columns;
            _tileGridLayout.cellSize = ComputeGridCell(columns, rows, _tileGrid.rect.height);
        }

        /// <summary>Cell size that makes a columns-by-rows grid fill the tile grid area.</summary>
        private Vector2 ComputeGridCell(int columns, int rows, float availableHeight)
        {
            if (_tileGridLayout == null || _tileGrid == null) return new Vector2(200f, 200f);
            Vector2 spacing = _tileGridLayout.spacing;
            RectOffset pad = _tileGridLayout.padding;
            float width = _tileGrid.rect.width - pad.horizontal - spacing.x * (columns - 1);
            float height = availableHeight - pad.vertical - spacing.y * (rows - 1);
            return new Vector2(Mathf.Floor(width / columns), Mathf.Floor(height / rows));
        }

        private void AddGridTile(TileData data, BoardCategory category, bool placeholder = false)
        {
            BoardTile tile = Instantiate(_tileTemplate, _tileGrid);
            tile.Bind(data, OnTileSelected);
            _gridTiles.Add(tile);

            if (placeholder)
            {
                // Invisible filler that keeps the grid positions stable.
                var group = tile.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.blocksRaycasts = false;
                tile.SetInteractable(false);
                return;
            }

            Color c = data.kind switch
            {
                TileKind.Phrase => Theme.RoleColor(BoardDefinition.RoleFor(category)),
                TileKind.SavedPhrase => Theme.personalTile,
                TileKind.RecentPhrase => Theme.recentTile,
                TileKind.RemovePhrase => Theme.noTile,
                TileKind.Letter => Theme.letterTile,
                TileKind.Info => Theme.infoTile,
                TileKind.SpeakSentence => Theme.speakTile,
                _ => Theme.actionTile,
            };
            tile.ApplyTheme(Theme, c);

            if (data.kind == TileKind.Info) tile.SetInteractable(false);

            var dwell = tile.GetComponent<DwellSelectable>();
            if (dwell != null)
            {
                switch (data.kind)
                {
                    case TileKind.DwellToggle:
                        dwell.AlwaysDwell = true;
                        break;
                    case TileKind.DeleteLetter:
                        dwell.RepeatWhileHovered = true; // keep gazing to delete several letters
                        break;
                    case TileKind.EditPhrases:
                    case TileKind.RemovePhrase:
                        dwell.DwellMultiplier = _carefulDwellMultiplier;
                        break;
                }
            }
        }

        // ---- Personal page ----

        private void EnsurePersonalCategory()
        {
            foreach (BoardCategory c in _definition.categories)
            {
                if (BoardDefinition.RoleFor(c) == CategoryRole.Personal) return;
            }
            // Older board assets: add it at runtime, in front.
            _definition.categories.Insert(0, new BoardCategory("Mine", 4, 3, CategoryRole.Personal, "star"));
        }

        /// <summary>
        /// Mine: saved phrases, with Edit (remove mode) and Recent (sub-page of spoken sentences)
        /// as fixed tail tiles. In edit mode every saved phrase becomes a red remove tile.
        /// </summary>
        private List<TileData> BuildPersonalTiles(List<TileData> tail)
        {
            var tiles = new List<TileData>();

            if (_personalView == PersonalView.Recent)
            {
                foreach (string phrase in _userPhrases.Recent) tiles.Add(new TileData(phrase, TileKind.RecentPhrase, phrase, "history"));
                if (tiles.Count == 0) tiles.Add(new TileData("Nothing spoken yet", TileKind.Info, null, "history"));
                return tiles; // the Mine category tile returns to the saved phrases
            }

            if (_userPhrases.Saved.Count == 0) _editMode = false;
            foreach (string phrase in _userPhrases.Saved)
            {
                tiles.Add(_editMode
                    ? new TileData(phrase, TileKind.RemovePhrase, phrase, "trash-2")
                    : new TileData(phrase, TileKind.SavedPhrase, phrase, "bookmark"));
            }
            if (tiles.Count == 0) tiles.Add(new TileData(SpokenPhrases.MineEmptyHint, TileKind.Info, null, "bookmark-plus"));

            if (_userPhrases.Saved.Count > 0)
            {
                tail.Add(_editMode
                    ? new TileData("Done", TileKind.EditPhrases, null, "check")
                    : new TileData("Edit", TileKind.EditPhrases, null, "square-pen"));
            }
            return tiles;
        }

        // ---- Settings page ----

        private void EnsureSettingsCategory()
        {
            foreach (BoardCategory c in _definition.categories)
            {
                if (c.isSettings) return;
            }
            // Not saved to the asset; just present at runtime.
            _definition.categories.Add(new BoardCategory("Settings", 4, 3, CategoryRole.Social, "settings") { isSettings = true });
        }

        private int PersonalIndex()
        {
            for (int i = 0; i < _definition.categories.Count; i++)
            {
                if (!_definition.categories[i].isSettings && BoardDefinition.RoleFor(_definition.categories[i]) == CategoryRole.Personal) return i;
            }
            return 0;
        }

        private int SettingsIndex()
        {
            for (int i = 0; i < _definition.categories.Count; i++)
            {
                if (_definition.categories[i].isSettings) return i;
            }
            return -1;
        }

        private IEnumerable<TileData> BuildSettingsTiles()
        {
            yield return new TileData($"Dwell\n{DwellSettings.DwellSeconds:0.0} s", TileKind.Info, null, "timer");
            yield return new TileData("Slower", TileKind.DwellSlower, null, "minus");
            yield return new TileData("Faster", TileKind.DwellFaster, null, "plus");
            yield return new TileData(DwellSettings.Enabled ? "Gaze select\nON" : "Gaze select\nOFF", TileKind.DwellToggle, null,
                DwellSettings.Enabled ? "eye" : "eye-off");

            yield return new TileData($"Voice\n{SpeechService.Rate:0.0}x", TileKind.Info, null, "audio-lines");
            yield return new TileData("Slower", TileKind.VoiceSlower, null, "minus");
            yield return new TileData("Faster", TileKind.VoiceFaster, null, "plus");
            yield return new TileData("Test voice", TileKind.TestVoice, null, "volume-2");

            // Row header like Dwell and Voice above, so the row reads "Look: Theme, View".
            yield return new TileData("Look", TileKind.Info, null, "sparkles");
            yield return new TileData("Theme\n" + Theme.displayName, TileKind.Theme, null, "palette");
            var env = EnvironmentManager.Instance;
            yield return env == null
                ? new TileData("Room\n(not set up)", TileKind.Environment, null, "house")
                : env.Mode == EnvironmentMode.Passthrough
                    ? new TileData("View\nMy room", TileKind.Environment, null, "camera")
                    : new TileData("View\nCalm room", TileKind.Environment, null, "house");

            // Page 2: phone alerts through ntfy, as a row with its own header.
            yield return new TileData("Phone", TileKind.Info, null, "smartphone");
            yield return new TileData(PhoneNotifier.Enabled ? "Alerts\nON" : "Alerts\nOFF", TileKind.PhoneToggle, null,
                PhoneNotifier.Enabled ? "bell-ring" : "phone-off");
            yield return new TileData("Code\n" + PhoneNotifier.Code, TileKind.PhoneCode, null, "key-round");
            yield return new TileData("Test phone", TileKind.PhoneTest, null, "send");
        }

        private bool ShowingPersonal => Current != null && !Current.isSettings && BoardDefinition.RoleFor(Current) == CategoryRole.Personal;

        private void OnTileSelected(BoardTile tile)
        {
            TileData data = tile.Data;

            // A running help alert keeps going while the user says more; only Stop or its
            // repeat limit ends it.
            switch (data.kind)
            {
                case TileKind.Phrase:
                    SpeechService.Speak(data.Text);
                    PhoneNotifier.Say(data.Text);
                    _sentence.AppendPhrase(data.Text);
                    break;
                case TileKind.SavedPhrase:
                case TileKind.RecentPhrase:
                    SpeakUser(data.Text);
                    PhoneNotifier.Say(data.Text);
                    _sentence.AppendPhrase(data.Text);
                    break;
                case TileKind.Letter:
                    _sentence.AppendRaw(data.Text);
                    break;
                case TileKind.Space:
                    _sentence.AppendRaw(" ");
                    break;
                case TileKind.Suggestion:
                    if (!string.IsNullOrEmpty(data.Text)) _sentence.ReplaceCurrentWord(data.Text);
                    break;
                case TileKind.SpeakSentence:
                    if (!_sentence.IsEmpty)
                    {
                        string text = _sentence.Text;
                        SpeakSentence(text);
                        PhoneNotifier.Say(text);
                        _userPhrases.AddRecent(text);
                        _predictor.Learn(text);
                        if (ShowingPersonal) RebuildGrid();
                    }
                    break;
                case TileKind.Backspace:
                    _sentence.Backspace();
                    break;
                case TileKind.DeleteLetter:
                    _sentence.DeleteLastChar();
                    break;

                // Mine page.
                case TileKind.EditPhrases:
                    _editMode = !_editMode;
                    SpeechService.Speak(_editMode ? SpokenPhrases.EditPhrasesOn : SpokenPhrases.EditPhrasesOff);
                    RebuildGrid();
                    break;
                case TileKind.RemovePhrase:
                    _userPhrases.Forget(data.Text);
                    SpeechService.Speak(SpokenPhrases.PhraseRemoved);
                    RebuildGrid();
                    break;
                case TileKind.ShowRecent:
                    // From the quick row: open Mine first (which resets the view), then switch to recents.
                    ShowCategory(PersonalIndex());
                    _personalView = PersonalView.Recent;
                    _page = 0;
                    RebuildGrid();
                    break;
                case TileKind.BackToSaved:
                    _personalView = PersonalView.Saved;
                    _page = 0;
                    RebuildGrid();
                    break;
                case TileKind.Clear:
                    SpeechService.Stop();
                    _sentence.Clear();
                    break;
                case TileKind.NextPage:
                    _page++;
                    RebuildGrid();
                    break;

                // Fixed row.
                case TileKind.Help:
                    if (SpeechService.IsAlerting)
                    {
                        SpeechService.StopAlert();
                    }
                    else
                    {
                        SpeechService.StartAlert(data.Text, SpokenPhrases.HelpRepeat);
                        PhoneNotifier.Help(data.Text);
                    }
                    BindHelpTile();
                    break;
                case TileKind.Repeat:
                    if (!SpeechService.RepeatLast()) SpeechService.Speak(SpokenPhrases.NothingToRepeat);
                    break;
                case TileKind.SavePhrase:
                    if (_sentence.IsEmpty)
                    {
                        SpeechService.Speak(SpokenPhrases.NothingToSave);
                    }
                    else
                    {
                        _userPhrases.Save(_sentence.Text);
                        SpeechService.Speak(SpokenPhrases.PhraseSaved);
                        if (ShowingPersonal) RebuildGrid();
                    }
                    break;
                case TileKind.Settings:
                    ShowCategory(Current != null && Current.isSettings ? StartCategory() : SettingsIndex());
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
                case TileKind.PhoneToggle:
                    PhoneNotifier.Enabled = !PhoneNotifier.Enabled;
                    SpeechService.Speak(PhoneNotifier.Enabled ? SpokenPhrases.PhoneOn : SpokenPhrases.PhoneOff);
                    RebuildGrid();
                    break;
                case TileKind.PhoneCode:
                    SpeechService.Speak(PhoneNotifier.SpokenCode(), true, userText: true);
                    break;
                case TileKind.PhoneTest:
                    PhoneNotifier.SendTest(ok => SpeechService.Speak(ok ? SpokenPhrases.PhoneTestSent : SpokenPhrases.PhoneSendFailed));
                    break;
                case TileKind.Info:
                    break;
            }
        }
    }
}
