﻿using UnityEngine;

namespace Core.Ship {
    public class ShipShake {
        private readonly Vector3 _originalPos;
        private float _shakeAmount;

        // How long the object should shake for.
        private float _shakeDuration;
        private float _shakeTimer;

        private readonly Transform _shipTransform;

        // Amplitude of the shake. A larger value shakes the camera harder.
        private float _targetShakeAmount;

        public ShipShake(Transform shipTransform) {
            _shipTransform = shipTransform;
            _originalPos = _shipTransform.localPosition;
        }

        public void Shake(float duration, float amount) {
            _shakeDuration = duration;
            _shakeTimer = duration;
            _targetShakeAmount = amount;
        }

        public void Reset() {
            _shakeTimer = 0;
            _shakeDuration = 0;
            _shakeAmount = 0;
        }

        public void Update() {
            if (_shakeTimer > 0) {
                _shakeAmount = Mathf.Lerp(0, _targetShakeAmount, _shakeTimer / _shakeDuration);
                _shipTransform.localPosition = _originalPos + Random.insideUnitSphere * _shakeAmount;
                _shakeTimer -= Time.deltaTime;
            }
            else {
                _shakeTimer = 0f;
                _shakeDuration = 0f;
                _shipTransform.localPosition = _originalPos;
            }
        }
    }
}