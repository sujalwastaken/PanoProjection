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
    private float saveLoadProgress = 0f; // Progress from 0 to 1
    
    [Header("Auto-Save")]
    public float autoSaveInterval = 60f; // 1 minute
    private float autoSaveTimer;
    
    private bool isEmergencyAutoSaving = false; // Prevents multiple concurrent emergency saves

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
            // --- PROGRESS BAR PANEL (Subtle, matches main UI) ---
            float panelWidth = 250f;
            float panelHeight = 50f;
            float topMargin = 20f;
            
            Rect panelRect = new Rect((Screen.width - panelWidth) / 2f, topMargin, panelWidth, panelHeight);
            
            // Panel background - matches main UI color
            Color oldColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f); // Same as main UI
            GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
            GUI.color = oldColor;
            
            // Text label - smaller and subtle
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = new GUIStyleState { textColor = Color.white }
            };
            
            Rect labelRect = new Rect(panelRect.x, panelRect.y, panelRect.width, 20f);
            string displayText = processMessageBase + " " + Mathf.RoundToInt(saveLoadProgress * 100f) + "%";
            GUI.Label(labelRect, displayText, labelStyle);
            
            // Progress bar background - subtle gray
            float barX = panelRect.x + 12f;
            float barY = panelRect.y + 24f;
            float barWidth = panelRect.width - 24f;
            float barHeight = 10f;
            
            Rect barBG = new Rect(barX, barY, barWidth, barHeight);
            GUI.color = new Color(0.15f, 0.15f, 0.15f, 1f); // Dark gray
            GUI.DrawTexture(barBG, Texture2D.whiteTexture);
            
            // Progress fill - muted blue-gray (lowkey, not bright green)
            Rect barFill = new Rect(barX + 1f, barY + 1f, (barWidth - 2f) * saveLoadProgress, barHeight - 2f);
            GUI.color = new Color(0.4f, 0.6f, 0.7f, 0.8f); // Muted blue-gray
            GUI.DrawTexture(barFill, Texture2D.whiteTexture);
            GUI.color = oldColor;
        }
    }

    // ============================================================================================
    //  PUBLIC METHOD: TRIGGER AUTO-SAVE (For Memory Fail-Safe System)
    // ============================================================================================
    
    /// <summary>
    /// Manually trigger auto-save from external systems (like MemoryFailSafe)
    /// If project hasn't been saved yet, prompts user to save as a file first
    /// Prevents multiple concurrent saves with a flag
    /// </summary>
    public void TriggerAutoSave()
    {
        if (isProcessing || isEmergencyAutoSaving) return;

        // If not yet saved, prompt for save location
        if (string.IsNullOrEmpty(currentProjectPath))
        {
            string path = SimpleFileBrowser.SaveFile("Save Project (Memory Fail-Safe)", "MyProject", "panon");
            if (!string.IsNullOrEmpty(path))
            {
                isEmergencyAutoSaving = true;
                StartCoroutine(SaveProjectRoutine(path, true));
            }
        }
        else
        {
            // Already has a save path, do normal auto-save
            isEmergencyAutoSaving = true;
            StartCoroutine(SaveProjectRoutine(currentProjectPath, true));
        }
    }

    // ============================================================================================
    //  ZERO-FREEZE STREAMING SAVE SYSTEM (Large Project Safe)
    // ============================================================================================
    IEnumerator SaveProjectRoutine(string path, bool isAutoSave)
    {
        isProcessing = true;
        processMessageBase = isAutoSave ? "AUTO-SAVING" : "SAVING";
        yield return null; 

        currentProjectPath = path; 
        autoSaveTimer = autoSaveInterval; 

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

        byte[] jsonBytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data, true));

        FileStream fs = null;
        BinaryWriter writer = null;
        bool saveFailed = false;

        try
        {
            fs = new FileStream(tempPath, FileMode.Create);
            writer = new BinaryWriter(fs);
            writer.Write("PANON_V1");
            writer.Write(jsonBytes.Length);
            writer.Write(jsonBytes);
            writer.Write(activePaintLayers.Count);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Failed to initialize save: " + e.Message);
            if (writer != null) writer.Close();
            if (fs != null) fs.Close();
            isProcessing = false;
            isEmergencyAutoSaving = false;
            yield break;
        }

        // --- THE FIX: STREAMING ARCHITECTURE ---
        // Process one layer at a time to prevent memory spikes
        for (int i = 0; i < activePaintLayers.Count; i++)
        {
            PaintLayer pl = activePaintLayers[i];

            // Update progress
            saveLoadProgress = (float)i / activePaintLayers.Count;

            if (pl.texture == null)
            {
                writer.Write(pl.id);
                writer.Write(0);
                yield return null; 
                continue;
            }

            // 1. Pull data from GPU
            var req = AsyncGPUReadback.Request(pl.texture, 0, TextureFormat.RGBA32);
            while (!req.done) yield return null; // Keep UI responsive

            if (req.hasError)
            {
                writer.Write(pl.id);
                writer.Write(0);
                continue;
            }

            // 2. Allocate RAM for exactly ONE layer
            byte[] rawBytes = req.GetData<byte>().ToArray();
            int w = pl.texture.width;
            int h = pl.texture.height;
            var format = pl.texture.graphicsFormat;
            
            yield return null; // Breathe after RAM allocation

            // 3. Compress and Write on Background Thread
            string layerId = pl.id;
            Task writeTask = Task.Run(() => {
                try {
                    byte[] pngBytes = ImageConversion.EncodeArrayToPNG(rawBytes, format, (uint)w, (uint)h);
                    writer.Write(layerId);
                    writer.Write(pngBytes.Length);
                    writer.Write(pngBytes);
                } catch {
                    saveFailed = true;
                }
            });

            while (!writeTask.IsCompleted) yield return null;

            if (saveFailed) break;

            // 4. Critical: Release the RAM before moving to the next layer
            rawBytes = null;
            yield return null; 
        }

        // Set progress to complete
        saveLoadProgress = 1f;
        yield return null;

        try { if (writer != null) writer.Close(); if (fs != null) fs.Close(); } catch { }

        // --- SAFETY SWAP EXECUTION ---
        if (saveFailed) 
        {
            Debug.LogError("Save aborted to protect original file.");
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
        else 
        {
            try
            {
                if (File.Exists(path))
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Replace(tempPath, path, backupPath);
                }
                else
                {
                    File.Move(tempPath, path);
                }
                Debug.Log("Streaming Save Complete: " + path);
            }
            catch (System.Exception e) { Debug.LogError("Safety Swap Failed: " + e.Message); }
        }
        
        isProcessing = false;
        isEmergencyAutoSaving = false;
        saveLoadProgress = 0f; // Reset progress bar
    }

    void TraverseTree(LayerNode node, List<LayerNode> list)
    {
        list.Add(node);
        if (node is GroupLayer group) foreach (var child in group.children) TraverseTree(child, list);
    }

    // ============================================================================================
    //  SMOOTH LOAD SYSTEM (Background I/O for Large Projects)
    // ============================================================================================
    IEnumerator LoadProjectRoutine(string path)
    {
        if (!File.Exists(path)) yield break;

        isProcessing = true;
        processMessageBase = "LOADING";
        yield return null; 

        currentProjectPath = path;
        
        string jsonString = "";
        List<string> texIds = new List<string>();
        List<byte[]> texBytes = new List<byte[]>();
        bool loadFailed = false;

        // --- THE FIX: BACKGROUND DISK I/O ---
        // Push the file reading to a background thread to prevent massive file reads from freezing Unity
        Task readTask = Task.Run(() => 
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    string header = reader.ReadString();
                    if (header != "PANON_V1") throw new System.Exception("Invalid format!");

                    int jsonLen = reader.ReadInt32();
                    byte[] jsonBytesArr = reader.ReadBytes(jsonLen);
                    jsonString = System.Text.Encoding.UTF8.GetString(jsonBytesArr);

                    int texCount = reader.ReadInt32();
                    for (int i = 0; i < texCount; i++)
                    {
                        texIds.Add(reader.ReadString());
                        int len = reader.ReadInt32();
                        if (len > 0) texBytes.Add(reader.ReadBytes(len));
                        else texBytes.Add(null);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("Background File Read Failed: " + e.Message);
                loadFailed = true;
            }
        });

        // Yield while hard drive does the heavy lifting
        while (!readTask.IsCompleted) yield return null;

        if (loadFailed || string.IsNullOrEmpty(jsonString)) 
        { 
            isProcessing = false; 
            yield break; 
        }

        ProjectData data = JsonUtility.FromJson<ProjectData>(jsonString);

        // --- STEP 2: BUILD HIERARCHY & UPLOAD TO GPU ---
        if (manager.root != null) manager.root.Cleanup();
        manager.root = null;
        manager.activeLayer = null;

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
                if (parent is GroupLayer pg) { node.parent = pg; pg.children.Add(node); }
            }
        }

        if (manager.root == null) manager.root = new GroupLayer("Root");

        // Upload Textures incrementally (Main Thread, but spaced out)
        for (int i = 0; i < texIds.Count; i++)
        {
            string id = texIds[i];
            byte[] pngBytes = texBytes[i];

            // Update progress
            saveLoadProgress = (float)i / texIds.Count;

            if (idToNode.ContainsKey(id) && idToNode[id] is PaintLayer pl)
            {
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false); 
                tex.filterMode = FilterMode.Point; 
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.LoadImage(pngBytes);

                pl.EnsureTextureAllocated();
                if (pl.texture.width != tex.width || pl.texture.height != tex.height)
                {
                    if (pl.texture != null) pl.texture.Release();
                    
                    pl.texture = new RenderTexture(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                    pl.texture.enableRandomWrite = true;
                    pl.texture.useMipMap = false; 
                    pl.texture.autoGenerateMips = false;
                    pl.texture.filterMode = FilterMode.Point; 
                    pl.texture.wrapMode = TextureWrapMode.Clamp; 
                    pl.texture.Create();
                }

                Graphics.Blit(tex, pl.texture);
                Destroy(tex);
                
                yield return null; 
            }
        }

        // Set progress to complete
        saveLoadProgress = 1f;
        yield return null;

        manager.activeLayer = manager.root;
        manager.compositionDirty = true;
        Debug.Log("Streaming Load Complete!");

        isProcessing = false;
        saveLoadProgress = 0f; // Reset progress bar
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