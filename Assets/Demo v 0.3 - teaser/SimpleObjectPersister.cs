using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class SimpleObjectPersister : MonoBehaviour
{
    [Header("Dependencies")]
    public ARAnchorManager anchorManager;

    [Header("Settings")]
    public string uniqueSaveID = "MySceneObject_01"; // Must be unique per object
    public LayerMask placementLayers = -1; // Set to Default or Spatial Awareness

    private bool _isPlacing = false;
    private ARAnchor _currentAnchor;

    void Start()
    {
        // 1. Auto-find manager if not assigned
        if (anchorManager == null) 
            anchorManager = FindFirstObjectByType<ARAnchorManager>();

        // 2. Try to load saved position immediately
        LoadSavedPosition();
    }

    void Update()
    {
        // 3. Logic to move object with Head Gaze
        if (_isPlacing)
        {
            PerformRaycastPlacement();
        }
    }

    // --- CONNECT THIS TO YOUR HAND MENU TOGGLE ---
    public void TogglePlacementMode(bool verify)
    {
        _isPlacing = verify;

        if (_isPlacing)
        {
            // Detach from any old anchor so we can move
            transform.SetParent(null);
            
            // Clean up old anchor GameObject to avoid junk
            if (_currentAnchor != null) Destroy(_currentAnchor.gameObject);
        }
    }

    // --- CONNECT THIS TO YOUR HAND MENU 'SAVE' BUTTON ---
    public async void LockAndSave()
    {
        // Turn off placement mode
        _isPlacing = false;

        if (anchorManager == null) return;

        // 1. Create Anchor at current spot
        Pose pose = new Pose(transform.position, transform.rotation);
        var result = await anchorManager.TryAddAnchorAsync(pose);

        if (result.status.IsSuccess())
        {
            _currentAnchor = result.value;

            // 2. Parent object to anchor
            transform.SetParent(_currentAnchor.transform);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            // 3. Save to Quest Storage
            var saveResult = await anchorManager.TrySaveAnchorAsync(_currentAnchor);

            if (saveResult.status.IsSuccess())
            {
                // 4. Save ID to PlayerPrefs
                SerializableGuid guid = saveResult.value;
                PlayerPrefs.SetString(uniqueSaveID, guid.ToString());
                PlayerPrefs.Save();
                Debug.Log($"<color=green>SAVED: {uniqueSaveID}</color>");
            }
        }
    }

    private void PerformRaycastPlacement()
    {
        // Ray from head center
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 10f, placementLayers))
        {
            transform.position = hit.point;
            
            // Optional: Make it look at you but stay flat
            Vector3 lookDir = transform.position - Camera.main.transform.position;
            lookDir.y = 0; 
            if (lookDir != Vector3.zero) transform.rotation = Quaternion.LookRotation(lookDir);
        }
        else
        {
            // Float in front of you if hitting nothing
            transform.position = ray.GetPoint(1.5f);
        }
    }

    private async void LoadSavedPosition()
    {
        if (!PlayerPrefs.HasKey(uniqueSaveID)) return;

        string guidString = PlayerPrefs.GetString(uniqueSaveID);
        
        if (Guid.TryParse(guidString, out Guid sysGuid))
        {
            var serializableGuid = new SerializableGuid(
                BitConverter.ToUInt64(sysGuid.ToByteArray(), 0), 
                BitConverter.ToUInt64(sysGuid.ToByteArray(), 8)
            );

            var result = await anchorManager.TryLoadAnchorAsync(serializableGuid);

            if (result.status.IsSuccess())
            {
                _currentAnchor = result.value;
                transform.SetParent(_currentAnchor.transform);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
        }
    }
}