using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlySequencePlayer))]
public class PlySequencePlayerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        if (GUILayout.Button("Populate Frames From Assets"))
        {
            PopulateFrames();
        }
    }

    void PopulateFrames()
    {
        var player = (PlySequencePlayer)target;
        var list = new List<Mesh>();
        var folder = player.assetsFolder;

        for (int i = player.startFrame; i <= player.endFrame; i++)
        {
            var name = player.framePrefix + i.ToString("D3");
            var mesh = FindMeshByName(name, folder);
            if (mesh != null)
                list.Add(mesh);
            else
                Debug.LogWarning("not found mesh asset name: " + name + " in " + folder, player);
        }

        Undo.RecordObject(player, "Populate Frames");
        player.frames = list.ToArray();
        EditorUtility.SetDirty(player);

        Debug.LogWarning("Loaded frames: " + player.frames.Length, player);
    }

    static Mesh FindMeshByName(string name, string folder)
    {
        var guids = AssetDatabase.FindAssets(name + " t:Mesh", new[] { folder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in assets)
            {
                var mesh = asset as Mesh;
                if (mesh != null && mesh.name == name)
                    return mesh;
            }
        }

        return null;
    }
}
