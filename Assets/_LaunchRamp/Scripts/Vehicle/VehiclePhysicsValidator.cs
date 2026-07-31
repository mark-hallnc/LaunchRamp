using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LaunchRamp.Vehicle
{
    /// <summary>
    /// Reports initial vehicle physics geometry in edit mode and once at runtime.
    /// It does not alter physics state, so validation cannot hide an unstable setup.
    /// </summary>
    public sealed class VehiclePhysicsValidator : MonoBehaviour
    {
        [SerializeField] private bool validateOnStart = true;

        private void Start()
        {
            if (validateOnStart) Validate(gameObject, true);
        }

        public static bool Validate(GameObject prototypeRoot, bool logReport)
        {
            if (prototypeRoot == null)
            {
                Debug.LogError("[Launch Ramp] Physics validation requires a VehiclePrototype root.");
                return false;
            }

            Transform truck = prototypeRoot.transform.Find("Truck");
            Transform trailer = prototypeRoot.transform.Find("Trailer");
            if (truck == null || trailer == null)
            {
                Debug.LogError("[Launch Ramp] Physics validation failed: Truck or Trailer is missing.", prototypeRoot);
                return false;
            }

            Physics.SyncTransforms();
            var report = new StringBuilder(2048);
            var issues = new List<string>();
            report.AppendLine("[Launch Ramp] Vehicle physics validation report");
            report.AppendLine($"Truck spawn: {truck.position:F3}; Trailer spawn: {trailer.position:F3}");
            AppendColliders(truck, report, issues);
            AppendColliders(trailer, report, issues);
            AppendJoint(trailer, report, issues);
            DetectSolidOverlaps(prototypeRoot, truck, trailer, report, issues);

            report.AppendLine(issues.Count == 0
                ? "Result: PASS - no initial physics configuration problems detected."
                : $"Result: FAIL - {issues.Count} problem(s) detected:\n - {string.Join("\n - ", issues)}");

            if (logReport)
            {
                if (issues.Count == 0) Debug.Log(report.ToString(), prototypeRoot);
                else Debug.LogError(report.ToString(), prototypeRoot);
            }
            return issues.Count == 0;
        }

        private static void AppendColliders(Transform bodyRoot, StringBuilder report, List<string> issues)
        {
            report.AppendLine($"Colliders under {Path(bodyRoot)}:");
            foreach (Collider collider in bodyRoot.GetComponentsInChildren<Collider>(true))
            {
                Rigidbody owner = collider.attachedRigidbody;
                report.Append($" - {Path(collider.transform)} | {collider.GetType().Name} | Rigidbody: ")
                    .AppendLine(owner == null ? "none" : Path(owner.transform));

                if (collider is WheelCollider wheel)
                {
                    report.AppendLine($"   radius={wheel.radius:F3}, suspensionDistance={wheel.suspensionDistance:F3}, " +
                                      $"localScale={wheel.transform.localScale:F3}, lossyScale={wheel.transform.lossyScale:F3}");
                    if (!ApproximatelyOne(wheel.transform.localScale) || !ApproximatelyOne(wheel.transform.lossyScale))
                        issues.Add($"WheelCollider '{Path(wheel.transform)}' is scaled; WheelCollider transforms must be 1,1,1.");
                }
                else if (collider.transform != bodyRoot)
                {
                    issues.Add($"Unexpected solid collider {collider.GetType().Name} on decorative child '{Path(collider.transform)}'.");
                }
            }

            foreach (Rigidbody rigidbody in bodyRoot.GetComponentsInChildren<Rigidbody>(true))
                if (rigidbody.transform != bodyRoot)
                    issues.Add($"Unexpected Rigidbody on visual child '{Path(rigidbody.transform)}'.");
        }

        private static void AppendJoint(Transform trailer, StringBuilder report, List<string> issues)
        {
            ConfigurableJoint joint = trailer.GetComponent<ConfigurableJoint>();
            if (joint == null)
            {
                issues.Add("Trailer hitch joint is missing.");
                return;
            }

            Vector3 trailerAnchor = trailer.TransformPoint(joint.anchor);
            Vector3 truckAnchor = joint.connectedBody == null
                ? joint.connectedAnchor
                : joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
            float error = Vector3.Distance(trailerAnchor, truckAnchor);
            report.AppendLine($"Joint anchor(local): {joint.anchor:F3}; connected anchor(local): {joint.connectedAnchor:F3}");
            report.AppendLine($"Joint anchors(world): trailer={trailerAnchor:F3}, truck={truckAnchor:F3}, error={error:F4} m");
            if (joint.connectedBody == null) issues.Add("Trailer joint has no connectedBody.");
            if (joint.enableCollision) issues.Add("Trailer joint permits connected bodies to collide.");
            if (error > .01f) issues.Add($"Hitch anchors begin {error:F3} m apart.");
        }

        private static void DetectSolidOverlaps(GameObject root, Transform truck, Transform trailer,
            StringBuilder report, List<string> issues)
        {
            Collider[] vehicle = root.GetComponentsInChildren<Collider>(true);
            Collider[] scene = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);
            var checkedPairs = new HashSet<(EntityId First, EntityId Second)>();
            foreach (Collider a in vehicle)
            foreach (Collider b in scene)
            {
                if (a == b || a is WheelCollider || b is WheelCollider || IsChildOf(b.transform, root.transform)) continue;
                CheckPenetration(a, b, checkedPairs, report, issues);
            }

            Collider[] truckSolids = truck.GetComponentsInChildren<Collider>(true);
            Collider[] trailerSolids = trailer.GetComponentsInChildren<Collider>(true);
            foreach (Collider a in truckSolids)
            foreach (Collider b in trailerSolids)
                if (a is not WheelCollider && b is not WheelCollider)
                    CheckPenetration(a, b, checkedPairs, report, issues);
        }

        private static void CheckPenetration(Collider a, Collider b,
            HashSet<(EntityId First, EntityId Second)> checkedPairs,
            StringBuilder report, List<string> issues)
        {
            EntityId aId = a.GetEntityId();
            EntityId bId = b.GetEntityId();
            (EntityId First, EntityId Second) key = aId < bId ? (aId, bId) : (bId, aId);
            if (!checkedPairs.Add(key) || !a.enabled || !b.enabled || a.isTrigger || b.isTrigger) return;

            if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                    b, b.transform.position, b.transform.rotation, out _, out float distance) && distance > .001f)
            {
                string message = $"Solid overlap: '{Path(a.transform)}' and '{Path(b.transform)}' penetrate {distance:F4} m.";
                issues.Add(message);
                report.AppendLine(message);
            }
        }

        private static bool IsChildOf(Transform value, Transform possibleParent) =>
            value == possibleParent || value.IsChildOf(possibleParent);

        private static bool ApproximatelyOne(Vector3 scale) =>
            Mathf.Abs(scale.x - 1f) < .001f && Mathf.Abs(scale.y - 1f) < .001f && Mathf.Abs(scale.z - 1f) < .001f;

        private static string Path(Transform value)
        {
            string path = value.name;
            while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
    }
}
