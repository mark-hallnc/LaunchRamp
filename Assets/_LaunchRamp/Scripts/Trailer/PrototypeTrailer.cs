using System;
using UnityEngine;

namespace LaunchRamp.Trailer
{
    /// <summary>Keeps the free-rolling trailer wheel meshes aligned with physics.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PrototypeTrailer : MonoBehaviour
    {
        [Serializable] public struct WheelBinding { public WheelCollider Collider; public Transform Visual; }
        [SerializeField] private WheelBinding[] wheels = Array.Empty<WheelBinding>();
        public void Configure(WheelBinding[] value) => wheels = value;

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
