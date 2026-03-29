using UnityEngine;

/// <summary>
/// Memory Fail-Safe System
/// Monitors RAM usage and takes action when threshold is exceeded:
/// 1. Incrementally clears undo/redo history
/// 2. Triggers auto-save
/// 3. Signals UI to show RAM text in red
/// </summary>
public class MemoryFailSafe : MonoBehaviour
{
    public static MemoryFailSafe Instance { get; private set; }

    [Header("Cleanup Strategy")]
    [Range(1, 20)] public int historyEntriesPerFrame = 2; // How many undo/redo entries to clear per frame when over threshold
    [Range(0.1f, 5f)] public float checkIntervalSeconds = 1f; // How often to check RAM
    [Range(5f, 95f)] public float thresholdPercent = 80f; // The Fail-Safe Threshold!

    private PanoramaLayerManager layerManager;
    private PanoramaProjectIO projectIO;
    private MemoryTracker memoryTracker;

    private bool isAboveThreshold = false;
    private float checkTimer = 0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        layerManager = GetComponent<PanoramaLayerManager>();
        projectIO = GetComponent<PanoramaProjectIO>();
        memoryTracker = MemoryTracker.Instance;

        if (memoryTracker == null)
        {
            Debug.LogError("MemoryFailSafe: MemoryTracker not found in scene!");
        }
    }

    void Update()
    {
        if (memoryTracker == null) return;

        checkTimer -= Time.deltaTime;
        if (checkTimer <= 0)
        {
            checkTimer = checkIntervalSeconds;
            CheckMemoryAndRespond();
        }
    }


    /// <summary>
    /// Public method for UI to check if RAM is above threshold and should be displayed in red
    /// </summary>
    public bool ShouldShowRamAsWarning()
    {
        return isAboveThreshold;
    }

    /// <summary>
    /// Get current RAM usage percentage (for UI display)
    /// </summary>
    public float GetCurrentRamPercent()
    {
        return memoryTracker != null ? memoryTracker.RamUsagePercent : 0f;
    }

    /// <summary>
    /// Check memory and respond with cleanup actions if threshold exceeded
    /// </summary>
    private void CheckMemoryAndRespond()
    {
        if (memoryTracker == null || layerManager == null || projectIO == null) return;

        float currentRamPercent = memoryTracker.RamUsagePercent;
        bool isCurrentlyAboveThreshold = currentRamPercent >= thresholdPercent;

        // 1. CROSSING THRESHOLD FROM BELOW (Entering danger zone)
        if (isCurrentlyAboveThreshold && !isAboveThreshold)
        {
            isAboveThreshold = true;
            Debug.LogWarning($"[MemoryFailSafe] RAM threshold exceeded: {currentRamPercent:F1}% >= {thresholdPercent:F1}%");
            Debug.LogWarning("[MemoryFailSafe] Starting auto-save and history cleanup!");
            
            // Trigger immediate auto-save
            projectIO.TriggerAutoSave();
        }

        // 2. STAYING ABOVE THRESHOLD (Clean up history incrementally)
        if (isAboveThreshold && isCurrentlyAboveThreshold)
        {
            // Clear undo/redo history incrementally
            for (int i = 0; i < historyEntriesPerFrame; i++)
            {
                // Prioritize clearing undo (older actions) over redo
                if (layerManager.GetUndoHistoryCount() > 0)
                {
                    layerManager.ClearOldestUndoState();
                    Debug.Log($"[MemoryFailSafe] Cleared undo state. Remaining: {layerManager.GetUndoHistoryCount()}");
                }
                else if (layerManager.GetRedoHistoryCount() > 0)
                {
                    layerManager.ClearOldestRedoState();
                    Debug.Log($"[MemoryFailSafe] Cleared redo state. Remaining: {layerManager.GetRedoHistoryCount()}");
                }
                else
                {
                    break; // Nothing left to clear
                }
            }

            // Re-check memory after cleanup
            memoryTracker.ForceRefresh();
        }

        // 3. CROSSING THRESHOLD FROM ABOVE (Recovering)
        if (!isCurrentlyAboveThreshold && isAboveThreshold)
        {
            isAboveThreshold = false;
            Debug.Log($"[MemoryFailSafe] RAM recovered below threshold: {currentRamPercent:F1}% < {thresholdPercent:F1}%");
            Debug.Log("[MemoryFailSafe] Cleanup suspended.");
        }
    }
}
