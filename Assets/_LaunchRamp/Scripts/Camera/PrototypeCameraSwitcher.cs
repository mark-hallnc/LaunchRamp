using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Camera
{
    /// <summary>Switches between the truck-mounted view and the fixed course overview.</summary>
    public sealed class PrototypeCameraSwitcher : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera driverCamera;
        [SerializeField] private UnityEngine.Camera diagnosticCamera;
        [SerializeField] private Transform diagnosticTarget;
        [SerializeField, Range(10f, 100f)] private float orbitDistance = 60f;
        [SerializeField] private float orbitSensitivity = .15f;
        [SerializeField] private float zoomSensitivity = .02f;
        [SerializeField] private float driverLookSensitivity = .12f;

        private InputAction _switchCamera;
        private InputAction _look;
        private InputAction _zoom;
        private InputAction _orbitButton;
        private bool _usingDiagnosticCamera = true;
        private float _orbitYaw;
        private float _orbitPitch = 25f;
        private float _driverYaw;
        private float _driverPitch;
        private Quaternion _driverBaseRotation;

        public void Configure(UnityEngine.Camera driver, UnityEngine.Camera diagnostic, Transform target)
        {
            driverCamera = driver;
            diagnosticCamera = diagnostic;
            diagnosticTarget = target;
            InitializeCameraAngles();
            ApplyCameraState();
        }

        private void Awake()
        {
            _switchCamera = new InputAction("Switch Camera", InputActionType.Button, "<Keyboard>/c");
            _switchCamera.AddBinding("<Gamepad>/rightShoulder");
            _look = new InputAction("Camera Look", InputActionType.Value, "<Mouse>/delta");
            _zoom = new InputAction("Diagnostic Zoom", InputActionType.Value, "<Mouse>/scroll");
            _orbitButton = new InputAction("Camera Look Button", InputActionType.Button, "<Mouse>/rightButton");
            InitializeCameraAngles();
            ApplyCameraState();
        }

        private void OnEnable()
        {
            _switchCamera?.Enable();
            _look?.Enable();
            _zoom?.Enable();
            _orbitButton?.Enable();
            if (_switchCamera != null) _switchCamera.performed += OnSwitchCamera;
        }

        private void OnDisable()
        {
            if (_switchCamera != null) _switchCamera.performed -= OnSwitchCamera;
            _switchCamera?.Disable();
            _look?.Disable();
            _zoom?.Disable();
            _orbitButton?.Disable();
        }

        private void OnDestroy()
        {
            _switchCamera?.Dispose();
            _look?.Dispose();
            _zoom?.Dispose();
            _orbitButton?.Dispose();
        }

        private void Update()
        {
            Vector2 look = _look?.ReadValue<Vector2>() ?? Vector2.zero;
            if (_usingDiagnosticCamera)
            {
                Vector2 scroll = _zoom?.ReadValue<Vector2>() ?? Vector2.zero;
                orbitDistance = Mathf.Clamp(orbitDistance - scroll.y * zoomSensitivity, 10f, 100f);
                if (_orbitButton?.IsPressed() == true)
                {
                    _orbitYaw += look.x * orbitSensitivity;
                    _orbitPitch = Mathf.Clamp(_orbitPitch - look.y * orbitSensitivity, 10f, 80f);
                }
                UpdateDiagnosticCamera();
            }
            else if (_orbitButton?.IsPressed() == true && driverCamera != null)
            {
                _driverYaw = Mathf.Clamp(_driverYaw + look.x * driverLookSensitivity, -70f, 70f);
                _driverPitch = Mathf.Clamp(_driverPitch - look.y * driverLookSensitivity, -35f, 35f);
                driverCamera.transform.localRotation = _driverBaseRotation * Quaternion.Euler(_driverPitch, _driverYaw, 0f);
            }
        }

        private void OnSwitchCamera(InputAction.CallbackContext context)
        {
            _usingDiagnosticCamera = !_usingDiagnosticCamera;
            ApplyCameraState();
        }

        private void ApplyCameraState()
        {
            SetCameraActive(driverCamera, !_usingDiagnosticCamera);
            SetCameraActive(diagnosticCamera, _usingDiagnosticCamera);
        }

        private void InitializeCameraAngles()
        {
            if (driverCamera != null) _driverBaseRotation = driverCamera.transform.localRotation;
            if (diagnosticCamera == null || diagnosticTarget == null) return;
            Vector3 offset = diagnosticCamera.transform.position - diagnosticTarget.position;
            orbitDistance = Mathf.Clamp(offset.magnitude, 10f, 100f);
            _orbitYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            _orbitPitch = Mathf.Asin(Mathf.Clamp(offset.y / orbitDistance, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void UpdateDiagnosticCamera()
        {
            if (diagnosticCamera == null || diagnosticTarget == null) return;
            float yaw = _orbitYaw * Mathf.Deg2Rad;
            float pitch = _orbitPitch * Mathf.Deg2Rad;
            Vector3 offset = new(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));
            diagnosticCamera.transform.position = diagnosticTarget.position + offset * orbitDistance;
            diagnosticCamera.transform.rotation = Quaternion.LookRotation(diagnosticTarget.position - diagnosticCamera.transform.position);
        }

        private static void SetCameraActive(UnityEngine.Camera target, bool active)
        {
            if (target == null) return;
            target.enabled = active;
            AudioListener listener = target.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }
    }
}
