using System.Globalization;
using UnityEngine;

namespace VLat.Runtime
{
    /// <summary>
    /// VLatランタイム確認用のカメラ軌道操作とIMGUIオーバーレイを提供します。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RuntimeDebugController : MonoBehaviour
    {
        private const string MouseXAxis = "Mouse X";
        private const string MouseYAxis = "Mouse Y";
        private const float OverlayWidth = 340f;
        private const float OverlayHeight = 285f;
        private const int MaximumDisplayedModifierCount = 512;

        [Header("Kakudoll")]
        [SerializeField]
        private VlatApplier _applier = null;

        [Header("Orbit Camera")]
        [SerializeField]
        private Camera _orbitCamera;

        [SerializeField]
        private Transform _orbitTarget = null;

        [SerializeField]
        private Vector3 _targetOffset = Vector3.zero;

        [SerializeField]
        [Min(0.01f)]
        private float _distance = 2f;

        [SerializeField]
        [Min(0.01f)]
        private float _minimumDistance = 0.25f;

        [SerializeField]
        [Min(0.01f)]
        private float _maximumDistance = 10f;

        [SerializeField]
        [Min(0f)]
        private float _rotationSensitivity = 4f;

        [SerializeField]
        [Min(0f)]
        private float _zoomSensitivity = 0.15f;

        [SerializeField]
        [Range(0f, 89f)]
        private float _maximumPitch = 85f;

        private float _orbitYaw;
        private float _orbitPitch;
        private bool _orbitInitialized;
        private string[] _yawLabels;
        private string[] _pitchLabels;
        private string[] _activeBandLabels;
        private string[] _modifierLabels;

        private Transform OrbitTarget
        {
            get
            {
                if (_orbitTarget != null)
                {
                    return _orbitTarget;
                }

                return _applier != null ? _applier.transform : null;
            }
        }

        private void OnEnable()
        {
            EnsureLabelCache();
            _orbitInitialized = false;
            TryInitializeOrbit();
        }

        private void Start()
        {
            if (_orbitCamera == null)
            {
                _orbitCamera = Camera.main;
            }

            TryInitializeOrbit();
        }

        private void Update()
        {
            if (!TryInitializeOrbit())
            {
                return;
            }

            if (Input.GetMouseButton(1))
            {
                _orbitYaw += Input.GetAxisRaw(MouseXAxis) * _rotationSensitivity;
                _orbitPitch -= Input.GetAxisRaw(MouseYAxis) * _rotationSensitivity;
                _orbitPitch = Mathf.Clamp(_orbitPitch, -_maximumPitch, _maximumPitch);
            }

            var scroll = Input.mouseScrollDelta.y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                _distance *= Mathf.Exp(-scroll * _zoomSensitivity);
                _distance = Mathf.Clamp(_distance, _minimumDistance, _maximumDistance);
            }

            ApplyOrbitTransform();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 12f, OverlayWidth, OverlayHeight), GUI.skin.box);
            GUILayout.Label("Kakudoll Runtime Debug");

            if (_applier == null)
            {
                GUILayout.Label("VlatApplier is not assigned.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(_yawLabels[QuantizeAngle(_applier.CurrentYaw, -180, 180)]);
            GUILayout.Label(_pitchLabels[QuantizeAngle(_applier.CurrentPitch, -90, 90)]);
            GUILayout.Label(_activeBandLabels[ClampInt(_applier.ActiveBandCount, 0, 64)]);
            GUILayout.Label(_modifierLabels[ClampInt(
                _applier.AppliedModifierCount,
                0,
                MaximumDisplayedModifierCount)]);

            GUILayout.BeginHorizontal();
            var previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = _applier.ApplicationEnabled ? Color.white : Color.cyan;
            if (GUILayout.Button("Before"))
            {
                _applier.ApplicationEnabled = false;
            }

            GUI.backgroundColor = _applier.ApplicationEnabled ? Color.cyan : Color.white;
            if (GUILayout.Button("After"))
            {
                _applier.ApplicationEnabled = true;
            }

            GUI.backgroundColor = previousBackground;
            GUILayout.EndHorizontal();

            var overrideEnabled = GUILayout.Toggle(
                _applier.DebugAngleOverrideEnabled,
                "Debug Angle Override");
            if (overrideEnabled != _applier.DebugAngleOverrideEnabled)
            {
                _applier.DebugAngleOverrideEnabled = overrideEnabled;
            }

            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && overrideEnabled;

            GUILayout.Label(_yawLabels[QuantizeAngle(_applier.DebugYaw, -180, 180)]);
            var debugYaw = GUILayout.HorizontalSlider(_applier.DebugYaw, -180f, 180f);
            if (!Mathf.Approximately(debugYaw, _applier.DebugYaw))
            {
                _applier.DebugYaw = debugYaw;
            }

            GUILayout.Label(_pitchLabels[QuantizeAngle(_applier.DebugPitch, -90, 90)]);
            var debugPitch = GUILayout.HorizontalSlider(_applier.DebugPitch, -90f, 90f);
            if (!Mathf.Approximately(debugPitch, _applier.DebugPitch))
            {
                _applier.DebugPitch = debugPitch;
            }

            GUI.enabled = previousEnabled;
            GUILayout.Label("Orbit: right drag / mouse wheel");
            GUILayout.EndArea();
        }

        private void EnsureLabelCache()
        {
            if (_yawLabels != null)
            {
                return;
            }

            _yawLabels = CreateAngleLabels("Yaw: ", -180, 180);
            _pitchLabels = CreateAngleLabels("Pitch: ", -90, 90);
            _activeBandLabels = new string[65];
            for (var index = 0; index < _activeBandLabels.Length; index++)
            {
                _activeBandLabels[index] = "Active bands: " +
                    index.ToString(CultureInfo.InvariantCulture);
            }

            _modifierLabels = new string[MaximumDisplayedModifierCount + 1];
            for (var index = 0; index < MaximumDisplayedModifierCount; index++)
            {
                _modifierLabels[index] = "Applied modifiers: " +
                    index.ToString(CultureInfo.InvariantCulture);
            }

            _modifierLabels[MaximumDisplayedModifierCount] =
                "Applied modifiers: " + MaximumDisplayedModifierCount + "+";
        }

        private static string[] CreateAngleLabels(string prefix, int minimum, int maximum)
        {
            var labels = new string[(maximum - minimum) + 1];
            for (var value = minimum; value <= maximum; value++)
            {
                labels[value - minimum] = prefix +
                    value.ToString(CultureInfo.InvariantCulture) + " deg";
            }

            return labels;
        }

        private static int QuantizeAngle(float value, int minimum, int maximum)
        {
            var rounded = (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);
            return ClampInt(rounded, minimum, maximum) - minimum;
        }

        private static int ClampInt(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }

        private bool TryInitializeOrbit()
        {
            if (_orbitInitialized)
            {
                return true;
            }

            var target = OrbitTarget;
            if (_orbitCamera == null || target == null)
            {
                return false;
            }

            var targetPosition = target.position + _targetOffset;
            var offset = _orbitCamera.transform.position - targetPosition;
            var offsetMagnitude = offset.magnitude;
            if (offsetMagnitude > 0.0001f)
            {
                _distance = Mathf.Clamp(offsetMagnitude, _minimumDistance, _maximumDistance);
                _orbitYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                _orbitPitch = Mathf.Asin(Mathf.Clamp(offset.y / offsetMagnitude, -1f, 1f))
                    * Mathf.Rad2Deg;
                _orbitPitch = Mathf.Clamp(_orbitPitch, -_maximumPitch, _maximumPitch);
            }
            else
            {
                _distance = Mathf.Clamp(_distance, _minimumDistance, _maximumDistance);
            }

            _orbitInitialized = true;
            ApplyOrbitTransform();
            return true;
        }

        private void ApplyOrbitTransform()
        {
            var target = OrbitTarget;
            if (_orbitCamera == null || target == null)
            {
                return;
            }

            var yawRadians = _orbitYaw * Mathf.Deg2Rad;
            var pitchRadians = _orbitPitch * Mathf.Deg2Rad;
            var horizontalScale = Mathf.Cos(pitchRadians) * _distance;
            var offset = new Vector3(
                Mathf.Sin(yawRadians) * horizontalScale,
                Mathf.Sin(pitchRadians) * _distance,
                Mathf.Cos(yawRadians) * horizontalScale);
            var targetPosition = target.position + _targetOffset;
            var cameraTransform = _orbitCamera.transform;
            cameraTransform.position = targetPosition + offset;
            cameraTransform.rotation = Quaternion.LookRotation(-offset, Vector3.up);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _minimumDistance = Mathf.Max(0.01f, _minimumDistance);
            _maximumDistance = Mathf.Max(_minimumDistance, _maximumDistance);
            _distance = Mathf.Clamp(_distance, _minimumDistance, _maximumDistance);
            _rotationSensitivity = Mathf.Max(0f, _rotationSensitivity);
            _zoomSensitivity = Mathf.Max(0f, _zoomSensitivity);
            _maximumPitch = Mathf.Clamp(_maximumPitch, 0f, 89f);
            _orbitInitialized = false;
        }
#endif
    }
}
