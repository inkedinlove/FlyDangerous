using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class DevPanelFlightParams : MonoBehaviour {

    [SerializeField] private InputField massTextField;
    [SerializeField] private InputField maxSpeedTextField;
    [SerializeField] private InputField maxBoostSpeedTextField;
    [SerializeField] private InputField maxThrustTextField;
    [SerializeField] private InputField dragTextField;
    [SerializeField] private InputField angularDragTextField;
    [SerializeField] private InputField torqueThrustMultiplierTextField;
    [SerializeField] private InputField pitchMultiplierTextField;
    [SerializeField] private InputField rollMultiplierTextField;
    [SerializeField] private InputField yawMultiplierTextField;
    [SerializeField] private InputField thrustBoostMultiplierTextField;
    [SerializeField] private InputField torqueBoostMultiplierTextField;
    [SerializeField] private InputField totalBoostTimeTextField;
    [SerializeField] private InputField totalBoostRotationalTimeTextField;
    [SerializeField] private InputField boostRechargeTimeTextField;
    [SerializeField] private InputField intertialTensorMultiplierTextField;
    [SerializeField] private InputField minUserLimitedVelocityTextField;
    
    // Start is called before the first frame update
    void OnEnable() {
        var game = FindObjectOfType<Game>();
        var defaults = Ship.ShipParameterDefaults;

        massTextField.placeholder.GetComponent<Text>().text = defaults.mass.ToString();
        maxSpeedTextField.placeholder.GetComponent<Text>().text = defaults.maxSpeed.ToString();
        maxBoostSpeedTextField.placeholder.GetComponent<Text>().text = defaults.maxBoostSpeed.ToString();
        maxThrustTextField.placeholder.GetComponent<Text>().text = defaults.maxThrust.ToString();
        dragTextField.placeholder.GetComponent<Text>().text = defaults.drag.ToString();
        angularDragTextField.placeholder.GetComponent<Text>().text = defaults.angularDrag.ToString();
        torqueThrustMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.torqueThrustMultiplier.ToString();
        pitchMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.pitchMultiplier.ToString();
        rollMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.rollMultiplier.ToString();
        yawMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.yawMultiplier.ToString();
        thrustBoostMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.thrustBoostMultiplier.ToString();
        torqueBoostMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.torqueBoostMultiplier.ToString();
        totalBoostTimeTextField.placeholder.GetComponent<Text>().text = defaults.totalBoostTime.ToString();
        totalBoostRotationalTimeTextField.placeholder.GetComponent<Text>().text = defaults.totalBoostRotationalTime.ToString();
        boostRechargeTimeTextField.placeholder.GetComponent<Text>().text = defaults.boostRechargeTime.ToString();
        intertialTensorMultiplierTextField.placeholder.GetComponent<Text>().text = defaults.inertiaTensorMultiplier.ToString();
        minUserLimitedVelocityTextField.placeholder.GetComponent<Text>().text = defaults.minUserLimitedVelocity.ToString();
        
        UpdateTextFields(game.ShipParameters);
    }

    public void RestoreDefaults() {
        UpdateTextFields(Ship.ShipParameterDefaults);
    }

    public void CopyToClipboard() {
        GUIUtility.systemCopyBuffer = GetFlightParams().ToJsonString();
    }

    public void LoadFromClipboard() {
        string data = GUIUtility.systemCopyBuffer;
        Debug.Log(data);
        var parameters = ShipParameters.FromJsonString(data);
        if (parameters != null) {
            UpdateTextFields(parameters);
        }
    }

    // Update is called once per frame
    public void UpdateTextFields(ShipParameters parameters) {
        massTextField.text = parameters.mass.ToString();
        maxSpeedTextField.text = parameters.maxSpeed.ToString();
        maxBoostSpeedTextField.text = parameters.maxBoostSpeed.ToString();
        maxThrustTextField.text = parameters.maxThrust.ToString();
        dragTextField.text = parameters.drag.ToString();
        angularDragTextField.text = parameters.angularDrag.ToString();
        torqueThrustMultiplierTextField.text = parameters.torqueThrustMultiplier.ToString();
        pitchMultiplierTextField.text = parameters.pitchMultiplier.ToString();
        rollMultiplierTextField.text = parameters.rollMultiplier.ToString();
        yawMultiplierTextField.text = parameters.yawMultiplier.ToString();
        thrustBoostMultiplierTextField.text = parameters.thrustBoostMultiplier.ToString();
        torqueBoostMultiplierTextField.text = parameters.torqueBoostMultiplier.ToString();
        totalBoostTimeTextField.text = parameters.totalBoostTime.ToString();
        totalBoostRotationalTimeTextField.text = parameters.totalBoostRotationalTime.ToString();
        boostRechargeTimeTextField.text = parameters.boostRechargeTime.ToString();
        intertialTensorMultiplierTextField.text = parameters.inertiaTensorMultiplier.ToString();
        minUserLimitedVelocityTextField.text = parameters.minUserLimitedVelocity.ToString();
    }

    public ShipParameters GetFlightParams() {
        var parameters = new ShipParameters();

        parameters.mass = float.Parse(massTextField.text);
        parameters.maxSpeed = float.Parse(maxSpeedTextField.text);
        parameters.maxBoostSpeed = float.Parse(maxBoostSpeedTextField.text);
        parameters.maxThrust = float.Parse(maxThrustTextField.text);
        parameters.drag = float.Parse(massTextField.text);
        parameters.angularDrag = float.Parse(angularDragTextField.text);
        parameters.torqueThrustMultiplier = float.Parse(torqueThrustMultiplierTextField.text);
        parameters.pitchMultiplier = float.Parse(pitchMultiplierTextField.text);
        parameters.rollMultiplier = float.Parse(rollMultiplierTextField.text);
        parameters.yawMultiplier = float.Parse(yawMultiplierTextField.text);
        parameters.thrustBoostMultiplier = float.Parse(thrustBoostMultiplierTextField.text);
        parameters.torqueBoostMultiplier = float.Parse(torqueBoostMultiplierTextField.text);
        parameters.totalBoostTime = float.Parse(totalBoostTimeTextField.text);
        parameters.totalBoostRotationalTime = float.Parse(totalBoostRotationalTimeTextField.text);
        parameters.boostRechargeTime = float.Parse(boostRechargeTimeTextField.text);
        parameters.inertiaTensorMultiplier = float.Parse(intertialTensorMultiplierTextField.text);
        parameters.minUserLimitedVelocity = float.Parse(minUserLimitedVelocityTextField.text);
        
        return parameters;
    }
}
