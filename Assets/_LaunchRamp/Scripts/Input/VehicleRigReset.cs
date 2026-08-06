using UnityEngine;
using UnityEngine.InputSystem;
using LaunchRamp.Vehicle;

namespace LaunchRamp.Input
{
    /// <summary>Restores both dynamic bodies without replacing or bypassing their physical hitch.</summary>
    public sealed class VehicleRigReset : MonoBehaviour
    {
        [SerializeField] private Rigidbody truckBody;
        [SerializeField] private Rigidbody trailerBody;
        [SerializeField] private bool hasPracticeSpawn;
        [SerializeField] private Vector3 practiceTruckPosition;
        [SerializeField] private Quaternion practiceTruckRotation = Quaternion.identity;
        [SerializeField] private Vector3 practiceTrailerPosition;
        [SerializeField] private Quaternion practiceTrailerRotation = Quaternion.identity;

        private InputAction _resetAction;
        private Vector3 _truckPosition;
        private Quaternion _truckRotation;
        private Vector3 _trailerPosition;
        private Quaternion _trailerRotation;

        public void Configure(Rigidbody truck, Rigidbody trailer)
        {
            truckBody = truck;
            trailerBody = trailer;
        }

        public void ConfigurePracticeSpawn(Vector3 truckPosition, Quaternion truckRotation,
            Vector3 trailerPosition, Quaternion trailerRotation)
        {
            hasPracticeSpawn = true;
            practiceTruckPosition = truckPosition; practiceTruckRotation = truckRotation;
            practiceTrailerPosition = trailerPosition; practiceTrailerRotation = trailerRotation;
        }

        private void Awake()
        {
            _resetAction = new InputAction("Reset Vehicle Rig", InputActionType.Button, "<Keyboard>/r");
            _resetAction.AddBinding("<Gamepad>/start");
            CaptureSpawnPose();
        }

        private void OnEnable()
        {
            _resetAction?.Enable();
            if (_resetAction != null) _resetAction.performed += OnReset;
        }

        private void OnDisable()
        {
            if (_resetAction != null) _resetAction.performed -= OnReset;
            _resetAction?.Disable();
        }

        private void OnDestroy() => _resetAction?.Dispose();

        private void CaptureSpawnPose()
        {
            if (truckBody != null) { _truckPosition = truckBody.position; _truckRotation = truckBody.rotation; }
            if (trailerBody != null) { _trailerPosition = trailerBody.position; _trailerRotation = trailerBody.rotation; }
        }

        private void OnReset(InputAction.CallbackContext context)
        {
            if (truckBody == null || trailerBody == null)
            {
                Debug.LogError("[Launch Ramp] Cannot reset rig: truck or trailer Rigidbody is missing.", this);
                return;
            }

            bool usePractice = hasPracticeSpawn && Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            ResetBody(truckBody, usePractice ? practiceTruckPosition : _truckPosition,
                usePractice ? practiceTruckRotation : _truckRotation);
            ResetBody(trailerBody, usePractice ? practiceTrailerPosition : _trailerPosition,
                usePractice ? practiceTrailerRotation : _trailerRotation);
            PrototypeTruckController controller = truckBody.GetComponent<PrototypeTruckController>();
            if (controller != null) controller.ResetToPark();
            Debug.Log(usePractice
                ? "[Launch Ramp] Truck and trailer reset to the ramp practice spawn."
                : hasPracticeSpawn
                    ? "[Launch Ramp] Truck and trailer reset to their entrance spawn poses."
                    : "[Launch Ramp] Truck and trailer reset to their original spawn poses.", this);
        }

        private static void ResetBody(Rigidbody body, Vector3 position, Quaternion rotation)
        {
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.WakeUp();
        }
    }
}

namespace LaunchRamp.Environment
{
    /// <summary>One-shot gray-box scenario marker; intentionally contains no scoring or objectives.</summary>
    public sealed class BoatRampScenarioTrigger : MonoBehaviour
    {
        [SerializeField] private string message;
        private bool _reported;

        public void Configure(string value) => message = value;

        private void OnTriggerEnter(Collider other)
        {
            if (_reported || other.attachedRigidbody == null ||
                other.attachedRigidbody.GetComponent<LaunchRamp.Vehicle.PrototypeTruckController>() == null) return;
            _reported = true;
            Debug.Log($"[Launch Ramp] {message}", this);
        }
    }

    /// <summary>F5 sight-line visualization for crest/rear-window calibration.</summary>
    public sealed class BoatRampSightLineDebug : MonoBehaviour
    {
        [SerializeField] private Transform driverEye;
        [SerializeField] private Transform trailerTopReference;
        [SerializeField] private Transform crestReference;
        [SerializeField] private GameObject markerRoot;
        private InputAction _toggle;
        private bool _visible;

        public void Configure(Transform eye, Transform trailerTop, Transform crest, GameObject markers)
        {
            driverEye = eye; trailerTopReference = trailerTop; crestReference = crest; markerRoot = markers;
            if (markerRoot != null) markerRoot.SetActive(false);
        }

        private void Awake()
        {
            _toggle = new InputAction("Toggle Ramp Sight Lines", InputActionType.Button, "<Keyboard>/f5");
            if (markerRoot != null) markerRoot.SetActive(false);
        }

        private void OnEnable()
        {
            _toggle?.Enable();
            if (_toggle != null) _toggle.performed += Toggle;
        }

        private void OnDisable()
        {
            if (_toggle != null) _toggle.performed -= Toggle;
            _toggle?.Disable();
        }

        private void OnDestroy() => _toggle?.Dispose();

        private void Update()
        {
            if (!_visible || driverEye == null || trailerTopReference == null) return;
            Transform eyeMarker = markerRoot != null ? markerRoot.transform.Find("DriverEyeMarker") : null;
            Transform trailerMarker = markerRoot != null ? markerRoot.transform.Find("TrailerTopMarker") : null;
            Transform crestMarker = markerRoot != null ? markerRoot.transform.Find("CrestMarker") : null;
            if (eyeMarker != null) eyeMarker.position = driverEye.position;
            if (trailerMarker != null) trailerMarker.position = trailerTopReference.position;
            if (crestMarker != null && crestReference != null) crestMarker.position = crestReference.position;
            Debug.DrawRay(driverEye.position, -driverEye.forward * 35f, Color.cyan);
            Debug.DrawLine(driverEye.position, trailerTopReference.position, Color.yellow);
            if (crestReference != null) Debug.DrawLine(driverEye.position, crestReference.position, Color.green);
        }

        private void Toggle(InputAction.CallbackContext context)
        {
            _visible = !_visible && (Application.isEditor || Debug.isDebugBuild);
            if (markerRoot != null) markerRoot.SetActive(_visible);
        }
    }
}
