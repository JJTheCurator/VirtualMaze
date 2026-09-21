using UnityEngine;
using System;

public class WrongRewardAreaError : MonoBehaviour
{
    //public CheckPoster checkPoster;
    public AudioClip errorClip;
    public RewardArea rewardArea;
    private static CueController cueController;
    private static RewardsController rewardsController;
    private static ExperimentController experimentController;
    private static NonTargetRaycast nonTargetRaycast;
    private static LevelController levelController;
    private float timer = 1000f;
    public bool isSoundTriggered = false;
    private int _sessionIndex = 0;


    // Number and duration of blinks
    int numBlinks = 2;
    private float overallBlinkDuration = 0.5f;

    void Start()
    {
        cueController = GameObject.FindObjectOfType(typeof(CueController)) as CueController;
        experimentController = GameObject.FindObjectOfType(typeof(ExperimentController)) as ExperimentController;
        nonTargetRaycast = GameObject.FindObjectOfType(typeof(NonTargetRaycast)) as NonTargetRaycast;
        rewardsController = GameObject.FindObjectOfType(typeof(RewardsController)) as RewardsController;
        levelController = GameObject.FindObjectOfType(typeof(LevelController)) as LevelController;
    }

    void Update()
    {
        timer += Time.deltaTime;
        HintBlink();

        if (levelController?.errorFlag == false)
        {
            isSoundTriggered = true; // doesn't immediately sound in new trial but the robot in the wrong area already
        }

        if (experimentController != null)
        {
            if (_sessionIndex != experimentController.sessionController.index && !experimentController.resetPositionOnSession) // checks if index has changed
            {
                isSoundTriggered = true; // doesn't immediately sound in new session but the robot in the wrong area already
            }
        }

        if (levelController != null)
        {
            if (LevelController.sessionStarted == false)
            {
                Reset();
            }
        }

        if (_sessionIndex != null)
        {
            _sessionIndex = experimentController.sessionController.index; // sets new value to test for next iteration
        }
    }


    /// <summary>
    /// Triggers error sound when the robot enters the trigger zone, commented out as this is no longer required with OnTriggerStay introduced
    /// </summary>
    /// <param name="other"></param>
    /*
    private void OnTriggerEnter(Collider other)
    {
        if (LevelController.sessionStarted && !isSoundTriggered
            && experimentController.enableRewardAreaError)

        {
            string areaPosterImage = rewardArea.cueImage.name;
            //Debug.Log(areaPosterImage);
            string cueImage = CueImage.cueImage;
            //Debug.Log(cueImage);
            if (areaPosterImage != cueImage)
            
            {
                Vector3 direction = rewardArea.transform.position - other.transform.position;
                direction.y = 0;
                float angle = Vector3.Angle(direction, other.transform.forward);
                float distance = Vector3.Magnitude(direction);
                if (Math.Abs(angle) < RewardArea.RequiredViewAngle * 0.5f)
                {
                    if (distance <= RewardArea.RequiredDistance)
                    {
                        Debug.Log("TriggerEnter Ping");
                        PlayerAudio.instance.PlayErrorClip();
                        timer = 0f;
                        isSoundTriggered = true;
                    }
                    
                }
            }
        }
    }*/



    /// <summary>
    /// Triggers error sound when the robot is within the collider (Called every frame)
	/// Trigger flag is resetted within the collider when the robot looks away or moves out of the required distance
    /// </summary>
    /// <param name="other"></param>
    private void OnTriggerStay(Collider other)
    {
        // Debug.Log("Disable Hint: " + experimentController.disableHint);
        Vector3 direction = rewardArea.transform.position - other.transform.position;
        direction.y = 0;
        float angle = Vector3.Angle(direction, other.transform.forward);
        float distance = Vector3.Magnitude(direction);
        //Debug.Log(angle);

        if (LevelController.sessionStarted && !isSoundTriggered
            && rewardsController.enableRewardAreaError && nonTargetRaycast.errorFlag.ToString() == "True")
            // global variable nonTargetRaycast.errorFlag does not directly interact with scripts unless used as string
        {
            // Debug.Log(angle);
            string areaPosterImage = rewardArea.cueImage.name;
            string cueImage = CueImage.cueImage;
            if (areaPosterImage != cueImage)
            {
                if (angle < RewardArea.RequiredViewAngle)
                {
                    // Debug.Log(distance);
                    // Debug.Log(angle);
                    if (distance <= RewardArea.RequiredDistance)
                    {
                        WrongPoster();
                        isSoundTriggered = true;
                    }
                }
            }
        }

        if (LevelController.sessionStarted && isSoundTriggered)
        {
            if (distance > RewardArea.RequiredDistance || angle > RewardArea.RequiredViewAngle)
                // resets the flag when robot stays within the collider but moves beyond RequiredDistance and RequiredViewAngle
            {
                isSoundTriggered = false;
            }
        }
        //Debug.Log(isSoundTriggered);
    }
    

    private void OnTriggerExit(Collider other)
    {
        isSoundTriggered = false;
    }

    private void WrongPoster()
    {
        Debug.Log("TriggerStay Ping");
        PlayerAudio.instance.PlayErrorClip();
        timer = 0f;
    }

    private void HintBlink()
    {
        for (int i = 0; i < numBlinks; i++)
        {
            if (timer >= (i * overallBlinkDuration) && timer < (((2 * i) + 1) * overallBlinkDuration / 2))
            {
                if (experimentController.disableHint) { cueController.ShowHint(); }
                else if (!experimentController.disableHint) { cueController.HideHint(); }
            }
            if (timer >= (((2 * i) + 1) * overallBlinkDuration / 2) && timer < ((i + 1) * overallBlinkDuration))
            {
                if (experimentController.disableHint) { cueController.HideHint(); } // if disableHint is true, end with hide hint
                else if (!experimentController.disableHint) { cueController.ShowHint(); } // if disableHint is false, end with show hint
            }
        }
    }

    private void Reset()
    {
        timer = 1000f;
    }
}
