using System.Collections.Generic;
using System.Linq;
using Audio;
using Core;
using Core.MapData;
using Core.MapData.Serializable;
using Core.Player;
using Gameplay.Game_Modes.Components;
using JetBrains.Annotations;
using NaughtyAttributes;
using UnityEngine;
#if UNITY_EDITOR
using System.Collections;
using Core.Scores;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Gameplay {
    [ExecuteAlways]
    public class Track : MonoBehaviour {
        public delegate void CheckpointHit(Checkpoint checkpoint, float excessTimeToHitSeconds);

        public event CheckpointHit OnCheckpointHit;

        [SerializeField] private Checkpoint checkpointPrefab;
        [SerializeField] private ModifierSpawner modifierPrefab;
        [SerializeField] private BillboardSpawner billboardPrefab;

        [SerializeField] private GameModeCheckpoints checkpointContainer;
        [SerializeField] private GameModeModifiers modifierContainer;
        [SerializeField] private GameModeBillboards billboardContainer;
        [SerializeField] private Transform geometryContainer;

        [HorizontalLine] [SerializeField] private string trackName;

        [Dropdown("GetGameModes")] [OnValueChanged("SetGameMode")] [SerializeField]
        private string gameMode;

        [Dropdown("GetEnvironments")] [OnValueChanged("SetEnvironment")] [SerializeField]
        private string environment;

        [Dropdown("GetMusicTracks")] [OnValueChanged("PlayMusicTrack")] [SerializeField]
        private string musicTrack;

        [SerializeField] [OnValueChanged("UpdateTimesFromAuthor")]
        private float authorTimeTarget;

        [ReadOnly] [UsedImplicitly] [SerializeField]
        private float goldTime;

        [ReadOnly] [UsedImplicitly] [SerializeField]
        private float silverTime;

        [ReadOnly] [UsedImplicitly] [SerializeField]
        private float bronzeTime;

        [HorizontalLine] [SerializeField] private Vector3 startPosition;
        [SerializeField] private Vector3 startRotation;

        private bool _loadingEnvironment;

        [Button("Set start from ship position")]
        [UsedImplicitly]
        private void SetFromShip() {
#if UNITY_EDITOR
            Undo.RecordObject(this, "Set player transform");
#endif
            var player = FdPlayer.FindLocalShipPlayer;
            if (player) {
                startPosition = player.AbsoluteWorldPosition;
                startRotation = player.transform.rotation.eulerAngles;
            }
        }

        public GameModeCheckpoints GameModeCheckpoints => checkpointContainer;

        private void OnDestroy() {
            if (checkpointContainer != null)
                foreach (var checkpoint in checkpointContainer.Checkpoints)
                    checkpoint.OnHit -= HandleOnCheckpointHit;
        }

        public LevelData Serialize(Vector3? overrideStartPosition = null, Quaternion? overrideStartRotation = null) {
            var loadedLevelData = Game.Instance.LoadedLevelData;

            var levelData = new LevelData {
                name = trackName,
                authorTimeTarget = authorTimeTarget,
                gameType = GameType.FromString(gameMode),
                location = loadedLevelData.location,
                musicTrack = MusicTrack.FromString(musicTrack),
                environment = Environment.FromString(environment),
                terrainSeed = string.IsNullOrEmpty(loadedLevelData.terrainSeed) ? null : loadedLevelData.terrainSeed,
                startPosition = SerializableVector3.FromVector3(overrideStartPosition ?? startPosition),
                startRotation = SerializableVector3.FromVector3(overrideStartRotation?.eulerAngles ?? startRotation)
            };

            checkpointContainer.RefreshCheckpoints();
            if (checkpointContainer.Checkpoints.Count > 0)
                levelData.checkpoints = checkpointContainer
                    .Checkpoints
                    .ConvertAll(SerializableCheckpoint.FromCheckpoint);

            billboardContainer.RefreshBillboardSpawners();
            if (billboardContainer.BillboardSpawners.Count > 0)
                levelData.billboards = billboardContainer.BillboardSpawners
                    .ConvertAll(SerializableBillboard.FromBillboardSpawner);

            modifierContainer.RefreshModifierSpawners();
            if (modifierContainer.ModifierSpawners.Count > 0)
                levelData.modifiers = modifierContainer.ModifierSpawners
                    .ConvertAll(SerializableModifier.FromModifierSpawner);

            // TODO: geometry

            return levelData;
        }

        // Build level geometry from json
        public void Deserialize(LevelData levelData) {
            trackName = levelData.name;
            gameMode = levelData.gameType.Name;
            authorTimeTarget = levelData.authorTimeTarget;
            environment = levelData.environment.Name;
            musicTrack = levelData.musicTrack.Name;

            startPosition = levelData.startPosition.ToVector3();
            startPosition = levelData.startRotation.ToVector3();

            if (levelData.checkpoints?.Count > 0)
                levelData.checkpoints.ForEach(c => {
                    var checkpoint = checkpointContainer.AddCheckpoint(c);
                    checkpoint.OnHit += HandleOnCheckpointHit;
                });

            if (levelData.billboards?.Count > 0)
                levelData.billboards.ForEach(b => billboardContainer.AddBillboard(b));

            if (levelData.modifiers?.Count > 0)
                levelData.modifiers.ForEach(m => modifierContainer.AddModifier(m));

            // TODO: geometry
        }

        private void HandleOnCheckpointHit(Checkpoint checkpoint, float excessTimeToHitSeconds) {
            OnCheckpointHit?.Invoke(checkpoint, excessTimeToHitSeconds);
        }

#if UNITY_EDITOR

        #region Editor Hooks

        [UsedImplicitly]
        private List<string> GetGameModes() {
            return GameType.List().Select(b => b.Name).ToList();
        }

        [UsedImplicitly]
        private void SetGameMode() {
            var player = FdPlayer.FindLocalShipPlayer;
            if (player) {
                Game.Instance.GameModeHandler.InitialiseGameMode(player, Serialize(), GameType.FromString(gameMode).GameMode, player.User.InGameUI, this);
                Game.Instance.RestartSession();
            }
        }

        [UsedImplicitly]
        private List<string> GetEnvironments() {
            return Environment.List().Select(b => b.Name).ToList();
        }

        [UsedImplicitly]
        private List<string> GetMusicTracks() {
            return MusicTrack.List().Select(b => b.Name).ToList();
        }

        [UsedImplicitly]
        private void SetEnvironment() {
            if (_loadingEnvironment) return;
            _loadingEnvironment = true;

            var sceneToLoad = Environment.FromString(environment).SceneToLoad;

            IEnumerator SetNewEnvironment(string sceneName) {
                // load new
                if (Application.isPlaying)
                    yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                else
                    EditorSceneManager.OpenScene($"Assets/Scenes/Environments/{sceneName}.unity", OpenSceneMode.Additive);
                SceneManager.SetActiveScene(SceneManager.GetSceneByName(sceneName));


                // unload any other matching environment scenes
                foreach (var environmentType in Environment.List()) {
                    if (environmentType.SceneToLoad == sceneName)
                        continue;

                    for (var i = 0; i < SceneManager.sceneCount; i++) {
                        var loadedScene = SceneManager.GetSceneAt(i);
                        if (loadedScene.name == environmentType.SceneToLoad) SceneManager.UnloadSceneAsync(loadedScene);
                    }
                }

                _loadingEnvironment = false;
            }

            StartCoroutine(SetNewEnvironment(sceneToLoad));
        }

        [UsedImplicitly]
        private void PlayMusicTrack() {
            if (Application.isPlaying)
                MusicManager.Instance.PlayMusic(MusicTrack.FromString(musicTrack), true, true, false);
        }

        [UsedImplicitly]
        private void UpdateTimesFromAuthor() {
            var levelData = Serialize();
            goldTime = Score.GoldTimeTarget(levelData);
            silverTime = Score.SilverTimeTarget(levelData);
            bronzeTime = Score.BronzeTimeTarget(levelData);
        }

        [Button("Copy level to Clipboard")]
        private void CopyToClipboard() {
            GUIUtility.systemCopyBuffer = Serialize().ToJsonString();
        }

        #endregion

        #region Track editing

        [HorizontalLine] [UsedImplicitly] [ReorderableList] [SerializeField]
        private List<Checkpoint> Checkpoints = new();

        [UsedImplicitly] [ReorderableList] [SerializeField]
        private List<ModifierSpawner> Modifiers = new();

        [UsedImplicitly] [ReorderableList] [SerializeField]
        private List<BillboardSpawner> Billboards = new();

        [Button("Create Checkpoint")]
        [UsedImplicitly]
        private void CreateCheckpoint() {
            var checkpoint = Instantiate(checkpointPrefab, checkpointContainer.transform);
            Selection.activeGameObject = checkpoint.gameObject;
            ForceRefresh();
        }

        [Button("Create Checkpoint At Ship Location")]
        [UsedImplicitly]
        private void CreateCheckpointAtShip() {
            var ship = FdPlayer.FindLocalShipPlayer;
            if (ship == null) {
                Debug.LogError("No ship found - are you actually playing?...");
                return;
            }

            var checkpoint = Instantiate(checkpointPrefab, checkpointContainer.transform, true);
            checkpoint.transform.SetPositionAndRotation(ship.Position, ship.transform.rotation);
            Selection.activeGameObject = checkpoint.gameObject;

            ForceRefresh();
        }

        [Button("Create Checkpoint At Last Position")]
        [UsedImplicitly]
        private void CreateCheckpointAtLastPosition() {
            if (checkpointContainer.Checkpoints.Count == 0) {
                Debug.LogError("No checkpoints to get last position from!");
                return;
            }

            var lastCheckpoint = checkpointContainer.Checkpoints.Last();
            var checkpoint = checkpointContainer.AddCheckpoint(SerializableCheckpoint.FromCheckpoint(lastCheckpoint));
            Selection.activeGameObject = checkpoint.gameObject;
            ForceRefresh();
        }

        [Button("Create Modifier")]
        [UsedImplicitly]
        private void CreateModifier() {
            var modifier = Instantiate(modifierPrefab, modifierContainer.transform);
            Selection.activeGameObject = modifier.gameObject;
            ForceRefresh();
        }

        [Button("Create Billboard")]
        [UsedImplicitly]
        private void CreateBillboard() {
            var billboard = Instantiate(billboardPrefab, billboardContainer.transform);
            Selection.activeGameObject = billboard.gameObject;
            ForceRefresh();
        }

        #endregion

        #region Visualisation

        private void RefreshLineRenderer() {
            var curvedLineRenderer = GetComponent<CurvedLineRenderer>();

            if (curvedLineRenderer.enabled)
                // we add the curved line point as a child transform so it can be manually tweaked if needed in the editor without moving the checkpoints
                foreach (var checkpoint in Checkpoints) {
                    var curvedLinePoint = checkpoint.GetComponentInChildren<CurvedLinePoint>();
                    if (curvedLinePoint != null) Destroy(curvedLinePoint.gameObject);

                    var linePoint = new GameObject("Curved Line Point");
                    linePoint.AddComponent<CurvedLinePoint>();
                    linePoint.transform.SetParent(checkpoint.transform, false);
                }
        }

        #endregion

        private void OnValidate() {
            checkpointContainer.RefreshCheckpoints();
            Checkpoints = checkpointContainer.Checkpoints;

            modifierContainer.RefreshModifierSpawners();
            Modifiers = modifierContainer.ModifierSpawners;

            billboardContainer.RefreshBillboardSpawners();
            Billboards = billboardContainer.BillboardSpawners;
        }

        [Button("Toggle line drawing")]
        [UsedImplicitly]
        private void ToggleLineDrawing() {
            var lineRenderer = GetComponent<LineRenderer>();
            var curvedLineRenderer = GetComponent<CurvedLineRenderer>();
            var shouldShow = !lineRenderer.enabled;

            lineRenderer.enabled = shouldShow;
            curvedLineRenderer.enabled = shouldShow;

            RefreshLineRenderer();
        }

        [Button("Force Refresh")]
        [UsedImplicitly]
        private void ForceRefresh() {
            OnValidate();
            RefreshLineRenderer();
        }
#endif
    }
}