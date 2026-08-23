using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

using ELink = SREYELINKLib.EyeLink;
using ELinkUtil = SREYELINKLib.EyeLinkUtil;
using Eye = SREYELINKLib.EL_EYE;


/// <summary>
/// Owns the EyeLink connection used by the maze.
///
/// This project uses the EyeLink Developers Kit directly through
/// Interop.SREYELINKLib.dll. It does not use the UDP WebLink transport from the
/// Unity WebLink example. Initialize is safe to call more than once because
/// several level controllers call it from Awake.
/// </summary>
public static class EyeLink
{
    public struct GazeSample
    {
        public double trackerTime;
        public Vector2 trackerPixels;
        public Vector2 unityPixels;
        public float pupilArea;
        public bool isValid;
    }

    /// <summary>
    /// Keep this true while developing without a physical tracker. Set it to
    /// false before testing the dedicated EyeLink network connection.
    /// </summary>
    public static bool openDummy = false;

    /// <summary>Default EyeLink Host address on a dedicated EyeLink network.</summary>
    public static string trackerAddress = "100.1.1.1";

    /// <summary>
    /// EyeLink Host EDF base name. It is normalized to at most eight characters.
    /// Do not include the .edf extension.
    /// </summary>
    public static string edfBaseName = "VMAZE";

    public static bool IsInitialized { get; private set; }
    public static bool IsRecording { get; private set; }
    public static bool HasGazeSample { get; private set; }
    public static GazeSample LatestGazeSample { get; private set; }
    public static string LastDownloadedEdf { get; private set; } = String.Empty;

    public static ELink eyelink;

    private static ELinkUtil eyelinkUtil;
    private static Eye activeEye = Eye.EL_RIGHT;

    private static bool dataFileOpen;
    private static bool shuttingDown;
    private static bool lifecycleCreated;
    private static int eyeLinkTrialNumber;
    private static double lastSampleTime = Double.MinValue;
    private static string remoteEdfName = String.Empty;

    /// <summary>
    /// Unity invokes this once before loading the first scene. This is the only
    /// automatic connection entry point; level controllers only subscribe to
    /// session events.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeFirstScene()
    {
        CreateLifecycleObject();
        Initialize();
    }

    public static void Initialize()
    {
        if (IsInitialized && TryGetEyelinkConnectedStatus()) {
            return;
        }

        if (shuttingDown) {
            return;
        }

        try {
            eyelinkUtil = new ELinkUtil();
            eyelink = new ELink();

            if (openDummy) {
                eyelink.dummyOpen();
                Debug.Log("[EyeLink] Connected in dummy mode.");
            }
            else {
                eyelink.open("100.1.1.1", 0);
                //eyelink.open();
                if (!eyelink.isConnected()) {
                    throw new InvalidOperationException(
                        "The EyeLink Host did not accept a connection at " + trackerAddress + ".");
                }

                Debug.Log("[EyeLink] Connected to " + trackerAddress + ".");
            }

            IsInitialized = true;
            HasGazeSample = false;
            LastDownloadedEdf = String.Empty;

            if (!openDummy) {
                ConfigureTracker();
                OpenDataFile();
            }
        }
        catch (Exception exception) {
            Debug.LogError("[EyeLink] Initialization failed: " + exception.Message);
            Shutdown(false);
        }
    }

    public static bool TryGetEyelinkConnectedStatus()
    {
        try {
            return eyelink != null && eyelink.isConnected();
        }
        catch (Exception) {
            return false;
        }
    }

    /// <summary>Runs the tracker-side camera setup and calibration.</summary>
    public static bool Calibrate()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!EnsureConnected("calibrate")) {
            return false;
        }

        if (openDummy) {
            Debug.Log("[EyeLink] Calibration skipped in dummy mode.");
            return true;
        }

        try {
            StopRecording();
            eyelink.setOfflineMode();
            eyelink.doTrackerSetup();
            eyelink.setOfflineMode();
            Debug.Log("[EyeLink] Tracker setup/calibration finished.");
            return true;
        }
        catch (Exception exception) {
            Debug.LogError("[EyeLink] Calibration failed: " + exception.Message);
            return false;
        }
#else
        return false;
#endif
    }

    public static bool OpenDataFile()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!EnsureConnected("open an EDF file")) {
            return false;
        }

        if (openDummy || dataFileOpen) {
            return true;
        }

        try {
            remoteEdfName = NormalizeEdfBaseName(edfBaseName) + ".edf";
            eyelink.openDataFile(remoteEdfName);
            dataFileOpen = true;
            eyelink.sendMessage("DISPLAY_COORDS 0 0 " +
                Math.Max(0, Screen.width - 1) + " " + Math.Max(0, Screen.height - 1));
            Debug.Log("[EyeLink] Opened Host EDF " + remoteEdfName + ".");
            return true;
        }
        catch (Exception exception) {
            Debug.LogError("[EyeLink] Could not open the EDF: " + exception.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Starts file samples/events and link samples/events. Dummy mode tracks the
    /// same lifecycle but does not create tracker data.
    /// </summary>
    public static bool StartRecording()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (IsRecording) {
            return true;
        }

        if (!EnsureConnected("start recording")) {
            return false;
        }

        if (openDummy) {
            IsRecording = true;
            return true;
        }

        if (!OpenDataFile()) {
            return false;
        }

        try {
            eyelink.setOfflineMode();
            eyelinkUtil.pumpDelay(50);
            eyelink.startRecording(true, true, true, true);
            eyelink.waitForBlockStart(1000, true, true);

            activeEye = (Eye)eyelink.eyeAvailable();
            if (activeEye == Eye.EL_BINOCULAR) {
                // Match the WebLink example: use the right eye for binocular data.
                activeEye = Eye.EL_RIGHT;
            }

            lastSampleTime = Double.MinValue;
            HasGazeSample = false;
            IsRecording = true;
            Debug.Log("[EyeLink] Recording started.");
            return true;
        }
        catch (Exception exception) {
            Debug.LogError("[EyeLink] Could not start recording: " + exception.Message);
            IsRecording = false;
            return false;
        }
#else
        return false;
#endif
    }

    public static void StopRecording()
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!IsRecording) {
            return;
        }

        try {
            if (!openDummy && eyelink != null) {
                eyelinkUtil.pumpDelay(100);
                eyelink.stopRecording();
                eyelink.setOfflineMode();
                Debug.Log("[EyeLink] Recording stopped.");
            }
        }
        catch (Exception exception) {
            Debug.LogWarning("[EyeLink] Recording did not stop cleanly: " + exception.Message);
        }
        finally {
            IsRecording = false;
        }
#endif
    }

    public static bool SendMessage(string message)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (String.IsNullOrWhiteSpace(message) || !EnsureConnected("send a message")) {
            return false;
        }

        try {
            eyelink.sendMessage(SanitizeLine(message));
            Debug.Log("[EyeLink] Sent message: " + message);
            return true;
        }
        catch (Exception exception) {
            Debug.LogWarning("[EyeLink] Could not send message: " + exception.Message);
            return false;
        }
#else
        Debug.Log("[EyeLink] Did not send message on this platform: " + message);
        return false;
#endif
    }

    public static bool SendCommand(string command)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (String.IsNullOrWhiteSpace(command) || !EnsureConnected("send a command")) {
            return false;
        }

        try {
            eyelink.sendCommand(SanitizeLine(command));
            Debug.Log("[EyeLink] Sent command: " + command);
            return true;
        }
        catch (Exception exception) {
            Debug.LogWarning("[EyeLink] Could not send command: " + exception.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>Compatibility wrapper retained for the existing maze code.</summary>
    public static void TryEyemsg_Printf(String msg)
    {
        SendMessage(msg);
    }

    /// <summary>
    /// Gets the newest right-eye (or monocular) link sample. Tracker coordinates
    /// use a top-left origin; Unity coordinates use a bottom-left origin.
    /// </summary>
    public static bool TryGetLatestSample(out GazeSample gazeSample)
    {
        gazeSample = LatestGazeSample;
        if (!IsRecording || openDummy || !TryGetEyelinkConnectedStatus()) {
            return false;
        }

        try {
            SREYELINKLib.Sample sample = eyelink.getNewestSample();
            if (sample == null || sample.time == lastSampleTime) {
                return false;
            }

            if (activeEye == Eye.EL_EYE_NONE) {
                activeEye = (Eye)eyelink.eyeAvailable();
                if (activeEye == Eye.EL_BINOCULAR) {
                    activeEye = Eye.EL_RIGHT;
                }
            }

            if (activeEye == Eye.EL_EYE_NONE) {
                return false;
            }

            float trackerX = sample.get_gx(activeEye);
            float trackerY = sample.get_gy(activeEye);
            float pupilArea = sample.get_pa(activeEye);
            float missingData = (float)SREYELINKLib.EL_CONSTANT.EL_MISSING_DATA;
            bool isValid = trackerX != missingData && trackerY != missingData &&
                !Single.IsNaN(trackerX) && !Single.IsNaN(trackerY) &&
                !Single.IsInfinity(trackerX) && !Single.IsInfinity(trackerY);

            lastSampleTime = sample.time;
            LatestGazeSample = new GazeSample {
                trackerTime = sample.time,
                trackerPixels = new Vector2(trackerX, trackerY),
                unityPixels = new Vector2(trackerX, (Screen.height - 1) - trackerY),
                pupilArea = pupilArea,
                isValid = isValid
            };
            HasGazeSample = true;
            gazeSample = LatestGazeSample;
            return true;
        }
        catch (Exception exception) {
            Debug.LogWarning("[EyeLink] Could not read the newest sample: " + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// WebLink-example-compatible sample shape: tracker X, tracker Y, pupil area.
    /// Returns an empty list when no new valid sample is available.
    /// </summary>
    public static List<float> GetSampleData()
    {
        GazeSample gazeSample;
        if (!TryGetLatestSample(out gazeSample) || !gazeSample.isValid)
        {
            return new List<float>();
        }

        return new List<float> {
            gazeSample.trackerPixels.x,
            gazeSample.trackerPixels.y,
            gazeSample.pupilArea
        };
    }

    /// <summary>
    /// Returns renderer bounds in EyeLink screen coordinates (top-left origin).
    /// This is the transport-independent interest-area helper from the example.
    /// </summary>
    public static Rect GetScreenRectFromGameObject(GameObject gameObject)
    {
        if (gameObject == null)
        {
            throw new ArgumentNullException("gameObject");
        }

        Renderer objectRenderer = gameObject.GetComponent<Renderer>();
        Camera mainCamera = Camera.main;
        if (objectRenderer == null)
        {
            throw new InvalidOperationException(gameObject.name + " does not have a Renderer.");
        }
        if (mainCamera == null)
        {
            throw new InvalidOperationException("The scene does not contain a MainCamera.");
        }

        Vector3 center = objectRenderer.bounds.center;
        Vector3 extent = objectRenderer.bounds.extents;
        Vector2[] screenPoints = {
            mainCamera.WorldToScreenPoint(new Vector3(center.x - extent.x, center.y - extent.y, center.z + extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x + extent.x, center.y - extent.y, center.z + extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x - extent.x, center.y - extent.y, center.z - extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x + extent.x, center.y - extent.y, center.z - extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x - extent.x, center.y + extent.y, center.z + extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x + extent.x, center.y + extent.y, center.z + extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x - extent.x, center.y + extent.y, center.z - extent.z)),
            mainCamera.WorldToScreenPoint(new Vector3(center.x + extent.x, center.y + extent.y, center.z - extent.z))
        };

        Vector2 minimum = screenPoints[0];
        Vector2 maximum = screenPoints[0];
        foreach (Vector2 screenPoint in screenPoints)
        {
            minimum = Vector2.Min(minimum, screenPoint);
            maximum = Vector2.Max(maximum, screenPoint);
        }

        float left = minimum.x;
        float top = Screen.height - maximum.y;
        float right = maximum.x;
        float bottom = Screen.height - minimum.y;
        return new Rect(
            Mathf.Round(left),
            Mathf.Round(top),
            Mathf.Round(right - left),
            Mathf.Round(bottom - top));
    }

    /// <summary>
    /// Handles the events already emitted by LevelController. Each maze task is
    /// treated as one EyeLink trial because TrialStartedTrigger is emitted once
    /// per task/cue.
    /// </summary>
    public static void OnSessionTrigger(SessionTrigger trigger, int triggerValue)
    {
        if (!TryGetEyelinkConnectedStatus())
        {
            Initialize();
        }

        if (!TryGetEyelinkConnectedStatus())
        {
            Debug.LogWarning("[EyeLink] Event ignored because the tracker is not connected.");
            return;
        }

        int flag = (int)trigger + triggerValue + 1;
        switch (trigger)
        {
            case SessionTrigger.TrialStartedTrigger:
                eyeLinkTrialNumber++;
                if (StartRecording())
                {
                    SendMessage("TRIALID " + eyeLinkTrialNumber);
                    SendMessage("Start Trial " + flag);
                }
                break;

            case SessionTrigger.CueOffsetTrigger:
                SendMessage("Cue Offset " + flag);
                break;

            case SessionTrigger.TrialEndedTrigger:
                SendMessage("End Trial " + flag);
                SendMessage("TRIAL_RESULT 0");
                StopRecording();
                break;

            case SessionTrigger.TimeoutTrigger:
                SendMessage("Timeout " + flag);
                SendMessage("TRIAL_RESULT 1");
                StopRecording();
                break;

            case SessionTrigger.ExperimentVersionTrigger:
                SendMessage("Trigger Version " + ((int)trigger + GameController.versionNum));
                break;
        }
    }

    /// <summary>
    /// Stops recording, closes the Host EDF, optionally downloads it, and closes
    /// the tracker link. The lifecycle helper invokes this when Play Mode or the
    /// standalone application exits.
    /// </summary>
    public static void Shutdown(bool downloadEdf = true)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (shuttingDown || eyelink == null) {
            return;
        }

        shuttingDown = true;
        try {
            StopRecording();

            if (!openDummy && dataFileOpen) {
                eyelink.setOfflineMode();
                if (eyelinkUtil != null) {
                    eyelinkUtil.pumpDelay(500);
                }

                eyelink.closeDataFile();
                dataFileOpen = false;

                if (downloadEdf) {
                    string localEdfPath = BuildLocalEdfPath();
                    eyelink.receiveDataFile(remoteEdfName, localEdfPath);
                    LastDownloadedEdf = localEdfPath;
                    Debug.Log("[EyeLink] Downloaded EDF to " + localEdfPath + ".");
                }
            }

            eyelink.close();
        }
        catch (Exception exception) {
            Debug.LogWarning("[EyeLink] Shutdown was not completely clean: " + exception.Message);
        }
        finally {
            eyelink = null;
            eyelinkUtil = null;
            activeEye = Eye.EL_EYE_NONE;
            IsInitialized = false;
            IsRecording = false;
            HasGazeSample = false;
            dataFileOpen = false;
            shuttingDown = false;
        }
#else
        IsInitialized = false;
        IsRecording = false;
        HasGazeSample = false;
#endif
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private static void ConfigureTracker() {
        int right = Math.Max(0, Screen.width - 1);
        int bottom = Math.Max(0, Screen.height - 1);

        eyelink.setOfflineMode();
        eyelink.sendCommand("screen_pixel_coords = 0 0 " + right + " " + bottom);
        eyelink.sendCommand("calibration_type = HV9");
        eyelink.sendCommand(
            "file_event_filter = LEFT,RIGHT,FIXATION,SACCADE,BLINK,MESSAGE,BUTTON,INPUT");
        eyelink.sendCommand(
            "file_sample_data = LEFT,RIGHT,GAZE,HREF,RAW,AREA,GAZERES,BUTTON,STATUS,INPUT");
        eyelink.sendCommand(
            "link_sample_data = LEFT,RIGHT,GAZE,GAZERES,AREA,STATUS,INPUT");
        eyelinkUtil.pumpDelay(50);
    }
#endif

    private static bool EnsureConnected(string action)
    {
        if (!TryGetEyelinkConnectedStatus())
        {
            Initialize();
        }

        if (TryGetEyelinkConnectedStatus())
        {
            return true;
        }

        Debug.LogWarning("[EyeLink] Cannot " + action + " because EyeLink is not connected.");
        return false;
    }

    private static void CreateLifecycleObject()
    {
        if (lifecycleCreated)
        {
            return;
        }

        GameObject lifecycleObject = new GameObject("EyeLink Lifecycle");
        lifecycleObject.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(lifecycleObject);
        lifecycleObject.AddComponent<EyeLinkLifecycle>();
        lifecycleCreated = true;
    }

    private static string NormalizeEdfBaseName(string requestedName)
    {
        string source = Path.GetFileNameWithoutExtension(requestedName ?? String.Empty).ToUpperInvariant();
        string normalized = String.Empty;
        foreach (char character in source)
        {
            bool isLetterOrNumber = character >= 'A' && character <= 'Z' ||
                character >= '0' && character <= '9';
            if (isLetterOrNumber || character == '_' && normalized.Length > 0)
            {
                normalized += character;
            }
            if (normalized.Length == 8)
            {
                break;
            }
        }

        return String.IsNullOrEmpty(normalized) ? "VMAZE" : normalized;
    }

    private static string BuildLocalEdfPath()
    {
        string outputFolder = Path.Combine(Application.persistentDataPath, "EyeLinkData");
        Directory.CreateDirectory(outputFolder);
        string localName = Path.GetFileNameWithoutExtension(remoteEdfName) + "_" +
            DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".edf";
        return Path.Combine(outputFolder, localName);
    }

    private static string SanitizeLine(string value)
    {
        return (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}

/// <summary>
/// Gives the static EyeLink service a Unity shutdown callback. It is created at
/// runtime and persists across scene loads; it does not need to be placed in a
/// scene manually.
/// </summary>
internal sealed class EyeLinkLifecycle : MonoBehaviour
{
    private void OnApplicationQuit()
    {
        EyeLink.Shutdown();
    }

    private void OnDestroy()
    {
        EyeLink.Shutdown();
    }
}
