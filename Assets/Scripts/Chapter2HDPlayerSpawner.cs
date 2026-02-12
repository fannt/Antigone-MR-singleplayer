using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class Chapter2HDPlayerSpawner : MonoBehaviour, ICueTriggeredReceiver
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

    [Header("Editor")]
    public string clipsFolder = "Assets/chapter2";
    public bool autoRefreshClips = true;

    private static bool s_IsSpawning = false;
    private const float kMinSeparationDegrees = 15f;
    private const float kMinSeparationMeters = 0.8f;
    private const float kSpawnInterval = 0.35f;
    private const bool kUniqueClipsPerSpawn = true;
    private const float kFadeInDuration = 0.35f;
    private const bool kAutoAddCanvasGroup = true;
    private float _currentDistance = 0f;
    private int _lastSpawnFrame = -1;
    private readonly List<GameObject> _clones = new List<GameObject>();
    private Quaternion _lookOffset = Quaternion.identity;
    private Coroutine _spawnRoutine;

    private void Awake()
    {
        if (_currentDistance <= 0f)
            _currentDistance = startDistance;

        _lookOffset = CalculateLookOffset(transform.rotation, transform.forward);
    }

    private void OnEnable()
    {
        if (spawnOnEnable)
            TrySpawn();
    }

    private void OnDisable()
    {
        if (destroyClonesOnDisable)
            ClearClones();
    }

    public void OnCueTriggered(Cue cue)
    {
        if (!spawnOnCue)
            return;

        TrySpawn();
    }

    public void Spawn()
    {
        if (s_IsSpawning)
            return;

        if (_spawnRoutine != null)
            StopCoroutine(_spawnRoutine);

        _spawnRoutine = StartCoroutine(SpawnSequence());
    }

    private void TrySpawn()
    {
        if (!isActiveAndEnabled)
            return;

        if (s_IsSpawning)
            return;

        if (!gameObject.activeInHierarchy)
        {
            ClearClones();
            return;
        }

        if (_lastSpawnFrame == Time.frameCount)
            return;

        Spawn();
    }

    private System.Collections.IEnumerator SpawnSequence()
    {
        s_IsSpawning = true;
        _lastSpawnFrame = Time.frameCount;

        if (resetDistanceOnEnable || _currentDistance <= 0f)
            _currentDistance = startDistance;

        ClearClones();

        var target = ResolveAudience();
        var count = Mathf.Clamp(Random.Range(minSpawnCount, maxSpawnCount + 1), 1, 10);
        var minBatchDistance = shrinkPerInstance
            ? Mathf.Max(minDistance, _currentDistance - distanceStep * (count - 1))
            : _currentDistance;
        var minAngle = Mathf.Max(kMinSeparationDegrees, MinAngleForArc(kMinSeparationMeters, minBatchDistance));
        var angles = GenerateAngles(count, Mathf.Clamp(minAngle, 0f, 180f));
        var clipsForSpawn = BuildClipList(count);

        var parent = spawnParent != null ? spawnParent : transform.parent;

        for (int i = 0; i < count; i++)
        {
            var instance = i == 0 ? gameObject : Instantiate(gameObject, parent);
            if (i > 0)
            {
                var spawner = instance.GetComponent<Chapter2HDPlayerSpawner>();
                if (spawner != null)
                    spawner.enabled = false;

                _clones.Add(instance);
            }

            var position = GetSpawnPosition(target, _currentDistance, angles[i]);
            var rotation = GetSpawnRotation(target, position);
            ApplyTransform(instance.transform, position, rotation);

            ApplyVideo(instance, clipsForSpawn[i]);
            StartFadeIn(instance);

            if (shrinkPerInstance)
                _currentDistance = Mathf.Max(minDistance, _currentDistance - distanceStep);

            if (i < count - 1 && kSpawnInterval > 0f)
                yield return new WaitForSeconds(kSpawnInterval);
        }

        if (!shrinkPerInstance)
            _currentDistance = Mathf.Max(minDistance, _currentDistance - distanceStep);

        s_IsSpawning = false;
        _spawnRoutine = null;
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

    private List<VideoClip> BuildClipList(int count)
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

        if (!kUniqueClipsPerSpawn || pool.Count == 1)
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

    private static List<float> GenerateAngles(int count, float minSeparation)
    {
        var angles = new List<float>(count);
        if (count <= 0)
            return angles;

        if (minSeparation <= 0f)
        {
            for (int i = 0; i < count; i++)
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
            float start = Random.Range(0f, 360f);
            float step = 360f / count;
            for (int i = 0; i < count; i++)
                angles.Add(start + step * i);
        }

        return angles;
    }

    private static float MinAngleForArc(float arcLength, float radius)
    {
        if (radius <= 0.001f)
            return 180f;

        var angleRad = arcLength / radius;
        return angleRad * Mathf.Rad2Deg;
    }

    private void StartFadeIn(GameObject target)
    {
        if (kFadeInDuration <= 0f)
            return;

        SetAlpha(target, 0f);
        StartCoroutine(FadeInRoutine(target, kFadeInDuration));
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
        if (canvasGroup == null && kAutoAddCanvasGroup)
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
