using System;
using System.Collections;
using System.Linq;
using Core.Player;
using Core.ShipModel.Feedback;
using Core.ShipModel.Feedback.interfaces;
using Core.ShipModel.ShipIndicator;
using JetBrains.Annotations;
using Misc;
using UnityEngine;

namespace Core.ShipModel {
    public class ShipPhysics : MonoBehaviour {
        public delegate void BoostFiredAction(float boostTime);

        public delegate void ShipPhysicsUpdated();

        // This magic number is the original inertiaTensor of the puffin ship which, unbeknownst to me at the time,
        // is actually calculated from the rigid body bounding boxes and impacts the torque rotation physics.
        // Therefore, to maintains consistency with the flight parameters model this will likely never change.
        // Good fun! Yay one-way doors!
        private static readonly Vector3 initialInertiaTensor = new(5189.9f, 5825.6f, 1471.6f);

        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private FeedbackEngine feedbackEngine;

        // ray-casting without per-frame allocation
        private readonly RaycastHit[] _raycastHits = new RaycastHit[2];
        private readonly ShipFeedbackData _shipFeedbackData = new();
        private readonly ShipIndicatorData _shipIndicatorData = new();
        private readonly ShipMotionData _shipMotionData = new();
        private float _boostCapacitorPercent = 100f;
        private bool _boostCharging;

        [CanBeNull] private Coroutine _boostCoroutine;
        private float _boostedMaxSpeedDelta;
        private int _checkpointLayerMask;
        private bool _collisionStartedThisFrame;
        private float _currentBoostTime;
        [CanBeNull] private Collision _currentFrameCollision;
        private float _gForce;
        private bool _isBoostDrop;
        private bool _isBoosting;

        private Vector3 _prevVelocity;

        [CanBeNull] private IShipModel _shipModel;

        private ShipParameters _shipParameters;
        private bool _stopShip;
        private float _velocityLimitCap;

        public FeedbackEngine FeedbackEngine => feedbackEngine;

        public ShipParameters FlightParameters {
            get {
                if (!targetRigidbody || _shipParameters == null) return ShipParameters.Defaults;
                return _shipParameters;
            }
            set {
                _shipParameters = value;

                // handle rigidbody params
                targetRigidbody.mass = value.mass;
                targetRigidbody.drag = value.drag;
                targetRigidbody.angularDrag = value.angularDrag;
                targetRigidbody.maxAngularVelocity = value.maxAngularVelocity;

                // setup angular momentum for collisions (higher multiplier = less spin)
                targetRigidbody.inertiaTensor = initialInertiaTensor * value.inertiaTensorMultiplier;
            }
        }

        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
        public Vector3 Velocity => targetRigidbody.velocity;
        public Vector3 AngularVelocity => targetRigidbody.angularVelocity;
        public float VelocityMagnitude => Mathf.Round(targetRigidbody.velocity.magnitude);
        public float VelocityNormalised => targetRigidbody.velocity.sqrMagnitude / (FlightParameters.maxBoostSpeed * FlightParameters.maxBoostSpeed);
        private bool BoostReady => !_boostCharging && _boostCapacitorPercent > FlightParameters.boostCapacitorPercentCost;
        public IShipFeedbackData ShipFeedbackData => _shipFeedbackData;
        public IShipMotionData ShipMotionData => _shipMotionData;
        public IShipIndicatorData ShipIndicatorData => _shipIndicatorData;
        public Vector3 CurrentFrameThrust { get; private set; }
        public Vector3 CurrentFrameTorque { get; private set; }
        public float MaxThrustWithBoost { get; private set; }
        public float MaxTorqueWithBoost { get; private set; }
        public float BoostedMaxSpeedDelta { get; private set; }


        public float Pitch { get; private set; }
        public float Roll { get; private set; }
        public float Yaw { get; private set; }
        public float Throttle { get; private set; }
        public float ThrottleRaw { get; private set; }
        public float LatH { get; private set; }
        public float LatV { get; private set; }
        public bool BoostButtonHeld { get; private set; }
        public bool VelocityLimitActive { get; private set; }
        public bool VectorFlightAssistActive { get; private set; }
        public bool RotationalFlightAssistActive { get; private set; }
        public bool IsShipLightsActive { get; private set; }


        [CanBeNull]
        public IShipModel ShipModel {
            get {
                // if no ship associated, try to grab one from the entity tree and initialise it
                if (_shipModel == null) {
                    _shipModel ??= GetComponentInChildren<PuffinShipModel>(true);
                    _shipModel ??= GetComponentInChildren<CalidrisShipModel>(true);

                    ShipModel = _shipModel;
                }

                return _shipModel;
            }
            private set {
                if (value != null) {
                    var prev = _shipModel;

                    // store and parent to main player transform
                    _shipModel = value;
                    var entity = _shipModel.Entity();
                    entity.transform.SetParent(transform, false);
                    FeedbackEngine.SubscribeFeedbackObject(_shipModel);

                    // clean up
                    if (prev != null) FeedbackEngine.RemoveFeedbackObject(prev);
                    if (prev != value && prev != null) {
                        Debug.Log("Cleaning up existing ... " + prev);
                        Destroy(prev.Entity().gameObject);
                    }
                }
            }
        }

        public void Start() {
            DontDestroyOnLoad(this);

            // rigidbody non-configurable defaults
            targetRigidbody.centerOfMass = Vector3.zero;
            targetRigidbody.inertiaTensorRotation = Quaternion.identity;

            // init physics
            FlightParameters = ShipParameters.Defaults;

            // init checkpoint mask id
            _checkpointLayerMask = LayerMask.NameToLayer("Checkpoint");
        }

        private void FixedUpdate() {
            if (_stopShip) UpdateShip(0, 0, 0, 0, 0, 0, false, false, true, true);
        }

        public void ResetPhysics(bool includeBoost = true) {
            targetRigidbody.velocity = Vector3.zero;
            targetRigidbody.angularVelocity = Vector3.zero;
            _prevVelocity = Vector3.zero;
            _stopShip = false;
            if (includeBoost) {
                _boostCharging = false;
                _isBoosting = false;
                _boostCapacitorPercent = 100f;
                if (_boostCoroutine != null) StopCoroutine(_boostCoroutine);
            }
        }

        public event BoostFiredAction OnBoost;
        public event ShipPhysicsUpdated OnShipPhysicsUpdated;

        public void RefreshShipModel(ShipProfile shipProfile) {
            var shipData = ShipMeta.FromString(shipProfile.shipModel);
            // TODO: make this async
            var shipModel = Instantiate(Resources.Load(shipData.PrefabToLoad, typeof(GameObject)) as GameObject);

            void SetLayerMaskRecursive(int layer, GameObject targetGameObject) {
                targetGameObject.layer = layer;
                foreach (Transform child in targetGameObject.transform) SetLayerMaskRecursive(layer, child.gameObject);
            }

            SetLayerMaskRecursive(gameObject.layer, shipModel);

            var shipObject = shipModel.GetComponent<IShipModel>();
            shipObject.SetPrimaryColor(shipProfile.primaryColor);
            shipObject.SetAccentColor(shipProfile.accentColor);
            shipObject.SetThrusterColor(shipProfile.thrusterColor);
            shipObject.SetTrailColor(shipProfile.trailColor);
            shipObject.SetHeadLightsColor(shipProfile.headLightsColor);
            ShipModel = shipObject;
        }

        public void OnCollision(Collision collision, bool startedThisFrame) {
            _currentFrameCollision = collision;
            _collisionStartedThisFrame = startedThisFrame;
        }

        // standard OnTriggerEnter doesn't cut the mustard at these speeds so we need to do something a bit more precise
        public void CheckpointCollisionCheck() {
            var frameVelocity = targetRigidbody.velocity * Time.fixedDeltaTime;
            if (frameVelocity == Vector3.zero) return;

            var start = targetRigidbody.transform.position;
            var direction = frameVelocity.normalized;
            var maxDistance = frameVelocity.magnitude * 2;
            var orientation = Quaternion.LookRotation(frameVelocity);

            // Check for checkpoint collisions using a velocity ray box rather than the inbuilt box check
            var halfExtents = new Vector3(5, 3, frameVelocity.magnitude / 2);
            var raycastHitCount = Physics.BoxCastNonAlloc(start, halfExtents, direction, _raycastHits, orientation, maxDistance, 1 << _checkpointLayerMask);

            ExtDebug.DrawBoxCastBox(start, halfExtents, orientation, direction, maxDistance, Color.red);

            var checkpointHitCount = 0;
            var checkpointDistance = 0f;
            Checkpoint checkpointFound = null;
            for (var i = 0; i < raycastHitCount; i++) {
                var raycastHit = _raycastHits[i];
                var checkpoint = raycastHit.collider.GetComponentInParent<Checkpoint>();
                if (checkpoint) {
                    checkpointHitCount++;
                    checkpointDistance = Mathf.Max(checkpointDistance, raycastHit.distance);
                    if (checkpointHitCount > 1) checkpointFound = checkpoint;
                }
            }

            if (checkpointFound != null) {
                var excessTimeToHit = checkpointDistance / frameVelocity.magnitude * Time.fixedDeltaTime;
                // Debug.Log("DISTANCE " + distance + $"   Time to hit: {excessTimeToHit}");
                checkpointFound.Hit(excessTimeToHit);
            }
        }

        public void ShipLightsToggle(Action<bool> shipLightStatus) {
            IsShipLightsActive = !IsShipLightsActive;
            shipLightStatus(IsShipLightsActive);
        }

        /**
         * Simulated flight assist on with zero input from this point onward (used for ending ghosts etc)
         * See fixed update
         */
        public void BringToStop() {
            _stopShip = true;
        }

        private void AttemptBoost() {
            if (BoostReady) {
                _boostCapacitorPercent -= FlightParameters.boostCapacitorPercentCost;
                _boostCharging = true;

                IEnumerator DoBoost() {
                    OnBoost?.Invoke(FlightParameters.totalBoostTime);
                    yield return YieldExtensions.WaitForFixedFrames(YieldExtensions.SecondsToFixedFrames(1));
                    _currentBoostTime = 0f;
                    _boostedMaxSpeedDelta = FlightParameters.maxBoostSpeed - FlightParameters.maxSpeed;
                    _isBoosting = true;
                    yield return YieldExtensions.WaitForFixedFrames(YieldExtensions.SecondsToFixedFrames(FlightParameters.boostRechargeTime));
                    _boostCharging = false;
                }

                _boostCoroutine = StartCoroutine(DoBoost());
            }
        }

        // TODO: clamping should be based on input rather than modifying the rigid body - if gravity pulls you down (or boost pads increase speed!)
        //   then that's fine, similar to if a collision yeets you into a spinning mess.
        private void ClampMaxSpeed(bool velocityLimiterActive) {
            // clamp max speed if user is holding the velocity limiter button down
            if (velocityLimiterActive) {
                _velocityLimitCap = Math.Max(_prevVelocity.magnitude, FlightParameters.minUserLimitedVelocity);
                targetRigidbody.velocity = Vector3.ClampMagnitude(targetRigidbody.velocity, _velocityLimitCap);
            }

            // clamp max speed in general including boost variance (max boost speed minus max speed)
            targetRigidbody.velocity = Vector3.ClampMagnitude(targetRigidbody.velocity, FlightParameters.maxSpeed + BoostedMaxSpeedDelta);

            // calculate g-force 
            _gForce = Math.Abs((Velocity - _prevVelocity).magnitude / (Time.fixedDeltaTime * 9.8f));
        }

        private void UpdateBoostStatus() {
            _boostCapacitorPercent = Mathf.Min(100,
                _boostCapacitorPercent + FlightParameters.boostCapacityPercentChargeRate * Time.fixedDeltaTime);

            // copy defaults before modifying
            MaxThrustWithBoost = FlightParameters.maxThrust;
            MaxTorqueWithBoost = FlightParameters.maxThrust * FlightParameters.torqueThrustMultiplier;
            BoostedMaxSpeedDelta = _boostedMaxSpeedDelta;

            _currentBoostTime += Time.fixedDeltaTime;

            // reduce boost potency over time period
            if (_isBoosting) {
                // Ease-in (boost drop-off is more dramatic)
                var t = _currentBoostTime / FlightParameters.totalBoostTime;
                var tBoost = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                var tTorque = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);

                MaxThrustWithBoost *= Mathf.Lerp(FlightParameters.thrustBoostMultiplier, 1, tBoost);
                MaxTorqueWithBoost *= Mathf.Lerp(FlightParameters.torqueBoostMultiplier, 1, tTorque);
            }

            // reduce max speed over time until we're back at 0
            if (_boostedMaxSpeedDelta > 0) {
                var t = _currentBoostTime / FlightParameters.boostMaxSpeedDropOffTime;
                // TODO: an actual curve rather than this ... idk what this is
                // clamp at 1 as it's being used as a multiplier and the first ~2 seconds are at max speed 
                var tBoostVelocityMax = Math.Min(1, 0.15f - Mathf.Cos(t * Mathf.PI * 0.6f) * -1);
                BoostedMaxSpeedDelta *= tBoostVelocityMax;

                if (tBoostVelocityMax < 0) _boostedMaxSpeedDelta = 0;
            }

            if (_currentBoostTime > FlightParameters.totalBoostRotationalTime) _isBoosting = false;
        }

        public void UpdateShip(
            float pitch, float roll, float yaw, float throttle, float latH, float latV, bool boostButtonHeld, bool velocityLimitActive,
            bool vectorFlightAssistActive, bool rotationalFlightAssistActive
        ) {
            Pitch = pitch;
            Roll = roll;
            Yaw = yaw;
            Throttle = throttle;
            ThrottleRaw = throttle;
            LatH = latH;
            LatV = latV;
            BoostButtonHeld = boostButtonHeld;
            VelocityLimitActive = velocityLimitActive;
            VectorFlightAssistActive = vectorFlightAssistActive;
            RotationalFlightAssistActive = rotationalFlightAssistActive;

            /* FLIGHT ASSISTS */
            var maxSpeedWithBoost = FlightParameters.maxSpeed + BoostedMaxSpeedDelta;
            if (vectorFlightAssistActive) CalculateVectorControlFlightAssist(maxSpeedWithBoost);
            if (rotationalFlightAssistActive) CalculateRotationalDampeningFlightAssist();

            OnShipPhysicsUpdated?.Invoke();

            if (boostButtonHeld) AttemptBoost();
            UpdateBoostStatus();
            ApplyFlightForces();
            UpdateIndicatorData();
            UpdateMotionData();
            UpdateFeedbackData();

            _prevVelocity = Velocity;
        }

        // public overrides for motion data, used for multiplayer non-local client RPC calls
        public void UpdateMotionData(Vector3 velocity, Vector3 thrust, Vector3 torque) {
            _shipMotionData.CurrentLateralForce = thrust;
            _shipMotionData.CurrentLateralVelocity = velocity;
            _shipMotionData.CurrentAngularTorque = torque;

            _shipMotionData.CurrentAngularVelocity = AngularVelocity;
            _shipMotionData.CurrentLateralForceNormalised = thrust / MaxThrustWithBoost;
            _shipMotionData.CurrentLateralVelocityNormalised = velocity / FlightParameters.maxBoostSpeed;
            _shipMotionData.CurrentAngularVelocityNormalised = AngularVelocity / FlightParameters.maxAngularVelocity;
            _shipMotionData.CurrentAngularTorqueNormalised = torque / (FlightParameters.maxThrust * FlightParameters.torqueThrustMultiplier);
            _shipMotionData.MaxLateralVelocity = FlightParameters.maxBoostSpeed;
        }

        private void UpdateMotionData() {
            UpdateMotionData(Velocity, CurrentFrameThrust, CurrentFrameTorque);
        }

        private void UpdateIndicatorData() {
            _shipIndicatorData.ThrottlePositionNormalised = VectorFlightAssistActive ? ThrottleRaw : Throttle;
            _shipIndicatorData.AccelerationMagnitudeNormalised =
                (Math.Abs(CurrentFrameThrust.x) + Math.Abs(CurrentFrameThrust.y) + Math.Abs(CurrentFrameThrust.z)) /
                FlightParameters.maxThrust;
            _shipIndicatorData.VelocityMagnitude = VelocityMagnitude;
            _shipIndicatorData.BoostCapacitorPercent = _boostCapacitorPercent;
            _shipIndicatorData.BoostTimerReady = !_boostCharging;
            _shipIndicatorData.BoostChargeReady = _boostCapacitorPercent > FlightParameters.boostCapacitorPercentCost;
            _shipIndicatorData.LightsActive = IsShipLightsActive;
            _shipIndicatorData.VelocityLimiterActive = VelocityLimitActive;
            _shipIndicatorData.VectorFlightAssistActive = VectorFlightAssistActive;
            _shipIndicatorData.RotationalFlightAssistActive = RotationalFlightAssistActive;
            _shipIndicatorData.GForce = _gForce;
        }

        private void UpdateFeedbackData() {
            // Collision Handling
            _shipFeedbackData.CollisionThisFrame = false;
            _shipFeedbackData.CollisionImpactNormalised = 0;
            _shipFeedbackData.CollisionDirection = Vector3.zero;

            if (_currentFrameCollision != null) {
                var normalBuffer = _currentFrameCollision.contacts.Aggregate(Vector3.zero, (current, contact) => current + contact.normal);
                var impact = Mathf.Abs(Vector3.Dot(normalBuffer.normalized, _prevVelocity.normalized));

                _shipFeedbackData.CollisionThisFrame = true;
                _shipFeedbackData.CollisionStartedThisFrame = _collisionStartedThisFrame;
                _shipFeedbackData.CollisionImpactNormalised = impact;
                _shipFeedbackData.CollisionDirection =
                    targetRigidbody.transform.InverseTransformDirection(normalBuffer.normalized) * -1;

                // handle boost cap impact (this should go elsewhere!)
                if (_collisionStartedThisFrame) _boostCapacitorPercent *= 1 - ShipFeedbackData.CollisionImpactNormalised;
            }

            _currentFrameCollision = null;
            _collisionStartedThisFrame = false;

            // Boost forces
            // TODO: rest of boost progress
            _shipFeedbackData.BoostProgressNormalised = _isBoosting ? _currentBoostTime / FlightParameters.totalBoostTime : 0;
            _shipFeedbackData.ShipShake = ShipModel?.ShipShake.CurrentShakeAmount ?? 0;
        }

        private void ApplyFlightForces() {
            /* GRAVITY */
            var gravity = new Vector3(
                Game.Instance.LoadedLevelData.gravity.x,
                Game.Instance.LoadedLevelData.gravity.y,
                Game.Instance.LoadedLevelData.gravity.z
            );
            targetRigidbody.AddForce(targetRigidbody.mass * gravity);

            /* INPUTS */

            var latH = LatH;
            var latV = LatV;
            var throttle = Throttle;

            // special case for throttle - no reverse while boosting but, while always going forward, the ship will change
            // vector less harshly while holding back (up to 40%). The whole reverse axis is remapped to 40% for this calculation.
            // any additional throttle thrust not used in boost to be distributed across laterals
            if (_isBoosting && _currentBoostTime < FlightParameters.totalBoostTime) {
                throttle = Mathf.Min(1f, MathfExtensions.Remap(-1, 0, 0.6f, 1, Throttle));

                var delta = 1f - throttle;
                if (delta > 0) {
                    var latInputTotal = Mathf.Abs(latH) + Mathf.Abs(latV);
                    if (latInputTotal > 0) {
                        var latHComponent = Mathf.Abs(latH) / latInputTotal;
                        var latVComponent = Mathf.Abs(latV) / latInputTotal;
                        latH *= 1 + delta * latHComponent;
                        latV *= 1 + delta * latVComponent;
                    }
                }
            }

            // Get the raw inputs multiplied by the ship params multipliers as a vector3.
            // All components are between -1 and 1.
            var thrustInput = new Vector3(
                latH * FlightParameters.latHMultiplier,
                latV * FlightParameters.latVMultiplier,
                throttle * FlightParameters.throttleMultiplier
            );

            /* THRUST */
            // standard thrust calculated per-axis (each axis has it's own max thrust component including boost)
            var thrust = thrustInput * MaxThrustWithBoost;
            targetRigidbody.AddForce(transform.TransformDirection(thrust));

            /* TORQUE */
            // torque is applied entirely independently, this may be looked at later.
            var torque = new Vector3(
                Pitch * FlightParameters.pitchMultiplier * MaxTorqueWithBoost * -1,
                Yaw * FlightParameters.yawMultiplier * MaxTorqueWithBoost,
                Roll * FlightParameters.rollMultiplier * MaxTorqueWithBoost * -1
            ) * FlightParameters
                .inertiaTensorMultiplier; // if we don't counteract the inertial tensor of the rigidbody, the rotation spin would increase in lockstep

            targetRigidbody.AddTorque(transform.TransformDirection(torque));

            ClampMaxSpeed(VelocityLimitActive);

            // output var for indicators etc
            CurrentFrameThrust = thrustInput * FlightParameters.maxThrust;
            CurrentFrameTorque = torque / FlightParameters.inertiaTensorMultiplier;
        }

        #region Flight Assist Calculations

        private void CalculateVectorControlFlightAssist(float maxSpeedWithBoost) {
            // TODO: Correctly calculate gravity for FA (need the actual velocity from acceleration caused in the previous frame)
            var localVelocity = transform.InverseTransformDirection(targetRigidbody.velocity);

            LatH = CalculateAssistedAxis(LatH, localVelocity.x, 0.1f, maxSpeedWithBoost);
            LatV = CalculateAssistedAxis(LatV, localVelocity.y, 0.1f, maxSpeedWithBoost);
            Throttle = CalculateAssistedAxis(Throttle, localVelocity.z, 0.1f, maxSpeedWithBoost);
        }

        private void CalculateRotationalDampeningFlightAssist() {
            // convert global rigid body velocity into local space
            var localAngularVelocity = transform.InverseTransformDirection(targetRigidbody.angularVelocity);

            Pitch = CalculateAssistedAxis(Pitch, localAngularVelocity.x * -1, 1f, 3.0f);
            Yaw = CalculateAssistedAxis(Yaw, localAngularVelocity.y, 1f, 3.0f);
            Roll = CalculateAssistedAxis(Roll, localAngularVelocity.z * -1, 1f, 3.0f);
        }

        /**
         * Given a target factor between 0 and 1 for a given axis, the current gross value and the maximum, calculate a
         * new axis value to apply as input.
         * @param targetFactor value between 0 and 1 (effectively the users' input)
         * @param currentAxisVelocity the non-normalised raw value of the current motion of the axis
         * @param interpolateAtPercent the point at which to begin linearly interpolating the acceleration
         * (e.g. 0.1 = at 10% of the MAXIMUM velocity of the axis away from the target, interpolate the axis -
         * if the current speed is 0, the target is 0.5 and this value is 0.1, this means that at 40% of the maximum
         * speed -- when the axis is at 0.4 -- decrease the output linearly such that it moves from 1 to 0 and slowly
         * decelerates.
         * @param max the maximum non-normalised value for this axis e.g. the maximum speed or maximum rotation in radians etc
         * @param out axis the value to apply the calculated new axis of input to
         */
        private float CalculateAssistedAxis(
            float targetFactor,
            float currentAxisVelocity,
            float interpolateAtPercent,
            float max
        ) {
            var targetRate = max * targetFactor;

            // prevent tiny noticeable movement on start and jitter
            if (Math.Abs(currentAxisVelocity - targetRate) < 0.000001f) return 0;

            // basic max or min
            float axis = currentAxisVelocity - targetRate < 0 ? 1 : -1;

            // interpolation over final range (interpolateAtPercent)
            var velocityInterpolateRange = max * interpolateAtPercent;

            // positive motion
            if (currentAxisVelocity < targetRate && currentAxisVelocity > targetRate - velocityInterpolateRange) {
                var startInterpolate = targetRate - velocityInterpolateRange;
                axis *= Mathf.InverseLerp(targetRate, startInterpolate, currentAxisVelocity);
            }

            // negative motion
            if (currentAxisVelocity > targetRate && currentAxisVelocity < targetRate + velocityInterpolateRange) {
                var startInterpolate = targetRate + velocityInterpolateRange;
                axis *= Mathf.InverseLerp(targetRate, startInterpolate, currentAxisVelocity);
            }

            return axis;
        }

        #endregion
    }
}