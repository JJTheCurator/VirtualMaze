using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class MyDropdownScript : Dropdown {
    //drag and drop in Unity GUI
    public InputField inputField;

    // Keep a non-setting option at index zero. Unity's Dropdown starts at zero,
    // so without this sentinel choosing the first real setting does not change
    // the value and therefore does not raise onValueChanged.
    private readonly List<string> noSelection = new List<string> { "" };

    public string text {
        get => inputField.text;
        set => inputField.text = value;
    }

    private bool isShown = false;

    protected override void Awake() {
        base.Awake();
        onValueChanged.AddListener(new UnityAction<int>(UpdateInputField));
        
    }

    public void UpdateInputField(int selected) {
        if (selected <= 0 || selected >= options.Count) {
            inputField.text = "";
            return;
        }

        string optionTitle = options[selected].text;
        inputField.text = optionTitle;

        //prevents UI bug
        Hide();
    }

    public void UpdateOptions(List<string> newOptions, int selectItem) {
        ClearOptions();
        AddOptions(noSelection);
        AddOptions(newOptions);

        int dropdownIndex = selectItem >= 0 && selectItem < newOptions.Count
            ? selectItem + 1
            : 0;
        value = dropdownIndex;
        UpdateInputField(dropdownIndex);

        RefreshShownValue();
    }

    protected override GameObject CreateBlocker(Canvas rootCanvas) {
        //override blocker's Hide to also call this custom Hide()
        GameObject blocker = base.CreateBlocker(rootCanvas);
        Button b = blocker.GetComponent<Button>();
        b.onClick.AddListener(Hide);
        return blocker;
    }

    private void ToggleDropdown() {
        if (!isShown) {
            Show();
        }
        else {
            Hide();
        }
    }

    public override void OnPointerClick(PointerEventData eventData) {
        ToggleDropdown();
    }

    //Wrapper Methods to implement custom UI behaviours
    public new void Show() {
        base.Show();
        isShown = true;
    }

    public new void Hide() {
        base.Hide();
        isShown = false;
    }

}
