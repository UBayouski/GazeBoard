using System;
using System.Collections;
using GazeBoard.Environment;
using UnityEngine;
using UnityEngine.UI;

namespace GazeBoard.Board
{
    /// <summary>
    /// Where the board sits in the room. Three ways to change it:
    /// Recenter puts it in front of the head (also done when the headset is put on),
    /// the Settings tiles step its distance and size, and pinching the pill under the board
    /// drags it with a hand ray. Distance, height and size persist; the direction does not,
    /// because the user may sit somewhere else next time.
    /// Added to the board canvas by BoardController, so no scene wiring is needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardPlacement : MonoBehaviour
    {
        public const float DefaultDistance = 1.0f;
        public const float MinDistance = 0.6f;
        public const float MaxDistance = 2.0f;
        public const float DistanceStep = 0.1f;

        public const float DefaultScale = 1.0f;
        public const float MinScale = 0.6f;
        public const float MaxScale = 1.4f;
        public const float ScaleStep = 0.1f;

        // Recenter puts the board's centre on the gaze line. The head's pitch is limited, so a
        // glance at the floor or ceiling at that moment cannot send the board there.
        private const float MinGazePitch = -30f;
        private const float MaxGazePitch = 15f;

        // The Recenter tile waits for the head to stay within this angle for this long.
        private const float SteadyAngle = 2.5f;
        private const float SteadySeconds = 0.5f;
        private const float MaxSteadyWait = 2.5f;

        private const string DistanceKey = "gazeboard.board.distance";
        private const string ScaleKey = "gazeboard.board.scale";

        // Handle geometry in canvas units (1 unit = 1 mm at scale 1).
        private const float HandleWidth = 320f;
        private const float HandleHeight = 20f;
        private const float HandleGap = 44f;
        private static readonly Vector2 HandleHitPadding = new Vector2(120f, 50f);

        [SerializeField, Tooltip("How quickly the board glides to a new place. Higher is snappier.")]
        private float _followSpeed = 12f;
        [SerializeField, Tooltip("Metres the board moves away per metre the dragging hand is pushed forward.")]
        private float _pushPullGain = 2.5f;

        public static BoardPlacement Instance { get; private set; }

        /// <summary>Raised when distance or size changes, so the Settings page can refresh its values.</summary>
        public event Action WhenChanged;

        public float Distance { get; private set; }
        public float ScaleFactor { get; private set; }
        public bool IsDragging => _dragHand != null;

        private Vector3 _baseScale;
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

        private Transform _head;
        private Transform _trackingSpace;
        private float _nextRigSearch;
        private OVRHand[] _hands = Array.Empty<OVRHand>();
        private bool[] _wasPinching = Array.Empty<bool>();
        private float _nextHandSearch;

        private RectTransform _handle;
        private Image _handleImage;
        private float _handleGlow;

        // Beam geometry in metres, except the fade distance (canvas units).
        private const float BeamWidth = 0.006f;
        private const float BeamDotSize = 0.022f;
        private const float BeamStartOffset = 0.06f;
        private const float BeamFadeDistance = 320f;
        private const float FreeBeamStrength = 0.45f; // a beam that is not near the drag bar
        private const float FreeBeamLength = 1.2f;    // when it points away from the board
        private const float MaxBeamLength = 4f;

        private RectTransform[] _beams = Array.Empty<RectTransform>();
        private Image[] _beamImages = Array.Empty<Image>();
        private RectTransform[] _beamDots = Array.Empty<RectTransform>();
        private Image[] _beamDotImages = Array.Empty<Image>();
        private float[] _beamStrength = Array.Empty<float>();

        private OVRHand _dragHand;
        private float _dragDistance;
        private float _dragStartReach;
        private Vector3 _dragGrabLocal;
        private float _pinchLostFor;

        private Coroutine _pending;
        private bool _placedOnce;
        private CanvasGroup _group;
        private Transform _room;
        private Vector3 _roomHomePosition;
        private Quaternion _roomHomeRotation;

        private void Awake()
        {
            Instance = this;
            _baseScale = transform.localScale;
            _targetPosition = transform.position;
            _targetRotation = transform.rotation;

            Distance = Mathf.Clamp(PlayerPrefs.GetFloat(DistanceKey, DefaultDistance), MinDistance, MaxDistance);
            ScaleFactor = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, DefaultScale), MinScale, MaxScale);
            transform.localScale = _baseScale * ScaleFactor;

            BuildHandle();
        }

        private void OnEnable()
        {
            OVRManager.HMDMounted += HandleMounted;
            RecenterWhenWorn();
        }

        private void OnDisable()
        {
            OVRManager.HMDMounted -= HandleMounted;
            if (_pending != null) StopCoroutine(_pending);
            _pending = null;
            if (_dragHand != null) EndDrag();
            DwellSettings.Suspended = false;
            SetVisible(true);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void HandleMounted() => RecenterWhenWorn();

        // ---- public actions ----

        /// <summary>Places the board in front of where the head points right now.</summary>
        public void Recenter(bool snap = false)
        {
            Transform head = Head();
            if (head == null) return;
            PlaceInFront(head.position, GazeDirection(head), snap);
            AlignRoom(head.position, FlatForward(head));
        }

        /// <summary>
        /// Recenters after a delay, so a gaze-only user can first look where the board should go.
        /// Dwell is suspended meanwhile: the gaze sweeps across tiles on its way there.
        /// </summary>
        public void RecenterAfter(float seconds, Action done = null)
        {
            if (_pending != null) StopCoroutine(_pending);
            _pending = StartCoroutine(RecenterAfterRoutine(seconds, done));
        }

        public void StepDistance(int steps)
        {
            Distance = Mathf.Clamp(Round1(Distance + steps * DistanceStep), MinDistance, MaxDistance);
            PlayerPrefs.SetFloat(DistanceKey, Distance);
            PlayerPrefs.Save();
            KeepDirection();
            WhenChanged?.Invoke();
        }

        public void StepScale(int steps)
        {
            ScaleFactor = Mathf.Clamp(Round1(ScaleFactor + steps * ScaleStep), MinScale, MaxScale);
            PlayerPrefs.SetFloat(ScaleKey, ScaleFactor);
            PlayerPrefs.Save();
            transform.localScale = _baseScale * ScaleFactor;
            WhenChanged?.Invoke();
        }

        /// <summary>Default distance, height and size, in front of the head.</summary>
        public void ResetPlacement()
        {
            Distance = DefaultDistance;
            ScaleFactor = DefaultScale;
            PlayerPrefs.SetFloat(DistanceKey, Distance);
            PlayerPrefs.SetFloat(ScaleKey, ScaleFactor);
            PlayerPrefs.Save();
            transform.localScale = _baseScale * ScaleFactor;
            Recenter();
            WhenChanged?.Invoke();
        }

        // ---- placement ----

        private void RecenterWhenWorn()
        {
            if (!isActiveAndEnabled) return;
            if (_pending != null) StopCoroutine(_pending);
            DwellSettings.Suspended = false;
            _pending = StartCoroutine(RecenterWhenWornRoutine());
        }

        private IEnumerator RecenterWhenWornRoutine()
        {
            bool first = !_placedOnce;
            // At launch the board stays hidden until it is placed, so it never jumps into view.
            if (first) SetVisible(false);

            // Until the headset is worn and tracking, the head pose is meaningless. If presence is
            // never reported, stop insisting on it after a few seconds rather than hide the board
            // for good. Without any head pose (editor play mode) the board keeps its scene pose.
            float waited = 0f;
            while (!HeadReady(requirePresence: waited < 3f))
            {
                waited += Time.unscaledDeltaTime;
                if (waited >= 3f) SetVisible(true);
                yield return null;
            }

            // Put on mid-session: let the user settle before the board moves.
            if (!first) yield return new WaitForSecondsRealtime(0.6f);
            Recenter(snap: true);
            _placedOnce = true;
            SetVisible(true);
            _pending = null;
        }

        private void SetVisible(bool visible)
        {
            if (_group == null)
            {
                _group = GetComponent<CanvasGroup>();
                if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
            }
            _group.alpha = visible ? 1f : 0f;
            _group.blocksRaycasts = visible;
        }

        private IEnumerator RecenterAfterRoutine(float seconds, Action done)
        {
            DwellSettings.Suspended = true;
            yield return new WaitForSecondsRealtime(seconds);

            // Take the direction once the head has come to rest, not at whatever instant the
            // timer runs out: a head still turning, or settling after the turn, lands the board
            // off the spot. Wait for it to hold still for a moment, but not for ever.
            Transform head = Head();
            if (head != null)
            {
                Vector3 reference = head.forward;
                float still = 0f, waited = 0f;
                while (still < SteadySeconds && waited < MaxSteadyWait)
                {
                    yield return null;
                    waited += Time.unscaledDeltaTime;
                    if (Vector3.Angle(reference, head.forward) > SteadyAngle)
                    {
                        reference = head.forward;
                        still = 0f;
                    }
                    else
                    {
                        still += Time.unscaledDeltaTime;
                    }
                }
            }
            Recenter();
            // Hold dwell until the board has arrived, so a tile gliding under the gaze does not fire.
            yield return new WaitForSecondsRealtime(0.6f);
            DwellSettings.Suspended = false;
            _pending = null;
            done?.Invoke();
        }

        private bool HeadReady(bool requirePresence)
        {
            Transform head = Head();
            return head != null && head.localPosition.sqrMagnitude > 0.0001f
                   && (!requirePresence || OVRPlugin.userPresent);
        }

        private void KeepDirection()
        {
            Transform head = Head();
            if (head == null) return;
            Vector3 toBoard = _targetPosition - head.position;
            Vector3 direction = toBoard.sqrMagnitude > 0.0001f ? toBoard.normalized : GazeDirection(head);
            PlaceInFront(head.position, direction, snap: false);
        }

        private void PlaceInFront(Vector3 headPosition, Vector3 direction, bool snap)
        {
            _targetPosition = headPosition + direction * Distance;
            _targetRotation = Quaternion.LookRotation(_targetPosition - headPosition, Vector3.up);
            if (snap) transform.SetPositionAndRotation(_targetPosition, _targetRotation);
        }

        /// <summary>
        /// Where the head points, with its up and down tilt kept within limits. A relaxed head
        /// points a little below the horizon, so a board at eye height would sit above the gaze dot.
        /// </summary>
        private static Vector3 GazeDirection(Transform head)
        {
            Vector3 flat = FlatForward(head);
            float pitch = Mathf.Asin(Mathf.Clamp(head.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, MinGazePitch, MaxGazePitch);
            Vector3 right = Vector3.Cross(Vector3.up, flat);
            return Quaternion.AngleAxis(-pitch, right) * flat;
        }

        private static Vector3 FlatForward(Transform head)
        {
            Vector3 flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            // Looking straight down or up: the top of the head points the way the body faces.
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.ProjectOnPlane(head.up, Vector3.up);
            return flat.sqrMagnitude > 0.0001f ? flat.normalized : Vector3.forward;
        }

        /// <summary>
        /// The calm room was laid out around a user at the origin facing +Z. Turn and move it with
        /// the board, or a user facing another way would find the board inside a wall.
        /// </summary>
        private void AlignRoom(Vector3 headPosition, Vector3 flatDirection)
        {
            if (_room == null)
            {
                EnvironmentManager env = EnvironmentManager.Instance;
                _room = env != null ? env.RoomRoot : null;
                if (_room == null) return;
                _roomHomePosition = _room.position;
                _roomHomeRotation = _room.rotation;
            }

            Quaternion yaw = Quaternion.LookRotation(flatDirection, Vector3.up);
            Vector3 position = new Vector3(headPosition.x, 0f, headPosition.z) + yaw * _roomHomePosition;
            _room.SetPositionAndRotation(position, yaw * _roomHomeRotation);
        }

        // ---- per-frame ----

        private void LateUpdate()
        {
            UpdateHands();

            float t = 1f - Mathf.Exp(-_followSpeed * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, _targetPosition, t),
                Quaternion.Slerp(transform.rotation, _targetRotation, t));

            if (_handleImage != null)
            {
                Color c = _handleImage.color;
                c.a = Mathf.Lerp(c.a, Mathf.Lerp(0.4f, 1f, _handleGlow), Time.unscaledDeltaTime * 14f);
                _handleImage.color = c;
                float grow = Mathf.Lerp(1f, 1.15f, _handleGlow);
                _handle.localScale = Vector3.Lerp(_handle.localScale, new Vector3(grow, grow, 1f), Time.unscaledDeltaTime * 14f);
            }

            UpdateBeams();
        }

        // ---- hand beams ----

        /// <summary>
        /// A beam from each hand, shown whenever the hand points anywhere but at the board itself,
        /// so the drag bar is easy to aim for. Over the tiles it hides: tiles are chosen by gaze
        /// (a pinch confirms the gazed tile), and a beam there would point at something a pinch
        /// does not select.
        /// </summary>
        private void UpdateBeams()
        {
            if (_beams.Length != _hands.Length) BuildBeams();

            Transform head = Head();
            bool boardVisible = _group == null || _group.alpha > 0.5f;
            for (int i = 0; i < _hands.Length; i++)
            {
                float strength = 0f;
                bool onPlane = false;
                bool over = false;
                Vector3 end = default;
                Ray ray = default;

                if (head != null && boardVisible && TryGetRay(_hands[i], out ray))
                {
                    if (_hands[i] == _dragHand)
                    {
                        strength = 1f;
                        onPlane = over = true;
                        end = transform.TransformPoint(_dragGrabLocal);
                    }
                    else if (_dragHand == null)
                    {
                        float near = HandleProximity(ray, out end, out onPlane, out bool overTiles);
                        over = near >= 1f;
                        // Dim while free, brightening as it closes in on the bar; gone over the tiles.
                        strength = overTiles && !over ? 0f : Mathf.Lerp(FreeBeamStrength, 1f, near);
                        if (!onPlane) end = ray.GetPoint(FreeBeamLength);
                    }
                }

                _beamStrength[i] = Mathf.Lerp(_beamStrength[i], strength, Time.unscaledDeltaTime * 16f);
                bool show = strength > 0f && _beamStrength[i] > 0.02f;
                _beams[i].gameObject.SetActive(show);
                _beamDots[i].gameObject.SetActive(show && onPlane);
                if (!show) continue;

                // The pointer pose sits at the wrist; start the beam a little ahead of the hand.
                Vector3 start = ray.origin + ray.direction * BeamStartOffset;
                Vector3 along = end - start;
                float length = along.magnitude;
                if (length < 0.01f)
                {
                    _beams[i].gameObject.SetActive(false);
                    _beamDots[i].gameObject.SetActive(false);
                    continue;
                }
                Vector3 direction = along / length;

                // A flat strip along the ray, turned about its own axis to face the viewer.
                Vector3 facing = Vector3.ProjectOnPlane((start + end) * 0.5f - head.position, direction);
                if (facing.sqrMagnitude < 0.000001f) facing = transform.forward;
                float unit = Mathf.Max(transform.lossyScale.x, 0.000001f);
                RectTransform beam = _beams[i];
                beam.SetPositionAndRotation(start, Quaternion.LookRotation(facing.normalized, direction));
                beam.sizeDelta = new Vector2(BeamWidth / unit, length / unit);
                _beamImages[i].color = new Color(1f, 1f, 1f, 0.9f * _beamStrength[i]);

                // The dot marks where the beam meets the board, a hair in front of it.
                RectTransform dot = _beamDots[i];
                dot.SetPositionAndRotation(end - transform.forward * 0.002f, transform.rotation);
                float dotSize = (over ? BeamDotSize * 1.4f : BeamDotSize) / unit;
                dot.sizeDelta = new Vector2(dotSize, dotSize);
                _beamDotImages[i].color = new Color(1f, 1f, 1f, _beamStrength[i]);
            }
        }

        /// <summary>
        /// 1 when the ray is on the drag bar, falling to 0 a hand's width away from it.
        /// hit is where the ray meets the board plane; onPlane is false when it does not meet it
        /// within reach, and overTiles is true when that point lies on the board itself.
        /// </summary>
        private float HandleProximity(Ray ray, out Vector3 hit, out bool onPlane, out bool overTiles)
        {
            hit = default;
            onPlane = false;
            overTiles = false;
            if (_handle == null) return 0f;

            var plane = new Plane(-transform.forward, _handle.position);
            if (!plane.Raycast(ray, out float distance) || distance > MaxBeamLength) return 0f;
            hit = ray.GetPoint(distance);
            onPlane = true;

            var board = (RectTransform)transform;
            Vector3 onBoard = board.InverseTransformPoint(hit);
            overTiles = board.rect.Contains(new Vector2(onBoard.x, onBoard.y));

            Vector3 local = _handle.InverseTransformPoint(hit);
            Rect rect = _handle.rect;
            float dx = Mathf.Max(0f, Mathf.Abs(local.x - rect.center.x) - (rect.width * 0.5f + HandleHitPadding.x));
            float dy = Mathf.Max(0f, Mathf.Abs(local.y - rect.center.y) - (rect.height * 0.5f + HandleHitPadding.y));
            float outside = Mathf.Sqrt(dx * dx + dy * dy);
            return 1f - Smooth(0f, BeamFadeDistance, outside);
        }

        private void BuildBeams()
        {
            foreach (RectTransform old in _beams) if (old != null) Destroy(old.gameObject);
            foreach (RectTransform old in _beamDots) if (old != null) Destroy(old.gameObject);

            int n = _hands.Length;
            _beams = new RectTransform[n];
            _beamImages = new Image[n];
            _beamDots = new RectTransform[n];
            _beamDotImages = new Image[n];
            _beamStrength = new float[n];

            // Drawn as canvas images rather than line renderers: they use the board's own
            // material, so they are certain to render the same way in the headset build.
            Sprite strip = BeamSprite();
            Sprite pill = PillSprite();
            for (int i = 0; i < n; i++)
            {
                _beams[i] = NewImage("HandBeam", strip, new Vector2(0.5f, 0f), out _beamImages[i]);
                _beamDots[i] = NewImage("HandBeamDot", pill, new Vector2(0.5f, 0.5f), out _beamDotImages[i]);
                _beamDotImages[i].type = Image.Type.Simple;
                _beams[i].gameObject.SetActive(false);
                _beamDots[i].gameObject.SetActive(false);
            }
        }

        private RectTransform NewImage(string objectName, Sprite sprite, Vector2 pivot, out Image image)
        {
            var go = new GameObject(objectName, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = pivot;
            image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            // Layout groups on the canvas must not move it.
            go.AddComponent<LayoutElement>().ignoreLayout = true;
            return rect;
        }

        /// <summary>A strip that is soft at both edges and fades out toward the hand.</summary>
        private static Sprite BeamSprite()
        {
            const int width = 8;
            const int height = 64;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "HandBeamStrip",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                // Fades in from the hand and softens again at the tip, so a beam that ends in
                // mid-air does not stop dead.
                float t = y / (height - 1f);
                float alongRay = Smooth(0f, 0.35f, t) * Mathf.Lerp(1f, 0.35f, Smooth(0.8f, 1f, t));
                for (int x = 0; x < width; x++)
                {
                    float across = 1f - Mathf.Abs((x + 0.5f) / width * 2f - 1f);
                    byte alpha = (byte)(Mathf.Clamp01(across * 1.6f) * Mathf.Lerp(0.1f, 1f, alongRay) * 255f);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0f), 100f);
        }

        private void UpdateHands()
        {
            if (_hands.Length == 0 && Time.unscaledTime >= _nextHandSearch)
            {
                _nextHandSearch = Time.unscaledTime + 1f;
                _hands = FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                _wasPinching = new bool[_hands.Length];
            }

            if (_dragHand != null)
            {
                _handleGlow = 1f;
                UpdateDrag();
                return;
            }

            bool hovered = false;
            for (int i = 0; i < _hands.Length; i++)
            {
                OVRHand hand = _hands[i];
                if (hand == null) continue;

                bool pinching = TryGetRay(hand, out Ray ray) && hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
                bool started = pinching && !_wasPinching[i];
                _wasPinching[i] = pinching;

                if (!TryGetRay(hand, out ray) || !RayOverHandle(ray, out Vector3 hit, out float distance)) continue;
                hovered = true;
                if (started)
                {
                    BeginDrag(hand, ray, hit, distance);
                    return;
                }
            }
            _handleGlow = hovered ? 0.6f : 0f;
        }

        // ---- hand drag ----

        private void BeginDrag(OVRHand hand, Ray ray, Vector3 hit, float distance)
        {
            Transform head = Head();
            if (head == null) return;

            if (_pending != null)
            {
                StopCoroutine(_pending);
                _pending = null;
                _placedOnce = true;
                SetVisible(true);
            }
            _dragHand = hand;
            _dragDistance = distance;
            _dragStartReach = Vector3.Distance(ray.origin, head.position);
            _dragGrabLocal = transform.InverseTransformPoint(hit);
            _pinchLostFor = 0f;
            // The board slides under the head gaze while it moves; no tile may fire on the way.
            DwellSettings.Suspended = true;
        }

        private void UpdateDrag()
        {
            Transform head = Head();
            bool held = head != null && TryGetRay(_dragHand, out Ray ray)
                        && _dragHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            if (!held)
            {
                // Tracking drops a frame now and then; only a sustained release ends the drag.
                _pinchLostFor += Time.unscaledDeltaTime;
                if (_pinchLostFor > 0.2f || head == null) EndDrag();
                return;
            }
            _pinchLostFor = 0f;
            TryGetRay(_dragHand, out ray);

            // Pushing the hand forward sends the board away, pulling brings it closer.
            float reach = Vector3.Distance(ray.origin, head.position);
            float along = Mathf.Clamp(_dragDistance + (reach - _dragStartReach) * _pushPullGain, 0.3f, MaxDistance + 0.5f);
            Vector3 grabPoint = ray.GetPoint(along);

            // Keep the grabbed point under the ray and the board facing the head. The rotation
            // depends on the centre and the centre on the rotation, so settle it in two passes.
            Quaternion rotation = _targetRotation;
            Vector3 centre = _targetPosition;
            for (int pass = 0; pass < 2; pass++)
            {
                centre = grabPoint - rotation * Vector3.Scale(_dragGrabLocal, transform.localScale);
                Vector3 fromHead = centre - head.position;
                if (fromHead.sqrMagnitude < 0.0001f) break;
                rotation = Quaternion.LookRotation(fromHead, Vector3.up);
            }

            // Never closer than arm's length of the minimum, never beyond the maximum.
            Vector3 offset = centre - head.position;
            float clamped = Mathf.Clamp(offset.magnitude, MinDistance * 0.8f, MaxDistance + 0.3f);
            centre = head.position + offset.normalized * clamped;

            _targetPosition = centre;
            _targetRotation = rotation;
        }

        private void EndDrag()
        {
            _dragHand = null;
            DwellSettings.Suspended = false;

            Transform head = Head();
            if (head == null) return;

            // Remember how far away the user likes it; Recenter reuses it.
            Distance = Mathf.Clamp(Round1((_targetPosition - head.position).magnitude), MinDistance, MaxDistance);
            PlayerPrefs.SetFloat(DistanceKey, Distance);
            PlayerPrefs.Save();
            WhenChanged?.Invoke();
        }

        private bool RayOverHandle(Ray ray, out Vector3 hit, out float distance)
        {
            hit = default;
            distance = 0f;
            if (_handle == null) return false;

            var plane = new Plane(-transform.forward, _handle.position);
            if (!plane.Raycast(ray, out distance)) return false;
            hit = ray.GetPoint(distance);

            Vector3 local = _handle.InverseTransformPoint(hit);
            Rect rect = _handle.rect;
            return Mathf.Abs(local.x - rect.center.x) <= rect.width * 0.5f + HandleHitPadding.x
                && Mathf.Abs(local.y - rect.center.y) <= rect.height * 0.5f + HandleHitPadding.y;
        }

        /// <summary>The hand's pointing ray in world space, or false when the hand is not usable.</summary>
        private bool TryGetRay(OVRHand hand, out Ray ray)
        {
            ray = default;
            if (hand == null || !hand.isActiveAndEnabled || !hand.IsTracked || !hand.IsPointerPoseValid) return false;

            Transform pose = hand.PointerPose;
            if (pose == null) return false;

            // Without a pointer pose root, OVRHand keeps the pose in tracking space on a root object.
            Head();
            if (pose.parent == null && _trackingSpace != null)
            {
                ray = new Ray(_trackingSpace.TransformPoint(pose.localPosition),
                    _trackingSpace.TransformDirection(pose.localRotation * Vector3.forward));
            }
            else
            {
                ray = new Ray(pose.position, pose.forward);
            }
            return true;
        }

        private Transform Head()
        {
            if (_head != null) return _head;
            if (Time.unscaledTime < _nextRigSearch) return null;
            _nextRigSearch = Time.unscaledTime + 1f;

            // The rig's disabled eye cameras are tagged MainCamera too, so ask the rig itself.
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                _head = rig.centerEyeAnchor;
                _trackingSpace = rig.trackingSpace;
            }
            else if (Camera.main != null)
            {
                _head = Camera.main.transform;
            }
            return _head;
        }

        // ---- handle ----

        private void BuildHandle()
        {
            var go = new GameObject("MoveHandle", typeof(RectTransform));
            _handle = (RectTransform)go.transform;
            _handle.SetParent(transform, false);
            // Hangs under the board's bottom edge, like the grab bar of a system window.
            _handle.anchorMin = _handle.anchorMax = new Vector2(0.5f, 0f);
            _handle.pivot = new Vector2(0.5f, 1f);
            _handle.anchoredPosition = new Vector2(0f, -HandleGap);
            _handle.sizeDelta = new Vector2(HandleWidth, HandleHeight);

            _handleImage = go.AddComponent<Image>();
            _handleImage.sprite = PillSprite();
            _handleImage.type = Image.Type.Sliced;
            _handleImage.color = new Color(1f, 1f, 1f, 0.4f);
            _handleImage.raycastTarget = false;
        }

        /// <summary>A white capsule, sliced so it stretches to any width.</summary>
        private static Sprite PillSprite()
        {
            const int size = 32;
            float radius = size * 0.5f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "MoveHandlePill",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    byte alpha = (byte)(Mathf.Clamp01(radius - d) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            float border = radius - 1f;
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static float Round1(float value) => Mathf.Round(value * 10f) / 10f;

        /// <summary>0 below edge0, 1 above edge1, eased in between. (Mathf.SmoothStep interpolates values instead.)</summary>
        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.InverseLerp(edge0, edge1, x);
            return t * t * (3f - 2f * t);
        }
    }
}
