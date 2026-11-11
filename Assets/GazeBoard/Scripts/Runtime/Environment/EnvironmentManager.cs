using System;
using UnityEngine;

namespace GazeBoard.Environment
{
    public enum EnvironmentMode
    {
        /// <summary>The real room through the headset cameras. The default for an AAC app.</summary>
        Passthrough,
        /// <summary>A calm virtual room.</summary>
        Room,
    }

    /// <summary>
    /// Switches between passthrough and the virtual room, persisted in PlayerPrefs.
    /// References are wired by GazeBoard > Add Environment.
    /// </summary>
    public class EnvironmentManager : MonoBehaviour
    {
        private const string ModeKey = "gazeboard.environment";

        [SerializeField] private Camera _centerCamera;
        [SerializeField] private OVRPassthroughLayer _passthroughLayer;
        [SerializeField] private GameObject _roomRoot;
        [SerializeField] private Material _roomSkybox;
        [SerializeField] private Color _roomClearColor = new Color(0.07f, 0.08f, 0.11f, 1f);

        public static EnvironmentManager Instance { get; private set; }
        public event Action<EnvironmentMode> WhenChanged;
        public EnvironmentMode Mode { get; private set; }

        /// <summary>Root of the virtual room, so it can be turned to face the way the user sits.</summary>
        public Transform RoomRoot => _roomRoot != null ? _roomRoot.transform : null;

        private void Awake()
        {
            Instance = this;
            Mode = (EnvironmentMode)PlayerPrefs.GetInt(ModeKey, (int)EnvironmentMode.Passthrough);
        }

        private void Start()
        {
            // Guard against a wiring mistake: only an enabled camera can clear to transparent.
            if (_centerCamera == null || !_centerCamera.enabled)
            {
                var rig = FindFirstObjectByType<OVRCameraRig>();
                Camera center = rig != null && rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : null;
                _centerCamera = center != null ? center : Camera.main;
            }
            Apply();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public EnvironmentMode Toggle()
        {
            Mode = Mode == EnvironmentMode.Passthrough ? EnvironmentMode.Room : EnvironmentMode.Passthrough;
            PlayerPrefs.SetInt(ModeKey, (int)Mode);
            PlayerPrefs.Save();
            Apply();
            WhenChanged?.Invoke(Mode);
            return Mode;
        }

        private void Apply()
        {
            bool passthrough = Mode == EnvironmentMode.Passthrough;

            if (_passthroughLayer != null) _passthroughLayer.enabled = passthrough;
            if (_roomRoot != null) _roomRoot.SetActive(!passthrough);

            if (_centerCamera != null)
            {
                if (passthrough)
                {
                    // Transparent clear so the passthrough underlay shows through.
                    _centerCamera.clearFlags = CameraClearFlags.SolidColor;
                    _centerCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                }
                else if (_roomSkybox != null)
                {
                    _centerCamera.clearFlags = CameraClearFlags.Skybox;
                    RenderSettings.skybox = _roomSkybox;
                }
                else
                {
                    _centerCamera.clearFlags = CameraClearFlags.SolidColor;
                    _centerCamera.backgroundColor = _roomClearColor;
                }
            }

            Debug.Log($"[GazeBoard] Environment: {Mode}");
        }
    }
}
