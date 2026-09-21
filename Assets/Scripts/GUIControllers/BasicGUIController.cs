using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// This class contains helper methods to help with manipulating the GUI.
/// </summary>
public abstract class BasicGUIController : MonoBehaviour {
    private static readonly Color errorColor = new Color(1, 0.35f, 0.35f);

    protected void SetInputFieldValid(InputField field, bool valid) {
        if (field == null || field.textComponent == null) {
            return;
        }

        field.textComponent.color = valid ? Color.green : errorColor;
    }

    protected void SetInputFieldNeutral(InputField field) {
        if (field == null || field.textComponent == null) {
            return;
        }

        field.textComponent.color = Color.black;
    }
}

/// <summary>
/// Abstract class to automatically provide a function to update the GUI.
/// </summary>
public abstract class DataGUIController : BasicGUIController, IDataChangedListener {
    protected virtual void Start() {
        UpdateSettingsGUI();
    }

    public abstract void UpdateSettingsGUI();
}

/// <summary>
/// Interface to notify class of any data changes.
/// </summary>
public interface IDataChangedListener {
    void UpdateSettingsGUI();
}
