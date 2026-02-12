using System.Collections.Generic;
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

    [Header("Jump Rebuild")]
    [SerializeField] private bool rebuildStateOnJumpAndRun = true;
    [SerializeField] private bool replayCueReceiversDuringJumpRebuild = false;
    [SerializeField] private bool simulateMediaTimeDuringJumpRebuild = true;

    public int CueCount => cues != null ? cues.Length : 0;
    public int NextCueIndex => nextCueIndex;
    public bool IsCueRunning => cueRunning;

    private readonly List<GameObject> controlledObjects = new List<GameObject>();
    private readonly List<AudioSource> controlledAudios = new List<AudioSource>();
    private readonly List<VideoPlayer> controlledVideos = new List<VideoPlayer>();
    private readonly List<GeometrySequencePlayer> controlledGeometryPlayers = new List<GeometrySequencePlayer>();
    private readonly Dictionary<GameObject, bool> baselineActiveStateByObject = new Dictionary<GameObject, bool>();
    private readonly HashSet<GameObject> cueReceiverObjects = new HashSet<GameObject>();
    private bool baselineCaptured = false;

    private void Start()
    {
        nextCueIndex = CueCount > 0 ? Mathf.Clamp(startingCue, 0, CueCount - 1) : 0;
        CacheControlledTargets();
        CaptureBaselineState();

        if (autoStart)
        {
            TriggerCue(startingCue);
        }
    }

    private int nextCueIndex = 0;
    private bool cueRunning = false;

    public void TriggerCue(int index)
    {
        TryTriggerCue(index);
    }

    public bool TryTriggerCue(int index, bool ignoreSequentialGate = false)
    {
        Debug.Log($"TriggerCue({index}) called");
        if (cueRunning)
        {
            Debug.Log("Cue blocked — another cue is running.");
            return false;
        }

        if (!ignoreSequentialGate && runSequentialy && index != nextCueIndex)
        {
            Debug.Log($"Skipping cue {index}, waiting for cue {nextCueIndex}");
            return false;
        }

        if (!IsCueIndexValid(index))
        {
            Debug.LogWarning("Cue index out of range");
            return false;
        }

        StartCoroutine(RunCue(index));
        return true;
    }

    public bool JumpToCue(int index)
    {
        if (!IsCueIndexValid(index))
        {
            Debug.LogWarning($"JumpToCue failed, index {index} is out of range.");
            return false;
        }

        nextCueIndex = index;
        Debug.Log($"Jumped to cue index {index} ({cues[index].cueName}).");
        return true;
    }

    public bool JumpToCueAndRun(int index, bool ignoreSequentialGateForRun = true)
    {
        if (!IsCueIndexValid(index))
        {
            Debug.LogWarning($"JumpToCueAndRun failed, index {index} is out of range.");
            return false;
        }

        AbortCueExecution();

        if (rebuildStateOnJumpAndRun)
        {
            RebuildStateUpToCue(index);
        }

        nextCueIndex = index;
        Debug.Log($"Jumped to cue index {index} ({cues[index].cueName}) and running.");
        return TryTriggerCue(index, ignoreSequentialGateForRun);
    }

    public bool IsCueIndexValid(int index)
    {
        return index >= 0 && index < CueCount;
    }

    private void AbortCueExecution()
    {
        StopAllCoroutines();
        cueRunning = false;
    }

    private void CacheControlledTargets()
    {
        controlledObjects.Clear();
        controlledAudios.Clear();
        controlledVideos.Clear();
        controlledGeometryPlayers.Clear();
        cueReceiverObjects.Clear();

        if (cues == null)
            return;

        for (int i = 0; i < cues.Length; i++)
        {
            Cue cue = cues[i];
            if (cue == null)
                continue;

            AddUnique(controlledObjects, cue.gameObject);
            if (cue.gameObject != null && HasCueReceivers(cue.gameObject))
                cueReceiverObjects.Add(cue.gameObject);

            AddUnique(controlledAudios, cue.audio);
            if (cue.audio != null)
                AddUnique(controlledObjects, cue.audio.gameObject);

            AddUnique(controlledVideos, cue.video);
            if (cue.video != null)
                AddUnique(controlledObjects, cue.video.gameObject);

            AddUnique(controlledGeometryPlayers, cue.geometryPlayer);
            if (cue.geometryPlayer != null)
                AddUnique(controlledObjects, cue.geometryPlayer.gameObject);
        }
    }

    private void CaptureBaselineState()
    {
        baselineActiveStateByObject.Clear();

        for (int i = 0; i < controlledObjects.Count; i++)
        {
            GameObject obj = controlledObjects[i];
            if (obj == null || obj == gameObject)
                continue;

            baselineActiveStateByObject[obj] = obj.activeSelf;
        }

        baselineCaptured = true;
    }

    private void RebuildStateUpToCue(int targetCueIndex)
    {
        if (!baselineCaptured)
        {
            CacheControlledTargets();
            CaptureBaselineState();
        }

        ResetControlledStateToBaseline();

        if (targetCueIndex <= 0)
            return;

        float targetTimelineTime = CalculateCueStartTime(targetCueIndex);
        float cueStartTime = 0f;

        for (int i = 0; i < targetCueIndex; i++)
        {
            Cue cue = cues[i];
            if (cue == null)
            {
                cueStartTime += 0f;
                continue;
            }

            ApplyCueInstant(cue, i, cueStartTime, targetTimelineTime);
            cueStartTime += Mathf.Max(0f, cue.duration);
        }
    }

    private void ResetControlledStateToBaseline()
    {
        for (int i = 0; i < controlledAudios.Count; i++)
        {
            AudioSource audio = controlledAudios[i];
            if (audio == null)
                continue;

            audio.Stop();
            audio.time = 0f;
        }

        for (int i = 0; i < controlledVideos.Count; i++)
        {
            VideoPlayer video = controlledVideos[i];
            if (video == null)
                continue;

            video.Stop();
            if (video.canSetTime)
                video.time = 0d;
        }

        for (int i = 0; i < controlledGeometryPlayers.Count; i++)
        {
            GeometrySequencePlayer player = controlledGeometryPlayers[i];
            if (player == null)
                continue;

            player.Pause();
            if (player.IsInitialized())
                player.GoToFrame(0);
            player.Hide();
        }

        foreach (KeyValuePair<GameObject, bool> kvp in baselineActiveStateByObject)
        {
            if (kvp.Key == null || kvp.Key == gameObject)
                continue;

            kvp.Key.SetActive(kvp.Value);
        }
    }

    private void ApplyCueInstant(Cue cue, int cueIndex, float cueStartTime, float targetTimelineTime)
    {
        if (cue.gameObject != null)
        {
            // Keep receiver-driven/spawner objects out of instant rebuild,
            // otherwise random/algorithmic behavior diverges from live cue flow.
            if (!cueReceiverObjects.Contains(cue.gameObject))
            {
                cue.gameObject.SetActive(cue.toggleActiveTo);

                if (replayCueReceiversDuringJumpRebuild)
                    NotifyCueReceivers(cue);
            }
        }

        try
        {
            TriggerGeometryAction(cue, cueIndex);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Cue {cueIndex}: geometry action failed during jump rebuild: {e.Message}");
        }

        if (!simulateMediaTimeDuringJumpRebuild)
            return;

        float elapsedSinceCueStartAtTarget = Mathf.Max(0f, targetTimelineTime - cueStartTime);
        ApplyAudioStateForTimeline(cue, elapsedSinceCueStartAtTarget);
        ApplyVideoStateForTimeline(cue, elapsedSinceCueStartAtTarget);
    }

    private static void ApplyAudioStateForTimeline(Cue cue, float elapsedSinceCueStart)
    {
        if (cue == null || cue.audio == null || cue.audio.clip == null)
            return;

        AudioSource audio = cue.audio;
        float playbackElapsed = elapsedSinceCueStart - Mathf.Max(0f, cue.audioDelay);
        if (playbackElapsed < 0f)
        {
            audio.Stop();
            audio.time = 0f;
            return;
        }

        float clipLength = audio.clip.length;
        if (clipLength <= 0f)
        {
            audio.Play();
            return;
        }

        if (audio.loop)
        {
            float wrapped = playbackElapsed % clipLength;
            if (wrapped < 0f)
                wrapped += clipLength;

            audio.time = wrapped;
            audio.Play();
            return;
        }

        if (playbackElapsed >= clipLength)
        {
            audio.Stop();
            audio.time = clipLength;
            return;
        }

        audio.time = Mathf.Clamp(playbackElapsed, 0f, Mathf.Max(0f, clipLength - 0.01f));
        audio.Play();
    }

    private static void ApplyVideoStateForTimeline(Cue cue, float elapsedSinceCueStart)
    {
        if (cue == null || cue.video == null)
            return;

        VideoPlayer video = cue.video;
        if (elapsedSinceCueStart < 0f)
        {
            video.Stop();
            if (video.canSetTime)
                video.time = 0d;
            return;
        }

        double clipLength = video.clip != null ? video.clip.length : 0d;
        double timeAtTarget = elapsedSinceCueStart;

        if (clipLength > 0d && video.isLooping)
            timeAtTarget = timeAtTarget % clipLength;

        if (clipLength > 0d && !video.isLooping && timeAtTarget >= clipLength)
        {
            video.Stop();
            if (video.canSetTime)
                video.time = clipLength;
            return;
        }

        if (video.canSetTime)
            video.time = Mathf.Max(0f, (float)timeAtTarget);

        video.Play();
    }

    private float CalculateCueStartTime(int cueIndex)
    {
        float total = 0f;
        int clampedEnd = Mathf.Clamp(cueIndex, 0, CueCount);
        for (int i = 0; i < clampedEnd; i++)
        {
            Cue cue = cues[i];
            if (cue == null)
                continue;

            total += Mathf.Max(0f, cue.duration);
        }

        return total;
    }

    private static void AddUnique<T>(List<T> list, T item) where T : Object
    {
        if (item == null || list.Contains(item))
            return;

        list.Add(item);
    }

    private static bool HasCueReceivers(GameObject obj)
    {
        if (obj == null)
            return false;

        var receivers = obj.GetComponentsInChildren<ICueTriggeredReceiver>(true);
        return receivers != null && receivers.Length > 0;
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

        try
        {
            TriggerGeometryAction(cue, index);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Cue {index}: geometry action failed: {e.Message}");
        }

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
            nextCueIndex = Mathf.Min(index + 1, CueCount);

            if (cue.goToNextCue && nextCueIndex < CueCount)
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
                EnsureGeometryPlayerActive(player);
                EnsureGeometryPlayerLoaded(player, cueIndex);
                player.Pause();
                if (player.IsInitialized())
                    player.GoToFrame(0);
                player.Hide();
                break;
            case GeometryCueAction.Play:
                EnsureGeometryPlayerActive(player);
                if (EnsureGeometryPlayerLoaded(player, cueIndex))
                {
                    player.Show();
                    player.Play();
                }
                break;
            case GeometryCueAction.PlayFromStart:
                EnsureGeometryPlayerActive(player);
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
                EnsureGeometryPlayerActive(player);
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

    private static void EnsureGeometryPlayerActive(GeometrySequencePlayer player)
    {
        if (player != null && !player.gameObject.activeSelf)
            player.gameObject.SetActive(true);
    }

    private bool EnsureGeometryPlayerLoaded(GeometrySequencePlayer player, int cueIndex)
    {
        if (player == null)
            return false;

        player.SetupGeometryStream();

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
