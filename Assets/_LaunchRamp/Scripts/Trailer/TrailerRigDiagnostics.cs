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
        [SerializeField] private BoxCollider trailerBodyCollider;
        [SerializeField] private Collider groundCollider;
        [SerializeField] private bool logWhileThrottleHeld;
        [SerializeField, Min(.01f)] private float separationErrorDistance = .08f;
        [SerializeField, Min(1f)] private float excessiveTrailerAngularSpeed = 6f;
        [SerializeField, Range(1f, 89f)] private float jackknifeWarningAngle = 60f;
        [SerializeField, Range(1f, 89f)] private float jackknifeCriticalAngle = 68f;

        private float _nextCheckTime;
        private bool _jackknifeWarningIssued;
        private bool _jackknifeCriticalIssued;

        public void Configure(Rigidbody configuredTruckBody, Rigidbody configuredTrailerBody,
            BoxCollider configuredTrailerBodyCollider, Collider configuredGroundCollider,
            ConfigurableJoint configuredHitchJoint, Transform truckAnchor, Transform trailerAnchor)
        {
            truckBody = configuredTruckBody;
            trailerBody = configuredTrailerBody;
            trailerBodyCollider = configuredTrailerBodyCollider;
            groundCollider = configuredGroundCollider;
            hitchJoint = configuredHitchJoint;
            truckHitch = truckAnchor;
            trailerHitch = trailerAnchor;
        }

        private void Start()
        {
            if (trailerBody == null)
                Debug.LogError("[Launch Ramp] Trailer rig diagnostics is missing the trailer Rigidbody.", this);
            if (trailerBodyCollider == null)
                Debug.LogError("[Launch Ramp] Trailer rig diagnostics is missing the trailer body BoxCollider.", this);
            if (groundCollider == null)
                Debug.LogError("[Launch Ramp] Trailer rig diagnostics is missing the TestGround Collider.", this);
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

            float absoluteYaw = Mathf.Abs(relativeYaw);
            if (absoluteYaw >= jackknifeCriticalAngle)
            {
                if (!_jackknifeCriticalIssued)
                    Debug.LogError($"[Launch Ramp] CRITICAL jackknife angle: trailer yaw is {relativeYaw:F1} degrees " +
                        $"(physical limit {(hitchJoint != null ? hitchJoint.angularYLimit.limit : 0f):F1} degrees).", this);
                _jackknifeCriticalIssued = true;
                _jackknifeWarningIssued = true;
            }
            else if (absoluteYaw >= jackknifeWarningAngle)
            {
                if (!_jackknifeWarningIssued)
                    Debug.LogWarning($"[Launch Ramp] Jackknife warning: trailer yaw is {relativeYaw:F1} degrees.", this);
                _jackknifeWarningIssued = true;
                _jackknifeCriticalIssued = false;
            }
            else if (absoluteYaw < jackknifeWarningAngle - 5f)
            {
                // Hysteresis permits a new warning after the rig returns to a clearly safe articulation.
                _jackknifeWarningIssued = false;
                _jackknifeCriticalIssued = false;
            }

            VehicleInputReader input = truckBody.GetComponent<VehicleInputReader>();
            if (!logWhileThrottleHeld || input == null || input.Accelerator <= .01f) return;

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
