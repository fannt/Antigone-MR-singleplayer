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
    [SerializeField] private bool preferStartTimesWhenDurationEmpty = true;

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

        header.TryGetValue("starting time", out int startTimeColumn);
        header.TryGetValue("duration", out int durationColumn);
        header.TryGetValue("cue", out int cueNameColumn);

        var dataByCueIndex = new Dictionary<int, CueTimingData>();
        int skippedRows = 0;

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

            CueTimingData data = new CueTimingData();
            if (TryGetCell(row, startTimeColumn, out string startRaw) && TryParseTimeLikeSeconds(startRaw, out float startSeconds))
            {
                data.hasStart = true;
                data.startSeconds = startSeconds;
            }

            if (TryGetCell(row, durationColumn, out string durationRaw) && TryParseTimeLikeSeconds(durationRaw, out float durationSeconds))
            {
                data.hasDuration = true;
                data.durationSeconds = Mathf.Max(0f, durationSeconds);
            }

            if (TryGetCell(row, cueNameColumn, out string cueName))
                data.cueName = cueName;

            dataByCueIndex[cueIndex] = data;
        }

        int appliedDirectDurations = 0;
        int appliedDerivedDurations = 0;
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
                float newDuration = Mathf.Max(0f, rowData.durationSeconds);
                if (!Mathf.Approximately(cue.duration, newDuration))
                {
                    cue.duration = newDuration;
                    changed = true;
                }

                appliedDirectDurations++;
            }
            else if (preferStartTimesWhenDurationEmpty && rowData.hasStart)
            {
                if (TryFindNextStart(i, dataByCueIndex, out float nextStartSeconds))
                {
                    float derived = Mathf.Max(0f, nextStartSeconds - rowData.startSeconds);
                    if (!Mathf.Approximately(cue.duration, derived))
                    {
                        cue.duration = derived;
                        changed = true;
                    }

                    appliedDerivedDurations++;
                }
            }

            if (!changed)
                unchanged++;
        }

        Debug.Log(
            $"CueCsvImporter: imported from {resolvedPath}. " +
            $"direct={appliedDirectDurations}, derived={appliedDerivedDurations}, unchanged={unchanged}, skippedRows={skippedRows}",
            this);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(cueController);
#endif

        return true;
    }

    private static bool TryFindNextStart(int cueIndex, Dictionary<int, CueTimingData> dataByCueIndex, out float nextStartSeconds)
    {
        int bestIndex = int.MaxValue;
        float bestStart = 0f;
        foreach (KeyValuePair<int, CueTimingData> kvp in dataByCueIndex)
        {
            if (kvp.Key <= cueIndex || !kvp.Value.hasStart)
                continue;

            if (kvp.Key < bestIndex)
            {
                bestIndex = kvp.Key;
                bestStart = kvp.Value.startSeconds;
            }
        }

        nextStartSeconds = bestStart;
        return bestIndex != int.MaxValue;
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
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float hh))
                return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float mm))
                return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ss))
                return false;

            seconds = hh * 3600f + mm * 60f + ss;
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
        public bool hasStart;
        public float startSeconds;
        public bool hasDuration;
        public float durationSeconds;
        public string cueName;
    }
}
