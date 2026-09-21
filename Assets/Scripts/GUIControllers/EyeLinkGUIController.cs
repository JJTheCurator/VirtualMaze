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
    private InputField requiredCueGazeMillisecondsField = null;

    [SerializeField]
    private LevelController levelController = null;

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

        if (requiredCueGazeMillisecondsField != null) {
            requiredCueGazeMillisecondsField.onValueChanged.AddListener(
                OnRequiredCueGazeMillisecondsChanged);
            requiredCueGazeMillisecondsField.onEndEdit.AddListener(
                OnRequiredCueGazeMillisecondsSubmitted);
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

        if (localEdfPathField != null) {
            localEdfPathField.text = EyeLink.LocalEdfPath;
            localEdfPathField.defaultPath = Path.GetDirectoryName(EyeLink.LocalEdfPath);
            SetInputFieldNeutral(localEdfPathField);
        }

        if (requiredCueGazeMillisecondsField != null && levelController != null) {
            requiredCueGazeMillisecondsField.text =
                levelController.RequiredCueGazeMilliseconds.ToString(
                    "0", CultureInfo.InvariantCulture);
            SetInputFieldNeutral(requiredCueGazeMillisecondsField);
        }
    }

    private void OnDestroy()
    {
        if (localEdfPathField != null) {
            localEdfPathField.OnPathSelected.RemoveListener(OnLocalEdfPathSelected);
        }

        if (requiredCueGazeMillisecondsField != null) {
            requiredCueGazeMillisecondsField.onValueChanged.RemoveListener(
                OnRequiredCueGazeMillisecondsChanged);
            requiredCueGazeMillisecondsField.onEndEdit.RemoveListener(
                OnRequiredCueGazeMillisecondsSubmitted);
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

    private void OnRequiredCueGazeMillisecondsChanged(string unused)
    {
        SetInputFieldNeutral(requiredCueGazeMillisecondsField);
    }

    private void OnRequiredCueGazeMillisecondsSubmitted(string text)
    {
        float milliseconds = 0f;
        bool isValid = levelController != null &&
            float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out milliseconds) &&
            milliseconds >= 0f;

        if (isValid) {
            levelController.RequiredCueGazeMilliseconds = milliseconds;
        }

        SetInputFieldValid(requiredCueGazeMillisecondsField, isValid);
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
}
