using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Input
{
    /// <summary>Restores both dynamic bodies without replacing or bypassing their physical hitch.</summary>
    public sealed class VehicleRigReset : MonoBehaviour
    {
        [SerializeField] private Rigidbody truckBody;
        [SerializeField] private Rigidbody trailerBody;

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

            ResetBody(truckBody, _truckPosition, _truckRotation);
            ResetBody(trailerBody, _trailerPosition, _trailerRotation);
            Debug.Log("[Launch Ramp] Truck and trailer reset to their spawn poses.", this);
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
