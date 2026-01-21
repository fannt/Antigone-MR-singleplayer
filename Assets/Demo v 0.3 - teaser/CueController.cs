using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

[System.Serializable]
public class Cue
{
    public string cueName = "Cue";
    public AudioSource audio;
    public VideoPlayer video;
    public GameObject gameObject;
    public float duration = 0f;   // duration in seconds for locking cue system

    public float audioDelay = 0f;        // delay before audio starts
    public bool goToCue = false;     // automatically trigger next cue when done
    public bool setActiveOnStart = true; // true = activate on start, false = deactivate on start
    public UnityEvent onStart;
    public UnityEvent onEnd;
}

public class CueController : MonoBehaviour
{

    public bool autoStart = false;
    public bool runSequentialy = true;
    public Cue[] cues;

    private void Start()
    {
        if (autoStart)
        {
            TriggerCue(0);
        }
    }

    private int nextCueIndex = 0;
    private bool cueRunning = false;
    private float cueTimer = 0f;

    public void TriggerCue(int index)
    {
        Debug.Log($"TriggerCue({index}) called");
        if (cueRunning)
        {
            Debug.Log("Cue blocked — another cue is running.");
            return;
        }

        if (runSequentialy && index != nextCueIndex)
        {
            Debug.Log($"Skipping cue {index}, waiting for cue {nextCueIndex}");
            return;
        }

        if (index < 0 || index >= cues.Length)
        {
            Debug.LogWarning("Cue index out of range");
            return;
        }

        StartCoroutine(RunCue(index));
    }

    private System.Collections.IEnumerator RunCue(int index)
    {
        Cue cue = cues[index];
        cueRunning = true;
        Debug.Log($"Cue {index} START: {cue.cueName}");
        if (cue.onStart != null)
        {
            cue.onStart.Invoke();
        }

        if (cue.gameObject != null)
        {
            if (cue.setActiveOnStart)
            {
                Debug.Log($"Cue {index}: activating object {cue.gameObject.name}");
                cue.gameObject.SetActive(true);
            }
            else
            {
                Debug.Log($"Cue {index}: deactivating object {cue.gameObject.name}");
                cue.gameObject.SetActive(false);
            }
        }
        else
        {
            Debug.Log($"Cue {index}: object activation/deactivation skipped by flag");
        }

        // Delay before audio
        if (cue.audioDelay > 0f)
            yield return new WaitForSeconds(cue.audioDelay);

        if (cue.audio != null)
        {
            Debug.Log($"Cue {index}: playing audio");
            cue.audio.Play();
        }

        if (cue.video != null)
        {
            Debug.Log($"Cue {index}: playing video");
            cue.video.Play();
        }

        // Duration timer
        float timer = cue.duration;
        while (timer > 0f)
        {
            timer -= Time.deltaTime;
            yield return null;
        }

        Debug.Log($"Cue {index} END: {cue.cueName}");
        cueRunning = false;
        if (cue.onEnd != null)
        {
            cue.onEnd.Invoke();
        }

        if (runSequentialy)
        {
            nextCueIndex++;

            if (cue.goToCue && nextCueIndex < cues.Length)
            {
                TriggerCue(nextCueIndex); // correctly trigger NEXT cue
            }
        }
    }
}
