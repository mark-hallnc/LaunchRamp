using UnityEngine;
using LaunchRamp.Input;
using System.Text;

namespace LaunchRamp.Trailer
{
    /// <summary>Low-noise integrity checks for the physical truck/trailer connection.</summary>
    public sealed class TrailerRigDiagnostics : MonoBehaviour
    {
        [SerializeField] private Rigidbody truckBody;
        [SerializeField] private Rigidbody trailerBody;
        [SerializeField] private ConfigurableJoint hitchJoint;
        [SerializeField] private Transform truckHitch;
        [SerializeField] private Transform trailerHitch;
        [SerializeField] private WheelCollider[] truckWheels;
        [SerializeField] private WheelCollider[] trailerWheels;
        [SerializeField] private bool logWhileThrottleHeld = true;
        [SerializeField, Min(.01f)] private float separationErrorDistance = .08f;
        [SerializeField, Min(1f)] private float excessiveAngularSpeed = 8f;

        private float _nextCheckTime;

        public void Configure(Rigidbody truck, Rigidbody trailer, ConfigurableJoint joint,
            Transform truckAnchor, Transform trailerAnchor, WheelCollider[] truckWheelColliders,
            WheelCollider[] trailerWheelColliders)
        {
            truckBody = truck;
            trailerBody = trailer;
            hitchJoint = joint;
            truckHitch = truckAnchor;
            trailerHitch = trailerAnchor;
            truckWheels = truckWheelColliders;
            trailerWheels = trailerWheelColliders;
        }

        private void Start()
        {
            if (hitchJoint == null || hitchJoint.connectedBody != truckBody)
                Debug.LogError("[Launch Ramp] Invalid trailer hitch: joint or connected truck Rigidbody is missing.", this);
        }

        private void FixedUpdate()
        {
            if (Time.unscaledTime < _nextCheckTime || truckBody == null || trailerBody == null ||
                truckHitch == null || trailerHitch == null) return;
            _nextCheckTime = Time.unscaledTime + 1f;

            float separation = Vector3.Distance(truckHitch.position, trailerHitch.position);
            float articulation = Quaternion.Angle(truckBody.rotation, trailerBody.rotation);
            float truckSpeed = truckBody.linearVelocity.magnitude;
            float trailerSpeed = trailerBody.linearVelocity.magnitude;
            float maximumAngularSpeed = Mathf.Max(truckBody.angularVelocity.magnitude, trailerBody.angularVelocity.magnitude);

            if (separation > separationErrorDistance)
                Debug.LogError($"[Launch Ramp] Hitch separation is {separation:F3} m; expected below {separationErrorDistance:F3} m.", this);
            if (maximumAngularSpeed > excessiveAngularSpeed)
                Debug.LogError($"[Launch Ramp] Excessive rig angular velocity detected: {maximumAngularSpeed:F2} rad/s.", this);

            VehicleInputReader input = truckBody.GetComponent<VehicleInputReader>();
            if (!logWhileThrottleHeld || input == null || Mathf.Abs(input.Drive) <= .01f) return;

            var message = new StringBuilder(768);
            message.AppendLine($"[Launch Ramp] Connected rig: truckSpeed={truckSpeed:F2} m/s, " +
                $"trailerSpeed={trailerSpeed:F2} m/s, hitchDistance={separation:F4} m, articulation={articulation:F1} deg")
                .AppendLine($" jointForce={(hitchJoint != null ? hitchJoint.currentForce : Vector3.zero):F2}, " +
                    $"jointTorque={(hitchJoint != null ? hitchJoint.currentTorque : Vector3.zero):F2}");
            foreach (WheelCollider wheel in truckWheels)
                if (wheel != null && Mathf.Abs(wheel.motorTorque) > .01f)
                    message.AppendLine($" truck driven wheel {wheel.name}: rpm={wheel.rpm:F1}, motorTorque={wheel.motorTorque:F1} Nm");
            foreach (WheelCollider wheel in trailerWheels)
            {
                if (wheel == null) continue;
                bool hasHit = wheel.GetGroundHit(out WheelHit hit);
                message.AppendLine($" trailer wheel {wheel.name}: enabled={wheel.enabled}, rpm={wheel.rpm:F1}, " +
                    $"motorTorque={wheel.motorTorque:F1}, brakeTorque={wheel.brakeTorque:F1}, grounded={wheel.isGrounded}, " +
                    $"forwardSlip={(hasHit ? hit.forwardSlip : 0f):F3}, sidewaysSlip={(hasHit ? hit.sidewaysSlip : 0f):F3}");
            }
            Debug.Log(message.ToString(), this);
        }
    }
}
