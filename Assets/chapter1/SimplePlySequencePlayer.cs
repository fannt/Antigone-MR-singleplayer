using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Simple PLY sequence player - loads at runtime, minimal code
/// Works on Quest with mesh rendering
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimplePlySequencePlayer : MonoBehaviour
{
    [Header("Sequence")]
    [Tooltip("Path relative to StreamingAssets (e.g. 'chapter1/video-flee-1')")]
    public string folderPath = "Assets/chapter1/video-flee-1";
    public float fps = 24f;
    public bool loop = true;

    [Header("Performance")]
    [Tooltip("Frames to cache in memory")]
    public int cacheSize = 15;

    private MeshFilter mf;
    private Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();
    private Queue<int> cacheOrder = new Queue<int>();
    private string[] plyFiles;
    private int currentFrame = 0;
    private float timer = 0f;

    void Start()
    {
        mf = GetComponent<MeshFilter>();

        // Setup material
        var mr = GetComponent<MeshRenderer>();
        if (mr.sharedMaterial == null)
        {
            var shader = Shader.Find("Point Cloud/Point");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            mr.sharedMaterial = new Material(shader);
            Debug.Log($"Created material with shader: {shader.name}");
        }

        // Find all PLY files - use StreamingAssets on Quest
        string fullPath;
        #if UNITY_EDITOR
        fullPath = Path.Combine("Assets/StreamingAssets", folderPath);
        #else
        fullPath = Path.Combine(Application.streamingAssetsPath, folderPath);
        #endif

        plyFiles = Directory.GetFiles(fullPath, "*.ply")
            .OrderBy(f => f).ToArray();

        Debug.Log($"Found {plyFiles.Length} PLY files in {fullPath}");

        if (plyFiles.Length == 0)
        {
            Debug.LogError($"No PLY files found in: {folderPath}");
            return;
        }

        // Preload first frames
        for (int i = 0; i < Mathf.Min(cacheSize, plyFiles.Length); i++)
            LoadFrame(i);

        // Show first frame
        if (cache.ContainsKey(0) && cache[0] != null)
        {
            mf.mesh = cache[0];
            Debug.Log($"Showing first frame with {cache[0].vertexCount} points");
        }
        else
        {
            Debug.LogError("Failed to load first frame!");
        }
    }

    void Update()
    {
        if (plyFiles == null || plyFiles.Length == 0) return;
        if (mf == null) return;

        timer += Time.deltaTime;
        if (timer >= 1f / fps)
        {
            timer = 0f;
            currentFrame++;

            if (currentFrame >= plyFiles.Length)
                currentFrame = loop ? 0 : plyFiles.Length - 1;

            // Show frame
            if (cache.ContainsKey(currentFrame) && cache[currentFrame] != null)
            {
                mf.mesh = cache[currentFrame];
            }
            else
            {
                LoadFrame(currentFrame);
                if (cache.ContainsKey(currentFrame) && cache[currentFrame] != null)
                    mf.mesh = cache[currentFrame];
            }

            // Preload next
            int next = currentFrame + 1;
            if (next < plyFiles.Length && !cache.ContainsKey(next))
                LoadFrame(next);
        }
    }

    void LoadFrame(int index)
    {
        if (cache.ContainsKey(index)) return;

        var points = LoadPLY(plyFiles[index]);
        if (points == null || points.Length == 0)
        {
            Debug.LogWarning($"Failed to load frame {index}: {plyFiles[index]}");
            return;
        }

        var mesh = new Mesh();
        mesh.name = $"Frame_{index}";
        mesh.indexFormat = points.Length > 65535 ?
            UnityEngine.Rendering.IndexFormat.UInt32 :
            UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(points.Select(p => p.position).ToArray());
        mesh.SetColors(points.Select(p => p.color).ToArray());
        mesh.SetIndices(Enumerable.Range(0, points.Length).ToArray(),
            MeshTopology.Points, 0);
        mesh.RecalculateBounds();

        // Cache management
        if (cache.Count >= cacheSize)
        {
            var old = cacheOrder.Dequeue();
            if (cache[old] != null) Destroy(cache[old]);
            cache.Remove(old);
        }

        cache[index] = mesh;
        cacheOrder.Enqueue(index);

        if (index == 0)
            Debug.Log($"Loaded frame 0: {points.Length} points, bounds: {mesh.bounds}");
    }

    // Minimal PLY loader
    struct Point { public Vector3 position; public Color32 color; }

    Point[] LoadPLY(string path)
    {
        try
        {
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read))
            using (var reader = new StreamReader(stream))
            {
                // Parse header
                string line;
                int vertexCount = 0;
                var props = new List<(string type, string name)>();

                // Read until end_header
                while ((line = reader.ReadLine()) != "end_header")
                {
                    var parts = line.Split(' ');
                    if (parts[0] == "element" && parts[1] == "vertex")
                        vertexCount = int.Parse(parts[2]);
                    if (parts[0] == "property")
                        props.Add((parts[1], parts[2])); // Store type and name
                }

                // Read binary data
                var br = new BinaryReader(stream);
                var points = new Point[vertexCount];

                for (int i = 0; i < vertexCount; i++)
                {
                    float x = 0, y = 0, z = 0;
                    byte r = 255, g = 255, b = 255;

                    foreach (var (type, name) in props)
                    {
                        switch (name)
                        {
                            case "x": x = br.ReadSingle(); break;
                            case "y": y = br.ReadSingle(); break;
                            case "z": z = br.ReadSingle(); break;
                            case "red": r = br.ReadByte(); break;
                            case "green": g = br.ReadByte(); break;
                            case "blue": b = br.ReadByte(); break;
                            default:
                                // Skip based on type
                                if (type == "float") br.ReadSingle();
                                else if (type == "uchar") br.ReadByte();
                                else if (type == "double") br.ReadDouble();
                                else br.ReadByte();
                                break;
                        }
                    }

                    points[i] = new Point
                    {
                        position = new Vector3(x, y, z),
                        color = new Color32(r, g, b, 255)
                    };
                }

                return points;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load {path}: {e.Message}");
            return null;
        }
    }

    void OnDestroy()
    {
        foreach (var mesh in cache.Values)
            if (mesh != null) Destroy(mesh);
    }
}
