using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

using ELink = SREYELINKLib.EyeLink;
using ELinkUtil = SREYELINKLib.EyeLinkUtil;
using Eye = SREYELINKLib.EL_EYE;
using Eltype = SREYELINKLib.EL_DATA_TYPE;
//using ALLF_DATA = SREYELINKLib.ALLF_DATA;


/// <summary>
/// Owns the EyeLink connection, recording lifecycle, and live gaze data used by
/// the maze.
///
/// This project uses the EyeLink Developers Kit directly through
/// Interop.SREYELINKLib.dll. It does not use the UDP WebLink transport from the
/// Unity WebLink example. Unity starts the service before the first scene and a
/// hidden lifecycle object shuts it down when Play Mode or the application ends.
/// <see cref="Initialize"/> is idempotent so callers may also use it to recover a
/// lost connection.
///
/// Tracker pixels use a top-left origin. Unity pixels exposed by this class use
/// a bottom-left origin. Live queue access and the EyeLink COM objects are
/// expected to remain on Unity's main thread.
/// </summary>
public static class EyeLink
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
#endif

    /// <summary>A gaze sample expressed in both tracker and Unity coordinates.</summary>
    public struct GazeSample
    {
        /// <summary>EyeLink timestamp in milliseconds.</summary>
        public double trackerTime;

        /// <summary>Display pixel position with the tracker's top-left origin.</summary>
        public Vector2 trackerPixels;

        /// <summary>Display pixel position with Unity's bottom-left origin.</summary>
        public Vector2 unityPixels;

        /// <summary>Pupil area reported by the active eye.</summary>
        public float pupilArea;

        /// <summary>Whether both gaze coordinates contain usable values.</summary>
        public bool isValid;

        /// <summary>
        /// Most recently queued EyeLink event type when this sample was read.
        /// This is deliberately not the sample object's own data type.
        /// </summary>
        public Eltype eltype;
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

    /// <summary>Whether the service currently owns a live tracker connection.</summary>
    public static bool IsInitialized { get; private set; }

    /// <summary>Whether an EyeLink recording block is currently active.</summary>
    public static bool IsRecording { get; private set; }

    /// <summary>Whether a gaze sample has been received in the current recording.</summary>
    public static bool HasGazeSample { get; private set; }

    /// <summary>
    /// Most recently received gaze sample. Check <see cref="HasGazeSample"/> and
    /// <see cref="GazeSample.isValid"/> before using its coordinates.
    /// </summary>
    public static GazeSample LatestGazeSample { get; private set; }

    /// <summary>
    /// Number of fixation-start events received for the active eye in the
    /// current recording. The count resets when recording starts or the service
    /// shuts down, so callers can snapshot it and compare later values.
    /// </summary>
    public static int StartFixationEventCount
    {
        get { return startFixationEventCount; }
    }

    /// <summary>
    /// Most recently received non-sample link event. Before the first event it
    /// is EL_SAMPLE_TYPE. Samples do not immediately overwrite this value so a
    /// short-lived event remains visible in the live gaze overlay.
    /// </summary>
    public static Eltype LatestEventType { get; private set; } = Eltype.EL_SAMPLE_TYPE;

    /// <summary>
    /// Exact local destination for the next downloaded EDF. A timestamped path
    /// under <see cref="Application.persistentDataPath"/> is created lazily when
    /// no destination has been selected.
    /// </summary>
    public static string LocalEdfPath
    {
        get
        {
            if (String.IsNullOrEmpty(localEdfPath)) {
                localEdfPath = CreateDefaultLocalEdfPath();
            }

            return localEdfPath;
        }
    }
    /// <summary>
    /// Local path of the EDF downloaded by the most recent successful shutdown,
    /// or an empty string when no file has been downloaded.
    /// </summary>
    public static string LastDownloadedEdf { get; private set; } = String.Empty;

    /// <summary>
    /// Raw EyeLink COM connection. Prefer the service methods in this class;
    /// this remains public for legacy integrations that require SDK access.
    /// </summary>
    public static ELink eyelink;

    private static ELinkUtil eyelinkUtil;
    private static Eye activeEye = Eye.EL_RIGHT;

    private static bool dataFileOpen;
    private static bool shuttingDown;
    private static bool lifecycleCreated;
    private static bool hasReceivedLinkEvent;
    private static int startFixationEventCount;
    private static int eyeLinkTrialNumber;
    private static double lastSampleTime = Double.MinValue;
    private static string remoteEdfName = String.Empty;
    private static string localEdfPath = String.Empty;

    private const int EnterKey = 0x000D;
    private const short NoKeyModifiers = 0;
    private const short KeyPress = 10;
    private const int MaximumQueuedItemsPerFrame = 4096;

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

    /// <summary>
    /// Connects to the configured tracker (or dummy tracker), configures live
    /// data, and opens the Host EDF. Repeated calls are safe while connected.
    /// Failures are logged and leave the service uninitialized.
    /// </summary>
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
            LatestEventType = Eltype.EL_SAMPLE_TYPE;
            hasReceivedLinkEvent = false;
            startFixationEventCount = 0;
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

    /// <summary>Checks the current EyeLink connection without throwing.</summary>
    /// <returns><c>true</c> when the COM connection reports that it is connected.</returns>
    public static bool TryGetEyelinkConnectedStatus()
    {
        try {
            return eyelink != null && eyelink.isConnected();
        }
        catch (Exception) {
            return false;
        }
    }

    /// <summary>Starts calibration directly from the EyeLink setup screen.</summary>
    /// <returns><c>true</c> when the setup operation completes successfully.</returns>
    public static bool Calibration()
    {
        return RunTrackerSetupMode("calibration", 'c', false);
    }

    /// <summary>Runs drift detection/correction at the center of the display.</summary>
    /// <returns><c>true</c> when drift correction completes successfully.</returns>
    public static bool DriftDetection()
    {
        return RunCalibrationWindow("drift detection", delegate(int width, int height) {
            eyelink.doDriftCorrect(
                (short)(width / 2),
                (short)(height / 2),
                true,
                true);
        });
    }

    /// <summary>Starts validation directly from the EyeLink setup screen.</summary>
    /// <returns><c>true</c> when validation completes successfully.</returns>
    public static bool Validation()
    {
        return RunTrackerSetupMode("validation", 'v', false);
    }

    /// <summary>Opens the EyeLink camera image/setup screen.</summary>
    /// <returns><c>true</c> when the camera setup operation completes successfully.</returns>
    public static bool CameraSetup()
    {
        return RunTrackerSetupMode("camera setup", EnterKey, true);
    }

    /// <summary>Compatibility wrapper retained for existing UI/code.</summary>
    /// <returns>The result of <see cref="Calibration"/>.</returns>
    public static bool Calibrate()
    {
        return Calibration();
    }

    /// <summary>
    /// Sets the exact local EDF destination. Selecting a directory generates a
    /// timestamped EDF filename inside it.
    /// </summary>
    /// <param name="requestedPath">An EDF file path or output directory.</param>
    /// <param name="resolvedPath">The resulting absolute EDF file path.</param>
    /// <returns><c>true</c> when the path is valid and its directory is available.</returns>
    public static bool TrySetLocalEdfPath(string requestedPath, out string resolvedPath)
    {
        resolvedPath = LocalEdfPath;
        if (String.IsNullOrWhiteSpace(requestedPath)) {
            return false;
        }

        try {
            string trimmedPath = requestedPath.Trim().Trim('"');
            string fullPath = Path.GetFullPath(trimmedPath);
            bool directorySelected = Directory.Exists(fullPath) ||
                trimmedPath.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                trimmedPath.EndsWith(Path.AltDirectorySeparatorChar.ToString()) ||
                String.IsNullOrEmpty(Path.GetExtension(fullPath));

            if (directorySelected) {
                fullPath = Path.Combine(fullPath, CreateLocalEdfFileName());
            }
            else if (!String.Equals(
                Path.GetExtension(fullPath), ".edf", StringComparison.OrdinalIgnoreCase)) {
                return false;
            }

            string outputFolder = Path.GetDirectoryName(fullPath);
            if (String.IsNullOrEmpty(outputFolder)) {
                return false;
            }

            Directory.CreateDirectory(outputFolder);
            localEdfPath = fullPath;
            resolvedPath = localEdfPath;
            return true;
        }
        catch (Exception exception) {
            Debug.LogWarning("[EyeLink] Invalid local EDF path: " + exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Opens the normalized EDF filename on the EyeLink Host. Dummy mode treats
    /// this as a successful no-op.
    /// </summary>
    /// <returns><c>true</c> when the file is open or no file is needed.</returns>
    public static bool OpenDataFile()
    {
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
    }

    /// <summary>
    /// Starts file samples/events and link samples/events. Dummy mode tracks the
    /// same lifecycle but does not create tracker data.
    /// </summary>
    /// <returns><c>true</c> when recording is active after the call.</returns>
    public static bool StartRecording()
    {
        if (IsRecording) {
            return true;
        }

        if (!EnsureConnected("start recording")) {
            return false;
        }

        ResetLiveDataForRecording();

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

            IsRecording = true;
            Debug.Log("[EyeLink] Recording started.");
            return true;
        }
        catch (Exception exception) {
            Debug.LogError("[EyeLink] Could not start recording: " + exception.Message);
            IsRecording = false;
            return false;
        }
    }

    /// <summary>
    /// Stops the active recording and returns the tracker to offline mode. Safe
    /// to call when no recording is active.
    /// </summary>
    public static void StopRecording()
    {
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
    }

    /// <summary>Sends a single sanitized message line to the EyeLink Host.</summary>
    /// <param name="message">Message to store in the EDF event stream.</param>
    /// <returns><c>true</c> when the Host accepts the message.</returns>
    public static bool SendMessage(string message)
    {
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
        Debug.Log("[EyeLink] Did not send message on this platform: " + message);
        return false;
    }

    /// <summary>Sends a single sanitized tracker command.</summary>
    /// <param name="command">Command text understood by the EyeLink Host.</param>
    /// <returns><c>true</c> when the Host accepts the command.</returns>
    public static bool SendCommand(string command)
    {
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
    }

    /// <summary>Compatibility wrapper retained for the existing maze code.</summary>
    /// <param name="msg">Message to forward to <see cref="SendMessage"/>.</param>
    public static void TryEyemsg_Printf(String msg)
    {
        SendMessage(msg);
    }

    /// <summary>
    /// Gets the newest right-eye (or monocular) link sample. Tracker coordinates
    /// use a top-left origin; Unity coordinates use a bottom-left origin.
    /// </summary>
    /// <param name="gazeSample">
    /// The newest sample, or the previously cached sample when no newer tracker
    /// timestamp is available.
    /// </param>
    /// <returns><c>true</c> only when a sample with a new timestamp was read.</returns>
    public static bool TryGetLatestSample(out GazeSample gazeSample)
    {
        gazeSample = LatestGazeSample;
        if (!IsRecording || openDummy || !TryGetEyelinkConnectedStatus()) {
            return false;
        }

        try {
            DrainQueuedLinkData();
            gazeSample = LatestGazeSample;

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
                isValid = isValid,
                eltype = LatestEventType
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
    /// <returns>A three-value sample list, or an empty list.</returns>
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
    /// <param name="gameObject">Rendered scene object whose bounds should be projected.</param>
    /// <returns>A pixel rectangle using the tracker's top-left origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="gameObject"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The object has no renderer or the scene has no main camera.
    /// </exception>
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
    /// <param name="trigger">Maze lifecycle event to mirror in the EDF.</param>
    /// <param name="triggerValue">Target index or version value associated with the event.</param>
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
    /// <param name="downloadEdf">
    /// Whether to copy the closed Host EDF to <see cref="LocalEdfPath"/>.
    /// </param>
    public static void Shutdown(bool downloadEdf = true)
    {
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
            LatestEventType = Eltype.EL_SAMPLE_TYPE;
            hasReceivedLinkEvent = false;
            startFixationEventCount = 0;
            dataFileOpen = false;
            shuttingDown = false;
        }
    }

    private static void ConfigureTracker() {
        int right = Math.Max(0, Screen.width - 1);
        int bottom = Math.Max(0, Screen.height - 1);

        eyelink.setOfflineMode();
        eyelink.sendCommand("screen_pixel_coords = 0 0 " + right + " " + bottom);
        eyelink.sendCommand("calibration_type = HV9");
        eyelink.sendCommand(
            "file_event_filter = LEFT,RIGHT,FIXATION,SACCADE,BLINK,MESSAGE,BUTTON,INPUT");
        eyelink.sendCommand(
            "link_event_filter = LEFT,RIGHT,FIXATION,FIXUPDATE,SACCADE,BLINK,MESSAGE,BUTTON,INPUT");
        eyelink.sendCommand("fixation_update_interval = 50");
        eyelink.sendCommand("fixation_update_accumulate = 50");
        eyelink.sendCommand(
            "file_sample_data = LEFT,RIGHT,GAZE,HREF,RAW,AREA,GAZERES,BUTTON,STATUS,INPUT");
        eyelink.sendCommand(
            "link_sample_data = LEFT,RIGHT,GAZE,GAZERES,AREA,STATUS,INPUT");
        eyelinkUtil.pumpDelay(50);
    }

    /// <summary>
    /// Drains the ordered link queue so samples cannot hide the less frequent
    /// blink, saccade, fixation, message, button, input, and lost-data events.
    /// getNewestSample remains the source of low-latency gaze coordinates.
    /// </summary>
    private static void DrainQueuedLinkData()
    {
        for (int itemIndex = 0; itemIndex < MaximumQueuedItemsPerFrame; itemIndex++)
        {
            SREYELINKLib.ELData linkData = eyelink.getNextData();
            if (linkData == null)
            {
                break;
            }

            try
            {
                Eltype dataType = linkData.eltype;
                bool isSample = dataType == Eltype.EL_SAMPLE_TYPE ||
                    dataType == Eltype.EL_RAWSAMPLE_TYPE;

                if (!isSample)
                {
                    LatestEventType = dataType;
                    hasReceivedLinkEvent = true;

                    if (dataType == Eltype.EL_STARTFIX)
                    {
                        CountStartFixation(linkData);
                    }
                }
                else if (!hasReceivedLinkEvent)
                {
                    LatestEventType = dataType;
                }
            }
            finally
            {
                if (Marshal.IsComObject(linkData))
                {
                    Marshal.ReleaseComObject(linkData);
                }
            }
        }

        if (HasGazeSample)
        {
            GazeSample latestSample = LatestGazeSample;
            latestSample.eltype = LatestEventType;
            LatestGazeSample = latestSample;
        }
    }

    /// <summary>
    /// Counts fixation starts for the selected eye. Coordinates come from the
    /// low-latency sample stream; the ordered queue is used only to count each
    /// event once.
    /// </summary>
    private static void CountStartFixation(SREYELINKLib.ELData linkData)
    {
        SREYELINKLib.IStartFixationEvent fixation =
            linkData as SREYELINKLib.IStartFixationEvent;
        if (fixation == null ||
            (activeEye != Eye.EL_EYE_NONE && fixation.eye != activeEye))
        {
            return;
        }

        startFixationEventCount++;
    }

    private static void ResetLiveDataForRecording()
    {
        lastSampleTime = Double.MinValue;
        HasGazeSample = false;
        LatestEventType = Eltype.EL_SAMPLE_TYPE;
        hasReceivedLinkEvent = false;
        startFixationEventCount = 0;
    }

    private static bool RunTrackerSetupMode(
        string operation,
        int setupKey,
        bool startInCameraMode)
    {
        return RunCalibrationWindow(operation, delegate(int width, int height) {
            eyelink.setTrackerSetupDefault((short)(startInCameraMode ? 1 : 0));

            try {
                if (startInCameraMode) {
                    eyelink.doTrackerSetup();
                    return;
                }

                // doTrackerSetup owns the native message loop. A WinForms timer
                // injects the requested setup key after that loop has started.
                using (System.Windows.Forms.Timer setupTimer =
                    new System.Windows.Forms.Timer()) {
                    setupTimer.Interval = 100;
                    setupTimer.Tick += delegate {
                        if (!eyelink.inSetup()) {
                            return;
                        }

                        setupTimer.Stop();
                        eyelink.sendKeybutton(setupKey, NoKeyModifiers, KeyPress);
                    };
                    setupTimer.Start();
                    eyelink.doTrackerSetup();
                }
            }
            finally {
                eyelink.setTrackerSetupDefault(0);
            }
        });
    }

    private static bool RunCalibrationWindow(
        string operation,
        Action<int, int> trackerAction)
    {
        if (!EnsureConnected(operation)) {
            return false;
        }

        if (openDummy) {
            Debug.Log("[EyeLink] " + operation + " skipped in dummy mode.");
            return true;
        }

        try {
            StopRecording();
            IntPtr unityWindow = GetActiveWindow();
            if (unityWindow == IntPtr.Zero) {
                throw new InvalidOperationException(
                    "Unity does not have an active window for the EyeLink setup display.");
            }

            using (EyeLinkCalibrationWindow calibrationWindow =
                new EyeLinkCalibrationWindow(unityWindow)) {
                calibrationWindow.ShowForCalibration();

                int width = calibrationWindow.ClientSize.Width;
                int height = calibrationWindow.ClientSize.Height;
                int right = Math.Max(0, width - 1);
                int bottom = Math.Max(0, height - 1);

                if (width != Screen.width || height != Screen.height) {
                    Debug.LogWarning(
                        "[EyeLink] The local setup window is " + width + "x" + height +
                        ", but Unity is rendering at " + Screen.width + "x" + Screen.height +
                        ". Use a fullscreen standalone build so gaze and stimulus " +
                        "coordinates remain aligned.");
                }

                eyelink.setOfflineMode();
                eyelink.sendCommand(
                    "screen_pixel_coords = 0 0 " + right + " " + bottom);
                eyelink.sendMessage(
                    "DISPLAY_COORDS 0 0 " + right + " " + bottom);
                eyelinkUtil.pumpDelay(50);

                SREYELINKLib.ELGDICal cal = eyelinkUtil.getGDICal();
                cal.setCalibrationWindow(calibrationWindow.Handle.ToInt32());
                cal.enableKeyCollection(true);

                try {
                    trackerAction(width, height);
                }
                finally {
                    cal.enableKeyCollection(false);
                }
            }

            Debug.Log("[EyeLink] " + operation + " finished.");
            return true;
        }
        catch (Exception exception) {
            Debug.LogError("[EyeLink] " + operation + " failed: " + exception.Message);
            return false;
        }
    }

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
        string path = LocalEdfPath;
        string outputFolder = Path.GetDirectoryName(path);
        Directory.CreateDirectory(outputFolder);
        return path;
    }

    private static string CreateDefaultLocalEdfPath()
    {
        string outputFolder = Path.Combine(Application.persistentDataPath, "EyeLinkData");
        return Path.Combine(outputFolder, CreateLocalEdfFileName());
    }

    private static string CreateLocalEdfFileName()
    {
        string baseName = String.IsNullOrEmpty(remoteEdfName)
            ? NormalizeEdfBaseName(edfBaseName)
            : Path.GetFileNameWithoutExtension(remoteEdfName);
        return baseName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".edf";
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
