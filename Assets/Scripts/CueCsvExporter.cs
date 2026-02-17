using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public enum CueCsvWriteMode
{
    RewriteAlways,
    MergeExisting
}

[DisallowMultipleComponent]
[AddComponentMenu("Cue/Cue CSV Exporter")]
public class CueCsvExporter : MonoBehaviour
{
    private const string ColStartingTime = "starting Time";
    private const string ColDuration = "Duration";
    private const string ColCue = "Cue";
    private const string ColVr = "VR";
    private const string ColUnityCueIndex = "Unity Cue index";
    private const string ColUnityGoto = "unity goto";
    private const string ColDescription = "Description / Choreography";
    private const string ColVideo = "Video";
    private const string ColLight = "Light";
    private const string ColLxCue = "LX cue";
    private const string ColAudio = "Audio";
    private const string ColAudioIndex = "Audio index";

    private static readonly string[] CsvColumns =
    {
        ColStartingTime,
        ColDuration,
        ColCue,
        ColVr,
        ColUnityCueIndex,
        ColUnityGoto,
        ColDescription,
        ColVideo,
        ColLight,
        ColLxCue,
        ColAudio,
        ColAudioIndex
    };

    [Header("Cue Source")]
    [SerializeField] private CueController cueController;
    [SerializeField] private bool autoFindCueController = true;

    [Header("CSV Output")]
    [SerializeField] private string outputDirectory = "Exports";
    [SerializeField] private string outputFileName = "cue_export.csv";
    [Tooltip("Offset applied to cue start times. Example: -60 writes first cue as -01:00:00.")]
    [SerializeField] private float timelineOffsetSeconds = 0f;
    [SerializeField] private CueCsvWriteMode writeMode = CueCsvWriteMode.MergeExisting;
    [Tooltip("In Merge mode, skip writing when Unity cue index set is unchanged (no add/delete).")]
    [SerializeField] private bool writeOnlyOnCueCountChange = true;

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

        string path = BuildOutputPath();
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        List<Dictionary<string, string>> unityRows = BuildUnityRows();
        List<Dictionary<string, string>> rowsToWrite = unityRows;
        bool shouldWrite = true;
        string modeInfo = "rewrite";

        if (writeMode == CueCsvWriteMode.MergeExisting && File.Exists(path))
        {
            if (TryReadExistingRows(path, out List<Dictionary<string, string>> existingRows))
            {
                rowsToWrite = MergeRows(
                    unityRows,
                    existingRows,
                    out bool cueSetChanged,
                    out int carriedRows,
                    out int appendedRows,
                    out int orphanRowsKept,
                    out int conflictAppendedRows);
                modeInfo = $"merge(cueSetChanged={cueSetChanged},carried={carriedRows},appended={appendedRows},conflictAppended={conflictAppendedRows},orphanKept={orphanRowsKept})";
                bool hasMergeAdditions = appendedRows > 0 || conflictAppendedRows > 0;
                if (writeOnlyOnCueCountChange && !cueSetChanged && !hasMergeAdditions)
                {
                    shouldWrite = false;
                    modeInfo += "-skipped";
                }
            }
            else
            {
                modeInfo = "rewrite(fallback-from-merge-parse-failure)";
            }
        }

        if (shouldWrite)
        {
            string csv = BuildCsv(rowsToWrite);
            File.WriteAllText(path, csv, new UTF8Encoding(false));
        }

        LastExportPath = path;
        exportedPath = path;

        int cueCount = cueController.cues != null ? cueController.cues.Length : 0;
        Debug.Log($"CueCsvExporter: exported {cueCount} cues to {path} [{modeInfo}]", this);
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

    private List<Dictionary<string, string>> BuildUnityRows()
    {
        var rows = new List<Dictionary<string, string>>();

        Cue[] cues = cueController.cues;
        if (cues == null || cues.Length == 0)
            return rows;

        float runningStartSeconds = timelineOffsetSeconds;
        for (int i = 0; i < cues.Length; i++)
        {
            Cue cue = cues[i];
            if (cue == null)
                continue;

            string cueName = string.IsNullOrWhiteSpace(cue.cueName) ? $"Cue {i}" : cue.cueName.Trim();
            string startTime = FormatMmSs00(runningStartSeconds);
            string duration = FormatMmSs00(Mathf.Max(0f, cue.duration));
            string vr = BuildVrField(cue, cueName);
            string unityCueIndex = i.ToString(CultureInfo.InvariantCulture);
            string unityGoto = cue.goToNextCue ? "1" : "0";
            string description = string.Empty;
            string video = BuildVideoField(cue);
            string light = string.Empty;
            string lxCue = string.Empty;
            string audio = BuildAudioField(cue);
            string audioIndex = string.Empty;

            var row = CreateEmptyRow();
            row[ColStartingTime] = startTime;
            row[ColDuration] = duration;
            row[ColCue] = cueName;
            row[ColVr] = vr;
            row[ColUnityCueIndex] = unityCueIndex;
            row[ColUnityGoto] = unityGoto;
            row[ColDescription] = description;
            row[ColVideo] = video;
            row[ColLight] = light;
            row[ColLxCue] = lxCue;
            row[ColAudio] = audio;
            row[ColAudioIndex] = audioIndex;
            rows.Add(row);

            runningStartSeconds += Mathf.Max(0f, cue.duration);
        }

        return rows;
    }

    private static string BuildCsv(List<Dictionary<string, string>> rows)
    {
        var sb = new StringBuilder(1024);
        AppendCsvRow(sb, CsvColumns);

        for (int r = 0; r < rows.Count; r++)
        {
            Dictionary<string, string> row = rows[r];
            string[] fields = new string[CsvColumns.Length];
            for (int c = 0; c < CsvColumns.Length; c++)
            {
                row.TryGetValue(CsvColumns[c], out fields[c]);
            }

            AppendCsvRow(sb, fields);
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

    private static Dictionary<string, string> CreateEmptyRow()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < CsvColumns.Length; i++)
            row[CsvColumns[i]] = string.Empty;

        return row;
    }

    private static Dictionary<string, string> CloneRow(Dictionary<string, string> source)
    {
        var row = CreateEmptyRow();
        for (int i = 0; i < CsvColumns.Length; i++)
        {
            string col = CsvColumns[i];
            if (source.TryGetValue(col, out string value))
                row[col] = value ?? string.Empty;
        }

        return row;
    }

    private static bool TryReadExistingRows(string path, out List<Dictionary<string, string>> rows)
    {
        rows = new List<Dictionary<string, string>>();
        if (!File.Exists(path))
            return false;

        string csvText = File.ReadAllText(path);
        List<List<string>> matrix = ParseCsv(csvText);
        if (matrix.Count == 0)
            return false;

        Dictionary<string, int> headerMap = BuildHeaderMap(matrix[0]);
        for (int r = 1; r < matrix.Count; r++)
        {
            List<string> rawRow = matrix[r];
            var row = CreateEmptyRow();
            for (int c = 0; c < CsvColumns.Length; c++)
            {
                string col = CsvColumns[c];
                if (headerMap.TryGetValue(col, out int idx) && idx >= 0 && idx < rawRow.Count)
                    row[col] = rawRow[idx];
            }

            if (!IsRowEmpty(row))
                rows.Add(row);
        }

        return true;
    }

    private static List<Dictionary<string, string>> MergeRows(
        List<Dictionary<string, string>> unityRows,
        List<Dictionary<string, string>> existingRows,
        out bool cueSetChanged,
        out int carriedRows,
        out int appendedRows,
        out int orphanRowsKept,
        out int conflictAppendedRows)
    {
        carriedRows = 0;
        appendedRows = 0;
        orphanRowsKept = 0;
        conflictAppendedRows = 0;

        var unityIndices = new HashSet<int>();
        for (int i = 0; i < unityRows.Count; i++)
        {
            Dictionary<string, string> row = unityRows[i];
            if (!TryParseUnityCueIndex(row, out int idx))
                continue;

            unityIndices.Add(idx);
        }

        var existingIndices = new HashSet<int>();
        var existingCueNamesByIndex = new Dictionary<int, HashSet<string>>();
        for (int i = 0; i < existingRows.Count; i++)
        {
            var row = existingRows[i];
            if (TryParseUnityCueIndex(row, out int idx))
            {
                existingIndices.Add(idx);

                if (!existingCueNamesByIndex.TryGetValue(idx, out HashSet<string> cueNames))
                {
                    cueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    existingCueNamesByIndex[idx] = cueNames;
                }

                if (row.TryGetValue(ColCue, out string cueName) && !string.IsNullOrWhiteSpace(cueName))
                    cueNames.Add(cueName.Trim());
            }
        }

        cueSetChanged = !existingIndices.SetEquals(unityIndices);

        var merged = new List<Dictionary<string, string>>();
        for (int i = 0; i < existingRows.Count; i++)
        {
            Dictionary<string, string> existingRow = existingRows[i];
            if (TryParseUnityCueIndex(existingRow, out int idx))
            {
                if (!unityIndices.Contains(idx))
                {
                    merged.Add(CloneRow(existingRow));
                    orphanRowsKept++;
                    continue;
                }

                merged.Add(CloneRow(existingRow));
                carriedRows++;
                continue;
            }

            merged.Add(CloneRow(existingRow));
        }

        for (int i = 0; i < unityRows.Count; i++)
        {
            Dictionary<string, string> unityRow = unityRows[i];
            if (!TryParseUnityCueIndex(unityRow, out int idx))
                continue;

            if (existingIndices.Contains(idx))
                continue;

            InsertRowByTimeline(merged, unityRow);
            appendedRows++;
        }

        // If index exists but cue name changed, append Unity row so newly added cues are not lost.
        for (int i = 0; i < unityRows.Count; i++)
        {
            Dictionary<string, string> unityRow = unityRows[i];
            if (!TryParseUnityCueIndex(unityRow, out int idx))
                continue;

            if (!existingIndices.Contains(idx))
                continue;

            if (!unityRow.TryGetValue(ColCue, out string unityCueName) || string.IsNullOrWhiteSpace(unityCueName))
                continue;

            bool hasMatchingName = existingCueNamesByIndex.TryGetValue(idx, out HashSet<string> namesForIndex)
                && namesForIndex.Contains(unityCueName.Trim());
            if (hasMatchingName)
                continue;

            InsertRowByTimeline(merged, unityRow);
            conflictAppendedRows++;
        }

        return merged;
    }

    private static bool TryParseUnityCueIndex(Dictionary<string, string> row, out int idx)
    {
        idx = -1;
        if (!row.TryGetValue(ColUnityCueIndex, out string raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        string trimmed = raw.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx))
            return true;

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatIdx))
        {
            idx = Mathf.RoundToInt(floatIdx);
            return true;
        }

        return false;
    }

    private static void InsertRowByTimeline(List<Dictionary<string, string>> rows, Dictionary<string, string> sourceRow)
    {
        Dictionary<string, string> rowToInsert = CloneRow(sourceRow);
        if (!TryGetRowStartTimeSeconds(rowToInsert, out float rowTimeSeconds))
        {
            rows.Add(rowToInsert);
            return;
        }

        int insertAt = rows.Count;
        int lastTimedIndex = -1;
        const float epsilon = 0.0001f;

        for (int i = 0; i < rows.Count; i++)
        {
            if (!TryGetRowStartTimeSeconds(rows[i], out float existingTimeSeconds))
                continue;

            lastTimedIndex = i;

            if (existingTimeSeconds > rowTimeSeconds + epsilon)
            {
                insertAt = i;
                break;
            }

            if (Mathf.Abs(existingTimeSeconds - rowTimeSeconds) <= epsilon)
            {
                insertAt = i + 1;
            }
        }

        if (insertAt == rows.Count && lastTimedIndex >= 0)
            insertAt = lastTimedIndex + 1;

        rows.Insert(Mathf.Clamp(insertAt, 0, rows.Count), rowToInsert);
    }

    private static bool TryGetRowStartTimeSeconds(Dictionary<string, string> row, out float seconds)
    {
        seconds = 0f;
        if (!row.TryGetValue(ColStartingTime, out string raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        return TryParseMmSsTimestamp(raw, out seconds);
    }

    private static bool TryParseMmSsTimestamp(string raw, out float seconds)
    {
        seconds = 0f;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string trimmed = raw.Trim();
        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
            return true;

        bool negative = trimmed.StartsWith("-", StringComparison.Ordinal);
        if (negative)
            trimmed = trimmed.Substring(1);

        string[] parts = trimmed.Split(':');
        if (parts.Length < 2 || parts.Length > 3)
            return false;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float mm))
            return false;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ss))
            return false;

        float result = mm * 60f + ss;
        if (parts.Length == 3)
        {
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ms))
                return false;

            // MM:SS:MS (milliseconds)
            result += ms / 1000f;
        }

        seconds = negative ? -result : result;
        return true;
    }

    private static Dictionary<string, int> BuildHeaderMap(List<string> headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerRow.Count; i++)
        {
            string key = (headerRow[i] ?? string.Empty).Trim();
            if (!map.ContainsKey(key))
                map[key] = i;
        }

        return map;
    }

    private static bool IsRowEmpty(Dictionary<string, string> row)
    {
        for (int i = 0; i < CsvColumns.Length; i++)
        {
            if (row.TryGetValue(CsvColumns[i], out string value) && !string.IsNullOrWhiteSpace(value))
                return false;
        }

        return true;
    }

    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text))
            return rows;

        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    bool escaped = (i + 1 < text.Length && text[i + 1] == '"');
                    if (escaped)
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                continue;
            }

            if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Length = 0;
                continue;
            }

            if (c == '\n')
            {
                row.Add(cell.ToString());
                cell.Length = 0;
                rows.Add(row);
                row = new List<string>();
                continue;
            }

            if (c == '\r')
                continue;

            cell.Append(c);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private string BuildVrField(Cue cue, string cueName)
    {
        if (cue.gameObject != null)
        {
            string toggleVerb = cue.toggleActiveTo ? "on" : "off";
            return $"object trigger ({toggleVerb}): {cue.gameObject.name}";
        }

        if (cue.audio != null)
            return BuildAudioField(cue);

        if (cue.geometryPlayer != null)
            return $"geometry {cue.geometryAction}: {cue.geometryPlayer.name}";

        return cueName;
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

    private static string FormatMmSs00(float seconds)
    {
        bool isNegative = seconds < 0f;
        int totalSeconds = Mathf.RoundToInt(Mathf.Abs(seconds));
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;

        string sign = isNegative ? "-" : string.Empty;
        return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}:00", sign, minutes, secs);
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
}
