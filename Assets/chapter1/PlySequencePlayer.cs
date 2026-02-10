using UnityEngine;
using System.Linq;

public class PlySequencePlayer : MonoBehaviour
{
    public float fps = 24f;

    [Header("Editor Populate Settings")]
    public string framePrefix = "0000";
    public int startFrame = 0;
    public int endFrame = 300;
    public string assetsFolder = "Assets/Resources/chapter1/video-flee-1/";

    public Mesh[] frames;
    MeshFilter mf;
    int index;
    float timer;

    void Start()
    {
        mf = GetComponent<MeshFilter>();

        if (frames == null || frames.Length == 0)
            Debug.LogWarning("Frames not populated. Use the editor button to populate meshes.", this);
    }

    void Update()
    {
        if (frames == null || frames.Length == 0) return;

        timer += Time.deltaTime;
        if (timer >= 1f / fps)
        {
            timer = 0f;
            index = (index + 1) % frames.Length;
            mf.mesh = frames[index];
        }
    }
}
