using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Rendering;
using System.Threading.Tasks;

public enum LayerType { Paint, Folder, Animation, Camera }

[System.Serializable]
public abstract class LayerNode
{
    public string name;
    public string id;
    public bool isVisible = true;
    public float opacity = 1.0f;
    public LayerNode parent;
    public bool expanded = true;

    public LayerNode(string name)
    {
        this.name = name;
        this.id = System.Guid.NewGuid().ToString();
    }
    public abstract void Cleanup();
}

public class PaintLayer : LayerNode
{
    public class PagedState
    {
        public RenderTexture rt;
        public string diskPath;
        public bool isWriting;
    }

    public RenderTexture texture;
    public List<PagedState> undoHistory = new List<PagedState>();
    public List<PagedState> redoHistory = new List<PagedState>();

    private int width, height;
    private int maxRAMStates = 3;   
    private int maxDiskStates = 30; 
    private string cacheDir;
    
    public bool isRestoring = false; 

    // --- THE RAM FIX: A single, reusable upload buffer to prevent Garbage Collection freezes ---
    private Texture2D stagingTexture; 

    public PaintLayer(string name, int width, int height) : base(name)
    {
        this.width = width;
        this.height = height;

        cacheDir = Path.Combine(Application.temporaryCachePath, "PanoramaHistory", id);
        if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

        if (width >= 4096 || height >= 4096) maxDiskStates = 15; 
    }

    private void DestroyRT(RenderTexture rt)
    {
        if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
    }

    public void EnsureTextureAllocated()
    {
        if (texture != null && texture.IsCreated()) return;
        texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        texture.enableRandomWrite = true;
        texture.Create();
        ClearTexture();
    }

    public void ClearTexture()
    {
        if (texture == null) return;
        RenderTexture.active = texture;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;
    }

    public void SaveState()
    {
        if (isRestoring) return; 
        EnsureTextureAllocated();
        ClearRedoHistory();

        PagedState newState = new PagedState();
        newState.rt = new RenderTexture(texture.descriptor);
        Graphics.CopyTexture(texture, newState.rt);
        newState.diskPath = Path.Combine(cacheDir, System.Guid.NewGuid().ToString() + ".raw");
        newState.isWriting = true;

        undoHistory.Add(newState);

        AsyncGPUReadback.Request(newState.rt, 0, TextureFormat.RGBA32, (request) =>
        {
            if (request.hasError) { newState.isWriting = false; return; }
            byte[] rawBytes = request.GetData<byte>().ToArray();
            Task.Run(() => {
                File.WriteAllBytes(newState.diskPath, rawBytes);
                newState.isWriting = false; 
            });
        });

        EnforceMemoryLimits();
    }

    private void EnforceMemoryLimits()
    {
        if (undoHistory.Count > maxDiskStates)
        {
            PagedState oldest = undoHistory[0];
            undoHistory.RemoveAt(0);
            DestroyRT(oldest.rt);
            if (File.Exists(oldest.diskPath)) File.Delete(oldest.diskPath);
        }

        int ramCount = 0;
        for (int i = undoHistory.Count - 1; i >= 0; i--)
        {
            if (undoHistory[i].rt != null)
            {
                ramCount++;
                if (ramCount > maxRAMStates && !undoHistory[i].isWriting)
                {
                    DestroyRT(undoHistory[i].rt);
                    undoHistory[i].rt = null; 
                }
            }
        }
    }

    private async Task RestoreStateAsync(PagedState stateToRestore, List<PagedState> targetList)
    {
        PagedState saveCurrent = new PagedState();
        saveCurrent.rt = new RenderTexture(texture.descriptor);
        Graphics.CopyTexture(texture, saveCurrent.rt);
        saveCurrent.diskPath = Path.Combine(cacheDir, System.Guid.NewGuid().ToString() + ".raw");
        saveCurrent.isWriting = true;
        targetList.Add(saveCurrent);

        AsyncGPUReadback.Request(saveCurrent.rt, 0, TextureFormat.RGBA32, (req) => {
            if (!req.hasError) {
                byte[] bytes = req.GetData<byte>().ToArray();
                Task.Run(() => { File.WriteAllBytes(saveCurrent.diskPath, bytes); saveCurrent.isWriting = false; });
            }
        });

        if (stateToRestore.rt != null)
        {
            Graphics.CopyTexture(stateToRestore.rt, texture);
        }
        else if (File.Exists(stateToRestore.diskPath))
        {
            // 1. BACKGROUND LOAD: Pull from SSD asynchronously
            byte[] diskBytes = await Task.Run(() => File.ReadAllBytes(stateToRestore.diskPath));

            // 2. STAGING BUFFER: Reuse the same Texture2D to stop Allocation & Garbage Collection freezes!
            if (stagingTexture == null || stagingTexture.width != width || stagingTexture.height != height)
            {
                if (stagingTexture != null) UnityEngine.Object.DestroyImmediate(stagingTexture);
                stagingTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                stagingTexture.hideFlags = HideFlags.HideAndDontSave; // Keep it clean from the editor
            }

            // 3. ZERO-ALLOCATION UPLOAD
            stagingTexture.LoadRawTextureData(diskBytes);
            stagingTexture.Apply(false, false); 
            Graphics.Blit(stagingTexture, texture);
        }

        DestroyRT(stateToRestore.rt);
        if (File.Exists(stateToRestore.diskPath)) File.Delete(stateToRestore.diskPath);
    }

    public async void Undo(System.Action onComplete = null)
    {
        if (undoHistory.Count == 0 || isRestoring) return;
        EnsureTextureAllocated();
        
        isRestoring = true;
        PagedState state = undoHistory[undoHistory.Count - 1];
        undoHistory.RemoveAt(undoHistory.Count - 1);
        
        await RestoreStateAsync(state, redoHistory);
        
        EnforceMemoryLimits();
        isRestoring = false;
        onComplete?.Invoke(); 
    }

    public async void Redo(System.Action onComplete = null)
    {
        if (redoHistory.Count == 0 || isRestoring) return;
        EnsureTextureAllocated();
        
        isRestoring = true;
        PagedState state = redoHistory[redoHistory.Count - 1];
        redoHistory.RemoveAt(redoHistory.Count - 1);
        
        await RestoreStateAsync(state, undoHistory);
        
        EnforceMemoryLimits();
        isRestoring = false;
        onComplete?.Invoke();
    }

    private void ClearRedoHistory()
    {
        foreach (var s in redoHistory) { DestroyRT(s.rt); if (File.Exists(s.diskPath)) File.Delete(s.diskPath); }
        redoHistory.Clear();
    }

    public void HibernateRAM()
    {
        if (isRestoring) return;
        foreach (var s in undoHistory) { if (!s.isWriting) { DestroyRT(s.rt); s.rt = null; } }
        foreach (var s in redoHistory) { if (!s.isWriting) { DestroyRT(s.rt); s.rt = null; } }
    }

    public override void Cleanup()
    {
        DestroyRT(texture);
        ClearRedoHistory();
        foreach (var s in undoHistory) { DestroyRT(s.rt); if (File.Exists(s.diskPath)) File.Delete(s.diskPath); }
        undoHistory.Clear();
        
        // Destroy the staging buffer
        if (stagingTexture != null) UnityEngine.Object.DestroyImmediate(stagingTexture);
        
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
    }
}

public class GroupLayer : LayerNode
{
    public List<LayerNode> children = new List<LayerNode>();
    public GroupLayer(string name) : base(name) { }
    public override void Cleanup() { foreach (var child in children) child.Cleanup(); }
}

public class AnimationLayer : GroupLayer
{
    public Dictionary<int, int> timelineMap = new Dictionary<int, int>();
    public AnimationLayer(string name) : base(name) { }

    public void SetCell(int frame, int childIndex)
    {
        if (timelineMap.ContainsKey(frame)) timelineMap[frame] = childIndex;
        else timelineMap.Add(frame, childIndex);
    }

    public int GetActiveCellIndex(int currentFrame)
    {
        for (int f = currentFrame; f >= 0; f--)
        {
            if (timelineMap.ContainsKey(f)) return timelineMap[f];
        }
        return -1;
    }
}

public class CameraLayer : GroupLayer
{
    public AnimationCurve curvePitch = new AnimationCurve();
    public AnimationCurve curveYaw = new AnimationCurve();
    public AnimationCurve curveRoll = new AnimationCurve();
    public AnimationCurve curveZoom = new AnimationCurve();
    public AnimationCurve curveFisheye = new AnimationCurve();

    public CameraLayer(string name) : base(name) { }

    public bool HasKeyframe(int frame)
    {
        foreach (var k in curveYaw.keys) if (Mathf.Approximately(k.time, frame)) return true;
        foreach (var k in curveZoom.keys) if (Mathf.Approximately(k.time, frame)) return true;
        return false;
    }
}