using System;
using UnityEngine;

namespace GazeBoard.Theme
{
    /// <summary>
    /// Holds the available themes and the selected one, persisted in PlayerPrefs.
    /// Lives on the Board object next to BoardController, which repaints on WhenChanged.
    /// </summary>
    public class ThemeManager : MonoBehaviour
    {
        private const string IndexKey = "gazeboard.themeIndex";

        [SerializeField, Tooltip("Leave empty to use the built-in Calm, Warm and High contrast themes.")]
        private BoardTheme[] _themes;

        private int _index;

        public static ThemeManager Instance { get; private set; }

        public event Action<BoardTheme> WhenChanged;

        public BoardTheme Current => _themes[_index];
        public int Count => _themes.Length;

        private void Awake()
        {
            Instance = this;
            if (_themes == null || _themes.Length == 0) _themes = BoardTheme.Defaults();
            _index = Mathf.Clamp(PlayerPrefs.GetInt(IndexKey, 0), 0, _themes.Length - 1);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Selects the next theme, wrapping around, and returns it.</summary>
        public BoardTheme Next()
        {
            _index = (_index + 1) % _themes.Length;
            PlayerPrefs.SetInt(IndexKey, _index);
            PlayerPrefs.Save();
            WhenChanged?.Invoke(Current);
            return Current;
        }
    }
}
