using System;
using System.Text;
using LaunchRamp.Input;
using UnityEngine;

namespace LaunchRamp.Vehicle
{
    public enum TransmissionState { Park, Reverse, Neutral, Drive }

    /// <summary>Four-wheel gray-box drivetrain with explicit PRND state; +Z is forward.</summary>
    [RequireComponent(typeof(Rigidbody), typeof(VehicleInputReader))]
    public sealed class PrototypeTruckController : MonoBehaviour
    {
        [Serializable]
        public struct WheelBinding
        {
            public WheelCollider Collider;
            public Transform Visual;
            public bool Steers;
            public bool Drives;
        }

        [SerializeField] private WheelBinding[] wheels = Array.Empty<WheelBinding>();
        [SerializeField, Min(0f)] private float motorTorque = 2100f;
        [SerializeField, Min(0f)] private float serviceBrakeTorque = 7000f;
        [SerializeField, Min(0f)] private float parkingBrakeTorque = 9000f;
        [SerializeField, Range(0f, 45f)] private float maximumSteerAngle = 30f;
        [SerializeField, Min(0f)] private float safeDirectionChangeSpeed = .5f;
        [SerializeField] private bool enableDetailedDrivetrainDiagnostics;
        [SerializeField, Min(1f)] private float maximumSpeedMetersPerSecond = 8.94f;
        [SerializeField, Min(.1f)] private float throttleResponsePerSecond = 1.5f;
        [SerializeField, Min(.1f)] private float steeringResponsePerSecond = 3f;
        [SerializeField, Min(.1f)] private float steeringReturnPerSecond = 4f;

        private Rigidbody _body;
        private VehicleInputReader _input;
        private float _nextDiagnosticLogTime;
        private float _smoothedAccelerator;
        private float _smoothedSteering;

        public float ForwardSpeedMetersPerSecond { get; private set; }
        public float ForwardSpeedMilesPerHour => ForwardSpeedMetersPerSecond * 2.2369363f;
        public float AcceleratorInput => _smoothedAccelerator;
        public float ServiceBrakeInput => _input != null ? _input.ServiceBrake : 0f;
        public float SteeringInput => _smoothedSteering;
        public bool ParkingBrakeApplied => _input != null && _input.ParkingBrake;
        public TransmissionState Transmission { get; private set; } = TransmissionState.Park;
        public string GearLabel => Transmission switch
        {
            TransmissionState.Park => "P", TransmissionState.Reverse => "R",
            TransmissionState.Neutral => "N", _ => "D"
        };

        public void Configure(WheelBinding[] value, float torque, float brake, float handbrake,
            float steer, float directionChangeSpeed)
        {
            wheels = value; motorTorque = torque; serviceBrakeTorque = brake;
            parkingBrakeTorque = handbrake; maximumSteerAngle = steer;
            safeDirectionChangeSpeed = directionChangeSpeed;
        }

        public void ResetToPark()
        {
            Transmission = TransmissionState.Park;
            _smoothedAccelerator = 0f;
            _input?.ResetDrivingState();
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _input = GetComponent<VehicleInputReader>();
            Transmission = TransmissionState.Park;
        }

        private void FixedUpdate()
        {
            ProcessGearCommands();
            float acceleratorTarget = _input != null ? _input.Accelerator : 0f;
            _smoothedAccelerator = Mathf.MoveTowards(_smoothedAccelerator, acceleratorTarget,
                throttleResponsePerSecond * Time.fixedDeltaTime);
            float steeringTarget = _input != null ? _input.Steering : 0f;
            float steeringRate = Mathf.Abs(steeringTarget) > .01f ? steeringResponsePerSecond : steeringReturnPerSecond;
            _smoothedSteering = Mathf.MoveTowards(_smoothedSteering, steeringTarget, steeringRate * Time.fixedDeltaTime);

            float speed = Vector3.Dot(_body.linearVelocity, transform.forward);
            ForwardSpeedMetersPerSecond = speed;
            float serviceBrake = _input != null ? _input.ServiceBrake : 0f;
            bool parkingBrake = _input != null && _input.ParkingBrake;
            bool pedalsSuppressed = serviceBrake > .01f || parkingBrake || Transmission is TransmissionState.Park or TransmissionState.Neutral;
            float direction = Transmission == TransmissionState.Drive ? 1f :
                Transmission == TransmissionState.Reverse ? -1f : 0f;
            bool acceleratingPastLimit = Mathf.Abs(speed) >= maximumSpeedMetersPerSecond && Mathf.Sign(direction) == Mathf.Sign(speed);
            float appliedMotorTorque = !pedalsSuppressed && !acceleratingPastLimit
                ? _smoothedAccelerator * motorTorque * direction : 0f;

            foreach (WheelBinding wheel in wheels)
            {
                if (wheel.Collider == null) continue;
                wheel.Collider.steerAngle = wheel.Steers ? _smoothedSteering * maximumSteerAngle : 0f;
                wheel.Collider.motorTorque = wheel.Drives ? appliedMotorTorque : 0f;
                float brake = serviceBrake * serviceBrakeTorque;
                if (Transmission == TransmissionState.Park) brake = Mathf.Max(brake, parkingBrakeTorque);
                else if (parkingBrake && wheel.Drives) brake = Mathf.Max(brake, parkingBrakeTorque);
                wheel.Collider.brakeTorque = brake;
            }

            LogDiagnostics(appliedMotorTorque, serviceBrake, parkingBrake, speed);
        }

        private void ProcessGearCommands()
        {
            if (_input == null || !_input.TryConsumeGearCommand(out GearCommand command)) return;
            TransmissionState requested = Transmission;
            if (command == GearCommand.Park) requested = TransmissionState.Park;
            else if (command == GearCommand.Neutral) requested = TransmissionState.Neutral;
            else if (command == GearCommand.ToggleDriveReverse)
                requested = Transmission == TransmissionState.Drive ? TransmissionState.Reverse : TransmissionState.Drive;

            bool reversingDirection = (Transmission == TransmissionState.Drive && requested == TransmissionState.Reverse) ||
                                      (Transmission == TransmissionState.Reverse && requested == TransmissionState.Drive);
            float forwardSpeed = Vector3.Dot(_body.linearVelocity, transform.forward);
            bool torqueWouldOpposeMotion = (requested == TransmissionState.Drive && forwardSpeed < -safeDirectionChangeSpeed) ||
                                            (requested == TransmissionState.Reverse && forwardSpeed > safeDirectionChangeSpeed);
            if ((reversingDirection && _body.linearVelocity.magnitude > safeDirectionChangeSpeed) || torqueWouldOpposeMotion)
            {
                Debug.LogWarning($"[Launch Ramp] Ignored {requested} selection at {_body.linearVelocity.magnitude:F2} m/s; " +
                    $"slow below {safeDirectionChangeSpeed:F2} m/s first.", this);
                return;
            }
            if (requested == Transmission) return;
            Transmission = requested;
            _smoothedAccelerator = 0f;
            Debug.Log($"[Launch Ramp] Transmission selected {GearLabel}.", this);
        }

        private void LogDiagnostics(float motor, float brakeInput, bool parkingBrake, float speed)
        {
            if (!enableDetailedDrivetrainDiagnostics ||
                (_smoothedAccelerator <= .01f && brakeInput <= .01f) || Time.unscaledTime < _nextDiagnosticLogTime) return;
            var message = new StringBuilder(512);
            message.Append($"[Launch Ramp] Drivetrain: gear={GearLabel}, accelerator={_smoothedAccelerator:F2}, ")
                .Append($"serviceBrake={brakeInput:F2}, parkingBrake={parkingBrake}, motor={motor:F1} Nm, ")
                .AppendLine($"speed={speed:F2} m/s ({ForwardSpeedMilesPerHour:F1} mph)");
            for (int i = 0; i < wheels.Length; i++)
            {
                WheelBinding wheel = wheels[i];
                if (wheel.Collider == null) continue;
                bool hitGround = wheel.Collider.GetGroundHit(out WheelHit hit);
                message.AppendLine($" wheel[{i}] motor={wheel.Collider.motorTorque:F1}, brake={wheel.Collider.brakeTorque:F1}, " +
                    $"rpm={wheel.Collider.rpm:F1}, grounded={wheel.Collider.isGrounded}, " +
                    $"forwardSlip={(hitGround ? hit.forwardSlip : 0f):F3}, sidewaysSlip={(hitGround ? hit.sidewaysSlip : 0f):F3}");
            }
            Debug.Log(message.ToString(), this);
            _nextDiagnosticLogTime = Time.unscaledTime + 1f;
        }

        private void LateUpdate()
        {
            foreach (WheelBinding wheel in wheels)
            {
                if (wheel.Collider == null || wheel.Visual == null) continue;
                wheel.Collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
                wheel.Visual.SetPositionAndRotation(position, rotation * Quaternion.Euler(0f, 0f, 90f));
            }
        }
    }
}
