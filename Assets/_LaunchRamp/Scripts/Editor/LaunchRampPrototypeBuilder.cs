#if UNITY_EDITOR
using System;
using System.IO;
using LaunchRamp.Input;
using LaunchRamp.Camera;
using LaunchRamp.Environment;
using LaunchRamp.Trailer;
using LaunchRamp.UI;
using LaunchRamp.Vehicle;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace LaunchRamp.Editor
{
    /// <summary>
    /// Builds the scene through Unity's object API (never YAML). VehiclePrototype owns
    /// two sibling rigid bodies, Truck and Trailer, joined at their named hitch points.
    /// </summary>
    public static class LaunchRampPrototypeBuilder
    {
        private const string ScenePath = "Assets/_LaunchRamp/Scenes/Testing/VehiclePhysicsTest.unity";
        private const string BoatRampScenePath = "Assets/_LaunchRamp/Scenes/Testing/BoatRampGrayboxTest.unity";
        private const string BoatRampEnvironmentName = "BoatRampGrayboxEnvironment";
        private const string RootName = "VehiclePrototype";
        private const string GroundName = "TestGround";
        private const string CourseName = "DiagnosticCourse";
        private const bool ConnectTrailer = true;
        private const float TruckMass = 2200f, TrailerFrameMass = 600f, BoatLoadMass = 1050f;
        private const float TrailerMass = TrailerFrameMass + BoatLoadMass;
        private const float BoatLength = 6.3f, BoatBeam = 2.2f, BoatHullHeight = 1.15f;
        private const float BoatCenterZ = -.10f, BoatHullBottomY = .23f;
        private const float TrailerYawLimit = 70f, BoatJackknifeSafetyMargin = 4f;
        private const float WheelRadius = .52f, WheelWidth = .34f, SuspensionDistance = .28f;
        private const float TruckSuspensionSpring = 38000f, TruckSuspensionDamper = 5200f;
        private const float TrailerWheelRadius = .38f, TrailerWheelWidth = .30f;
        private const float TrailerSuspensionSpring = 20000f, TrailerSuspensionDamper = 3000f;
        private const float TrailerLateralGrip = 3000f, TrailerRollingResistance = 50f;
        private const float TruckHalfWidth = 1.10f;
        private const float MirrorOutboardExtension = .30f;
        private const float MirrorTargetOutboardOffset = .15f;
        private const float MirrorTargetHeightOffset = .65f;
        private const string PrototypeMaterialFolder = "Assets/_LaunchRamp/Materials/Prototype";
        private const string MirrorSurfaceLayerName = "MirrorSurface";
        private const string LeftMirrorTexturePath = "Assets/_LaunchRamp/Materials/LeftMirrorRenderTexture.renderTexture";
        private const string RightMirrorTexturePath = "Assets/_LaunchRamp/Materials/RightMirrorRenderTexture.renderTexture";
        private const string LeftMirrorMaterialPath = "Assets/_LaunchRamp/Materials/LeftMirrorPrototype.mat";
        private const string RightMirrorMaterialPath = "Assets/_LaunchRamp/Materials/RightMirrorPrototype.mat";
        private static readonly Vector3 DriverEyePosition = new(-.50f, 1.62f, .50f);
        private static readonly Vector3 LeftMirrorPosition = new(-1.38f, 1.52f, 1.08f);
        private static readonly Vector3 RightMirrorPosition = new(1.38f, 1.52f, 1.08f);
        private static readonly Vector3 LeftMirrorOpticalPosition = new(-TruckHalfWidth - MirrorOutboardExtension, 1.54f, 1.12f);
        private static readonly Vector3 RightMirrorOpticalPosition = new(TruckHalfWidth + MirrorOutboardExtension, 1.54f, 1.12f);
        private static readonly Vector3 LeftMirrorAimTrim = Vector3.zero;
        private static readonly Vector3 RightMirrorAimTrim = Vector3.zero;
        private const float LeftMirrorSurfaceYawOffset = 0f, RightMirrorSurfaceYawOffset = 0f;
        private const float MirrorWidth = .64f, MirrorHeight = .30f, MirrorThickness = .06f;
        private const float MirrorFieldOfView = 42f;
        private const float MotorTorque = 2100f, BrakeTorque = 7000f, ParkingBrakeTorque = 9000f;
        private const float SteerAngle = 30f, SafeDirectionChangeSpeed = .5f;
        private const float TruckOverallLength = 5.7f, TruckWheelbase = 3.5f;
        private const float TruckFrontAxleZ = 1.45f, TruckRearAxleZ = -2.05f, TruckHitchZ = -3.05f;
        private const float TrailerOverallLength = 5.8f, TrailerBodyCenterZ = -.4f;
        private const float TrailerHitchZ = 3.2f, TrailerAxleZ = -1f, TrailerHitchToAxle = 4.2f;
        private const float TrailerAxleSpread = 1f;
        private const float TrailerFrontAxleZ = TrailerAxleZ + TrailerAxleSpread * .5f;
        private const float TrailerRearAxleZ = TrailerAxleZ - TrailerAxleSpread * .5f;
        private static readonly Vector3 TruckSize = new(2.2f, .35f, 5.5f);
        private static readonly Vector3 TrailerSize = new(2.2f, .8f, TrailerOverallLength);
        private static readonly Vector3 TruckColliderSize = new(2.1f, .4f, 5.6f);
        private static readonly Vector3 TruckColliderCenter = new(0f, .25f, 0f);
        private static readonly Vector3 TrailerColliderSize = new(2.1f, .4f, TrailerOverallLength);
        private static readonly Vector3 TrailerColliderCenter = new(0f, .42f, TrailerBodyCenterZ);
        private const float MinimumBodyClearance = .5f;
        private static Material truckMaterial, trailerMaterial, wheelMaterial, groundMaterial;
        private static Material lineMaterial, targetMaterial, hazardMaterial, hitchMaterial, mirrorHousingMaterial;
        private static Material asphaltMaterial, concreteMaterial, waterMaterial, terrainMaterial, dockMaterial;
        private static Material boatHullMaterial, boatAccentMaterial, boatWindshieldMaterial;

        [MenuItem("Launch Ramp/Build Vehicle Physics Prototype")]
        public static void Build() => BuildPrototype(ConnectTrailer);

        [MenuItem("Launch Ramp/Build Truck-Only Physics Prototype")]
        public static void BuildTruckOnly() => BuildPrototype(false);

        [MenuItem("Launch Ramp/Build Boat Ramp Graybox Test")]
        public static void BuildBoatRamp() => BuildBoatRampPrototype();

        private static void BuildPrototype(bool connectTrailer)
        {
            try
            {
                Scene scene = OpenTargetScene(ScenePath);
                EnsurePrototypeMaterials();
                ReplaceRoot(scene);
                EnsureGround(scene);
                GameObject root = new(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Rigidbody truck = BuildTruck(root.transform);
                BuildTrailer(root.transform, truck, connectTrailer);
                Rigidbody trailer = root.transform.Find("Trailer").GetComponent<Rigidbody>();
                Transform truckHitch = truck.transform.Find("HitchPoint");
                Transform trailerHitch = trailer.transform.Find("HitchPoint");
                ConfigurableJoint hitchJoint = trailer.GetComponent<ConfigurableJoint>();
                ValidateBodyClearance(truck.GetComponent<BoxCollider>(), trailer.GetComponent<BoxCollider>());
                root.AddComponent<VehicleRigReset>().Configure(truck, trailer);
                if (connectTrailer)
                    root.AddComponent<TrailerRigDiagnostics>().Configure(
                        truck,
                        trailer,
                        trailer.GetComponent<BoxCollider>(),
                        FindGroundCollider(scene),
                        hitchJoint,
                        truckHitch,
                        trailerHitch);
                BuildCourse(root.transform);
                BuildCameras(scene, root, truck.transform.Find("DriverCameraMount"));
                BuildHandlingPanel(root, truck, trailer, truckHitch, trailerHitch);
                root.AddComponent<VehiclePhysicsValidator>().Configure(connectTrailer);
                EnsureLight(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"Unity could not save '{ScenePath}'.");
                Selection.activeGameObject = root;
                Debug.Log($"[Launch Ramp] Vehicle physics prototype built and saved successfully: {ScenePath}", root);
            }
            catch (OperationCanceledException e) { Debug.LogWarning($"[Launch Ramp] Build cancelled: {e.Message}"); }
            catch (Exception e) { Debug.LogError($"[Launch Ramp] Prototype build failed: {e.Message}\n{e}"); }
        }

        private static void BuildBoatRampPrototype()
        {
            try
            {
                Scene scene = OpenTargetScene(BoatRampScenePath);
                EnsurePrototypeMaterials();
                ReplaceNamedRoot(scene, RootName);
                ReplaceNamedRoot(scene, BoatRampEnvironmentName);
                Transform crestReference = BuildBoatRampEnvironment(scene);

                GameObject root = new(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Quaternion entranceRotation = Quaternion.Euler(0f, 180f, 0f);
                Rigidbody truck = BuildTruck(root.transform, new Vector3(0f, 6.1f, 76f), entranceRotation);
                BuildTrailer(root.transform, truck, true);
                Rigidbody trailer = root.transform.Find("Trailer").GetComponent<Rigidbody>();
                Transform truckHitch = truck.transform.Find("HitchPoint");
                Transform trailerHitch = trailer.transform.Find("HitchPoint");
                ValidateBodyClearance(truck.GetComponent<BoxCollider>(), trailer.GetComponent<BoxCollider>());

                VehicleRigReset reset = root.AddComponent<VehicleRigReset>();
                reset.Configure(truck, trailer);
                ComputeConnectedRigPose(new Vector3(-2.2f, 6.1f, 27f), entranceRotation,
                    out Vector3 practiceTruck, out Quaternion practiceTruckRotation,
                    out Vector3 practiceTrailer, out Quaternion practiceTrailerRotation);
                reset.ConfigurePracticeSpawn(practiceTruck, practiceTruckRotation, practiceTrailer, practiceTrailerRotation);

                root.AddComponent<TrailerRigDiagnostics>().Configure(truck, trailer, trailer.GetComponent<BoxCollider>(),
                    FindEnvironmentGroundCollider(scene), trailer.GetComponent<ConfigurableJoint>(), truckHitch, trailerHitch);
                BuildCameras(scene, root, truck.transform.Find("DriverCameraMount"),
                    new Vector3(0f, 1.5f, 5f), new Vector3(38f, 32f, 48f));
                BuildHandlingPanel(root, truck, trailer, truckHitch, trailerHitch);
                BuildBoatRampSightLineDebug(root, truck.transform.Find("DriverCameraMount"), trailer.transform, crestReference);
                root.AddComponent<VehiclePhysicsValidator>().Configure(true);
                EnsureLight(scene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, BoatRampScenePath))
                    throw new InvalidOperationException($"Unity could not save '{BoatRampScenePath}'.");
                Selection.activeGameObject = root;
                Debug.Log($"[Launch Ramp] Boat-ramp gray-box test built and saved: {BoatRampScenePath}", root);
            }
            catch (OperationCanceledException e) { Debug.LogWarning($"[Launch Ramp] Build cancelled: {e.Message}"); }
            catch (Exception e) { Debug.LogError($"[Launch Ramp] Boat-ramp build failed: {e.Message}\n{e}"); }
        }

        private static void ComputeConnectedRigPose(Vector3 truckPosition, Quaternion truckRotation,
            out Vector3 resultTruckPosition, out Quaternion resultTruckRotation,
            out Vector3 resultTrailerPosition, out Quaternion resultTrailerRotation)
        {
            resultTruckPosition = truckPosition; resultTruckRotation = truckRotation;
            resultTrailerRotation = truckRotation;
            Vector3 hitchWorld = truckPosition + truckRotation * new Vector3(0f, 0f, TruckHitchZ);
            resultTrailerPosition = hitchWorld - truckRotation * new Vector3(0f, 0f, TrailerHitchZ);
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

        [MenuItem("Launch Ramp/Validate Boat Ramp Graybox Test")]
        public static void ValidateBoatRamp()
        {
            GameObject environment = GameObject.Find(BoatRampEnvironmentName);
            GameObject vehicle = GameObject.Find(RootName);
            var issues = new System.Collections.Generic.List<string>();
            if (environment == null) issues.Add($"Missing {BoatRampEnvironmentName} root.");
            if (vehicle == null) issues.Add($"Missing {RootName} root.");
            if (Mathf.Abs(9f - 4f * 2f - 1f) > .001f) issues.Add("Ramp lane-width configuration is invalid.");
            if (Mathf.Abs((4.64f - .32f) / 36f - .12f) > .001f) issues.Add("Main ramp grade is not 12 percent.");
            if (environment != null)
            {
                Transform water = environment.transform.Find("WaterPlane");
                if (water == null || Mathf.Abs(water.GetComponent<Renderer>().bounds.max.y) > .01f)
                    issues.Add("Water surface is missing or not at Y=0.");
                foreach (string path in new[] { "EntranceAccessRoad", "UpperStagingPavement", "RampApproach",
                             "TwoLaneLaunchRamp/MainRamp_12Percent", "ModularDock/DockWalkway" })
                    if (environment.transform.Find(path)?.GetComponent<Collider>() == null)
                        issues.Add($"Required ground collider is missing: {path}.");
                if (environment.transform.Find("RampCrestReference") == null)
                    issues.Add("Ramp crest sight-line reference is missing.");
                Collider dock = environment.transform.Find("ModularDock/DockWalkway")?.GetComponent<Collider>();
                if (dock != null && dock.bounds.min.x - 4.5f < .4f)
                    issues.Add("Dock clearance from the 9 m ramp is below 0.4 m.");
            }
            if (vehicle != null)
            {
                VehiclePhysicsValidator.Validate(vehicle, true);
                Transform truck = vehicle.transform.Find("Truck");
                Transform trailer = vehicle.transform.Find("Trailer");
                if (trailer?.Find("BoatLoad/Hull") == null || trailer.Find("BoatLoad/Bow") == null ||
                    trailer.Find("BoatLoad/BoatTopReference") == null || trailer.Find("BoatLoad/BoatSternReference") == null)
                    issues.Add("Secured BoatLoad hierarchy or sight-line references are missing.");
                Rigidbody trailerBody = trailer?.GetComponent<Rigidbody>();
                if (trailerBody == null || Mathf.Abs(trailerBody.mass - TrailerMass) > .1f)
                    issues.Add($"Trailer/load mass is not the configured {TrailerMass:F0} kg.");
                Transform truckHitch = truck?.Find("HitchPoint"); Transform trailerHitch = trailer?.Find("HitchPoint");
                if (truckHitch == null || trailerHitch == null || Vector3.Distance(truckHitch.position, trailerHitch.position) > .01f)
                    issues.Add("Truck/trailer hitch anchors are not coincident.");
                foreach (string wheel in new[] { "Wheels/FrontLeftWheelPoint", "Wheels/FrontRightWheelPoint",
                             "Wheels/RearLeftWheelPoint", "Wheels/RearRightWheelPoint" })
                {
                    Transform point = trailer?.Find(wheel);
                    if (point == null || !Physics.Raycast(point.position, -trailer.up, out _, TrailerWheelRadius + .3f,
                            ~0, QueryTriggerInteraction.Ignore))
                        issues.Add($"Passive trailer suspension point is not grounded: {wheel}.");
                }
            }
            if (issues.Count == 0) Debug.Log("[Launch Ramp] Boat-ramp gray-box validation PASS.", environment);
            else Debug.LogError($"[Launch Ramp] Boat-ramp validation failed:\n - {string.Join("\n - ", issues)}", environment);
        }

        private static Scene OpenTargetScene(string scenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new OperationCanceledException("the current modified scene was not saved.");
            string directory = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) throw new InvalidOperationException("Invalid target scene path.");
            Directory.CreateDirectory(directory);
            return File.Exists(scenePath) ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single) :
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static void ReplaceRoot(Scene scene)
        {
            ReplaceNamedRoot(scene, RootName);
        }

        private static void ReplaceNamedRoot(Scene scene, string rootName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == rootName) UnityEngine.Object.DestroyImmediate(root);
        }

        private static void EnsureGround(Scene scene)
        {
            // Always replace the ground so its collider type and top surface are deterministic.
            // A template Plane at Y=-0.25 leaves these WheelColliders outside suspension reach.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != GroundName) continue;
                UnityEngine.Object.DestroyImmediate(root);
            }
            GameObject ground = Primitive(GroundName, PrimitiveType.Cube, null, new(0f, -.25f, 25f), new(100f, .5f, 180f), false);
            SetMaterial(ground, groundMaterial);
            SceneManager.MoveGameObjectToScene(ground, scene);
        }

        private static void BuildCourse(Transform parent)
        {
            // Collider-free backing course: 8 m approach, 3.5 m backing lane, target box, and curved option.
            Transform course = Group(CourseName, parent);
            CourseMark("ApproachCenterLine", course, new(0f, .012f, 40f), new(.15f, .02f, 80f), lineMaterial);
            CourseMark("BackingLaneLeftLine", course, new(-1.75f, .014f, -22.5f), new(.15f, .025f, 45f), lineMaterial);
            CourseMark("BackingLaneRightLine", course, new(1.75f, .014f, -22.5f), new(.15f, .025f, 45f), lineMaterial);
            CourseMark("TargetFill", course, new(0f, .008f, -39f), new(3.35f, .012f, 11.8f), targetMaterial);
            CourseMark("TargetBack", course, new(0f, .026f, -45f), new(3.5f, .04f, .18f), targetMaterial);
            CourseMark("TargetFront", course, new(0f, .026f, -33f), new(3.5f, .04f, .18f), targetMaterial);
            CourseMark("TargetLeft", course, new(-1.75f, .026f, -39f), new(.18f, .04f, 12f), targetMaterial);
            CourseMark("TargetRight", course, new(1.75f, .026f, -39f), new(.18f, .04f, 12f), targetMaterial);

            for (int distance = 0; distance <= 50; distance += 10)
            {
                CourseMark($"DistanceLine_{distance}m", course, new(0f, .016f, distance),
                    new(3.5f, .025f, .12f), lineMaterial);
                SetMaterial(Primitive($"DistanceMarker_{distance}m", PrimitiveType.Cube, course,
                    new(-2.25f, .18f, distance), new(.28f, .36f, .28f), true),
                    distance % 20 == 0 ? hazardMaterial : targetMaterial);
            }

            for (int i = 0; i <= 9; i++)
            {
                float z = -4f - i * 4.5f;
                Material markerMaterial = i % 2 == 0 ? hazardMaterial : lineMaterial;
                SetMaterial(Primitive($"BackingPostLeft_{i}", PrimitiveType.Cylinder, course, new(-2.15f, .4f, z), new(.18f, .4f, .18f), true), markerMaterial);
                SetMaterial(Primitive($"BackingPostRight_{i}", PrimitiveType.Cylinder, course, new(2.15f, .4f, z), new(.18f, .4f, .18f), true), markerMaterial);
            }

            for (int i = 0; i <= 10; i++)
            {
                float angle = Mathf.Lerp(10f, 80f, i / 10f) * Mathf.Deg2Rad;
                Vector3 point = new(10f - Mathf.Cos(angle) * 10f, .35f, 2f + Mathf.Sin(angle) * 10f);
                SetMaterial(Primitive($"CurvedApproachPost_{i}", PrimitiveType.Cube, course, point, new(.35f, .7f, .35f), true),
                    i % 2 == 0 ? hazardMaterial : lineMaterial);
            }
        }

        private static void CourseMark(string name, Transform parent, Vector3 position, Vector3 scale, Material material) =>
            SetMaterial(Primitive(name, PrimitiveType.Cube, parent, position, scale, true), material);

        private static Transform BuildBoatRampEnvironment(Scene scene)
        {
            GameObject environment = new(BoatRampEnvironmentName);
            SceneManager.MoveGameObjectToScene(environment, scene);
            Transform root = environment.transform;

            Surface("EntranceAccessRoad", root, new(0f, 4.75f, 80f), new(10f, .5f, 30f), asphaltMaterial);
            Surface("UpperStagingPavement", root, new(0f, 4.75f, 52.5f), new(68f, .5f, 25f), asphaltMaterial);
            Surface("RampApproach", root, new(0f, 4.75f, 25f), new(12f, .5f, 30f), asphaltMaterial);
            Surface("WestTurnaround", root, new(-20f, 4.75f, 25f), new(28f, .5f, 30f), asphaltMaterial);
            Surface("EastTurnaround", root, new(20f, 4.75f, 25f), new(28f, .5f, 30f), asphaltMaterial);
            Surface("TrailerParkingArea", root, new(38f, 4.75f, 50f), new(8f, .5f, 25f), asphaltMaterial);

            Transform ramp = Group("TwoLaneLaunchRamp", root);
            Vector2[] profile = { new(10f, 5f), new(8f, 4.96f), new(6f, 4.84f),
                new(4f, 4.64f), new(-32f, .32f), new(-45f, -1.24f) };
            string[] names = { "CrestTransition_02Percent", "CrestTransition_06Percent",
                "CrestTransition_10Percent", "MainRamp_12Percent", "SubmergedContinuation" };
            for (int i = 0; i < profile.Length - 1; i++)
                SlopeSurface(names[i], ramp, profile[i], profile[i + 1], 9f, concreteMaterial, out _);

            Transform crest = Group("RampCrestReference", root);
            crest.position = new Vector3(0f, 5.05f, 10f);

            BuildRampMarkings(root, profile);
            BuildDock(root);
            Surface("WestTerrain", root, new(-45f, 2.25f, 25f), new(22f, 5.5f, 140f), terrainMaterial);
            Surface("EastTerrain", root, new(51f, 2.25f, 25f), new(18f, 5.5f, 140f), terrainMaterial);
            Surface("ShorelineWest", root, new(-18f, .7f, -30f), new(27f, 1.4f, 40f), terrainMaterial);
            Surface("ShorelineEast", root, new(22f, .7f, -30f), new(31f, 1.4f, 40f), terrainMaterial);

            GameObject water = Primitive("WaterPlane", PrimitiveType.Cube, root, new(0f, -.06f, -43f),
                new(90f, .12f, 55f), true);
            SetMaterial(water, waterMaterial);

            BuildScenarioTriggers(root);
            return crest;
        }

        private static GameObject Surface(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject value = Primitive(name, PrimitiveType.Cube, parent, position, scale, false);
            SetMaterial(value, material);
            return value;
        }

        private static void SlopeSurface(string name, Transform parent, Vector2 upper, Vector2 lower,
            float width, Material material, out Transform result)
        {
            Vector3 start = new(0f, upper.y, upper.x);
            Vector3 end = new(0f, lower.y, lower.x);
            Vector3 direction = end - start;
            Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            GameObject value = Primitive(name, PrimitiveType.Cube, parent, Vector3.zero,
                new(width, .25f, direction.magnitude), false);
            value.transform.position = (start + end) * .5f - rotation * Vector3.up * .125f;
            value.transform.rotation = rotation;
            SetMaterial(value, material);
            result = value.transform;
        }

        private static void BuildRampMarkings(Transform root, Vector2[] profile)
        {
            Transform markings = Group("ColliderFreeSiteMarkings", root);
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector3 start = new(0f, profile[i].y, profile[i].x);
                Vector3 end = new(0f, profile[i + 1].y, profile[i + 1].x);
                Vector3 direction = end - start;
                Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
                GameObject center = Primitive($"RampCenterLine_{i}", PrimitiveType.Cube, markings, Vector3.zero,
                    new(.14f, .025f, direction.magnitude - .05f), true);
                center.transform.position = (start + end) * .5f + rotation * Vector3.up * .025f;
                center.transform.rotation = rotation;
                SetMaterial(center, lineMaterial);
            }
            for (int i = 0; i < 7; i++)
            {
                float z = 18f + i * 6f;
                CourseMark($"StagingSpace_{i}", markings, new(-18f + i * 6f, 5.015f, 52f),
                    new(4.8f, .025f, .14f), lineMaterial);
            }
            CourseMark("TrafficArrowStem", markings, new(0f, 5.015f, 35f), new(.18f, .025f, 5f), hitchMaterial);
            GameObject arrow = Primitive("TrafficArrowHead", PrimitiveType.Cube, markings,
                new(0f, 5.02f, 32.2f), new(1.5f, .03f, 1.5f), true);
            arrow.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            SetMaterial(arrow, hitchMaterial);
            for (int i = 0; i < 5; i++)
                CourseMark($"TrailerParkingOutline_{i}", markings, new(38f, 5.015f, 41f + i * 4f),
                    new(7.5f, .025f, .12f), lineMaterial);
            Transform loop = Group("TurningLoop", markings);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2f / 16f;
                Vector3 position = new(Mathf.Cos(angle) * 22f, 5.02f, 34f + Mathf.Sin(angle) * 14f);
                GameObject dash = Primitive($"LoopDash_{i}", PrimitiveType.Cube, loop, position,
                    new(.16f, .025f, 2.2f), true);
                dash.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                SetMaterial(dash, hitchMaterial);
            }
        }

        private static void BuildDock(Transform root)
        {
            Transform dock = Group("ModularDock", root);
            Surface("DockWalkway", dock, new(5.8f, .22f, -18f), new(1.6f, .44f, 24f), dockMaterial);
            for (int i = 0; i < 5; i++)
            {
                float z = -7f - i * 5.5f;
                Surface($"DockPiling_{i}", dock, new(6.45f, -.15f, z), new(.25f, 2.1f, .25f), dockMaterial);
            }
        }

        private static void BuildScenarioTriggers(Transform root)
        {
            CreateScenarioTrigger("SiteEntranceTrigger", root, new(0f, 6f, 84f), new(10f, 3f, 2f), "Entered boat-ramp site.");
            CreateScenarioTrigger("StagingAreaTrigger", root, new(0f, 6f, 52f), new(30f, 3f, 5f), "Entered staging area.");
            CreateScenarioTrigger("RampApproachTrigger", root, new(0f, 6f, 20f), new(10f, 3f, 3f), "Approaching launch lanes.");
            CreateScenarioTrigger("RampCrestTrigger", root, new(0f, 5.2f, 9f), new(9f, 3f, 2f), "Crossed ramp crest.");
            CreateScenarioTrigger("TargetLaunchDepthTrigger", root, new(0f, .5f, -35f), new(9f, 4f, 3f), "Reached prototype launch depth.");
            CreateScenarioTrigger("TrailerParkingTrigger", root, new(38f, 6f, 50f), new(8f, 3f, 8f), "Entered trailer parking area.");
        }

        private static void CreateScenarioTrigger(string name, Transform parent, Vector3 position,
            Vector3 size, string message)
        {
            Transform trigger = Group(name, parent); trigger.localPosition = position;
            BoxCollider collider = trigger.gameObject.AddComponent<BoxCollider>(); collider.size = size; collider.isTrigger = true;
            trigger.gameObject.AddComponent<BoatRampScenarioTrigger>().Configure(message);
        }

        private static void BuildBoatRampSightLineDebug(GameObject vehicleRoot, Transform driverEye,
            Transform trailer, Transform crestReference)
        {
            Transform boatTop = trailer.Find("BoatLoad/BoatTopReference");
            Transform boatStern = trailer.Find("BoatLoad/BoatSternReference");
            if (boatTop == null || boatStern == null)
                throw new InvalidOperationException("Boat sight-line reference points were not created.");
            Transform markers = Group("BoatRampSightLineMarkers", vehicleRoot.transform);
            SetMaterial(Primitive("DriverEyeMarker", PrimitiveType.Sphere, markers, Vector3.zero,
                new(.18f, .18f, .18f), true), targetMaterial);
            SetMaterial(Primitive("BoatTopMarker", PrimitiveType.Sphere, markers, Vector3.zero,
                new(.18f, .18f, .18f), true), hitchMaterial);
            SetMaterial(Primitive("BoatSternMarker", PrimitiveType.Sphere, markers, Vector3.zero,
                new(.18f, .18f, .18f), true), boatAccentMaterial);
            SetMaterial(Primitive("CrestMarker", PrimitiveType.Cube, markers, Vector3.zero,
                new(9.4f, .16f, .16f), true), hazardMaterial);
            markers.gameObject.SetActive(false);
            vehicleRoot.AddComponent<BoatRampSightLineDebug>().Configure(driverEye, boatTop, boatStern,
                crestReference, markers.gameObject);
        }

        private static Collider FindEnvironmentGroundCollider(Scene scene)
        {
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot.name != BoatRampEnvironmentName) continue;
                Transform access = sceneRoot.transform.Find("EntranceAccessRoad");
                if (access != null) return access.GetComponent<Collider>();
            }
            return null;
        }

        private static void BuildCameras(Scene scene, GameObject root, Transform driverMount,
            Vector3? diagnosticTargetPosition = null, Vector3? diagnosticCameraPosition = null)
        {
            // Replace prior player and mirror cameras; the prototype root is also recreated each run.
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (UnityEngine.Camera camera in sceneRoot.GetComponentsInChildren<UnityEngine.Camera>(true))
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);

            if (driverMount == null) throw new InvalidOperationException("DriverCameraMount was not created.");
            UnityEngine.Camera driver = CreateCamera("Driver Camera", driverMount, Vector3.zero, Quaternion.identity);
            driver.fieldOfView = 65f;

            Transform target = Group("DiagnosticCameraTarget", root.transform);
            target.position = diagnosticTargetPosition ?? new Vector3(0f, 0f, 32f);
            Transform diagnosticTransform = Group("DiagnosticCamera", root.transform);
            diagnosticTransform.position = diagnosticCameraPosition ?? new Vector3(18f, 28f, -18f);
            diagnosticTransform.rotation = Quaternion.LookRotation(target.position - diagnosticTransform.position);
            UnityEngine.Camera diagnostic = diagnosticTransform.gameObject.AddComponent<UnityEngine.Camera>();
            diagnostic.fieldOfView = 60f;
            diagnostic.farClipPlane = 180f;
            diagnosticTransform.gameObject.AddComponent<AudioListener>();

            Transform exteriorTransform = Group("ExteriorFollowCamera", root.transform);
            exteriorTransform.position = root.transform.Find("Truck").TransformPoint(new Vector3(0f, 6f, -10f));
            UnityEngine.Camera exterior = exteriorTransform.gameObject.AddComponent<UnityEngine.Camera>();
            exterior.fieldOfView = 60f;
            exterior.farClipPlane = 180f;
            exteriorTransform.gameObject.AddComponent<AudioListener>();

            Transform truck = root.transform.Find("Truck");
            Transform developmentCameras = Group("DevelopmentCameras", root.transform);
            Transform freeTransform = Group("FreeCamera", developmentCameras);
            Vector3 freeLookPoint = truck.position + truck.up * 1f;
            freeTransform.position = freeLookPoint - truck.forward * 13f + Vector3.up * 7f;
            freeTransform.rotation = Quaternion.LookRotation(freeLookPoint - freeTransform.position, Vector3.up);
            UnityEngine.Camera free = freeTransform.gameObject.AddComponent<UnityEngine.Camera>();
            free.fieldOfView = diagnostic.fieldOfView;
            free.nearClipPlane = diagnostic.nearClipPlane;
            free.farClipPlane = diagnostic.farClipPlane;
            freeTransform.gameObject.AddComponent<AudioListener>();
            freeTransform.gameObject.AddComponent<UniversalAdditionalCameraData>();
            PrototypeFreeCameraController freeController = freeTransform.gameObject.AddComponent<PrototypeFreeCameraController>();
            freeController.Configure(free, truck, root.transform.Find("Trailer"));

            (UnityEngine.Camera leftMirror, UnityEngine.Camera rightMirror, Transform leftSurface, Transform rightSurface,
                Vector3 leftAimTarget, Vector3 rightAimTarget) =
                BuildMirrors(root.transform.Find("Truck"), driverMount);
            BuildMirrorDebug(root, leftMirror, rightMirror, leftSurface, rightSurface, driverMount,
                leftAimTarget, rightAimTarget, leftMirror.targetTexture, rightMirror.targetTexture);
            root.AddComponent<PrototypeCameraSwitcher>().Configure(driver, diagnostic, exterior, free,
                freeController, target, truck);
        }

        private static (UnityEngine.Camera left, UnityEngine.Camera right, Transform leftSurface, Transform rightSurface,
            Vector3 leftAimTarget, Vector3 rightAimTarget)
            BuildMirrors(Transform truck, Transform driverEye)
        {
            if (truck == null || driverEye == null)
                throw new InvalidOperationException("Truck or DriverCameraMount was not available for mirror creation.");
            int mirrorLayer = EnsureLayer(MirrorSurfaceLayerName);
            RenderTexture leftTexture = EnsureRenderTexture(LeftMirrorTexturePath);
            RenderTexture rightTexture = EnsureRenderTexture(RightMirrorTexturePath);
            Material leftMaterial = EnsureMirrorMaterial(LeftMirrorMaterialPath, leftTexture);
            Material rightMaterial = EnsureMirrorMaterial(RightMirrorMaterialPath, rightTexture);
            Transform trailer = truck.parent.Find("Trailer");
            if (trailer == null) throw new InvalidOperationException("Trailer was not available for mirror calibration.");
            float trailerRearZ = TrailerBodyCenterZ - TrailerOverallLength * .5f;
            Vector3 leftAimTarget = trailer.TransformPoint(new Vector3(
                -TruckHalfWidth - MirrorTargetOutboardOffset, MirrorTargetHeightOffset, trailerRearZ));
            Vector3 rightAimTarget = trailer.TransformPoint(new Vector3(
                TruckHalfWidth + MirrorTargetOutboardOffset, MirrorTargetHeightOffset, trailerRearZ));

            UnityEngine.Camera left = CreateMirrorCamera("LeftMirrorCamera", truck, LeftMirrorOpticalPosition,
                leftAimTarget, LeftMirrorAimTrim, leftTexture, mirrorLayer);
            UnityEngine.Camera right = CreateMirrorCamera("RightMirrorCamera", truck, RightMirrorOpticalPosition,
                rightAimTarget, RightMirrorAimTrim, rightTexture, mirrorLayer);
            Transform leftSurface = CreateMirrorAssembly("LeftMirror", truck, LeftMirrorPosition,
                leftMaterial, mirrorLayer, driverEye, LeftMirrorSurfaceYawOffset);
            Transform rightSurface = CreateMirrorAssembly("RightMirror", truck, RightMirrorPosition,
                rightMaterial, mirrorLayer, driverEye, RightMirrorSurfaceYawOffset);
            return (left, right, leftSurface, rightSurface, leftAimTarget, rightAimTarget);
        }

        private static UnityEngine.Camera CreateMirrorCamera(string name, Transform parent, Vector3 position,
            Vector3 worldAimTarget, Vector3 aimTrim, RenderTexture texture, int mirrorLayer)
        {
            Transform value = Group(name, parent);
            value.localPosition = position;
            Vector3 rearwardAim = worldAimTarget - value.position;
            if (rearwardAim.sqrMagnitude < .001f)
                throw new InvalidOperationException($"{name} has a coincident mirror aim target.");
            value.rotation = Quaternion.LookRotation(rearwardAim.normalized, parent.up) * Quaternion.Euler(aimTrim);
            UnityEngine.Camera camera = value.gameObject.AddComponent<UnityEngine.Camera>();
            camera.targetTexture = texture;
            camera.enabled = true;
            camera.fieldOfView = MirrorFieldOfView;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 120f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.18f, .22f, .28f, 1f);
            camera.cullingMask &= ~(1 << mirrorLayer);
            UniversalAdditionalCameraData data = value.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            return camera;
        }

        private static Transform CreateMirrorAssembly(string name, Transform parent, Vector3 position,
            Material displayMaterial, int mirrorLayer, Transform driverEye, float additionalYaw)
        {
            Transform assembly = Group(name + "Assembly", parent);
            assembly.localPosition = position;
            // Unity's Quad visible normal faces the driver (-local Z) in this placement.
            // Rotating it 180 degrees exposes its back face and makes the black housing appear solid.
            assembly.localRotation = Quaternion.identity;
            GameObject housing = Primitive("MirrorHousing", PrimitiveType.Cube, assembly,
                new(0f, 0f, .035f), new(MirrorWidth + .06f, MirrorHeight + .06f, MirrorThickness), true);
            GameObject display = Primitive("MirrorSurface", PrimitiveType.Quad, assembly,
                new(0f, 0f, 0f), new(MirrorWidth, MirrorHeight, 1f), true);
            Vector3 directionToDriver = assembly.InverseTransformDirection(driverEye.position - display.transform.position);
            directionToDriver.y = 0f;
            if (directionToDriver.sqrMagnitude < .001f)
                throw new InvalidOperationException($"{name} cannot aim at a coincident DriverCameraMount.");
            // A Unity Quad's visible normal is -forward, so forward points away from the driver.
            display.transform.localRotation = Quaternion.LookRotation(-directionToDriver.normalized, Vector3.up) *
                Quaternion.Euler(0f, additionalYaw, 0f);
            SetMaterial(housing, mirrorHousingMaterial);
            SetMaterial(display, displayMaterial);
            housing.layer = display.layer = mirrorLayer;
            return display.transform;
        }

        private static RenderTexture EnsureRenderTexture(string path)
        {
            RenderTexture texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (texture == null)
            {
                texture = new RenderTexture(512, 256, 16) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(texture, path);
            }
            texture.width = 512;
            texture.height = 256;
            texture.depth = 16;
            texture.filterMode = FilterMode.Bilinear;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Material EnsureMirrorMaterial(string path, RenderTexture texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("No compatible unlit mirror-display shader was available.");
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            material.mainTexture = texture;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            // A negative U scale makes the rear camera feed read like a physical mirror.
            material.mainTextureScale = new Vector2(-1f, 1f);
            material.mainTextureOffset = new Vector2(1f, 0f);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
                material.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
                material.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildHandlingPanel(GameObject root, Rigidbody truck, Rigidbody trailer,
            Transform truckHitch, Transform trailerHitch)
        {
            GameObject canvasObject = new("PrototypeDebugCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject panel = new("HandlingDebugPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = panelRect.anchorMax = panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(18f, -18f);
            panelRect.sizeDelta = new Vector2(420f, 350f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, .68f);

            GameObject textObject = new("Telemetry", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panel.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 10f); textRect.offsetMax = new Vector2(-14f, -10f);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f; text.color = Color.white; text.alignment = TextAlignmentOptions.TopLeft;

            root.AddComponent<PrototypeHandlingDebugPanel>().Configure(panel, text,
                truck.GetComponent<PrototypeTruckController>(), truck, trailer, truckHitch,
                trailerHitch, trailer.GetComponent<PassiveTrailerAxle>(), truck.transform.Find("DriverCameraMount"),
                trailer.transform.Find("BoatLoad/BoatTopReference"));
        }

        private static void BuildMirrorDebug(GameObject root, UnityEngine.Camera left, UnityEngine.Camera right,
            Transform leftSurface, Transform rightSurface, Transform driverEye,
            Vector3 leftAimTarget, Vector3 rightAimTarget, RenderTexture leftTexture, RenderTexture rightTexture)
        {
            GameObject canvasObject = new("MirrorDebugCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            CreateMirrorViewport("LeftMirrorViewport", "LEFT MIRROR", canvasObject.transform, leftTexture, true);
            CreateMirrorViewport("RightMirrorViewport", "RIGHT MIRROR", canvasObject.transform, rightTexture, false);
            GameObject textObject = new("MirrorAimDetails", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 22f);
            rect.sizeDelta = new Vector2(820f, 140f);
            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 16f; label.color = Color.white; label.alignment = TextAlignmentOptions.TopRight;
            Transform markers = BuildMirrorCalibrationMarkers(root.transform, leftAimTarget, rightAimTarget);
            root.AddComponent<PrototypeMirrorDebug>().Configure(canvasObject, left, right, leftSurface,
                rightSurface, driverEye, root.transform.Find("Truck"), root.transform.Find("Trailer"), markers.gameObject,
                leftAimTarget, rightAimTarget, TruckHalfWidth * 2f, TrailerSize.x, MirrorOutboardExtension,
                TruckOverallLength, TruckWheelbase, TrailerOverallLength, TrailerHitchToAxle, label);
        }

        private static Transform BuildMirrorCalibrationMarkers(Transform root, Vector3 leftAimTarget, Vector3 rightAimTarget)
        {
            Transform markers = Group("MirrorCalibrationMarkers", root);
            Transform truck = root.Find("Truck");
            Transform trailer = root.Find("Trailer");
            float truckRearZ = -TruckOverallLength * .5f;
            float trailerRearZ = TrailerBodyCenterZ - TrailerOverallLength * .5f;
            CalibrationMarker("TruckLeftRear", markers, truck.TransformPoint(new Vector3(-TruckHalfWidth, 0f, truckRearZ)), lineMaterial);
            CalibrationMarker("TruckRightRear", markers, truck.TransformPoint(new Vector3(TruckHalfWidth, 0f, truckRearZ)), lineMaterial);
            CalibrationMarker("TrailerLeftRear", markers, trailer.TransformPoint(new Vector3(-TruckHalfWidth, 0f, trailerRearZ)), hazardMaterial);
            CalibrationMarker("TrailerRightRear", markers, trailer.TransformPoint(new Vector3(TruckHalfWidth, 0f, trailerRearZ)), hazardMaterial);
            CalibrationMarker("LeftMirrorAimTarget", markers, leftAimTarget, targetMaterial);
            CalibrationMarker("RightMirrorAimTarget", markers, rightAimTarget, hitchMaterial);
            markers.gameObject.SetActive(false);
            return markers;
        }

        private static void CalibrationMarker(string name, Transform parent, Vector3 worldPosition, Material material)
        {
            GameObject marker = Primitive(name, PrimitiveType.Sphere, parent, Vector3.zero, new(.16f, .16f, .16f), true);
            marker.transform.position = worldPosition;
            SetMaterial(marker, material);
        }

        private static void CreateMirrorViewport(string name, string labelText, Transform parent,
            RenderTexture texture, bool left)
        {
            GameObject value = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            value.transform.SetParent(parent, false);
            RectTransform rect = value.GetComponent<RectTransform>();
            Vector2 corner = left ? Vector2.zero : new Vector2(1f, 0f);
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = left ? new Vector2(22f, 22f) : new Vector2(-22f, 22f);
            rect.sizeDelta = new Vector2(420f, 210f);
            RawImage image = value.GetComponent<RawImage>();
            image.texture = texture;
            image.uvRect = new Rect(1f, 0f, -1f, 1f);

            GameObject labelObject = new("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(value.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f); labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(.5f, 0f); labelRect.anchoredPosition = new Vector2(0f, 5f);
            labelRect.sizeDelta = new Vector2(0f, 28f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = labelText; label.fontSize = 19f; label.fontStyle = FontStyles.Bold;
            label.color = Color.white; label.alignment = TextAlignmentOptions.Center;
        }

        private static UnityEngine.Camera CreateCamera(string name, Transform parent, Vector3 localPosition,
            Quaternion localRotation)
        {
            Transform cameraTransform = Group(name, parent);
            cameraTransform.localPosition = localPosition;
            cameraTransform.localRotation = localRotation;
            UnityEngine.Camera camera = cameraTransform.gameObject.AddComponent<UnityEngine.Camera>();
            cameraTransform.gameObject.AddComponent<AudioListener>();
            return camera;
        }

        private static Rigidbody BuildTruck(Transform parent, Vector3? spawnPosition = null, Quaternion? spawnRotation = null)
        {
            GameObject truck = Group("Truck", parent).gameObject;
            truck.transform.localPosition = spawnPosition ?? new Vector3(0f, 1.1f, 3.5f);
            truck.transform.localRotation = spawnRotation ?? Quaternion.identity;
            Rigidbody body = truck.AddComponent<Rigidbody>();
            body.mass = TruckMass; body.centerOfMass = new(0f, -.30f, .10f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            BoxCollider truckCollider = truck.AddComponent<BoxCollider>();
            truckCollider.size = TruckColliderSize;
            truckCollider.center = TruckColliderCenter;
            BuildPickupVisuals(truck.transform);

            Transform wheelGroup = Group("Wheels", truck.transform);
            Vector3[] positions = { new(-1.05f, -.45f, TruckFrontAxleZ), new(1.05f, -.45f, TruckFrontAxleZ),
                new(-1.05f, -.45f, TruckRearAxleZ), new(1.05f, -.45f, TruckRearAxleZ) };
            var wheels = new PrototypeTruckController.WheelBinding[4];
            for (int i = 0; i < positions.Length; i++)
            {
                string name = (i < 2 ? "Front" : "Rear") + (positions[i].x < 0 ? "Left" : "Right");
                Transform mount = Group(name + "Collider", wheelGroup); mount.localPosition = positions[i];
                wheels[i] = new PrototypeTruckController.WheelBinding { Collider = TruckWheel(mount.gameObject),
                    Visual = WheelVisual(name + "Visual", truck.transform, positions[i]), Steers = i < 2, Drives = i >= 2 };
            }
            Transform hitch = Group("HitchPoint", truck.transform); hitch.localPosition = new(0f, 0f, TruckHitchZ);
            SetMaterial(Primitive("HitchVisual", PrimitiveType.Sphere, hitch, Vector3.zero, new(.18f, .18f, .18f), true), hitchMaterial);
            Transform camera = Group("DriverCameraMount", truck.transform); camera.localPosition = DriverEyePosition;
            truck.AddComponent<VehicleInputReader>();
            truck.AddComponent<PrototypeTruckController>().Configure(wheels, MotorTorque, BrakeTorque,
                ParkingBrakeTorque, SteerAngle, SafeDirectionChangeSpeed);
            return body;
        }

        private static void BuildPickupVisuals(Transform truck)
        {
            // Collider-free pickup silhouette. Open spaces between these pieces provide windshield,
            // side-window, and rear-window sightlines while one simple BoxCollider handles physics.
            VisualBox("LowerChassis", truck, new(0f, -.12f, 0f), TruckSize);
            VisualBox("FrontBumper", truck, new(0f, .05f, 2.80f), new(2.18f, .28f, .10f));
            VisualBox("RearBumper", truck, new(0f, .05f, -2.80f), new(2.18f, .28f, .10f));
            VisualBox("Hood", truck, new(0f, .42f, 1.85f), new(2.08f, .72f, 2f));
            VisualBox("Bed", truck, new(0f, .30f, -1.48f), new(2.12f, .68f, 1.65f));
            Transform cab = Group("CabFrame", truck);
            VisualBox("Roof", cab, new(0f, 1.82f, .18f), new(2.04f, .18f, 1.72f));
            VisualBox("LeftLowerDoor", cab, new(-.98f, .48f, .18f), new(.16f, .72f, 1.72f));
            VisualBox("RightLowerDoor", cab, new(.98f, .48f, .18f), new(.16f, .72f, 1.72f));
            foreach (float x in new[] { -.98f, .98f })
            {
                VisualBox(x < 0f ? "LeftAPillar" : "RightAPillar", cab, new(x, 1.25f, 1.00f), new(.14f, 1.10f, .14f));
                VisualBox(x < 0f ? "LeftBPillar" : "RightBPillar", cab, new(x, 1.25f, .12f), new(.12f, 1.10f, .12f));
                VisualBox(x < 0f ? "LeftRearPillar" : "RightRearPillar", cab, new(x, 1.25f, -.68f), new(.14f, 1.10f, .14f));
            }
            VisualBox("RearCabLowerWall", cab, new(0f, .73f, -.68f), new(1.82f, .28f, .14f));
            VisualBox("RearCabUpperRail", cab, new(0f, 1.72f, -.68f), new(1.82f, .16f, .14f));
        }

        private static void VisualBox(string name, Transform parent, Vector3 position, Vector3 scale) =>
            SetMaterial(Primitive(name, PrimitiveType.Cube, parent, position, scale, true), truckMaterial);

        private static void BuildTrailer(Transform parent, Rigidbody truckBody, bool connectTrailer)
        {
            GameObject trailer = Group("Trailer", parent).gameObject;
            // Connected mode makes both local hitch anchors coincide exactly in world space.
            // Diagnostic mode keeps the trailer dynamic but moves it clear of the truck.
            if (connectTrailer)
            {
                Vector3 hitchWorld = truckBody.transform.TransformPoint(new Vector3(0f, 0f, TruckHitchZ));
                trailer.transform.rotation = truckBody.rotation;
                trailer.transform.position = hitchWorld - trailer.transform.rotation * new Vector3(0f, 0f, TrailerHitchZ);
            }
            else trailer.transform.localPosition = new Vector3(0f, 1.1f, -11f);
            Rigidbody body = trailer.AddComponent<Rigidbody>();
            body.mass = TrailerMass;
            // Combined 600 kg frame + 1050 kg secured boat load. Lowering the boat permits
            // a corresponding, geometry-derived COM reduction without placing it below the frame.
            body.centerOfMass = new Vector3(0f, .28f, -.35f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            BoxCollider trailerCollider = trailer.AddComponent<BoxCollider>();
            trailerCollider.size = TrailerColliderSize;
            trailerCollider.center = TrailerColliderCenter;
            BuildOpenTrailerFrame(trailer.transform);

            Transform wheelGroup = Group("Wheels", trailer.transform);
            Vector3[] positions =
            {
                new(-1.08f, -.58f, TrailerFrontAxleZ), new(1.08f, -.58f, TrailerFrontAxleZ),
                new(-1.08f, -.58f, TrailerRearAxleZ), new(1.08f, -.58f, TrailerRearAxleZ)
            };
            string[] wheelNames = { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
            var bindings = new PassiveTrailerAxle.WheelBinding[4];
            for (int i = 0; i < bindings.Length; i++)
            {
                Transform point = Group(wheelNames[i] + "WheelPoint", wheelGroup);
                point.localPosition = positions[i];
                Transform visual = TrailerWheelVisual(wheelNames[i] + "WheelVisual", trailer.transform, positions[i]);
                bindings[i] = new PassiveTrailerAxle.WheelBinding
                    { Label = wheelNames[i], Point = point, Visual = visual };
            }
            Transform hitch = Group("HitchPoint", trailer.transform); hitch.localPosition = new(0f, 0f, TrailerHitchZ);
            BuildBoatLoad(trailer.transform);
            if (connectTrailer)
            {
                ConfigurableJoint joint = trailer.AddComponent<ConfigurableJoint>();
                joint.connectedBody = truckBody; joint.autoConfigureConnectedAnchor = false;
                joint.anchor = hitch.localPosition; joint.connectedAnchor = new(0f, 0f, TruckHitchZ);
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                // Joint axes map to trailer pitch (X), articulation/yaw (Y), and roll (Z).
                joint.angularYMotion = ConfigurableJointMotion.Limited;
                joint.angularZMotion = ConfigurableJointMotion.Limited;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
                joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
                joint.angularYLimit = new SoftJointLimit { limit = TrailerYawLimit };
                joint.angularZLimit = new SoftJointLimit { limit = 8f };
                joint.enableCollision = false;
            }
            trailer.AddComponent<PassiveTrailerAxle>().Configure(body, bindings, ~0,
                TrailerWheelRadius, .30f, TrailerSuspensionSpring, TrailerSuspensionDamper,
                TrailerLateralGrip, TrailerRollingResistance, TrailerWheelWidth);
        }

        private static void BuildOpenTrailerFrame(Transform trailer)
        {
            // Visible structure is collider-free; the root BoxCollider remains the deliberately
            // simple physical frame envelope. Rails nest around the keel instead of forming a slab.
            FrameBox("LeftFrameRail", trailer, new(-.72f, -.04f, -.40f), new(.16f, .18f, 5.60f), trailerMaterial);
            FrameBox("RightFrameRail", trailer, new(.72f, -.04f, -.40f), new(.16f, .18f, 5.60f), trailerMaterial);
            float[] crossmemberZ = { -3.22f, -2.15f, -1.05f, .05f, 1.18f };
            for (int i = 0; i < crossmemberZ.Length; i++)
                FrameBox(i == 0 ? "RearCrossmember" : $"Crossmember_{i}", trailer,
                    new(0f, .02f, crossmemberZ[i]), new(1.62f, .14f, .14f), trailerMaterial);

            VisualBeam("LeftAFrameTongue", trailer, new(-.72f, -.04f, 2.38f),
                new(0f, .02f, TrailerHitchZ), .16f, hitchMaterial);
            VisualBeam("RightAFrameTongue", trailer, new(.72f, -.04f, 2.38f),
                new(0f, .02f, TrailerHitchZ), .16f, hitchMaterial);
            FrameBox("FrontAxleMount", trailer, new(0f, -.17f, TrailerFrontAxleZ),
                new(1.82f, .12f, .16f), trailerMaterial);
            FrameBox("RearAxleMount", trailer, new(0f, -.17f, TrailerRearAxleZ),
                new(1.82f, .12f, .16f), trailerMaterial);

            GameObject leftBunk = FrameBox("LeftBunk", trailer, new(-.52f, .145f, -.62f),
                new(.18f, .12f, 4.70f), wheelMaterial);
            GameObject rightBunk = FrameBox("RightBunk", trailer, new(.52f, .145f, -.62f),
                new(.18f, .12f, 4.70f), wheelMaterial);
            leftBunk.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            rightBunk.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);

            FrameBox("LeftFender", trailer, new(-1.12f, -.15f, TrailerAxleZ),
                new(.16f, .16f, 2.05f), trailerMaterial);
            FrameBox("RightFender", trailer, new(1.12f, -.15f, TrailerAxleZ),
                new(.16f, .16f, 2.05f), trailerMaterial);
        }

        private static GameObject FrameBox(string name, Transform parent, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject box = Primitive(name, PrimitiveType.Cube, parent, position, scale, true);
            SetMaterial(box, material);
            return box;
        }

        private static void VisualBeam(string name, Transform parent, Vector3 start, Vector3 end,
            float width, Material material)
        {
            Vector3 direction = end - start;
            GameObject beam = FrameBox(name, parent, (start + end) * .5f,
                new(width, width, direction.magnitude), material);
            beam.transform.localRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private static void BuildBoatLoad(Transform trailer)
        {
            float estimatedContactAngle = EstimateBoatTruckContactAngle();
            if (estimatedContactAngle - TrailerYawLimit < BoatJackknifeSafetyMargin)
                throw new InvalidOperationException($"Boat/truck jackknife clearance is insufficient: estimated contact " +
                    $"at {estimatedContactAngle:F0} degrees, limit {TrailerYawLimit:F0} degrees.");
            // Secured load: visual children share the trailer Rigidbody and deliberately have no
            // Rigidbody or Collider. Their mass is represented by the trailer Rigidbody above.
            Transform boat = Group("BoatLoad", trailer);
            boat.localPosition = new Vector3(0f, BoatHullBottomY, BoatCenterZ);

            Transform hull = Group("Hull", boat);
            MeshFilter hullFilter = hull.gameObject.AddComponent<MeshFilter>();
            MeshRenderer hullRenderer = hull.gameObject.AddComponent<MeshRenderer>();
            hullFilter.sharedMesh = EnsureBoatSectionMesh("PrototypeBoatHull",
                new[] { -BoatLength * .5f, -1.0f, 1.65f },
                new[] { BoatBeam * .46f, BoatBeam * .5f, BoatBeam * .39f },
                new[] { .88f, 1.0f, 1.06f });
            hullRenderer.sharedMaterial = boatHullMaterial;
            SetMaterial(Primitive("LowerHullAccent", PrimitiveType.Cube, hull,
                new(0f, .10f, -.45f), new(.30f, .16f, 5.25f), true), boatAccentMaterial);

            Transform bow = Group("Bow", boat);
            MeshFilter bowFilter = bow.gameObject.AddComponent<MeshFilter>();
            MeshRenderer bowRenderer = bow.gameObject.AddComponent<MeshRenderer>();
            bowFilter.sharedMesh = EnsureBoatSectionMesh("PrototypeBoatBow",
                new[] { 1.65f, BoatLength * .5f }, new[] { BoatBeam * .39f, .06f },
                new[] { 1.06f, 1.28f });
            bowRenderer.sharedMaterial = boatHullMaterial;

            SetMaterial(Primitive("Transom", PrimitiveType.Cube, boat,
                new(0f, .50f, -BoatLength * .5f), new(BoatBeam * .92f, .88f, .10f), true), boatHullMaterial);
            SetMaterial(Primitive("Console", PrimitiveType.Cube, boat,
                new(.28f, 1.02f, .25f), new(.72f, .42f, .65f), true), boatHullMaterial);
            GameObject windshield = Primitive("Windshield", PrimitiveType.Cube, boat,
                new(.28f, 1.34f, .43f), new(.78f, .35f, .06f), true);
            windshield.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            SetMaterial(windshield, boatWindshieldMaterial);

            Transform top = Group("BoatTopReference", boat); top.localPosition = new Vector3(.28f, 1.52f, .43f);
            Transform stern = Group("BoatSternReference", boat); stern.localPosition = new Vector3(0f, .78f, -BoatLength * .5f);
            GameObject post = Primitive("WinchPost", PrimitiveType.Cube, trailer,
                new(0f, .70f, 2.62f), new(.16f, 1.32f, .16f), true);
            post.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            SetMaterial(post, hitchMaterial);
            SetMaterial(Primitive("BowStop", PrimitiveType.Cube, trailer,
                new(0f, 1.20f, 2.76f), new(.42f, .22f, .18f), true), wheelMaterial);
        }

        private static float EstimateBoatTruckContactAngle()
        {
            // Sample both tapered bow rails about the hitch against the pickup's rear visual envelope.
            float bowStartZ = BoatCenterZ + 1.65f;
            float bowEndZ = BoatCenterZ + BoatLength * .5f;
            float bowStartHalfWidth = BoatBeam * .39f;
            const float bowTipHalfWidth = .06f;
            float truckRearZ = -TruckOverallLength * .5f;
            for (int angle = 1; angle < 90; angle++)
            {
                Quaternion articulation = Quaternion.Euler(0f, angle, 0f);
                for (int sample = 0; sample <= 100; sample++)
                {
                    float t = sample / 100f;
                    float z = Mathf.Lerp(bowStartZ, bowEndZ, t);
                    float halfWidth = Mathf.Lerp(bowStartHalfWidth, bowTipHalfWidth, t);
                    foreach (float x in new[] { -halfWidth, halfWidth })
                    {
                        Vector3 relativeToHitch = new Vector3(x, 0f, z - TrailerHitchZ);
                        Vector3 truckLocal = new Vector3(0f, 0f, TruckHitchZ) + articulation * relativeToHitch;
                        if (Mathf.Abs(truckLocal.x) <= TruckHalfWidth && truckLocal.z >= truckRearZ)
                            return angle;
                    }
                }
            }
            return 90f;
        }

        private static Mesh EnsureBoatSectionMesh(string name, float[] z, float[] halfWidths, float[] heights)
        {
            const string folder = "Assets/_LaunchRamp/Meshes";
            Directory.CreateDirectory(folder);
            string path = $"{folder}/{name}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            var vertices = new Vector3[z.Length * 5];
            for (int i = 0; i < z.Length; i++)
            {
                float half = halfWidths[i]; float height = heights[i]; int start = i * 5;
                vertices[start] = new Vector3(-half, height, z[i]);
                vertices[start + 1] = new Vector3(-half * .88f, height * .34f, z[i]);
                vertices[start + 2] = new Vector3(0f, 0f, z[i]);
                vertices[start + 3] = new Vector3(half * .88f, height * .34f, z[i]);
                vertices[start + 4] = new Vector3(half, height, z[i]);
            }
            var triangles = new System.Collections.Generic.List<int>((z.Length - 1) * 30);
            for (int section = 0; section < z.Length - 1; section++)
            for (int edge = 0; edge < 5; edge++)
            {
                int nextEdge = (edge + 1) % 5;
                int a = section * 5 + edge; int b = section * 5 + nextEdge;
                int c = (section + 1) * 5 + edge; int d = (section + 1) * 5 + nextEdge;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.triangles = triangles.ToArray();
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void ValidateBodyClearance(BoxCollider truckCollider, BoxCollider trailerCollider)
        {
            if (truckCollider == null || trailerCollider == null)
                throw new InvalidOperationException("Truck or trailer body BoxCollider is missing.");

            Physics.SyncTransforms();
            Vector3 truckCenter = truckCollider.transform.TransformPoint(truckCollider.center);
            Vector3 trailerCenter = trailerCollider.transform.TransformPoint(trailerCollider.center);
            float truckHalfZ = truckCollider.size.z * Mathf.Abs(truckCollider.transform.lossyScale.z) * .5f;
            float trailerHalfZ = trailerCollider.size.z * Mathf.Abs(trailerCollider.transform.lossyScale.z) * .5f;
            float truckMinZ = truckCenter.z - truckHalfZ;
            float truckMaxZ = truckCenter.z + truckHalfZ;
            float trailerMinZ = trailerCenter.z - trailerHalfZ;
            float trailerMaxZ = trailerCenter.z + trailerHalfZ;
            float longitudinalGap = Mathf.Max(trailerMinZ - truckMaxZ, truckMinZ - trailerMaxZ);
            if (longitudinalGap < MinimumBodyClearance)
                throw new InvalidOperationException($"Truck/trailer solid body clearance is {longitudinalGap:F3} m; " +
                    $"at least {MinimumBodyClearance:F3} m is required. Truck Z=[{truckMinZ:F3}, " +
                    $"{truckMaxZ:F3}], Trailer Z=[{trailerMinZ:F3}, {trailerMaxZ:F3}].");
        }

        private static WheelCollider TruckWheel(GameObject target) =>
            ConfigureWheel(target, TruckSuspensionSpring, TruckSuspensionDamper);

        private static WheelCollider ConfigureWheel(GameObject target, float springStrength, float damperStrength)
        {
            target.transform.localRotation = Quaternion.identity;
            target.transform.localScale = Vector3.one;
            WheelCollider wheel = target.AddComponent<WheelCollider>();
            wheel.radius = WheelRadius; wheel.suspensionDistance = SuspensionDistance; wheel.mass = 35f;
            JointSpring spring = wheel.suspensionSpring;
            spring.spring = springStrength; spring.damper = damperStrength; spring.targetPosition = .5f;
            wheel.suspensionSpring = spring;
            return wheel;
        }

        private static Collider FindGroundCollider(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == GroundName) return root.GetComponent<Collider>();
            return null;
        }

        private static Transform WheelVisual(string name, Transform parent, Vector3 position)
        {
            GameObject visual = Primitive(name, PrimitiveType.Cylinder, parent, position,
                new(WheelRadius * 2f, WheelWidth * .5f, WheelRadius * 2f), true);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            SetMaterial(visual, wheelMaterial);
            return visual.transform;
        }

        private static Transform TrailerWheelVisual(string name, Transform parent, Vector3 position)
        {
            GameObject visual = Primitive(name, PrimitiveType.Cylinder, parent, position,
                new(TrailerWheelRadius * 2f, TrailerWheelWidth * .5f, TrailerWheelRadius * 2f), true);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            SetMaterial(visual, wheelMaterial);
            GameObject hub = Primitive("Hub", PrimitiveType.Cylinder, visual.transform, Vector3.zero,
                new(.52f, 1.04f, .52f), true);
            SetMaterial(hub, lineMaterial);
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

        private static void EnsurePrototypeMaterials()
        {
            Directory.CreateDirectory(PrototypeMaterialFolder);
            truckMaterial = EnsureColorMaterial("Prototype_Truck_Blue", new Color(.06f, .24f, .72f), .3f);
            trailerMaterial = EnsureColorMaterial("Prototype_Trailer_Orange", new Color(.95f, .31f, .04f), .25f);
            wheelMaterial = EnsureColorMaterial("Prototype_Wheel_Dark", new Color(.035f, .04f, .05f), .18f);
            groundMaterial = EnsureColorMaterial("Prototype_Ground_Gray", new Color(.32f, .34f, .36f), .12f);
            lineMaterial = EnsureColorMaterial("Prototype_Line_White", new Color(.92f, .92f, .9f), .2f);
            targetMaterial = EnsureColorMaterial("Prototype_Target_Green", new Color(.05f, .62f, .2f), .2f);
            hazardMaterial = EnsureColorMaterial("Prototype_Hazard_Red", new Color(.82f, .035f, .025f), .22f);
            hitchMaterial = EnsureColorMaterial("Prototype_Hitch_Yellow", new Color(.95f, .68f, .03f), .28f);
            mirrorHousingMaterial = EnsureColorMaterial("Prototype_MirrorHousing_Black", new Color(.012f, .012f, .015f), .35f);
            asphaltMaterial = EnsureColorMaterial("Prototype_Asphalt_Dark", new Color(.12f, .13f, .14f), .08f);
            concreteMaterial = EnsureColorMaterial("Prototype_Ramp_Concrete", new Color(.48f, .50f, .50f), .12f);
            terrainMaterial = EnsureColorMaterial("Prototype_Terrain_BrownGreen", new Color(.26f, .32f, .16f), .05f);
            dockMaterial = EnsureColorMaterial("Prototype_Dock_Brown", new Color(.34f, .20f, .10f), .18f);
            waterMaterial = EnsureTransparentMaterial("Prototype_Water_BlueGreen", new Color(.03f, .42f, .48f, .62f));
            boatHullMaterial = EnsureColorMaterial("Prototype_Boat_OffWhite", new Color(.86f, .87f, .82f), .32f);
            boatAccentMaterial = EnsureColorMaterial("Prototype_Boat_Accent_Navy", new Color(.025f, .08f, .18f), .28f);
            boatWindshieldMaterial = EnsureTransparentMaterial("Prototype_Boat_Windshield",
                new Color(.035f, .10f, .15f, .55f));
        }

        private static Material EnsureTransparentMaterial(string name, Color color)
        {
            Material material = EnsureColorMaterial(name, color, .45f);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureColorMaterial(string name, Color color, float smoothness)
        {
            string path = $"{PrototypeMaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader was not available.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.mainTexture = null;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetMaterial(GameObject target, Material material)
        {
            Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
            if (renderer != null && material != null) renderer.sharedMaterial = material;
        }

        private static int EnsureLayer(string layerName)
        {
            int existing = LayerMask.NameToLayer(layerName);
            if (existing >= 0) return existing;
            UnityEngine.Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            SerializedObject tagManager = new(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");
            for (int i = 8; i < 32; i++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
            throw new InvalidOperationException($"No free user layer is available for '{layerName}'.");
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
