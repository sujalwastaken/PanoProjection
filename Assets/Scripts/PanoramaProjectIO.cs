using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.Rendering;
using System.Threading.Tasks;

[RequireComponent(typeof(PanoramaLayerManager))]
public class PanoramaProjectIO : MonoBehaviour
{
    private PanoramaLayerManager manager;

    // --- UI & STATE ---
    private bool isProcessing = false;
    private string processMessageBase = "";
    private string currentProjectPath = ""; 
    
    [Header("Auto-Save")]
    public float autoSaveInterval = 60f; // 1 minute
    private float autoSaveTimer;

    void Start()
    {
        manager = GetComponent<PanoramaLayerManager>();
        autoSaveTimer = autoSaveInterval;
    }

    void Update()
    {
        // 1. Auto-Save Logic (Every 60 seconds if a path exists)
        if (!string.IsNullOrEmpty(currentProjectPath) && !isProcessing)
        {
            autoSaveTimer -= Time.deltaTime;
            if (autoSaveTimer <= 0)
            {
                autoSaveTimer = autoSaveInterval;
                StartCoroutine(SaveProjectRoutine(currentProjectPath, true));
            }
        }

        // 2. Handle Shortcuts (Simplified)
        if (!isProcessing)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            // Ctrl + S = Save (Prompts for file name only if it's a brand new project)
            if (ctrl && Input.GetKeyDown(KeyCode.S))
            {
                if (string.IsNullOrEmpty(currentProjectPath))
                {
                    string path = SimpleFileBrowser.SaveFile("Save Project", "MyProject", "panon");
                    if (!string.IsNullOrEmpty(path)) StartCoroutine(SaveProjectRoutine(path, false));
                }
                else
                {
                    StartCoroutine(SaveProjectRoutine(currentProjectPath, false));
                }
            }

            // Ctrl + D = Load
            if (ctrl && Input.GetKeyDown(KeyCode.D))
            {
                string path = SimpleFileBrowser.OpenFile("Load Project", "panon");
                if (!string.IsNullOrEmpty(path)) StartCoroutine(LoadProjectRoutine(path));
            }
        }
    }

    void OnGUI()
    {
        if (isProcessing)
        {
            // UI ANIMATION: Bouncing dots based on time
            int dots = Mathf.FloorToInt(Time.realtimeSinceStartup * 3f) % 4;
            string animatedMessage = processMessageBase + new string('.', dots);

            // --- TOP-MIDDLE NOTIFICATION DESIGN ---
            float w = 220f; 
            float h = 40f;
            float topMargin = 20f; 
            
            // Position: Centered horizontally, hugging the top
            Rect rect = new Rect((Screen.width - w) / 2f, topMargin, w, h);

            // --- THE FIX: Draw a completely flat, borderless background ---
            Color oldColor = GUI.color;
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.7f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture); 
            GUI.color = oldColor;

            // Notification Text Style
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = new GUIStyleState { textColor = new Color(0.9f, 0.9f, 0.9f, 1f) }
            };
            
            // Draw the animated text inside the box
            GUI.Label(rect, animatedMessage, labelStyle);
        }
    }

    // ============================================================================================
    //  ZERO-FREEZE SAVE SYSTEM (With Safety Swap)
    // ============================================================================================
    IEnumerator SaveProjectRoutine(string path, bool isAutoSave)
    {
        isProcessing = true;
        processMessageBase = isAutoSave ? "AUTO-SAVING" : "SAVING";
        yield return null; 

        currentProjectPath = path; // Update tracking path
        autoSaveTimer = autoSaveInterval; // Reset timer

        // --- SAFETY SWAP SETUP ---
        string tempPath = path + ".tmp";
        string backupPath = path + ".bak";

        ProjectData data = new ProjectData();
        data.fps = manager.fps;
        data.totalFrames = manager.totalFrames;
        data.exportWidth = manager.exportWidth;
        data.exportHeight = manager.exportHeight;

        List<LayerNode> allNodes = new List<LayerNode>();
        if (manager.root != null) TraverseTree(manager.root, allNodes);

        List<PaintLayer> activePaintLayers = new List<PaintLayer>();

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
                activePaintLayers.Add(pl);
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

        // --- ASYNC GPU EXTRACTION ---
        List<AsyncGPUReadbackRequest> requests = new List<AsyncGPUReadbackRequest>();
        List<string> layerIds = new List<string>();
        List<Vector2Int> dimensions = new List<Vector2Int>();
        List<UnityEngine.Experimental.Rendering.GraphicsFormat> formats = new List<UnityEngine.Experimental.Rendering.GraphicsFormat>();

        foreach (var pl in activePaintLayers)
        {
            layerIds.Add(pl.id);
            if (pl.texture != null)
            {
                dimensions.Add(new Vector2Int(pl.texture.width, pl.texture.height));
                formats.Add(pl.texture.graphicsFormat);
                requests.Add(AsyncGPUReadback.Request(pl.texture, 0, TextureFormat.RGBA32));
            }
            else
            {
                dimensions.Add(Vector2Int.zero);
                formats.Add(UnityEngine.Experimental.Rendering.GraphicsFormat.None);
                requests.Add(new AsyncGPUReadbackRequest()); // Dummy request
            }
        }

        // Wait for GPU to finish packaging the pixels
        while (requests.Any(r => !r.done && !r.hasError)) yield return null;

        List<byte[]> rawPixelArrays = new List<byte[]>();
        for (int i = 0; i < requests.Count; i++)
        {
            if (dimensions[i] == Vector2Int.zero || requests[i].hasError) rawPixelArrays.Add(null);
            else rawPixelArrays.Add(requests[i].GetData<byte>().ToArray());
        }

        // --- BACKGROUND THREAD ENCODING & WRITING ---
        Task saveTask = Task.Run(() =>
        {
            // WRITE TO TEMP FILE INSTEAD OF MAIN FILE
            using (FileStream fs = new FileStream(tempPath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                writer.Write("PANON_V1");
                writer.Write(jsonBytes.Length);
                writer.Write(jsonBytes);
                writer.Write(layerIds.Count);

                for (int i = 0; i < layerIds.Count; i++)
                {
                    writer.Write(layerIds[i]);

                    if (rawPixelArrays[i] == null)
                    {
                        writer.Write(0); 
                    }
                    else
                    {
                        byte[] pngBytes = ImageConversion.EncodeArrayToPNG(
                            rawPixelArrays[i], formats[i], (uint)dimensions[i].x, (uint)dimensions[i].y);
                        
                        writer.Write(pngBytes.Length);
                        writer.Write(pngBytes);
                    }
                }
            }
        });

        // Wait for background thread to finish writing to hard drive
        while (!saveTask.IsCompleted) yield return null;

        // --- SAFETY SWAP EXECUTION ---
        if (saveTask.IsFaulted) 
        {
            Debug.LogError("Save Failed: " + saveTask.Exception.Message);
            // Delete the bad temp file so it doesn't clutter the drive
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        else 
        {
            try
            {
                // Swap the .tmp file to be the real file
                if (File.Exists(path))
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Replace(tempPath, path, backupPath);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                Debug.Log("Safe Save Complete: " + path);
            }
            catch (System.Exception e)
            {
                Debug.LogError("Safety Swap Failed: " + e.Message);
            }
        }
        
        isProcessing = false;
    }

    void TraverseTree(LayerNode node, List<LayerNode> list)
    {
        list.Add(node);
        if (node is GroupLayer group) foreach (var child in group.children) TraverseTree(child, list);
    }

    // ============================================================================================
    //  SMOOTH LOAD SYSTEM (Compiler Safe)
    // ============================================================================================
    IEnumerator LoadProjectRoutine(string path)
    {
        if (!File.Exists(path)) yield break;

        isProcessing = true;
        processMessageBase = "LOADING";
        yield return null; 

        currentProjectPath = path;
        
        ProjectData data = null;
        List<string> texIds = new List<string>();
        List<byte[]> texBytes = new List<byte[]>();
        bool loadSuccess = false;

        // --- STEP 1: READ FILE SYNCHRONOUSLY (Inside Try/Catch) ---
        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Open))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                string header = reader.ReadString();
                if (header != "PANON_V1") { Debug.LogError("Invalid format!"); isProcessing = false; yield break; }

                int jsonLen = reader.ReadInt32();
                byte[] jsonBytes = reader.ReadBytes(jsonLen);
                string json = System.Text.Encoding.UTF8.GetString(jsonBytes);
                data = JsonUtility.FromJson<ProjectData>(json);

                int texCount = reader.ReadInt32();
                for (int i = 0; i < texCount; i++)
                {
                    string id = reader.ReadString();
                    int len = reader.ReadInt32();
                    if (len > 0)
                    {
                        texIds.Add(id);
                        texBytes.Add(reader.ReadBytes(len));
                    }
                }
            }
            loadSuccess = true;
        }
        catch (System.Exception e) 
        { 
            Debug.LogError("Load Failed: " + e.Message); 
            isProcessing = false;
            yield break;
        }

        // --- STEP 2: BUILD HIERARCHY & UPLOAD TO GPU (Outside Try/Catch, Safe to Yield!) ---
        if (loadSuccess && data != null)
        {
            if (manager.root != null) manager.root.Cleanup();
            manager.root = null;
            manager.activeLayer = null;

            manager.fps = data.fps;
            manager.totalFrames = data.totalFrames;
            manager.exportWidth = data.exportWidth;
            manager.exportHeight = data.exportHeight;

            Dictionary<string, LayerNode> idToNode = new Dictionary<string, LayerNode>();

            // 1. Create Nodes
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

            // 2. Link Parents
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
                    if (parent is GroupLayer pg) { node.parent = pg; pg.children.Add(node); }
                }
            }

            if (manager.root == null) manager.root = new GroupLayer("Root");

            // 3. Upload Textures incrementally to prevent freezing
            for (int i = 0; i < texIds.Count; i++)
            {
                string id = texIds[i];
                byte[] pngBytes = texBytes[i];

                if (idToNode.ContainsKey(id) && idToNode[id] is PaintLayer pl)
                {
                    // --- FIX 1: Disable MipMaps on the temporary decoder texture ---
                    // The 'false' parameter stops Unity from generating blurry downscaled versions
                    Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); 
                    tex.filterMode = FilterMode.Bilinear; // Use FilterMode.Point if you want hard pixel-art edges
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.LoadImage(pngBytes);

                    pl.EnsureTextureAllocated();
                    if (pl.texture.width != tex.width || pl.texture.height != tex.height)
                    {
                        if (pl.texture != null) pl.texture.Release();
                        
                        pl.texture = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                        pl.texture.enableRandomWrite = true;
                        
                        // --- FIX 2: Apply crisp settings to the actual Canvas layer ---
                        pl.texture.useMipMap = false; // Crucial: Stops the canvas from blurring when zoomed out
                        pl.texture.autoGenerateMips = false;
                        pl.texture.filterMode = FilterMode.Point; // Again, use .Point for sharp pixel-art
                        pl.texture.wrapMode = TextureWrapMode.Clamp; // Prevents strokes from wrapping across the screen edges
                        
                        pl.texture.Create();
                    }

                    Graphics.Blit(tex, pl.texture);
                    Destroy(tex);
                    
                    yield return null; 
                }
            }

            manager.activeLayer = manager.root;
            manager.compositionDirty = true;
            Debug.Log("Load Complete!");
        }

        isProcessing = false; 
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