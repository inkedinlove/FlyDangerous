using System;
using System.Collections;
using System.Collections.Generic;
using Audio;
using Core.MapData;
using Core.Scores;
using Den.Tools;
using Misc;
using UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;

namespace Menus.Main_Menu.Components {

    enum LevelSelectionMode {
        LevelSelect,
        Summary
    }
    
    public class LevelSelectPanel : MonoBehaviour {
        
        public delegate void OnLevelSelectedAction();
        public event OnLevelSelectedAction OnLevelSelectedEvent;
        
        [SerializeField] private LevelUIElement levelUIElementPrefab;
        [SerializeField] private RectTransform levelPrefabContainer;

        [SerializeField] private Text levelName;
        [SerializeField] private Image levelThumbnail;

        [SerializeField] private Text personalBest;
        [SerializeField] private Text platinumTarget;
        [SerializeField] private Text goldTarget;
        [SerializeField] private Text silverTarget;
        [SerializeField] private Text bronzeTarget;
        [SerializeField] private GameObject platinumMedalContainer;

        [SerializeField] private LayoutElement levelGridLayoutElement;
        [SerializeField] private LayoutElement summaryScreenGridLayoutElement;
        [SerializeField] private FlowLayoutGroup levelFlowLayoutGroup;
        [SerializeField] private AnimationCurve screenTransitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        [SerializeField] private float panelAnimationTimeSeconds = 0.5f;
        private readonly float openPanelPreferredWidthValue = 2000;

        public Level SelectedLevel { get; private set; }
        private Coroutine _panelAnimationHideCoroutine;
        private Coroutine _panelAnimationShowCoroutine;

        public void LoadLevels(List<Level> levels) {
            foreach (var levelUI in levelPrefabContainer.gameObject.GetComponentsInChildren<LevelUIElement>()) Destroy(levelUI.gameObject);
            foreach (var level in levels) {
                var levelButton = Instantiate(levelUIElementPrefab, levelPrefabContainer);
                levelButton.LevelData = level;
                levelButton.gameObject.GetComponent<UIButton>().OnButtonSubmitEvent += OnLevelSelected;
                levelButton.gameObject.GetComponent<UIButton>().OnButtonSelectEvent += OnLevelHighLighted;
                levelButton.gameObject.GetComponent<UIButton>().OnButtonHighlightedEvent += OnLevelHighLighted;
                levelButton.gameObject.GetComponent<UIButton>().OnButtonUnHighlightedEvent += OnLevelUnHighLighted;
            }

            levelFlowLayoutGroup.enabled = true;
            levelGridLayoutElement.preferredWidth = 2000;
            summaryScreenGridLayoutElement.preferredWidth = 0;

            levelGridLayoutElement.gameObject.SetActive(true);
            summaryScreenGridLayoutElement.gameObject.SetActive(false);
        }

        private void OnLevelHighLighted(UIButton uiButton) {
            HighlightSelectedLevel(uiButton.GetComponent<LevelUIElement>().LevelData);
        }        
        
        private void OnLevelUnHighLighted(UIButton uiButton) {
            HighlightSelectedLevel(SelectedLevel);
        }

        private void OnLevelSelected(UIButton uiButton) {
            UIAudioManager.Instance.Play("ui-dialog-open");
            SetSelectedLevel(uiButton.GetComponent<LevelUIElement>().LevelData);
        }

        private void HighlightSelectedLevel(Level level) {
            if (level != null) {
                levelName.text = level.Name;
                levelThumbnail.sprite = level.Thumbnail;

                var score = level.Score;
                var bestTime = score.PersonalBestTotalTime;
                personalBest.text = bestTime > 0 ? TimeExtensions.TimeSecondsToString(bestTime) : "NONE";

                var platinumTargetTime = level.Data.authorTimeTarget;
                var goldTargetTime = Score.GoldTimeTarget(level.Data);
                var silverTargetTime = Score.SilverTimeTarget(level.Data);
                var bronzeTargetTime = Score.BronzeTimeTarget(level.Data);

                platinumTarget.text = TimeExtensions.TimeSecondsToString(platinumTargetTime);
                goldTarget.text = TimeExtensions.TimeSecondsToString(goldTargetTime);
                silverTarget.text = TimeExtensions.TimeSecondsToString(silverTargetTime);
                bronzeTarget.text = TimeExtensions.TimeSecondsToString(bronzeTargetTime);

                // if user hasn't beaten author time, hide it!
                platinumMedalContainer.gameObject.SetActive(score.HasPlayedPreviously && bestTime <= platinumTargetTime);

                // TODO: show a medal icon associated with users' time
            }
        }

        public void DeSelectLevel() {
            // select the previous level if there is one
            if (SelectedLevel != null) {
                levelPrefabContainer.GetComponentsInChildren<LevelUIElement>()
                    .FindMember(levelButton => levelButton.LevelData == SelectedLevel)
                    ?.GetComponent<Button>()
                    ?.Select();
            }
            
            SwitchToLevelSelectScreen();
            SelectedLevel = null;
        }

        private void SetSelectedLevel(Level level) {
            SelectedLevel = level;
            SwitchToSummaryScreen();
        }

        private void SwitchToSummaryScreen() {
            levelFlowLayoutGroup.enabled = false;
            if (_panelAnimationHideCoroutine != null) StopCoroutine(_panelAnimationHideCoroutine);
            if (_panelAnimationShowCoroutine != null) StopCoroutine(_panelAnimationShowCoroutine);
            _panelAnimationHideCoroutine = StartCoroutine(HidePanel(levelGridLayoutElement, () => OnLevelSelectedEvent?.Invoke()));
            _panelAnimationShowCoroutine = StartCoroutine(ShowPanel(summaryScreenGridLayoutElement));
        }

        private void SwitchToLevelSelectScreen() {
            if (_panelAnimationHideCoroutine != null) StopCoroutine(_panelAnimationHideCoroutine);
            if (_panelAnimationShowCoroutine != null) StopCoroutine(_panelAnimationShowCoroutine);
            _panelAnimationHideCoroutine = StartCoroutine(HidePanel(summaryScreenGridLayoutElement));
            _panelAnimationShowCoroutine = StartCoroutine(ShowPanel(levelGridLayoutElement, () => levelFlowLayoutGroup.enabled = true));
        }

        private IEnumerator HidePanel(LayoutElement panel, Action onComplete = null) {
            var frameIncrement = Time.fixedDeltaTime / panelAnimationTimeSeconds;

            // levelFlowLayoutGroup.enabled = false;
            var animationPosition = 0f;
            while (animationPosition <= 1) {
                panel.preferredWidth = MathfExtensions.Remap(0, 1, openPanelPreferredWidthValue, 0, screenTransitionCurve.Evaluate(animationPosition));
                animationPosition += frameIncrement;
                yield return new WaitForFixedUpdate();
            }

            panel.gameObject.SetActive(false);
            onComplete?.Invoke();
        }

        private IEnumerator ShowPanel(LayoutElement panel, Action onComplete = null) {
            var frameIncrement = Time.fixedDeltaTime / panelAnimationTimeSeconds;

            panel.gameObject.SetActive(true);
            var animationPosition = 0f;
            while (animationPosition <= 1) {
                panel.preferredWidth = MathfExtensions.Remap(0, 1, 0, openPanelPreferredWidthValue, screenTransitionCurve.Evaluate(animationPosition));
                animationPosition += frameIncrement;
                yield return new WaitForFixedUpdate();
            }

            onComplete?.Invoke();
        }
    }
}