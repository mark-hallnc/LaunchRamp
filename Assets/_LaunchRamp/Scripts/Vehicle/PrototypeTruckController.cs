using System;
using LaunchRamp.Input;
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

        private Rigidbody _body;
        private VehicleInputReader _input;
        private float _nextDiagnosticLogTime;

        public float ForwardSpeedMetersPerSecond { get; private set; }
        public float ForwardSpeedMilesPerHour => ForwardSpeedMetersPerSecond * 2.2369363f;

        public void Configure(WheelBinding[] value, float torque, float brake, float handbrake, float steer, float reverseSpeed)
        {
            wheels = value; motorTorque = torque; serviceBrakeTorque = brake;
            parkingBrakeTorque = handbrake; maximumSteerAngle = steer; reverseEngagementSpeed = reverseSpeed;
        }

        private void Awake() { _body = GetComponent<Rigidbody>(); _input = GetComponent<VehicleInputReader>(); }

        private void FixedUpdate()
        {
            float drive = _input.Drive;
            float speed = Vector3.Dot(_body.linearVelocity, transform.forward);
            ForwardSpeedMetersPerSecond = speed;
            bool changingDirection = Mathf.Abs(speed) > reverseEngagementSpeed && Mathf.Sign(drive) != Mathf.Sign(speed);
            foreach (WheelBinding wheel in wheels)
            {
                if (wheel.Collider == null) continue;
                wheel.Collider.steerAngle = wheel.Steers ? _input.Steering * maximumSteerAngle : 0f;
                wheel.Collider.motorTorque = wheel.Drives && !changingDirection ? drive * motorTorque : 0f;
                wheel.Collider.brakeTorque = _input.ParkingBrake ? parkingBrakeTorque :
                    changingDirection ? serviceBrakeTorque * Mathf.Abs(drive) : 0f;
            }

            if (Mathf.Abs(speed) >= .25f && Time.unscaledTime >= _nextDiagnosticLogTime)
            {
                float brake = changingDirection ? Mathf.Abs(drive) : 0f;
                Debug.Log($"[Launch Ramp] Speed {speed:F2} m/s ({ForwardSpeedMilesPerHour:F1} mph), " +
                          $"throttle={drive:F2}, brake={brake:F2}, steering={_input.Steering:F2}, " +
                          $"parkingBrake={_input.ParkingBrake}", this);
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
