# User Guide
* [EyeLink setup buttons](#eyelink-setup-buttons)
* [Session Logs](#Session-Logs)
* [Adding new maze scenes](#Adding-new-maze-scenes)
* [Adding/Editing GUI](#Adding/Editing-GUI)
* [Adding new posters](#Adding-new-posters)
* [Modify scene lighting](#Modify-scene-lighting)

## EyeLink setup buttons

EyeLink setup uses Display 2 when available and stops any current recording.
After the calibration window is shown, VirtualMaze identifies the monitor
containing it and sends that window's measured pixel dimensions to EyeLink.
Both `screen_pixel_coords` and EDF `DISPLAY_COORDS` use local coordinates
`0 0 (width - 1) (height - 1)`. Unity's main-window dimensions are not sent.
The log records the monitor, calibration area, and exact command.

* Select one calibration mode: **H3** (three points in a horizontal line),
  **HV3** (three points in a triangle), **HV5** (five points in a cross),
  **HV9** (nine-point grid), or **HV13** (13 points). The choice is saved with
  experiment settings; older settings default to HV9.
* **Calibration Target Color** selects the dot's foreground color. The background
  is automatically black for lighter targets and white for darker targets. The
  default is a black target on white. Older presets reuse their saved color
  selection as the target color.
* **Diameter** and **Hole Size** sliders set the outer dot and center hole
  diameters in participant-display pixels. The outer dot is at least three pixels
  wide; the hole is at least two pixels wide and always smaller than the dot.
  Reducing the outer diameter also clamps the hole size when necessary.
  Both values are saved with experiment settings. Defaults use
  `diameter = width / 60` and `hole = max(2, width / 300)`, using integer division.
  The center hole uses the background color.
  The log records the applied sizes/colors, the first tracker target state, and
  each target draw position. An out-of-window target reports an error.
  Appearance changes apply when the next setup operation starts.
  Calibration, validation, and drift targets are painted by the native window
  and restored on Windows repaint events. The SDK still handles camera images
  and beeps; target visibility does not depend on its GDI target drawing.
* **Calibration** sets the Host to the selected calibration type after
  configuring the display coordinates, opens tracker setup, waits
  one second after it is ready, and sends the tracker's `c` key to start
  calibration. It reapplies the selected type on each click, including after a
  manual change on the Host; no manual matching of the Host type is required.
  When the sequence finishes, the calibration window stays open for review.
  Click **Accept** on the EyeLink Host to keep the values and return to Unity;
  **Escape** cancels/discards. VirtualMaze does not automatically press Enter
  or Escape after a completed calibration or validation.
* **Validation** follows the same sequence and sends `v`.
* **Drift Detection** displays one target at the center of Display 2 and checks
  gaze against that position using the current calibration. Fixate the target
  and press **Space** to accept it. A successful result is applied through the
  EyeLink SDK. Whether the tracker checks drift or adjusts gaze depends on its
  configuration.
* **Camera Setup** opens the tracker camera/setup interface.

When Calibration Reward is enabled, every accepted calibration point uses the
same reward duration (in milliseconds) configured in the Hardware tab. That
duration is saved in the Hardware reward settings.

**Draw Gaze Circle** controls the gaze overlay only; **Calibration Reward**
controls reward pulses only. The two toggles are independent.

Calibration is held by the EyeLink Host, which supplies calibrated gaze pixel
coordinates to Unity. Recording starts through the original SDK sequence;
it does not query or check display dimensions before starting a trial. Gaze
mapping uses the setup-window dimensions when available, with the original
Unity-window fallback before a setup window has been measured.

Drift Detection returns to Unity after one result. **Escape** cancels it, and
an unanswered check times out after 60 seconds. It does not automatically open
setup, start calibration, or repeat the check. If it fails, use Calibration or
Camera Setup explicitly before trying again. Escape also cancels calibration
or validation. Start recording again when ready to resume the experiment.

## Session Logs
The session logs contains data describing setup of the session and the location of the robot in VirtualMaze.

The data in the file is formatted as follows:
~~~
<Session Information>
<flag> <timeElapsed(ms)> <position-x> <position-z> <rotation-y>
<flag> <timeElapsed(ms)> <position-x> <position-z> <rotation-y>
~~~

### Session Information V4.1
As of version 4.1, the data in the JsonObject contains:

~~~~
// Raw JSON in logs (should exist as a single line)
{"version":"20171221 Taxi Continuous v4","triggerVersion":"v4","taskType":"Continuous","posterLocations":[{"name":"CatPoster","position":{"x":-5.0,"y":1.850000023841858,"z":-7.546999931335449}}],"trialName":"Double Tee","rewardsNumber":5,"completionWindow":10000,"timeoutDuration":3000,"intersessionInterval":1000,"rewardTime":1000,"rewardDistance":0,"rewardViewCriteria":100.0,"rotationSpeed":200.0,"movementSpeed":10.0,"joystickDeadzone":0.0}

//Pretty printed version (does not exists in logs)
{
  "version": "20171221 Taxi Continuous v4",
  "triggerVersion": "v4",
  "taskType": "Continuous",
  "posterLocations": [
    {
      "name": "CatPoster",
      "position": {
        "x": -5,
        "y": 1.8500000238419,
        "z": -7.5469999313354
      }
    }
  ],
  "trialName": "Double Tee",
  "rewardsNumber": 5,
  "completionWindow": 10000,
  "timeoutDuration": 3000,
  "intersessionInterval": 1000,
  "rewardTime": 1000,
  "rewardDistance": 0,
  "rewardViewCriteria": 100,
  "rotationSpeed": 200,
  "movementSpeed": 10,
  "joystickDeadzone": 0
}
~~~~

#### Usage
Due to the format of the data, the first line of the session log can be read and parsed as a JsonObject, therefore allowing fast access to the session details without the need to manually parse the values line by line.

###### Matlab
~~~~
logs = fopen(*path to file here*);
nextline = fgetl(logs);
sessionSettings = jsondecode(nextline);
*read next lines and process data*
~~~~

### Session Information V4 (Depreciated)
In version 4, the details of the experiment is stored in plain text and should be parsed line by line. This serves as documentation for experiment data that is generated by VirtualMaze V4. The data is structured as follows.

~~~~
Version: 20171221 Taxi Continuous v4
Trigger: v4
TaskType: Continuous
PosterLocations: P1(-5,1.5,-7.55) P2(-7.55,1.5,5) P3(7.55,1.5,-5) P4(5,1.5,7.55) P5(5,1.5,2.45) P6(-5,1.5,-2.45)
TrialType: Double Tee
SpecifiedRewardNo: 400
CompletionWindow: 25000
TimeoutDuration: 4000
IntersessionInterval: 500
RewardTime: 1000
RotationSpeed: 60.4234
TranslationSpeed: 5.218029
JoystickDeadzone: 0.3033634
RewardViewCriteria: 1
~~~~

## Adding new maze scenes
1. Create new maze scene
2. Create new asset file for that maze in ScriptableObjects
3. In ScriptableObjects > Masterlist, increase the size and assign the new maze to the latest element
4. In File > Build Settings > Scenes in Build, ensure that the new maze is selected and click 'Add Open Scenes'

## Adding/Editing GUI
1. In Start Scene > Persist > ExperimentControlCanvas, open the prefab menu, and add/edit/remove GUI elements
2. Exit the prefab menu, and set the size, position, and text of the GUI element (left, right, Pos Y, Height)
3. In the respective GUI Controller script, add the new GUI element in all the necessary places (Tip: copy-paste the existing GUI elements and change the variable/function names according)
4. In the respective Controller script, add the new variable in the Settings class
5. From the respective Controller script, the equivalent variable used in another script can be linked to the variable in this script (for convenience, link the variables in *private void PrepareLevelController()* function as it is called only once at the start of each session)

## Adding new posters
1. Add new poster images to Assets > Sprites
2. In Assets > Sprites > Materials, create/duplicate a material object and set the albedo in Inspector to the correct sprite

## Modify scene lighting
1. In Hierarchy of maze scene, add/move/remove CeilingLamps, CeilingLampBuffer, and Light Probe Group
2. In Window > Rendering > Lighting Settings, modify the settings (if necessary) and click Generate Lighting (note: VirtualMaze uses Baked Lightmaps instead of Realtime Lightmaps)
