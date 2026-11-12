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
    /// <summary>The calibration pattern selected in the EyeLink menu.</summary>
    public static string CalibrationType { get; private set; } = "HV9";

    private static int calibrationTargetDiameter;
    private static int calibrationHoleDiameter;

    /// <summary>Outer calibration target diameter in participant-display pixels.</summary>
    public static int CalibrationTargetDiameter {
        get {
            return calibrationTargetDiameter > 0 ? calibrationTargetDiameter :
                Math.Max(3, GetCalibrationDisplayWidthForDefaults() / 60);
        }
    }

    /// <summary>Central hole diameter in pixels, always smaller than the target.</summary>
    public static int CalibrationHoleDiameter {
        get {
            int diameter = calibrationHoleDiameter > 0 ? calibrationHoleDiameter :
                Math.Max(2, GetCalibrationDisplayWidthForDefaults() / 300);
            return Math.Min(diameter, CalibrationTargetDiameter - 1);
        }
    }

    /// <summary>Calibration target and background colors for the next setup operation.</summary>
    public static Color CalibrationTargetColor { get; private set; } = Color.black;
    public static Color CalibrationBackgroundColor { get; private set; } = Color.white;

    /// <summary>
    /// C# equivalent of set_target_size(). Values are integer pixel diameters;
    /// keep the focal hole at least two pixels wide, as in the SDK example.
    /// Used by the target renderer for the next calibration, validation, or drift correction.
    /// </summary>
    public static void SetCalibrationTargetSize(int diameter, int holeDiameter)
    {
        calibrationTargetDiameter = Math.Max(3, Math.Min(short.MaxValue, diameter));
        calibrationHoleDiameter = Math.Max(2,
            Math.Min(calibrationTargetDiameter - 1, holeDiameter));
    }

    /// <summary>Returns the SDK example's width/60 and width/300 defaults.</summary>
    public static void GetDefaultCalibrationTargetSize(out int diameter, out int holeDiameter)
    {
        int width = GetCalibrationDisplayWidthForDefaults();
        diameter = Math.Max(3, Math.Min(short.MaxValue, width / 60));
        holeDiameter = Math.Max(2, Math.Min(diameter - 1, width / 300));
    }

    /// <summary>C# equivalent of set_calibration_colors(foreground, background).</summary>
    public static void SetCalibrationColors(Color foreground, Color background)
    {
        CalibrationTargetColor = foreground;
        CalibrationBackgroundColor = background;
    }

    private static int GetCalibrationDisplayWidthForDefaults()
    {
        if (CalibrationDisplayWidth > 0) {
            return CalibrationDisplayWidth;
        }
        int monitorIndex = GetDefaultCalibrationMonitorIndex();
        int width = Display.displays.Length > monitorIndex
            ? Display.displays[monitorIndex].systemWidth : 0;
        return width > 0 ? width : Screen.width;
    }

    /// <summary>The measured calibration surface sent to the current Host.</summary>
    public static int CalibrationDisplayWidth { get; private set; }
    public static int CalibrationDisplayHeight { get; private set; }
    public static int CalibrationDisplayLeft { get; private set; }
    public static int CalibrationDisplayTop { get; private set; }

    private static int GazeDisplayWidth {
        get { return CalibrationDisplayWidth > 1 ? CalibrationDisplayWidth : Screen.width; }
    }
    private static int GazeDisplayHeight {
        get { return CalibrationDisplayHeight > 1 ? CalibrationDisplayHeight : Screen.height; }
    }

    /// <summary>Converts Host pixels to participant-display bottom-left pixels.</summary>
    public static Vector2 TrackerToUnityPixels(float trackerX, float trackerY)
    {
        return new Vector2(trackerX - CalibrationDisplayLeft,
            CalibrationDisplayTop + GazeDisplayHeight - 1 - trackerY);
    }

    /// <summary>Maps Host pixels to the subject camera's normalized viewport.</summary>
    public static bool TryTrackerPixelsToViewport(Vector2 trackerPixels, out Vector2 viewport)
    {
        viewport = Vector2.zero;
        int width = GazeDisplayWidth;
        int height = GazeDisplayHeight;
        if (width <= 1 || height <= 1) {
            return false;
        }
        Vector2 pixels = TrackerToUnityPixels(trackerPixels.x, trackerPixels.y);
        viewport = new Vector2(pixels.x / (width - 1),
            pixels.y / (height - 1));
        return viewport.x >= 0f && viewport.x <= 1f &&
            viewport.y >= 0f && viewport.y <= 1f;
    }

    /// <summary>Changes the pattern used by the next calibration.</summary>
    public static bool TrySetCalibrationType(string type)
    {
        string normalized = type == null ? String.Empty : type.Trim().ToUpperInvariant();
        switch (normalized) {
            case "H3": case "HV3": case "HV5": case "HV9": case "HV13":
                CalibrationType = normalized;
                return true;
            default:
                return false;
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();
#endif

    /// <summary>
    /// Returns Display 2 for EyeLink calibration when it is available.
    /// </summary>
    public static int GetDefaultCalibrationMonitorIndex()
    {
        return Display.displays.Length > 1 ? 1 : 0;
    }

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
        /// The sample object's own data type. Eye events are exposed separately
        /// through LatestEventType and TryGetStartFixationEvent.
        /// </summary>
        public Eltype eltype;
    }

    /// <summary>A copy of one queued STARTFIX event for the active eye.</summary>
    public struct StartFixation
    {
        public double trackerTime;
        public Eye eye;
        public ushort readFlags;
        public Vector2 trackerPixels;
        public Vector2 unityPixels;

        /// <summary>Whether this event includes a usable start gaze position.</summary>
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
        get { return startFixationEvents.Count; }
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
    private static readonly List<StartFixation> startFixationEvents =
        new List<StartFixation>();
    private static bool warnedMissingStartFixationPosition;
    private static int eyeLinkTrialNumber;
    private static double lastSampleTime = Double.MinValue;
    private static string remoteEdfName = String.Empty;
    private static string localEdfPath = String.Empty;

    private const int EnterKey = 0x000D;
    private const int EscapeKey = 0x001B;
    private const short NoKeyModifiers = 0;
    private const short KeyPress = 10;
    private const short NoReply = 1000;
    private const short InTargetMode = 8;
    private const short InDriftCorrectionMode = 16;
    private const short HostCalibrationMode = 11;
    private const short HostValidationMode = 12;
    private const int TrackerCommandTimeoutMilliseconds = 1000;
    private const int SetupKeyDelayMilliseconds = 1000;
    private const int SetupModeTimeoutMilliseconds = 10000;
    private const int SetupExitTimeoutMilliseconds = 2000;
    private const int DriftCorrectionTimeoutMilliseconds = 60000;
    private const int MaximumQueuedItemsPerPoll = 4096;
    // FEVENT.read flags from eye_data.h (not the EVENT_* block-content flags).
    private const ushort ReadBeginPosition = 0x0008;
    private const ushort ReadGazePosition = 0x1000;

    /// <summary>
    /// Runs calibration, validation, or drift correction without a blocking
    /// SDK setup loop. All tracker calls and window messages stay on the
    /// Unity thread, which avoids re-entering the EyeLink COM object.
    /// </summary>
    private sealed class TrackerTargetSequence
    {
        private readonly string operation;
        private readonly int setupKey;
        private readonly EyeLinkCalibrationWindow calibrationWindow;
        private readonly SREYELINKLib.ELGDICal calibrationGraphics;
        private readonly Action<int> onPointAccepted;
        private bool targetPending;
        private bool targetDrawn;
        private bool targetStateReported;
        private float targetX;
        private float targetY;
        private int acceptedPointCount;

        public TrackerTargetSequence(
            string operation,
            int setupKey,
            EyeLinkCalibrationWindow calibrationWindow,
            SREYELINKLib.ELGDICal calibrationGraphics,
            Action<int> onPointAccepted)
        {
            this.operation = operation;
            this.setupKey = setupKey;
            this.calibrationWindow = calibrationWindow;
            this.calibrationGraphics = calibrationGraphics;
            this.onPointAccepted = onPointAccepted;
        }

        public void Run()
        {
            bool setupStarted = false;
            bool hostFinished = false;
            string sdkCall = "getCalibrationResult";
            try {
                if (setupKey == 'c') {
                    // The display coordinates have now been set for the
                    // calibration window. Reissue the type to regenerate
                    // targets and override later changes on the Host.
                    sdkCall = "sendTimedCommand (calibration_type)";
                    eyelink.sendTimedCommand(TrackerCommandTimeoutMilliseconds,
                        "calibration_type = " + CalibrationType);
                    ReportStatus(
                        "[EyeLink] Requested " + CalibrationType +
                        " calibration on the Host.");
                }

                // Reading the result clears any reply left by an earlier setup.
                ReportStatus("[EyeLink] Clearing previous calibration result.");
                sdkCall = "getCalibrationResult";
                eyelink.getCalibrationResult();
                ReportStatus("[EyeLink] Requesting tracker setup mode.");
                sdkCall = "startSetup";
                eyelink.startSetup();
                setupStarted = true;

                ReportStatus("[EyeLink] Waiting for tracker setup mode to become ready.");
                // Poll the mode while servicing Windows messages instead of
                // blocking inside COM's waitForModeReady call.
                sdkCall = "inSetup (waiting for setup screen)";
                WaitForSetupScreen();
                if (setupKey != 0) {
                    ReportStatus(
                        "[EyeLink] Setup screen ready; waiting one second " +
                        "before requesting " + operation + ".");
                    PumpFor(SetupKeyDelayMilliseconds);

                    if (!eyelink.inSetup()) {
                        throw new InvalidOperationException(
                            "EyeLink left setup mode before the " + operation +
                            " key could be sent.");
                    }

                    sdkCall = "sendKeybutton (starting " + operation + ")";
                    eyelink.sendKeybutton(setupKey, NoKeyModifiers, KeyPress);
                    ReportStatus("[EyeLink] Requested " + operation + " mode.");
                }
                else {
                    ReportStatus(
                        "[EyeLink] Setup screen ready; waiting for the " +
                        "tracker to start " + operation + ".");
                }

                sdkCall = "waiting for target mode";
                WaitForTargetMode();
                sdkCall = "running target display";
                short result = RunTargetModeDisplay();
                hostFinished = true;
                if (result == EscapeKey) {
                    throw new OperationCanceledException(operation + " cancelled on the Host.");
                }
                if (result != 0) {
                    throw new InvalidOperationException(
                        "EyeLink " + operation + " returned result " + result +
                        GetCalibrationFailureDetails() + ".");
                }
            }
            catch (Exception exception) {
                exception.Data["EyeLinkSdkCall"] = sdkCall;
                throw;
            }
            finally {
                if (setupStarted && !hostFinished) {
                    ExitSetupMode();
                }
            }
        }

        public void RunDriftCorrection(short x, short y)
        {
            bool displayStarted = false;
            bool driftStarted = false;
            short result = NoReply;
            string sdkCall = "getCalibrationResult";
            try {
                eyelink.getCalibrationResult();
                sdkCall = "painting the native drift target display";
                BeginTargetDisplay();
                displayStarted = true;
                calibrationWindow.DrawCalibrationTarget(x, y);
                targetDrawn = true;
                ReportStatus("[EyeLink] Drew drift target at (" + x + ", " + y + ").");
                calibrationGraphics.playCalibrationBeep(
                    SREYELINKLib.CAL_BEEP_ID.DC_TARG_BEEP);

                ReportStatus(
                    "[EyeLink] Drift detection: fixate the center target and " +
                    "press Space to accept; Escape cancels. " +
                    "The check times out after 60 seconds.");
                // Use the low-level operation so Escape cannot start the
                // SDK's nested setup/calibration loop.
                sdkCall = "startDriftCorrect";
                driftStarted = true;
                eyelink.startDriftCorrect(x, y);
                sdkCall = "waiting for drift correction mode";
                int startedAt = Environment.TickCount;
                while ((eyelink.getCurrentMode() & InDriftCorrectionMode) == 0) {
                    PumpOnce(true);
                    if (HasTimedOut(startedAt, SetupModeTimeoutMilliseconds)) {
                        throw new TimeoutException(
                            "EyeLink did not enter drift correction mode within " +
                            SetupModeTimeoutMilliseconds + " ms.");
                    }
                }

                sdkCall = "getCalibrationResult (drift detection)";
                startedAt = Environment.TickCount;
                while (result == NoReply) {
                    PumpOnce(true);
                    result = eyelink.getCalibrationResult();
                    if (result == NoReply &&
                        HasTimedOut(startedAt, DriftCorrectionTimeoutMilliseconds)) {
                        throw new TimeoutException(
                            "EyeLink drift detection timed out after " +
                            DriftCorrectionTimeoutMilliseconds + " ms.");
                    }
                }

                if (result == EscapeKey) {
                    throw new OperationCanceledException("Drift detection cancelled.");
                }
                if (result != 0) {
                    throw new InvalidOperationException(
                        "EyeLink drift detection returned result " + result +
                        ". Use Calibration or Camera Setup before trying again.");
                }

                sdkCall = "applyDriftCorrect";
                eyelink.applyDriftCorrect();
            }
            catch (Exception exception) {
                exception.Data["EyeLinkSdkCall"] = sdkCall;
                throw;
            }
            finally {
                try {
                    if (driftStarted) {
                        // Always leave the tracker idle; do not retry drift or
                        // open camera setup from this button.
                        eyelink.setOfflineMode();
                    }
                }
                finally {
                    if (displayStarted) {
                        try {
                            if (targetDrawn) {
                                calibrationWindow.EraseCalibrationTarget();
                                targetDrawn = false;
                            }
                            if (result != NoReply) {
                                calibrationGraphics.playDoneBeep(result);
                            }
                        }
                        finally {
                            calibrationWindow.EndTargetDisplay();
                        }
                    }
                }
            }
        }

        private void WaitForSetupScreen()
        {
            int startedAt = Environment.TickCount;
            while (!eyelink.inSetup()) {
                PumpOnce();
                if (HasTimedOut(startedAt, SetupModeTimeoutMilliseconds)) {
                    throw new TimeoutException(
                        "EyeLink did not enter setup mode within " +
                        SetupModeTimeoutMilliseconds + " ms.");
                }
            }
        }

        private short WaitForHostAcceptance(short result)
        {
            // Keep the native display alive while the operator reviews the
            // result. Only the Host/operator may accept it; never send Enter
            // or the successful-path Escape that could discard calibration.
            short completedMode = setupKey == 'c' ? HostCalibrationMode : HostValidationMode;
            ReportStatus("[EyeLink] " + operation +
                " sequence finished. Review the result and click Accept on " +
                "the EyeLink Host; Escape cancels.");
            while (eyelink.inSetup() && eyelink.getTrackerMode() == completedMode) {
                PumpOnce();
                short reply = eyelink.getCalibrationResult();
                if (reply != NoReply) {
                    result = reply;
                }
            }
            return result;
        }

        private void WaitForTargetMode()
        {
            int startedAt = Environment.TickCount;
            while ((eyelink.getCurrentMode() & InTargetMode) == 0) {
                PumpOnce();
                if (!eyelink.inSetup()) {
                    throw new InvalidOperationException(
                        "EyeLink left setup mode before " + operation +
                        " began.");
                }

                if (HasTimedOut(startedAt, SetupModeTimeoutMilliseconds)) {
                    throw new TimeoutException(
                        "EyeLink did not enter " + operation +
                        " target mode within " + SetupModeTimeoutMilliseconds +
                        " ms after setup became ready.");
                }
            }
        }

        private short RunTargetModeDisplay()
        {
            short result = NoReply;
            BeginTargetDisplay();
            ReportStatus("[EyeLink] Target display ready; waiting for tracker target positions.");

            try {
                while (eyelink.inSetup() &&
                    (eyelink.getCurrentMode() & InTargetMode) != 0) {
                    PumpOnce();

                    result = eyelink.getCalibrationResult();
                    if (result != NoReply) {
                        break;
                    }

                    UpdateCalibrationTarget();
                }

                if (result == 0 && targetPending) {
                    NotifyPointAccepted();
                    targetPending = false;
                }
                if (result != EscapeKey) {
                    result = WaitForHostAcceptance(result);
                }

                if (result == NoReply) {
                    // The final reply can arrive just after the target-mode
                    // flag clears. Allow a bounded interval for that packet.
                    int startedAt = Environment.TickCount;
                    do {
                        PumpOnce();
                        result = eyelink.getCalibrationResult();
                    } while (result == NoReply &&
                        !HasTimedOut(startedAt, SetupExitTimeoutMilliseconds));
                }

                if (result == 0 && targetPending) {
                    NotifyPointAccepted();
                    targetPending = false;
                }
            }
            finally {
                try {
                    if (targetDrawn) {
                        calibrationWindow.EraseCalibrationTarget();
                        targetDrawn = false;
                    }
                    if (result != NoReply) {
                        calibrationGraphics.playDoneBeep(result);
                    }
                }
                finally {
                    calibrationWindow.EndTargetDisplay();
                }
            }

            if (result == NoReply) {
                throw new InvalidOperationException(
                    "EyeLink left " + operation +
                    " target mode without returning a result.");
            }

            return result;
        }

        private void BeginTargetDisplay()
        {
            calibrationWindow.BeginTargetDisplay(ToColorRef(CalibrationTargetColor),
                ToColorRef(CalibrationBackgroundColor), CalibrationTargetDiameter,
                CalibrationHoleDiameter);
            ReportStatus("[EyeLink] Target renderer: native window painting with repaint support.");
        }

        private void UpdateCalibrationTarget()
        {
            float currentX;
            float currentY;
            // COM exposes eyelink_target_check() as its floating-point
            // counterpart, targetCheckf(). Like the documented C function,
            // its return value is non-zero while the tracker wants the target
            // displayed and zero while it wants the target erased.
            short targetState = eyelink.targetCheckf(
                out currentX,
                out currentY);
            bool targetVisible = targetState != 0;
            if (!targetStateReported) {
                ReportStatus("[EyeLink] First targetCheckf: state=" + targetState +
                    ", position=(" + currentX + ", " + currentY + ").");
                targetStateReported = true;
            }

            bool targetMoved = targetPending &&
                (currentX != targetX || currentY != targetY);
            if (targetDrawn && (!targetVisible || targetMoved)) {
                calibrationWindow.EraseCalibrationTarget();
                targetDrawn = false;
            }

            if (!targetVisible) {
                return;
            }

            if (Single.IsNaN(currentX) || Single.IsInfinity(currentX) ||
                Single.IsNaN(currentY) || Single.IsInfinity(currentY) ||
                currentX < 0 || currentY < 0 ||
                currentX >= CalibrationDisplayWidth || currentY >= CalibrationDisplayHeight) {
                throw new InvalidOperationException(
                    "EyeLink requested a target outside the calibration window: (" +
                    currentX + ", " + currentY + ") for " +
                    CalibrationDisplayWidth + "x" + CalibrationDisplayHeight + " pixels.");
            }

            if (!targetPending || targetMoved) {
                if (targetPending) {
                    NotifyPointAccepted();
                }

                targetPending = true;
                targetX = currentX;
                targetY = currentY;
                calibrationGraphics.playCalibrationBeep(
                    SREYELINKLib.CAL_BEEP_ID.CAL_TARG_BEEP);
            }

            if (!targetDrawn) {
                calibrationWindow.DrawCalibrationTarget(
                    Convert.ToInt16(Math.Round(currentX)),
                    Convert.ToInt16(Math.Round(currentY)));
                targetDrawn = true;
                ReportStatus("[EyeLink] Drew " + operation + " target at (" +
                    currentX + ", " + currentY + "), targetCheckf=" + targetState + ".");
            }
        }

        private void NotifyPointAccepted()
        {
            if (onPointAccepted == null) {
                return;
            }

            acceptedPointCount++;
            ReportStatus(
                "[EyeLink] Calibration point " + acceptedPointCount + " accepted.");

            try {
                onPointAccepted(acceptedPointCount);
            }
            catch (Exception exception) {
                ReportError(
                    "[EyeLink] Calibration reward failed: " +
                    FormatException(exception));
            }
        }

        private void PumpFor(int milliseconds)
        {
            int startedAt = Environment.TickCount;
            while (!HasTimedOut(startedAt, milliseconds)) {
                PumpOnce();
            }
        }

        private void PumpOnce(bool driftOnly = false, bool allowCancel = true)
        {
            calibrationWindow.PumpEvents();

            int key;
            while (calibrationWindow.TryDequeueSetupKey(out key)) {
                if (key == EscapeKey && allowCancel) {
                    throw new OperationCanceledException(operation + " cancelled.");
                }
                if (key == ' ') {
                    eyelink.acceptTrigger();
                }
                else if (!driftOnly) {
                    eyelink.sendKeybutton(key, NoKeyModifiers, KeyPress);
                }
            }

            if (!eyelink.isConnected()) {
                throw new InvalidOperationException(
                    "EyeLink disconnected during " + operation + ".");
            }
            System.Threading.Thread.Sleep(5);
        }

        private void ExitSetupMode()
        {
            try {
                if (!eyelink.inSetup()) {
                    return;
                }

                eyelink.sendKeybutton(EscapeKey, NoKeyModifiers, KeyPress);
                int startedAt = Environment.TickCount;
                while (eyelink.inSetup() &&
                    !HasTimedOut(startedAt, SetupExitTimeoutMilliseconds)) {
                    // During cleanup, forward further Escape presses instead
                    // of interrupting the exit/abort fallback.
                    PumpOnce(false, false);
                }

                if (eyelink.inSetup()) {
                    eyelink.abort();
                    eyelink.waitForModeReady(1000);
                }
            }
            catch (Exception exception) {
                Debug.LogWarning(
                    "[EyeLink] Could not close setup mode cleanly: " +
                    FormatException(exception));
            }
        }

        private static bool HasTimedOut(int startedAt, int timeoutMilliseconds)
        {
            return unchecked(Environment.TickCount - startedAt) >=
                timeoutMilliseconds;
        }
    }

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
                ReportStatus("[EyeLink] Connected in dummy mode.");
            }
            else {
                eyelink.open(trackerAddress, 0);
                //eyelink.open();
                if (!eyelink.isConnected()) {
                    throw new InvalidOperationException(
                        "The EyeLink Host did not accept a connection at " + trackerAddress + ".");
                }

                ReportStatus("[EyeLink] Connected to " + trackerAddress + ".");
            }

            IsInitialized = true;
            ResetLiveDataForRecording();
            LastDownloadedEdf = String.Empty;

            if (!openDummy) {
                ConfigureTracker();
                OpenDataFile();
            }
        }
        catch (Exception exception) {
            ReportError("[EyeLink] Initialization failed: " + exception.Message);
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
        return Calibration(null);
    }

    /// <summary>
    /// Starts calibration and invokes <paramref name="onPointAccepted"/> once
    /// for every collected calibration target. Point numbers are one-based.
    /// </summary>
    public static bool Calibration(Action<int> onPointAccepted)
    {
        return RunTrackerSetupMode("calibration", 'c', false, onPointAccepted);
    }

    /// <summary>
    /// Runs one drift detection/correction at the center of Display 2.
    /// Escape cancels and returns to Unity without opening tracker setup.
    /// </summary>
    /// <returns><c>true</c> when drift correction completes successfully.</returns>
    public static bool DriftDetection()
    {
        return RunCalibrationWindow("drift detection", delegate(
            int width,
            int height,
            EyeLinkCalibrationWindow calibrationWindow,
            SREYELINKLib.ELGDICal calibrationGraphics) {
            TrackerTargetSequence sequence = new TrackerTargetSequence(
                "drift detection", 0, calibrationWindow, calibrationGraphics, null);
            sequence.RunDriftCorrection(
                (short)(width / 2),
                (short)(height / 2));
        });
    }

    /// <summary>Starts validation directly from the EyeLink setup screen.</summary>
    /// <returns><c>true</c> when validation completes successfully.</returns>
    public static bool Validation()
    {
        return RunTrackerSetupMode("validation", 'v', false, null);
    }

    /// <summary>Opens the EyeLink camera/setup screen on Display 2.</summary>
    /// <returns><c>true</c> when the camera setup operation completes successfully.</returns>
    public static bool CameraSetup()
    {
        return RunTrackerSetupMode("camera setup", EnterKey, true, null);
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
            if (CalibrationDisplayWidth > 0 && CalibrationDisplayHeight > 0) {
                SendCalibrationDisplayCoordinatesMessage();
            }
            ReportStatus("[EyeLink] Opened Host EDF " + remoteEdfName + ".");
            return true;
        }
        catch (Exception exception) {
            ReportError("[EyeLink] Could not open the EDF: " + exception.Message);
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
            ReportStatus("[EyeLink] Recording started.");
            return true;
        }
        catch (Exception exception) {
            ReportError("[EyeLink] Could not start recording: " + exception.Message);
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
                ReportStatus("[EyeLink] Recording stopped.");
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
                unityPixels = TrackerToUnityPixels(trackerX, trackerY),
                pupilArea = pupilArea,
                isValid = isValid,
                eltype = sample.eltype
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
    /// Reads queued events independently of newest-sample access. Called every
    /// frame by the lifecycle object and before a cue consumes fixation events.
    /// All SDK queue access stays on Unity's main thread.
    /// </summary>
    public static void PollLinkEvents()
    {
        if (!IsRecording || openDummy || !TryGetEyelinkConnectedStatus())
        {
            return;
        }

        try
        {
            if (activeEye == Eye.EL_EYE_NONE)
            {
                activeEye = (Eye)eyelink.eyeAvailable();
                if (activeEye == Eye.EL_BINOCULAR)
                {
                    activeEye = Eye.EL_RIGHT;
                }
            }

            if (activeEye != Eye.EL_EYE_NONE)
            {
                DrainQueuedLinkData();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[EyeLink] Could not read queued events: " + exception.Message);
        }
    }

    /// <summary>
    /// Reads one retained fixation start by index, including events with missing
    /// positions. Each caller advances its own index, so sample readers cannot
    /// consume another caller's events. Indices reset at the next recording.
    /// </summary>
    public static bool TryGetStartFixationEvent(int eventIndex, out StartFixation fixation)
    {
        if (eventIndex < 0 || eventIndex >= startFixationEvents.Count)
        {
            fixation = default(StartFixation);
            return false;
        }

        fixation = startFixationEvents[eventIndex];
        return true;
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
            mainCamera.WorldToViewportPoint(new Vector3(center.x - extent.x, center.y - extent.y, center.z + extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x + extent.x, center.y - extent.y, center.z + extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x - extent.x, center.y - extent.y, center.z - extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x + extent.x, center.y - extent.y, center.z - extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x - extent.x, center.y + extent.y, center.z + extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x + extent.x, center.y + extent.y, center.z + extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x - extent.x, center.y + extent.y, center.z - extent.z)),
            mainCamera.WorldToViewportPoint(new Vector3(center.x + extent.x, center.y + extent.y, center.z - extent.z))
        };

        Vector2 minimum = screenPoints[0];
        Vector2 maximum = screenPoints[0];
        foreach (Vector2 screenPoint in screenPoints)
        {
            minimum = Vector2.Min(minimum, screenPoint);
            maximum = Vector2.Max(maximum, screenPoint);
        }

        float left = CalibrationDisplayLeft + minimum.x * (GazeDisplayWidth - 1);
        float top = CalibrationDisplayTop + (1f - maximum.y) * (GazeDisplayHeight - 1);
        float right = CalibrationDisplayLeft + maximum.x * (GazeDisplayWidth - 1);
        float bottom = CalibrationDisplayTop + (1f - minimum.y) * (GazeDisplayHeight - 1);
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
                    ReportStatus("[EyeLink] Downloaded EDF to " + localEdfPath + ".");
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
            CalibrationDisplayWidth = 0;
            CalibrationDisplayHeight = 0;
            CalibrationDisplayLeft = 0;
            CalibrationDisplayTop = 0;
            ResetLiveDataForRecording();
            dataFileOpen = false;
            shuttingDown = false;
        }
    }

    private static void ConfigureTracker() {
        eyelink.setOfflineMode();
        // The participant display is measured when its calibration window is
        // visible. Screen.width/height here describe Unity's operator window.
        // Keep the Host's accepted calibration intact on reconnect. Apply the
        // menu's requested type only when a new calibration is started.
        eyelink.sendCommand(
            "file_event_filter = LEFT,RIGHT,FIXATION,SACCADE,BLINK");
        eyelink.sendCommand(
            "link_event_filter = LEFT,RIGHT,FIXATION,FIXUPDATE,SACCADE,BLINK");
        // Request positions on STARTFIX itself. NOSTART would suppress them;
        // sample-data settings do not control the fields included in events.
        eyelink.sendCommand(
            "link_event_data = GAZE,GAZERES,HREF,AREA,VELOCITY,STATUS");
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
    /// blink, saccade, fixation, and lost-data events.
    /// getNewestSample remains the source of low-latency gaze coordinates.
    /// </summary>
    private static void DrainQueuedLinkData()
    {
        for (int itemIndex = 0; itemIndex < MaximumQueuedItemsPerPoll; itemIndex++)
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
                        StoreStartFixation(linkData);
                    }
                    else if (dataType == Eltype.EL_LOST_DATA)
                    {
                        Debug.LogWarning("[EyeLink] Link data was lost; fixation counts may be incomplete.");
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
    }

    /// <summary>
    /// Copies the event before its COM object is released. Missing event
    /// positions remain missing; they are never replaced by a newer sample.
    /// </summary>
    private static void StoreStartFixation(SREYELINKLib.ELData linkData)
    {
        SREYELINKLib.IStartFixationEvent fixation =
            linkData as SREYELINKLib.IStartFixationEvent;
        if (fixation == null || fixation.eye != activeEye)
        {
            return;
        }

        float trackerX = fixation.gstx;
        float trackerY = fixation.gsty;
        ushort readFlags = unchecked((ushort)fixation.read);
        int positionFlags = ReadBeginPosition | ReadGazePosition;
        float missingData = (float)SREYELINKLib.EL_CONSTANT.EL_MISSING_DATA;
        bool isValid = (readFlags & positionFlags) == positionFlags &&
            trackerX != missingData && trackerY != missingData &&
            !Single.IsNaN(trackerX) && !Single.IsNaN(trackerY) &&
            !Single.IsInfinity(trackerX) && !Single.IsInfinity(trackerY);

        startFixationEvents.Add(new StartFixation {
            trackerTime = fixation.sttime,
            eye = fixation.eye,
            readFlags = readFlags,
            trackerPixels = new Vector2(trackerX, trackerY),
            unityPixels = TrackerToUnityPixels(trackerX, trackerY),
            isValid = isValid
        });

        if (!isValid && !warnedMissingStartFixationPosition)
        {
            warnedMissingStartFixationPosition = true;
            Debug.LogWarning("[EyeLink] STARTFIX has no valid start gaze position " +
                "(read=0x" + readFlags.ToString("X4") + ", x=" + trackerX +
                ", y=" + trackerY + "). It is retained but cannot count as a " +
                "fixation on the cue. Check link_event_data includes GAZE and omits NOSTART.");
        }
    }

    private static void ResetLiveDataForRecording()
    {
        lastSampleTime = Double.MinValue;
        HasGazeSample = false;
        LatestGazeSample = default(GazeSample);
        LatestEventType = Eltype.EL_SAMPLE_TYPE;
        hasReceivedLinkEvent = false;
        startFixationEvents.Clear();
        warnedMissingStartFixationPosition = false;
    }

    private static bool RunTrackerSetupMode(
        string operation,
        int setupKey,
        bool startInCameraMode,
        Action<int> onCalibrationPointAccepted)
    {
        return RunCalibrationWindow(operation, delegate(
            int width,
            int height,
            EyeLinkCalibrationWindow calibrationWindow,
            SREYELINKLib.ELGDICal calibrationGraphics) {
            eyelink.setTrackerSetupDefault((short)(startInCameraMode ? 1 : 0));

            try {
                if (startInCameraMode) {
                    eyelink.doTrackerSetup();
                    return;
                }

                TrackerTargetSequence targetSequence = new TrackerTargetSequence(
                    operation,
                    setupKey,
                    calibrationWindow,
                    calibrationGraphics,
                    onCalibrationPointAccepted);
                targetSequence.Run();
            }
            finally {
                eyelink.setTrackerSetupDefault(0);
            }
        }, startInCameraMode);
    }

    private static bool RunCalibrationWindow(
        string operation,
        Action<int, int, EyeLinkCalibrationWindow, SREYELINKLib.ELGDICal>
            trackerAction,
        bool useSdkGraphics = false)
    {
        // Calibration, validation, drift detection, and camera setup all use
        // this same Display 2 window and coordinate system.
        if (!EnsureConnected(operation)) {
            return false;
        }

        if (openDummy) {
            ReportStatus("[EyeLink] " + operation + " skipped in dummy mode.");
            return true;
        }

        string stage = "stopping recording";
        try {
            ReportStatus("[EyeLink] Starting " + operation + ".");
            StopRecording();
            stage = "getting the Unity window";
            IntPtr unityWindow = GetActiveWindow();
            if (unityWindow == IntPtr.Zero) {
                throw new InvalidOperationException(
                    "Unity does not have an active window for the EyeLink setup display.");
            }

            stage = "creating the calibration window";
            using (EyeLinkCalibrationWindow calibrationWindow =
                new EyeLinkCalibrationWindow(unityWindow)) {
                stage = "showing the calibration window";
                calibrationWindow.ShowForCalibration();

                System.Drawing.Size displaySize = calibrationWindow.ClientSize;
                int width = displaySize.Width;
                int height = displaySize.Height;

                stage = "switching the tracker offline";
                eyelink.setOfflineMode();
                // EyeLink coordinates are local to the selected monitor and
                // its calibration window, not global virtual-desktop pixels.
                stage = "sending the calibration screen coordinates";
                ConfigureCalibrationDisplay(calibrationWindow, width, height);
                stage = "sending the display coordinates message";
                SendCalibrationDisplayCoordinatesMessage();
                stage = "waiting for tracker commands to settle";
                eyelinkUtil.pumpDelay(50);

                stage = "creating the GDI calibration interface";
                SREYELINKLib.ELGDICal cal = eyelinkUtil.getGDICal();
                // Preserve the sizes chosen in the settings panel. The getters
                // use the SDK width-based defaults only when no size has been set.
                ReportStatus("[EyeLink] Calibration appearance: display width=" + width +
                    ", diameter=" + CalibrationTargetDiameter + " px, hole=" +
                    CalibrationHoleDiameter + " px; target=#" +
                    ColorUtility.ToHtmlStringRGB(CalibrationTargetColor) + ", background=#" +
                    ColorUtility.ToHtmlStringRGB(CalibrationBackgroundColor) + ".");
                if (useSdkGraphics) {
                    // The SDK owns the window only for camera setup. Manual
                    // target sequences handle their own painting and keyboard input.
                    stage = "assigning the camera setup window handle";
                    cal.setCalibrationWindow(calibrationWindow.Handle.ToInt32());
                    cal.setCalibrationTargetSize((short)CalibrationTargetDiameter,
                        (short)CalibrationHoleDiameter);
                    cal.setCalibrationColors(ToColorRef(CalibrationTargetColor),
                        ToColorRef(CalibrationBackgroundColor));
                    stage = "enabling camera setup key collection";
                    cal.enableKeyCollection(true);
                }

                try {
                    stage = "starting the tracker operation";
                    trackerAction(
                        width,
                        height,
                        calibrationWindow,
                        cal);
                }
                finally {
                    if (useSdkGraphics) {
                        cal.enableKeyCollection(false);
                    }
                }
            }

            ReportStatus("[EyeLink] " + operation + " finished.");
            return true;
        }
        catch (OperationCanceledException) {
            ReportStatus("[EyeLink] " + operation + " cancelled; returning to Unity.");
            return false;
        }
        catch (Exception exception) {
            ReportError(
                "[EyeLink] " + operation + " failed: " +
                FormatException(exception) + " (stage: " + stage + ").");
            Debug.LogException(exception);
            return false;
        }
    }

    private static uint ToColorRef(Color color)
    {
        // Windows RGB()/COLORREF is 0x00BBGGRR, not ARGB. Alpha is unused.
        Color32 rgb = color;
        return (uint)(rgb.r | (rgb.g << 8) | (rgb.b << 16));
    }

    private static void ConfigureCalibrationDisplay(
        EyeLinkCalibrationWindow window, int width, int height)
    {
        if (width <= 0 || height <= 0) {
            throw new InvalidOperationException(
                "The visible EyeLink calibration window has an invalid size: " +
                width + "x" + height + ".");
        }

        string command = "screen_pixel_coords = 0 0 " + (width - 1) + " " + (height - 1);
        ReportStatus(
            "[EyeLink] Calibration window on " + window.ActualMonitorDeviceName +
            ": " + width + "x" + height + " pixels; monitor " +
            window.ActualMonitorBounds.Width + "x" + window.ActualMonitorBounds.Height +
            " at (" + window.ActualMonitorBounds.X + ", " + window.ActualMonitorBounds.Y +
            "). Sending " + command + ".");
        eyelink.sendTimedCommand(TrackerCommandTimeoutMilliseconds, command);
        CalibrationDisplayWidth = width;
        CalibrationDisplayHeight = height;
        CalibrationDisplayLeft = 0;
        CalibrationDisplayTop = 0;
    }

    private static void SendCalibrationDisplayCoordinatesMessage()
    {
        eyelink.sendMessage("DISPLAY_COORDS " + CalibrationDisplayLeft + " " +
            CalibrationDisplayTop + " " + (CalibrationDisplayLeft + CalibrationDisplayWidth - 1) +
            " " + (CalibrationDisplayTop + CalibrationDisplayHeight - 1));
    }

    private static string GetCalibrationFailureDetails()
    {
        string details = CalibrationDisplayWidth > 0 && CalibrationDisplayHeight > 0
            ? " (display " + CalibrationDisplayWidth + "x" + CalibrationDisplayHeight + ")"
            : String.Empty;
        try {
            string hostMessage = eyelink.getCalibrationMessage();
            if (!String.IsNullOrWhiteSpace(hostMessage)) {
                details += " Host: " + hostMessage.Trim();
            }
        }
        catch (Exception) {
            // Keep the original result if the Host has no diagnostic reply.
        }
        return details;
    }

    private static string FormatException(Exception exception)
    {
        if (exception == null) {
            return "Unknown error.";
        }

        string message = exception.Message;
        if (String.IsNullOrWhiteSpace(message)) {
            message = exception.GetType().Name + " (HRESULT 0x" +
                Marshal.GetHRForException(exception).ToString("X8") + ")";
        }
        else {
            message = exception.GetType().Name + ": " + message;
        }

        if (exception.Data.Contains("EyeLinkSdkCall")) {
            message += " (SDK call: " + exception.Data["EyeLinkSdkCall"] + ")";
        }

        if (exception.InnerException != null) {
            message += " Inner error: " + FormatException(exception.InnerException);
        }

        return message;
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

    private static void ReportStatus(string message)
    {
        if (Console.Instance != null) {
            Console.Write(message);
        }
        else {
            Debug.Log(message);
        }
    }

    private static void ReportError(string message)
    {
        if (Console.Instance != null) {
            Console.WriteError(message);
        }
        else {
            Debug.LogError(message);
        }
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
    private void Update()
    {
        EyeLink.PollLinkEvents();
    }

    private void OnApplicationQuit()
    {
        EyeLink.Shutdown();
    }

    private void OnDestroy()
    {
        EyeLink.Shutdown();
    }
}
