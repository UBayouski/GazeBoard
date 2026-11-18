using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace GazeBoard
{
    /// <summary>
    /// Switches the Interaction SDK EyeGaze component to head-pose emulation when the
    /// headset has no eye tracking (Quest 3S), and leaves true eye gaze on when it does.
    /// Attach to the same GameObject as the EyeGaze component in the Gaze Interaction block.
    /// </summary>
    [RequireComponent(typeof(EyeGaze))]
    [DisallowMultipleComponent]
    public class HeadGazeFallback : MonoBehaviour
    {
        public enum Mode
        {
            /// <summary>Use eye tracking when the device supports it, otherwise head gaze.</summary>
            Auto,
            /// <summary>Always use head gaze, even when eye tracking is available.</summary>
            ForceHead,
            /// <summary>Never emulate. Gaze is dead on devices without eye tracking.</summary>
            EyeOnly,
        }

        [SerializeField, Tooltip("Auto picks eye tracking when the device supports it and head gaze otherwise.")]
        private Mode _mode = Mode.Auto;

        [SerializeField, Min(0.1f), Tooltip("Degrees around the head-gaze line within which a tile counts as looked at. " +
                                            "The SDK default of 2 suits jittery eye tracking; with head gaze it lets a tile " +
                                            "fire while the dot rests in the gap beside it. Eye tracking keeps the scene value.")]
        private float _headGazeConeAngle = 0.5f;

        private EyeGaze _eyeGaze;
        private GazeConecaster _conecaster;
        private float _eyeGazeConeAngle = -1f;

        /// <summary>True when gaze is currently driven by the head pose.</summary>
        public bool UsingHeadGaze { get; private set; }

        public Mode CurrentMode
        {
            get => _mode;
            set { _mode = value; ApplyMode(); }
        }

        private void Awake()
        {
            _eyeGaze = GetComponent<EyeGaze>();
        }

        private void OnEnable()
        {
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (_eyeGaze == null) return;

            bool eyeTrackingAvailable = OVRPlugin.eyeGazeInteractionsSupported;
            bool useHead = _mode switch
            {
                Mode.ForceHead => true,
                Mode.EyeOnly => false,
                _ => !eyeTrackingAvailable,
            };

            _eyeGaze.EmulateGazeWithCameraPose = useHead;
            UsingHeadGaze = useHead;

            if (_conecaster == null) _conecaster = FindFirstObjectByType<GazeConecaster>();
            if (_conecaster != null)
            {
                if (_eyeGazeConeAngle < 0f) _eyeGazeConeAngle = _conecaster.ConeAngle;
                _conecaster.ConeAngle = useHead ? _headGazeConeAngle : _eyeGazeConeAngle;
            }

            Debug.Log($"[GazeBoard] Gaze source: {(useHead ? "head pose" : "eye tracking")} " +
                      $"(mode={_mode}, eyeTrackingSupported={eyeTrackingAvailable}), " +
                      $"cone {(_conecaster != null ? _conecaster.ConeAngle.ToString("0.##") + " deg" : "not found")}.");
        }
    }
}
