using System;
using UnityEngine;

namespace LaunchRamp.Trailer
{
    /// <summary>Passive, unpowered raycast suspension shared by all trailer wheels.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PassiveTrailerAxle : MonoBehaviour
    {
        [Serializable]
        public struct WheelBinding
        {
            public string Label;
            public Transform Point;
            public Transform Visual;
        }

        private struct WheelState
        {
            public bool Grounded;
            public float Compression;
            public float SpringForce;
            public float LateralVelocity;
            public float LongitudinalVelocity;
            public Vector3 LastVisualPosition;
            public float VisualAngle;
        }

        [SerializeField] private Rigidbody trailerBody;
        [SerializeField] private WheelBinding[] wheels = Array.Empty<WheelBinding>();
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float wheelRadius = .38f;
        [SerializeField] private float suspensionTravel = .30f;
        [SerializeField] private float springStrength = 20000f;
        [SerializeField] private float damperStrength = 3000f;
        [SerializeField] private float lateralGrip = 3000f;
        [SerializeField] private float rollingResistance = 50f;
        [SerializeField] private float wheelVisualWidth = .30f;
        [SerializeField] private float maximumSuspensionForce = 15000f;
        [SerializeField] private float maximumTireForce = 6000f;
        [SerializeField] private bool diagnosticsEnabled = false;

        private WheelState[] states = Array.Empty<WheelState>();
        private float nextDiagnosticTime;

        public bool LeftGrounded => Grounded(0) && Grounded(2);
        public bool RightGrounded => Grounded(1) && Grounded(3);
        public bool FrontLeftGrounded => Grounded(0);
        public bool FrontRightGrounded => Grounded(1);
        public bool RearLeftGrounded => Grounded(2);
        public bool RearRightGrounded => Grounded(3);

        public void Configure(Rigidbody body, WheelBinding[] wheelBindings, LayerMask mask,
            float radius, float travel, float spring, float damper, float grip,
            float resistance, float visualWidth)
        {
            trailerBody = body;
            wheels = wheelBindings ?? Array.Empty<WheelBinding>();
            groundMask = mask;
            wheelRadius = radius;
            suspensionTravel = travel;
            springStrength = spring;
            damperStrength = damper;
            lateralGrip = grip;
            rollingResistance = resistance;
            wheelVisualWidth = visualWidth;
            EnsureStates();
        }

        private void Awake()
        {
            if (trailerBody == null)
                trailerBody = GetComponent<Rigidbody>();
            EnsureStates();
            if (trailerBody == null || wheels.Length != 4)
                Debug.LogError("[Launch Ramp] PassiveTrailerAxle requires a Rigidbody and four wheel bindings.", this);
            for (int i = 0; i < wheels.Length; i++)
                if (wheels[i].Point == null || wheels[i].Visual == null)
                    Debug.LogError($"[Launch Ramp] Passive trailer wheel binding {i} is incomplete.", this);
        }

        private void FixedUpdate()
        {
            if (trailerBody == null || wheels == null)
                return;

            EnsureStates();
            for (int i = 0; i < wheels.Length; i++)
                SimulateWheel(i);

            if (diagnosticsEnabled && Time.unscaledTime >= nextDiagnosticTime)
            {
                nextDiagnosticTime = Time.unscaledTime + 1f;
                Debug.Log(FormatDiagnostics(), this);
            }
        }

        private void SimulateWheel(int index)
        {
            WheelBinding wheel = wheels[index];
            if (wheel.Point == null)
                return;

            Transform point = wheel.Point;
            Vector3 down = -transform.up;
            float rayLength = wheelRadius + suspensionTravel;
            bool hitGround = Physics.Raycast(point.position, down, out RaycastHit hit,
                rayLength, groundMask, QueryTriggerInteraction.Ignore);

            WheelState state = states[index];
            state.Grounded = hitGround;
            state.Compression = 0f;
            state.SpringForce = 0f;
            state.LateralVelocity = 0f;
            state.LongitudinalVelocity = 0f;

            Vector3 visualPosition = point.position + down * suspensionTravel;
            if (hitGround)
            {
                float suspensionLength = Mathf.Max(0f, hit.distance - wheelRadius);
                state.Compression = Mathf.Clamp(suspensionTravel - suspensionLength, 0f, suspensionTravel);
                Vector3 pointVelocity = trailerBody.GetPointVelocity(hit.point);
                float axisVelocity = Vector3.Dot(pointVelocity, transform.up);
                float suspensionForce = state.Compression * springStrength - axisVelocity * damperStrength;
                state.SpringForce = Mathf.Clamp(suspensionForce, 0f, maximumSuspensionForce);
                trailerBody.AddForceAtPosition(transform.up * state.SpringForce, point.position, ForceMode.Force);

                state.LateralVelocity = Vector3.Dot(pointVelocity, transform.right);
                state.LongitudinalVelocity = Vector3.Dot(pointVelocity, transform.forward);
                float lateralForce = Mathf.Clamp(-state.LateralVelocity * lateralGrip,
                    -maximumTireForce, maximumTireForce);
                float longitudinalForce = Mathf.Clamp(-state.LongitudinalVelocity * rollingResistance,
                    -maximumTireForce * .1f, maximumTireForce * .1f);
                trailerBody.AddForceAtPosition(
                    transform.right * lateralForce + transform.forward * longitudinalForce,
                    hit.point, ForceMode.Force);
                visualPosition = hit.point + transform.up * wheelRadius;
            }

            UpdateVisual(wheel.Visual, ref state, visualPosition);
            states[index] = state;

            if (diagnosticsEnabled)
                Debug.DrawRay(point.position, down * rayLength, hitGround ? Color.green : Color.red);
        }

        private void UpdateVisual(Transform visual, ref WheelState state, Vector3 position)
        {
            if (visual == null)
                return;

            Vector3 travel = position - state.LastVisualPosition;
            if (state.LastVisualPosition != Vector3.zero)
            {
                float longitudinalTravel = Vector3.Dot(travel, transform.forward);
                state.VisualAngle += longitudinalTravel / (2f * Mathf.PI * wheelRadius) * 360f;
            }

            state.LastVisualPosition = position;
            visual.SetPositionAndRotation(position,
                transform.rotation * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(0f, state.VisualAngle, 0f));
            visual.localScale = new Vector3(wheelRadius * 2f, wheelVisualWidth * .5f, wheelRadius * 2f);
        }

        private bool Grounded(int index) => index >= 0 && index < states.Length && states[index].Grounded;

        private void EnsureStates()
        {
            int count = wheels == null ? 0 : wheels.Length;
            if (states.Length != count)
                states = new WheelState[count];
        }

        private string FormatDiagnostics()
        {
            string message = $"Passive trailer suspension | speed={trailerBody.linearVelocity.magnitude:F2} m/s";
            for (int i = 0; i < wheels.Length; i++)
            {
                string label = string.IsNullOrEmpty(wheels[i].Label) ? i.ToString() : wheels[i].Label;
                WheelState state = states[i];
                message += $" | {label}: grounded={state.Grounded}, compression={state.Compression:F3} m, " +
                    $"spring={state.SpringForce:F0} N, lateral={state.LateralVelocity:F2} m/s, " +
                    $"longitudinal={state.LongitudinalVelocity:F2} m/s";
            }
            return message;
        }
    }
}
