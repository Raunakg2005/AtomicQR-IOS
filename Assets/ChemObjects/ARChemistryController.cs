using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARChemistryController : MonoBehaviour
{
    [Header("AR Session Origin")]
    [Tooltip("Assign your ARSessionOrigin GameObject containing ARTrackedImageManager")] 
    [SerializeField] private GameObject xrOrigin;

    [Header("Element Prefabs")]
    [Tooltip("Prefab for each element; index matches elementIndex in mappings")]
    public GameObject[] elementPrefabs = new GameObject[118];

    [Header("Tracked Image Mapping")]
    [Tooltip("Map each reference image name to an element prefab index")]  
    public TrackedImageElementMapping[] trackedImageMappings = new TrackedImageElementMapping[118];

    [Header("Dynamic Library Management")]
    [Tooltip("Multiple smaller reference image libraries (max 20-25 images each)")]
    public XRReferenceImageLibrary[] referenceLibraries = new XRReferenceImageLibrary[6];
    
    [Tooltip("Time between library switches when no images are detected (seconds)")]
    public float librarySwitchInterval = 3f;
    
    [Header("Global Transform")]
    [Tooltip("Scale applied to all spawned elements")]
    public Vector3 elementGlobalScale = Vector3.one;
    [Tooltip("Rotation applied to all spawned elements")]
    public Vector3 elementGlobalRotation = Vector3.zero;

    [Header("Spawn Settings")]
    [Tooltip("Vertical offset from image plane (meters)")]
    public float elementOffsetDistance = 0.1f;
    [Tooltip("Allow multiple instances of same element")]  
    public bool allowMultipleInstances = true;

    [Header("Performance")]
    [Tooltip("Updates per second for repositioning spawned elements")]
    public float positionUpdateFrequency = 30f;

    [System.Serializable]
    public class TrackedImageElementMapping
    {
        public string trackedImageName;
        public int elementIndex;
        public int libraryIndex; // New field to specify which library contains this image
    }

    private ARTrackedImageManager trackedImageManager;
    private Dictionary<string, int> imageToElement = new Dictionary<string, int>();
    private Dictionary<string, List<ElementInstance>> activeElements = new Dictionary<string, List<ElementInstance>>();
    private int currentLibraryIndex = 0;
    private float lastLibrarySwitchTime = 0f;
    private bool isTrackingAnyImage = false;

    private class ElementInstance
    {
        public GameObject obj;
        public ARTrackedImage image;
    }

    void Start()
    {
        // Find or assign XR Origin
        if (xrOrigin == null)
        {
            var found = FindObjectOfType<ARSessionOrigin>();
            if (found != null)
                xrOrigin = found.gameObject;
        }

        if (xrOrigin == null)
        {
            Debug.LogError("ARChemistryController: No ARSessionOrigin assigned or found.");
            enabled = false;
            return;
        }

        trackedImageManager = xrOrigin.GetComponentInChildren<ARTrackedImageManager>();
        if (trackedImageManager == null)
        {
            Debug.LogError("ARChemistryController: No ARTrackedImageManager found under XR Origin.");
            enabled = false;
            return;
        }

        // Validate libraries
        if (!ValidateLibraries())
        {
            enabled = false;
            return;
        }

        // Set up mapping and events
        BuildImageMapping();
        trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
        
        // Start with first library
        SwitchToLibrary(0);
        
        // Validate prefabs
        ValidatePrefabs();

        // Start update loops
        StartCoroutine(UpdatePositionsLoop());
        StartCoroutine(LibrarySwitchLoop());

        Debug.Log("ARChemistryController initialized with dynamic library switching.");
    }

    void OnDestroy()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    private bool ValidateLibraries()
    {
        int validLibraries = 0;
        for (int i = 0; i < referenceLibraries.Length; i++)
        {
            if (referenceLibraries[i] != null)
                validLibraries++;
        }
        
        if (validLibraries == 0)
        {
            Debug.LogError("ARChemistryController: No reference image libraries assigned.");
            return false;
        }
        
        Debug.Log($"ARChemistryController: {validLibraries} reference libraries validated.");
        return true;
    }

    private void BuildImageMapping()
    {
        imageToElement.Clear();
        foreach (var map in trackedImageMappings)
        {
            if (!string.IsNullOrEmpty(map.trackedImageName))
                imageToElement[map.trackedImageName] = map.elementIndex;
        }
    }

    private void SwitchToLibrary(int libraryIndex)
    {
        if (libraryIndex >= referenceLibraries.Length || referenceLibraries[libraryIndex] == null)
            return;

        currentLibraryIndex = libraryIndex;
        trackedImageManager.enabled = false;
        trackedImageManager.referenceLibrary = referenceLibraries[libraryIndex];
        trackedImageManager.enabled = true;
        lastLibrarySwitchTime = Time.time;
        
        Debug.Log($"Switched to library {libraryIndex} with {referenceLibraries[libraryIndex].count} images");
    }

    private IEnumerator LibrarySwitchLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(0.5f);
            
            // Only switch libraries if no images are currently being tracked
            if (!isTrackingAnyImage && Time.time - lastLibrarySwitchTime > librarySwitchInterval)
            {
                int nextLibrary = (currentLibraryIndex + 1) % referenceLibraries.Length;
                // Skip null libraries
                while (referenceLibraries[nextLibrary] == null && nextLibrary != currentLibraryIndex)
                {
                    nextLibrary = (nextLibrary + 1) % referenceLibraries.Length;
                }
                
                if (nextLibrary != currentLibraryIndex)
                {
                    SwitchToLibrary(nextLibrary);
                }
            }
        }
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs args)
    {
        isTrackingAnyImage = false;
        
        foreach (var added in args.added) ProcessImage(added);
        foreach (var updated in args.updated) ProcessImage(updated);
        foreach (var removed in args.removed) RemoveImage(removed);
        
        // Check if any images are currently being tracked
        foreach (var trackedImage in trackedImageManager.trackables)
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                isTrackingAnyImage = true;
                break;
            }
        }
    }

private void ProcessImage(ARTrackedImage trackedImage)
{
    string name = trackedImage.referenceImage.name;
    if (!imageToElement.ContainsKey(name))
    {
        Debug.LogWarning($"Unmapped image: {name}");
        return;
    }

    int idx = imageToElement[name];
    if (idx < 0 || idx >= elementPrefabs.Length || elementPrefabs[idx] == null)
        return;

    string key = allowMultipleInstances
        ? $"{name}_{trackedImage.trackableId}"
        : name;

    // Create if not exists
    if (!activeElements.ContainsKey(key) || activeElements[key].Count == 0)
    {
        // Instantiate
        Vector3 pos = trackedImage.transform.position + Vector3.up * elementOffsetDistance;
        Quaternion rot = trackedImage.transform.rotation * Quaternion.Euler(elementGlobalRotation);
        GameObject go = Instantiate(elementPrefabs[idx], pos, rot);
        go.transform.localScale = Vector3.Scale(elementPrefabs[idx].transform.localScale, elementGlobalScale);

        var inst = new ElementInstance { obj = go, image = trackedImage };
        if (!activeElements.ContainsKey(key))
            activeElements[key] = new List<ElementInstance>();
        activeElements[key].Add(inst);
    }

    // Show or hide based on tracking state
    foreach (var inst in activeElements[key])
    {
        if (inst.obj == null) continue;
        bool shouldShow = trackedImage.trackingState == TrackingState.Tracking;
        if (inst.obj.activeSelf != shouldShow)
            inst.obj.SetActive(shouldShow);
    }
}


    private void AddOrUpdateElement(ARTrackedImage trackedImage, string name)
    {
        int idx = imageToElement[name];
        if (idx < 0 || idx >= elementPrefabs.Length || elementPrefabs[idx] == null)
        {
            Debug.LogError($"Prefab missing or invalid index for element '{name}' ({idx}).");
            return;
        }

        string key = allowMultipleInstances
            ? $"{name}_{trackedImage.trackableId}"
            : name;

        // Remove existing if single-instance
        if (!allowMultipleInstances) RemoveElement(name);

        // Create if not exists
        if (!activeElements.ContainsKey(key))
            activeElements[key] = new List<ElementInstance>();

        // Check if element already exists to avoid duplicates
        bool elementExists = false;
        foreach (var existingInst in activeElements[key])
        {
            if (existingInst.image.trackableId == trackedImage.trackableId)
            {
                elementExists = true;
                break;
            }
        }

        if (!elementExists)
        {
            // Instantiate
            Vector3 pos = trackedImage.transform.position + Vector3.up * elementOffsetDistance;
            Quaternion rot = trackedImage.transform.rotation * Quaternion.Euler(elementGlobalRotation);
            GameObject go = Instantiate(elementPrefabs[idx], pos, rot);
            go.transform.localScale = Vector3.Scale(elementPrefabs[idx].transform.localScale, elementGlobalScale);

            var inst = new ElementInstance { obj = go, image = trackedImage };
            activeElements[key].Add(inst);
        }
    }

    private void RemoveElement(string name)
    {
        var toRemove = new List<string>();
        foreach (var kv in activeElements)
        {
            if (kv.Key.StartsWith(name))
            {
                foreach (var inst in kv.Value)
                    if (inst.obj != null) Destroy(inst.obj);
                toRemove.Add(kv.Key);
            }
        }
        foreach (var k in toRemove)
            activeElements.Remove(k);
    }

    private void RemoveImage(ARTrackedImage trackedImage)
    {
    string name = trackedImage.referenceImage.name;
    string key = allowMultipleInstances
        ? $"{name}_{trackedImage.trackableId}"
        : name;

    if (activeElements.ContainsKey(key))
    {
        foreach (var inst in activeElements[key])
            if (inst.obj != null) Destroy(inst.obj);
        activeElements.Remove(key);
    }
    }


    IEnumerator UpdatePositionsLoop()
{
    float wait = 1f / positionUpdateFrequency;
    while (true)
    {
        yield return new WaitForSeconds(wait);
        foreach (var kv in activeElements)
        {
            foreach (var inst in kv.Value)
            {
                if (inst.image == null || inst.obj == null)
                    continue;

                // Only update position if tracking
                if (inst.image.trackingState == TrackingState.Tracking && inst.obj.activeSelf)
                {
                    inst.obj.transform.position = inst.image.transform.position + Vector3.up * elementOffsetDistance;
                    inst.obj.transform.rotation = inst.image.transform.rotation * Quaternion.Euler(elementGlobalRotation);
                }
            }
        }
    }
}

    private void ValidatePrefabs()
    {
        int missing = 0;
        for (int i = 0; i < elementPrefabs.Length; i++)
            if (elementPrefabs[i] == null) missing++;
        if (missing > 0)
            Debug.LogWarning($"ARChemistryController: {missing} element prefabs not assigned.");
        else
            Debug.Log("ARChemistryController: All element prefabs assigned.");
    }
}
