#if UNITY_EDITOR
using System;
using System.IO;
using LaunchRamp.Input;
using LaunchRamp.Trailer;
using LaunchRamp.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LaunchRamp.Editor
{
    /// <summary>
    /// Builds the scene through Unity's object API (never YAML). VehiclePrototype owns
    /// two sibling rigid bodies, Truck and Trailer, joined at their named hitch points.
    /// </summary>
    public static class LaunchRampPrototypeBuilder
    {
        private const string ScenePath = "Assets/_LaunchRamp/Scenes/Testing/VehiclePhysicsTest.unity";
        private const string RootName = "VehiclePrototype";
        private const string GroundName = "TestGround";
        private const float TruckMass = 3200f, TrailerMass = 1700f;
        private const float WheelRadius = .52f, WheelWidth = .34f, SuspensionDistance = .28f;
        private const float MotorTorque = 2100f, BrakeTorque = 3600f, ParkingBrakeTorque = 6500f;
        private const float SteerAngle = 30f, ReverseEngagementSpeed = 1.5f;
        private static readonly Vector3 TruckSize = new(2.2f, 1f, 4.8f);
        private static readonly Vector3 TrailerSize = new(2.35f, .8f, 5f);
        private static readonly Vector3 TruckColliderSize = new(2.1f, .4f, 4.6f);
        private static readonly Vector3 TruckColliderCenter = new(0f, .4f, 0f);
        private static readonly Vector3 TrailerColliderSize = new(2.25f, .4f, 4.8f);
        private static readonly Vector3 TrailerColliderCenter = new(0f, .42f, 0f);

        [MenuItem("Launch Ramp/Build Vehicle Physics Prototype")]
        public static void Build()
        {
            try
            {
                Scene scene = OpenTargetScene();
                ReplaceRoot(scene);
                EnsureGround(scene);
                GameObject root = new(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Rigidbody truck = BuildTruck(root.transform);
                BuildTrailer(root.transform, truck);
                root.AddComponent<VehiclePhysicsValidator>();
                EnsureLight(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"Unity could not save '{ScenePath}'.");
                Selection.activeGameObject = root;
                Debug.Log($"[Launch Ramp] Vehicle physics prototype built and saved successfully: {ScenePath}", root);
            }
            catch (OperationCanceledException e) { Debug.LogWarning($"[Launch Ramp] Build cancelled: {e.Message}"); }
            catch (Exception e) { Debug.LogError($"[Launch Ramp] Prototype build failed: {e.Message}\n{e}"); }
        }

        [MenuItem("Launch Ramp/Validate Vehicle Physics Prototype")]
        public static void Validate()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                Debug.LogError($"[Launch Ramp] Validation failed: '{RootName}' is not present in the active scene. Run the builder first.");
                return;
            }

            VehiclePhysicsValidator.Validate(root, true);
        }

        private static Scene OpenTargetScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("the current modified scene was not saved.");
            string directory = Path.GetDirectoryName(ScenePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Invalid target scene path.");
            Directory.CreateDirectory(directory);
            return File.Exists(ScenePath) ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void ReplaceRoot(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == RootName) UnityEngine.Object.DestroyImmediate(root);
        }

        private static void EnsureGround(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.name == GroundName) return;
            GameObject ground = Primitive(GroundName, PrimitiveType.Cube, null, new(0f, -.25f, 0f), new(80f, .5f, 80f), false);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        private static Rigidbody BuildTruck(Transform parent)
        {
            GameObject truck = Group("Truck", parent).gameObject;
            truck.transform.localPosition = new(0f, 1.1f, 3.5f);
            Rigidbody body = truck.AddComponent<Rigidbody>();
            body.mass = TruckMass; body.centerOfMass = new(0f, -.35f, .15f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            BoxCollider truckCollider = truck.AddComponent<BoxCollider>();
            truckCollider.size = TruckColliderSize;
            truckCollider.center = TruckColliderCenter;
            Primitive("Chassis", PrimitiveType.Cube, truck.transform, Vector3.zero, TruckSize, true);
            Primitive("Cab", PrimitiveType.Cube, truck.transform, new(0f, 1.05f, 1.15f), new(2.05f, 1.3f, 1.8f), true);

            Transform wheelGroup = Group("Wheels", truck.transform);
            Vector3[] positions = { new(-1.05f, -.45f, 1.55f), new(1.05f, -.45f, 1.55f),
                new(-1.05f, -.45f, -1.55f), new(1.05f, -.45f, -1.55f) };
            var wheels = new PrototypeTruckController.WheelBinding[4];
            for (int i = 0; i < positions.Length; i++)
            {
                string name = (i < 2 ? "Front" : "Rear") + (positions[i].x < 0 ? "Left" : "Right");
                Transform mount = Group(name + "Collider", wheelGroup); mount.localPosition = positions[i];
                wheels[i] = new PrototypeTruckController.WheelBinding { Collider = Wheel(mount.gameObject),
                    Visual = WheelVisual(name + "Visual", truck.transform, positions[i]), Steers = i < 2, Drives = i >= 2 };
            }
            Transform hitch = Group("HitchPoint", truck.transform); hitch.localPosition = new(0f, 0f, -2.65f);
            Transform camera = Group("DriverCameraMount", truck.transform); camera.localPosition = new(0f, 1.65f, .8f);
            truck.AddComponent<VehicleInputReader>();
            truck.AddComponent<PrototypeTruckController>().Configure(wheels, MotorTorque, BrakeTorque,
                ParkingBrakeTorque, SteerAngle, ReverseEngagementSpeed);
            return body;
        }

        private static void BuildTrailer(Transform parent, Rigidbody truckBody)
        {
            GameObject trailer = Group("Trailer", parent).gameObject;
            // This position makes both empty hitch transforms coincide at world Z 0.85.
            // The truck and trailer solid boxes still retain 0.4 m of longitudinal clearance.
            trailer.transform.localPosition = new(0f, 1f, -1.8f);
            Rigidbody body = trailer.AddComponent<Rigidbody>();
            body.mass = TrailerMass; body.centerOfMass = new(0f, -.25f, -.15f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            BoxCollider trailerCollider = trailer.AddComponent<BoxCollider>();
            trailerCollider.size = TrailerColliderSize;
            trailerCollider.center = TrailerColliderCenter;
            Primitive("TrailerBody", PrimitiveType.Cube, trailer.transform, Vector3.zero, TrailerSize, true);

            Transform wheelGroup = Group("Wheels", trailer.transform);
            Vector3[] positions = { new(-1.12f, -.35f, -.75f), new(1.12f, -.35f, -.75f) };
            var wheels = new PrototypeTrailer.WheelBinding[2];
            for (int i = 0; i < 2; i++)
            {
                string name = i == 0 ? "TrailerLeft" : "TrailerRight";
                Transform mount = Group(name + "Collider", wheelGroup); mount.localPosition = positions[i];
                wheels[i] = new PrototypeTrailer.WheelBinding { Collider = Wheel(mount.gameObject),
                    Visual = WheelVisual(name + "Visual", trailer.transform, positions[i]) };
            }
            Transform hitch = Group("HitchPoint", trailer.transform); hitch.localPosition = new(0f, 0f, 2.65f);
            ConfigurableJoint joint = trailer.AddComponent<ConfigurableJoint>();
            joint.connectedBody = truckBody; joint.autoConfigureConnectedAnchor = false;
            joint.anchor = hitch.localPosition; joint.connectedAnchor = new(0f, 0f, -2.65f);
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
            joint.angularYLimit = new SoftJointLimit { limit = 35f };
            joint.angularZLimit = new SoftJointLimit { limit = 10f };
            joint.enableCollision = false;
            trailer.AddComponent<PrototypeTrailer>().Configure(wheels);
        }

        private static WheelCollider Wheel(GameObject target)
        {
            WheelCollider wheel = target.AddComponent<WheelCollider>();
            wheel.radius = WheelRadius; wheel.suspensionDistance = SuspensionDistance; wheel.mass = 35f;
            JointSpring spring = wheel.suspensionSpring;
            spring.spring = 38000f; spring.damper = 5200f; spring.targetPosition = .5f;
            wheel.suspensionSpring = spring;
            return wheel;
        }

        private static Transform WheelVisual(string name, Transform parent, Vector3 position)
        {
            GameObject visual = Primitive(name, PrimitiveType.Cylinder, parent, position,
                new(WheelRadius * 2f, WheelWidth * .5f, WheelRadius * 2f), true);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return visual.transform;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent,
            Vector3 position, Vector3 scale, bool removeCollider)
        {
            GameObject value = GameObject.CreatePrimitive(type); value.name = name;
            value.transform.SetParent(parent, false); value.transform.localPosition = position; value.transform.localScale = scale;
            if (removeCollider)
                foreach (Collider collider in value.GetComponents<Collider>())
                    UnityEngine.Object.DestroyImmediate(collider);
            return value;
        }

        private static Transform Group(string name, Transform parent)
        {
            GameObject value = new(name); value.transform.SetParent(parent, false); return value.transform;
        }

        private static void EnsureLight(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects()) if (root.GetComponentInChildren<Light>() != null) return;
            GameObject value = new("Directional Light"); SceneManager.MoveGameObjectToScene(value, scene);
            value.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = value.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
        }
    }
}
#endif
