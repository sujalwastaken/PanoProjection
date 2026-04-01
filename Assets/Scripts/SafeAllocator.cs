using UnityEngine;

/// <summary>
/// Global Factory for memory-heavy objects. 
/// Every script in the project must request textures from here.
/// </summary>
public static class SafeAllocator
{
    // Quick check for UI scripts to see if they should even try to allocate
    public static bool CanAllocate()
    {
        if (MemoryFailSafe.Instance != null)
        {
            return MemoryFailSafe.Instance.IsSafeToAllocate();
        }
        return true;
    }

    public static RenderTexture RequestRenderTexture(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGB32)
    {
        if (!CanAllocate()) return null;

        RenderTexture rt = new RenderTexture(width, height, 0, format);
        ApplyCrispSettings(rt);
        rt.Create();
        return rt;
    }

    // Overload for the Undo/Redo system which uses descriptors
    public static RenderTexture RequestRenderTexture(RenderTextureDescriptor desc)
    {
        if (!CanAllocate()) return null;

        // Force descriptor to not use mipmaps before creation
        desc.useMipMap = false;
        desc.autoGenerateMips = false;

        RenderTexture rt = new RenderTexture(desc);
        ApplyCrispSettings(rt);
        rt.Create();
        return rt;
    }

    // Centralized settings so you never have to type these out again!
    private static void ApplyCrispSettings(RenderTexture rt)
    {
        rt.enableRandomWrite = true;
        rt.useMipMap = false;
        rt.autoGenerateMips = false;
        rt.filterMode = FilterMode.Point; // Change to Bilinear if you ever want smooth brushes
        rt.wrapMode = TextureWrapMode.Clamp;
    }
}