using System;
using System.Collections;
using UnityEngine;

public class ExperimentController : ConfigurableComponent {
    [Serializable]
    public class Settings : ComponentSettings {
        public bool isTrialIntermissionFixed;
        public bool restartOnTrialFail;
        public bool resetPositionOnTrial;
        public bool faceRandomDirectionOnStart;
        public int maxAngularDeviation;
        public bool multipleWaypoints;
        public bool disableInterSessionBlackout;
        public bool resetPositionOnSession;
        public bool enableDirectionError;
        public bool disableHint;
  
        public int fixedTrialIntermissionDuration;
        public int maxTrialIntermissionDuration;
        public int minTrialIntermissionDuration;

        public string saveLocation;
        public int sessionIntermissionDuration;
        public int timeoutDuration;
        public int timeLimitDuration;

        public Settings(
            bool isTrialIntermissionFixed,
            bool restartOnTrialFail,
            bool resetPositionOnTrial,
            bool faceRandomDirectionOnStart,
            int maxAngularDeviation,
            bool multipleWaypoints,
            bool disableInterSessionBlackout,
            bool resetPositionOnSession,
            bool enableDirectionError,
            bool disableHint,
            int fixedTrialIntermissionDuration,
            int maxTrialIntermissionDuration,
            int minTrialIntermissionDuration,
            int sessionIntermissionDuration,
            int timeoutDuration,
            int timeLimitDuration,
            string saveLocation
            ) {
            this.isTrialIntermissionFixed = isTrialIntermissionFixed;
            this.restartOnTrialFail = restartOnTrialFail;
            this.resetPositionOnTrial = resetPositionOnTrial;
            this.faceRandomDirectionOnStart = faceRandomDirectionOnStart;
            this.maxAngularDeviation = maxAngularDeviation;
            this.multipleWaypoints = multipleWaypoints;
            this.disableInterSessionBlackout = disableInterSessionBlackout;
            this.resetPositionOnSession = resetPositionOnSession;
            this.enableDirectionError = enableDirectionError;
            this.disableHint = disableHint;
            this.saveLocation = saveLocation;

            this.fixedTrialIntermissionDuration = fixedTrialIntermissionDuration;
            this.maxTrialIntermissionDuration = maxTrialIntermissionDuration;
            this.minTrialIntermissionDuration = minTrialIntermissionDuration;
            this.sessionIntermissionDuration = sessionIntermissionDuration;
            this.timeoutDuration = timeoutDuration;
            this.timeLimitDuration = timeLimitDuration;
        }
    }

    public bool restartOnTrialFail;
    public bool resetPositionOnTrial;
    public bool faceRandomDirectionOnStart;
    public int maxAngularDeviation;
    public bool multipleWaypoints;
    public bool disableInterSessionBlackout;
    public bool resetPositionOnSession;
    public bool enableDirectionError;
    public bool disableHint;
    public string SaveLocation { get; set; }
    public int SessionIntermissionDuration { get; set; }

    public bool started { get; private set; } = false;
    private ExperimentLogger logger = new ExperimentLogger();

    //coroutine reference for properly stopping coroutine
    private Coroutine goNextLevelCoroutine;

    // Caches the SessionTrigger for logging the trigger with the robot movement
    private SessionTrigger triggerCache = SessionTrigger.NoTrigger;
    private int rewardIndexCache = 0; // value will be ignored when NoTrigger, 0 based

    private bool isPaused = false;

    private WaitUntil waitIfPaused;

    //drag in Unity Editor
    public SessionController sessionController;
    public LevelController lvlController;
    public DirectionError directionError;

    [SerializeField]
    private RobotMovement robot = null;

    protected override void Awake() {
        base.Awake();
        waitIfPaused = new WaitUntil(() => !isPaused);
        
        print(VersionInfo.Version);
    }

    /// <summary>
    /// Toggles if the experiment should pause
    /// </summary>
    /// <returns>returns true if preparing to pause</returns>
    public bool TogglePause() {
        isPaused = !isPaused;

        if (isPaused) {
            robot.OnRobotMoved -= OnRobotMoved;
        }
        else {
            robot.OnRobotMoved += OnRobotMoved;
        }

        lvlController.isPaused = isPaused;
        return isPaused;
    }

    public void StartExperiment() {
        //ignore btn click if already started.
        if (started) return;
        started = true;
        sessionController.RestartIndex();

        if (logger != null) {
            //cleanup
            logger.CloseLog();
        }

        //initilize ExperimentLogger
        logger.SetExperimentIdDefault();
        logger.SetSaveLocation(SaveLocation);

        goNextLevelCoroutine = StartCoroutine(GoToNextSession());
    }

    private IEnumerator GoToNextSession() {
        // checks if should pause else continue.
        if (isPaused) {
            Console.Write("ExperimentPaused");
        }
        yield return waitIfPaused;

        while (sessionController.HasNextLevel() && started) {
            Session session = sessionController.NextLevel();
            int sessionIndex = sessionController.index;

            //delay and display countdown
            float countDownTime = SessionIntermissionDuration / 1000.0f;

            yield return SessionStatusDisplay.Countdown("Starting Session", countDownTime);
            SessionStatusDisplay.DisplaySessionNumber(sessionIndex);

            while (FadeCanvas.fadeCanvas.isTransiting) {
                yield return null;
            }

            PrepareLevelController();

            // Load the maze before collecting its rewards for the session header.
            yield return lvlController.PrepareSession(session);

            SessionContext context = new SessionContext(session, SaveLoad.getCurrentSettings(), lvlController.rewards);
            if (!logger.OpenSessionLog(sessionIndex, context)) {
                Console.WriteError("failed to create save files");
                StopExperiment();
                yield break;
            }

            //start logging robotmovement
            robot.OnRobotMoved += OnRobotMoved;

            //start the scene
            yield return lvlController.RunPreparedSession();

            robot.OnRobotMoved -= OnRobotMoved;
            logger.CloseLog();
        }

        StopExperiment();

    }

    private void PrepareLevelController() {
        if (lvlController == null) {
            Debug.LogError("No GameObject found with the tag " + Tags.LevelController);
            StopExperiment();
        }
        else {
            lvlController.onSessionTrigger.AddListener(OnSessionTriggered);
            lvlController.isPaused = isPaused;
            lvlController.resetRobotPositionDuringInterTrial = resetPositionOnTrial;
            lvlController.restartOnTaskFail = restartOnTrialFail;
            lvlController.faceRandomDirectionOnStart = faceRandomDirectionOnStart;
            lvlController.maxAngularDeviation = maxAngularDeviation;
            lvlController.multipleWaypoints = multipleWaypoints;
            lvlController.disableInterSessionBlackout = disableInterSessionBlackout;
            lvlController.resetPositionOnSession = resetPositionOnSession;
            lvlController.disableHint = disableHint;

            directionError.enableDirectionError = enableDirectionError;
            directionError.disableHint = disableHint;
        }
    }

    public void StopExperiment() {
        Debug.Log("Experiment Stopped");
        //coroutine will be not be null if coroutine is still running
        if (goNextLevelCoroutine != null) {
            StopCoroutine(goNextLevelCoroutine);
        }

        robot.OnRobotMoved -= OnRobotMoved;
        lvlController.StopLevel();
        directionError.Reset();
        started = false;
        //Clean up when Experiment is stopped adruptly.
        logger.CloseLog();
    }

    private void OnRobotMoved(Transform t) {
        //needs to be cached as this thid data is logged on every robotmovement.
        switch (triggerCache) {
            case SessionTrigger.NoTrigger:
                logger.LogMovement(t);
                break;
            case SessionTrigger.ExperimentVersionTrigger:
                logger.LogMovement(triggerCache, GameController.versionNum, t);
                // Consume the trigger
                triggerCache = SessionTrigger.NoTrigger;
                break;
            default:
                // logs need rewardIndex to be 1 based
                logger.LogMovement(triggerCache, rewardIndexCache + 1, t);
                // Consume the trigger
                triggerCache = SessionTrigger.NoTrigger;
                break;
        }
    }

    private void OnSessionTriggered(SessionTrigger trigger, int targetIndex) {
        // cache the trigger to be logged with the robot movement
        triggerCache = trigger;
        rewardIndexCache = targetIndex;
    }

    public void ActivateExternalDisplay() {
        if (Display.displays.Length > 1) {
            Display d = Display.displays[1];
            if (!d.active) {
                d.Activate();
            }
        }
    }

    public override Type GetSettingsType() {
        return typeof(Settings);
    }

    public override ComponentSettings GetDefaultSettings() {
        return new Settings(false, true, true, false,180, false,true, false, false, false, -1, -1, -1, -1, -1, -1, "");
    }

    public override ComponentSettings GetCurrentSettings() {
        return new Settings(Session.isTrailIntermissionRandom, restartOnTrialFail, resetPositionOnTrial, faceRandomDirectionOnStart,
            maxAngularDeviation, multipleWaypoints, disableInterSessionBlackout, resetPositionOnSession, enableDirectionError, disableHint,
            Session.fixedTrialIntermissionDuration, Session.maxTrialIntermissionDuration,
            Session.minTrialIntermissionDuration, SessionIntermissionDuration,
            Session.timeoutDuration, Session.trialTimeLimit, SaveLocation);
    }

    protected override void ApplySettings(ComponentSettings loadedSettings) {
        Settings settings = (Settings)loadedSettings;

        Session.isTrailIntermissionRandom = settings.isTrialIntermissionFixed;
        restartOnTrialFail = settings.restartOnTrialFail;
        resetPositionOnTrial = settings.resetPositionOnTrial;
        faceRandomDirectionOnStart = settings.faceRandomDirectionOnStart;
        maxAngularDeviation = settings.maxAngularDeviation;
        multipleWaypoints = settings.multipleWaypoints;
        disableInterSessionBlackout = settings.disableInterSessionBlackout;
        resetPositionOnSession = settings.resetPositionOnSession;
        enableDirectionError = settings.enableDirectionError;
        disableHint = settings.disableHint;
        Session.fixedTrialIntermissionDuration = settings.fixedTrialIntermissionDuration;
        Session.maxTrialIntermissionDuration = settings.maxTrialIntermissionDuration;
        Session.minTrialIntermissionDuration = settings.minTrialIntermissionDuration;
        SessionIntermissionDuration = settings.sessionIntermissionDuration;
        Session.timeoutDuration = settings.timeoutDuration;
        Session.trialTimeLimit = settings.timeLimitDuration;
        SaveLocation = settings.saveLocation;
    }
}
