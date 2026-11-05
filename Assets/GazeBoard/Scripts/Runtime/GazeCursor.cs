using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard
{
    /// <summary>
    /// A small dot on the board where the head is pointing. Head gaze has no natural feedback
    /// (unlike a hand ray), so without it users hunt for which tile is "under" their gaze.
    /// Shown whenever gaze is driven by the head pose, with dwell on or off: with dwell off the
    /// user still aims by head and pinches, so the dot is just as useful.
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class GazeCursor : MonoBehaviour
    {
        [SerializeField, Tooltip("The board canvas this cursor lives on. Found automatically if empty.")]
        private RectTransform _canvasRect;

        private Image _image;
        private RectTransform _rect;
        private HeadGazeFallback _headGaze;
        private Transform _head;
        private float _nextHeadSearch;

        private void Awake()
        {
            _image = GetComponent<Image>();
            _image.raycastTarget = false;
            _rect = GetComponent<RectTransform>();
            if (_canvasRect == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas != null) _canvasRect = canvas.GetComponent<RectTransform>();
            }
            _headGaze = FindFirstObjectByType<HeadGazeFallback>();
            _image.enabled = false;
        }

        private Transform Head()
        {
            if (_head != null) return _head;
            if (Time.unscaledTime < _nextHeadSearch) return null;
            _nextHeadSearch = Time.unscaledTime + 1f;

            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null) _head = rig.centerEyeAnchor;
            else if (Camera.main != null) _head = Camera.main.transform;
            return _head;
        }

        private void LateUpdate()
        {
            Transform head = Head();
            bool show = head != null && _canvasRect != null
                        && (_headGaze == null || _headGaze.UsingHeadGaze);
            if (!show)
            {
                _image.enabled = false;
                return;
            }

            // Intersect the forward ray with the canvas plane and convert to canvas space.
            var plane = new Plane(-_canvasRect.forward, _canvasRect.position);
            var ray = new Ray(head.position, head.forward);
            if (!plane.Raycast(ray, out float distance))
            {
                _image.enabled = false;
                return;
            }

            Vector3 local = _canvasRect.InverseTransformPoint(ray.GetPoint(distance));
            bool inside = _canvasRect.rect.Contains(new Vector2(local.x, local.y));
            _image.enabled = inside;
            if (inside) _rect.anchoredPosition = new Vector2(local.x, local.y);
        }

        public void SetColor(Color color)
        {
            if (_image == null) _image = GetComponent<Image>();
            _image.color = color;
        }
    }
}
