using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

[RequireComponent(typeof(PanoramaLayerManager))]
public class PanoramaProjectIO : MonoBehaviour
{
    private PanoramaLayerManager manager;

    // --- UI STATE ---
    private bool isProcessing = false;
    private string processMessage = "";

    void Start()
    {
        manager = GetComponent<PanoramaLayerManager>();
    }

    void OnGUI()
    {
        // 1. Handle Shortcuts (Only if not processing)
        if (!isProcessing)
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown && e.control && e.shift)
            {
                if (e.keyCode == KeyCode.S)
                {
                    string path = SimpleFileBrowser.SaveFile("Save Project", "MyProject", "panon");
                    if (!string.IsNullOrEmpty(path)) StartCoroutine(SaveProjectRoutine(path));
                    e.Use();
                }
                if (e.keyCode == KeyCode.D)
                {
                    string path = SimpleFileBrowser.OpenFile("Load Project", "panon");
                    if (!string.IsNullOrEmpty(path)) StartCoroutine(LoadProjectRoutine(path));
                    e.Use();
                }
            }
        }

        // 2. Draw Loading Indicator Overlay
        if (isProcessing)
        {
            // Create a dark background covering the whole screen
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", GUI.skin.box);

            // Centered Message Box
            float w = 300, h = 100;
            Rect rect = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);

            GUI.BeginGroup(rect, GUI.skin.window);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(new Rect(0, 0, w, h), processMessage, labelStyle);
            GUI.EndGroup();
        }
    }

    // ============================================================================================
    //  SAVE SYSTEM (COROUTINE)
    // ============================================================================================
    IEnumerator SaveProjectRoutine(string path)
    {
        isProcessing = true;
        processMessage = "SAVING PROJECT...";
        yield return null; // Wait 1 frame to let UI draw the "Saving..." box

        // Heavy work starts here (will freeze app briefly)
        try
        {
            ProjectData data = new ProjectData();
            data.fps = manager.fps;
            data.totalFrames = manager.totalFrames;
            data.exportWidth = manager.exportWidth;
            data.exportHeight = manager.exportHeight;

            List<LayerNode> allNodes = new List<LayerNode>();
            if (manager.root != null) TraverseTree(manager.root, allNodes);

            foreach (var node in allNodes)
            {
                LayerData lData = new LayerData();
                lData.id = node.id;
                lData.name = node.name;
                lData.isVisible = node.isVisible;
                lData.opacity = node.opacity;
                lData.expanded = node.expanded;
                lData.parentId = (node.parent != null) ? node.parent.id : "";

                if (node is PaintLayer pl)
                {
                    lData.type = "Paint";
                    if (pl.texture != null) { lData.width = pl.texture.width; lData.height = pl.texture.height; }
                    else { lData.width = 2048; lData.height = 1024; }
                }
                else if (node is CameraLayer cam)
                {
                    lData.type = "Camera";
                    lData.curvePitch = SerializerHelper.CurveToData(cam.curvePitch);
                    lData.curveYaw = SerializerHelper.CurveToData(cam.curveYaw);
                    lData.curveRoll = SerializerHelper.CurveToData(cam.curveRoll);
                    lData.curveZoom = SerializerHelper.CurveToData(cam.curveZoom);
                    lData.curveFisheye = SerializerHelper.CurveToData(cam.curveFisheye);
                }
                else if (node is AnimationLayer anim)
                {
                    lData.type = "Animation";
                    lData.animTimelineKeys = anim.timelineMap.Keys.ToList();
                    lData.animTimelineValues = anim.timelineMap.Values.ToList();
                }
                else if (node is GroupLayer) lData.type = "Group";

                data.layers.Add(lData);
            }

            string json = JsonUtility.ToJson(data, true);
            byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);

            using (FileStream fs = new FileStream(path, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                writer.Write("PANON_V1");
                writer.Write(jsonBytes.Length);
                writer.Write(jsonBytes);

                List<PaintLayer> paintLayers = new List<PaintLayer>();
                foreach (var n in allNodes) if (n is PaintLayer pl) paintLayers.Add(pl);

                writer.Write(paintLayers.Count);

                foreach (var pl in paintLayers)
                {
                    writer.Write(pl.id);

                    if (pl.texture == null)
                    {
                        writer.Write(0); // Empty Layer
                    }
                    else
                    {
                        Texture2D tex = new Texture2D(pl.texture.width, pl.texture.height, TextureFormat.RGBA32, false);
                        RenderTexture.active = pl.texture;
                        tex.ReadPixels(new Rect(0, 0, pl.texture.width, pl.texture.height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;

                        byte[] pngBytes = tex.EncodeToPNG();
                        Destroy(tex);

                        writer.Write(pngBytes.Length);
                        writer.Write(pngBytes);
                    }
                }
            }
            Debug.Log("Save Complete!");
        }
        catch (System.Exception e) { Debug.LogError("Save Failed: " + e.Message); }
        finally
        {
            isProcessing = false; // Turn off UI
        }
    }

    void TraverseTree(LayerNode node, List<LayerNode> list)
    {
        list.Add(node);
        if (node is GroupLayer group) foreach (var child in group.children) TraverseTree(child, list);
    }

    // ============================================================================================
    //  LOAD SYSTEM (COROUTINE)
    // ============================================================================================
    IEnumerator LoadProjectRoutine(string path)
    {
        if (!File.Exists(path)) yield break;

        isProcessing = true;
        processMessage = "LOADING PROJECT...";
        yield return null; // Wait 1 frame to allow UI repaint

        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Open))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                string header = reader.ReadString();
                if (header != "PANON_V1") { Debug.LogError("Invalid file format!"); isProcessing = false; yield break; }

                if (manager.root != null) manager.root.Cleanup();
                manager.root = null;
                manager.activeLayer = null;

                int jsonLen = reader.ReadInt32();
                byte[] jsonBytes = reader.ReadBytes(jsonLen);
                string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                ProjectData data = JsonUtility.FromJson<ProjectData>(json);

                manager.fps = data.fps;
                manager.totalFrames = data.totalFrames;
                manager.exportWidth = data.exportWidth;
                manager.exportHeight = data.exportHeight;

                Dictionary<string, LayerNode> idToNode = new Dictionary<string, LayerNode>();

                foreach (var lData in data.layers)
                {
                    LayerNode node = null;
                    if (lData.type == "Paint")
                    {
                        int w = (lData.width > 0) ? lData.width : 2048;
                        int h = (lData.height > 0) ? lData.height : 1024;
                        node = new PaintLayer(lData.name, w, h);
                    }
                    else if (lData.type == "Group") node = new GroupLayer(lData.name);
                    else if (lData.type == "Animation")
                    {
                        var al = new AnimationLayer(lData.name);
                        if (lData.animTimelineKeys != null)
                        {
                            for (int i = 0; i < lData.animTimelineKeys.Count; i++)
                                al.timelineMap[lData.animTimelineKeys[i]] = lData.animTimelineValues[i];
                        }
                        node = al;
                    }
                    else if (lData.type == "Camera")
                    {
                        var cl = new CameraLayer(lData.name);
                        cl.curvePitch = SerializerHelper.DataToCurve(lData.curvePitch);
                        cl.curveYaw = SerializerHelper.DataToCurve(lData.curveYaw);
                        cl.curveRoll = SerializerHelper.DataToCurve(lData.curveRoll);
                        cl.curveZoom = SerializerHelper.DataToCurve(lData.curveZoom);
                        cl.curveFisheye = SerializerHelper.DataToCurve(lData.curveFisheye);
                        node = cl;
                    }

                    if (node != null)
                    {
                        node.id = lData.id;
                        node.isVisible = lData.isVisible;
                        node.opacity = lData.opacity;
                        node.expanded = lData.expanded;
                        idToNode[lData.id] = node;
                    }
                }

                foreach (var lData in data.layers)
                {
                    if (!idToNode.ContainsKey(lData.id)) continue;
                    LayerNode node = idToNode[lData.id];

                    if (string.IsNullOrEmpty(lData.parentId))
                    {
                        if (node is GroupLayer gl && node.name == "Root") manager.root = gl;
                    }
                    else if (idToNode.ContainsKey(lData.parentId))
                    {
                        LayerNode parent = idToNode[lData.parentId];
                        if (parent is GroupLayer pg)
                        {
                            node.parent = pg;
                            pg.children.Add(node);
                        }
                    }
                }

                if (manager.root == null) manager.root = new GroupLayer("Root");

                int texCount = reader.ReadInt32();
                for (int i = 0; i < texCount; i++)
                {
                    string id = reader.ReadString();
                    int len = reader.ReadInt32();

                    if (len > 0)
                    {
                        byte[] pngBytes = reader.ReadBytes(len);

                        if (idToNode.ContainsKey(id) && idToNode[id] is PaintLayer pl)
                        {
                            Texture2D tex = new Texture2D(2, 2);
                            tex.LoadImage(pngBytes);

                            pl.EnsureTextureAllocated();

                            if (pl.texture.width != tex.width || pl.texture.height != tex.height)
                            {
                                pl.texture.Release();
                                pl.texture = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                                pl.texture.enableRandomWrite = true;
                                pl.texture.Create();
                            }

                            Graphics.Blit(tex, pl.texture);
                            pl.SaveState();
                            Destroy(tex);
                        }
                    }
                }
            }
            manager.activeLayer = manager.root;
            manager.compositionDirty = true;
            Debug.Log("Load Complete!");
        }
        catch (System.Exception e) { Debug.LogError("Load Failed: " + e.Message); }
        finally
        {
            isProcessing = false; // Turn off UI
        }
    }
}

// ============================================================================================
//  SERIALIZATION HELPERS (Do NOT modify)
// ============================================================================================

[System.Serializable]
public class ProjectData
{
    public int fps;
    public int totalFrames;
    public int exportWidth;
    public int exportHeight;
    public List<LayerData> layers = new List<LayerData>();
}

[System.Serializable]
public class LayerData
{
    public string id;
    public string name;
    public string type;
    public string parentId;
    public bool isVisible;
    public float opacity;
    public bool expanded;
    public int width;
    public int height;

    public List<int> animTimelineKeys;
    public List<int> animTimelineValues;
    public SimpleCurve curvePitch;
    public SimpleCurve curveYaw;
    public SimpleCurve curveRoll;
    public SimpleCurve curveZoom;
    public SimpleCurve curveFisheye;
}

[System.Serializable]
public class SimpleCurve
{
    public List<SimpleKey> keys = new List<SimpleKey>();
}

[System.Serializable]
public struct SimpleKey
{
    public float time, value, inTan, outTan;
    public int mode;
}

public static class SerializerHelper
{
    public static SimpleCurve CurveToData(AnimationCurve curve)
    {
        SimpleCurve sc = new SimpleCurve();
        if (curve == null) return sc;
        foreach (var k in curve.keys)
        {
            sc.keys.Add(new SimpleKey
            {
                time = k.time,
                value = k.value,
                inTan = k.inTangent,
                outTan = k.outTangent,
                mode = (int)k.weightedMode
            });
        }
        return sc;
    }

    public static AnimationCurve DataToCurve(SimpleCurve data)
    {
        if (data == null || data.keys == null) return new AnimationCurve();
        Keyframe[] keys = new Keyframe[data.keys.Count];
        for (int i = 0; i < data.keys.Count; i++)
        {
            var k = data.keys[i];
            keys[i] = new Keyframe(k.time, k.value, k.inTan, k.outTan);
            keys[i].weightedMode = (WeightedMode)k.mode;
        }
        return new AnimationCurve(keys);
    }
}