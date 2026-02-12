using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Cue/Cue CSV Exporter")]
public class CueCsvExporter : MonoBehaviour
{
    private const string CsvHeader = "Cue,starting Time,Duration,VR,Description / Choreography,Video,Light,Audio,Notes";

    [Header("Cue Source")]
    [SerializeField] private CueController cueController;
    [SerializeField] private bool autoFindCueController = true;

    [Header("CSV Output")]
    [SerializeField] private string outputDirectory = "Exports";
    [SerializeField] private string outputFileName = "cue_export.csv";
    [Tooltip("Offset applied to cue start times. Example: -60 writes first cue as -01:00.")]
    [SerializeField] private float timelineOffsetSeconds = 0f;
    [SerializeField] private bool includeCueIndexInNotes = true;

    public string LastExportPath { get; private set; } = string.Empty;

    [ContextMenu("Export Cue CSV")]
    public void ExportFromContextMenu()
    {
        ExportCsv();
    }

    public bool ExportCsv()
    {
        return ExportCsv(out _);
    }

    public bool ExportCsv(out string exportedPath)
    {
        exportedPath = string.Empty;
        if (!TryAssignCueController())
        {
            Debug.LogWarning("CueCsvExporter: no CueController found.", this);
            return false;
        }

        string csv = BuildCsv();
        string path = BuildOutputPath();
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, csv, new UTF8Encoding(false));
        LastExportPath = path;
        exportedPath = path;

        int cueCount = cueController.cues != null ? cueController.cues.Length : 0;
        Debug.Log($"CueCsvExporter: exported {cueCount} cues to {path}", this);
        return true;
    }

    private bool TryAssignCueController()
    {
        if (cueController != null)
            return true;

        if (!autoFindCueController)
            return false;

        cueController = GetComponent<CueController>();
        if (cueController != null)
            return true;

#if UNITY_2023_1_OR_NEWER
        cueController = FindFirstObjectByType<CueController>();
#else
        cueController = FindObjectOfType<CueController>();
#endif
        return cueController != null;
    }

    private string BuildCsv()
    {
        var sb = new StringBuilder(1024);
        sb.AppendLine(CsvHeader);

        Cue[] cues = cueController.cues;
        if (cues == null || cues.Length == 0)
            return sb.ToString();

        float runningStartSeconds = timelineOffsetSeconds;
        for (int i = 0; i < cues.Length; i++)
        {
            Cue cue = cues[i];
            if (cue == null)
                continue;

            string cueName = string.IsNullOrWhiteSpace(cue.cueName) ? $"Cue {i}" : cue.cueName.Trim();
            string startTime = FormatTimestamp(runningStartSeconds);
            string duration = FormatDurationSeconds(cue.duration);
            string vr = BuildVrField(cue);
            string description = string.Empty;
            string video = BuildVideoField(cue);
            string light = string.Empty;
            string audio = BuildAudioField(cue);
            string notes = BuildNotesField(cue, i);

            AppendCsvRow(sb, cueName, startTime, duration, vr, description, video, light, audio, notes);
            runningStartSeconds += Mathf.Max(0f, cue.duration);
        }

        return sb.ToString();
    }

    private string BuildOutputPath()
    {
        string fileName = string.IsNullOrWhiteSpace(outputFileName)
            ? $"cue_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            : outputFileName.Trim();

        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            fileName += ".csv";

        string directory = string.IsNullOrWhiteSpace(outputDirectory) ? "Exports" : outputDirectory.Trim();
        if (!Path.IsPathRooted(directory))
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            directory = Path.Combine(projectRoot, directory);
        }

        return Path.Combine(directory, fileName);
    }

    private string BuildVrField(Cue cue)
    {
        var parts = new List<string>(2);
        if (cue.gameObject != null)
        {
            string toggleVerb = cue.toggleActiveTo ? "on" : "off";
            parts.Add($"object trigger ({toggleVerb}): {cue.gameObject.name}");
        }

        if (cue.geometryPlayer != null)
            parts.Add($"geometry {cue.geometryAction}: {cue.geometryPlayer.name}");

        return string.Join(" / ", parts);
    }

    private static string BuildVideoField(Cue cue)
    {
        if (cue.video == null)
            return string.Empty;

        string playerName = cue.video.gameObject != null ? cue.video.gameObject.name : cue.video.name;
        string clipName = cue.video.clip != null ? cue.video.clip.name : string.Empty;
        if (string.IsNullOrEmpty(clipName) || string.Equals(playerName, clipName, StringComparison.Ordinal))
            return playerName;

        return $"{playerName} ({clipName})";
    }

    private static string BuildAudioField(Cue cue)
    {
        if (cue.audio == null)
            return string.Empty;

        string sourceName = cue.audio.gameObject != null ? cue.audio.gameObject.name : cue.audio.name;
        string clipName = cue.audio.clip != null ? cue.audio.clip.name : string.Empty;
        if (string.IsNullOrEmpty(clipName) || string.Equals(sourceName, clipName, StringComparison.Ordinal))
            return sourceName;

        return $"{sourceName} ({clipName})";
    }

    private string BuildNotesField(Cue cue, int cueIndex)
    {
        var notes = new List<string>(2);

        if (cue.goToNextCue)
            notes.Add("auto-next");

        if (includeCueIndexInNotes)
            notes.Add($"index={cueIndex}");

        return string.Join(" | ", notes);
    }

    private static string FormatDurationSeconds(float seconds)
    {
        return Mathf.Max(0f, seconds).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void AppendCsvRow(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
                sb.Append(',');

            sb.Append(EscapeCsv(fields[i]));
        }

        sb.AppendLine();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        bool needsQuotes = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
        if (!needsQuotes)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string FormatTimestamp(float seconds)
    {
        bool isNegative = seconds < 0f;
        int totalSeconds = Mathf.RoundToInt(Mathf.Abs(seconds));

        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int secs = totalSeconds % 60;

        string sign = isNegative ? "-" : string.Empty;
        if (hours > 0)
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}:{3:00}", sign, hours, minutes, secs);

        return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}", sign, minutes, secs);
    }
}
