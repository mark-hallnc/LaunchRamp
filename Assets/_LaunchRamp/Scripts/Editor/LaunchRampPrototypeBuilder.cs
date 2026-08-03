#if UNITY_EDITOR
using System;
using System.IO;
using LaunchRamp.Input;
using LaunchRamp.Camera;
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
        private const string RootName = "VehiclePrototype";
        private const string GroundName = "TestGround";
        private const string CourseName = "DiagnosticCourse";
        private const bool ConnectTrailer = true;
        private const float TruckMass = 2200f, TrailerMass = 1700f;
        private const float WheelRadius = .52f, WheelWidth = .34f, SuspensionDistance = .28f;
        private const float TruckSuspensionSpring = 38000f, TruckSuspensionDamper = 5200f;
        private const float TrailerSuspensionSpring = 40000f, TrailerSuspensionDamper = 6000f;
        private const float TrailerLateralGrip = 6000f, TrailerRollingResistance = 100f;
        private const string PrototypeMaterialFolder = "Assets/_LaunchRamp/Materials/Prototype";
        private const string MirrorSurfaceLayerName = "MirrorSurface";
        private const string LeftMirrorTexturePath = "Assets/_LaunchRamp/Materials/LeftMirrorRenderTexture.renderTexture";
        private const string RightMirrorTexturePath = "Assets/_LaunchRamp/Materials/RightMirrorRenderTexture.renderTexture";
        private const string LeftMirrorMaterialPath = "Assets/_LaunchRamp/Materials/LeftMirrorPrototype.mat";
        private const string RightMirrorMaterialPath = "Assets/_LaunchRamp/Materials/RightMirrorPrototype.mat";
        private static readonly Vector3 LeftMirrorPosition = new(-1.12f, 1.52f, 1.28f);
        private static readonly Vector3 RightMirrorPosition = new(1.12f, 1.52f, 1.28f);
        private static readonly Vector3 LeftMirrorEulerAim = new(3f, 168f, 0f);
        private static readonly Vector3 RightMirrorEulerAim = new(3f, 192f, 0f);
        private const float MirrorWidth = .64f, MirrorHeight = .30f, MirrorThickness = .06f;
        private const float MirrorCameraFieldOfView = 42f;
        private const float MotorTorque = 2100f, BrakeTorque = 3600f, ParkingBrakeTorque = 6500f;
        private const float SteerAngle = 30f, ReverseEngagementSpeed = 1.5f;
        private static readonly Vector3 TruckSize = new(2.2f, 1f, 4.8f);
        private static readonly Vector3 TrailerSize = new(2.35f, .8f, 4f);
        private static readonly Vector3 TruckColliderSize = new(2.1f, .4f, 4.6f);
        private static readonly Vector3 TruckColliderCenter = new(0f, .4f, 0f);
        private static readonly Vector3 TrailerColliderSize = new(2.25f, .4f, 4f);
        private static readonly Vector3 TrailerColliderCenter = new(0f, .42f, -.5f);
        private const float MinimumBodyClearance = .5f;
        private static Material truckMaterial, trailerMaterial, wheelMaterial, groundMaterial;
        private static Material lineMaterial, targetMaterial, hazardMaterial, hitchMaterial, mirrorHousingMaterial;

        [MenuItem("Launch Ramp/Build Vehicle Physics Prototype")]
        public static void Build() => BuildPrototype(ConnectTrailer);

        [MenuItem("Launch Ramp/Build Truck-Only Physics Prototype")]
        public static void BuildTruckOnly() => BuildPrototype(false);

        private static void BuildPrototype(bool connectTrailer)
        {
            try
            {
                Scene scene = OpenTargetScene();
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

        private static void BuildCameras(Scene scene, GameObject root, Transform driverMount)
        {
            // Replace prior player and mirror cameras; the prototype root is also recreated each run.
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                foreach (UnityEngine.Camera camera in sceneRoot.GetComponentsInChildren<UnityEngine.Camera>(true))
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);

            if (driverMount == null) throw new InvalidOperationException("DriverCameraMount was not created.");
            UnityEngine.Camera driver = CreateCamera("Driver Camera", driverMount, Vector3.zero, Quaternion.identity);
            driver.fieldOfView = 65f;

            Transform target = Group("DiagnosticCameraTarget", root.transform);
            target.position = new Vector3(0f, 0f, 32f);
            Transform diagnosticTransform = Group("DiagnosticCamera", root.transform);
            diagnosticTransform.position = new Vector3(18f, 28f, -18f);
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

            (UnityEngine.Camera leftMirror, UnityEngine.Camera rightMirror) = BuildMirrors(root.transform.Find("Truck"));
            BuildMirrorDebug(root, leftMirror, rightMirror,
                leftMirror.targetTexture, rightMirror.targetTexture);
            root.AddComponent<PrototypeCameraSwitcher>().Configure(driver, diagnostic, exterior, target,
                root.transform.Find("Truck"));
        }

        private static (UnityEngine.Camera left, UnityEngine.Camera right) BuildMirrors(Transform truck)
        {
            if (truck == null) throw new InvalidOperationException("Truck was not available for mirror creation.");
            int mirrorLayer = EnsureLayer(MirrorSurfaceLayerName);
            RenderTexture leftTexture = EnsureRenderTexture(LeftMirrorTexturePath);
            RenderTexture rightTexture = EnsureRenderTexture(RightMirrorTexturePath);
            Material leftMaterial = EnsureMirrorMaterial(LeftMirrorMaterialPath, leftTexture);
            Material rightMaterial = EnsureMirrorMaterial(RightMirrorMaterialPath, rightTexture);

            UnityEngine.Camera left = CreateMirrorCamera("LeftMirrorCamera", truck, LeftMirrorPosition,
                LeftMirrorEulerAim, leftTexture, mirrorLayer);
            UnityEngine.Camera right = CreateMirrorCamera("RightMirrorCamera", truck, RightMirrorPosition,
                RightMirrorEulerAim, rightTexture, mirrorLayer);
            CreateMirrorAssembly("LeftMirror", truck, LeftMirrorPosition, leftMaterial, mirrorLayer);
            CreateMirrorAssembly("RightMirror", truck, RightMirrorPosition, rightMaterial, mirrorLayer);
            return (left, right);
        }

        private static UnityEngine.Camera CreateMirrorCamera(string name, Transform parent, Vector3 position,
            Vector3 euler, RenderTexture texture, int mirrorLayer)
        {
            Transform value = Group(name, parent);
            value.localPosition = position;
            value.localRotation = Quaternion.Euler(euler);
            UnityEngine.Camera camera = value.gameObject.AddComponent<UnityEngine.Camera>();
            camera.targetTexture = texture;
            camera.fieldOfView = MirrorCameraFieldOfView;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 120f;
            camera.cullingMask &= ~(1 << mirrorLayer);
            UniversalAdditionalCameraData data = value.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = false;
            return camera;
        }

        private static void CreateMirrorAssembly(string name, Transform parent, Vector3 position,
            Material displayMaterial, int mirrorLayer)
        {
            Transform assembly = Group(name + "Assembly", parent);
            assembly.localPosition = position;
            // The display faces the cab; its source camera independently looks rearward.
            assembly.localRotation = Quaternion.Euler(0f, 180f, 0f);
            GameObject housing = Primitive(name + "Housing", PrimitiveType.Cube, assembly,
                new(0f, 0f, .035f), new(MirrorWidth + .06f, MirrorHeight + .06f, MirrorThickness), true);
            GameObject display = Primitive(name + "Surface", PrimitiveType.Quad, assembly,
                new(0f, 0f, 0f), new(MirrorWidth, MirrorHeight, 1f), true);
            SetMaterial(housing, mirrorHousingMaterial);
            SetMaterial(display, displayMaterial);
            housing.layer = display.layer = mirrorLayer;
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
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Texture");
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = texture;
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
            panelRect.sizeDelta = new Vector2(390f, 235f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, .68f);

            GameObject textObject = new("Telemetry", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panel.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 10f); textRect.offsetMax = new Vector2(-14f, -10f);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.fontSize = 18f; text.color = Color.white; text.alignment = TextAlignmentOptions.TopLeft;

            root.AddComponent<PrototypeHandlingDebugPanel>().Configure(panel, text,
                truck.GetComponent<PrototypeTruckController>(), truck.GetComponent<VehicleInputReader>(),
                truck, trailer, truckHitch, trailerHitch, trailer.GetComponent<PassiveTrailerAxle>());
        }

        private static void BuildMirrorDebug(GameObject root, UnityEngine.Camera left, UnityEngine.Camera right,
            RenderTexture leftTexture, RenderTexture rightTexture)
        {
            GameObject canvasObject = new("MirrorDebugCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            CreateMirrorViewport("LeftMirrorViewport", canvasObject.transform, leftTexture, new Vector2(-550f, -20f));
            CreateMirrorViewport("RightMirrorViewport", canvasObject.transform, rightTexture, new Vector2(-278f, -20f));
            GameObject textObject = new("MirrorAimDetails", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-20f, -160f);
            rect.sizeDelta = new Vector2(530f, 80f);
            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 16f; label.color = Color.white; label.alignment = TextAlignmentOptions.TopRight;
            root.AddComponent<PrototypeMirrorDebug>().Configure(canvasObject, left, right, label);
        }

        private static void CreateMirrorViewport(string name, Transform parent, RenderTexture texture, Vector2 position)
        {
            GameObject value = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            value.transform.SetParent(parent, false);
            RectTransform rect = value.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(256f, 128f);
            value.GetComponent<RawImage>().texture = texture;
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
            SetMaterial(Primitive("Chassis", PrimitiveType.Cube, truck.transform, Vector3.zero, TruckSize, true), truckMaterial);
            SetMaterial(Primitive("Cab", PrimitiveType.Cube, truck.transform, new(0f, 1.05f, 1.15f), new(2.05f, 1.3f, 1.8f), true), truckMaterial);

            Transform wheelGroup = Group("Wheels", truck.transform);
            Vector3[] positions = { new(-1.05f, -.45f, 1.55f), new(1.05f, -.45f, 1.55f),
                new(-1.05f, -.45f, -1.55f), new(1.05f, -.45f, -1.55f) };
            var wheels = new PrototypeTruckController.WheelBinding[4];
            for (int i = 0; i < positions.Length; i++)
            {
                string name = (i < 2 ? "Front" : "Rear") + (positions[i].x < 0 ? "Left" : "Right");
                Transform mount = Group(name + "Collider", wheelGroup); mount.localPosition = positions[i];
                wheels[i] = new PrototypeTruckController.WheelBinding { Collider = TruckWheel(mount.gameObject),
                    Visual = WheelVisual(name + "Visual", truck.transform, positions[i]), Steers = i < 2, Drives = i >= 2 };
            }
            Transform hitch = Group("HitchPoint", truck.transform); hitch.localPosition = new(0f, 0f, -2.65f);
            SetMaterial(Primitive("HitchVisual", PrimitiveType.Sphere, hitch, Vector3.zero, new(.18f, .18f, .18f), true), hitchMaterial);
            Transform camera = Group("DriverCameraMount", truck.transform); camera.localPosition = new(-.52f, 1.55f, 1.1f);
            truck.AddComponent<VehicleInputReader>();
            truck.AddComponent<PrototypeTruckController>().Configure(wheels, MotorTorque, BrakeTorque,
                ParkingBrakeTorque, SteerAngle, ReverseEngagementSpeed);
            return body;
        }

        private static void BuildTrailer(Transform parent, Rigidbody truckBody, bool connectTrailer)
        {
            GameObject trailer = Group("Trailer", parent).gameObject;
            // Connected mode makes both local hitch anchors coincide exactly in world space.
            // Diagnostic mode keeps the trailer dynamic but moves it clear of the truck.
            trailer.transform.localPosition = connectTrailer
                ? new Vector3(0f, 1.1f, -1.8f)
                : new Vector3(0f, 1.1f, -8f);
            Rigidbody body = trailer.AddComponent<Rigidbody>();
            body.mass = TrailerMass; body.centerOfMass = new(0f, -.25f, -.15f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            BoxCollider trailerCollider = trailer.AddComponent<BoxCollider>();
            trailerCollider.size = TrailerColliderSize;
            trailerCollider.center = TrailerColliderCenter;
            SetMaterial(Primitive("TrailerBody", PrimitiveType.Cube, trailer.transform, new(0f, 0f, -.5f), TrailerSize, true), trailerMaterial);

            Transform wheelGroup = Group("Wheels", trailer.transform);
            Vector3[] positions = { new(-1.12f, -.35f, -.75f), new(1.12f, -.35f, -.75f) };
            Transform leftPoint = Group("LeftWheelPoint", wheelGroup); leftPoint.localPosition = positions[0];
            Transform rightPoint = Group("RightWheelPoint", wheelGroup); rightPoint.localPosition = positions[1];
            Transform leftVisual = WheelVisual("LeftWheelVisual", trailer.transform, positions[0]);
            Transform rightVisual = WheelVisual("RightWheelVisual", trailer.transform, positions[1]);
            Transform hitch = Group("HitchPoint", trailer.transform); hitch.localPosition = new(0f, 0f, 2.65f);
            SetMaterial(Primitive("TrailerTongue", PrimitiveType.Cube, trailer.transform, new(0f, .05f, 2.075f),
                new(.35f, .18f, 1.15f), true), hitchMaterial);
            if (connectTrailer)
            {
                ConfigurableJoint joint = trailer.AddComponent<ConfigurableJoint>();
                joint.connectedBody = truckBody; joint.autoConfigureConnectedAnchor = false;
                joint.anchor = hitch.localPosition; joint.connectedAnchor = new(0f, 0f, -2.65f);
                joint.axis = Vector3.right;
                joint.secondaryAxis = Vector3.up;
                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                joint.angularYMotion = ConfigurableJointMotion.Free;
                joint.angularZMotion = ConfigurableJointMotion.Limited;
                joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
                joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
                joint.angularZLimit = new SoftJointLimit { limit = 8f };
                joint.enableCollision = false;
            }
            trailer.AddComponent<PassiveTrailerAxle>().Configure(body, leftPoint, rightPoint,
                leftVisual, rightVisual, ~0, WheelRadius, .30f, TrailerSuspensionSpring,
                TrailerSuspensionDamper, TrailerLateralGrip, TrailerRollingResistance, WheelWidth);
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
