using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Camera
{
    /// <summary>Switches between the truck-mounted view and the fixed course overview.</summary>
    public sealed class PrototypeCameraSwitcher : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera driverCamera;
        [SerializeField] private UnityEngine.Camera diagnosticCamera;
        [SerializeField] private UnityEngine.Camera exteriorCamera;
        [SerializeField] private Transform diagnosticTarget;
        [SerializeField] private Transform exteriorTarget;
        [SerializeField, Range(10f, 100f)] private float orbitDistance = 60f;
        [SerializeField] private float orbitSensitivity = .15f;
        [SerializeField] private float zoomSensitivity = .02f;
        [SerializeField] private float driverLookSensitivity = .12f;
        [SerializeField] private bool returnDriverViewToForward;
        [SerializeField] private float driverReturnSpeed = 45f;
        [SerializeField] private Vector3 exteriorOffset = new(0f, 6f, -10f);

        private InputAction _switchCamera;
        private InputAction _look;
        private InputAction _zoom;
        private InputAction _orbitButton;
        private InputAction _exteriorToggle;
        private bool _usingDiagnosticCamera;
        private bool _usingExteriorCamera;
        private bool _previousDiagnosticState;
        private float _orbitYaw;
        private float _orbitPitch = 25f;
        private float _driverYaw;
        private float _driverPitch;
        private Quaternion _driverBaseRotation;

        public void Configure(UnityEngine.Camera driver, UnityEngine.Camera diagnostic,
            UnityEngine.Camera exterior, Transform diagnosticLookTarget, Transform followTarget)
        {
            driverCamera = driver;
            diagnosticCamera = diagnostic;
            exteriorCamera = exterior;
            diagnosticTarget = diagnosticLookTarget;
            exteriorTarget = followTarget;
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
            _exteriorToggle = new InputAction("Exterior Camera", InputActionType.Button, "<Keyboard>/v");
            InitializeCameraAngles();
            ApplyCameraState();
        }

        private void OnEnable()
        {
            _switchCamera?.Enable();
            _look?.Enable();
            _zoom?.Enable();
            _orbitButton?.Enable();
            _exteriorToggle?.Enable();
            if (_switchCamera != null) _switchCamera.performed += OnSwitchCamera;
            if (_exteriorToggle != null) _exteriorToggle.performed += OnExteriorToggle;
        }

        private void OnDisable()
        {
            if (_switchCamera != null) _switchCamera.performed -= OnSwitchCamera;
            if (_exteriorToggle != null) _exteriorToggle.performed -= OnExteriorToggle;
            _switchCamera?.Disable();
            _look?.Disable();
            _zoom?.Disable();
            _orbitButton?.Disable();
            _exteriorToggle?.Disable();
        }

        private void OnDestroy()
        {
            _switchCamera?.Dispose();
            _look?.Dispose();
            _zoom?.Dispose();
            _orbitButton?.Dispose();
            _exteriorToggle?.Dispose();
        }

        private void Update()
        {
            Vector2 look = _look?.ReadValue<Vector2>() ?? Vector2.zero;
            if (_usingExteriorCamera)
            {
                UpdateExteriorCamera();
            }
            else if (_usingDiagnosticCamera)
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
                _driverYaw = Mathf.Clamp(_driverYaw + look.x * driverLookSensitivity, -85f, 85f);
                _driverPitch = Mathf.Clamp(_driverPitch - look.y * driverLookSensitivity, -35f, 45f);
                driverCamera.transform.localRotation = _driverBaseRotation * Quaternion.Euler(_driverPitch, _driverYaw, 0f);
            }
            else if (returnDriverViewToForward && driverCamera != null)
            {
                _driverYaw = Mathf.MoveTowards(_driverYaw, 0f, driverReturnSpeed * Time.deltaTime);
                _driverPitch = Mathf.MoveTowards(_driverPitch, 0f, driverReturnSpeed * Time.deltaTime);
                driverCamera.transform.localRotation = _driverBaseRotation * Quaternion.Euler(_driverPitch, _driverYaw, 0f);
            }
        }

        private void OnSwitchCamera(InputAction.CallbackContext context)
        {
            _usingExteriorCamera = false;
            _usingDiagnosticCamera = !_usingDiagnosticCamera;
            ApplyCameraState();
        }

        private void OnExteriorToggle(InputAction.CallbackContext context)
        {
            if (!_usingExteriorCamera) _previousDiagnosticState = _usingDiagnosticCamera;
            _usingExteriorCamera = !_usingExteriorCamera;
            if (!_usingExteriorCamera) _usingDiagnosticCamera = _previousDiagnosticState;
            ApplyCameraState();
        }

        private void ApplyCameraState()
        {
            SetCameraActive(driverCamera, !_usingExteriorCamera && !_usingDiagnosticCamera);
            SetCameraActive(diagnosticCamera, !_usingExteriorCamera && _usingDiagnosticCamera);
            SetCameraActive(exteriorCamera, _usingExteriorCamera);
        }

        private void UpdateExteriorCamera()
        {
            if (exteriorCamera == null || exteriorTarget == null) return;
            Vector3 desiredPosition = exteriorTarget.TransformPoint(exteriorOffset);
            exteriorCamera.transform.position = Vector3.Lerp(exteriorCamera.transform.position,
                desiredPosition, 1f - Mathf.Exp(-6f * Time.deltaTime));
            Vector3 lookPoint = exteriorTarget.position + exteriorTarget.up * 1f;
            exteriorCamera.transform.rotation = Quaternion.LookRotation(lookPoint - exteriorCamera.transform.position,
                Vector3.up);
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

    /// <summary>Development-only mirror viewport and aim visualization, toggled with F4.</summary>
    public sealed class PrototypeMirrorDebug : MonoBehaviour
    {
        [SerializeField] private GameObject overlay;
        [SerializeField] private UnityEngine.Camera leftMirrorCamera;
        [SerializeField] private UnityEngine.Camera rightMirrorCamera;
        [SerializeField] private TMP_Text details;
        private InputAction _toggle;
        private bool _visible;

        public void Configure(GameObject overlayRoot, UnityEngine.Camera left, UnityEngine.Camera right, TMP_Text label)
        {
            overlay = overlayRoot; leftMirrorCamera = left; rightMirrorCamera = right; details = label;
            if (overlay != null) overlay.SetActive(false);
        }

        private void Awake()
        {
            _toggle = new InputAction("Toggle Mirror Debug", InputActionType.Button, "<Keyboard>/f4");
            if (overlay != null) overlay.SetActive(false);
        }

        private void OnEnable()
        {
            _toggle?.Enable();
            if (_toggle != null) _toggle.performed += OnToggle;
        }

        private void OnDisable()
        {
            if (_toggle != null) _toggle.performed -= OnToggle;
            _toggle?.Disable();
        }

        private void OnDestroy() => _toggle?.Dispose();

        private void Update()
        {
            if (!_visible) return;
            DrawAim(leftMirrorCamera, Color.cyan);
            DrawAim(rightMirrorCamera, Color.magenta);
            if (details != null && leftMirrorCamera != null && rightMirrorCamera != null)
                details.text = $"MIRROR TUNING\nLeft FOV {leftMirrorCamera.fieldOfView:F0} deg  aim {leftMirrorCamera.transform.localEulerAngles}\n" +
                               $"Right FOV {rightMirrorCamera.fieldOfView:F0} deg  aim {rightMirrorCamera.transform.localEulerAngles}";
        }

        private void OnToggle(InputAction.CallbackContext context)
        {
            _visible = !_visible && (Debug.isDebugBuild || Application.isEditor);
            if (overlay != null) overlay.SetActive(_visible);
        }

        private static void DrawAim(UnityEngine.Camera camera, Color color)
        {
            if (camera != null) Debug.DrawRay(camera.transform.position, camera.transform.forward * 12f, color);
        }
    }
}
