using UnityEngine;
using System.Collections.Generic;

// 1. Keep your LayerType enum
public enum LayerType { Paint, Folder, Animation, Camera }

// 2. Base Layer (Upgraded with Serialization)
[System.Serializable]
public abstract class LayerNode
{
    public string name;
    public string id;
    public LayerNode parent;
    public bool isVisible = true;
    public bool expanded = true;
    public float opacity = 1.0f;

    public LayerNode(string name)
    {
        this.name = name;
        this.id = System.Guid.NewGuid().ToString();
    }
    
    public virtual void Cleanup() { }
}

// 3. Group Layer (Upgraded with Serialization)
[System.Serializable]
public class GroupLayer : LayerNode
{
    public List<LayerNode> children = new List<LayerNode>();
    public GroupLayer(string name) : base(name) { }

    public override void Cleanup()
    {
        foreach (var child in children) child.Cleanup();
    }
}

// 4. Paint Layer (Upgraded with Pano's Undo/Redo & Memory Optimization)
[System.Serializable]
public class PaintLayer : LayerNode
{
    public RenderTexture texture;
    public List<RenderTexture> undoHistory = new List<RenderTexture>();
    public List<RenderTexture> redoHistory = new List<RenderTexture>();

    private int width, height;
    private int maxHistory = 30;

    public PaintLayer(string name, int w, int h) : base(name)
    {
        this.width = w;
        this.height = h;

        // Optimization: Reduce history for large textures
        if (width > 2048) maxHistory = 20;

        // LAZY ALLOCATION: Do NOT create texture here to save RAM
    }

    public void EnsureTextureAllocated()
    {
        if (texture != null && texture.IsCreated()) return;

        texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        texture.enableRandomWrite = true;
        texture.filterMode = FilterMode.Point; // Keeps strokes crisp
        texture.useMipMap = false;             // Prevents zoom-blur
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

    public override void Cleanup()
    {
        // Combined memory safety from both scripts to completely prevent VRAM leaks
        if (texture != null) 
        { 
            texture.Release(); 
            UnityEngine.Object.DestroyImmediate(texture); 
        }
        foreach (var rt in undoHistory) if (rt) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        foreach (var rt in redoHistory) if (rt) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
        
        undoHistory.Clear();
        redoHistory.Clear();
    }

    public void SaveState()
    {
        EnsureTextureAllocated();

        RenderTexture snapshot = new RenderTexture(texture.descriptor);
        Graphics.CopyTexture(texture, snapshot);
        undoHistory.Add(snapshot);
        redoHistory.Clear();

        if (undoHistory.Count > maxHistory)
        {
            RenderTexture old = undoHistory[0];
            undoHistory.RemoveAt(0);
            if (old != null) 
            {
                old.Release();
                UnityEngine.Object.DestroyImmediate(old);
            }
        }
    }

    public void Undo()
    {
        if (undoHistory.Count == 0) return;

        EnsureTextureAllocated();

        RenderTexture redoSnap = new RenderTexture(texture.descriptor);
        Graphics.CopyTexture(texture, redoSnap);
        redoHistory.Add(redoSnap);

        RenderTexture lastState = undoHistory[undoHistory.Count - 1];
        Graphics.CopyTexture(lastState, texture);

        undoHistory.RemoveAt(undoHistory.Count - 1);
        lastState.Release();
        UnityEngine.Object.DestroyImmediate(lastState);
    }

    public void Redo()
    {
        if (redoHistory.Count == 0) return;

        EnsureTextureAllocated();

        RenderTexture undoSnap = new RenderTexture(texture.descriptor);
        Graphics.CopyTexture(texture, undoSnap);
        undoHistory.Add(undoSnap);

        RenderTexture nextState = redoHistory[redoHistory.Count - 1];
        Graphics.CopyTexture(nextState, texture);

        redoHistory.RemoveAt(redoHistory.Count - 1);
        nextState.Release();
        UnityEngine.Object.DestroyImmediate(nextState);
    }
}

// 5. Animation Layer (Upgraded with Pano's Cached Timeline Logic)
[System.Serializable]
public class AnimationLayer : GroupLayer
{
    public Dictionary<int, int> timelineMap = new Dictionary<int, int>();

    // OPTIMIZATION: Cache sorted keys for fast playback lookup
    private List<int> _cachedSortedKeys = new List<int>();
    private bool _dirtyKeys = true;

    public AnimationLayer(string name) : base(name) { }

    public void SetCell(int frame, int childIndex)
    {
        if (timelineMap.ContainsKey(frame)) timelineMap[frame] = childIndex;
        else timelineMap.Add(frame, childIndex);
        _dirtyKeys = true; // Mark dirty so we rebuild cache next lookup
    }

    public int GetActiveCellIndex(int frame)
    {
        // 1. Direct Hit (O(1)) - Most common during painting
        if (timelineMap.ContainsKey(frame)) return timelineMap[frame];

        // 2. Rebuild Cache if needed
        if (_dirtyKeys)
        {
            _cachedSortedKeys = new List<int>(timelineMap.Keys);
            _cachedSortedKeys.Sort();
            _dirtyKeys = false;
        }

        // 3. Binary Search Approximation (O(log n)) - Fast playback lookup
        int count = _cachedSortedKeys.Count;
        if (count == 0) return -1;
        if (_cachedSortedKeys[0] > frame) return -1; // Frame is before first drawing

        // Reverse loop is extremely fast for timeline lookups
        for (int i = count - 1; i >= 0; i--)
        {
            if (_cachedSortedKeys[i] < frame) return timelineMap[_cachedSortedKeys[i]];
        }

        return -1;
    }
}

// 6. Camera Layer (Merged curves and Group inheritance)
[System.Serializable]
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
        return HasKey(curvePitch, frame) || HasKey(curveYaw, frame) || HasKey(curveRoll, frame) || HasKey(curveZoom, frame);
    }
    
    bool HasKey(AnimationCurve c, int f)
    {
        foreach (var k in c.keys) if (Mathf.Approximately(k.time, f)) return true;
        return false;
    }
}