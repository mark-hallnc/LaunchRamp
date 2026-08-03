using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Camera
{
    /// <summary>Switches between the truck-mounted view and the fixed course overview.</summary>
    public sealed class PrototypeCameraSwitcher : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera driverCamera;
        [SerializeField] private UnityEngine.Camera diagnosticCamera;

        private InputAction _switchCamera;
        private bool _usingDiagnosticCamera = true;

        public void Configure(UnityEngine.Camera driver, UnityEngine.Camera diagnostic)
        {
            driverCamera = driver;
            diagnosticCamera = diagnostic;
            ApplyCameraState();
        }

        private void Awake()
        {
            _switchCamera = new InputAction("Switch Camera", InputActionType.Button, "<Keyboard>/c");
            _switchCamera.AddBinding("<Gamepad>/rightShoulder");
            ApplyCameraState();
        }

        private void OnEnable()
        {
            _switchCamera?.Enable();
            if (_switchCamera != null) _switchCamera.performed += OnSwitchCamera;
        }

        private void OnDisable()
        {
            if (_switchCamera != null) _switchCamera.performed -= OnSwitchCamera;
            _switchCamera?.Disable();
        }

        private void OnDestroy() => _switchCamera?.Dispose();

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

        private static void SetCameraActive(UnityEngine.Camera target, bool active)
        {
            if (target == null) return;
            target.enabled = active;
            AudioListener listener = target.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }
    }
}
