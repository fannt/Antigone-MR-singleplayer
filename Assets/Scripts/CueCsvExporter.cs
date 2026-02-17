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

    private static readonly string[] UnityManagedColumns =
    {
        ColStartingTime,
        ColDuration,
        ColCue,
        ColVr,
        ColUnityCueIndex,
        ColUnityGoto
    };

    private static readonly string[] ExternalColumns =
    {
        ColDescription,
        ColVideo,
        ColLight,
        ColLxCue,
        ColAudio,
        ColAudioIndex
    };

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
                    out int prunedDuplicateManagedRows,
                    out int prunedStaleManagedRows,
                    out int coverageInsertedRows,
                    out int clearedExternalIndexConflicts);
                bool needsNormalization = NeedsTimeNormalization(existingRows);
                bool needsOrdering = NeedsTimelineOrdering(existingRows);
                modeInfo = $"merge(cueSetChanged={cueSetChanged},carried={carriedRows},appended={appendedRows},coverageInserted={coverageInsertedRows},clearedExternalIdx={clearedExternalIndexConflicts},orphanKept={orphanRowsKept},prunedDup={prunedDuplicateManagedRows},prunedStale={prunedStaleManagedRows},normalize={needsNormalization},ordering={needsOrdering})";
                bool hasMergeChanges = appendedRows > 0 ||
                                       coverageInsertedRows > 0 ||
                                       clearedExternalIndexConflicts > 0 ||
                                       prunedDuplicateManagedRows > 0 ||
                                       prunedStaleManagedRows > 0 ||
                                       needsNormalization ||
                                       needsOrdering;
                if (writeOnlyOnCueCountChange && !cueSetChanged && !hasMergeChanges)
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
            SortRowsByTimeline(rowsToWrite);
            NormalizeTimeColumns(rowsToWrite);
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
        out int prunedDuplicateManagedRows,
        out int prunedStaleManagedRows,
        out int coverageInsertedRows,
        out int clearedExternalIndexConflicts)
    {
        carriedRows = 0;
        appendedRows = 0;
        orphanRowsKept = 0;
        prunedDuplicateManagedRows = 0;
        prunedStaleManagedRows = 0;
        coverageInsertedRows = 0;
        clearedExternalIndexConflicts = 0;

        var unityIndices = new HashSet<int>();
        var unityCueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < unityRows.Count; i++)
        {
            Dictionary<string, string> row = unityRows[i];
            if (!TryParseUnityCueIndex(row, out int idx))
                continue;

            unityIndices.Add(idx);
            if (TryGetCueName(row, out string cueName))
                unityCueNames.Add(cueName);
        }

        var existingIndices = new HashSet<int>();
        for (int i = 0; i < existingRows.Count; i++)
        {
            var row = existingRows[i];
            if (TryParseUnityCueIndex(row, out int idx))
                existingIndices.Add(idx);
        }

        cueSetChanged = !existingIndices.SetEquals(unityIndices);

        bool[] usedExistingRows = new bool[existingRows.Count];
        var replacementByExistingRow = new Dictionary<int, Dictionary<string, string>>();
        var appendedUnityRows = new List<Dictionary<string, string>>();
        var outputManagedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < unityRows.Count; i++)
        {
            Dictionary<string, string> unityRow = unityRows[i];
            if (!TryParseUnityCueIndex(unityRow, out _))
                continue;

            int existingMatch = FindBestExistingMatch(existingRows, usedExistingRows, unityRow, cueSetChanged);
            if (existingMatch >= 0)
            {
                Dictionary<string, string> replacement = CloneRow(existingRows[existingMatch]);
                CopyManagedColumns(unityRow, replacement);
                replacementByExistingRow[existingMatch] = replacement;
                usedExistingRows[existingMatch] = true;
                carriedRows++;

                if (TryBuildManagedPairKey(replacement, out string managedPair))
                    outputManagedPairs.Add(managedPair);
            }
            else
            {
                Dictionary<string, string> appended = CloneRow(unityRow);
                appendedUnityRows.Add(appended);
                appendedRows++;

                if (TryBuildManagedPairKey(appended, out string managedPair))
                    outputManagedPairs.Add(managedPair);
            }
        }

        var merged = new List<Dictionary<string, string>>();
        for (int i = 0; i < existingRows.Count; i++)
        {
            if (replacementByExistingRow.TryGetValue(i, out Dictionary<string, string> replacement))
            {
                merged.Add(replacement);
                continue;
            }

            Dictionary<string, string> existingRow = existingRows[i];
            if (TryParseUnityCueIndex(existingRow, out int idx))
            {
                if (!unityIndices.Contains(idx))
                {
                    merged.Add(CloneRow(existingRow));
                    orphanRowsKept++;
                    continue;
                }

                if (TryBuildManagedPairKey(existingRow, out string existingPairKey) && outputManagedPairs.Contains(existingPairKey))
                {
                    prunedDuplicateManagedRows++;
                    continue;
                }

                bool hasCueName = TryGetCueName(existingRow, out string existingCueName);
                bool cueIsManagedByUnity = hasCueName && unityCueNames.Contains(existingCueName);
                if (LooksAutoGeneratedUnityRow(existingRow) || cueIsManagedByUnity)
                {
                    prunedStaleManagedRows++;
                    continue;
                }

                if (hasCueName && !cueIsManagedByUnity)
                {
                    // Keep external cue row, but clear conflicting Unity index so
                    // one Unity cue index maps to one managed cue row.
                    Dictionary<string, string> clearedConflictRow = CloneRow(existingRow);
                    clearedConflictRow[ColUnityCueIndex] = string.Empty;
                    clearedConflictRow[ColUnityGoto] = string.Empty;
                    merged.Add(clearedConflictRow);
                    clearedExternalIndexConflicts++;
                    continue;
                }

                merged.Add(CloneRow(existingRow));
                continue;
            }

            merged.Add(CloneRow(existingRow));
        }

        for (int i = 0; i < appendedUnityRows.Count; i++)
        {
            InsertRowByTimeline(merged, appendedUnityRows[i]);
        }

        coverageInsertedRows = EnsureUnityCoverage(merged, unityRows);

        return merged;
    }

    private static int EnsureUnityCoverage(List<Dictionary<string, string>> mergedRows, List<Dictionary<string, string>> unityRows)
    {
        if (mergedRows == null || unityRows == null || unityRows.Count == 0)
            return 0;

        var existingManagedPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < mergedRows.Count; i++)
        {
            Dictionary<string, string> row = mergedRows[i];
            if (TryBuildManagedPairKey(row, out string pairKey))
                existingManagedPairs.Add(pairKey);
        }

        int inserted = 0;
        for (int i = 0; i < unityRows.Count; i++)
        {
            Dictionary<string, string> unityRow = unityRows[i];
            if (!TryBuildManagedPairKey(unityRow, out string pairKey))
                continue;

            if (existingManagedPairs.Contains(pairKey))
                continue;

            InsertRowByTimeline(mergedRows, unityRow);
            existingManagedPairs.Add(pairKey);
            inserted++;
        }

        return inserted;
    }

    private static int FindBestExistingMatch(
        List<Dictionary<string, string>> existingRows,
        bool[] usedExistingRows,
        Dictionary<string, string> unityRow,
        bool cueSetChanged)
    {
        if (!TryParseUnityCueIndex(unityRow, out int unityIndex))
            return -1;

        string unityCueName = TryGetCueName(unityRow, out string cueName) ? cueName : string.Empty;
        string unityVr = GetCellTrim(unityRow, ColVr);

        int bestRowIndex = -1;
        int bestScore = int.MinValue;
        bool foundCueNameMatch = false;

        for (int i = 0; i < existingRows.Count; i++)
        {
            if (usedExistingRows[i])
                continue;

            Dictionary<string, string> existingRow = existingRows[i];
            if (!TryParseUnityCueIndex(existingRow, out int existingIndex))
                continue;

            string existingCueName = TryGetCueName(existingRow, out string parsedCueName) ? parsedCueName : string.Empty;
            if (!string.IsNullOrEmpty(unityCueName) &&
                string.Equals(unityCueName, existingCueName, StringComparison.OrdinalIgnoreCase))
            {
                foundCueNameMatch = true;
                int score = ScoreExistingMatch(existingRow, unityRow, existingIndex, unityIndex, unityVr, cueSetChanged);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRowIndex = i;
                }
            }
        }

        if (foundCueNameMatch)
            return bestRowIndex;

        // Fallback for rows with no/changed cue names: prefer same Unity index.
        for (int i = 0; i < existingRows.Count; i++)
        {
            if (usedExistingRows[i])
                continue;

            Dictionary<string, string> existingRow = existingRows[i];
            if (!TryParseUnityCueIndex(existingRow, out int existingIndex))
                continue;

            if (existingIndex == unityIndex)
                return i;
        }

        return -1;
    }

    private static int ScoreExistingMatch(
        Dictionary<string, string> existingRow,
        Dictionary<string, string> unityRow,
        int existingIndex,
        int unityIndex,
        string unityVr,
        bool cueSetChanged)
    {
        int score = CountExternalDataColumns(existingRow) * 10;

        if (existingIndex == unityIndex)
            score += cueSetChanged ? 4 : 12;

        string existingVr = GetCellTrim(existingRow, ColVr);
        if (!string.IsNullOrEmpty(unityVr) &&
            string.Equals(existingVr, unityVr, StringComparison.OrdinalIgnoreCase))
            score += 3;

        if (TryGetRowStartTimeSeconds(existingRow, out float existingStart) &&
            TryGetRowStartTimeSeconds(unityRow, out float unityStart))
        {
            float delta = Mathf.Abs(existingStart - unityStart);
            score -= Mathf.RoundToInt(Mathf.Clamp(delta, 0f, 30f));
        }

        return score;
    }

    private static int CountExternalDataColumns(Dictionary<string, string> row)
    {
        int count = 0;
        for (int i = 0; i < ExternalColumns.Length; i++)
        {
            string col = ExternalColumns[i];
            if (!IsEmptyCell(row, col))
                count++;
        }

        return count;
    }

    private static string GetCellTrim(Dictionary<string, string> row, string colName)
    {
        if (!row.TryGetValue(colName, out string value) || string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim();
    }

    private static void CopyManagedColumns(Dictionary<string, string> sourceRow, Dictionary<string, string> targetRow)
    {
        for (int i = 0; i < UnityManagedColumns.Length; i++)
        {
            string col = UnityManagedColumns[i];
            targetRow[col] = sourceRow.TryGetValue(col, out string value) ? value ?? string.Empty : string.Empty;
        }
    }

    private static bool TryBuildManagedPairKey(Dictionary<string, string> row, out string key)
    {
        key = string.Empty;
        if (!TryParseUnityCueIndex(row, out int idx))
            return false;

        if (!TryGetCueName(row, out string cueName))
            return false;

        key = MakePairKey(idx, cueName);
        return true;
    }

    private static bool TryGetCueName(Dictionary<string, string> row, out string cueName)
    {
        cueName = string.Empty;
        if (!row.TryGetValue(ColCue, out string raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        cueName = raw.Trim();
        return cueName.Length > 0;
    }

    private static string MakePairKey(int idx, string cueName)
    {
        return $"{idx}|{cueName.Trim()}";
    }

    private static bool LooksAutoGeneratedUnityRow(Dictionary<string, string> row)
    {
        // Heuristic: rows produced by Unity exporter usually leave show-control fields empty.
        bool emptyDescription = IsEmptyCell(row, ColDescription);
        bool emptyLight = IsEmptyCell(row, ColLight);
        bool emptyLx = IsEmptyCell(row, ColLxCue);
        bool emptyAudioIndex = IsEmptyCell(row, ColAudioIndex);
        bool hasCue = !IsEmptyCell(row, ColCue);
        bool hasVr = !IsEmptyCell(row, ColVr);
        return emptyDescription && emptyLight && emptyLx && emptyAudioIndex && (hasCue || hasVr);
    }

    private static bool IsEmptyCell(Dictionary<string, string> row, string col)
    {
        return !row.TryGetValue(col, out string value) || string.IsNullOrWhiteSpace(value);
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

    private static bool NeedsTimeNormalization(List<Dictionary<string, string>> rows)
    {
        if (rows == null)
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            if (NeedsTimeNormalization(row, ColStartingTime) || NeedsTimeNormalization(row, ColDuration))
                return true;
        }

        return false;
    }

    private static bool NeedsTimeNormalization(Dictionary<string, string> row, string colName)
    {
        if (!row.TryGetValue(colName, out string raw) || string.IsNullOrWhiteSpace(raw))
            return false;

        if (!TryParseMmSsTimestamp(raw, out float seconds))
            return false;

        string normalized = FormatMmSs00(seconds);
        return !string.Equals(raw.Trim(), normalized, StringComparison.Ordinal);
    }

    private static bool NeedsTimelineOrdering(List<Dictionary<string, string>> rows)
    {
        if (rows == null || rows.Count < 2)
            return false;

        bool hasPrevious = false;
        float previous = 0f;
        const float epsilon = 0.0001f;

        for (int i = 0; i < rows.Count; i++)
        {
            if (!TryGetRowStartTimeSeconds(rows[i], out float current))
                continue;

            if (hasPrevious && current + epsilon < previous)
                return true;

            previous = current;
            hasPrevious = true;
        }

        return false;
    }

    private static void NormalizeTimeColumns(List<Dictionary<string, string>> rows)
    {
        if (rows == null)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            NormalizeTimeCell(row, ColStartingTime);
            NormalizeTimeCell(row, ColDuration);
        }
    }

    private static void NormalizeTimeCell(Dictionary<string, string> row, string colName)
    {
        if (!row.TryGetValue(colName, out string raw) || string.IsNullOrWhiteSpace(raw))
            return;

        if (!TryParseMmSsTimestamp(raw, out float seconds))
            return;

        row[colName] = FormatMmSs00(seconds);
    }

    private struct TimelineSortItem
    {
        public Dictionary<string, string> Row;
        public bool HasTime;
        public float TimeSeconds;
        public int OriginalOrder;
    }

    private static void SortRowsByTimeline(List<Dictionary<string, string>> rows)
    {
        if (rows == null || rows.Count < 2)
            return;

        var items = new List<TimelineSortItem>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            Dictionary<string, string> row = rows[i];
            bool hasTime = TryGetRowStartTimeSeconds(row, out float timeSeconds);
            items.Add(new TimelineSortItem
            {
                Row = row,
                HasTime = hasTime,
                TimeSeconds = hasTime ? timeSeconds : 0f,
                OriginalOrder = i
            });
        }

        const float epsilon = 0.0001f;
        items.Sort((a, b) =>
        {
            if (a.HasTime && b.HasTime)
            {
                float delta = a.TimeSeconds - b.TimeSeconds;
                if (Mathf.Abs(delta) > epsilon)
                    return delta < 0f ? -1 : 1;

                return a.OriginalOrder.CompareTo(b.OriginalOrder);
            }

            if (a.HasTime != b.HasTime)
                return a.HasTime ? -1 : 1;

            return a.OriginalOrder.CompareTo(b.OriginalOrder);
        });

        rows.Clear();
        for (int i = 0; i < items.Count; i++)
            rows.Add(items[i].Row);
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
        return string.Format(CultureInfo.InvariantCulture, "{0}{1}:{2:00}:00", sign, minutes, secs);
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
