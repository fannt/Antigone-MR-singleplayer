using UnityEngine;
using UnityEngine.Video;
using BuildingVolumes.Player;

public enum GeometryCueAction
{
    None,
    Preload,
    Play,
    PlayFromStart,
    Pause,
    Stop,
    Show,
    Hide,
    PauseRewindHide
}

[System.Serializable]
public class Cue
{
    public string cueName = "Cue";
    public AudioSource audio;
    public VideoPlayer video;
    public GameObject gameObject;
    public GeometrySequencePlayer geometryPlayer;
    public GeometryCueAction geometryAction = GeometryCueAction.None;
    public float duration = 0f;   // duration in seconds for locking cue system

    public float audioDelay = 0f;        // delay before audio starts
    public bool goToNextCue = false;     // automatically trigger next cue when done
    public bool toggleActiveTo = true; // true = activate on start, false = deactivate on start
}

public interface ICueTriggeredReceiver
{
    void OnCueTriggered(Cue cue);
}

public class CueController : MonoBehaviour
{

    public bool autoStart = false;
    public bool runSequentialy = true;
    public int startingCue = 0;   // starting cue index
    public Cue[] cues;

    private void Start()
    {
        if (autoStart)
        {
            TriggerCue(startingCue);
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

        if (cue.gameObject != null)
        {
            if (cue.toggleActiveTo)
            {
                Debug.Log($"Cue {index}: activating object {cue.gameObject.name}");
                cue.gameObject.SetActive(true);
            }
            else
            {
                Debug.Log($"Cue {index}: deactivating object {cue.gameObject.name}");
                cue.gameObject.SetActive(false);
            }

            NotifyCueReceivers(cue);
        }
        else
        {
            Debug.Log($"Cue {index}: object activation/deactivation skipped by flag");
        }

        TriggerGeometryAction(cue, index);

        if (cue.audio != null)
        {
            Debug.Log($"Cue {index}: playing audio");
            if (cue.audioDelay > 0f)
                StartCoroutine(PlayAudioAfterDelay(cue.audio, cue.audioDelay));
            else
                cue.audio.Play();
        }

        if (cue.video != null)
        {
            Debug.Log($"Cue {index}: playing video");
            cue.video.Play();
        }

        // Duration timer
        float timer = Mathf.Max(0f, cue.duration);
        if (timer > 0f)
        {
            while (timer > 0f)
            {
                timer -= Time.deltaTime;
                yield return null;
            }
        }

        Debug.Log($"Cue {index} END: {cue.cueName}");
        cueRunning = false;

        if (runSequentialy)
        {
            nextCueIndex++;

            if (cue.goToNextCue && nextCueIndex < cues.Length)
            {
                TriggerCue(nextCueIndex); // correctly trigger NEXT cue
            }
        }
    }

    private void TriggerGeometryAction(Cue cue, int cueIndex)
    {
        if (cue == null || cue.geometryPlayer == null || cue.geometryAction == GeometryCueAction.None)
            return;

        var player = cue.geometryPlayer;
        switch (cue.geometryAction)
        {
            case GeometryCueAction.Preload:
                EnsureGeometryPlayerLoaded(player, cueIndex);
                player.Pause();
                if (player.IsInitialized())
                    player.GoToFrame(0);
                player.Hide();
                break;
            case GeometryCueAction.Play:
                if (EnsureGeometryPlayerLoaded(player, cueIndex))
                {
                    player.Show();
                    player.Play();
                }
                break;
            case GeometryCueAction.PlayFromStart:
                if (EnsureGeometryPlayerLoaded(player, cueIndex))
                {
                    player.Show();
                    player.PlayFromStart();
                }
                break;
            case GeometryCueAction.Pause:
                player.Pause();
                break;
            case GeometryCueAction.Stop:
                player.Stop();
                break;
            case GeometryCueAction.Show:
                player.Show();
                break;
            case GeometryCueAction.Hide:
                player.Hide();
                break;
            case GeometryCueAction.PauseRewindHide:
                player.Pause();
                if (player.IsInitialized())
                    player.GoToFrame(0);
                player.Hide();
                break;
        }

        Debug.Log($"Cue {cueIndex}: geometry action {cue.geometryAction} on {player.name}");
    }

    private bool EnsureGeometryPlayerLoaded(GeometrySequencePlayer player, int cueIndex)
    {
        if (player == null)
            return false;

        if (player.IsInitialized())
            return true;

        bool loaded = player.LoadCurrentSequence(false, true);
        if (!loaded)
        {
            Debug.LogWarning($"Cue {cueIndex}: failed to load geometry sequence on {player.name}");
            return false;
        }

        return player.IsInitialized();
    }

    private void NotifyCueReceivers(Cue cue)
    {
        if (cue.gameObject == null)
            return;

        var receivers = cue.gameObject.GetComponentsInChildren<ICueTriggeredReceiver>(true);
        if (receivers == null || receivers.Length == 0)
            return;

        for (int i = 0; i < receivers.Length; i++)
        {
            var receiver = receivers[i];
            if (receiver != null)
                receiver.OnCueTriggered(cue);
        }
    }

    private System.Collections.IEnumerator PlayAudioAfterDelay(AudioSource audio, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (audio != null)
            audio.Play();
    }
}
