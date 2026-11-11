using System.Collections;
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

        /// <summary>Time a gaze user gets to look at the new spot after choosing Recenter.</summary>
        private const float RecenterDelaySeconds = 3.5f;
        private BoardPlacement _placement;

        private const string SpeakTilesKey = "gazeboard.speakTiles";
        private static int _speakTiles = -1;

        /// <summary>
        /// Whether a phrase tile is spoken the moment it is chosen. When off, phrase tiles only
        /// build the sentence and nothing is heard (or sent to the phone) until Speak. Yes, No
        /// and Help always speak at once. Persisted.
        /// </summary>
        private static bool SpeakTiles
        {
            get
            {
                if (_speakTiles < 0) _speakTiles = PlayerPrefs.GetInt(SpeakTilesKey, 1);
                return _speakTiles == 1;
            }
            set
            {
                _speakTiles = value ? 1 : 0;
                PlayerPrefs.SetInt(SpeakTilesKey, _speakTiles);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Speaks user content: baked clip when the text is a board phrase, per-user cache otherwise.</summary>
        private void SpeakUser(string text) => SpeechService.Speak(text, true, userText: !_bakedPhrases.Contains(text));

        /// <summary>
        /// Speaks the sentence bar. Online, the whole sentence streams as one natural utterance.
        /// Offline it is spoken only if all of it can be: from a clip of the whole sentence (a
        /// board phrase, or a sentence spoken online before and kept on the device), or else from
        /// the clips of the phrases it was built from, played back to back. If any word has no
        /// clip, nothing is spoken and the user is told so. A sentence with words missing can
        /// mean something else, so part of it is never said in its place. The text stays in the
        /// sentence bar either way, where the listener can read it.
        /// </summary>
        private void SpeakSentence(string text)
        {
            if (!SpeechService.IsOffline || SpeechService.HasOfflineClip(text))
            {
                SpeakUser(text);
                return;
            }

            List<string> parts = SplitIntoOfflineClips(text);
            if (parts != null) SpeechService.SpeakParts(parts);
            else SpeechService.ReportUnavailable(text);
        }

        /// <summary>
        /// Greedy longest match of the sentence's words against the clips on the device. Returns
        /// null when a word is left over: the sentence cannot be spoken whole.
        /// </summary>
        private static List<string> SplitIntoOfflineClips(string text)
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
                    if (SpeechService.HasOfflineClip(candidate)) { found = candidate; i += len; break; }
                }
                if (found == null) return null;
                parts.Add(found);
            }
            return parts.Count > 0 ? parts : null;
        }

        // ---- notices: what did not happen ----

        private const float NoticeSeconds = 6f;
        private const float NoticeFadeSeconds = 0.5f;
        private const string NoticeCannotSpeak = "No internet. This cannot be spoken right now.";
        private const string NoticePhoneFailed = "Phone alert was not sent.";
        private const string NoticeHelpFailed = "Help alert was not sent to the phone.";

        private RectTransform _notice;
        private Image _noticeBackground;
        private TMP_Text _noticeText;
        private CanvasGroup _noticeGroup;
        private float _noticeUntil;
        private bool _noticeIsSpeech;

        private void HandleSpeechUnavailable(string text) => ShowNotice(NoticeCannotSpeak, aboutSpeech: true);

        private void HandlePhoneSent(bool accepted, bool urgent)
        {
            if (accepted) return;
            // With no connection both speech and the phone fail; the speech notice says more.
            if (_noticeIsSpeech && Time.unscaledTime < _noticeUntil) return;
            ShowNotice(urgent ? NoticeHelpFailed : NoticePhoneFailed, aboutSpeech: false);
        }

        /// <summary>
        /// A short line above the board for something that did not happen: a sentence that could
        /// not be spoken, an alert that did not reach the notification service. It sits outside
        /// the board so it covers no tile, and fades by itself.
        /// </summary>
        private void ShowNotice(string message, bool aboutSpeech)
        {
            if (_sentenceText == null) return;
            if (_notice == null)
            {
                var go = new GameObject("Notice", typeof(RectTransform));
                _notice = (RectTransform)go.transform;
                _notice.SetParent(transform, false);
                _notice.anchorMin = _notice.anchorMax = new Vector2(0.5f, 1f);
                _notice.pivot = new Vector2(0.5f, 0f);
                _notice.anchoredPosition = new Vector2(0f, 22f);
                _noticeGroup = go.AddComponent<CanvasGroup>();
                _noticeGroup.blocksRaycasts = false;
                _noticeGroup.interactable = false;
                _noticeBackground = go.AddComponent<Image>();
                _noticeBackground.sprite = BoardPlacement.PillSprite();
                _noticeBackground.type = Image.Type.Sliced;
                _noticeBackground.raycastTarget = false;

                var textGo = new GameObject("Text", typeof(RectTransform));
                var textRect = (RectTransform)textGo.transform;
                textRect.SetParent(_notice, false);
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = textRect.offsetMax = Vector2.zero;
                var text = textGo.AddComponent<TextMeshProUGUI>();
                text.font = _sentenceText.font;
                text.fontSize = 40f;
                text.alignment = TextAlignmentOptions.Center;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.raycastTarget = false;
                _noticeText = text;
            }

            // The warning colour of the theme; where that is black (High contrast), the text
            // colour instead, so the notice does not vanish against the dark around the board.
            Color ground = BoardTheme.Luminance(Theme.noTile) < 0.02f ? Theme.tileText : Theme.noTile;
            _noticeBackground.color = ground;
            _noticeText.color = BoardTheme.Contrast(ground, Color.white) >= BoardTheme.Contrast(ground, Color.black) ? Color.white : Color.black;
            _noticeText.text = message;
            _notice.sizeDelta = new Vector2(_noticeText.GetPreferredValues(message).x + 96f, 76f);
            _notice.SetAsLastSibling();
            if (_gazeCursor != null) _gazeCursor.transform.SetAsLastSibling();

            _noticeIsSpeech = aboutSpeech;
            _noticeUntil = Time.unscaledTime + NoticeSeconds;
            _noticeGroup.alpha = 1f;
            _notice.gameObject.SetActive(true);
        }

        private void UpdateNotice()
        {
            if (_notice == null || !_notice.gameObject.activeSelf) return;
            float left = _noticeUntil - Time.unscaledTime;
            if (left <= 0f) _notice.gameObject.SetActive(false);
            else _noticeGroup.alpha = Mathf.Clamp01(left / NoticeFadeSeconds);
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
            RenderSentence();

            _themes = GetComponent<ThemeManager>();
            if (_themes == null) _fallbackTheme ??= BoardTheme.Defaults()[0];
            else _themes.WhenChanged += HandleThemeChanged;

            // Placement needs no scene wiring: it lives on the board canvas and builds its own handle.
            _placement = GetComponent<BoardPlacement>();
            if (_placement == null) _placement = gameObject.AddComponent<BoardPlacement>();
            _placement.WhenChanged += HandlePlacementChanged;

            SpeechService.WhenUnavailable += HandleSpeechUnavailable;
            PhoneNotifier.WhenSent += HandlePhoneSent;

            BindFixedTiles();
            ApplyBoardTheme();
            BuildCategoryBar();
            ShowCategory(StartCategory());
        }

        private void OnDestroy()
        {
            SpeechService.WhenUnavailable -= HandleSpeechUnavailable;
            PhoneNotifier.WhenSent -= HandlePhoneSent;
            if (_placement != null) _placement.WhenChanged -= HandlePlacementChanged;
            if (_themes != null) _themes.WhenChanged -= HandleThemeChanged;
        }

        private void Update()
        {
            if (_themes == null && _fallbackTheme == null) return;

            UpdateNotice();

            // The alert stops by itself after its repeat limit; put the Help tile back.
            if (_helpShowsStop && !SpeechService.IsAlerting) BindHelpTile();

            // While the alert runs, the Stop tile pulses slowly so it reads as "alarm active".
            if (_helpTile != null && _helpTile.Background != null)
            {
                float pulse = _helpShowsStop ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
                _helpTile.Background.color = new Color(pulse, pulse, pulse, 1f);
            }

            // The caret blinks at the end of the sentence, where the next word will land.
            bool caretOn = Time.unscaledTime < _caretSolidUntil || Mathf.Repeat(Time.unscaledTime, CaretPeriod * 2f) < CaretPeriod;
            if (caretOn != _caretOn)
            {
                _caretOn = caretOn;
                RenderSentence();
            }

            // The sentence box glows while the voice is speaking so the user sees the output happen.
            // It breathes rather than holding one colour: a steady tint is easy to miss.
            if (_sentenceBox == null) return;
            Color target = Theme.sentenceBox;
            if (SpeechService.IsSpeaking)
            {
                float breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
                target = Color.Lerp(Theme.sentenceBoxSpeaking, Color.Lerp(Theme.sentenceBoxSpeaking, Theme.sentenceText, 0.16f), breath);
            }
            _sentenceBox.color = Color.Lerp(_sentenceBox.color, target, Time.unscaledDeltaTime * 8f);
        }

        // ---- sentence bar ----

        private const float CaretPeriod = 0.53f;
        private const string EmptyHint = "Choose a tile to start";
        private string _sentenceRaw = "";
        private bool _caretOn = true;
        private float _caretSolidUntil;

        /// <summary>Writes the sentence with its caret, or a faint hint while there is nothing yet.</summary>
        private void RenderSentence()
        {
            if (_sentenceText == null) return;
            // The hidden caret keeps its width, so the text does not shift as it blinks.
            string caret = _caretOn ? "<alpha=#FF>|" : "<alpha=#00>|";
            _sentenceText.text = _sentenceRaw.Length == 0
                ? caret + "<alpha=#66> " + EmptyHint
                : "<noparse>" + _sentenceRaw + "</noparse>" + caret;
        }

        // ---- selection feedback ----

        private readonly List<GameObject> _flying = new List<GameObject>();

        /// <summary>
        /// Sends a copy of the chosen word from its tile to the end of the sentence. With speech
        /// on each tile this is a flourish; with tiles silent it is the confirmation that the
        /// choice registered and where it went.
        /// </summary>
        private void FlyToSentence(BoardTile tile, string word)
        {
            if (_sentenceText == null || tile == null || tile.Label == null || string.IsNullOrWhiteSpace(word)) return;

            var tileRect = (RectTransform)tile.transform;
            Vector3 from = transform.InverseTransformPoint(tileRect.TransformPoint(tileRect.rect.center));

            // Where the caret stands now is where the word is about to appear. Taken from the
            // laid-out text, so it is right on any line once the sentence wraps.
            RectTransform textRect = _sentenceText.rectTransform;
            Vector3 end = new Vector3(textRect.rect.xMin + _sentenceText.margin.x, textRect.rect.center.y, 0f);
            _sentenceText.ForceMeshUpdate();
            TMP_TextInfo info = _sentenceText.textInfo;
            if (info != null && info.characterCount > 0)
            {
                // The caret is the first character while the hint shows, the last one otherwise.
                int caretIndex = _sentenceRaw.Length == 0 ? 0 : info.characterCount - 1;
                TMP_CharacterInfo caret = info.characterInfo[caretIndex];
                end = new Vector3(caret.bottomLeft.x, (caret.ascender + caret.descender) * 0.5f, 0f);
            }
            Vector3 to = transform.InverseTransformPoint(textRect.TransformPoint(end));

            var go = new GameObject("FlyingWord", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.sizeDelta = new Vector2(700f, 90f);
            rect.localPosition = from;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = tile.Label.font;
            text.fontSize = Mathf.Max(tile.Label.fontSize, 30f);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = "<noparse>" + word.Trim() + "</noparse>";
            text.color = tile.Label.color;
            if (_gazeCursor != null) _gazeCursor.transform.SetAsLastSibling(); // the gaze dot stays on top

            _flying.Add(go);
            StartCoroutine(Fly(go, rect, text, from, to, word.Trim().Length <= 1 ? 0.24f : 0.34f));
        }

        private IEnumerator Fly(GameObject go, RectTransform rect, TMP_Text text, Vector3 from, Vector3 to, float seconds)
        {
            Color start = text.color, finish = Theme.sentenceText;
            float elapsed = 0f;
            while (elapsed < seconds && go != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                float eased = t * t * (3f - 2f * t);
                // A shallow arc reads as a toss rather than a slide.
                Vector3 position = Vector3.Lerp(from, to, eased) + Vector3.up * (Mathf.Sin(eased * Mathf.PI) * 36f);
                rect.localPosition = position;
                float scale = Mathf.Lerp(1f, 0.6f, eased);
                rect.localScale = new Vector3(scale, scale, 1f);
                Color c = Color.Lerp(start, finish, eased);
                // Stay solid until it has all but arrived. Fading earlier made it vanish part-way,
                // which looked as if every word were heading for the middle of the bar.
                c.a = t < 0.85f ? 1f : 1f - (t - 0.85f) / 0.15f;
                text.color = c;
                yield return null;
            }
            _flying.Remove(go);
            if (go != null) Destroy(go);
        }

        private void OnDisable()
        {
            // Coroutines stop with the component; do not leave words hanging in the air.
            foreach (GameObject go in _flying) if (go != null) Destroy(go);
            _flying.Clear();
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
            _sentenceRaw = text ?? "";
            // Solid for a moment after each change, so the caret is not caught mid-blink.
            _caretSolidUntil = Time.unscaledTime + CaretPeriod;
            _caretOn = true;
            RenderSentence();
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
            ApplySettingsTileDwell();

            BindActionTile(_yesTile, new TileData("Yes", TileKind.Phrase, SpokenPhrases.Yes, "check"), Theme.yesTile);
            BindActionTile(_noTile, new TileData("No", TileKind.Phrase, SpokenPhrases.No, "x"), Theme.noTile);
            BindHelpTile();
            BindActionTile(_repeatTile, new TileData("Repeat", TileKind.Repeat, null, "repeat"), Theme.actionTile);
            BindActionTile(_recentTile, new TileData("Recent", TileKind.ShowRecent, null, "history"), Theme.recentTile);
            BindActionTile(_saveTile, new TileData("Save", TileKind.SavePhrase, null, "bookmark-plus"), Theme.personalTile, _carefulDwellMultiplier);
        }

        /// <summary>
        /// Gaze select can be switched off, and the setting is kept across restarts. The tile
        /// that switches it back on is inside Settings, so the Settings tile must answer to gaze
        /// as well, or a person who cannot pinch would have no way back. With gaze select off it
        /// takes longer, so someone aiming by head to pinch does not open Settings by resting on it.
        /// </summary>
        private void ApplySettingsTileDwell()
        {
            if (_settingsTile == null) return;
            var dwell = _settingsTile.GetComponent<DwellSelectable>();
            if (dwell == null) return;
            dwell.AlwaysDwell = true;
            dwell.DwellMultiplier = DwellSettings.Enabled ? _categoryDwellMultiplier : _categoryDwellMultiplier * GazeOffSettingsDwell;
        }

        private const float GazeOffSettingsDwell = 1.5f;

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

        /// <summary>How far a category that is not open blends into the board colour. 0 turns it off.</summary>
        private const float InactiveCategoryFade = 0.22f;

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
                // The open category keeps its full colour; the others step back a little towards
                // the board, so navigation does not compete with the phrases.
                Color ground = Theme.boardBackground;
                ground.a = 1f;
                Color resting = Color.Lerp(role, ground, InactiveCategoryFade);
                _categoryTiles[tileIndex].ApplyTheme(Theme, active ? Theme.OpenCategory(role) : resting, active);
                tileIndex++;
            }
            if (_settingsTile != null)
            {
                _settingsTile.ApplyTheme(Theme, current.isSettings ? Theme.OpenCategory(Theme.actionTile) : Theme.actionTile, current.isSettings);
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
            bool needsPaging = tiles.Count > free;
            // The first page gives one cell to More and the last one to Back; pages in between
            // carry both, so every page can go either way.
            int edgeCapacity = Mathf.Max(1, free - 1);
            int middleCapacity = Mathf.Max(1, free - 2);
            int pageCount = 1;
            if (needsPaging)
            {
                pageCount = 2;
                while (edgeCapacity * 2 + middleCapacity * (pageCount - 2) < tiles.Count) pageCount++;
            }
            if (_page >= pageCount || _page < 0) _page = 0;

            bool first = _page == 0;
            bool last = _page == pageCount - 1;
            int capacity = !needsPaging ? free : (first || last) ? edgeCapacity : middleCapacity;
            int start = first ? 0 : edgeCapacity + (_page - 1) * middleCapacity;
            int end = Mathf.Min(tiles.Count, start + capacity);
            for (int i = start; i < end; i++)
            {
                AddGridTile(tiles[i], category);
            }

            if (needsPaging)
            {
                // Pad so the navigation and tail tiles keep their place at the end of the grid.
                for (int i = end - start; i < capacity; i++) AddGridTile(new TileData("", TileKind.Info), category, placeholder: true);

                string count = $"({_page + 1}/{pageCount})";
                if (!first && !last) AddGridTile(new TileData("Back", TileKind.PrevPage, null, "arrow-left"), category);
                AddGridTile(last
                    ? new TileData($"Back {count}", TileKind.PrevPage, null, "arrow-left")
                    : new TileData($"More {count}", TileKind.NextPage, null, "arrow-right"), category);
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
                    case TileKind.PhoneNewCode: // unpairs the phone, so not by a passing glance
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

            // Page 2: where the board sits. The same can be done by pinching the bar under the
            // board and dragging; these tiles are the no-hands way.
            float distance = _placement != null ? _placement.Distance : BoardPlacement.DefaultDistance;
            float scale = _placement != null ? _placement.ScaleFactor : BoardPlacement.DefaultScale;
            yield return new TileData($"Board\n{distance:0.0} m", TileKind.Info, null, "move");
            yield return new TileData("Recenter", TileKind.BoardRecenter, null, "scan-eye");
            yield return new TileData("Closer", TileKind.BoardCloser, null, "minus");
            yield return new TileData("Farther", TileKind.BoardFarther, null, "plus");

            yield return new TileData($"Size\n{Mathf.RoundToInt(scale * 100f)}%", TileKind.Info, null, "layers");
            yield return new TileData("Smaller", TileKind.BoardSmaller, null, "minus");
            yield return new TileData("Bigger", TileKind.BoardBigger, null, "plus");
            yield return new TileData("Reset", TileKind.BoardReset, null, "history");

            // Speak each tile as it is chosen, or stay silent until Speak.
            yield return new TileData("Speech", TileKind.Info, null, "speech");
            yield return new TileData(SpeakTiles ? "Speak tiles\nON" : "Speak tiles\nOFF", TileKind.SpeakTilesToggle, null,
                SpeakTiles ? "volume-2" : "ban");
            // Back and More fill the rest of this row, which ends page 2.

            // Page 3: phone alerts through ntfy, in two rows with their own headers. First the
            // pairing: the code, a new code (if the old one may have been seen by someone it was
            // not meant for), and a test message.
            yield return new TileData("Pairing", TileKind.Info, null, "smartphone");
            yield return new TileData("Code\n" + PhoneNotifier.Code, TileKind.PhoneCode, null, "key-round");
            yield return new TileData("New code", TileKind.PhoneNewCode, null, "repeat");
            yield return new TileData("Test phone", TileKind.PhoneTest, null, "send");

            // Then whether what is said goes to the phone.
            yield return new TileData("Phone", TileKind.Info, null, "phone");
            yield return new TileData(PhoneNotifier.Enabled ? "Alerts\nON" : "Alerts\nOFF", TileKind.PhoneToggle, null,
                PhoneNotifier.Enabled ? "bell-ring" : "phone-off");
        }

        private bool ShowingPersonal => Current != null && !Current.isSettings && BoardDefinition.RoleFor(Current) == CategoryRole.Personal;

        private void OnTileSelected(BoardTile tile)
        {
            TileData data = tile.Data;

            // Feedback first, while the tile and the old end of the sentence are still there.
            tile.Pulse();
            switch (data.kind)
            {
                case TileKind.Phrase:
                case TileKind.SavedPhrase:
                case TileKind.RecentPhrase:
                case TileKind.Letter:
                case TileKind.Suggestion:
                    FlyToSentence(tile, data.Text);
                    break;
            }

            // A running help alert keeps going while the user says more; only Stop or its
            // repeat limit ends it.
            switch (data.kind)
            {
                case TileKind.Phrase:
                    // Yes and No are answers, so they are heard at once even when tiles are silent.
                    if (SpeakTiles || tile == _yesTile || tile == _noTile)
                    {
                        SpeechService.Speak(data.Text);
                        PhoneNotifier.Say(data.Text);
                    }
                    _sentence.AppendPhrase(data.Text);
                    break;
                case TileKind.SavedPhrase:
                case TileKind.RecentPhrase:
                    if (SpeakTiles)
                    {
                        SpeakUser(data.Text);
                        PhoneNotifier.Say(data.Text);
                    }
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
                case TileKind.PrevPage:
                    _page = Mathf.Max(0, _page - 1);
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
                    if (Current != null && Current.isSettings && !DwellSettings.Enabled && _page > 0)
                    {
                        // With gaze select off this is the only tile that still answers to gaze,
                        // and the switch is on the first page: go straight there.
                        ShowCategory(SettingsIndex(), resetView: true);
                    }
                    else
                    {
                        ShowCategory(Current != null && Current.isSettings ? StartCategory() : SettingsIndex());
                    }
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
                    ApplySettingsTileDwell();
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
                case TileKind.PhoneNewCode:
                    PhoneNotifier.NewCode();
                    SpeechService.Speak(SpokenPhrases.PhoneNewCode);
                    RebuildGrid();
                    break;
                case TileKind.SpeakTilesToggle:
                    SpeakTiles = !SpeakTiles;
                    SpeechService.Speak(SpeakTiles ? SpokenPhrases.SpeakTilesOn : SpokenPhrases.SpeakTilesOff);
                    RebuildGrid();
                    break;
                case TileKind.BoardRecenter:
                    if (_placement != null)
                    {
                        // A gaze user cannot look at this tile and at the new spot at once, so
                        // the board waits while they turn, then comes to where they look.
                        SpeechService.Speak(SpokenPhrases.BoardRecenterPrompt);
                        _placement.RecenterAfter(RecenterDelaySeconds, () => SpeechService.Speak(SpokenPhrases.BoardMoved));
                    }
                    break;
                case TileKind.BoardCloser:
                    StepBoard(distanceSteps: -1, scaleSteps: 0, SpokenPhrases.BoardCloser);
                    break;
                case TileKind.BoardFarther:
                    StepBoard(distanceSteps: 1, scaleSteps: 0, SpokenPhrases.BoardFarther);
                    break;
                case TileKind.BoardSmaller:
                    StepBoard(distanceSteps: 0, scaleSteps: -1, SpokenPhrases.BoardSmaller);
                    break;
                case TileKind.BoardBigger:
                    StepBoard(distanceSteps: 0, scaleSteps: 1, SpokenPhrases.BoardBigger);
                    break;
                case TileKind.BoardReset:
                    if (_placement != null)
                    {
                        SpeechService.Speak(SpokenPhrases.BoardReset);
                        _placement.ResetPlacement(); // raises WhenChanged, which refreshes the page
                    }
                    break;
                case TileKind.Info:
                    break;
            }
        }

        private void StepBoard(int distanceSteps, int scaleSteps, string spoken)
        {
            if (_placement == null) return;
            SpeechService.Speak(spoken);
            // Each step raises WhenChanged, which refreshes the value tiles.
            if (distanceSteps != 0) _placement.StepDistance(distanceSteps);
            if (scaleSteps != 0) _placement.StepScale(scaleSteps);
        }

        /// <summary>Tiles and hand drags both change the placement; keep the Settings value tiles current.</summary>
        private void HandlePlacementChanged()
        {
            if (Current != null && Current.isSettings) RebuildGrid();
        }
    }
}
