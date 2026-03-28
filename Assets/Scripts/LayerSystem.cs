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
    private int width, height;

    public PaintLayer(string name, int width, int height) : base(name)
    {
        this.width = width;
        this.height = height;
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
        if (texture != null)
        {
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
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