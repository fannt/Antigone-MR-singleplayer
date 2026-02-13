using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Cue/Cue CSV Importer")]
public class CueCsvImporter : MonoBehaviour
{
    [Header("Cue Target")]
    [SerializeField] private CueController cueController;
    [SerializeField] private bool autoFindCueController = true;

    [Header("CSV Input")]
    [SerializeField] private string csvFilePath = string.Empty;

    [ContextMenu("Import Cue Times CSV")]
    public void ImportFromConfiguredPath()
    {
        if (string.IsNullOrWhiteSpace(csvFilePath))
        {
            Debug.LogWarning("CueCsvImporter: csvFilePath is empty.", this);
            return;
        }

        ImportFromCsv(csvFilePath);
    }

    public bool ImportFromCsv(string path)
    {
        if (!TryAssignCueController())
        {
            Debug.LogWarning("CueCsvImporter: no CueController found.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            Debug.LogWarning("CueCsvImporter: CSV path is empty.", this);
            return false;
        }

        string resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
        {
            Debug.LogWarning($"CueCsvImporter: file does not exist: {resolvedPath}", this);
            return false;
        }

        string csvText = File.ReadAllText(resolvedPath);
        List<List<string>> rows = ParseCsv(csvText);
        if (rows.Count < 2)
        {
            Debug.LogWarning($"CueCsvImporter: no data rows in CSV {resolvedPath}", this);
            return false;
        }

        Dictionary<string, int> header = BuildHeaderMap(rows[0]);
        if (!header.TryGetValue("unity cue index", out int unityIndexColumn))
        {
            Debug.LogWarning("CueCsvImporter: missing required column 'Unity Cue index'.", this);
            return false;
        }

        if (!header.TryGetValue("duration", out int durationColumn))
        {
            Debug.LogWarning("CueCsvImporter: missing required column 'Duration'.", this);
            return false;
        }

        var dataByCueIndex = new Dictionary<int, CueTimingData>();
        int skippedRows = 0;
        int continuationRows = 0;
        int rowsWithoutDuration = 0;

        for (int r = 1; r < rows.Count; r++)
        {
            List<string> row = rows[r];
            if (IsRowEmpty(row))
                continue;

            if (!TryGetCell(row, unityIndexColumn, out string indexRaw) || !TryParseCueIndex(indexRaw, out int cueIndex))
            {
                skippedRows++;
                continue;
            }

            if (!cueController.IsCueIndexValid(cueIndex))
            {
                skippedRows++;
                continue;
            }

            if (TryGetCell(row, durationColumn, out string durationRaw) && TryParseTimeLikeSeconds(durationRaw, out float durationSeconds))
            {
                float clampedDuration = Mathf.Max(0f, durationSeconds);
                if (dataByCueIndex.TryGetValue(cueIndex, out CueTimingData existing))
                {
                    existing.hasDuration = true;
                    existing.durationSeconds += clampedDuration;
                    existing.segmentCount += 1;
                    dataByCueIndex[cueIndex] = existing;
                    continuationRows++;
                }
                else
                {
                    CueTimingData data = new CueTimingData
                    {
                        hasDuration = true,
                        durationSeconds = clampedDuration,
                        segmentCount = 1
                    };
                    dataByCueIndex[cueIndex] = data;
                }
            }
            else
            {
                rowsWithoutDuration++;
                continue;
            }
        }

        int appliedDurations = 0;
        int unchanged = 0;

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(cueController, "Import Cue CSV Timings");
#endif

        for (int i = 0; i < cueController.CueCount; i++)
        {
            if (!dataByCueIndex.TryGetValue(i, out CueTimingData rowData))
                continue;

            Cue cue = cueController.cues[i];
            if (cue == null)
                continue;

            bool changed = false;
            if (rowData.hasDuration)
            {
                float nextDuration = Mathf.Max(0f, rowData.durationSeconds);
                if (!Mathf.Approximately(cue.duration, nextDuration))
                {
                    cue.duration = nextDuration;
                    changed = true;
                }

                appliedDurations++;
            }

            if (!changed)
                unchanged++;
        }

        Debug.Log(
            $"CueCsvImporter: imported from {resolvedPath}. " +
            $"durations={appliedDurations}, unchanged={unchanged}, skippedRows={skippedRows}, continuationRows={continuationRows}, rowsWithoutDuration={rowsWithoutDuration}",
            this);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(cueController);
#endif

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

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, path);
    }

    private static bool TryParseCueIndex(string raw, out int cueIndex)
    {
        cueIndex = -1;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string trimmed = raw.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out cueIndex))
            return true;

        if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
        {
            cueIndex = Mathf.RoundToInt(f);
            return true;
        }

        return false;
    }

    private static bool TryParseTimeLikeSeconds(string raw, out float seconds)
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

        if (parts.Length == 2)
        {
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float mm))
                return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ss))
                return false;

            seconds = mm * 60f + ss;
        }
        else
        {
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float mm))
                return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ss))
                return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sub))
                return false;

            // 3-part format is treated as MM:SS:subseconds (not frames).
            // Examples: 2:10:00 -> 130s, 0:05:50 -> 5.5s.
            string subPart = parts[2].Trim();
            int subDigits = Mathf.Clamp(subPart.Length, 1, 6);
            float subScale = Mathf.Pow(10f, subDigits);
            seconds = mm * 60f + ss + (sub / subScale);
        }

        if (negative)
            seconds = -seconds;

        return true;
    }

    private static Dictionary<string, int> BuildHeaderMap(List<string> headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerRow.Count; i++)
        {
            string key = (headerRow[i] ?? string.Empty).Trim().ToLowerInvariant();
            if (!map.ContainsKey(key))
                map[key] = i;
        }

        return map;
    }

    private static bool TryGetCell(List<string> row, int columnIndex, out string value)
    {
        value = string.Empty;
        if (columnIndex < 0 || columnIndex >= row.Count)
            return false;

        value = row[columnIndex] ?? string.Empty;
        return true;
    }

    private static bool IsRowEmpty(List<string> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(row[i]))
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
        var cell = new System.Text.StringBuilder();
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

    private struct CueTimingData
    {
        public bool hasDuration;
        public float durationSeconds;
        public int segmentCount;
    }
}
