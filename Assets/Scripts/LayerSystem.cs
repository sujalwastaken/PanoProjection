using UnityEngine;
using System.Collections.Generic;

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
    public RenderTexture texture;
    public List<RenderTexture> undoHistory = new List<RenderTexture>();
    public List<RenderTexture> redoHistory = new List<RenderTexture>();

    private int maxHistory = 30;
    private int width, height; // Store dimensions for later

    public PaintLayer(string name, int width, int height) : base(name)
    {
        this.width = width;
        this.height = height;

        // MEMORY FIX: Reduce history size for 4K/8K textures to prevent crash
        if (width > 2048 || height > 2048) maxHistory = 20;

        // CRITICAL OPTIMIZATION: Do NOT create texture here (Lazy Allocation).
        // It creates only when you actually paint or save.
    }

    // --- COMPILER FIX: Added this method ---
    public void EnsureTextureAllocated()
    {
        // If already exists, do nothing
        if (texture != null && texture.IsCreated()) return;

        // Create texture now
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
        EnsureTextureAllocated(); // Ensure memory exists before using

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

    public override void Cleanup()
    {
        if (texture != null) texture.Release();
        foreach (var rt in undoHistory) if (rt) rt.Release();
        foreach (var rt in redoHistory) if (rt) rt.Release();
        undoHistory.Clear(); redoHistory.Clear();
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