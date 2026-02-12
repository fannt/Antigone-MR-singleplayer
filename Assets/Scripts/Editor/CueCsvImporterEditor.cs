#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CueCsvImporter))]
public class CueCsvImporterEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        var importer = (CueCsvImporter)target;

        if (GUILayout.Button("Import From Configured Path"))
        {
            importer.ImportFromConfiguredPath();
            EditorUtility.SetDirty(importer);
        }

        if (GUILayout.Button("Pick CSV And Import"))
        {
            string path = EditorUtility.OpenFilePanel("Import Cue Times CSV", Application.dataPath, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                importer.ImportFromCsv(path);
                EditorUtility.SetDirty(importer);
            }
        }
    }
}
#endif
