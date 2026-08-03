using UnityEngine;

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
        [SerializeField] private bool enableDetailedRigDiagnostics;
        [SerializeField, Min(.01f)] private float separationErrorDistance = .08f;
        [SerializeField, Min(1f)] private float excessiveAngularSpeed = 8f;

        private float _nextCheckTime;

        public void Configure(Rigidbody truck, Rigidbody trailer, ConfigurableJoint joint,
            Transform truckAnchor, Transform trailerAnchor)
        {
            truckBody = truck;
            trailerBody = trailer;
            hitchJoint = joint;
            truckHitch = truckAnchor;
            trailerHitch = trailerAnchor;
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

            if (enableDetailedRigDiagnostics)
                Debug.Log($"[Launch Ramp] Rig: hitchDistance={separation:F4} m, articulation={articulation:F1} deg, " +
                          $"truckSpeed={truckSpeed:F2} m/s, trailerSpeed={trailerSpeed:F2} m/s, " +
                          $"jointExists={hitchJoint != null}, connectedBodyCorrect={hitchJoint != null && hitchJoint.connectedBody == truckBody}", this);
        }
    }
}
