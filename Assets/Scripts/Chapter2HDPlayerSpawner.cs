using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class Chapter2HDPlayerSpawner : MonoBehaviour, ICueTriggeredReceiver, ICueCompletedReceiver
{
    [Header("Cue Triggers")]
    public bool spawnOnEnable = false;
    public bool spawnOnCue = true;

    [Header("Audience")]
    public Transform audience;
    public float heightOffset = 0f;
    public bool faceAudience = true;
    public float faceYawOffset = 0f;

    [Header("Spawn Count")]
    public int minSpawnCount = 2;
    public int maxSpawnCount = 3;
    public float minSeparationDegrees = 15f;
    public float spawnInterval = 0.35f;
    public bool firstInFrontOfAudience = false;

    [Header("Distance")]
    public float startDistance = 5f;
    public float minDistance = 2f;
    public float distanceStep = 0.5f;
    public bool shrinkPerInstance = false;
    public bool resetDistanceOnEnable = false;

    [Header("Video")]
    public List<VideoClip> clips = new List<VideoClip>();
    public bool randomizePerInstance = true;
    public bool uniqueClipsPerSpawn = true;
    public bool playOnSpawn = true;

    [Header("Hierarchy")]
    public Transform spawnParent;
    public bool destroyClonesOnDisable = true;

    [Header("Fade")]
    public float fadeInDuration = 0.35f;
    public bool autoAddCanvasGroup = true;

    [Header("Despawn FX")]
    public float despawnFlickerDuration = 1.2f;
    public float despawnFlickerMinInterval = 0.04f;
    public float despawnFlickerMaxInterval = 0.12f;
    public float despawnFadeOutDuration = 0.25f;
    public bool disableRootOnDespawn = true;

    [Header("Editor")]
    public string clipsFolder = "Assets/chapter2";
    public bool autoRefreshClips = true;

    private const float kMinSeparationMeters = 0.8f;
    private float _currentDistance = 0f;
    private readonly List<GameObject> _clones = new List<GameObject>();
    private Quaternion _lookOffset = Quaternion.identity;
    private Coroutine _spawnRoutine;
    private Coroutine _despawnRoutine;
    private Cue _pendingCue;

    private void Awake()
    {
        if (_currentDistance <= 0f)
            _currentDistance = startDistance;

        _lookOffset = CalculateLookOffset(transform.rotation, transform.forward);
    }

    private void OnEnable()
    {
        if (spawnOnEnable)
            TrySpawn(null);
    }

    private void OnDisable()
    {
        StopDespawnRoutine();
        StopSpawnRoutine();
        CleanupOwnedInstancesGlobal();

        if (destroyClonesOnDisable)
            ClearClones();
    }

    private void OnDestroy()
    {
        StopDespawnRoutine();
        StopSpawnRoutine();
        CleanupOwnedInstancesGlobal();
    }

    public void OnCueTriggered(Cue cue)
    {
        if (cue != null && cue.videoSpawnerDespawnNow)
        {
            TriggerDespawnFx(cue);
            return;
        }

        if (cue != null && cue.videoSpawnerDespawnOnCueEnd)
            return;

        if (!spawnOnCue)
            return;

        TrySpawn(cue);
    }

    public void OnCueCompleted(Cue cue, int cueIndex, bool isLastCue)
    {
        if (cue == null || !cue.videoSpawnerDespawnOnCueEnd || cue.videoSpawnerDespawnNow)
            return;

        TriggerDespawnFx(cue);
    }

    public void Spawn()
    {
        TrySpawn(null);
    }

    private void TrySpawn(Cue cue)
    {
        if (!isActiveAndEnabled)
            return;

        if (!gameObject.activeInHierarchy)
        {
            ClearClones();
            return;
        }

        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        StopDespawnRoutine(restoreVisibleState: true);

        _pendingCue = cue;
        _spawnRoutine = StartCoroutine(SpawnSequence());
    }

    private System.Collections.IEnumerator SpawnSequence()
    {
        try
        {
            if (resetDistanceOnEnable || _currentDistance <= 0f)
                _currentDistance = startDistance;

            var target = ResolveAudience();
            if (target != null)
                yield return SpawnWave(target, BuildWaveSettings(_pendingCue));
        }
        finally
        {
            _spawnRoutine = null;
            _pendingCue = null;
        }
    }

    private void StopSpawnRoutine()
    {
        if (_spawnRoutine != null)
        {
            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        _pendingCue = null;
    }

    private struct DespawnSettings
    {
        public float flickerDuration;
        public float flickerMinInterval;
        public float flickerMaxInterval;
        public float fadeOutDuration;
        public bool disableRoot;
    }

    private void TriggerDespawnFx(Cue cue)
    {
        if (!isActiveAndEnabled)
            return;

        StopSpawnRoutine();
        StopDespawnRoutine(restoreVisibleState: true);
        _despawnRoutine = StartCoroutine(DespawnFxSequence(BuildDespawnSettings(cue)));
    }

    private DespawnSettings BuildDespawnSettings(Cue cue)
    {
        var settings = new DespawnSettings
        {
            flickerDuration = Mathf.Max(0f, despawnFlickerDuration),
            flickerMinInterval = Mathf.Max(0.01f, despawnFlickerMinInterval),
            flickerMaxInterval = Mathf.Max(0.01f, despawnFlickerMaxInterval),
            fadeOutDuration = Mathf.Max(0f, despawnFadeOutDuration),
            disableRoot = disableRootOnDespawn
        };

        if (cue != null)
        {
            settings.flickerDuration = Mathf.Max(0f, cue.videoSpawnerFlickerDuration);
            settings.flickerMinInterval = Mathf.Max(0.01f, cue.videoSpawnerFlickerMinInterval);
            settings.flickerMaxInterval = Mathf.Max(0.01f, cue.videoSpawnerFlickerMaxInterval);
            settings.fadeOutDuration = Mathf.Max(0f, cue.videoSpawnerFadeOutDuration);
            settings.disableRoot = cue.videoSpawnerDisableRootAfterDespawn;
        }

        if (settings.flickerMaxInterval < settings.flickerMinInterval)
            settings.flickerMaxInterval = settings.flickerMinInterval;

        return settings;
    }

    private System.Collections.IEnumerator DespawnFxSequence(DespawnSettings settings)
    {
        try
        {
            var targets = CollectOwnedInstances(includeRoot: true);
            if (targets.Count == 0)
                yield break;

            float minInterval = Mathf.Max(0.01f, settings.flickerMinInterval);
            float maxInterval = Mathf.Max(minInterval, settings.flickerMaxInterval);
            int count = targets.Count;
            var nextToggleAt = new float[count];

            // Independent initial states/phases so targets don't flicker in sync.
            for (int i = 0; i < count; i++)
            {
                var target = targets[i];
                if (target == null)
                    continue;

                bool lightsOn = Random.value > 0.35f;
                SetAlpha(target, lightsOn ? 1f : 0f);
                nextToggleAt[i] = Random.Range(0f, maxInterval);
            }

            float elapsed = 0f;
            while (elapsed < settings.flickerDuration)
            {
                elapsed += Time.deltaTime;

                for (int i = 0; i < count; i++)
                {
                    var target = targets[i];
                    if (target == null)
                        continue;

                    if (elapsed < nextToggleAt[i])
                        continue;

                    bool lightsOn = Random.value > 0.35f;
                    SetAlpha(target, lightsOn ? 1f : 0f);
                    nextToggleAt[i] = elapsed + Random.Range(minInterval, maxInterval);
                }

                yield return null;
            }

            SetAlphaForTargets(targets, 1f);

            if (settings.fadeOutDuration > 0f)
            {
                float t = 0f;
                while (t < settings.fadeOutDuration)
                {
                    t += Time.deltaTime;
                    float alpha = 1f - Mathf.Clamp01(t / settings.fadeOutDuration);
                    SetAlphaForTargets(targets, alpha);
                    yield return null;
                }
            }

            SetAlphaForTargets(targets, 0f);

            CleanupOwnedInstancesGlobal();
            ClearClones();

            if (settings.disableRoot && gameObject != null && gameObject.activeSelf)
                gameObject.SetActive(false);
        }
        finally
        {
            _despawnRoutine = null;
        }
    }

    private void StopDespawnRoutine(bool restoreVisibleState = false)
    {
        if (_despawnRoutine != null)
        {
            StopCoroutine(_despawnRoutine);
            _despawnRoutine = null;
        }

        if (restoreVisibleState)
            SetAlphaForTargets(CollectOwnedInstances(includeRoot: true), 1f);
    }

    private struct WaveSettings
    {
        public int minCount;
        public int maxCount;
        public float distance;
        public float minSeparation;
        public float perSpawnDelay;
        public float fadeDuration;
        public bool equalDistribution;
        public bool uniqueClips;
        public bool firstInFront;
    }

    private WaveSettings BuildWaveSettings(Cue cue)
    {
        var settings = new WaveSettings
        {
            minCount = Mathf.Max(1, minSpawnCount),
            maxCount = Mathf.Max(minSpawnCount, maxSpawnCount),
            distance = _currentDistance,
            minSeparation = minSeparationDegrees,
            perSpawnDelay = spawnInterval,
            fadeDuration = fadeInDuration,
            equalDistribution = false,
            uniqueClips = uniqueClipsPerSpawn,
            firstInFront = firstInFrontOfAudience
        };

        if (cue != null && cue.overrideSpawnerWave)
        {
            settings.minCount = Mathf.Max(1, cue.waveMinCount);
            settings.maxCount = Mathf.Max(settings.minCount, cue.waveMaxCount);
            settings.distance = Mathf.Max(minDistance, cue.waveDistance);
            settings.minSeparation = cue.waveMinSeparationDegrees;
            settings.perSpawnDelay = cue.waveSpawnInterval;
            settings.fadeDuration = cue.waveFadeInDuration;
            settings.equalDistribution = cue.waveEqualDistribution;
            settings.uniqueClips = cue.waveUniqueClips;
            settings.firstInFront = cue.waveFirstInFrontOfAudience;
        }

        return settings;
    }

    private System.Collections.IEnumerator SpawnWave(Transform target, WaveSettings wave)
    {
        ClearClones();

        int count = Mathf.Clamp(Random.Range(wave.minCount, wave.maxCount + 1), 1, 10);
        float minBatchDistance = shrinkPerInstance
            ? Mathf.Max(minDistance, wave.distance - distanceStep * (count - 1))
            : wave.distance;
        float minAngle = Mathf.Max(wave.minSeparation, MinAngleForArc(kMinSeparationMeters, minBatchDistance));
        float firstAngle = GetAudienceForwardAngle(target);
        var angles = wave.equalDistribution
            ? GenerateEqualAngles(count, wave.firstInFront ? firstAngle : (float?)null)
            : GenerateAngles(count, Mathf.Clamp(minAngle, 0f, 180f), wave.firstInFront ? firstAngle : (float?)null);
        var clipsForSpawn = BuildClipList(count, wave.uniqueClips);
        var parent = spawnParent != null ? spawnParent : transform;

        for (int i = 0; i < count; i++)
        {
            var instance = i == 0 ? gameObject : Instantiate(gameObject, parent);
            if (i > 0)
            {
                var spawner = instance.GetComponent<Chapter2HDPlayerSpawner>();
                if (spawner != null)
                    spawner.enabled = false;

                var tag = instance.GetComponent<Chapter2HDPlayerSpawnedInstanceTag>();
                if (tag == null)
                    tag = instance.AddComponent<Chapter2HDPlayerSpawnedInstanceTag>();
                tag.ownerSpawner = this;

                _clones.Add(instance);
            }

            float distanceForInstance = shrinkPerInstance
                ? Mathf.Max(minDistance, wave.distance - (distanceStep * i))
                : wave.distance;
            var position = GetSpawnPosition(target, distanceForInstance, angles[i]);
            var rotation = GetSpawnRotation(target, position);
            ApplyTransform(instance.transform, position, rotation);
            ApplyVideo(instance, clipsForSpawn[i]);
            StartFadeIn(instance, wave.fadeDuration);

            if (i < count - 1 && wave.perSpawnDelay > 0f)
                yield return new WaitForSeconds(wave.perSpawnDelay);
        }

        _currentDistance = Mathf.Max(minDistance, wave.distance - distanceStep);
    }

    private void ClearClones()
    {
        for (int i = _clones.Count - 1; i >= 0; i--)
        {
            var clone = _clones[i];
            if (clone != null)
                Destroy(clone);
        }

        _clones.Clear();
    }

    private List<GameObject> CollectOwnedInstances(bool includeRoot)
    {
        var result = new List<GameObject>();
        var seen = new HashSet<GameObject>();

        void Add(GameObject go)
        {
            if (go == null || !seen.Add(go))
                return;
            result.Add(go);
        }

        if (includeRoot)
            Add(gameObject);

        for (int i = 0; i < _clones.Count; i++)
            Add(_clones[i]);

        var spawned = Object.FindObjectsOfType<Chapter2HDPlayerSpawnedInstanceTag>(true);
        if (spawned != null)
        {
            for (int i = 0; i < spawned.Length; i++)
            {
                var tag = spawned[i];
                if (tag == null || tag.ownerSpawner != this)
                    continue;

                Add(tag.gameObject);
            }
        }

        return result;
    }

    private void SetAlphaForTargets(List<GameObject> targets, float alpha)
    {
        if (targets == null)
            return;

        float clamped = Mathf.Clamp01(alpha);
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null)
                continue;

            SetAlpha(target, clamped);
        }
    }

    private void CleanupOwnedInstancesGlobal()
    {
        var spawned = Object.FindObjectsOfType<Chapter2HDPlayerSpawnedInstanceTag>(true);
        if (spawned == null || spawned.Length == 0)
            return;

        for (int i = 0; i < spawned.Length; i++)
        {
            var tag = spawned[i];
            if (tag == null || tag.ownerSpawner != this)
                continue;

            if (tag.gameObject != null)
                Destroy(tag.gameObject);
        }
    }

    private Transform ResolveAudience()
    {
        if (audience != null)
            return audience;

        var cam = Camera.main;
        if (cam != null)
            audience = cam.transform;

        return audience;
    }

    private Vector3 GetSpawnPosition(Transform target, float distance, float angleDegrees)
    {
        if (target == null)
            return transform.position;

        var angle = angleDegrees * Mathf.Deg2Rad;
        var offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Mathf.Max(0f, distance);
        return target.position + offset + Vector3.up * heightOffset;
    }

    private Quaternion GetSpawnRotation(Transform target, Vector3 position)
    {
        if (!faceAudience || target == null)
            return transform.rotation;

        var direction = target.position - position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            return transform.rotation;

        var look = Quaternion.LookRotation(direction.normalized, Vector3.up);
        if (Mathf.Abs(faceYawOffset) > 0.001f)
            look *= Quaternion.Euler(0f, faceYawOffset, 0f);

        return look * _lookOffset;
    }

    private static void ApplyTransform(Transform target, Vector3 position, Quaternion rotation)
    {
        target.position = position;
        target.rotation = rotation;
    }

    private void ApplyVideo(GameObject target, VideoClip clip)
    {
        if (clip == null)
            return;

        var player = target.GetComponentInChildren<VideoPlayer>(true);
        if (player == null)
            return;

        player.clip = clip;
        if (playOnSpawn)
        {
            player.Stop();
            player.prepareCompleted -= OnVideoPrepared;
            player.prepareCompleted += OnVideoPrepared;
            player.Prepare();
        }
    }

    private void OnVideoPrepared(VideoPlayer player)
    {
        player.prepareCompleted -= OnVideoPrepared;
        player.Play();
    }

    private VideoClip PickRandomClip()
    {
        if (clips == null || clips.Count == 0)
            return null;

        return clips[Random.Range(0, clips.Count)];
    }

    private List<VideoClip> BuildClipList(int count, bool uniqueClips)
    {
        var result = new List<VideoClip>(count);
        if (clips == null || clips.Count == 0)
        {
            for (int i = 0; i < count; i++)
                result.Add(null);
            return result;
        }

        if (!randomizePerInstance)
        {
            var shared = PickRandomClip();
            for (int i = 0; i < count; i++)
                result.Add(shared);
            return result;
        }

        var pool = new List<VideoClip>(clips);
        Shuffle(pool);

        if (!uniqueClips || pool.Count == 1)
        {
            for (int i = 0; i < count; i++)
                result.Add(PickRandomClip());
            return result;
        }

        for (int i = 0; i < count; i++)
            result.Add(pool[i % pool.Count]);

        return result;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static List<float> GenerateAngles(int count, float minSeparation, float? fixedFirstAngle = null)
    {
        var angles = new List<float>(count);
        if (count <= 0)
            return angles;

        if (fixedFirstAngle.HasValue)
            angles.Add(NormalizeAngle(fixedFirstAngle.Value));

        if (minSeparation <= 0f)
        {
            for (int i = angles.Count; i < count; i++)
                angles.Add(Random.Range(0f, 360f));
            return angles;
        }

        int attempts = 0;
        while (angles.Count < count && attempts < 2000)
        {
            attempts++;
            var candidate = Random.Range(0f, 360f);
            bool ok = true;
            for (int i = 0; i < angles.Count; i++)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(candidate, angles[i])) < minSeparation)
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
                angles.Add(candidate);
        }

        if (angles.Count < count)
        {
            angles.Clear();
            float start = fixedFirstAngle.HasValue ? NormalizeAngle(fixedFirstAngle.Value) : Random.Range(0f, 360f);
            float step = 360f / count;
            for (int i = 0; i < count; i++)
                angles.Add(start + step * i);
        }

        return angles;
    }

    private static List<float> GenerateEqualAngles(int count, float? fixedFirstAngle = null)
    {
        var angles = new List<float>(count);
        if (count <= 0)
            return angles;

        float start = fixedFirstAngle.HasValue ? NormalizeAngle(fixedFirstAngle.Value) : Random.Range(0f, 360f);
        float step = 360f / count;
        for (int i = 0; i < count; i++)
            angles.Add(start + (step * i));

        return angles;
    }

    private static float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle < 0f)
            angle += 360f;
        return angle;
    }

    private static float GetAudienceForwardAngle(Transform target)
    {
        if (target == null)
            return Random.Range(0f, 360f);

        Vector3 forward = target.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            return Random.Range(0f, 360f);

        forward.Normalize();
        return Mathf.Atan2(forward.z, forward.x) * Mathf.Rad2Deg;
    }

    private static float MinAngleForArc(float arcLength, float radius)
    {
        if (radius <= 0.001f)
            return 180f;

        var angleRad = arcLength / radius;
        return angleRad * Mathf.Rad2Deg;
    }

    private void StartFadeIn(GameObject target, float duration)
    {
        if (duration <= 0f)
            return;

        SetAlpha(target, 0f);
        StartCoroutine(FadeInRoutine(target, duration));
    }

    private System.Collections.IEnumerator FadeInRoutine(GameObject target, float duration)
    {
        float time = 0f;
        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.Clamp01(time / duration);
            SetAlpha(target, t);
            yield return null;
        }

        SetAlpha(target, 1f);
    }

    private void SetAlpha(GameObject target, float alpha)
    {
        var canvasGroup = target.GetComponent<CanvasGroup>();
        if (canvasGroup == null && autoAddCanvasGroup)
            canvasGroup = target.AddComponent<CanvasGroup>();

        if (canvasGroup != null)
        {
            canvasGroup.alpha = alpha;
            return;
        }

        var graphics = target.GetComponentsInChildren<Graphic>(true);
        if (graphics != null && graphics.Length > 0)
        {
            for (int i = 0; i < graphics.Length; i++)
            {
                var color = graphics[i].color;
                color.a = alpha;
                graphics[i].color = color;
            }
            return;
        }

        var renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            var material = renderer.material;
            if (material == null || !material.HasProperty("_Color"))
                continue;

            var color = material.color;
            color.a = alpha;
            material.color = color;
        }
    }

    private static Quaternion CalculateLookOffset(Quaternion initialRotation, Vector3 initialForward)
    {
        if (initialForward.sqrMagnitude < 0.001f)
            return Quaternion.identity;

        var baseLook = Quaternion.LookRotation(initialForward.normalized, Vector3.up);
        return Quaternion.Inverse(baseLook) * initialRotation;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!autoRefreshClips)
            return;

        if (string.IsNullOrWhiteSpace(clipsFolder))
            return;

        var guids = AssetDatabase.FindAssets("t:VideoClip", new[] { clipsFolder });
        var found = new List<VideoClip>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            if (clip != null)
                found.Add(clip);
        }

        found.Sort((a, b) => string.Compare(
            AssetDatabase.GetAssetPath(a),
            AssetDatabase.GetAssetPath(b),
            System.StringComparison.OrdinalIgnoreCase));

        if (!AreSame(clips, found))
        {
            clips = found;
            EditorUtility.SetDirty(this);
        }
    }

    private static bool AreSame(List<VideoClip> a, List<VideoClip> b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }
#endif
}
