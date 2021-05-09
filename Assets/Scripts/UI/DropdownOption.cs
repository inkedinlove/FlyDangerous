using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DropdownOption : MonoBehaviour {
    public string Preference = "default-preference";
    [SerializeField]
    private UnityEngine.UI.Dropdown dropdown;

    public string Value {
        get => dropdown.options[dropdown.value].text.ToLower();
        set { dropdown.value = dropdown.options.FindIndex(val => val.text.ToLower() == value.ToLower());  }
    }
}