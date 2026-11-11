using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GazeBoard
{
    /// <summary>
    /// Makes a UI Selectable activate after it has been hovered for DwellSettings.DwellSeconds.
    /// Works with any pointer that drives Unity UI hover events: head gaze, eye gaze, hand ray,
    /// or controller ray, because it listens to IPointerEnter / IPointerExit only.
    /// A direct click (pinch) still works and cancels the dwell so the tile does not fire twice.
    /// </summary>
    [RequireComponent(typeof(Selectable))]
    [DisallowMultipleComponent]
    public class DwellSelectable : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField, Tooltip("Filled Image that shows dwell progress. Optional.")]
        private Image _progressRing;

        [SerializeField, Tooltip("Override the global dwell time for this tile. Leave at 0 to use DwellSettings.")]
        private float _dwellSecondsOverride = 0f;

        [SerializeField, Tooltip("If true, keep firing every dwell period while the gaze stays on the tile. Useful for backspace or scroll tiles.")]
        private bool _repeatWhileHovered = false;

        [SerializeField, Tooltip("Seconds the ring stays full after firing, as visual confirmation.")]
        private float _confirmFlashSeconds = 0.15f;

        [SerializeField, Tooltip("Seconds the gaze may leave the tile without losing dwell progress. Absorbs head-gaze jitter at tile edges.")]
        private float _graceSeconds = 0.18f;

        /// <summary>Raised on any DwellSelectable right after it fires by dwell.</summary>
        public static event Action<DwellSelectable> WhenDwellFired;

        /// <summary>Raised on this instance after it fires by dwell.</summary>
        public event Action WhenFired;

        private readonly HashSet<int> _hoveringPointers = new HashSet<int>();
        private Selectable _selectable;
        private Button _button;
        private float _elapsed;
        private float _flashRemaining;
        private float _graceRemaining;
        private bool _armed = true;

        public bool IsHovered => _hoveringPointers.Count > 0;
        public float Progress => Mathf.Clamp01(_elapsed / EffectiveDwellSeconds);
        public float EffectiveDwellSeconds =>
            _dwellSecondsOverride > 0f ? _dwellSecondsOverride : DwellSettings.DwellSeconds * DwellMultiplier;

        /// <summary>Scales the global dwell time for this tile. Use above 1 for tiles that are costly to hit by accident.</summary>
        public float DwellMultiplier { get; set; } = 1f;

        /// <summary>Keep firing every dwell period while the gaze stays on the tile (delete, scroll).</summary>
        public bool RepeatWhileHovered
        {
            get => _repeatWhileHovered;
            set => _repeatWhileHovered = value;
        }

        /// <summary>
        /// If true this tile dwells even when DwellSettings.Enabled is false. Used by the tile that
        /// turns dwell back on, so a gaze-only user can never lock themselves out.
        /// </summary>
        public bool AlwaysDwell { get; set; }

        public Image ProgressRing
        {
            get => _progressRing;
            set { _progressRing = value; SetRing(0f); }
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _button = _selectable as Button;
            SetRing(0f);
        }

        private void OnDisable()
        {
            ResetDwell();
            _hoveringPointers.Clear();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hoveringPointers.Add(eventData.pointerId);
            _graceRemaining = 0f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hoveringPointers.Remove(eventData.pointerId);
            if (_hoveringPointers.Count == 0)
            {
                // Keep the progress for a moment: a gaze that clips the tile edge comes straight back.
                if (_elapsed > 0f && _graceSeconds > 0f) _graceRemaining = _graceSeconds;
                else { ResetDwell(); _armed = true; }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            // A direct pinch or click already activated the Selectable through its own handler.
            // Cancel the pending dwell so the tile does not fire a second time.
            ResetDwell();
            _armed = _repeatWhileHovered;
        }

        private void Update()
        {
            if (_flashRemaining > 0f)
            {
                _flashRemaining -= Time.unscaledDeltaTime;
                if (_flashRemaining <= 0f) SetRing(0f);
                return;
            }

            if (!IsHovered && _graceRemaining > 0f)
            {
                _graceRemaining -= Time.unscaledDeltaTime;
                if (_graceRemaining <= 0f) { ResetDwell(); _armed = true; }
                return; // progress is held, not advanced
            }

            if (DwellSettings.Suspended || (!DwellSettings.Enabled && !AlwaysDwell) || !_armed || !IsHovered || !IsInteractable())
            {
                if (_elapsed > 0f) ResetDwell();
                return;
            }

            _elapsed += Time.unscaledDeltaTime;
            float dwell = EffectiveDwellSeconds;
            SetRing(_elapsed / dwell);

            if (_elapsed >= dwell)
            {
                Fire();
            }
        }

        private void Fire()
        {
            _elapsed = 0f;
            _flashRemaining = _confirmFlashSeconds;
            SetRing(1f);
            _armed = _repeatWhileHovered;

            if (_button != null)
            {
                _button.onClick.Invoke();
            }
            else
            {
                var data = new BaseEventData(EventSystem.current);
                ExecuteEvents.Execute(gameObject, data, ExecuteEvents.submitHandler);
            }

            WhenFired?.Invoke();
            WhenDwellFired?.Invoke(this);
        }

        private bool IsInteractable()
        {
            return _selectable != null && _selectable.IsInteractable() && _selectable.isActiveAndEnabled;
        }

        private void ResetDwell()
        {
            _elapsed = 0f;
            _flashRemaining = 0f;
            _graceRemaining = 0f;
            SetRing(0f);
        }

        private void SetRing(float fill)
        {
            if (_progressRing == null) return;
            _progressRing.fillAmount = Mathf.Clamp01(fill);
        }
    }
}
