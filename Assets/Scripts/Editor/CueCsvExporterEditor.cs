#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CueCsvExporter))]
public class CueCsvExporterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var exporter = (CueCsvExporter)target;
        if (GUILayout.Button("Export Cue CSV"))
        {
            if (exporter.ExportCsv(out string path))
            {
                Debug.Log($"CueCsvExporter: export complete -> {path}", exporter);
            }
            else
            {
                Debug.LogWarning("CueCsvExporter: export failed.", exporter);
            }

            EditorUtility.SetDirty(exporter);
        }
    }
}
#endif
