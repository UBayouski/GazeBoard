using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.Board
{
    /// <summary>
    /// One tile on the board. Holds its data and reports selection, by dwell or by pinch,
    /// to whoever bound it. Visual wiring (label, ring, button) is done by the board builder.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class BoardTile : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Image _background;

        private Button _button;
        private Action<BoardTile> _onSelected;

        public TileData Data { get; private set; }
        public TMP_Text Label => _label;
        public Image Background => _background;

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
            gameObject.name = "Tile " + data.label;
            gameObject.SetActive(true);
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
            if (Data != null) _onSelected?.Invoke(this);
        }
    }
}
