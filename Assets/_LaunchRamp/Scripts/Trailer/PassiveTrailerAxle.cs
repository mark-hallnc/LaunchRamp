using System.Text;
using UnityEngine;

namespace LaunchRamp.Trailer
{
    /// <summary>Passive two-point suspension: vertical support, lateral grip, and minimal rolling drag.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PassiveTrailerAxle : MonoBehaviour
    {
        [SerializeField] private Rigidbody trailerBody;
        [SerializeField] private Transform leftWheelPoint, rightWheelPoint;
        [SerializeField] private Transform leftWheelVisual, rightWheelVisual;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float wheelRadius = .52f, suspensionTravel = .30f;
        [SerializeField] private float springStrength = 40000f, damperStrength = 6000f;
        [SerializeField] private float lateralGrip = 6000f, rollingResistance = 100f;
        [SerializeField] private float wheelVisualWidth = .34f;
        [SerializeField] private float maximumSuspensionForce = 25000f, maximumTireForce = 12000f;
        [SerializeField] private bool enableDiagnostics;

        private WheelState _leftState, _rightState;
        private float _leftRotation, _rightRotation, _nextLogTime;

        private struct WheelState
        {
            public bool Grounded;
            public float Compression, SpringForce, LateralVelocity, LongitudinalVelocity;
        }

        public void Configure(Rigidbody body, Transform leftPoint, Transform rightPoint,
            Transform leftVisual, Transform rightVisual, LayerMask mask, float radius, float travel,
            float spring, float damper, float lateral, float rolling, float visualWidth)
        {
            trailerBody = body; leftWheelPoint = leftPoint; rightWheelPoint = rightPoint;
            leftWheelVisual = leftVisual; rightWheelVisual = rightVisual; groundMask = mask;
            wheelRadius = radius; suspensionTravel = travel; springStrength = spring;
            damperStrength = damper; lateralGrip = lateral; rollingResistance = rolling;
            wheelVisualWidth = visualWidth;
        }

        private void Awake()
        {
            if (trailerBody == null) trailerBody = GetComponent<Rigidbody>();
            if (trailerBody == null || leftWheelPoint == null || rightWheelPoint == null ||
                leftWheelVisual == null || rightWheelVisual == null)
                Debug.LogError("[Launch Ramp] PassiveTrailerAxle is missing required references.", this);
        }

        private void FixedUpdate()
        {
            if (trailerBody == null) return;
            _leftState = SimulateWheel(leftWheelPoint, leftWheelVisual, ref _leftRotation);
            _rightState = SimulateWheel(rightWheelPoint, rightWheelVisual, ref _rightRotation);
            if (!enableDiagnostics || Time.unscaledTime < _nextLogTime) return;
            _nextLogTime = Time.unscaledTime + 1f;
            Rigidbody truck = GetComponent<ConfigurableJoint>()?.connectedBody;
            var text = new StringBuilder(384);
            text.AppendLine($"[Launch Ramp] Passive axle: trailerSpeed={trailerBody.linearVelocity.magnitude:F2} m/s, " +
                $"truckSpeed={(truck != null ? truck.linearVelocity.magnitude : 0f):F2} m/s")
                .AppendLine(Format("left", _leftState)).AppendLine(Format("right", _rightState));
            Debug.Log(text.ToString(), this);
        }

        private WheelState SimulateWheel(Transform point, Transform visual, ref float rotation)
        {
            if (point == null || visual == null) return default;
            Vector3 up = transform.up;
            float rayLength = wheelRadius + suspensionTravel;
            if (enableDiagnostics) Debug.DrawRay(point.position, -up * rayLength, Color.cyan, Time.fixedDeltaTime);
            if (!Physics.Raycast(point.position, -up, out RaycastHit hit, rayLength, groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                visual.position = point.position - up * suspensionTravel;
                SetVisualRotation(visual, rotation);
                return default;
            }

            Vector3 velocity = trailerBody.GetPointVelocity(hit.point);
            float compression = Mathf.Clamp(rayLength - hit.distance, 0f, suspensionTravel);
            float force = Mathf.Clamp(compression * springStrength - Vector3.Dot(velocity, up) * damperStrength,
                0f, maximumSuspensionForce);
            trailerBody.AddForceAtPosition(up * force, point.position, ForceMode.Force);

            float lateralVelocity = Vector3.Dot(velocity, transform.right);
            float longitudinalVelocity = Vector3.Dot(velocity, transform.forward);
            float lateralForce = Mathf.Clamp(-lateralVelocity * lateralGrip, -maximumTireForce, maximumTireForce);
            float rollingForce = Mathf.Clamp(-longitudinalVelocity * rollingResistance,
                -maximumTireForce * .1f, maximumTireForce * .1f);
            trailerBody.AddForceAtPosition(transform.right * lateralForce + transform.forward * rollingForce,
                hit.point, ForceMode.Force);

            visual.position = hit.point + up * wheelRadius;
            rotation += longitudinalVelocity / Mathf.Max(wheelRadius, .01f) * Mathf.Rad2Deg * Time.fixedDeltaTime;
            SetVisualRotation(visual, rotation);
            return new WheelState { Grounded = true, Compression = compression, SpringForce = force,
                LateralVelocity = lateralVelocity, LongitudinalVelocity = longitudinalVelocity };
        }

        private void SetVisualRotation(Transform visual, float rotation)
        {
            visual.rotation = transform.rotation * Quaternion.AngleAxis(rotation, Vector3.right) * Quaternion.Euler(0f, 0f, 90f);
            visual.localScale = new Vector3(wheelRadius * 2f, wheelVisualWidth * .5f, wheelRadius * 2f);
        }

        private static string Format(string label, WheelState value) =>
            $" {label}: grounded={value.Grounded}, compression={value.Compression:F3} m, springForce={value.SpringForce:F0} N, " +
            $"lateralVelocity={value.LateralVelocity:F2} m/s, longitudinalVelocity={value.LongitudinalVelocity:F2} m/s";
    }
}
