using UnityEngine;

namespace LaunchRamp.Trailer
{
    /// <summary>Disconnected diagnostic only; applies a documented one-second force pulse.</summary>
    public sealed class TrailerRollingResistanceTest : MonoBehaviour
    {
        [SerializeField] private Rigidbody trailerBody;
        [SerializeField] private float testForceNewtons = 1000f;
        [SerializeField] private float forceStartTime = 1f;
        [SerializeField] private float forceDuration = 1f;
        private float _elapsed;
        private bool _reported;

        public void Configure(Rigidbody body) => trailerBody = body;

        private void FixedUpdate()
        {
            if (trailerBody == null) return;
            _elapsed += Time.fixedDeltaTime;
            if (_elapsed >= forceStartTime && _elapsed < forceStartTime + forceDuration)
                trailerBody.AddForce(Vector3.forward * testForceNewtons, ForceMode.Force);
            else if (!_reported && _elapsed >= forceStartTime + forceDuration)
            {
                _reported = true;
                Debug.Log($"[Launch Ramp] Trailer rolling-resistance test complete: force={testForceNewtons:F0} N " +
                    $"for {forceDuration:F1} s, finalSpeed={trailerBody.linearVelocity.magnitude:F3} m/s.", this);
            }
        }
    }
}
