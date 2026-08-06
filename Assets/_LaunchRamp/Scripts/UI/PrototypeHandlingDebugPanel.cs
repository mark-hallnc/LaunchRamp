using LaunchRamp.Trailer;
using LaunchRamp.Vehicle;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.UI
{
    /// <summary>Development-only backing telemetry; not intended as final HUD.</summary>
    public sealed class PrototypeHandlingDebugPanel : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text text;
        [SerializeField] private PrototypeTruckController truckController;
        [SerializeField] private Rigidbody truckBody;
        [SerializeField] private Rigidbody trailerBody;
        [SerializeField] private Transform truckHitch;
        [SerializeField] private Transform trailerHitch;
        [SerializeField] private PassiveTrailerAxle passiveAxle;
        private InputAction _toggle;

        public void Configure(GameObject root, TMP_Text targetText, PrototypeTruckController controller,
            Rigidbody truck, Rigidbody trailer, Transform truckAnchor,
            Transform trailerAnchor, PassiveTrailerAxle axle)
        {
            panelRoot = root; text = targetText; truckController = controller;
            truckBody = truck; trailerBody = trailer; truckHitch = truckAnchor;
            trailerHitch = trailerAnchor; passiveAxle = axle;
        }

        private void Awake()
        {
            _toggle = new InputAction("Toggle Handling Diagnostics", InputActionType.Button, "<Keyboard>/f3");
            if (panelRoot != null) panelRoot.SetActive(Debug.isDebugBuild || Application.isEditor);
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
            if (text == null || panelRoot == null || !panelRoot.activeSelf || truckController == null) return;
            float yaw = truckBody != null && trailerBody != null
                ? Mathf.DeltaAngle(0f, (Quaternion.Inverse(truckBody.rotation) * trailerBody.rotation).eulerAngles.y) : 0f;
            float separation = truckHitch != null && trailerHitch != null
                ? Vector3.Distance(truckHitch.position, trailerHitch.position) : 0f;
            text.text = $"BACKING TEST\nSpeed  {Mathf.Abs(truckController.ForwardSpeedMilesPerHour):F1} mph\n" +
                        $"Gear  {truckController.GearLabel}\n" +
                        $"Accelerator  {truckController.AcceleratorInput * 100f:F0}%\n" +
                        $"Service brake  {truckController.ServiceBrakeInput * 100f:F0}%\n" +
                        $"Steer  {truckController.SteeringInput:F2}\n" +
                        $"Parking brake  {(truckController.ParkingBrakeApplied ? "On" : "Off")}\n" +
                        $"Trailer yaw  {yaw:F1} deg\nHitch separation  {separation:F3} m\n" +
                        $"Axle grounded  L:{passiveAxle != null && passiveAxle.LeftGrounded} " +
                        $"R:{passiveAxle != null && passiveAxle.RightGrounded}";
        }

        private void OnToggle(InputAction.CallbackContext context)
        {
            if (panelRoot != null) panelRoot.SetActive(!panelRoot.activeSelf);
        }
    }
}
