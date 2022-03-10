using System;
using Cinemachine;
using Core;
using Misc;
using UnityEngine;

namespace Gameplay {

    public enum CameraType {
        FirstPerson,
        ThirdPerson
    }
    
    [RequireComponent(typeof(CinemachineVirtualCamera))]
    public class ShipCamera : MonoBehaviour {
        
        [SerializeField] private string cameraName;
        [SerializeField] public CameraType cameraType;
        [SerializeField] public Vector3 maxOffset = Vector3.one;
        
        private float _baseFov;
        private CinemachineVirtualCamera _camera;
        
        public float smoothSpeed = 0.1f;
        
        private Vector3 _targetOffset = Vector3.zero;
        private Vector3 _offset = Vector3.zero;
        private Vector3 _baseLocalPosition;

        public string Name => cameraName;
        
        // Use the starting position of the active camera as the pivot otherwise the cinemachine system
        // will FREAK THE FUCK OUT trying to update the position while basing that formula on the position itself
        public Vector3 BaseLocalPosition => _baseLocalPosition;

        public CinemachineVirtualCamera Camera {
            get {
                if (_camera == null) {
                    _camera = GetComponent<CinemachineVirtualCamera>();
                }
                return _camera;
            }
        }

        public void Awake() {
            _baseLocalPosition = transform.localPosition;
        }

        public void OnEnable() {
            Game.OnGameSettingsApplied += SetBaseFov;
            SetBaseFov();
        }
    
        public void OnDisable() {
            Game.OnGameSettingsApplied -= SetBaseFov;
        }

        public void UpdateFov(Vector3 velocity, float maxVelocity) {
            var fov = Mathf.Lerp(Camera.m_Lens.FieldOfView,
                MathfExtensions.Remap(0, 1, _baseFov, _baseFov + 10, velocity.z / maxVelocity), 
                smoothSpeed
            );
            Camera.m_Lens.FieldOfView = fov; 
            Game.Instance.InGameUICamera.fieldOfView = fov;
        }
        
        public Vector3 GetCameraOffset(Vector3 force, float maxForce) {
            
            _targetOffset = new Vector3(
                MathfExtensions.Remap(-1, 1, maxOffset.x, -maxOffset.x, force.x / maxForce),
                MathfExtensions.Remap(-1, 1, maxOffset.y, -maxOffset.y, force.y / maxForce),
                MathfExtensions.Remap(-1, 1, maxOffset.z, -maxOffset.z, force.z / maxForce)
            );

            _offset = Vector3.Lerp(_offset, _targetOffset, 0.04f);
            return _offset;
        }

        private void SetBaseFov() {
            _baseFov = cameraType == CameraType.FirstPerson
                ? Preferences.Instance.GetFloat("graphics-field-of-view")
                : Preferences.Instance.GetFloat("graphics-field-of-view-ext");
        }
    }
}
