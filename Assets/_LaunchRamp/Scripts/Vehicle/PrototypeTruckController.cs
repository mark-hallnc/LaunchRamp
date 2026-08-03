using System;
using LaunchRamp.Input;
using System.Text;
using UnityEngine;

namespace LaunchRamp.Vehicle
{
    /// <summary>Simple four-wheel gray-box drivetrain; +Z is forward and Y is up.</summary>
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
        [SerializeField, Min(0f)] private float serviceBrakeTorque = 3600f;
        [SerializeField, Min(0f)] private float parkingBrakeTorque = 6500f;
        [SerializeField, Range(0f, 45f)] private float maximumSteerAngle = 30f;
        [SerializeField, Min(0f)] private float reverseEngagementSpeed = 1.5f;
        [SerializeField] private bool enableDetailedDrivetrainDiagnostics;
        [SerializeField, Min(1f)] private float maximumSpeedMetersPerSecond = 8.94f;
        [SerializeField, Min(.1f)] private float throttleResponsePerSecond = 1.5f;
        [SerializeField, Min(.1f)] private float steeringResponsePerSecond = 3f;
        [SerializeField, Min(.1f)] private float steeringReturnPerSecond = 4f;

        private Rigidbody _body;
        private VehicleInputReader _input;
        private float _nextDiagnosticLogTime;
        private float _smoothedDrive;
        private float _smoothedSteering;

        public float ForwardSpeedMetersPerSecond { get; private set; }
        public float ForwardSpeedMilesPerHour => ForwardSpeedMetersPerSecond * 2.2369363f;
        public float DriveInput => _smoothedDrive;
        public float SteeringInput => _smoothedSteering;
        public bool ParkingBrakeApplied => _input != null && _input.ParkingBrake;

        public void Configure(WheelBinding[] value, float torque, float brake, float handbrake, float steer, float reverseSpeed)
        {
            wheels = value; motorTorque = torque; serviceBrakeTorque = brake;
            parkingBrakeTorque = handbrake; maximumSteerAngle = steer; reverseEngagementSpeed = reverseSpeed;
        }

        private void Awake() { _body = GetComponent<Rigidbody>(); _input = GetComponent<VehicleInputReader>(); }

        private void FixedUpdate()
        {
            float drive = Mathf.MoveTowards(_smoothedDrive, _input.Drive,
                throttleResponsePerSecond * Time.fixedDeltaTime);
            _smoothedDrive = drive;
            float steeringRate = Mathf.Abs(_input.Steering) > .01f
                ? steeringResponsePerSecond
                : steeringReturnPerSecond;
            _smoothedSteering = Mathf.MoveTowards(_smoothedSteering, _input.Steering,
                steeringRate * Time.fixedDeltaTime);
            float speed = Vector3.Dot(_body.linearVelocity, transform.forward);
            ForwardSpeedMetersPerSecond = speed;
            bool changingDirection = Mathf.Abs(speed) > reverseEngagementSpeed && Mathf.Sign(drive) != Mathf.Sign(speed);
            foreach (WheelBinding wheel in wheels)
            {
                if (wheel.Collider == null) continue;
                wheel.Collider.steerAngle = wheel.Steers ? _smoothedSteering * maximumSteerAngle : 0f;
                bool acceleratingPastLimit = Mathf.Abs(speed) >= maximumSpeedMetersPerSecond &&
                                             Mathf.Sign(drive) == Mathf.Sign(speed);
                wheel.Collider.motorTorque = wheel.Drives && !changingDirection && !acceleratingPastLimit
                    ? drive * motorTorque : 0f;
                wheel.Collider.brakeTorque = _input.ParkingBrake ? parkingBrakeTorque :
                    changingDirection ? serviceBrakeTorque * Mathf.Abs(drive) : 0f;
            }

            if (enableDetailedDrivetrainDiagnostics && Mathf.Abs(drive) > .01f &&
                Time.unscaledTime >= _nextDiagnosticLogTime)
            {
                float brake = changingDirection ? Mathf.Abs(drive) : 0f;
                var message = new StringBuilder(512);
                message.Append($"[Launch Ramp] Drivetrain: throttle={drive:F2}, brake={brake:F2}, ")
                    .Append($"reverse={drive < 0f && !changingDirection}, parkingBrake={_input.ParkingBrake}, ")
                    .Append($"speed={speed:F2} m/s ({ForwardSpeedMilesPerHour:F1} mph), ")
                    .AppendLine($"linearVelocity={_body.linearVelocity:F3}, angularVelocity={_body.angularVelocity:F3}")
                    .AppendLine($" constraints={_body.constraints}, isSleeping={_body.IsSleeping()}, " +
                                $"truckForward={transform.forward:F3}");
                for (int i = 0; i < wheels.Length; i++)
                {
                    WheelBinding wheel = wheels[i];
                    if (wheel.Collider == null) { message.AppendLine($" wheel[{i}]=UNASSIGNED"); continue; }
                    bool hasHit = wheel.Collider.GetGroundHit(out WheelHit hit);
                    message.AppendLine($" wheel[{i}] {wheel.Collider.name}: driven={wheel.Drives}, " +
                                       $"motorTorque={wheel.Collider.motorTorque:F1} Nm, " +
                                       $"brakeTorque={wheel.Collider.brakeTorque:F1} Nm, " +
                                       $"rpm={wheel.Collider.rpm:F1}, isGrounded={wheel.Collider.isGrounded}, " +
                                       $"forwardSlip={(hasHit ? hit.forwardSlip : 0f):F3}, " +
                                       $"sidewaysSlip={(hasHit ? hit.sidewaysSlip : 0f):F3}, " +
                                       $"contactForce={(hasHit ? hit.force : 0f):F1} N");
                }
                Debug.Log(message.ToString(), this);
                _nextDiagnosticLogTime = Time.unscaledTime + 1f;
            }
        }

        private void LateUpdate()
        {
            foreach (WheelBinding wheel in wheels)
            {
                if (wheel.Collider == null || wheel.Visual == null) continue;
                wheel.Collider.GetWorldPose(out Vector3 p, out Quaternion r);
                wheel.Visual.SetPositionAndRotation(p, r * Quaternion.Euler(0f, 0f, 90f));
            }
        }
    }
}
