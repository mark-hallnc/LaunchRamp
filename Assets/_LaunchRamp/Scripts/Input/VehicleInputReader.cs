using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Input
{
    public enum GearCommand { None, ToggleDriveReverse, Neutral, Park }

    /// <summary>Replaceable Input System bindings with independent pedals and latched controls.</summary>
    public sealed class VehicleInputReader : MonoBehaviour
    {
        private const float KeyboardBrakePressSeconds = .275f;
        private const float KeyboardBrakeReleaseSeconds = .15f;
        private InputAction _accelerator;
        private InputAction _controllerBrake;
        private InputAction _keyboardBrake;
        private InputAction _steer;
        private InputAction _parkingBrakeToggle;
        private InputAction _toggleDirection;
        private InputAction _neutral;
        private InputAction _park;
        private float _keyboardBrakeValue;
        private bool _parkingBrake;
        private bool _suppressPedalsUntilReleased;
        private GearCommand _pendingGearCommand;

        public float Accelerator => _suppressPedalsUntilReleased ? 0f : Mathf.Clamp01(_accelerator?.ReadValue<float>() ?? 0f);
        public float ServiceBrake => _suppressPedalsUntilReleased ? 0f : Mathf.Max(
            Mathf.Clamp01(_controllerBrake?.ReadValue<float>() ?? 0f), _keyboardBrakeValue);
        public float Steering => _steer?.ReadValue<float>() ?? 0f;
        public bool ParkingBrake => _parkingBrake;
        public float KeyboardBrakePressTime => KeyboardBrakePressSeconds;
        public float KeyboardBrakeReleaseTime => KeyboardBrakeReleaseSeconds;

        private void Awake()
        {
            _accelerator = new InputAction("Accelerator", InputActionType.Value);
            _accelerator.AddBinding("<Keyboard>/w");
            _accelerator.AddBinding("<Gamepad>/rightTrigger");
            _controllerBrake = new InputAction("Analog Service Brake", InputActionType.Value, "<Gamepad>/leftTrigger");
            _keyboardBrake = new InputAction("Keyboard Service Brake", InputActionType.Button, "<Keyboard>/s");
            _steer = new InputAction("Steer", InputActionType.Value);
            _steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            _steer.AddBinding("<Gamepad>/leftStick/x");
            _parkingBrakeToggle = new InputAction("Toggle Parking Brake", InputActionType.Button);
            _parkingBrakeToggle.AddBinding("<Keyboard>/space");
            _parkingBrakeToggle.AddBinding("<Gamepad>/buttonEast");
            _toggleDirection = new InputAction("Toggle Drive Reverse", InputActionType.Button);
            _toggleDirection.AddBinding("<Keyboard>/f");
            _toggleDirection.AddBinding("<Gamepad>/buttonNorth");
            _neutral = new InputAction("Select Neutral", InputActionType.Button, "<Keyboard>/n");
            _park = new InputAction("Select Park", InputActionType.Button, "<Keyboard>/p");
        }

        private void Update()
        {
            float target = _keyboardBrake?.IsPressed() == true ? 1f : 0f;
            float seconds = target > _keyboardBrakeValue ? KeyboardBrakePressSeconds : KeyboardBrakeReleaseSeconds;
            _keyboardBrakeValue = Mathf.MoveTowards(_keyboardBrakeValue, target, Time.deltaTime / seconds);
            if (_suppressPedalsUntilReleased && (_accelerator?.ReadValue<float>() ?? 0f) < .01f &&
                (_controllerBrake?.ReadValue<float>() ?? 0f) < .01f && _keyboardBrake?.IsPressed() != true)
                _suppressPedalsUntilReleased = false;
        }

        public bool TryConsumeGearCommand(out GearCommand command)
        {
            command = _pendingGearCommand;
            _pendingGearCommand = GearCommand.None;
            return command != GearCommand.None;
        }

        public void ResetDrivingState()
        {
            _keyboardBrakeValue = 0f;
            _parkingBrake = false;
            _pendingGearCommand = GearCommand.Park;
            _suppressPedalsUntilReleased = true;
        }

        private void OnEnable()
        {
            foreach (InputAction action in Actions()) action?.Enable();
            _parkingBrakeToggle.performed += ToggleParkingBrake;
            _toggleDirection.performed += ToggleDirection;
            _neutral.performed += SelectNeutral;
            _park.performed += SelectPark;
        }

        private void OnDisable()
        {
            if (_parkingBrakeToggle != null) _parkingBrakeToggle.performed -= ToggleParkingBrake;
            if (_toggleDirection != null) _toggleDirection.performed -= ToggleDirection;
            if (_neutral != null) _neutral.performed -= SelectNeutral;
            if (_park != null) _park.performed -= SelectPark;
            foreach (InputAction action in Actions()) action?.Disable();
        }

        private void OnDestroy()
        {
            foreach (InputAction action in Actions()) action?.Dispose();
        }

        private InputAction[] Actions() => new[] { _accelerator, _controllerBrake, _keyboardBrake, _steer,
            _parkingBrakeToggle, _toggleDirection, _neutral, _park };

        private void ToggleParkingBrake(InputAction.CallbackContext context) => _parkingBrake = !_parkingBrake;
        private void ToggleDirection(InputAction.CallbackContext context) => _pendingGearCommand = GearCommand.ToggleDriveReverse;
        private void SelectNeutral(InputAction.CallbackContext context) => _pendingGearCommand = GearCommand.Neutral;
        private void SelectPark(InputAction.CallbackContext context) => _pendingGearCommand = GearCommand.Park;
    }
}
