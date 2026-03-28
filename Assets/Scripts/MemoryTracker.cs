using UnityEngine;
using System.Runtime.InteropServices;

public class MemoryTracker : MonoBehaviour
{
    public static MemoryTracker Instance { get; private set; }

    [Header("Settings")]
    public int updateEveryNFrames = 60;

    public float WorkingSetMB { get; private set; }
    public float PrivateMemoryMB { get; private set; }

    [DllImport("psapi.dll")]
    private static extern bool GetProcessMemoryInfo(System.IntPtr hProcess, out PROCESS_MEMORY_COUNTERS counters, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS
    {
        public uint cb;
        public uint PageFaultCount;
        public ulong PeakWorkingSetSize;
        public ulong WorkingSetSize;
        public ulong QuotaPeakPagedPoolUsage;
        public ulong QuotaPagedPoolUsage;
        public ulong QuotaPeakNonPagedPoolUsage;
        public ulong QuotaNonPagedPoolUsage;
        public ulong PagefileUsage;
        public ulong PeakPagefileUsage;
    }

    private int frameCounter = 0;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (frameCounter++ % updateEveryNFrames == 0)
            Refresh();
    }

    private void Refresh()
    {
        try
        {
            PROCESS_MEMORY_COUNTERS pmc;
            pmc.cb = (uint)Marshal.SizeOf(typeof(PROCESS_MEMORY_COUNTERS));
            var handle = System.Diagnostics.Process.GetCurrentProcess().Handle;
            if (GetProcessMemoryInfo(handle, out pmc, pmc.cb))
            {
                WorkingSetMB = pmc.WorkingSetSize / 1048576f;
                PrivateMemoryMB = pmc.PagefileUsage / 1048576f;
            }
        }
        catch { }
    }
}