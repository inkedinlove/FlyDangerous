﻿using Bhaptics.Tact.Unity;
using Core.ShipModel.Feedback.interfaces;
using Core.ShipModel.ShipIndicator;
using UnityEngine;

namespace Core.ShipModel.Feedback.bHaptics {
    public class BHapticsShipFeedback : MonoBehaviour, IShipFeedback, IShipInstruments {
        [SerializeField] private FeedbackEngine feedbackEngine;

        [SerializeField] private VestHapticClip collisionImpactVestHapticClip;
        [SerializeField] private VestHapticClip boostDropVestHapticClip;
        [SerializeField] private VestHapticClip boostFireVestHapticClip;
        [SerializeField] private VestHapticClip shipShakeVestHapticClip;
        [SerializeField] private ArmsHapticClip collisionImpactLeftArmHapticClip;
        [SerializeField] private ArmsHapticClip boostDropLeftArmHapticClip;
        [SerializeField] private ArmsHapticClip boostFireLeftArmHapticClip;
        [SerializeField] private ArmsHapticClip shipShakeLeftArmHapticClip;
        [SerializeField] private ArmsHapticClip collisionImpactRightArmHapticClip;
        [SerializeField] private ArmsHapticClip boostDropRightArmHapticClip;
        [SerializeField] private ArmsHapticClip boostFireRightArmHapticClip;
        [SerializeField] private ArmsHapticClip shipShakeRightArmHapticClip;

        // No idea why but `IsPlaying()` always returns false :/
        private float _shakeHapticPlayTime;

        private void OnEnable() {
            feedbackEngine.SubscribeFeedbackObject(this);
        }

        private void OnDisable() {
            feedbackEngine.RemoveFeedbackObject(this);
        }

        public void OnShipFeedbackUpdate(IShipFeedbackData shipFeedbackData) {
            if (shipFeedbackData.BoostDropStartThisFrame) {
                boostDropVestHapticClip.Play(0.3f);
                boostDropLeftArmHapticClip.Play(0.3f, 2);
                boostDropRightArmHapticClip.Play(0.3f, 2);
            }

            if (shipFeedbackData.BoostThrustStartThisFrame) {
                boostFireVestHapticClip.Play();
                boostFireLeftArmHapticClip.Play();
                boostFireRightArmHapticClip.Play();
            }

            if (shipFeedbackData.ShipShake > 0 && _shakeHapticPlayTime > 0.1f) {
                _shakeHapticPlayTime = 0;
                shipShakeVestHapticClip.Play(shipFeedbackData.ShipShake * 5, 0.1f);
                shipShakeLeftArmHapticClip.Play(shipFeedbackData.ShipShake * 5, 0.2f);
                shipShakeRightArmHapticClip.Play(shipFeedbackData.ShipShake * 5, 0.2f);
            }

            _shakeHapticPlayTime += Time.fixedDeltaTime;

            if (shipFeedbackData.CollisionStartedThisFrame) {
                collisionImpactVestHapticClip.Play(shipFeedbackData.CollisionImpactNormalised, 1, shipFeedbackData.CollisionDirection, Vector3.zero,
                    Vector3.forward, 1);
                collisionImpactLeftArmHapticClip.Play(shipFeedbackData.CollisionImpactNormalised);
                collisionImpactRightArmHapticClip.Play(shipFeedbackData.CollisionImpactNormalised);
            }
        }

        public void OnShipIndicatorUpdate(IShipInstrumentData shipInstrumentData) {
        }
    }
}