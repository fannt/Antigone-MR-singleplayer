using UnityEngine;
using UnityEngine.Video;

[System.Serializable]
public class Cue
{
    public AudioSource audio;
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

    public void TriggerCue(int index)
    {
        if (index < 0 || index >= cues.Length)
        {
            Debug.LogWarning("Cue index out of range");
            return;
        }

        cues[index].Execute();
    }
}
