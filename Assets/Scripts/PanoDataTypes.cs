using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class PanoLayerNode
{
    public string name;
    public string id;
    public PanoLayerNode parent;
    public bool isVisible = true;
    public bool expanded = true;
    public float opacity = 1.0f;

    public PanoLayerNode(string name)
    {
        this.name = name;
        this.id = System.Guid.NewGuid().ToString();
    }
    public virtual void Cleanup() { }
}

[System.Serializable]
public class PanoGroupLayer : PanoLayerNode
{
    public List<PanoLayerNode> children = new List<PanoLayerNode>();
    public PanoGroupLayer(string name) : base(name) { }

    public override void Cleanup()
    {
        foreach (var child in children) child.Cleanup();
    }
}

[System.Serializable]
public class PanoPaintLayer : PanoLayerNode
{
    public RenderTexture texture;
    public List<RenderTexture> undoHistory = new List<RenderTexture>();
    public List<RenderTexture> redoHistory = new List<RenderTexture>();

    private int width, height;
    private int maxHistory = 30;

    public PanoPaintLayer(string name, int w, int h) : base(name)
    {
        this.width = w;
        this.height = h;

        // Optimization: Reduce history for large textures
        if (width > 2048) maxHistory = 20;

        // LAZY ALLOCATION: Do NOT create texture here
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

    public override void Cleanup()
    {
        if (texture != null) texture.Release();
        foreach (var rt in undoHistory) if (rt) rt.Release();
        foreach (var rt in redoHistory) if (rt) rt.Release();
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
            if (old != null) old.Release();
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
    }
}

[System.Serializable]
public class PanoAnimationLayer : PanoGroupLayer
{
    public Dictionary<int, int> timelineMap = new Dictionary<int, int>();

    // OPTIMIZATION: Cache sorted keys for fast playback lookup
    private List<int> _cachedSortedKeys = new List<int>();
    private bool _dirtyKeys = true;

    public PanoAnimationLayer(string name) : base(name) { }

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
        // We need the last key that is <= frame
        int count = _cachedSortedKeys.Count;
        if (count == 0) return -1;
        if (_cachedSortedKeys[0] > frame) return -1; // Frame is before first drawing

        // Linear scan backward is fast enough for small N, but binary is better for long anims
        // For simplicity and speed in C#, a reverse loop on cached keys is very fast
        for (int i = count - 1; i >= 0; i--)
        {
            if (_cachedSortedKeys[i] < frame) return timelineMap[_cachedSortedKeys[i]];
        }

        return -1;
    }
}

[System.Serializable]
public class PanoCameraLayer : PanoLayerNode
{
    public AnimationCurve curvePitch = new AnimationCurve();
    public AnimationCurve curveYaw = new AnimationCurve();
    public AnimationCurve curveRoll = new AnimationCurve();
    public AnimationCurve curveZoom = new AnimationCurve();
    public AnimationCurve curveFisheye = new AnimationCurve();

    public PanoCameraLayer(string name) : base(name) { }

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