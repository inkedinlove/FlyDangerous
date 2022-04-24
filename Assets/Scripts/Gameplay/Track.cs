using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Audio;
using Core;
using Core.MapData;
using Core.Player;
using Core.Replays;
using Core.Scores;
using Core.ShipModel;
using Game_UI;
using GameUI.GameModes;
using JetBrains.Annotations;
using Misc;
using UnityEngine;
using UnityEngine.UI;

namespace Gameplay {
    [RequireComponent(typeof(ReplayRecorder))]
    public class Track : MonoBehaviour {
        private static readonly Color goldColor = new(1, 0.98f, 0.4f, 1);
        private static readonly Color silverColor = new(0.6f, 0.6f, 0.6f, 1);
        private static readonly Color bronzeColor = new(1, 0.43f, 0, 1);

        public List<Checkpoint> hitCheckpoints;

        // Set to true by the level loader but may be overridden for testing
        [SerializeField] private bool isActive;

        private bool _complete;
        private IGameModeUI _gameModeUI;

        private Score _previousBestScore;

        private ReplayRecorder _replayRecorder;
        private Replay _replayToRecord;
        [CanBeNull] private Coroutine _splitDeltaFader;
        [CanBeNull] private Coroutine _splitFader;
        private List<float> _splits = new();

        private float _timeSeconds;

        private IGameModeUI GameModeUI {
            get {
                if (_gameModeUI == null) {
                    var ship = FdPlayer.FindLocalShipPlayer;
                    if (ship) {
                        _gameModeUI = ship.User.InGameUI.GameModeUIHandler.ActiveGameModeUI;
                        UpdateTargetTimeElements();
                    }
                }

                return _gameModeUI;
            }
        }

        public List<Checkpoint> Checkpoints {
            get => GetComponentsInChildren<Checkpoint>().ToList();
            set => ReplaceCheckpoints(value);
        }

        public List<ShipGhost> ActiveGhosts { get; set; } = new();

        public bool IsEndCheckpointValid => hitCheckpoints.Count >= Checkpoints.Count - 2; // remove start and end

        private void FixedUpdate() {
            // failing to get user in early stages due to modular loading? 
            if (isActive && !_complete && GameModeUI != null && GameModeUI.Timers.TotalTimeDisplay != null) {
                GameModeUI.Timers.TotalTimeDisplay.TextBox.color = new Color(1f, 1f, 1f, 1f);
                _timeSeconds += Time.fixedDeltaTime;
                GameModeUI.Timers.TotalTimeDisplay.SetTimeSeconds(Math.Abs(_timeSeconds));
            }
        }

        private void OnEnable() {
            _replayRecorder = GetComponent<ReplayRecorder>();
            Game.OnRestart += InitialiseTrack;
        }

        private void OnDisable() {
            Game.OnRestart -= InitialiseTrack;
        }

        public void InitialiseTrack() {
            _previousBestScore = Score.ScoreForLevel(Game.Instance.LoadedLevelData);

            if (Game.Instance.LoadedLevelData.gameType == GameType.TimeTrial) GameModeUI.Timers.ShowTimers();

            var start = Checkpoints.Find(c => c.Type == CheckpointType.Start);
            if (start) {
                var ship = FdPlayer.FindLocalShipPlayer;
                if (ship) ship.transform.position = start.transform.position;
            }
            else if (Checkpoints.Count > 0) {
                Debug.LogWarning("Checkpoints loaded with no start block! Is this intentional?");
            }

            Checkpoints.ForEach(c => { c.Reset(); });

            hitCheckpoints = new List<Checkpoint>();
            if (isActive) {
                ResetTimer();
                StopTimer();
            }
        }

        public void ResetTimer() {
            _complete = false;
            _timeSeconds = 0;
            _splits = new List<float>();

            // reset timer text to 0, hide split timer
            if (GameModeUI != null) {
                GameModeUI.Timers.TotalTimeDisplay.SetTimeSeconds(0);
                GameModeUI.Timers.TotalTimeDisplay.TextBox.color = new Color(1f, 1f, 1f, 1);
                GameModeUI.Timers.SplitTimeDisplay.TextBox.color = new Color(1f, 1f, 1f, 0);
                GameModeUI.Timers.SplitTimeDeltaDisplay.TextBox.color = new Color(1f, 1f, 1f, 0);
            }
        }

        public void StopTimer() {
            isActive = false;
            _complete = false;
        }

        public IEnumerator StartTrackWithCountdown() {
            if (Checkpoints.Count > 0) {
                _timeSeconds = -2.5f;
                isActive = true;
                _complete = false;

                // enable user input but disable actual movement
                var player = FdPlayer.FindLocalShipPlayer;
                if (player != null) {
                    var user = player.User;
                    user.EnableGameInput();
                    user.movementEnabled = false;
                    user.pauseMenuEnabled = false;

                    // Trigger recording and ghost replays
                    _replayRecorder.CancelRecording();
                    _replayRecorder.StartNewRecording(player.ShipPhysics);
                    foreach (var shipGhost in ActiveGhosts) Game.Instance.RemoveGhost(shipGhost);
                    ActiveGhosts = new List<ShipGhost>();
                    foreach (var activeReplay in Game.Instance.ActiveGameReplays)
                        ActiveGhosts.Add(Game.Instance.LoadGhost(activeReplay));

                    // half a second (2.5 second total) before countdown
                    yield return YieldExtensions.WaitForFixedFrames(YieldExtensions.SecondsToFixedFrames(0.5f));

                    // start countdown sounds
                    UIAudioManager.Instance.Play("tt-countdown");

                    // second beep (boost available here)
                    yield return YieldExtensions.WaitForFixedFrames(YieldExtensions.SecondsToFixedFrames(1));
                    user.boostButtonEnabledOverride = true;

                    // GO!
                    yield return YieldExtensions.WaitForFixedFrames(YieldExtensions.SecondsToFixedFrames(1));
                    user.movementEnabled = true;
                    user.pauseMenuEnabled = true;
                }
            }

            yield return new WaitForEndOfFrame();
        }

        public void FinishTimer() {
            _complete = true;
        }

        private IEnumerator FadeTimer(TimeDisplay timeDisplay, Color color) {
            timeDisplay.TextBox.color = color;
            while (timeDisplay.TextBox.color.a > 0.0f) {
                timeDisplay.TextBox.color = new Color(color.r, color.g, color.b,
                    timeDisplay.TextBox.color.a - Time.unscaledDeltaTime / 3);
                yield return null;
            }
        }

        public async void CheckpointHit(Checkpoint checkpoint, AudioSource checkpointHitAudio) {
            if (isActive && GameModeUI.Timers) {
                var hitCheckpoint = hitCheckpoints.Find(c => c == checkpoint);
                if (!hitCheckpoint) {
                    // new checkpoint, record it and split timer
                    hitCheckpoints.Add(checkpoint);
                    checkpointHitAudio.Play();

                    // store split time
                    if (checkpoint.Type != CheckpointType.Start) {
                        _splits.Add(_timeSeconds);
                        if (_splitDeltaFader != null) StopCoroutine(_splitDeltaFader);
                        if (_previousBestScore.HasPlayedPreviously && _previousBestScore.PersonalBestTimeSplits.Count >= _splits.Count) {
                            var index = _splits.Count - 1;
                            var previousBestSplit = _previousBestScore.PersonalBestTimeSplits[index];
                            var deltaSplit = _timeSeconds - previousBestSplit;
                            GameModeUI.Timers.SplitTimeDeltaDisplay.SetTimeSeconds(deltaSplit, true);
                            var color = deltaSplit > 0 ? Color.red : Color.green;
                            GameModeUI.Timers.SplitTimeDeltaDisplay.TextBox.color = color;
                            _splitDeltaFader = StartCoroutine(FadeTimer(GameModeUI.Timers.SplitTimeDeltaDisplay, color));
                        }
                    }

                    // update split display and fade out
                    if (checkpoint.Type == CheckpointType.Check) {
                        GameModeUI.Timers.SplitTimeDisplay.SetTimeSeconds(_timeSeconds);
                        if (_splitFader != null) StopCoroutine(_splitFader);
                        _splitFader = StartCoroutine(FadeTimer(GameModeUI.Timers.SplitTimeDisplay, Color.white));
                    }

                    if (checkpoint.Type == CheckpointType.End) {
                        if (_splitDeltaFader != null) StopCoroutine(_splitDeltaFader);

                        // TODO: Make this more generalised and implement a tinker tier for saving these times
                        // TODO: Prevent saving a score from _any_ changes mid-game :| this will be a pain in the ass
                        if (!FindObjectOfType<Game>().ShipParameters.ToJsonString()
                                .Equals(ShipParameters.Defaults.ToJsonString())) {
                            // you dirty debug cheater!
                            GameModeUI.Timers.TotalTimeDisplay.GetComponent<Text>().color = new Color(1, 1, 0, 1);
                        }

                        else {
                            GameModeUI.Timers.TotalTimeDisplay.GetComponent<Text>().color = new Color(0, 1, 0, 1);

                            var score = Score.NewPersonalBest(_timeSeconds, _splits);
                            GameModeUI.ShowResultsScreen(score, _previousBestScore);

                            // if new run OR better score, save!
                            // TODO: move this to the end screen too
                            if (_previousBestScore.PersonalBestTotalTime == 0 || _timeSeconds < _previousBestScore.PersonalBestTotalTime) {
                                _previousBestScore = score;
                                var scoreData = score.Save(Game.Instance.LoadedLevelData);
                                Score.SaveToDisk(scoreData, Game.Instance.LoadedLevelData);

                                if (_replayRecorder) {
                                    _replayRecorder.StopRecording();
                                    var levelHash = Game.Instance.LoadedLevelData.LevelHash();
                                    var replay = _replayRecorder.Replay;
                                    var replayFileName = replay?.Save(scoreData);
                                    var replayFilepath = Path.Combine(Replay.ReplayDirectory, levelHash, replayFileName ?? string.Empty);

                                    // TODO: yeet this to the result screen
                                    if (FdNetworkManager.Instance.HasLeaderboardServices) {
                                        var flagId = Flag.FromFilename(Preferences.Instance.GetString("playerFlag")).FixedId;
                                        var leaderboard = await FdNetworkManager.Instance.OnlineService!.Leaderboard!.FindOrCreateLeaderboard(levelHash);
                                        var timeMilliseconds = _timeSeconds * 1000;
                                        // TODO: This can ABSOLUTELY fail, handle it in the end screen!
                                        await leaderboard.UploadScore((int)timeMilliseconds, flagId, replayFilepath, replayFileName);
                                        Debug.Log("Leaderboard upload succeeded");
                                    }
                                }
                            }

                            UpdateTargetTimeElements();
                        }

                        FinishTimer();
                    }
                }
            }
        }

        private void ReplaceCheckpoints(List<Checkpoint> checkpoints) {
            foreach (var checkpoint in checkpoints) Destroy(checkpoint.gameObject);

            hitCheckpoints = new List<Checkpoint>();
            InitialiseTrack();
        }

        private void UpdateTargetTimeElements() {
            if (Game.Instance.LoadedLevelData.gameType.Id == GameType.TimeTrial.Id) {
                var levelData = Game.Instance.LoadedLevelData;
                var score = _previousBestScore;

                var targetType = GameModeUI.Timers.TargetTimeTypeDisplay;
                var targetTimer = GameModeUI.Timers.TargetTimeDisplay;
                targetTimer.TextBox.color = Color.white;

                var personalBest = score.PersonalBestTotalTime;
                var goldTargetTime = Score.GoldTimeTarget(levelData);
                var silverTargetTime = Score.SilverTimeTarget(levelData);
                var bronzeTargetTime = Score.BronzeTimeTarget(levelData);

                // not played yet
                if (personalBest == 0) {
                    targetType.text = "TARGET BRONZE";
                    targetType.color = bronzeColor;
                    targetTimer.SetTimeSeconds(bronzeTargetTime);
                    return;
                }

                if (personalBest < goldTargetTime) {
                    targetType.text = "PERSONAL BEST";
                    targetType.color = Color.white;
                    targetTimer.SetTimeSeconds(personalBest);
                }
                else if (personalBest < silverTargetTime) {
                    targetType.text = "TARGET GOLD";
                    targetType.color = goldColor;
                    targetTimer.SetTimeSeconds(goldTargetTime);
                }
                else if (personalBest < bronzeTargetTime) {
                    targetType.text = "TARGET SILVER";
                    targetType.color = silverColor;
                    targetTimer.SetTimeSeconds(silverTargetTime);
                }
                else {
                    targetType.text = "TARGET BRONZE";
                    targetType.color = bronzeColor;
                    targetTimer.SetTimeSeconds(bronzeTargetTime);
                }
            }
        }
    }
}