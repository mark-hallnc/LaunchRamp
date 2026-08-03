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
        [SerializeField] private BoxCollider trailerBodyCollider;
        [SerializeField] private Collider groundCollider;
        [SerializeField] private bool logWhileThrottleHeld = true;
        [SerializeField, Min(.01f)] private float separationErrorDistance = .08f;
        [SerializeField, Min(1f)] private float excessiveTrailerAngularSpeed = 6f;

        private float _nextCheckTime;

        public void Configure(Rigidbody truck, Rigidbody trailer, ConfigurableJoint joint,
            Transform truckAnchor, Transform trailerAnchor, WheelCollider[] truckWheelColliders,
            WheelCollider[] trailerWheelColliders, BoxCollider trailerBody, Collider ground)
        {
            truckBody = truck;
            trailerBody = trailer;
            hitchJoint = joint;
            truckHitch = truckAnchor;
            trailerHitch = trailerAnchor;
            truckWheels = truckWheelColliders;
            trailerWheels = trailerWheelColliders;
            trailerBodyCollider = trailerBody;
            groundCollider = ground;
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
            Vector3 relativeAngles = NormalizeAngles((Quaternion.Inverse(truckBody.rotation) * trailerBody.rotation).eulerAngles);
            float relativePitch = relativeAngles.x;
            float relativeYaw = relativeAngles.y;
            float relativeRoll = relativeAngles.z;
            float truckSpeed = truckBody.linearVelocity.magnitude;
            float trailerSpeed = trailerBody.linearVelocity.magnitude;
            float trailerAngularSpeed = trailerBody.angularVelocity.magnitude;

            if (separation > separationErrorDistance)
                Debug.LogError($"[Launch Ramp] Hitch separation is {separation:F3} m; expected below {separationErrorDistance:F3} m.", this);
            if (Mathf.Abs(relativePitch) > 25f)
                Debug.LogError($"[Launch Ramp] Excessive relative trailer pitch on flat ground: {relativePitch:F1} degrees.", this);
            if (Mathf.Abs(relativeRoll) > 15f)
                Debug.LogError($"[Launch Ramp] Excessive relative trailer roll: {relativeRoll:F1} degrees.", this);
            if (trailerAngularSpeed > excessiveTrailerAngularSpeed)
                Debug.LogError($"[Launch Ramp] Excessive trailer angular velocity: {trailerAngularSpeed:F2} rad/s.", this);

            VehicleInputReader input = truckBody.GetComponent<VehicleInputReader>();
            if (!logWhileThrottleHeld || input == null || Mathf.Abs(input.Drive) <= .01f) return;

            var message = new StringBuilder(768);
            float bodyMinimumY = trailerBodyCollider != null ? trailerBodyCollider.bounds.min.y : float.NaN;
            float groundSurfaceY = groundCollider != null ? groundCollider.bounds.max.y : float.NaN;
            bool penetrates = false;
            Vector3 penetrationDirection = Vector3.zero;
            float penetrationDistance = 0f;
            if (trailerBodyCollider != null && groundCollider != null)
                penetrates = Physics.ComputePenetration(trailerBodyCollider, trailerBodyCollider.transform.position,
                    trailerBodyCollider.transform.rotation, groundCollider, groundCollider.transform.position,
                    groundCollider.transform.rotation, out penetrationDirection, out penetrationDistance);
            message.AppendLine($"[Launch Ramp] Connected rig: truckSpeed={truckSpeed:F2} m/s, " +
                $"trailerSpeed={trailerSpeed:F2} m/s, hitchDistance={separation:F4} m, " +
                $"pitch={relativePitch:F1} deg, yaw={relativeYaw:F1} deg, roll={relativeRoll:F1} deg")
                .AppendLine($" jointForce={(hitchJoint != null ? hitchJoint.currentForce : Vector3.zero):F2}, " +
                    $"jointTorque={(hitchJoint != null ? hitchJoint.currentTorque : Vector3.zero):F2}")
                .AppendLine($" trailerBodyMinY={bodyMinimumY:F3}, groundSurfaceY={groundSurfaceY:F3}, " +
                    $"touchingGround={bodyMinimumY <= groundSurfaceY + .005f}, penetratesGround={penetrates}, " +
                    $"penetrationDirection={penetrationDirection:F2}, penetrationDistance={penetrationDistance:F4} m");
            foreach (WheelCollider wheel in truckWheels)
                if (wheel != null && Mathf.Abs(wheel.motorTorque) > .01f)
                    message.AppendLine($" truck driven wheel {wheel.name}: rpm={wheel.rpm:F1}, motorTorque={wheel.motorTorque:F1} Nm");
            foreach (WheelCollider wheel in trailerWheels)
            {
                if (wheel == null) continue;
                bool hasHit = wheel.GetGroundHit(out WheelHit hit);
                message.AppendLine($" trailer wheel {wheel.name}: enabled={wheel.enabled}, rpm={wheel.rpm:F1}, " +
                    $"motorTorque={wheel.motorTorque:F1}, brakeTorque={wheel.brakeTorque:F1}, grounded={wheel.isGrounded}, " +
                    $"forwardSlip={(hasHit ? hit.forwardSlip : 0f):F3}, sidewaysSlip={(hasHit ? hit.sidewaysSlip : 0f):F3}, " +
                    $"contactForce={(hasHit ? hit.force : 0f):F1} N, damping={wheel.wheelDampingRate:F2}");
            }
            if (penetrates)
                Debug.LogError($"[Launch Ramp] Trailer body penetrates TestGround by {penetrationDistance:F4} m " +
                    $"along {penetrationDirection:F2}.", this);
            Debug.Log(message.ToString(), this);
        }

        private static Vector3 NormalizeAngles(Vector3 angles) => new(
            Mathf.DeltaAngle(0f, angles.x),
            Mathf.DeltaAngle(0f, angles.y),
            Mathf.DeltaAngle(0f, angles.z));
    }
}
