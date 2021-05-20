using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using Audio;
using Engine;
using JetBrains.Annotations;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.UI;

public class ShipParameters {
    public float mass;
    public float drag;
    public float angularDrag;
    public float inertiaTensorMultiplier;
    public float maxSpeed;
    public float maxBoostSpeed;
    public float maxThrust;
    public float torqueThrustMultiplier;
    public float pitchMultiplier;
    public float rollMultiplier;
    public float yawMultiplier;
    public float thrustBoostMultiplier;
    public float torqueBoostMultiplier;
    public float totalBoostTime;
    public float totalBoostRotationalTime;
    public float boostMaxSpeedDropOffTime;
    public float boostRechargeTime;
    public float minUserLimitedVelocity;

    public string ToJsonString() {
        return JsonConvert.SerializeObject(this, Formatting.Indented);
    }

    [CanBeNull]
    public static ShipParameters FromJsonString(string json) {
        try {
            return JsonConvert.DeserializeObject<ShipParameters>(json);
        }
        catch (Exception e){
            Debug.LogWarning(e.Message);
            return null;
        }
    }
}

[RequireComponent(typeof(Transform))]
[RequireComponent(typeof(Rigidbody))]
public class Ship : MonoBehaviour {
    
    // TODO: remove this stuff once params are finalised (this is for debug panel in release)
    public static ShipParameters ShipParameterDefaults {
        get => new ShipParameters {
            mass = 1000f,
            drag = 0f,
            angularDrag = 0f,
            inertiaTensorMultiplier = 125f,
            maxSpeed = 800f,
            maxBoostSpeed = 932f,
            maxThrust = 100000f,
            torqueThrustMultiplier = 0.1f,
            pitchMultiplier = 1,
            rollMultiplier = 0.3f,
            yawMultiplier = 0.5f,
            thrustBoostMultiplier = 5,
            torqueBoostMultiplier = 2f,
            totalBoostTime = 6f,
            totalBoostRotationalTime = 7f,
            boostMaxSpeedDropOffTime = 12f,
            boostRechargeTime = 5f,
            minUserLimitedVelocity = 250f,
        };
    }
    public ShipParameters Parameters {
        get {
            if (!_rigidBody) {
                return ShipParameterDefaults; 
            }
            var parameters = new ShipParameters();
            parameters.mass = Mathf.Round(_rigidBody.mass);
            parameters.drag = _rigidBody.drag;
            parameters.angularDrag = _rigidBody.angularDrag;
            parameters.inertiaTensorMultiplier = inertialTensorMultiplier;
            parameters.maxSpeed = maxSpeed; 
            parameters.maxBoostSpeed = maxBoostSpeed;
            parameters.maxThrust = maxThrust;
            parameters.torqueThrustMultiplier = torqueThrustMultiplier;
            parameters.pitchMultiplier = pitchMultiplier;
            parameters.rollMultiplier = rollMultiplier;
            parameters.yawMultiplier = yawMultiplier;
            parameters.thrustBoostMultiplier = thrustBoostMultiplier;
            parameters.torqueBoostMultiplier = torqueBoostMultiplier;
            parameters.totalBoostTime = totalBoostTime;
            parameters.totalBoostRotationalTime = totalBoostRotationalTime;
            parameters.boostMaxSpeedDropOffTime = boostMaxSpeedDropOffTime;
            parameters.boostRechargeTime = boostRechargeTime;
            parameters.minUserLimitedVelocity = minUserLimitedVelocity;
            return parameters;
        }
        set {
            _rigidBody.mass = value.mass;
            _rigidBody.drag = value.drag;
            _rigidBody.angularDrag = value.angularDrag;
            _rigidBody.inertiaTensor = _initialInertiaTensor * value.inertiaTensorMultiplier;
            inertialTensorMultiplier = value.inertiaTensorMultiplier;
            
            maxSpeed = value.maxSpeed;
            maxBoostSpeed = value.maxBoostSpeed;
            maxThrust = value.maxThrust;
            torqueThrustMultiplier = value.torqueThrustMultiplier;
            pitchMultiplier = value.pitchMultiplier;
            rollMultiplier = value.rollMultiplier;
            yawMultiplier = value.yawMultiplier;
            thrustBoostMultiplier = value.thrustBoostMultiplier;
            torqueBoostMultiplier = value.torqueBoostMultiplier;
            totalBoostTime = value.totalBoostTime;
            totalBoostRotationalTime = value.totalBoostRotationalTime;
            boostMaxSpeedDropOffTime = value.boostMaxSpeedDropOffTime;
            boostRechargeTime = value.boostRechargeTime;
            minUserLimitedVelocity = value.minUserLimitedVelocity;
        }
    }
    
    [SerializeField] private Text velocityIndicator;
    [SerializeField] private Light shipLights;
    
    // TODO: split this into various thruster powers
    [SerializeField] private float maxSpeed = 800;
    [SerializeField] private float maxBoostSpeed = 932;
    [SerializeField] private float maxThrust = 100000;
    [SerializeField] private float torqueThrustMultiplier = 0.1f;
    [SerializeField] private float pitchMultiplier = 1;
    [SerializeField] private float rollMultiplier = 0.3f;
    [SerializeField] private float yawMultiplier = 0.5f;
    [SerializeField] private float thrustBoostMultiplier = 5;
    [SerializeField] private float torqueBoostMultiplier = 2f;
    [SerializeField] private float totalBoostTime = 6f;
    [SerializeField] private float totalBoostRotationalTime = 7f;
    [SerializeField] private float boostMaxSpeedDropOffTime = 12f;
    [SerializeField] private float boostRechargeTime = 5f;
    [SerializeField] private float inertialTensorMultiplier = 125f;
    [SerializeField] private float minUserLimitedVelocity = 250f;

    private Vector3 _initialInertiaTensor;

    private bool _boostCharging;
    private bool _isBoosting;
    private float _currentBoostTime;
    private float _boostedMaxSpeedDelta;

    private float _prevVelocity;
    private bool _userVelocityLimit;
    private float _velocityLimitCap;
    private bool _flightAssist;

    // input axes -1 to 1
    private float _throttle;
    private float _latV;
    private float _latH;
    private float _pitch;
    private float _yaw;
    private float _roll;
    
    // flight assist targets
    private float _throttleTargetFactor;
    private float _latHTargetFactor;
    private float _latVTargetFactor;
    private float _pitchTargetFactor;
    private float _rollTargetFactor;
    private float _yawTargetFactor;

    [CanBeNull] private Coroutine _boostCoroutine;

    private Transform _transformComponent;
    private Rigidbody _rigidBody;
    
    public float Velocity {
        get {
            return Mathf.Round(_rigidBody.velocity.magnitude);
        }
    }

    public void Awake() {
        _transformComponent = GetComponent<Transform>();
        _rigidBody = GetComponent<Rigidbody>();
    }

    public void Start() {
        _flightAssist = Preferences.Instance.GetBool("flightAssistOnByDefault");
        _rigidBody.centerOfMass = Vector3.zero;
        _rigidBody.inertiaTensorRotation = Quaternion.identity;

        // setup angular momentum for collisions (higher multiplier = less spin)
        _initialInertiaTensor = _rigidBody.inertiaTensor;
        _rigidBody.inertiaTensor *= inertialTensorMultiplier;
    }

    public void Reset() {
        _rigidBody.velocity = Vector3.zero;
        _rigidBody.angularVelocity = Vector3.zero;
        _pitch = 0;
        _roll = 0;
        _yaw = 0;
        _throttle = 0;
        _latH = 0;
        _latV = 0;
        _boostCharging = false;
        _isBoosting = false;
        _prevVelocity = 0;
        var shipCamera = GetComponentInChildren<ShipCamera>();
        if (shipCamera) {
            shipCamera.Reset();
        }

        if (_boostCoroutine != null) {
            StopCoroutine(_boostCoroutine);
        }

        AudioManager.Instance.Stop("ship-boost");
    }

    public void SetPitch(float value) {
        if (_flightAssist) {
            _pitchTargetFactor = ClampInput(value);
        }
        else {
            _pitch = ClampInput(value);
        }
    }

    public void SetRoll(float value) {
        if (_flightAssist) {
            _rollTargetFactor = ClampInput(value);
        }
        else {
            _roll = ClampInput(value);
        }
    }

    public void SetYaw(float value) {
        if (_flightAssist) {
            _yawTargetFactor = ClampInput(value);
        }
        else {
            _yaw = ClampInput(value);
        }
    }

    public void SetThrottle(float value) {
        if (_flightAssist) {
            _throttleTargetFactor = ClampInput(value);
        }
        else {
            _throttle = ClampInput(value);
        }
    }
    
    public void SetLateralH(float value) {
        if (_flightAssist) {
            _latHTargetFactor = ClampInput(value);
        }
        else {
            _latH = ClampInput(value);
        }
    }
    
    public void SetLateralV(float value) {
        if (_flightAssist) {
            _latVTargetFactor = ClampInput(value);
        }
        else {
            _latV = ClampInput(value);
        }
    }

    public void Boost(bool isPressed) {
        var boost = isPressed;
        if (boost && !_boostCharging) {
            _boostCharging = true;

            IEnumerator DoBoost() {
                AudioManager.Instance.Play("ship-boost");
                yield return new WaitForSeconds(1);
                _currentBoostTime = 0f;
                _boostedMaxSpeedDelta = maxBoostSpeed - maxSpeed;
                _isBoosting = true;
                yield return new WaitForSeconds(boostRechargeTime);
                _boostCharging = false;
            }
            _boostCoroutine = StartCoroutine(DoBoost());
        }
    }

    public void FlightAssistToggle() {
        _flightAssist = !_flightAssist;
        Debug.Log("Flight Assist " + (_flightAssist ? "ON" : "OFF") + " (partially implemented)");
        
        // TODO: proper flight assist sounds
        if (_flightAssist) {
            AudioManager.Instance.Play("ship-alternate-flight-on");
        }
        else {
            AudioManager.Instance.Play("ship-alternate-flight-off");
        }
    }

    public void ShipLightsToggle() {
        AudioManager.Instance.Play("ui-nav");
        shipLights.enabled = !shipLights.enabled;
    }

    public void VelocityLimiterIsPressed(bool isPressed) {
        _userVelocityLimit = isPressed;
        
        if (_userVelocityLimit) {
            AudioManager.Instance.Play("ship-velocity-limit-on");
        }
        else {
            AudioManager.Instance.Play("ship-velocity-limit-off");
        }
    }
    
    // Get the position and rotation of the ship within the world, taking into account floating origin fix
    public void AbsoluteWorldPosition(out Vector3 position, out Quaternion rotation) {
        var t = transform; 
        var p = t.position; 
        var r = t.rotation.eulerAngles;
        position.x = p.x;
        position.y = p.y;
        position.z = p.z;
        rotation = Quaternion.Euler(r.x, r.y, r.z);

        // if floating origin fix is active, overwrite position with corrected world space
        var floatingOrigin = FindObjectOfType<FloatingOrigin>();
        if (floatingOrigin) {
            var origin = floatingOrigin.FocalObjectPosition;
            position.x = origin.x;
            position.y = origin.y;
            position.z = origin.z;
        }
    }

    private void OnTriggerEnter(Collider other) {
        var checkpoint = other.GetComponentInParent<Checkpoint>();
        if (checkpoint) {
            checkpoint.Hit();
        }
    }

    // Apply all physics updates in fixed intervals (WRITE)
    private void FixedUpdate() {
        CalculateBoost(out var maxThrustWithBoost, out var maxTorqueWithBoost, out var boostedMaxSpeedDelta);
        CalculateFlight(maxThrustWithBoost, maxTorqueWithBoost);
        
        // TODO: clamping should be based on input rather than modifying the rigid body - if gravity pulls you down then that's fine, similar to if a collision yeets you into a spinning mess.
        ClampMaxSpeed(boostedMaxSpeedDelta);
        UpdateIndicators();
    }

    private void UpdateIndicators() {
        if (velocityIndicator != null) {
            velocityIndicator.text = Velocity.ToString(CultureInfo.InvariantCulture);
        }
    }
    
    /**
     * All axis should be between -1 and 1. 
     */
    private float ClampInput(float input) {
        return Mathf.Min(Mathf.Max(input, -1), 1);
    }

    private void CalculateBoost(out float maxThrustWithBoost, out float maxTorqueWithBoost, out float boostedMaxSpeedDelta) {
        maxThrustWithBoost = maxThrust;
        maxTorqueWithBoost = maxThrust * torqueThrustMultiplier;
        boostedMaxSpeedDelta = _boostedMaxSpeedDelta;
        
        _currentBoostTime += Time.fixedDeltaTime;

        // reduce boost potency over time period
        if (_isBoosting) {
            // Ease-in (boost dropoff is more dramatic)
            float t = _currentBoostTime / totalBoostTime;
            float tBoost = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
            float tTorque = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);

            maxThrustWithBoost *= Mathf.Lerp(thrustBoostMultiplier, 1, tBoost);
            maxTorqueWithBoost *= Mathf.Lerp(torqueBoostMultiplier, 1, tTorque);
        }

        // reduce max speed over time until we're back at 0
        if (_boostedMaxSpeedDelta > 0) {
            float t = _currentBoostTime / boostMaxSpeedDropOffTime;
            // TODO: an actual curve rather than this ... idk what this is
            // clamp at 1 as it's being used as a multiplier and the first ~2 seconds are at max speed 
            float tBoostVelocityMax =  Math.Min(1, 0.15f - (Mathf.Cos(t * Mathf.PI * 0.6f) * -1));
            boostedMaxSpeedDelta *= tBoostVelocityMax;
            
            if (tBoostVelocityMax < 0) {
                _boostedMaxSpeedDelta = 0;
            }
        }
        
        if (_currentBoostTime > totalBoostRotationalTime) {
            _isBoosting = false;
        }
    }

    private void CalculateFlight(float maxThrustWithBoost, float maxTorqueWithBoost) {
        if (_flightAssist) {
            CalculateFlightAssist();
        }
        
        // special case for throttle - no reverse while boosting! sorry mate 
        var throttle = _isBoosting && _currentBoostTime < totalBoostTime
            ? 1
            : _throttle;

        var tThrust = new Vector3(
            _latH * maxThrustWithBoost,
            _latV * maxThrustWithBoost,
            throttle * maxThrustWithBoost
        );

        var tRot = new Vector3(
            _pitch * pitchMultiplier * maxTorqueWithBoost,
            _yaw * yawMultiplier * maxTorqueWithBoost,
            _roll * rollMultiplier * maxTorqueWithBoost * -1
        ) * inertialTensorMultiplier;   // if we don't counteract the inertial tensor of the rigidbody, the rotation spin would increase in lockstep
        
        _rigidBody.AddForce(transform.TransformDirection(tThrust));
        _rigidBody.AddTorque(transform.TransformDirection(tRot));
    }

    private void CalculateFlightAssist() {
        // convert global rigid body velocities into local space
        Vector3 localVelocity = transform.InverseTransformDirection(_rigidBody.velocity);
        Vector3 localAngularVelocity = transform.InverseTransformDirection(_rigidBody.angularVelocity);
        
        // thrust
        CalculateAssistedAxis(_latHTargetFactor, localVelocity.x, 0.1f, maxSpeed, out _latH);
        CalculateAssistedAxis(_latVTargetFactor, localVelocity.y, 0.1f, maxSpeed, out _latV);
        CalculateAssistedAxis(_throttleTargetFactor, localVelocity.z, 0.1f, maxSpeed, out _throttle);

        // rotation
        CalculateAssistedAxis(_pitchTargetFactor, localAngularVelocity.x, 0.1f, 1, out _pitch);
        CalculateAssistedAxis(_yawTargetFactor, localAngularVelocity.y, 0.1f, 1, out _yaw);
        CalculateAssistedAxis(_rollTargetFactor, localAngularVelocity.z * -1, 0.1f, 1, out _roll);
    }
    
    private void CalculateAssistedAxis(
        float targetFactor, 
        float currentAxisVelocity, 
        float interpolateAtPercent,
        float max,
        out float axis
    ) {
        var targetRate = max * targetFactor;

        // basic max or min
        axis = currentAxisVelocity - targetRate < 0 ? 1 : -1;

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
    }

    private void ClampMaxSpeed(float boostedMaxSpeedDelta) {
        // clamp max speed if user is holding the velocity limiter button down
        if (_userVelocityLimit) {
            _velocityLimitCap = Math.Max(_prevVelocity, minUserLimitedVelocity);
            _rigidBody.velocity = Vector3.ClampMagnitude(_rigidBody.velocity, _velocityLimitCap);
        }

        // clamp max speed in general including boost variance (max boost speed minus max speed)
        _rigidBody.velocity = Vector3.ClampMagnitude(_rigidBody.velocity, maxSpeed + boostedMaxSpeedDelta);
        _prevVelocity = _rigidBody.velocity.magnitude;
    }
}
