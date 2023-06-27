using System.Linq;
using Audio;
using Core.Player.HeadTracking;
using FdUI;
using GameUI;
using JetBrains.Annotations;
using Misc;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.Users;

namespace Core.Player {
    public class User : MonoBehaviour {
        [SerializeField] private ShipPlayer shipPlayer;
        [SerializeField] private ShipArcadeFlightComputer shipArcadeFlightComputer;
        [SerializeField] private ShipCameraRig shipCameraRig;
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private InputSystemUIInputModule pauseUIInputModule;
        [SerializeField] private InGameUI inGameUI;
        [SerializeField] private HeadTrackingManager headTracking;

        [SerializeField] public bool movementEnabled;
        [SerializeField] public bool restartEnabled = true;
        [SerializeField] public bool pauseMenuEnabled = true;
        [SerializeField] public bool boostButtonForceEnabled;
        private bool _alternateFlightControls;
        private bool _autoRotateDrift;
        private bool _showIndicatorHud = true;
        private Vector2 _cameraMouse;
        private bool _cameraRotateAxisControlsEnabled = true;

        private float _cameraX;
        private float _cameraY;

        private bool _freeCamEnabled;

        private float _lateralH;
        private float _lateralV;
        private bool _limiter;
        private bool _mouseLookActive;
        private Vector2 _mousePositionDelta;
        private Vector2 _mousePositionNormalized;
        private Vector2 _mousePositionNormalizedDelta;
        private Vector2 _mousePositionScreen;
        private Vector2 _mousePreviousRelativeRate;
        private bool _reverse;
        private float _pitch;
        private float _roll;
        private float _targetThrottle;
        private float _targetThrottleIncrement;
        private float _throttle;
        private float _yaw;

        public InGameUI InGameUI => inGameUI;

        [CanBeNull]
        public Transform UserCameraTransform =>
            Game.IsVREnabled
                ? xrOrigin.Camera.transform
                : shipCameraRig.CurrentCameraTransform;

        public ShipCameraRig ShipCameraRig => shipCameraRig;

        public bool BoostButtonHeld { get; private set; }

        public void Awake() {
            DisableGameInput();
            DisableUIInput();
            ResetMouseToCentre();
        }

        public void Update() {
            if (movementEnabled && !shipPlayer.Freeze) {
                var pitch = _pitch;
                var roll = _roll;
                var yaw = _yaw;
                var throttle = _throttle;
                var lateralH = _lateralH;
                var lateralV = _lateralV;

                // handle advanced throttle control
                if (Preferences.Instance.GetString("throttleType") == "forward only") {
                    throttle = throttle.Remap(-1, 1, 0, 1);
                    if (_reverse) throttle *= -1;
                }

                _targetThrottle = Mathf.Clamp(_targetThrottle + _targetThrottleIncrement, -1, 1);
                if (_targetThrottle != 0) throttle = _targetThrottle;

                // handle mouse flight input
                if (
                    !inGameUI.PauseSystem.IsPaused &&
                    !_mouseLookActive &&
                    Preferences.Instance.GetBool("enableMouseFlightControls") &&
                    Preferences.Instance.GetString("controlSchemeType") == "advanced"
                ) {
                    CalculateMouseInput(
                        out var mousePitch, out var mouseRoll, out var mouseYaw, out var mouseLateralH, out var mouseLateralV, out var mouseThrottle);
                    pitch += mousePitch;
                    roll += mouseRoll;
                    yaw += mouseYaw;
                    lateralH += mouseLateralH;
                    lateralV += mouseLateralV;
                    throttle += mouseThrottle;
                }

                // if user has any auto-roll handling set, invoke the arcade flight computer to override the inputs
                if (Preferences.Instance.GetBool("autoShipRoll") ||
                    Preferences.Instance.GetBool("autoShipRotation") ||
                    Preferences.Instance.GetString("controlSchemeType") == "arcade")
                    shipArcadeFlightComputer.UpdateShipFlightInput(ref lateralH, ref lateralV, ref throttle, ref pitch, ref yaw, ref roll, _autoRotateDrift);

                // update the player
                shipPlayer.SetPitch(pitch);
                shipPlayer.SetYaw(yaw);
                shipPlayer.SetRoll(roll);
                shipPlayer.SetLateralH(lateralH);
                shipPlayer.SetLateralV(lateralV);
                shipPlayer.SetThrottle(throttle);
                shipPlayer.VelocityLimiterIsPressed(_limiter);
                shipPlayer.Boost(BoostButtonHeld);
            }

            // handle camera rig
            if (_cameraRotateAxisControlsEnabled) {
                if ((Preferences.Instance.GetString("cameraMode") == "absolute" ||
                     Preferences.Instance.GetString("controlSchemeType") == "arcade") &&
                    !_mouseLookActive)
                    shipCameraRig.SetPosition(new Vector2(_cameraX, _cameraY), CameraPositionUpdate.Absolute);

                else if (Preferences.Instance.GetString("cameraMode") == "relative" || _mouseLookActive)
                    shipCameraRig.SetPosition(new Vector2(_cameraX, _cameraY), CameraPositionUpdate.Relative);
            }
            else {
                shipCameraRig.SetPosition(Vector2.zero, CameraPositionUpdate.Absolute);
            }

            if (_mouseLookActive)
                shipCameraRig.SetPosition(
                    new Vector2(
                        _mousePositionDelta.x / Screen.width * Preferences.Instance.GetFloat("mouseXSensitivity") * 25,
                        _mousePositionDelta.y / Screen.height * Preferences.Instance.GetFloat("mouseYSensitivity") * 25
                    ),
                    CameraPositionUpdate.Relative
                );

            if (boostButtonForceEnabled) shipPlayer.Boost(BoostButtonHeld);

            headTracking.SetShipVelocityVector(shipPlayer.Rigidbody.velocity);
            shipCameraRig.SetHeadTransform(ref headTracking.HeadTransform);
        }

        public void OnEnable() {
            _mouseLookActive = Preferences.Instance.GetString("controlSchemeType") == "advanced" && Preferences.Instance.GetBool("mouseLook");
            Game.OnVRStatus += SetVRStatus;
            InputSystem.onDeviceChange += OnDeviceChange;
            ResetMouseToCentre();
            FdConsole.Instance.Clear();
        }

        public void OnDisable() {
            Game.OnVRStatus -= SetVRStatus;
            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        /**
         * Enable and Disable input modules depending on context (in-game vs UI).
         * This prevents conflicts between the two, especially when rebinding keys...
         */
        public void EnableGameInput() {
            var playerInput = GetComponent<PlayerInput>();
            playerInput.ActivateInput();

            // choose the correct action set depending on advanced control scheme preference
            if (Preferences.Instance.GetString("controlSchemeType") == "advanced") {
                var advancedControlActionMap = playerInput.actions.FindActionMap("Ship");
                playerInput.currentActionMap = advancedControlActionMap ?? playerInput.currentActionMap;
            }
            else {
                var arcadeActionMap = playerInput.actions.FindActionMap("ShipArcade");
                playerInput.currentActionMap = arcadeActionMap ?? playerInput.currentActionMap;
            }

            // ensure that the global action set of the control scheme type is enabled
            playerInput.actions.FindActionMap(Preferences.Instance.GetString("controlSchemeType") == "arcade" ? "GlobalArcade" : "Global").Enable();
            playerInput.actions.FindActionMap(Preferences.Instance.GetString("controlSchemeType") == "advanced" ? "GlobalArcade" : "Global").Disable();

            playerInput.currentActionMap.Enable();

            movementEnabled = true;
            pauseMenuEnabled = true;
            restartEnabled = true;

            FdConsole.Instance.LogMessage("** USER INPUT ENABLED **");
            foreach (var inputDevice in InputSystem.devices) {
                FdConsole.Instance.LogMessage(inputDevice.name + " with path <" + inputDevice.device.path + ">" +
                                              " detected");
                if (!playerInput.devices.Contains(inputDevice)) {
                    FdConsole.Instance.LogMessage(inputDevice.name + " not paired to user! Pairing ...");
                    InputUser.PerformPairingWithDevice(inputDevice, playerInput.user);
                }
            }

            FdConsole.Instance.LogMessage("---");
            foreach (var playerInputDevice in playerInput.devices) FdConsole.Instance.LogMessage(playerInputDevice.name + " paired");

            ResetMouseToCentre();
        }

        public void DisableGameInput() {
            var playerInput = GetComponent<PlayerInput>();

            // enable multiple input action sets
            playerInput.actions.FindActionMap("Ship").Disable();
            playerInput.actions.FindActionMap("ShipArcade").Disable();

            movementEnabled = false;
            restartEnabled = false;

            // clear inputs
            shipPlayer.SetPitch(0);
            shipPlayer.SetRoll(0);
            shipPlayer.SetYaw(0);
            shipPlayer.SetThrottle(0);
            shipPlayer.SetLateralH(0);
            shipPlayer.SetLateralV(0);
            shipPlayer.Boost(false);

            FdConsole.Instance.LogMessage("** USER INPUT DISABLED **");
        }

        public void EnableUIInput() {
            pauseUIInputModule.enabled = true;
            FdConsole.Instance.LogMessage("** UI INPUT ENABLED **");
        }

        public void DisableUIInput() {
            pauseUIInputModule.enabled = false;
            FdConsole.Instance.LogMessage("** UI INPUT DISABLED **");
        }

        public void EnableFreeCamInput() {
            var playerInput = GetComponent<PlayerInput>();
            playerInput.actions.FindActionMap("FreeCam").Enable();
            FdConsole.Instance.LogMessage("** FREE CAM ENABLED **");
        }

        public void DisableFreeCamInput() {
            var playerInput = GetComponent<PlayerInput>();
            playerInput.actions.FindActionMap("FreeCam").Disable();
            FdConsole.Instance.LogMessage("** FREE CAM DISABLED **");
        }

        public void SetVRStatus(bool isVREnabled) {
            if (isVREnabled) {
                Game.Instance.SetFlatScreenCameraControllerActive(false);
                shipCameraRig.gameObject.SetActive(false);
                xrOrigin.gameObject.SetActive(true);
            }
            else {
                Game.Instance.SetFlatScreenCameraControllerActive(true);
                shipCameraRig.gameObject.SetActive(true);
                xrOrigin.gameObject.SetActive(false);
            }
        }

        public void ResetMouseToCentre() {
            var screenCentre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Mouse.current.WarpCursorPosition(screenCentre);
            InputState.Change(Mouse.current.position, screenCentre);
            _mousePositionScreen = screenCentre;

            _mousePositionNormalized = Vector2.zero;
            _mousePositionNormalizedDelta = Vector2.zero;
            _mousePositionDelta = Vector2.zero;
            _mousePreviousRelativeRate = Vector2.zero;

            inGameUI.MouseWidget.ResetToCentre();
        }

        // Event responders for PlayerInput, only valid in-game.
        [UsedImplicitly]
        public void OnShowGameMenu() {
            if (pauseMenuEnabled) inGameUI.OnGameMenuToggle();
        }

        [UsedImplicitly]
        public void OnRestartTrack() {
            if (restartEnabled) Game.Instance.RestartSession();
        }

        [UsedImplicitly]
        public void OnRestartFromLastCheckpoint() {
            if (movementEnabled) Debug.Log("lol no");
        }

        [UsedImplicitly]
        public void OnPitch(InputValue value) {
            if (!_alternateFlightControls) _pitch = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnPitchAlt(InputValue value) {
            if (_alternateFlightControls) _pitch = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnRoll(InputValue value) {
            if (!_alternateFlightControls) _roll = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnRollAlt(InputValue value) {
            if (_alternateFlightControls) _roll = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnYaw(InputValue value) {
            if (!_alternateFlightControls) _yaw = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnYawAlt(InputValue value) {
            if (_alternateFlightControls) _yaw = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnThrottle(InputValue value) {
            _targetThrottle = 0;
            _targetThrottleIncrement = 0;
            if (!_alternateFlightControls) _throttle = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnThrottleAlt(InputValue value) {
            _targetThrottle = 0;
            _targetThrottleIncrement = 0;
            if (_alternateFlightControls) _throttle = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnThrottleIncrease(InputValue value) {
            if (value.isPressed)
                _targetThrottleIncrement = value.Get<float>().Remap(0, 1, 0, 0.005f);
            else
                _targetThrottleIncrement = 0;
        }

        [UsedImplicitly]
        public void OnThrottleDecrease(InputValue value) {
            if (value.isPressed)
                _targetThrottleIncrement = value.Get<float>().Remap(0, 1, 0, -0.005f);
            else
                _targetThrottleIncrement = 0;
        }

        [UsedImplicitly]
        public void OnLateralH(InputValue value) {
            if (!_alternateFlightControls) _lateralH = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnLateralHAlt(InputValue value) {
            if (_alternateFlightControls) _lateralH = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnLateralV(InputValue value) {
            if (!_alternateFlightControls) _lateralV = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnLateralVAlt(InputValue value) {
            if (_alternateFlightControls) _lateralV = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnToggleReverse(InputValue value) {
            UIAudioManager.Instance.Play("ui-nav");
            _reverse = !_reverse;
        }

        [UsedImplicitly]
        public void OnBoost(InputValue value) {
            BoostButtonHeld = value.isPressed;
        }

        [UsedImplicitly]
        public void OnDrift(InputValue value) {
            _autoRotateDrift = value.isPressed;
            shipPlayer.IsAutoRotateDriftEnabled = _autoRotateDrift;
        }

        [UsedImplicitly]
        public void OnVelocityLimiter(InputValue value) {
            _limiter = value.isPressed;
        }

        [UsedImplicitly]
        public void OnAllFlightAssistToggle(InputValue value) {
            if (Preferences.Instance.GetString("flightAssistAllBindType") == "toggle" && !value.isPressed) return;

            // if any flight assist is enabled, deactivate (any on = all off)
            var isEnabled = !(shipPlayer.IsVectorFlightAssistActive | shipPlayer.IsRotationalFlightAssistActive);

            // if user has all flight assists on by default, flip that logic on its head (any off = all on)
            if (Preferences.Instance.GetString("flightAssistDefault") == "all on")
                isEnabled = !(shipPlayer.IsVectorFlightAssistActive & shipPlayer.IsRotationalFlightAssistActive);


            shipPlayer.SetAllFlightAssistEnabled(isEnabled);
        }

        [UsedImplicitly]
        public void OnVectorFlightAssistToggle(InputValue value) {
            if (Preferences.Instance.GetString("flightAssistVectorBindType") == "toggle" && !value.isPressed) return;
            shipPlayer.SetFlightAssistVectorControlEnabled(!shipPlayer.IsVectorFlightAssistActive);
        }

        [UsedImplicitly]
        public void OnRotationalFlightAssistToggle(InputValue value) {
            if (Preferences.Instance.GetString("flightAssistRotationalBindType") == "toggle" && !value.isPressed) return;
            shipPlayer.SetFlightAssistRotationalDampeningEnabled(!shipPlayer.IsRotationalFlightAssistActive);
        }

        [UsedImplicitly]
        public void OnShipLightsToggle(InputValue value) {
            shipPlayer.SetNightVisionEnabled(!shipPlayer.ShipPhysics.IsNightVisionActive);
        }

        [UsedImplicitly]
        public void OnChangeCamera(InputValue value) {
            shipCameraRig.ToggleActiveCamera();
        }

        [UsedImplicitly]
        public void OnRotateCameraH(InputValue value) {
            _cameraX = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnRotateCameraV(InputValue value) {
            _cameraY = value.Get<float>();
        }

        [UsedImplicitly]
        public void OnToggleCameraRotateControls(InputValue value) {
            _cameraRotateAxisControlsEnabled = !_cameraRotateAxisControlsEnabled;
        }

        [UsedImplicitly]
        public void OnAltFlightControlsToggle(InputValue value) {
            var altFlightBindType = Preferences.Instance.GetString("altFlightBindType");
            switch (altFlightBindType) {
                case "toggle" when value.isPressed:
                    _alternateFlightControls = !_alternateFlightControls;
                    UIAudioManager.Instance.Play(_alternateFlightControls ? "ship-alternate-flight-on" : "ship-alternate-flight-off");
                    break;
                case "hold":
                    _alternateFlightControls = value.isPressed;
                    UIAudioManager.Instance.Play(_alternateFlightControls ? "ship-alternate-flight-on" : "ship-alternate-flight-off");
                    break;
            }

            // restore values based on current input 
            // (if we do nothing then last held, unbound input will continue forever and if we reset to 0 then any held, identical input will also be reset!)
            var playerInput = GetComponent<PlayerInput>();
            _pitch = _alternateFlightControls ? playerInput.actions["Pitch Alt"].ReadValue<float>() : playerInput.actions["Pitch"].ReadValue<float>();
            _roll = _alternateFlightControls ? playerInput.actions["Roll Alt"].ReadValue<float>() : playerInput.actions["Roll"].ReadValue<float>();
            _yaw = _alternateFlightControls ? playerInput.actions["Yaw Alt"].ReadValue<float>() : playerInput.actions["Yaw"].ReadValue<float>();
            _throttle = _alternateFlightControls ? playerInput.actions["Throttle Alt"].ReadValue<float>() : playerInput.actions["Throttle"].ReadValue<float>();
            _lateralH = _alternateFlightControls ? playerInput.actions["LateralH Alt"].ReadValue<float>() : playerInput.actions["LateralH"].ReadValue<float>();
            _lateralV = _alternateFlightControls ? playerInput.actions["LateralV Alt"].ReadValue<float>() : playerInput.actions["LateralV"].ReadValue<float>();
        }

        [UsedImplicitly]
        public void OnMouseRawDelta(InputValue value) {
            _mousePositionDelta = value.Get<Vector2>();
            _mousePositionScreen.x += _mousePositionDelta.x;
            _mousePositionScreen.y += _mousePositionDelta.y;
        }

        [UsedImplicitly]
        public void OnMouselook(InputValue value) {
            var mouseLookType = Preferences.Instance.GetString("mouseLookBindType");
            switch (mouseLookType) {
                case "toggle" when value.isPressed:
                    _mouseLookActive = !_mouseLookActive;
                    shipCameraRig.SoftReset();
                    Preferences.Instance.SetBool("mouseLook", _mouseLookActive);
                    break;
                case "hold":
                    _mouseLookActive = value.isPressed;
                    break;
            }
        }

        [UsedImplicitly]
        public void OnToggleCameraDrift(InputValue value) {
            var autoLookBindType = Preferences.Instance.GetString("autoTrackBindType");
            headTracking.IsAutoTrackEnabled = autoLookBindType switch {
                "toggle" when value.isPressed => !headTracking.IsAutoTrackEnabled,
                "hold" => value.isPressed,
                _ => headTracking.IsAutoTrackEnabled
            };
        }

        [UsedImplicitly]
        public void OnToggleIndicatorHUD(InputValue value) {
            if (value.isPressed) {
                UIAudioManager.Instance.Play("ui-nav");

                _showIndicatorHud = !_showIndicatorHud;
                inGameUI.IndicatorSystem.ToggleVisibility(_showIndicatorHud);
                inGameUI.TargettingSystem.UserToggleVisibility(_showIndicatorHud);
            }
        }

        [UsedImplicitly]
        public void OnRecenterMouse(InputValue value) {
            ResetMouseToCentre();
        }

        [UsedImplicitly]
        public void OnResetHMDView(InputValue value) {
            if (xrOrigin) Game.Instance.ResetHmdView(xrOrigin, transform);
        }

        [UsedImplicitly]
        public void OnToggleConsole(InputValue value) {
            if (FdConsole.Instance.Visible)
                FdConsole.Instance.Hide();
            else
                FdConsole.Instance.Show();
        }

        [UsedImplicitly]
        public void OnToggleFreeCam(InputValue value) {
            if (!Game.IsVREnabled) {
                _freeCamEnabled = !_freeCamEnabled;
                if (_freeCamEnabled) {
                    DisableGameInput();
                    EnableFreeCamInput();
                    pauseMenuEnabled = false;
                }
                else {
                    EnableGameInput();
                    DisableFreeCamInput();
                }

                shipCameraRig.SetFreeCameraEnabled(_freeCamEnabled);
            }
        }

        [UsedImplicitly]
        public void OnFreeCamMove(InputValue value) {
            ShipCameraRig.ShipFreeCamera.Move(value.Get<Vector2>());
        }

        [UsedImplicitly]
        public void OnFreeCamLook(InputValue value) {
            ShipCameraRig.ShipFreeCamera.LookAround(value.Get<Vector2>());
        }

        [UsedImplicitly]
        public void OnFreeCamAscend(InputValue value) {
            ShipCameraRig.ShipFreeCamera.Ascend(value.Get<float>());
        }

        [UsedImplicitly]
        public void OnFreeCamToggleMovementLock(InputValue value) {
            Debug.Log("Movement Lock not yet implemented");
        }

        [UsedImplicitly]
        public void OnFreeCamToggleFocusLock(InputValue value) {
            if (value.isPressed) ShipCameraRig.ShipFreeCamera.ToggleAimLock();
        }

        [UsedImplicitly]
        public void OnFreeCamToggleFreeze(InputValue value) {
            if (value.isPressed) Time.timeScale = Time.timeScale != 0 ? 0 : 1;
        }

        [UsedImplicitly]
        public void OnFreeCamFieldOfView(InputValue value) {
            ShipCameraRig.ShipFreeCamera.Zoom(value.Get<float>());
        }

        [UsedImplicitly]
        public void OnFreeCamSetMotionMultiplier(InputValue value) {
            var input = value.Get<float>();

            if (input > 0) input = 1;
            if (input < 0) input = -1;
            if (input != 0) ShipCameraRig.ShipFreeCamera.IncrementMotionMultiplier(input);
        }

        private void CalculateMouseInput(out float pitchMouseInput, out float rollMouseInput, out float yawMouseInput,
            out float lateralHMouseInput, out float lateralVMouseInput, out float throttleMouseInput) {
            float pitch = 0, roll = 0, yaw = 0, throttle = 0, lateralH = 0, lateralV = 0;

            // get prefs
            var mouseXAxisBindPref = Preferences.Instance.GetString("mouseXAxis");
            var mouseYAxisBindPref = Preferences.Instance.GetString("mouseYAxis");

            var sensitivityXPref = Preferences.Instance.GetFloat("mouseXSensitivity");
            var sensitivityYPref = Preferences.Instance.GetFloat("mouseYSensitivity");

            var mouseXInvertPref = Preferences.Instance.GetBool("mouseXInvert");
            var mouseYInvertPref = Preferences.Instance.GetBool("mouseYInvert");

            var mouseIsRelativePref = Preferences.Instance.GetString("mouseInputMode") == "relative" ||
                                      (Preferences.Instance.GetBool("forceRelativeMouseWithFAOff") &&
                                       !shipPlayer.IsRotationalFlightAssistActive);

            var mouseRelativeRatePref = Mathf.Clamp(Preferences.Instance.GetFloat("mouseRelativeRate"), 1, 50f);

            var mouseDeadzonePref = Mathf.Clamp(Preferences.Instance.GetFloat("mouseDeadzone"), 0, 1);
            var mousePowerCurvePref = Mathf.Clamp(Preferences.Instance.GetFloat("mousePowerCurve"), 1, 3);

            // add extra room for power curve
            var normalizedClamp = 1 + mouseDeadzonePref;

            // convert to normalized screen space (center is 0,0)
            var mouseXNormalized = (_mousePositionScreen.x / Screen.width * 2 - 1) * sensitivityXPref;
            var mouseYNormalized = (_mousePositionScreen.y / Screen.height * 2 - 1) * sensitivityYPref;
            _mousePositionNormalizedDelta.x = _mousePositionDelta.x / Screen.width;
            _mousePositionNormalizedDelta.y = _mousePositionDelta.y / Screen.height;

            // clamp to max extents of mouse input
            _mousePositionNormalized.x = Mathf.Clamp(mouseXNormalized, -normalizedClamp, normalizedClamp);
            _mousePositionNormalized.y = Mathf.Clamp(mouseYNormalized, -normalizedClamp, normalizedClamp);

            // write back the clamped values to keep our max mouse position intact
            _mousePositionScreen.x = (_mousePositionNormalized.x / sensitivityXPref + 1) / 2 * Screen.width;
            _mousePositionScreen.y = (_mousePositionNormalized.y / sensitivityYPref + 1) / 2 * Screen.height;

            // calculate continuous input including deadzone and sensitivity
            var continuousMouseX = 0f;
            var continuousMouseY = 0f;
            if (_mousePositionNormalized.x > mouseDeadzonePref)
                continuousMouseX = _mousePositionNormalized.x - mouseDeadzonePref;
            if (_mousePositionNormalized.x < -mouseDeadzonePref)
                continuousMouseX = _mousePositionNormalized.x + mouseDeadzonePref;
            if (_mousePositionNormalized.y > mouseDeadzonePref)
                continuousMouseY = _mousePositionNormalized.y - mouseDeadzonePref;
            if (_mousePositionNormalized.y < -mouseDeadzonePref)
                continuousMouseY = _mousePositionNormalized.y + mouseDeadzonePref;

            // calculate relative input from deltas including sensitivity
            var relativeMouse = new Vector2(
                _mousePreviousRelativeRate.x + _mousePositionNormalizedDelta.x * sensitivityXPref,
                _mousePreviousRelativeRate.y + _mousePositionNormalizedDelta.y * sensitivityYPref
            );

            // power curve (Mathf.Pow does not allow negatives because REASONS so abs and multiply by -1 if the original val is < 0)
            continuousMouseX = (continuousMouseX < 0 ? -1 : 1) *
                               Mathf.Pow(Mathf.Abs(continuousMouseX), mousePowerCurvePref);
            continuousMouseY = (continuousMouseY < 0 ? -1 : 1) *
                               Mathf.Pow(Mathf.Abs(continuousMouseY), mousePowerCurvePref);
            relativeMouse.x = (relativeMouse.x < 0 ? -1 : 1) *
                              Mathf.Pow(Mathf.Abs(relativeMouse.x), mousePowerCurvePref);
            relativeMouse.y = (relativeMouse.y < 0 ? -1 : 1) *
                              Mathf.Pow(Mathf.Abs(relativeMouse.y), mousePowerCurvePref);

            // set the input for a given axis 
            void SetInput(string axis, float amount, bool shouldInvert) {
                var invert = shouldInvert ? -1 : 1;

                switch (axis) {
                    case "pitch":
                        pitch += amount * invert;
                        break;
                    case "roll":
                        roll += amount * invert;
                        break;
                    case "yaw":
                        yaw += amount * invert;
                        break;
                    case "lateral h":
                        lateralH += amount * invert;
                        break;
                    case "lateral v":
                        lateralV += amount * invert;
                        break;
                    case "throttle":
                        throttle += amount * invert;
                        break;
                }
            }

            // send input depending on mouse mode
            SetInput(mouseXAxisBindPref, mouseIsRelativePref ? relativeMouse.x : continuousMouseX, mouseXInvertPref);
            SetInput(mouseYAxisBindPref, mouseIsRelativePref ? relativeMouse.y : continuousMouseY, mouseYInvertPref);

            // update widget graphics
            var widgetPosition = new Vector2(
                mouseIsRelativePref ? relativeMouse.x : continuousMouseX,
                mouseIsRelativePref ? relativeMouse.y : continuousMouseY
            );
            inGameUI.MouseWidget.UpdateWidgetSprites(widgetPosition);

            // return to 0 by mouseRelativeRate
            _mousePreviousRelativeRate = Vector2.MoveTowards(new Vector2(
                    relativeMouse.x,
                    relativeMouse.y),
                Vector2.zero, mouseRelativeRatePref / 500);

            // store relative rate for relative return rate next frame
            _mousePreviousRelativeRate.x = Mathf.Clamp(_mousePreviousRelativeRate.x, -1, 1);
            _mousePreviousRelativeRate.y = Mathf.Clamp(_mousePreviousRelativeRate.y, -1, 1);

            // we're done
            pitchMouseInput = pitch;
            rollMouseInput = roll;
            yawMouseInput = yaw;
            lateralHMouseInput = lateralH;
            lateralVMouseInput = lateralV;
            throttleMouseInput = throttle;
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change) {
            var playerInput = GetComponent<PlayerInput>();
            if (change == InputDeviceChange.Added) InputUser.PerformPairingWithDevice(device, playerInput.user);
            if (change == InputDeviceChange.Removed) playerInput.user.UnpairDevice(device);
        }
    }
}