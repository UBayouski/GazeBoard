using GazeBoard.Speech;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard
{
    /// <summary>
    /// Speaks a phrase when the Button on this GameObject is activated, whether by dwell,
    /// pinch, controller or mouse. If Phrase is empty the tile's label text is spoken.
    /// </summary>
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public class SpeakOnClick : MonoBehaviour
    {
        [SerializeField, Tooltip("Text to speak. Leave empty to speak the tile's label.")]
        private string _phrase = "";

        [SerializeField, Tooltip("If true, interrupts any phrase already playing.")]
        private bool _interrupt = true;

        private Button _button;
        private TMP_Text _label;

        /// <summary>The phrase this tile speaks, falling back to its label.</summary>
        public string Phrase
        {
            get => string.IsNullOrWhiteSpace(_phrase) ? LabelText : _phrase;
            set => _phrase = value;
        }

        private string LabelText
        {
            get
            {
                if (_label == null) _label = GetComponentInChildren<TMP_Text>(true);
                return _label != null ? _label.text : "";
            }
        }

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(Speak);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(Speak);
        }

        /// <summary>Speak this tile's phrase now.</summary>
        public void Speak()
        {
            SpeechService.Speak(Phrase, _interrupt);
        }
    }
}
