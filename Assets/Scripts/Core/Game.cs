using System.Collections;
using System.Linq;
using Core.Player;
using MapMagic.Core;
using Menus.Main_Menu;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;

namespace Core {

    public enum SessionType {
        Singleplayer,
        Multiplayer
    }
    public class Game : MonoBehaviour {

        public static Game Instance;

        public delegate void RestartLevelAction();
        public delegate void GraphicsSettingsApplyAction();
        public delegate void VRToggledAction(bool enabled);
        public static event RestartLevelAction OnRestart;
        public static event GraphicsSettingsApplyAction OnGraphicsSettingsApplied;
        public static event VRToggledAction OnVRStatus;

        [SerializeField] private InputActionAsset playerBindings;
        [SerializeField] private ScriptableRendererFeature ssao;
        private ShipParameters _shipParameters;
        private Vector3 _hmdPosition;
        private Quaternion _hmdRotation;
        private LevelLoader _levelLoader;
        private SessionType _sessionType = SessionType.Singleplayer;
        private bool _isVREnabled;
        
        // The level data most recently used to load a map
        public LevelData LoadedLevelData => _levelLoader.LoadedLevelData;
        // The level data hydrated with the current player position and track layout
        public LevelData LevelDataAtCurrentPosition => _levelLoader.LevelDataAtCurrentPosition;
        
        public SessionType SessionType => _sessionType;
        public bool IsVREnabled => _isVREnabled;

        public ShipParameters ShipParameters {
            get => _shipParameters == null
                ? ShipPlayer.FindLocal?.Parameters ?? ShipPlayer.ShipParameterDefaults
                : _shipParameters;
            set {
                _shipParameters = value;
                var ship = ShipPlayer.FindLocal;
                if (ship) ship.Parameters = _shipParameters;
            }
        }

        public bool IsTerrainMap =>
            _levelLoader.LoadedLevelData.location == Location.TerrainV1 ||
            _levelLoader.LoadedLevelData.location == Location.TerrainV2;

        public string Seed => _levelLoader.LoadedLevelData.terrainSeed;

        [SerializeField] private Animator crossfade;

        // show certain things if first time hitting the menu
        private bool _menuFirstRun = true;
        public bool menuFirstRun => _menuFirstRun;
        
        private static readonly int fadeToBlack = Animator.StringToHash("FadeToBlack");
        private static readonly int fadeFromBlack = Animator.StringToHash("FadeFromBlack");

        void Awake() {
            // singleton shenanigans
            if (Instance == null) {
                Instance = this;
            }
            else {
                Destroy(gameObject);
            }
        }

        public void Start() {
            // must be a level loader in the scene
            _levelLoader = FindObjectOfType<LevelLoader>();
            
            // if there's a user object when the game starts, enable input (usually in the editor!)
            FindObjectOfType<ShipPlayer>()?.User.EnableGameInput();
            LoadBindings();
            ApplyGraphicsOptions();

            // We use a custom canvas cursor to work in VR and pancake
            Cursor.visible = false;

            // check for command line args
            var args = System.Environment.GetCommandLineArgs();
            if (args.ToList().Contains("-vr") || args.ToList().Contains("-VR")) {
                EnableVR();
            }
            
            // load hmd position from preferences
            _hmdPosition = Preferences.Instance.GetVector3("hmdPosition");
            _hmdRotation = Quaternion.Euler(Preferences.Instance.GetVector3("hmdRotation"));
        }

        private void OnDestroy() {
            DisableVRIfNeeded();
        }

        private void OnApplicationQuit() {
            DisableVRIfNeeded();
        }

        public void LoadBindings() {
            var bindings = Preferences.Instance.GetString("inputBindings");
            if (!string.IsNullOrEmpty(bindings)) {
                playerBindings.LoadBindingOverridesFromJson(bindings);
            }
        }

        public void ApplyGraphicsOptions() {
            if (OnGraphicsSettingsApplied != null) {
                OnGraphicsSettingsApplied();
            }

            var urp = (UniversalRenderPipelineAsset) GraphicsSettings.currentRenderPipeline;
            urp.renderScale = Preferences.Instance.GetFloat("graphics-render-scale");
            var msaa = Preferences.Instance.GetString("graphics-anti-aliasing");
            switch (msaa) {
                case "8x":
                    urp.msaaSampleCount = 8;
                    break;
                case "4x":
                    urp.msaaSampleCount = 4;
                    break;
                case "2x":
                    urp.msaaSampleCount = 2;
                    break;
                case "none":
                case "default":
                    urp.msaaSampleCount = 0;
                    break;
            }

            ssao.SetActive(Preferences.Instance.GetBool("graphics-ssao"));
        }

        public void EnableVR() {
            IEnumerator StartXR() {
                yield return UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.InitializeLoader();
                UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.StartSubsystems();
                _isVREnabled = true;
                NotifyVRStatus();
            }

            StartCoroutine(StartXR());
        }

        public void DisableVRIfNeeded() {
            if (IsVREnabled) {
                UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.StopSubsystems();
                UnityEngine.XR.Management.XRGeneralSettings.Instance.Manager.DeinitializeLoader();
                _isVREnabled = false;
                NotifyVRStatus();
            }
        }

        public void ResetHmdView(XRRig xrRig, Transform targetTransform) {
            
            xrRig.MoveCameraToWorldLocation(targetTransform.position);
            xrRig.MatchRigUpCameraForward(targetTransform.up, targetTransform.forward);
            
            var xrRigTransform = xrRig.transform;
            _hmdPosition = xrRigTransform.localPosition;
            _hmdRotation = xrRigTransform.localRotation;
            
            Preferences.Instance.SetVector3("hmdPosition", _hmdPosition);
            Preferences.Instance.SetVector3("hmdRotation", _hmdRotation.eulerAngles);
            Preferences.Instance.Save();
        }

        public void StartGame(SessionType sessionType, LevelData levelData, bool dynamicPlacementStart = false) {

            /* Split this into single and multiplayer - logic should be mostly the same but we don't
                transition from a lobby and we need to set the queryable SessionType for other logic in-game
                (e.g. no actual pause on pause menu, quick to menu being quit to lobby etc)
            */
            _sessionType = sessionType;

            LockCursor();

            IEnumerator LoadGame() {
                yield return _levelLoader.ShowLoadingScreen();
                
                // Position the active camera to the designated start location so we can be sure to load in anything
                // important at that location as part of the load sequence 
                var loadingRoom = FindObjectOfType<LoadingRoom>();
                if (loadingRoom) {
                    var loadingPlayerCameraTransform = loadingRoom.transform;
                    loadingPlayerCameraTransform.position = new Vector3(
                        levelData.startPosition.x,
                        levelData.startPosition.y, 
                        levelData.startPosition.z
                    );
                }
                
                yield return _levelLoader.StartGame(levelData);

                yield return FdNetworkManager.Instance.WaitForAllPlayersLoaded();
                
                FdNetworkManager.Instance.StartMainGame(levelData);
                
                // wait for local ship client object
                while (!ShipPlayer.FindLocal) {
                    yield return new WaitForEndOfFrame();
                }
                var ship = ShipPlayer.FindLocal;
                
                // Allow the rigid body to initialise before setting new parameters!
                yield return new WaitForEndOfFrame();

                ship.Parameters = ShipParameters;
                var shipPosition = ship.transform.position;
                _levelLoader.LoadedLevelData.startPosition.x = shipPosition.x;
                _levelLoader.LoadedLevelData.startPosition.y = shipPosition.y;
                _levelLoader.LoadedLevelData.startPosition.z = shipPosition.z;

                // set up graphics settings (e.g. camera FoV) + VR status (cameras, radial fog etc)
                ApplyGraphicsOptions();
                NotifyVRStatus();

                yield return _levelLoader.HideLoadingScreen();
                
                // if there's a track, initialise it
                var track = FindObjectOfType<Track>();
                if (track) {
                    track.InitialiseTrack();
                }

                // resume the game
                Time.timeScale = 1;
                FadeFromBlack();
                yield return new WaitForSeconds(0.7f);

                // if there's a track in the game world, start it
                if (track) {
                    yield return track.StartTrackWithCountdown();
                }

                // enable user input
                ship.User.EnableGameInput();
            }

            StartCoroutine(LoadGame());
        }

        public void RestartLevel() {
            StartCoroutine(_levelLoader.RestartLevel(() => {
                if (OnRestart != null) {
                    OnRestart();
                }
            }));
        }

        public void QuitToMenu() {
            if (FindObjectOfType<MainMenu>()) {
                return;
            }
            
            _menuFirstRun = false;
            var mapMagic = FindObjectOfType<MapMagicObject>();
            if (mapMagic) {
                mapMagic.StopGenerate();
                mapMagic.enabled = false;
            }

            var ship = ShipPlayer.FindLocal;
            if (ship) {
                ship.User.DisableGameInput();
            }

            IEnumerator LoadMenu() {
                FadeToBlack();
                yield return new WaitForSeconds(0.5f);
                yield return SceneManager.LoadSceneAsync("Main Menu");
                yield return new WaitForEndOfFrame();
                FdNetworkManager.Instance.StopAll();
                _levelLoader.ResetLoadedLevelData();
                ApplyGraphicsOptions();
                yield return new WaitForEndOfFrame();
                NotifyVRStatus();
                FreeCursor();
            }

            StartCoroutine(LoadMenu());
        }

        public void QuitGame() {
            IEnumerator Quit() {
                FadeToBlack();
                yield return new WaitForSeconds(0.5f);
                Application.Quit();
            }

            StartCoroutine(Quit());
        }

        public void FreeCursor() {
            Cursor.lockState = CursorLockMode.None;
        }

        public void LockCursor() {
            Cursor.lockState = CursorLockMode.Confined;
        }

        public void FadeToBlack() {
            crossfade.SetTrigger(fadeToBlack);
        }

        public void FadeFromBlack() {
            crossfade.SetTrigger(fadeFromBlack);
        }

        private void NotifyVRStatus() {
            if (OnVRStatus != null) {
                OnVRStatus(IsVREnabled);
            }

            // if user has previously applied a HMD position, reapply
            if (IsVREnabled) {
                var xrRig = FindObjectOfType<XRRig>(true);
                if (xrRig) {
                    var xrTransform = xrRig.transform;
                    xrTransform.localRotation = _hmdRotation;
                    xrTransform.localPosition = _hmdPosition;
                }
            }
        }
    }
}