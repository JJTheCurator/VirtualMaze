using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Class for encapsulation the data required to parse and unparse settings to be logged into
/// the session Logs.
/// </summary>
[Serializable]
public class SessionContext {
    public string Version;
    public string TriggerVersion;
    public string TaskType;
    public string MazeName;
    public string NumberOfTrials; //number of trials
    public List<PosterLocation> PosterLocations = new List<PosterLocation>();
    public string CompletionWindow;
    public string TimeoutDuration;
    public string IntersessionInterval;
    public string RewardTime;
    public string RewardViewCriteria;
    public string RotationSpeed;
    public string MovementSpeed;
    public string JoystickDeadzone;
    public string AngleRestrictionAmount;
    public string RequiredDistance;
    public string ProximityDistance;
    public string DirectionErrorDistance;
    public string isJoystickEnabled;
    public string isReverseEnabled;
    public string isForwardEnabled;
    public string isRightEnabled;
    public string isLeftEnabled;
    public string isYInverted;
    public string isXInverted;
    public string RestartOnTrialFail;
    public string ResetPositionOnTrial;
    public string FaceRandomDirectionOnStart;
    public string MultipleWaypoints;
    public string DisableInterSessionBlackout;
    public string ResetPositionOnSession;
    public string EnableDirectionError;
    public string DisableHint;
    public string EnableRewardAreaError;
    public string RewardAreaErrorTime;


    //regex to extract information from string
    // any word ( float or int, float or int, float or int )
    private const string posterRegex = @"(\w+)\(([-+]?[0-9]*\.?[0-9]+),([-+]?[0-9]*\.?[0-9]+),([-+]?[0-9]*\.?[0-9]+)\)";

    public SessionContext(Session session, ExperimentSettings settings, RewardArea[] rewards) {
        Version = GameController.versionInfo + VersionInfo.Version.ToString();
        TriggerVersion = GameController.pportInfo;
        GetExperimentSettings(settings);

        if (ResetPositionOnTrial == "False") {
            TaskType = "Continuous";
        }
        else {
            TaskType = "Discontinuous";
        }

        MazeName = session.maze.MazeName;
        NumberOfTrials = session.numTrials.ToString();

        foreach (RewardArea reward in rewards) {
            if (reward.target != null) {
                PosterLocations.Add(new PosterLocation(reward.target.position, reward.cueImage.name));
            }
        }

        GetJoystickSettings(settings);
        GetRobotMovementSettings(settings);
        GetRewardSettings(settings);
    }

    /// <summary>
    /// Creates a SessionContext with the old header where data is stored line by line
    /// </summary>
    /// <param name="currentline"></param>
    /// <param name="reader"></param>
    public SessionContext(string currentLine, StreamReader reader) {
        string line = currentLine;
        Version = GetValue(line);
        /*
        line = reader.ReadLine();
        TriggerVersion = GetValue(line);

        line = reader.ReadLine();
        TaskType = GetValue(line);

        line = reader.ReadLine();
        if (!ProcessPosterLocations(PosterLocations, GetValue(line))) {
            throw new FormatException();
        }

        line = reader.ReadLine();
        MazeName = GetValue(line);

        line = reader.ReadLine();
        NumTrial = int.Parse(GetValue(line));

        line = reader.ReadLine();
        CompletionWindow = int.Parse(GetValue(line));

        line = reader.ReadLine();
        TimeoutDuration = int.Parse(GetValue(line));

        line = reader.ReadLine();
        IntersessionInterval = int.Parse(GetValue(line));

        line = reader.ReadLine();
        RewardTime = int.Parse(GetValue(line));

        line = reader.ReadLine();
        RotationSpeed = float.Parse(GetValue(line));

        line = reader.ReadLine();
        MovementSpeed = float.Parse(GetValue(line));

        line = reader.ReadLine();
        JoystickDeadzone = float.Parse(GetValue(line));

        line = reader.ReadLine();
        RewardViewCriteria = float.Parse(GetValue(line));
        */
    }

    private bool ProcessPosterLocations(List<PosterLocation> PosterLocations, string line) {
        PosterLocations.Clear();
        MatchCollection matches = Regex.Matches(line, posterRegex, RegexOptions.IgnoreCase);

        foreach (Match match in matches) {
            Vector3 location = Vector3.zero;

            if (!float.TryParse(match.Groups[2].Value, out location.x)) {
                return false;
            }
            if (!float.TryParse(match.Groups[3].Value, out location.y)) {
                return false;
            }

            if (!float.TryParse(match.Groups[4].Value, out location.z)) {
                return false;
            }

            PosterLocation pLoc = new PosterLocation(location, match.Groups[1].Value);

            PosterLocations.Add(pLoc);
        }
        return true;
    }



    /******************************************************/
    /// <summary>
	/// This function splits keys and values from the header string's into a collection and
	/// recontructs a new header.

    public string ToJsonString() {
        string header;
        string newHeader = "";
        string temp;
        bool keyFlag = true;
        header = JsonUtility.ToJson(this, true);
        var reg = new Regex("\".*?\""); // specifies that selection is done to the strings encased in ""
        var matches = reg.Matches(header);
        foreach (var item in matches)
        {
            temp = item.ToString(); // converts Match to string, but contains "" in string
            if (keyFlag) // checks if the item is supposed to be a key
            {
                newHeader += temp.Substring(1, temp.Length - 2) + ": "; // removes "" from the string and add separator to newHeader
                keyFlag = false;
            }
            else
            {
                newHeader += temp.Substring(1, temp.Length - 2) + "\n"; // removes "" from the string and add new line to newHeader
                keyFlag = true;
            }

            if (temp == "\"PosterLocations\"")
            {
                newHeader += "\n";
                keyFlag = true; // sets keyFlag to true again as posterLocation has an entire list as its value.
            }
            
        }
        return newHeader + "--- Start of Dataset --- \n";
    }
    /*******************************************************/



    //helper methods to log required settings
    private void GetJoystickSettings(ExperimentSettings settings) {
        if (settings.TryGetComponentSetting(out JoystickController.Settings joystickSettings)) {
            // Add new joystick variable here.
        }
        else {
            //this values are a must to be logged. Therefore an exception is thrown.
            throw new SaveLoad.SettingNotFoundException("JoystickController.Settings not found");
        }
    }

    private void GetRobotMovementSettings(ExperimentSettings settings) {
        if (settings.TryGetComponentSetting(out RobotMovement.Settings movementSettings)) {
            RotationSpeed = movementSettings.rotationSpeed.ToString();
            MovementSpeed = movementSettings.movementSpeed.ToString();
            JoystickDeadzone = movementSettings.deadzoneAmount.ToString();
            AngleRestrictionAmount = movementSettings.angleRestrictionAmount.ToString();
            isJoystickEnabled = movementSettings.isJoystickEnabled.ToString();
            isReverseEnabled = movementSettings.isReverseEnabled.ToString();
            isForwardEnabled = movementSettings.isForwardEnabled.ToString();
            isRightEnabled = movementSettings.isRightEnabled.ToString();
            isLeftEnabled = movementSettings.isLeftEnabled.ToString();
            isYInverted = movementSettings.isYInverted.ToString();
            isXInverted = movementSettings.isXInverted.ToString();
        }
        else {
            //this values are a must to be logged. Therefore an exception is thrown.
            throw new SaveLoad.SettingNotFoundException("RobotMovement.Settings not found");
        }
    }

    private void GetRewardSettings(ExperimentSettings settings) {
        if (settings.TryGetComponentSetting(out RewardsController.Settings rewardSettings)) {
            RewardTime = rewardSettings.rewardDurationMilliSecs.ToString();
            RewardViewCriteria = rewardSettings.requiredViewAngle.ToString();
            RequiredDistance = rewardSettings.requiredDistance.ToString();
            ProximityDistance = rewardSettings.proximityDistance.ToString();
            DirectionErrorDistance = rewardSettings.directionErrorDistance.ToString();
            EnableRewardAreaError = rewardSettings.enableRewardAreaError.ToString();
            RewardAreaErrorTime = rewardSettings.rewardAreaErrorTime.ToString();
        }
        else {
            //this values are a must to be logged. Therefore an exception is thrown.
            throw new SaveLoad.SettingNotFoundException("RewardsController.Settings not found");
        }
    }

    private void GetExperimentSettings(ExperimentSettings settings) {
        if (settings.TryGetComponentSetting(out ExperimentController.Settings experimentSettings)) {
            CompletionWindow = experimentSettings.timeLimitDuration.ToString();
            TimeoutDuration = experimentSettings.timeoutDuration.ToString();
            IntersessionInterval = experimentSettings.sessionIntermissionDuration.ToString();

            RestartOnTrialFail = experimentSettings.restartOnTrialFail.ToString();
            ResetPositionOnTrial = experimentSettings.resetPositionOnTrial.ToString();
            FaceRandomDirectionOnStart = experimentSettings.faceRandomDirectionOnStart.ToString();
            MultipleWaypoints = experimentSettings.multipleWaypoints.ToString();
            DisableInterSessionBlackout = experimentSettings.disableInterSessionBlackout.ToString();
            ResetPositionOnSession = experimentSettings.resetPositionOnSession.ToString();
            EnableDirectionError = experimentSettings.enableDirectionError.ToString();
            DisableHint = experimentSettings.disableHint.ToString();
        }
        else {
            //this values are a must to have. Therefore an exception is thrown
            throw new SaveLoad.SettingNotFoundException("ExperimentController.Settings not found");
        }
    }

    private string GetValue(string line) {
        //get second index where the value resides
        return GetValue(line, ':');
    }

    private string GetValue(string line, char delimiter) {
        //get second index where the value resides
        return line.Split(delimiter)[1].Trim();
    }

    [Serializable]
    public struct PosterLocation {
        public string name;
        public string posterPosition;
        public PosterLocation(Vector3 position, string name) {
            this.name = name;
            this.posterPosition = $"({position.x}, {position.y}, {position.z})";
        }

        public override string ToString() {
            return $"{name}, {posterPosition}";
        }
    }
}
