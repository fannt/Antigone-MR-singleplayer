using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using BuildingVolumes.Player;

public class StreamingAssetsToPersistentCopy : MonoBehaviour
{
    [Header("Source / Destination")]
    [Tooltip("Relative path inside StreamingAssets. Example: chapter1/video-1")]
    public string streamingAssetsRelativePath = "chapter1";

    [Tooltip("Relative path inside persistentDataPath. Leave empty to use the same as source.")]
    public string persistentRelativePath = "";

    [Header("Behavior")]
    public bool copyOnStart = true;
    public bool overwriteExisting = false;

    [Serializable]
    public class StringEvent : UnityEvent<string> { }

    [Header("Events")]
    [Tooltip("Invoked when the copy step finishes (or is skipped) successfully. Argument is dstRoot.")]
    public StringEvent onCopyCompleted;

    [Tooltip("Invoked if one or more files failed to copy. Argument is dstRoot.")]
    public StringEvent onCopyFailed;

    [Header("Numeric Fallback (no manifest)")]
    public bool useNumericFallback = true;
    public bool copySequenceJson = true;
    public string sequenceJsonName = "sequence.json";
    public int startIndex = 0;
    public int endIndex = 1024;
    public int numberPadding = 7; // 0000000.ply
    public string fileExtension = ".ply";

    bool hasStarted;

    public string PersistentFullPath
    {
        get
        {
            string rel = string.IsNullOrWhiteSpace(persistentRelativePath)
                ? streamingAssetsRelativePath
                : persistentRelativePath;
            return Path.Combine(Application.persistentDataPath, rel);
        }
    }

    void OnEnable()
    {
        // Debug.LogWarning("[CopySeq] Enabled on " + name);
        TryAutoStart();
    }

    void Start()
    {
        // Debug.LogWarning("[CopySeq] Start on " + name);
        TryAutoStart();
    }

    void TryAutoStart()
    {
        if (!copyOnStart || hasStarted)
            return;

        hasStarted = true;
        StartCoroutine(CopySequenceFolder());
    }

    public IEnumerator CopySequenceFolder()
    {
        string srcRoot = CombinePath(Application.streamingAssetsPath, streamingAssetsRelativePath);
        string dstRoot = PersistentFullPath;

        // Debug.LogWarning($"[CopySeq] Platform={Application.platform} | streamingAssetsPath={Application.streamingAssetsPath} | persistentDataPath={Application.persistentDataPath}");
        // Debug.LogWarning($"[CopySeq] srcRoot={srcRoot}");
        // Debug.LogWarning($"[CopySeq] dstRoot={dstRoot}");

        if (!Directory.Exists(dstRoot))
            Directory.CreateDirectory(dstRoot);

        // Try to load a manifest (recommended). If missing, we fall back to a known list.
        string manifestPath = CombinePath(srcRoot, "manifest.txt");
        List<string> relativeFiles = new List<string>();

        yield return ReadStreamingText(manifestPath, (ok, text) =>
        {
            if (ok && !string.IsNullOrWhiteSpace(text))
            {
                string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                relativeFiles.AddRange(lines);
            }
        });

        if (relativeFiles.Count == 0)
        {
            if (!useNumericFallback)
            {
                // Debug.LogError("No manifest.txt found and numeric fallback disabled.");
                yield break;
            }

            // Debug.LogWarning("Using numeric fallback copy: " + startIndex + ".." + endIndex);
            if (copySequenceJson && !string.IsNullOrWhiteSpace(sequenceJsonName))
                relativeFiles.Add(sequenceJsonName);
            for (int i = startIndex; i <= endIndex; i++)
            {
                string name = i.ToString().PadLeft(numberPadding, '0') + fileExtension;
                relativeFiles.Add(name);
            }
        }

        if (!overwriteExisting && relativeFiles.Count > 0)
        {
            string firstRel = relativeFiles[0].Replace("\\", "/");
            string firstDst = Path.Combine(dstRoot, firstRel);
            if (File.Exists(firstDst))
            {
                // Debug.LogWarning($"[CopySeq] First file exists, skipping copy: {firstDst}");
                onCopyCompleted?.Invoke(dstRoot);
                yield break;
            }
        }

        int copied = 0;
        int skipped = 0;
        int failed = 0;
        int totalPlanned = relativeFiles.Count;
        // Debug.LogWarning($"[CopySeq] Planned files: {totalPlanned} (overwriteExisting={overwriteExisting})");

        for (int i = 0; i < relativeFiles.Count; i++)
        {
            string rel = relativeFiles[i].Replace("\\", "/");
            string srcFile = CombinePath(srcRoot, rel);
            string dstFile = Path.Combine(dstRoot, rel);

            string dstDir = Path.GetDirectoryName(dstFile);
            if (!Directory.Exists(dstDir))
                Directory.CreateDirectory(dstDir);

            if (!overwriteExisting && File.Exists(dstFile))
            {
                skipped++;
                if ((i % 25) == 0)
                    // Debug.LogWarning($"[CopySeq] Progress {i + 1}/{totalPlanned} | copied={copied} skipped={skipped} failed={failed} (skipping existing)");
                continue;
            }

            bool ok = false;
            yield return CopyStreamingFile(srcFile, dstFile, success => ok = success);
            if (ok) copied++; else failed++;

            // if ((i % 25) == 0)
                // Debug.LogWarning($"[CopySeq] Progress {i + 1}/{totalPlanned} | copied={copied} skipped={skipped} failed={failed}");
        }

        // Debug.LogWarning($"[CopySeq] DONE | planned={totalPlanned} copied={copied} skipped={skipped} failed={failed} | dstRoot={dstRoot}");
        // Debug.LogWarning("Sequence copied to: " + dstRoot);

        if (failed > 0)
        {
            // Debug.LogWarning($"[CopySeq] Invoking onCopyFailed (failed={failed})");
            onCopyFailed?.Invoke(dstRoot);
        }
        else
        {
            // Debug.LogWarning("[CopySeq] Invoking onCopyCompleted");
            onCopyCompleted?.Invoke(dstRoot);
        }
    }

    IEnumerator CopyStreamingFile(string fromUrlOrPath, string toPath, Action<bool> onDone)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(fromUrlOrPath))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                byte[] data = www.downloadHandler.data;
                try
                {
                    File.WriteAllBytes(toPath, data);
                    onDone?.Invoke(true);
                }
                catch (Exception e)
                {
                    // Debug.LogError($"[CopySeq] Write failed: {toPath} | {e.Message}");
                    onDone?.Invoke(false);
                }
            }
            else
            {
                // Debug.LogError($"[CopySeq] Download failed ({www.responseCode}): {fromUrlOrPath} => {toPath} | Error: {www.error}");
                onDone?.Invoke(false);
            }
        }
    }

    IEnumerator ReadStreamingText(string fromUrlOrPath, Action<bool, string> onDone)
    {
        using (UnityWebRequest www = UnityWebRequest.Get(fromUrlOrPath))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                onDone(true, www.downloadHandler.text);
            else
                onDone(false, null);
        }
    }

    static string CombinePath(string a, string b)
    {
        if (string.IsNullOrEmpty(a)) return b;
        if (string.IsNullOrEmpty(b)) return a;
        return a.TrimEnd('/', '\\') + "/" + b.TrimStart('/', '\\');
    }
}
