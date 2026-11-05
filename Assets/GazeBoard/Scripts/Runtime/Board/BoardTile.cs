using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.Board
{
    /// <summary>
    /// One tile on the board. Holds its data and reports selection, by dwell or by pinch,
    /// to whoever bound it. Visual wiring (label, icon, ring, button) is done by the board builder.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BoardTile : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _icon;
        [SerializeField] private Image _background;
        [SerializeField] private Outline _outline;
        [SerializeField] private Sprite _roundedSprite;

        [SerializeField, Tooltip("Fraction of the tile height the icon takes when a tile has both an icon and a label.")]
        private float _iconShare = 0.56f;

        private Button _button;
        private Action<BoardTile> _onSelected;
        private bool _emphasized;

        public TileData Data { get; private set; }
        public TMP_Text Label => _label;
        public Image Background => _background;

        /// <summary>Applies colours, outline, corner style and ring colour from a theme.</summary>
        public void ApplyTheme(Theme.BoardTheme theme, Color tileColor, bool emphasized = false)
        {
            _emphasized = emphasized;
            SetColors(tileColor, theme.Highlight(tileColor));

            Color text = theme.TextFor(tileColor);
            if (_label != null) _label.color = text;
            if (_icon != null) _icon.color = new Color(text.r, text.g, text.b, text.a * 0.92f);

            if (_background != null)
            {
                _background.sprite = theme.roundedCorners ? _roundedSprite : null;
                _background.type = theme.roundedCorners ? Image.Type.Sliced : Image.Type.Simple;
            }

            if (_outline != null)
            {
                float width = emphasized ? theme.activeOutlineWidth : theme.outlineWidth;
                Color color = emphasized ? theme.activeOutlineColor : theme.outlineColor;
                _outline.enabled = width > 0f && color.a > 0f;
                _outline.effectColor = color;
                _outline.effectDistance = new Vector2(width, -width);
            }

            var dwell = GetComponent<DwellSelectable>();
            if (dwell != null && dwell.ProgressRing != null) dwell.ProgressRing.color = theme.dwellRing;
        }

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(HandleClick);
        }

        public void Bind(TileData data, Action<BoardTile> onSelected)
        {
            Data = data;
            _onSelected = onSelected;
            if (_label != null) _label.text = data.label;
            SetIcon(BoardDefinition.IconFor(data));
            gameObject.name = "Tile " + data.label;
            gameObject.SetActive(true);
        }

        /// <summary>Shows a Lucide icon above the label, or centres the label when there is none.</summary>
        public void SetIcon(string iconName)
        {
            string glyph = Icons.Glyph(iconName);
            bool hasIcon = _icon != null && glyph.Length > 0;

            if (_icon != null)
            {
                _icon.text = glyph;
                _icon.gameObject.SetActive(hasIcon);
            }

            if (_label == null) return;
            var labelRect = _label.rectTransform;
            if (hasIcon)
            {
                // Icon in the upper part, label in the lower part.
                var iconRect = _icon.rectTransform;
                iconRect.anchorMin = new Vector2(0f, 1f - _iconShare);
                iconRect.anchorMax = new Vector2(1f, 1f);
                iconRect.offsetMin = new Vector2(0f, 0f);
                iconRect.offsetMax = new Vector2(0f, -4f);

                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f - _iconShare);
                labelRect.offsetMin = new Vector2(0f, 2f);
                labelRect.offsetMax = new Vector2(0f, 4f);
                _label.alignment = TextAlignmentOptions.Top;
            }
            else
            {
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                _label.alignment = TextAlignmentOptions.Center;
            }
        }

        public void SetColors(Color normal, Color highlighted)
        {
            if (_button == null) _button = GetComponent<Button>();
            ColorBlock colors = _button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = highlighted;
            colors.selectedColor = normal;
            colors.pressedColor = highlighted;
            colors.disabledColor = normal;
            _button.colors = colors;
        }

        /// <summary>Read-only tiles ignore gaze and pinch entirely.</summary>
        public void SetInteractable(bool interactable)
        {
            if (_button == null) _button = GetComponent<Button>();
            _button.interactable = interactable;
        }

        private void HandleClick()
        {
            // A pinch or click leaves the Button "selected", and a selected Button shows its selected
            // colour instead of the hover colour until something else is selected. Clear it so the
            // tile highlights again the next time the pointer returns.
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject == gameObject)
            {
                eventSystem.SetSelectedGameObject(null);
            }
            if (Data != null) _onSelected?.Invoke(this);
        }
    }
}
