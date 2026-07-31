using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Input
{
    /// <summary>Owns replaceable input bindings, keeping input separate from physics.</summary>
    public sealed class VehicleInputReader : MonoBehaviour
    {
        private InputAction _drive;
        private InputAction _steer;
        private InputAction _parkingBrake;

        public float Drive => _drive?.ReadValue<float>() ?? 0f;
        public float Steering => _steer?.ReadValue<float>() ?? 0f;
        public bool ParkingBrake => _parkingBrake?.IsPressed() ?? false;

        private void Awake()
        {
            _drive = new InputAction("Drive", InputActionType.Value);
            _drive.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
            _drive.AddBinding("<Gamepad>/rightTrigger");
            _drive.AddBinding("<Gamepad>/leftTrigger").WithProcessor("invert");
            _steer = new InputAction("Steer", InputActionType.Value);
            _steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            _steer.AddBinding("<Gamepad>/leftStick/x");
            _parkingBrake = new InputAction("Parking Brake", InputActionType.Button);
            _parkingBrake.AddBinding("<Keyboard>/space");
            _parkingBrake.AddBinding("<Gamepad>/buttonSouth");
        }

        private void OnEnable() { _drive?.Enable(); _steer?.Enable(); _parkingBrake?.Enable(); }
        private void OnDisable() { _drive?.Disable(); _steer?.Disable(); _parkingBrake?.Disable(); }
        private void OnDestroy() { _drive?.Dispose(); _steer?.Dispose(); _parkingBrake?.Dispose(); }
    }
}
