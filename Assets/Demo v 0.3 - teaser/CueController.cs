using UnityEngine;
using UnityEngine.Video;

[System.Serializable]
public class Cue
{
    public AudioSource audio;
    public float duration = 0f; // duration in seconds
    public VideoPlayer video;
    public GameObject objectToActivate;

    public void Execute()
    {
        if (objectToActivate != null)
            objectToActivate.SetActive(true);

        if (audio != null)
            audio.Play();

        if (video != null)
            video.Play();
    }
}

public class CueController : MonoBehaviour
{
    public Cue[] cues;  // Assign cues in Inspector

    public bool sequentialOnly = true;
    private bool cueRunning = false;
    private int nextCueIndex = 0;
    private float cueTimer = 0f;

    public void TriggerCue(int index)
    {
        if (cueRunning)
        {
            Debug.Log("Cue blocked — another cue is running.");
            return;
        }

        if (sequentialOnly && index != nextCueIndex)
        {
            Debug.Log($"Skipping cue {index}, waiting for cue {nextCueIndex}");
            return;
        }

        if (index < 0 || index >= cues.Length)
        {
            Debug.LogWarning("Cue index out of range");
            return;
        }

        cues[index].Execute();

        if (cues[index].duration > 0f)
        {
            cueRunning = true;
            cueTimer = cues[index].duration;
        }

        if (sequentialOnly)
        {
            nextCueIndex++;
        }
    }

    private void Update()
    {
        if (!cueRunning) return;

        cueTimer -= Time.deltaTime;
        if (cueTimer <= 0f)
        {
            cueRunning = false;
        }
    }
}
