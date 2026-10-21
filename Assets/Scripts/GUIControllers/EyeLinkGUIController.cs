using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Connects the EyeLink settings panel to the tracker and level.</summary>
public sealed class EyeLinkGUIController : ConfigurableComponent
{
    [Serializable]
    public sealed class Settings : ComponentSettings
    {
        public string localEdfPath;
        public bool useFixedCueDuration;
        public float fixedCueDurationMilliseconds;
        public bool useCueGazeDuration;
        public float requiredCueGazeMilliseconds;
        public bool useCueGazeEventCount;
        public int requiredCueGazeEventCount;
        public bool drawGazeCircle;
        public bool rewardDuringCalibration;

        [OptionalField(VersionAdded = 2)]
        public int calibrationMonitorIndex;

        [OptionalField(VersionAdded = 2)]
        public bool hasCalibrationMonitorIndex;

        public Settings(
            string localEdfPath,
            bool useFixedCueDuration,
            float fixedCueDurationMilliseconds,
            bool useCueGazeDuration,
            float requiredCueGazeMilliseconds,
            bool useCueGazeEventCount,
            int requiredCueGazeEventCount,
            bool drawGazeCircle,
            bool rewardDuringCalibration,
            int calibrationMonitorIndex)
        {
            this.localEdfPath = localEdfPath;
            this.useFixedCueDuration = useFixedCueDuration;
            this.fixedCueDurationMilliseconds = fixedCueDurationMilliseconds;
            this.useCueGazeDuration = useCueGazeDuration;
            this.requiredCueGazeMilliseconds = requiredCueGazeMilliseconds;
            this.useCueGazeEventCount = useCueGazeEventCount;
            this.requiredCueGazeEventCount = requiredCueGazeEventCount;
            this.drawGazeCircle = drawGazeCircle;
            this.rewardDuringCalibration = rewardDuringCalibration;
            this.calibrationMonitorIndex = calibrationMonitorIndex;
            hasCalibrationMonitorIndex = true;
        }
    }

    private static readonly Color ErrorColor = new Color(1f, 0.35f, 0.35f);

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

    [SerializeField]
    private Dropdown monitorsListDropDown = null;

    [Header("Tracker actions")]
    [SerializeField]
    private Button calibrationButton = null;

    [SerializeField]
    private Button driftDetectionButton = null;

    [SerializeField]
    private Button validationButton = null;

    [SerializeField]
    private Button cameraSetupButton = null;

    [SerializeField]
    private Toggle drawGazeCircleToggle = null;

    [SerializeField]
    private Toggle calibrationRewardToggle = null;

    [SerializeField]
    private RewardsController rewardsController = null;

    private bool calibrationRewardsEnabled;
    private bool isRefreshingSettingsUi;

    protected override void Awake()
    {
        ResolveReferences();
        PopulateMonitorOptions();

        // Apply defaults and register with the settings manager before wiring
        // UI callbacks, so initialization does not look like user input.
        base.Awake();

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

        if (calibrationRewardToggle != null) {
            calibrationRewardToggle.onValueChanged.AddListener(
                OnCalibrationRewardToggled);
        }

        if (drawGazeCircleToggle != null) {
            drawGazeCircleToggle.onValueChanged.AddListener(
                OnDrawGazeCircleToggled);
        }

        if (monitorsListDropDown != null) {
            monitorsListDropDown.onValueChanged.AddListener(
                OnCalibrationMonitorChanged);
        }

        AddButtonListener(calibrationButton, OnCalibrationClicked);
        AddButtonListener(driftDetectionButton, OnDriftDetectionClicked);
        AddButtonListener(validationButton, OnValidationClicked);
        AddButtonListener(cameraSetupButton, OnCameraSetupClicked);
    }

    private void Start()
    {
        ResolveReferences();
        RefreshSettingsUi();
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

        if (calibrationRewardToggle != null) {
            calibrationRewardToggle.onValueChanged.RemoveListener(
                OnCalibrationRewardToggled);
        }

        if (drawGazeCircleToggle != null) {
            drawGazeCircleToggle.onValueChanged.RemoveListener(
                OnDrawGazeCircleToggled);
        }

        if (monitorsListDropDown != null) {
            monitorsListDropDown.onValueChanged.RemoveListener(
                OnCalibrationMonitorChanged);
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
            WriteEyeLinkStatus("EDF output path updated.");
        }
        else {
            WriteEyeLinkError("The selected EDF output path is invalid.");
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
        if (isRefreshingSettingsUi) {
            return;
        }

        if (levelController != null) {
            levelController.UseFixedCueDuration = value;
        }

        SetInputFieldInteractable(fixedCueDurationMillisecondsField, value);
    }

    private void OnCueGazeDurationToggled(bool value)
    {
        if (isRefreshingSettingsUi) {
            return;
        }

        if (levelController != null) {
            levelController.UseCueGazeDuration = value;
        }

        SetInputFieldInteractable(requiredCueGazeMillisecondsField, value);
    }

    private void OnCueGazeEventCountToggled(bool value)
    {
        if (isRefreshingSettingsUi) {
            return;
        }

        if (levelController != null) {
            levelController.UseCueGazeEventCount = value;
        }

        SetInputFieldInteractable(requiredCueGazeEventCountField, value);
    }

    public void OnCalibrationClicked()
    {
        Action<int> rewardCallback = calibrationRewardsEnabled
            ? (Action<int>)OnCalibrationPointAccepted
            : null;
        EyeLink.Calibration(rewardCallback);
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
        if (isRefreshingSettingsUi) {
            return;
        }

        if (liveGazeRaycaster == null) {
            liveGazeRaycaster = FindObjectOfType<LiveGazeRaycaster>();
        }

        if (liveGazeRaycaster != null) {
            liveGazeRaycaster.ShowGazeArea = value;
        }
    }

    private void OnCalibrationMonitorChanged(int monitorIndex)
    {
        if (isRefreshingSettingsUi) {
            return;
        }

        EyeLink.CalibrationMonitorIndex = monitorIndex;
        WriteEyeLinkStatus(
            "Calibration display set to " + GetMonitorName(monitorIndex) + ".");
    }

    private void OnCalibrationRewardToggled(bool value)
    {
        if (isRefreshingSettingsUi) {
            return;
        }

        calibrationRewardsEnabled = value;
        WriteEyeLinkStatus(
            value
                ? "Calibration rewards enabled."
                : "Calibration rewards disabled.");
    }

    private void OnCalibrationPointAccepted(int pointNumber)
    {
        if (rewardsController == null) {
            rewardsController = FindObjectOfType<RewardsController>();
        }

        if (rewardsController == null) {
            WriteEyeLinkError(
                "Calibration point " + pointNumber +
                " was accepted, but no RewardsController was found.");
            return;
        }

        if (rewardsController.CalibrationReward()) {
            WriteEyeLinkStatus(
                "Calibration reward sent for point " + pointNumber + ".");
        }
        else {
            WriteEyeLinkError(
                "Calibration point " + pointNumber +
                " was accepted, but the reward port could not be opened.");
        }
    }

    public override Type GetSettingsType()
    {
        return typeof(Settings);
    }

    public override ComponentSettings GetDefaultSettings()
    {
        return new Settings(
            String.Empty,
            true,
            1000f,
            false,
            1000f,
            true,
            1,
            false,
            false,
            EyeLink.GetDefaultCalibrationMonitorIndex());
    }

    public override ComponentSettings GetCurrentSettings()
    {
        ResolveReferences();

        return new Settings(
            EyeLink.LocalEdfPath,
            levelController == null || levelController.UseFixedCueDuration,
            levelController == null
                ? 1000f
                : levelController.FixedCueDurationMilliseconds,
            levelController != null && levelController.UseCueGazeDuration,
            levelController == null
                ? 1000f
                : levelController.RequiredCueGazeMilliseconds,
            levelController == null || levelController.UseCueGazeEventCount,
            levelController == null
                ? 1
                : levelController.RequiredCueGazeEventCount,
            liveGazeRaycaster != null && liveGazeRaycaster.ShowGazeArea,
            calibrationRewardsEnabled,
            EyeLink.CalibrationMonitorIndex);
    }

    protected override void ApplySettings(ComponentSettings loadedSettings)
    {
        Settings settings = loadedSettings as Settings;
        if (settings == null) {
            WriteEyeLinkError("The saved EyeLink settings have an invalid format.");
            return;
        }

        ResolveReferences();
        calibrationRewardsEnabled = settings.rewardDuringCalibration;
        EyeLink.CalibrationMonitorIndex = settings.hasCalibrationMonitorIndex
            ? settings.calibrationMonitorIndex
            : EyeLink.GetDefaultCalibrationMonitorIndex();

        if (!String.IsNullOrEmpty(settings.localEdfPath)) {
            string unused;
            if (!EyeLink.TrySetLocalEdfPath(settings.localEdfPath, out unused)) {
                WriteEyeLinkError(
                    "The saved EDF path is unavailable; the current path was kept.");
            }
        }

        if (levelController != null) {
            levelController.UseFixedCueDuration = settings.useFixedCueDuration;
            levelController.FixedCueDurationMilliseconds =
                Math.Max(0f, settings.fixedCueDurationMilliseconds);
            levelController.UseCueGazeDuration = settings.useCueGazeDuration;
            levelController.RequiredCueGazeMilliseconds =
                Math.Max(0f, settings.requiredCueGazeMilliseconds);
            levelController.UseCueGazeEventCount = settings.useCueGazeEventCount;
            levelController.RequiredCueGazeEventCount =
                Math.Max(0, settings.requiredCueGazeEventCount);
        }

        if (liveGazeRaycaster != null) {
            liveGazeRaycaster.ShowGazeArea = settings.drawGazeCircle;
        }

        RefreshSettingsUi();
    }

    private void ResolveReferences()
    {
        if (levelController == null) {
            levelController = FindObjectOfType<LevelController>();
        }

        if (liveGazeRaycaster == null) {
            liveGazeRaycaster = FindObjectOfType<LiveGazeRaycaster>();
        }

        if (rewardsController == null) {
            rewardsController = FindObjectOfType<RewardsController>();
        }

        if (localEdfPathField == null) {
            localEdfPathField = GetComponentInChildren<FileSelector>(true);
        }

        if (monitorsListDropDown == null) {
            Dropdown[] dropdowns = GetComponentsInChildren<Dropdown>(true);
            foreach (Dropdown dropdown in dropdowns) {
                if (dropdown.name == "MonitorsListDropDown" ||
                    dropdown.name == "MonitorListDropDown") {
                    monitorsListDropDown = dropdown;
                    break;
                }
            }
        }

        if (monitorsListDropDown == null) {
            GameObject dropdownObject = GameObject.Find("MonitorsListDropDown");
            if (dropdownObject == null) {
                dropdownObject = GameObject.Find("MonitorListDropDown");
            }

            if (dropdownObject != null) {
                monitorsListDropDown = dropdownObject.GetComponent<Dropdown>();
            }
        }

        if (calibrationRewardToggle == null) {
            Toggle[] toggles = GetComponentsInChildren<Toggle>(true);
            foreach (Toggle toggle in toggles) {
                if (toggle.name == "CalibrationRewardToggle") {
                    calibrationRewardToggle = toggle;
                    break;
                }
            }
        }

        if (calibrationRewardToggle == null) {
            GameObject toggleObject = GameObject.Find("CalibrationRewardToggle");
            if (toggleObject != null) {
                calibrationRewardToggle = toggleObject.GetComponent<Toggle>();
            }
        }
    }

    private void RefreshSettingsUi()
    {
        isRefreshingSettingsUi = true;
        try {
            if (localEdfPathField != null) {
                localEdfPathField.text = EyeLink.LocalEdfPath;
                localEdfPathField.defaultPath =
                    Path.GetDirectoryName(EyeLink.LocalEdfPath);
                SetInputFieldNeutral(localEdfPathField);
            }

            if (monitorsListDropDown != null) {
                PopulateMonitorOptions();
                monitorsListDropDown.value = EyeLink.CalibrationMonitorIndex;
                monitorsListDropDown.RefreshShownValue();
            }

            if (levelController != null) {
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

                SetToggleValue(
                    fixedCueDurationToggle,
                    levelController.UseFixedCueDuration);
                SetToggleValue(
                    cueGazeDurationToggle,
                    levelController.UseCueGazeDuration);
                SetToggleValue(
                    cueGazeEventCountToggle,
                    levelController.UseCueGazeEventCount);
                SetInputFieldInteractable(
                    fixedCueDurationMillisecondsField,
                    levelController.UseFixedCueDuration);
                SetInputFieldInteractable(
                    requiredCueGazeMillisecondsField,
                    levelController.UseCueGazeDuration);
                SetInputFieldInteractable(
                    requiredCueGazeEventCountField,
                    levelController.UseCueGazeEventCount);
            }

            SetToggleValue(calibrationRewardToggle, calibrationRewardsEnabled);
            SetToggleValue(
                drawGazeCircleToggle,
                liveGazeRaycaster != null && liveGazeRaycaster.ShowGazeArea);
        }
        finally {
            isRefreshingSettingsUi = false;
        }
    }

    private void PopulateMonitorOptions()
    {
        if (monitorsListDropDown == null) {
            return;
        }

        monitorsListDropDown.ClearOptions();
        monitorsListDropDown.AddOptions(
            new System.Collections.Generic.List<string>(
                EyeLink.GetCalibrationMonitorNames()));
    }

    private static string GetMonitorName(int monitorIndex)
    {
        string[] monitorNames = EyeLink.GetCalibrationMonitorNames();
        return monitorIndex >= 0 && monitorIndex < monitorNames.Length
            ? monitorNames[monitorIndex]
            : "Display " + (monitorIndex + 1);
    }

    private static void WriteEyeLinkStatus(string message)
    {
        string text = "[EyeLink] " + message;
        if (Console.Instance != null) {
            Console.Write(text);
        }
        else {
            Debug.Log(text);
        }
    }

    private static void WriteEyeLinkError(string message)
    {
        string text = "[EyeLink] " + message;
        if (Console.Instance != null) {
            Console.WriteError(text);
        }
        else {
            Debug.LogError(text);
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

    private static void SetInputFieldValid(InputField field, bool valid)
    {
        if (field == null || field.textComponent == null) {
            return;
        }

        field.textComponent.color = valid ? Color.green : ErrorColor;
    }

    private static void SetInputFieldNeutral(InputField field)
    {
        if (field == null || field.textComponent == null) {
            return;
        }

        field.textComponent.color = Color.black;
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
