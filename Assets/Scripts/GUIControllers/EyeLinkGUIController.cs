using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Connects the EyeLink settings panel to the tracker and level.</summary>
public sealed class EyeLinkGUIController : BasicGUIController
{
    [Header("Settings")]
    [SerializeField]
    private FileSelector localEdfPathField = null;

    [SerializeField]
    private Toggle fixedCueDurationToggle = null;

    [SerializeField]
    private InputField fixedCueDurationMillisecondsField = null;

    [SerializeField]
    private Toggle cueGazeDurationToggle = null;

    [SerializeField]
    private InputField requiredCueGazeMillisecondsField = null;

    [SerializeField]
    private Toggle cueGazeEventCountToggle = null;

    [SerializeField]
    private InputField requiredCueGazeEventCountField = null;

    [SerializeField]
    private LevelController levelController = null;

    [SerializeField]
    private LiveGazeRaycaster liveGazeRaycaster = null;

    [Header("Tracker actions")]
    [SerializeField]
    private Button calibrationButton = null;

    [SerializeField]
    private Button driftDetectionButton = null;

    [SerializeField]
    private Button validationButton = null;

    [SerializeField]
    private Button cameraSetupButton = null;

    private void Awake()
    {
        if (localEdfPathField == null) {
            localEdfPathField = GetComponentInChildren<FileSelector>(true);
        }

        if (localEdfPathField != null) {
            localEdfPathField.OnPathSelected.AddListener(OnLocalEdfPathSelected);
        }

        AddInputFieldListeners(
            fixedCueDurationMillisecondsField,
            OnFixedCueDurationMillisecondsChanged,
            OnFixedCueDurationMillisecondsSubmitted);
        AddInputFieldListeners(
            requiredCueGazeMillisecondsField,
            OnRequiredCueGazeMillisecondsChanged,
            OnRequiredCueGazeMillisecondsSubmitted);
        AddInputFieldListeners(
            requiredCueGazeEventCountField,
            OnRequiredCueGazeEventCountChanged,
            OnRequiredCueGazeEventCountSubmitted);

        if (fixedCueDurationToggle != null) {
            fixedCueDurationToggle.onValueChanged.AddListener(
                OnFixedCueDurationToggled);
        }

        if (cueGazeDurationToggle != null) {
            cueGazeDurationToggle.onValueChanged.AddListener(
                OnCueGazeDurationToggled);
        }

        if (cueGazeEventCountToggle != null) {
            cueGazeEventCountToggle.onValueChanged.AddListener(
                OnCueGazeEventCountToggled);
        }

        AddButtonListener(calibrationButton, OnCalibrationClicked);
        AddButtonListener(driftDetectionButton, OnDriftDetectionClicked);
        AddButtonListener(validationButton, OnValidationClicked);
        AddButtonListener(cameraSetupButton, OnCameraSetupClicked);
    }

    private void Start()
    {
        if (levelController == null) {
            levelController = FindObjectOfType<LevelController>();
        }

        if (liveGazeRaycaster == null) {
            liveGazeRaycaster = FindObjectOfType<LiveGazeRaycaster>();
        }

        if (localEdfPathField != null) {
            localEdfPathField.text = EyeLink.LocalEdfPath;
            localEdfPathField.defaultPath = Path.GetDirectoryName(EyeLink.LocalEdfPath);
            SetInputFieldNeutral(localEdfPathField);
        }

        if (levelController == null) {
            return;
        }

        SetMillisecondsField(
            fixedCueDurationMillisecondsField,
            levelController.FixedCueDurationMilliseconds);
        SetMillisecondsField(
            requiredCueGazeMillisecondsField,
            levelController.RequiredCueGazeMilliseconds);

        if (requiredCueGazeEventCountField != null) {
            requiredCueGazeEventCountField.text =
                levelController.RequiredCueGazeEventCount.ToString(
                    CultureInfo.InvariantCulture);
            SetInputFieldNeutral(requiredCueGazeEventCountField);
        }

        SetToggleValue(fixedCueDurationToggle, levelController.UseFixedCueDuration);
        SetToggleValue(cueGazeDurationToggle, levelController.UseCueGazeDuration);
        SetToggleValue(cueGazeEventCountToggle, levelController.UseCueGazeEventCount);

        OnFixedCueDurationToggled(levelController.UseFixedCueDuration);
        OnCueGazeDurationToggled(levelController.UseCueGazeDuration);
        OnCueGazeEventCountToggled(levelController.UseCueGazeEventCount);
    }

    private void OnDestroy()
    {
        if (localEdfPathField != null) {
            localEdfPathField.OnPathSelected.RemoveListener(OnLocalEdfPathSelected);
        }

        RemoveInputFieldListeners(
            fixedCueDurationMillisecondsField,
            OnFixedCueDurationMillisecondsChanged,
            OnFixedCueDurationMillisecondsSubmitted);
        RemoveInputFieldListeners(
            requiredCueGazeMillisecondsField,
            OnRequiredCueGazeMillisecondsChanged,
            OnRequiredCueGazeMillisecondsSubmitted);
        RemoveInputFieldListeners(
            requiredCueGazeEventCountField,
            OnRequiredCueGazeEventCountChanged,
            OnRequiredCueGazeEventCountSubmitted);

        if (fixedCueDurationToggle != null) {
            fixedCueDurationToggle.onValueChanged.RemoveListener(
                OnFixedCueDurationToggled);
        }

        if (cueGazeDurationToggle != null) {
            cueGazeDurationToggle.onValueChanged.RemoveListener(
                OnCueGazeDurationToggled);
        }

        if (cueGazeEventCountToggle != null) {
            cueGazeEventCountToggle.onValueChanged.RemoveListener(
                OnCueGazeEventCountToggled);
        }

        RemoveButtonListener(calibrationButton, OnCalibrationClicked);
        RemoveButtonListener(driftDetectionButton, OnDriftDetectionClicked);
        RemoveButtonListener(validationButton, OnValidationClicked);
        RemoveButtonListener(cameraSetupButton, OnCameraSetupClicked);
    }

    private void OnLocalEdfPathSelected(string selectedPath)
    {
        string resolvedPath;
        bool isValid = EyeLink.TrySetLocalEdfPath(selectedPath, out resolvedPath);
        SetInputFieldValid(localEdfPathField, isValid);

        if (isValid) {
            localEdfPathField.text = resolvedPath;
            localEdfPathField.defaultPath = Path.GetDirectoryName(resolvedPath);
        }
    }

    private void OnFixedCueDurationMillisecondsChanged(string unused)
    {
        SetInputFieldNeutral(fixedCueDurationMillisecondsField);
    }

    private void OnFixedCueDurationMillisecondsSubmitted(string text)
    {
        float milliseconds = 0f;
        bool isValid = levelController != null &&
            TryParseNonNegativeFloat(text, out milliseconds);

        if (isValid) {
            levelController.FixedCueDurationMilliseconds = milliseconds;
        }

        SetInputFieldValid(fixedCueDurationMillisecondsField, isValid);
    }

    private void OnRequiredCueGazeMillisecondsChanged(string unused)
    {
        SetInputFieldNeutral(requiredCueGazeMillisecondsField);
    }

    private void OnRequiredCueGazeMillisecondsSubmitted(string text)
    {
        float milliseconds = 0f;
        bool isValid = levelController != null &&
            TryParseNonNegativeFloat(text, out milliseconds);

        if (isValid) {
            levelController.RequiredCueGazeMilliseconds = milliseconds;
        }

        SetInputFieldValid(requiredCueGazeMillisecondsField, isValid);
    }

    private void OnRequiredCueGazeEventCountChanged(string unused)
    {
        SetInputFieldNeutral(requiredCueGazeEventCountField);
    }

    private void OnRequiredCueGazeEventCountSubmitted(string text)
    {
        int eventCount = 0;
        bool isValid = levelController != null &&
            int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out eventCount) &&
            eventCount >= 0;

        if (isValid) {
            levelController.RequiredCueGazeEventCount = eventCount;
        }

        SetInputFieldValid(requiredCueGazeEventCountField, isValid);
    }

    private void OnFixedCueDurationToggled(bool value)
    {
        if (levelController != null) {
            levelController.UseFixedCueDuration = value;
        }

        SetInputFieldInteractable(fixedCueDurationMillisecondsField, value);
    }

    private void OnCueGazeDurationToggled(bool value)
    {
        if (levelController != null) {
            levelController.UseCueGazeDuration = value;
        }

        SetInputFieldInteractable(requiredCueGazeMillisecondsField, value);
    }

    private void OnCueGazeEventCountToggled(bool value)
    {
        if (levelController != null) {
            levelController.UseCueGazeEventCount = value;
        }

        SetInputFieldInteractable(requiredCueGazeEventCountField, value);
    }

    public void OnCalibrationClicked()
    {
        EyeLink.Calibration();
    }

    public void OnDriftDetectionClicked()
    {
        EyeLink.DriftDetection();
    }

    public void OnValidationClicked()
    {
        EyeLink.Validation();
    }

    public void OnCameraSetupClicked()
    {
        EyeLink.CameraSetup();
    }

    public void OnDrawGazeCircleToggled(bool value)
    {
        if (liveGazeRaycaster == null) {
            liveGazeRaycaster = FindObjectOfType<LiveGazeRaycaster>();
        }

        if (liveGazeRaycaster != null) {
            liveGazeRaycaster.ShowGazeArea = value;
        }
    }

    private static void AddButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.AddListener(action);
        }
    }

    private static void RemoveButtonListener(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null) {
            button.onClick.RemoveListener(action);
        }
    }

    private static void AddInputFieldListeners(
        InputField field,
        UnityEngine.Events.UnityAction<string> changedAction,
        UnityEngine.Events.UnityAction<string> submittedAction)
    {
        if (field != null) {
            field.onValueChanged.AddListener(changedAction);
            field.onEndEdit.AddListener(submittedAction);
        }
    }

    private static void RemoveInputFieldListeners(
        InputField field,
        UnityEngine.Events.UnityAction<string> changedAction,
        UnityEngine.Events.UnityAction<string> submittedAction)
    {
        if (field != null) {
            field.onValueChanged.RemoveListener(changedAction);
            field.onEndEdit.RemoveListener(submittedAction);
        }
    }

    private void SetMillisecondsField(InputField field, float milliseconds)
    {
        if (field != null) {
            field.text = milliseconds.ToString("0", CultureInfo.InvariantCulture);
            SetInputFieldNeutral(field);
        }
    }

    private static bool TryParseNonNegativeFloat(string text, out float value)
    {
        return float.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value) && value >= 0f;
    }

    private static void SetToggleValue(Toggle toggle, bool value)
    {
        if (toggle != null) {
            toggle.isOn = value;
        }
    }

    private static void SetInputFieldInteractable(InputField field, bool value)
    {
        if (field != null) {
            field.interactable = value;
        }
    }
}
