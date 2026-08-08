using TMPro;
using LaunchRamp.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LaunchRamp.Camera
{
    /// <summary>Switches between the truck-mounted view and the fixed course overview.</summary>
    public sealed class PrototypeCameraSwitcher : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera driverCamera;
        [SerializeField] private UnityEngine.Camera diagnosticCamera;
        [SerializeField] private UnityEngine.Camera exteriorCamera;
        [SerializeField] private UnityEngine.Camera freeCamera;
        [SerializeField] private PrototypeFreeCameraController freeCameraController;
        [SerializeField] private Transform diagnosticTarget;
        [SerializeField] private Transform exteriorTarget;
        [SerializeField, Range(10f, 100f)] private float orbitDistance = 60f;
        [SerializeField] private float orbitSensitivity = .15f;
        [SerializeField] private float zoomSensitivity = .02f;
        [SerializeField] private float driverLookSensitivity = .12f;
        [SerializeField] private bool returnDriverViewToForward;
        [SerializeField] private float driverReturnSpeed = 45f;
        [SerializeField] private float driverYawLimit = 110f;
        [SerializeField] private float quickLookYaw = 105f;
        [SerializeField] private float quickLookSmoothTime = .22f;
        [SerializeField] private Vector3 exteriorOffset = new(0f, 6f, -10f);

        private InputAction _switchCamera;
        private InputAction _look;
        private InputAction _zoom;
        private InputAction _orbitButton;
        private InputAction _exteriorToggle;
        private InputAction _freeCameraToggle;
        private InputAction _quickLookLeft;
        private InputAction _quickLookRight;
        private bool _usingDiagnosticCamera;
        private bool _usingExteriorCamera;
        private bool _previousDiagnosticState;
        private bool _usingFreeCamera;
        private bool _storedDiagnosticState;
        private bool _storedExteriorState;
        private VehicleInputReader _vehicleInput;
        private float _orbitYaw;
        private float _orbitPitch = 25f;
        private float _driverYaw;
        private float _driverPitch;
        private Quaternion _driverBaseRotation;
        private bool _quickLookActive;
        private bool _quickLookReturning;
        private float _quickLookReturnYaw;
        private float _quickLookYawVelocity;
        private string _lastCameraAction = "None";

        public string ActiveCameraMode => _usingFreeCamera ? "Free" : _usingExteriorCamera ? "Exterior" :
            _usingDiagnosticCamera ? "Diagnostic" : "Driver";
        public bool FreeCameraActive => _usingFreeCamera;
        public bool DriverCameraActive => driverCamera != null && driverCamera.enabled;
        public bool ExteriorCameraActive => exteriorCamera != null && exteriorCamera.enabled;
        public bool DiagnosticCameraActive => diagnosticCamera != null && diagnosticCamera.enabled;
        public bool RightMouseLookHeld => _orbitButton?.IsPressed() == true;
        public bool ShoulderLookLeftHeld => !_usingFreeCamera && _quickLookLeft?.IsPressed() == true;
        public bool ShoulderLookRightHeld => !_usingFreeCamera && _quickLookRight?.IsPressed() == true;
        public string LastCameraAction => _lastCameraAction;

        public void Configure(UnityEngine.Camera driver, UnityEngine.Camera diagnostic,
            UnityEngine.Camera exterior, UnityEngine.Camera free, PrototypeFreeCameraController freeController,
            Transform diagnosticLookTarget, Transform followTarget)
        {
            driverCamera = driver;
            diagnosticCamera = diagnostic;
            exteriorCamera = exterior;
            freeCamera = free;
            freeCameraController = freeController;
            diagnosticTarget = diagnosticLookTarget;
            exteriorTarget = followTarget;
            _vehicleInput = followTarget != null ? followTarget.GetComponent<VehicleInputReader>() : null;
            InitializeCameraAngles();
            ApplyCameraState();
        }

        private void Awake()
        {
            _switchCamera = new InputAction("Switch Camera", InputActionType.Button, "<Keyboard>/c");
            _switchCamera.AddBinding("<Gamepad>/rightShoulder");
            _look = new InputAction("Camera Look", InputActionType.Value, "<Mouse>/delta");
            _zoom = new InputAction("Diagnostic Zoom", InputActionType.Value, "<Mouse>/scroll");
            _orbitButton = new InputAction("Camera Look Button", InputActionType.Button, "<Mouse>/rightButton");
            _exteriorToggle = new InputAction("Exterior Camera", InputActionType.Button, "<Keyboard>/v");
            _freeCameraToggle = new InputAction("Free Camera", InputActionType.Button, "<Keyboard>/f2");
            _quickLookLeft = new InputAction("Quick Look Left", InputActionType.Button, "<Keyboard>/q");
            _quickLookRight = new InputAction("Quick Look Right", InputActionType.Button, "<Keyboard>/e");
            _vehicleInput = exteriorTarget != null ? exteriorTarget.GetComponent<VehicleInputReader>() : null;
            InitializeCameraAngles();
            ApplyCameraState();
        }

        private void OnEnable()
        {
            _switchCamera?.Enable();
            _look?.Enable();
            _zoom?.Enable();
            _orbitButton?.Enable();
            _exteriorToggle?.Enable();
            _quickLookLeft?.Enable();
            _quickLookRight?.Enable();
            _freeCameraToggle?.Enable();
            if (_switchCamera != null) _switchCamera.performed += OnSwitchCamera;
            if (_exteriorToggle != null) _exteriorToggle.performed += OnExteriorToggle;
            if (_freeCameraToggle != null) _freeCameraToggle.performed += OnFreeCameraToggle;
        }

        private void OnDisable()
        {
            if (_switchCamera != null) _switchCamera.performed -= OnSwitchCamera;
            if (_exteriorToggle != null) _exteriorToggle.performed -= OnExteriorToggle;
            if (_freeCameraToggle != null) _freeCameraToggle.performed -= OnFreeCameraToggle;
            _switchCamera?.Disable();
            _look?.Disable();
            _zoom?.Disable();
            _orbitButton?.Disable();
            _exteriorToggle?.Disable();
            _quickLookLeft?.Disable();
            _quickLookRight?.Disable();
            _freeCameraToggle?.Disable();
        }

        private void OnDestroy()
        {
            _switchCamera?.Dispose();
            _look?.Dispose();
            _zoom?.Dispose();
            _orbitButton?.Dispose();
            _exteriorToggle?.Dispose();
            _quickLookLeft?.Dispose();
            _quickLookRight?.Dispose();
            _freeCameraToggle?.Dispose();
        }

        private void Update()
        {
            if (_usingFreeCamera) return;
            if (_quickLookLeft?.WasPressedThisFrame() == true) _lastCameraAction = "Driver shoulder left (Q)";
            if (_quickLookRight?.WasPressedThisFrame() == true) _lastCameraAction = "Driver shoulder right (E)";
            if (_orbitButton?.WasPressedThisFrame() == true) _lastCameraAction = "Right mouse look";
            Vector2 look = _look?.ReadValue<Vector2>() ?? Vector2.zero;
            if (_usingExteriorCamera)
            {
                UpdateExteriorCamera();
            }
            else if (_usingDiagnosticCamera)
            {
                Vector2 scroll = _zoom?.ReadValue<Vector2>() ?? Vector2.zero;
                orbitDistance = Mathf.Clamp(orbitDistance - scroll.y * zoomSensitivity, 10f, 100f);
                if (_orbitButton?.IsPressed() == true)
                {
                    _orbitYaw += look.x * orbitSensitivity;
                    _orbitPitch = Mathf.Clamp(_orbitPitch - look.y * orbitSensitivity, 10f, 80f);
                }
                UpdateDiagnosticCamera();
            }
            else UpdateDriverView(look);
        }

        private void UpdateDriverView(Vector2 look)
        {
            if (driverCamera == null) return;
            bool leftHeld = _quickLookLeft?.IsPressed() == true;
            bool rightHeld = _quickLookRight?.IsPressed() == true;
            bool quickHeld = leftHeld || rightHeld;
            if (quickHeld)
            {
                if (!_quickLookActive)
                {
                    _quickLookReturnYaw = _driverYaw;
                    _quickLookActive = true;
                    _quickLookReturning = false;
                }
                float target = leftHeld && !rightHeld ? -quickLookYaw : quickLookYaw;
                _driverYaw = Mathf.SmoothDampAngle(_driverYaw, target, ref _quickLookYawVelocity,
                    quickLookSmoothTime, Mathf.Infinity, Time.deltaTime);
            }
            else if ((_quickLookActive || _quickLookReturning) && _orbitButton?.IsPressed() != true)
            {
                _quickLookActive = false;
                _quickLookReturning = true;
                _driverYaw = Mathf.SmoothDampAngle(_driverYaw, _quickLookReturnYaw, ref _quickLookYawVelocity,
                    quickLookSmoothTime, Mathf.Infinity, Time.deltaTime);
                if (Mathf.Abs(Mathf.DeltaAngle(_driverYaw, _quickLookReturnYaw)) < .15f)
                {
                    _driverYaw = _quickLookReturnYaw;
                    _quickLookReturning = false;
                }
            }
            else if (_orbitButton?.IsPressed() == true)
            {
                _quickLookActive = _quickLookReturning = false;
                _quickLookYawVelocity = 0f;
                _driverYaw = Mathf.Clamp(_driverYaw + look.x * driverLookSensitivity, -driverYawLimit, driverYawLimit);
                _driverPitch = Mathf.Clamp(_driverPitch - look.y * driverLookSensitivity, -35f, 45f);
            }
            else if (returnDriverViewToForward)
            {
                _driverYaw = Mathf.MoveTowards(_driverYaw, 0f, driverReturnSpeed * Time.deltaTime);
                _driverPitch = Mathf.MoveTowards(_driverPitch, 0f, driverReturnSpeed * Time.deltaTime);
            }
            driverCamera.transform.localRotation = _driverBaseRotation * Quaternion.Euler(_driverPitch, _driverYaw, 0f);
        }

        private void OnSwitchCamera(InputAction.CallbackContext context)
        {
            if (_usingFreeCamera) return;
            _lastCameraAction = "C: driver/diagnostic";
            _usingExteriorCamera = false;
            _usingDiagnosticCamera = !_usingDiagnosticCamera;
            ApplyCameraState();
        }

        private void OnExteriorToggle(InputAction.CallbackContext context)
        {
            if (_usingFreeCamera) return;
            _lastCameraAction = "V: exterior";
            if (!_usingExteriorCamera) _previousDiagnosticState = _usingDiagnosticCamera;
            _usingExteriorCamera = !_usingExteriorCamera;
            if (!_usingExteriorCamera) _usingDiagnosticCamera = _previousDiagnosticState;
            ApplyCameraState();
        }

        private void OnFreeCameraToggle(InputAction.CallbackContext context)
        {
            if (!Application.isEditor && !Debug.isDebugBuild) return;
            _lastCameraAction = _usingFreeCamera ? "F2: leave free camera" : "F2: enter free camera";
            if (!_usingFreeCamera)
            {
                _storedDiagnosticState = _usingDiagnosticCamera;
                _storedExteriorState = _usingExteriorCamera;
                UnityEngine.Camera source = _usingExteriorCamera ? exteriorCamera :
                    _usingDiagnosticCamera ? diagnosticCamera : driverCamera;
                if (freeCamera != null && source != null)
                {
                    if (source == driverCamera && exteriorTarget != null)
                    {
                        Vector3 point = exteriorTarget.position + exteriorTarget.up;
                        freeCamera.transform.position = point - exteriorTarget.forward * 13f + Vector3.up * 7f;
                        freeCamera.transform.rotation = Quaternion.LookRotation(point - freeCamera.transform.position, Vector3.up);
                    }
                    else freeCamera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                }
                _usingFreeCamera = true;
                if (_vehicleInput != null) _vehicleInput.enabled = false;
                freeCameraController?.SetInspectionActive(true);
            }
            else
            {
                _usingFreeCamera = false;
                _usingDiagnosticCamera = _storedDiagnosticState;
                _usingExteriorCamera = _storedExteriorState;
                freeCameraController?.SetInspectionActive(false);
                if (_vehicleInput != null) _vehicleInput.enabled = true;
            }
            ApplyCameraState();
        }

        private void ApplyCameraState()
        {
            SetCameraActive(driverCamera, !_usingFreeCamera && !_usingExteriorCamera && !_usingDiagnosticCamera);
            SetCameraActive(diagnosticCamera, !_usingFreeCamera && !_usingExteriorCamera && _usingDiagnosticCamera);
            SetCameraActive(exteriorCamera, !_usingFreeCamera && _usingExteriorCamera);
            SetCameraActive(freeCamera, _usingFreeCamera);
        }

        private void UpdateExteriorCamera()
        {
            if (exteriorCamera == null || exteriorTarget == null) return;
            Vector3 desiredPosition = exteriorTarget.TransformPoint(exteriorOffset);
            exteriorCamera.transform.position = Vector3.Lerp(exteriorCamera.transform.position,
                desiredPosition, 1f - Mathf.Exp(-6f * Time.deltaTime));
            Vector3 lookPoint = exteriorTarget.position + exteriorTarget.up * 1f;
            exteriorCamera.transform.rotation = Quaternion.LookRotation(lookPoint - exteriorCamera.transform.position,
                Vector3.up);
        }

        private void InitializeCameraAngles()
        {
            if (driverCamera != null) _driverBaseRotation = driverCamera.transform.localRotation;
            if (diagnosticCamera == null || diagnosticTarget == null) return;
            Vector3 offset = diagnosticCamera.transform.position - diagnosticTarget.position;
            orbitDistance = Mathf.Clamp(offset.magnitude, 10f, 100f);
            _orbitYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            _orbitPitch = Mathf.Asin(Mathf.Clamp(offset.y / orbitDistance, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private void UpdateDiagnosticCamera()
        {
            if (diagnosticCamera == null || diagnosticTarget == null) return;
            float yaw = _orbitYaw * Mathf.Deg2Rad;
            float pitch = _orbitPitch * Mathf.Deg2Rad;
            Vector3 offset = new(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));
            diagnosticCamera.transform.position = diagnosticTarget.position + offset * orbitDistance;
            diagnosticCamera.transform.rotation = Quaternion.LookRotation(diagnosticTarget.position - diagnosticCamera.transform.position);
        }

        private static void SetCameraActive(UnityEngine.Camera target, bool active)
        {
            if (target == null) return;
            target.enabled = active;
            AudioListener listener = target.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = active;
        }
    }

    /// <summary>Development-only unscaled-time fly/pan/orbit camera.</summary>
    public sealed class PrototypeFreeCameraController : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Camera controlledCamera;
        [SerializeField] private Transform truckTarget;
        [SerializeField] private Transform trailerTarget;
        [SerializeField] private float baseSpeed = 8f;
        [SerializeField] private float minimumSpeed = .5f;
        [SerializeField] private float maximumSpeed = 60f;
        [SerializeField] private float lookSensitivity = .12f;
        [SerializeField] private float panSensitivity = .0025f;
        private InputAction _move, _vertical, _look, _scroll, _rightMouse, _middleMouse;
        private InputAction _fast, _precision, _home, _end, _focus, _clearFocus, _orbitModifier;
        private bool _inspectionActive;
        private bool _hasFocus;
        private Vector3 _focusPoint;
        private float _focusDistance;
        private float _yaw;
        private float _pitch;

        public float MovementSpeed => baseSpeed;
        public bool InspectionActive => _inspectionActive;
        public Vector2 MoveVector { get; private set; }
        public Vector2 LookDelta { get; private set; }
        public bool RightMouseHeld => _rightMouse?.IsPressed() == true;

        public void Configure(UnityEngine.Camera camera, Transform truck, Transform trailer)
        {
            controlledCamera = camera; truckTarget = truck; trailerTarget = trailer;
        }

        public void SetInspectionActive(bool active)
        {
            _inspectionActive = active;
            if (active) CaptureAngles();
            else { MoveVector = Vector2.zero; LookDelta = Vector2.zero; }
        }

        private void Awake()
        {
            if (controlledCamera == null) controlledCamera = GetComponent<UnityEngine.Camera>();
            _move = new InputAction("Free Camera Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _vertical = new InputAction("Free Camera Vertical", InputActionType.Value);
            _vertical.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
            _look = new InputAction("Free Camera Mouse", InputActionType.Value, "<Mouse>/delta");
            _scroll = new InputAction("Free Camera Speed", InputActionType.Value, "<Mouse>/scroll");
            _rightMouse = new InputAction("Free Camera Look Button", InputActionType.Button, "<Mouse>/rightButton");
            _middleMouse = new InputAction("Free Camera Pan Button", InputActionType.Button, "<Mouse>/middleButton");
            _fast = ButtonWithBindings("Free Camera Fast", "<Keyboard>/leftShift", "<Keyboard>/rightShift");
            _precision = ButtonWithBindings("Free Camera Precision", "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl");
            _orbitModifier = ButtonWithBindings("Free Camera Orbit Modifier", "<Keyboard>/leftAlt", "<Keyboard>/rightAlt");
            _home = new InputAction("Focus Truck", InputActionType.Button, "<Keyboard>/home");
            _end = new InputAction("Focus Trailer", InputActionType.Button, "<Keyboard>/end");
            _focus = new InputAction("Set Object Focus", InputActionType.Button, "<Keyboard>/g");
            _clearFocus = new InputAction("Clear Object Focus", InputActionType.Button, "<Keyboard>/escape");
            CaptureAngles();
        }

        private void OnEnable() { foreach (InputAction action in Actions()) action?.Enable(); }
        private void OnDisable() { foreach (InputAction action in Actions()) action?.Disable(); }
        private void OnDestroy() { foreach (InputAction action in Actions()) action?.Dispose(); }

        private void Update()
        {
            if (!_inspectionActive || controlledCamera == null) return;
            if (_home.WasPressedThisFrame()) FrameTarget(truckTarget, 12f);
            if (_end.WasPressedThisFrame()) FrameTarget(trailerTarget, 12f);
            if (_focus.WasPressedThisFrame()) TogglePointFocus();
            if (_clearFocus.WasPressedThisFrame()) _hasFocus = false;

            Vector2 mouse = _look.ReadValue<Vector2>();
            LookDelta = mouse;
            float scroll = _scroll.ReadValue<Vector2>().y;
            if (_hasFocus && Mathf.Abs(scroll) > .01f)
            {
                _focusDistance = Mathf.Clamp(_focusDistance * Mathf.Exp(-scroll * .001f), .5f, 100f);
                transform.position = _focusPoint - transform.forward * _focusDistance;
            }
            else if (Mathf.Abs(scroll) > .01f)
                baseSpeed = Mathf.Clamp(baseSpeed * Mathf.Exp(scroll * .001f), minimumSpeed, maximumSpeed);

            if (_rightMouse.IsPressed())
            {
                bool orbiting = _hasFocus && _orbitModifier.IsPressed();
                if (_hasFocus && !orbiting) _hasFocus = false;
                _yaw += mouse.x * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch - mouse.y * lookSensitivity, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
                if (orbiting)
                {
                    transform.position = _focusPoint - transform.forward * _focusDistance;
                    transform.rotation = Quaternion.LookRotation(_focusPoint - transform.position, Vector3.up);
                    CaptureAngles();
                }
            }
            else if (_middleMouse.IsPressed())
            {
                float scale = panSensitivity * Mathf.Max(1f, baseSpeed);
                transform.position += (-transform.right * mouse.x - transform.up * mouse.y) * scale;
                if (_hasFocus) _focusPoint += (-transform.right * mouse.x - transform.up * mouse.y) * scale;
            }

            Vector2 move = _move.ReadValue<Vector2>();
            MoveVector = move;
            float vertical = _vertical.ReadValue<float>();
            float multiplier = _fast.IsPressed() ? 3f : _precision.IsPressed() ? .25f : 1f;
            Vector3 velocity = transform.forward * move.y + transform.right * move.x + transform.up * vertical;
            if (velocity.sqrMagnitude > 1f) velocity.Normalize();
            transform.position += velocity * (baseSpeed * multiplier * Time.unscaledDeltaTime);
            if (velocity.sqrMagnitude > 0f) _hasFocus = false;
        }

        private void TogglePointFocus()
        {
            if (_hasFocus) { _hasFocus = false; return; }
            Ray ray = controlledCamera.ViewportPointToRay(new Vector3(.5f, .5f));
            if (!Physics.Raycast(ray, out RaycastHit hit, 500f, ~0, QueryTriggerInteraction.Ignore)) return;
            _hasFocus = true; _focusPoint = hit.point;
            _focusDistance = Mathf.Max(.5f, Vector3.Distance(transform.position, _focusPoint));
        }

        private void FrameTarget(Transform target, float distance)
        {
            if (target == null) return;
            Vector3 point = target.position + target.up * 1f;
            transform.position = point - target.forward * distance + Vector3.up * (distance * .45f);
            transform.rotation = Quaternion.LookRotation(point - transform.position, Vector3.up);
            _hasFocus = true; _focusPoint = point; _focusDistance = Vector3.Distance(transform.position, point);
            CaptureAngles();
        }

        private void CaptureAngles()
        {
            Vector3 euler = transform.eulerAngles;
            _yaw = euler.y; _pitch = Mathf.DeltaAngle(0f, euler.x);
        }

        private void OnGUI()
        {
            if (!_inspectionActive || (!Application.isEditor && !Debug.isDebugBuild)) return;
            GUI.Box(new Rect(16f, Screen.height - 190f, 310f, 174f),
                $"FREE CAMERA  {baseSpeed:F1} m/s\n" +
                "F2 Toggle   WASD Move   Q/E Down/Up\n" +
                "RMB Look   MMB Pan   Wheel Speed\n" +
                "Shift Fast   Ctrl Precision\n" +
                "Home Truck   End Trailer\n" +
                "G Set/Clear object focus\nAlt+RMB Orbit   Esc Clear focus");
        }

        private static InputAction ButtonWithBindings(string name, string first, string second)
        {
            var action = new InputAction(name, InputActionType.Button); action.AddBinding(first); action.AddBinding(second); return action;
        }

        private InputAction[] Actions() => new[] { _move, _vertical, _look, _scroll, _rightMouse, _middleMouse,
            _fast, _precision, _home, _end, _focus, _clearFocus, _orbitModifier };
    }

    /// <summary>Development-only mirror viewport and aim visualization, toggled with F4.</summary>
    public sealed class PrototypeMirrorDebug : MonoBehaviour
    {
        [SerializeField] private GameObject overlay;
        [SerializeField] private UnityEngine.Camera leftMirrorCamera;
        [SerializeField] private UnityEngine.Camera rightMirrorCamera;
        [SerializeField] private Transform leftMirrorSurface;
        [SerializeField] private Transform rightMirrorSurface;
        [SerializeField] private Transform driverEye;
        [SerializeField] private Transform truck;
        [SerializeField] private Transform trailer;
        [SerializeField] private GameObject calibrationMarkers;
        [SerializeField] private Vector3 leftAimTarget;
        [SerializeField] private Vector3 rightAimTarget;
        [SerializeField] private float truckWidth;
        [SerializeField] private float trailerWidth;
        [SerializeField] private float outboardExtension;
        [SerializeField] private float truckLength;
        [SerializeField] private float truckWheelbase;
        [SerializeField] private float trailerLength;
        [SerializeField] private float hitchToAxleDistance;
        [SerializeField] private TMP_Text details;
        private InputAction _toggle;
        private bool _visible;
        private bool _validated;

        public void Configure(GameObject overlayRoot, UnityEngine.Camera left, UnityEngine.Camera right,
            Transform leftSurface, Transform rightSurface, Transform configuredDriverEye,
            Transform configuredTruck, Transform configuredTrailer, GameObject markerRoot,
            Vector3 configuredLeftAimTarget, Vector3 configuredRightAimTarget,
            float configuredTruckWidth, float configuredTrailerWidth, float configuredOutboardExtension,
            float configuredTruckLength, float configuredTruckWheelbase, float configuredTrailerLength,
            float configuredHitchToAxleDistance, TMP_Text label)
        {
            overlay = overlayRoot; leftMirrorCamera = left; rightMirrorCamera = right;
            leftMirrorSurface = leftSurface; rightMirrorSurface = rightSurface;
            driverEye = configuredDriverEye; truck = configuredTruck; trailer = configuredTrailer;
            calibrationMarkers = markerRoot; leftAimTarget = configuredLeftAimTarget; rightAimTarget = configuredRightAimTarget;
            truckWidth = configuredTruckWidth; trailerWidth = configuredTrailerWidth;
            outboardExtension = configuredOutboardExtension; truckLength = configuredTruckLength;
            truckWheelbase = configuredTruckWheelbase; trailerLength = configuredTrailerLength;
            hitchToAxleDistance = configuredHitchToAxleDistance; details = label;
            if (overlay != null) overlay.SetActive(false);
            if (calibrationMarkers != null) calibrationMarkers.SetActive(false);
        }

        private void Awake()
        {
            _toggle = new InputAction("Toggle Mirror Debug", InputActionType.Button, "<Keyboard>/f4");
            if (overlay != null) overlay.SetActive(false);
            if (calibrationMarkers != null) calibrationMarkers.SetActive(false);
        }

        private void OnEnable()
        {
            _toggle?.Enable();
            if (_toggle != null) _toggle.performed += OnToggle;
        }

        private void OnDisable()
        {
            if (_toggle != null) _toggle.performed -= OnToggle;
            _toggle?.Disable();
        }

        private void OnDestroy() => _toggle?.Dispose();

        private void Update()
        {
            if (!_validated && Time.frameCount > 2)
            {
                _validated = true;
                ValidateMirrorCamera("LEFT", leftMirrorCamera);
                ValidateMirrorCamera("RIGHT", rightMirrorCamera);
            }
            if (!_visible) return;
            DrawAim(leftMirrorCamera, Color.cyan);
            DrawAim(rightMirrorCamera, Color.magenta);
            DrawDriverSightLine(leftMirrorSurface, Color.green);
            DrawDriverSightLine(rightMirrorSurface, Color.yellow);
            if (details != null && leftMirrorCamera != null && rightMirrorCamera != null)
                details.text = $"MIRROR TUNING | target truck strip: 10-20% (verify panels)\n" +
                    $"Widths truck/trailer {truckWidth:F2}/{trailerWidth:F2} m | outboard {outboardExtension:F2} m | yaw {RelativeYaw():F1} deg\n" +
                    $"Lengths truck/trailer {truckLength:F2}/{trailerLength:F2} m | wheelbase {truckWheelbase:F2} m | hitch-axle {hitchToAxleDistance:F2} m\n" +
                    $"Left local/world {Format(leftMirrorCamera.transform.localPosition)} / {Format(leftMirrorCamera.transform.position)} " +
                    $"aim {Format(leftAimTarget)} rot {Format(leftMirrorCamera.transform.localEulerAngles)} FOV {leftMirrorCamera.fieldOfView:F0}\n" +
                    $"Right local/world {Format(rightMirrorCamera.transform.localPosition)} / {Format(rightMirrorCamera.transform.position)} " +
                    $"aim {Format(rightAimTarget)} rot {Format(rightMirrorCamera.transform.localEulerAngles)} FOV {rightMirrorCamera.fieldOfView:F0}";
        }

        private void OnToggle(InputAction.CallbackContext context)
        {
            _visible = !_visible && (Debug.isDebugBuild || Application.isEditor);
            if (overlay != null) overlay.SetActive(_visible);
            if (calibrationMarkers != null) calibrationMarkers.SetActive(_visible);
        }

        private static void DrawAim(UnityEngine.Camera camera, Color color)
        {
            if (camera != null) Debug.DrawRay(camera.transform.position, camera.transform.forward * 12f, color);
        }

        private void DrawDriverSightLine(Transform surface, Color color)
        {
            if (driverEye != null && surface != null)
                Debug.DrawRay(driverEye.position, surface.position - driverEye.position, color);
        }

        private static string Format(Vector3 value) => $"({value.x:F2}, {value.y:F2}, {value.z:F2})";

        private float RelativeYaw()
        {
            if (truck == null || trailer == null) return 0f;
            Vector3 angles = (Quaternion.Inverse(truck.rotation) * trailer.rotation).eulerAngles;
            return Mathf.DeltaAngle(0f, angles.y);
        }

        private static void ValidateMirrorCamera(string side, UnityEngine.Camera camera)
        {
            if (camera == null)
            {
                Debug.LogError($"[Launch Ramp] {side} mirror camera is missing.");
                return;
            }
            if (!camera.enabled) Debug.LogError($"[Launch Ramp] {side} mirror camera is disabled.", camera);
            if (camera.targetTexture == null)
                Debug.LogError($"[Launch Ramp] {side} mirror camera has no target RenderTexture.", camera);
            else if (!camera.targetTexture.IsCreated())
                Debug.LogError($"[Launch Ramp] {side} mirror RenderTexture is not active.", camera.targetTexture);
            if ((camera.cullingMask & 1) == 0)
                Debug.LogError($"[Launch Ramp] {side} mirror camera excludes the Default layer used by the rig/course.", camera);
            if (camera.nearClipPlane >= camera.farClipPlane)
                Debug.LogError($"[Launch Ramp] {side} mirror camera has invalid clipping planes.", camera);
            if (camera.transform.parent != null && Vector3.Dot(camera.transform.forward, -camera.transform.parent.forward) < .7f)
                Debug.LogError($"[Launch Ramp] {side} mirror camera is not aimed rearward.", camera);
        }
    }
}
