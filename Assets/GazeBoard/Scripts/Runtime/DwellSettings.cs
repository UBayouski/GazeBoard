using System;
using UnityEngine;

namespace GazeBoard
{
    /// <summary>
    /// Global dwell configuration, persisted with PlayerPrefs.
    /// Tiles read DwellSeconds every frame, so changes apply immediately.
    /// </summary>
    public static class DwellSettings
    {
        private const string DwellKey = "gazeboard.dwellSeconds";
        private const string EnabledKey = "gazeboard.dwellEnabled";

        public const float MinDwellSeconds = 0.4f;
        public const float MaxDwellSeconds = 3.0f;
        public const float DefaultDwellSeconds = 1.0f;

        private static float _dwellSeconds = PlayerPrefs.GetFloat(DwellKey, DefaultDwellSeconds);
        private static bool _enabled = PlayerPrefs.GetInt(EnabledKey, 1) == 1;

        /// <summary>Raised whenever DwellSeconds or Enabled changes.</summary>
        public static event Action WhenChanged;

        /// <summary>How long a tile must be gazed before it activates.</summary>
        public static float DwellSeconds
        {
            get => _dwellSeconds;
            set
            {
                float clamped = Mathf.Clamp(value, MinDwellSeconds, MaxDwellSeconds);
                if (Mathf.Approximately(clamped, _dwellSeconds)) return;
                _dwellSeconds = clamped;
                PlayerPrefs.SetFloat(DwellKey, clamped);
                PlayerPrefs.Save();
                WhenChanged?.Invoke();
            }
        }

        /// <summary>When false, tiles only respond to direct pinch or click.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (value == _enabled) return;
                _enabled = value;
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
                WhenChanged?.Invoke();
            }
        }
    }
}
