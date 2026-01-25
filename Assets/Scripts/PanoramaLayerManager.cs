using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(PanoramaProjectionEffect))]
[RequireComponent(typeof(PanoramaPaintGPU))]
public class PanoramaLayerManager : MonoBehaviour
{
    [Header("Composition Settings")]
    public Shader compositeShader;
    public Shader onionSkinShader;
    private Material compositeMat;
    private Material onionMat;

    [Header("Performance")]
    [Tooltip("Reduces resolution during playback to ensure smooth framerate.")]
    public bool reduceResolutionOnPlay = true;
    private int fullWidth, fullHeight;

    [Header("Export Settings")]
    public int exportWidth = 1920;
    public int exportHeight = 1080;

    public GroupLayer root;
    public LayerNode activeLayer;
    public int currentFrame = 0;
    public int totalFrames = 24;
    public int fps = 12;
    public bool isPlaying = false;
    public bool loop = true;

    [HideInInspector] public bool ignoreShortcuts = false;
    [HideInInspector] public bool isHoveringGraph = false;
    public bool isExporting = false;

    private PanoramaPaintGPU painter;
    private PanoramaProjectionEffect projector;
    private Camera cam;
    private RenderTexture canvasComposite;
    private float timer;
    public bool compositionDirty = true;

    // --- Onion Skin Settings ---
    [Header("Onion Skin")]
    public bool onionEnabled = false;
    public bool onionSkinLoop = true;
    [Range(0, 5)] public int onionBefore = 1;
    [Range(0, 5)] public int onionAfter = 1;
    [Range(0f, 1f)] public float onionOpacity = 0.35f;
    public Color onionColorBefore = new Color(0f, 0f, 1f, 1f);  // Blue for previous frames
    public Color onionColorAfter = new Color(0f, 1f, 0f, 1f);   // Green for next frames

    private bool showExportPopup = false;
    private float targetHeight = 1080f;
    private int exportFps = 12;

    private int paintCount = 1;
    private int groupCount = 1;
    private int animCount = 1;
    private int camCount = 1;

    public enum ExportFormat { Sequence, MP4, GIF }

    void Start()
    {
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 1;

        painter = GetComponent<PanoramaPaintGPU>();
        projector = GetComponent<PanoramaProjectionEffect>();
        cam = GetComponent<Camera>();

        if (compositeShader == null) compositeShader = Shader.Find("Sprites/Default");
        compositeMat = new Material(compositeShader);

        if (onionSkinShader == null) onionSkinShader = Shader.Find("Hidden/OnionSkin");
        onionMat = new Material(onionSkinShader);

        InitializeSystem();
    }

    void InitializeSystem()
    {
        if (projector.panoramaTexture != null)
        {
            fullWidth = projector.panoramaTexture.width;
            fullHeight = projector.panoramaTexture.height;
        }
        else
        {
            fullWidth = 2048; fullHeight = 1024;
        }

        root = new GroupLayer("Root");
        PaintLayer defaultL = new PaintLayer("Paper", fullWidth, fullHeight);
        defaultL.EnsureTextureAllocated();

        RenderTexture.active = defaultL.texture;
        GL.Clear(true, true, Color.white);
        RenderTexture.active = null;

        defaultL.parent = root;
        root.children.Add(defaultL);

        AddLayer(LayerType.Paint);

        RebuildCompositeCanvas();

        cam.targetTexture = null;
        compositionDirty = true;
    }

    void RebuildCompositeCanvas()
    {
        if (canvasComposite != null) canvasComposite.Release();

        int w = (isPlaying && reduceResolutionOnPlay) ? fullWidth / 2 : fullWidth;
        int h = (isPlaying && reduceResolutionOnPlay) ? fullHeight / 2 : fullHeight;

        canvasComposite = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
        canvasComposite.Create();
    }

    void Update()
    {
        if (activeLayer == null && root != null && root.children.Count > 0)
        {
            activeLayer = root.children[0];
            UpdatePaintTarget(); // Force re-bind
        }
        if (isPlaying)
        {
            timer += Time.deltaTime;
            float interval = 1.0f / fps;

            // ACCUMULATED DELTA FIX:
            // Instead of resetting to 0, we subtract the interval.
            // This 'catches up' if a frame was slow, maintaining perfect clock sync.
            int catchUpLimit = 0; 

            while (timer >= interval && catchUpLimit < 5) // Limit to 5 frames skip
            {
                timer -= interval;
                StepFrame(1);
                catchUpLimit++;
            }
            
            // If we are still behind after 5 frames, just reset the timer to prevent lag spiral
            if (catchUpLimit >= 5) timer = 0;

            ApplyCameraAnimation(currentFrame);
        }

        if (!ignoreShortcuts && !isExporting && !showExportPopup)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) StepFrame(-1);
            if (Input.GetKeyDown(KeyCode.Alpha2)) StepFrame(1);
            if (Input.GetKeyDown(KeyCode.K)) ActionAddKeyframe();
            if (Input.GetKeyDown(KeyCode.A)) JumpToKeyframe(-1);
            if (Input.GetKeyDown(KeyCode.D)) JumpToKeyframe(1);

            if (Input.GetKeyDown(KeyCode.C) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                ActionAddCell();
            }
            if (Input.GetKeyDown(KeyCode.Delete)) DeleteActiveLayer();

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (ctrl && Input.GetKeyDown(KeyCode.X))
            {
                if (shift)
                {
                    string path = SimpleFileBrowser.SaveFile("Export Full Canvas (Sequence)", "CanvasAnim", "png");
                    if (!string.IsNullOrEmpty(path)) StartCoroutine(ExportSequence(path, true, ExportFormat.Sequence, fullWidth, fullHeight, fps));
                }
                else
                {
                    exportFps = fps;
                    showExportPopup = true;
                }
            }
        }

        if (Input.GetMouseButton(0) && GUIUtility.hotControl == 0 && !ignoreShortcuts) compositionDirty = true;

        UpdatePaintTarget();

        if (compositionDirty)
        {
            RenderComposite();
            compositionDirty = false;
        }
    }

    public void ActionAddCell()
    {
        AnimationLayer animLayer = null;
        if (activeLayer is AnimationLayer al) animLayer = al;
        else if (activeLayer != null && activeLayer.parent is AnimationLayer pal) animLayer = pal;

        if (animLayer == null) { AddLayer(LayerType.Paint); return; }

        if (animLayer.timelineMap.ContainsKey(currentFrame))
        {
            int gap = 1;
            List<int> keys = animLayer.timelineMap.Keys.ToList();
            keys.Sort();
            int currentIndex = keys.IndexOf(currentFrame);
            if (currentIndex > 0)
            {
                int prevFrame = keys[currentIndex - 1];
                gap = currentFrame - prevFrame;
            }
            int targetFrame = currentFrame + gap;
            if (targetFrame >= totalFrames)
            {
                if (loop) targetFrame = targetFrame % totalFrames; else targetFrame = totalFrames - 1;
            }
            currentFrame = targetFrame;
            StepFrame(0);
        }
        AddLayer(LayerType.Paint);
    }

    void RenderComposite()
    {
        if (canvasComposite == null || !canvasComposite.IsCreated()) RebuildCompositeCanvas();

        RenderTexture.active = canvasComposite;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;
        RenderNode(root, canvasComposite, 1.0f);
        
        // --- ONION SKIN LOGIC ---
        if (onionEnabled && activeLayer != null)
        {
            AnimationLayer animParent = null;
            if (activeLayer is AnimationLayer al) animParent = al;
            else if (activeLayer.parent is AnimationLayer pl) animParent = pl;

            if (animParent != null && animParent.timelineMap.Count > 0)
            {
                // Get all keyframes sorted
                List<int> keys = animParent.timelineMap.Keys.ToList();
                keys.Sort();

                // Find current index
                int currentKeyIndex = -1;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (keys[i] <= currentFrame) currentKeyIndex = i;
                    else break;
                }
                
                // If before first keyframe, check loop setting
                if (currentKeyIndex == -1 && onionSkinLoop) currentKeyIndex = keys.Count - 1;

                if (currentKeyIndex != -1)
                {
                    int count = keys.Count;

                    // Helper to draw onion skin
                    void DrawOnion(int keyIndex, float opacityBase, Color tint)
                    {
                        // Handle Looping based on new boolean
                        if (onionSkinLoop)
                        {
                            keyIndex = (keyIndex % count + count) % count;
                        }
                        else
                        {
                            if (keyIndex < 0 || keyIndex >= count) return;
                        }

                        // Don't draw self
                        if (keyIndex == currentKeyIndex) return;

                        int frame = keys[keyIndex];
                        int cellIdx = animParent.timelineMap[frame];
                        
                        if (cellIdx >= 0 && cellIdx < animParent.children.Count)
                        {
                            PaintLayer layer = animParent.children[cellIdx] as PaintLayer;
                            if (layer != null && layer.texture != null)
                            {
                                float weight = Mathf.Clamp01(onionOpacity * opacityBase);
                                BlendLayerWithColor(layer.texture, canvasComposite, weight, tint);
                            }
                        }
                    }

                    // Render "Before" frames
                    for (int i = 1; i <= onionBefore; i++)
                    {
                        float falloff = 1.0f - (i - 1) * 0.2f;
                        DrawOnion(currentKeyIndex - i, falloff, onionColorBefore);
                    }

                    // Render "After" frames
                    for (int i = 1; i <= onionAfter; i++)
                    {
                        float falloff = 1.0f - (i - 1) * 0.2f;
                        DrawOnion(currentKeyIndex + i, falloff, onionColorAfter);
                    }
                }
            }
        }

        // NEW: Apply grid overlay AFTER all layers are composited
        painter.UpdateGridDisplay(canvasComposite);
    }

    void RenderNode(LayerNode node, RenderTexture destination, float parentOpacity)
    {
        if (!node.isVisible) return;
        float currentOpacity = node.opacity * parentOpacity;

        if (currentOpacity <= 0.001f) return;

        if (node is PaintLayer pLayer)
        {
            if (pLayer.texture != null)
            {
                BlendLayer(pLayer.texture, destination, currentOpacity);
            }
        }
        else if (node is GroupLayer group && !(node is AnimationLayer))
        {
            foreach (var child in group.children) RenderNode(child, destination, currentOpacity);
        }
        else if (node is AnimationLayer animLayer)
        {
            int activeIndex = animLayer.GetActiveCellIndex(currentFrame);
            if (activeIndex != -1 && activeIndex < animLayer.children.Count)
            {
                RenderNode(animLayer.children[activeIndex], destination, currentOpacity);
            }
        }
    }

    void BlendLayer(Texture source, RenderTexture dest, float alpha)
    {
        RenderTexture.active = dest;
        compositeMat.mainTexture = source;
        compositeMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        GL.Color(new Color(1, 1, 1, alpha));
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = null;
    }

    void BlendLayerWithColor(Texture source, RenderTexture dest, float opacity, Color color)
    {
        RenderTexture.active = dest;
        onionMat.mainTexture = source;
        onionMat.SetColor("_Color", new Color(color.r, color.g, color.b, opacity));
        onionMat.SetPass(0);
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.QUADS);
        GL.Color(Color.white);
        GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = null;
    }

    void UpdatePaintTarget()
    {
        if (isHoveringGraph) { painter.targetTexture = null; painter.allowCameraMovement = false; return; }

        painter.allowCameraMovement = false;
        painter.targetTexture = null;

        if (activeLayer is PaintLayer pl)
        {
            // Allow camera movement if layer exists (even if hidden)
            painter.allowCameraMovement = true;
            
            // Only allow painting if layer is visible
            if (pl.isVisible)
            {
                bool isPaintable = true;
                if (pl.parent is AnimationLayer animParent)
                {
                    int activeIndex = animParent.GetActiveCellIndex(currentFrame);
                    if (activeIndex == -1 || animParent.children[activeIndex] != pl) isPaintable = false;
                }
                if (isPaintable)
                {
                    pl.EnsureTextureAllocated();
                    painter.targetTexture = pl.texture;
                }
            }
        }
        else if (activeLayer is CameraLayer)
        {
            painter.targetTexture = null;
            painter.allowCameraMovement = true;
        }
        else if (activeLayer is GroupLayer || activeLayer is AnimationLayer)
        {
            // Allow camera movement for group/animation layers too
            painter.allowCameraMovement = true;
        }
    }

    void OnGUI()
    {
        if (showExportPopup)
        {
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "", GUI.skin.box);
            Rect rect = new Rect((Screen.width - 400) / 2, (Screen.height - 350) / 2, 400, 350);
            GUI.Window(99, rect, DrawExportWindow, "Export Camera View");
        }
        if (isExporting) GUI.Label(new Rect(20, 20, 300, 50), "EXPORTING...", new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.red } });
    }

    void DrawExportWindow(int id)
    {
        GUILayout.BeginVertical(); GUILayout.Space(15);
        GUILayout.Label("Choose Format:", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }); GUILayout.Space(5);
        if (GUILayout.Button("Image Sequence (PNG)", GUILayout.Height(25))) InitiateExport(ExportFormat.Sequence);
        if (GUILayout.Button("Video (MP4)", GUILayout.Height(25))) InitiateExport(ExportFormat.MP4);
        if (GUILayout.Button("Animated GIF", GUILayout.Height(25))) InitiateExport(ExportFormat.GIF);
        GUILayout.Space(15); GUILayout.Label("Export Resolution (16:9)", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        int h = Mathf.RoundToInt(targetHeight); int w = Mathf.RoundToInt(h * (16.0f / 9.0f)); if (w % 2 != 0) w++; if (h % 2 != 0) h++;
        GUILayout.Label($"{w} x {h}", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });
        targetHeight = GUILayout.HorizontalSlider(targetHeight, 360f, 2160f);
        GUILayout.Space(15); GUILayout.Label($"Frame Rate: {exportFps} FPS", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        exportFps = Mathf.RoundToInt(GUILayout.HorizontalSlider((float)exportFps, 1f, 60f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("12", GUILayout.Width(30))) exportFps = 12; if (GUILayout.Button("24", GUILayout.Width(30))) exportFps = 24;
        if (GUILayout.Button("30", GUILayout.Width(30))) exportFps = 30; if (GUILayout.Button("60", GUILayout.Width(30))) exportFps = 60;
        GUILayout.EndHorizontal(); GUILayout.Space(10);
        if (GUILayout.Button("Cancel", GUILayout.Height(20))) showExportPopup = false;
        GUILayout.EndVertical();
    }

    void InitiateExport(ExportFormat format)
    {
        showExportPopup = false; string ext = format == ExportFormat.MP4 ? "mp4" : (format == ExportFormat.GIF ? "gif" : "png");
        string title = "Save " + format.ToString(); string path = SimpleFileBrowser.SaveFile(title, "MyAnimation", ext);
        if (!string.IsNullOrEmpty(path))
        {
            int h = Mathf.RoundToInt(targetHeight); int w = Mathf.RoundToInt(h * (16.0f / 9.0f)); if (w % 2 != 0) w++; if (h % 2 != 0) h++;
            StartCoroutine(ExportSequence(path, false, format, w, h, exportFps));
        }
    }

    IEnumerator ExportSequence(string fullPath, bool exportCanvas, ExportFormat format, int renderW, int renderH, int targetFps)
    {
        isExporting = true; bool wasPlaying = isPlaying; isPlaying = false;
        RenderTexture tempCameraRT = null;
        RebuildCompositeCanvas();

        try
        {
            string finalDirectory = Path.GetDirectoryName(fullPath); string fileName = Path.GetFileNameWithoutExtension(fullPath);
            string saveFolder = (format == ExportFormat.Sequence) ? Path.Combine(finalDirectory, fileName + "_Sequence") : Path.Combine(Application.temporaryCachePath, "RenderTemp");
            if (Directory.Exists(saveFolder)) Directory.Delete(saveFolder, true); Directory.CreateDirectory(saveFolder);
            if (!exportCanvas) { tempCameraRT = new RenderTexture(renderW, renderH, 24, RenderTextureFormat.ARGB32); }
            RenderTexture sourceRT = exportCanvas ? canvasComposite : tempCameraRT;
            int w = sourceRT.width; int h = sourceRT.height;

            for (int f = 0; f < totalFrames; f++)
            {
                currentFrame = f; ApplyCameraAnimation(currentFrame);
                AnimationLayer al = GetActiveAnimationLayer(); if (al != null) AutoSelectLayerForFrame(al);
                RenderComposite();
                if (!exportCanvas) { var prevTarget = cam.targetTexture; cam.targetTexture = tempCameraRT; cam.Render(); cam.targetTexture = prevTarget; }
                var req = AsyncGPUReadback.Request(sourceRT, 0, TextureFormat.RGBA32);
                while (!req.done) yield return null;
                if (!req.hasError) { byte[] bytes = ImageConversion.EncodeArrayToPNG(req.GetData<byte>().ToArray(), sourceRT.graphicsFormat, (uint)w, (uint)h); string framePath = Path.Combine(saveFolder, $"frame_{f:D4}.png"); File.WriteAllBytes(framePath, bytes); }
            }

            if (format == ExportFormat.MP4 || format == ExportFormat.GIF)
            {
                string ffmpegPath = Path.Combine(Application.streamingAssetsPath, "ffmpeg.exe");
                if (!File.Exists(ffmpegPath)) ffmpegPath = Path.Combine(Application.streamingAssetsPath, "bin", "ffmpeg.exe");
                if (File.Exists(ffmpegPath))
                {
                    string inputPattern = Path.Combine(saveFolder, "frame_%04d.png").Replace("\\", "/"); string safeOutput = fullPath.Replace("\\", "/");
                    string args = ""; if (format == ExportFormat.MP4) args = $"-framerate {targetFps} -i \"{inputPattern}\" -c:v libx264 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" \"{safeOutput}\" -y"; else args = $"-framerate {targetFps} -i \"{inputPattern}\" -vf \"split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" \"{safeOutput}\" -y";
                    ProcessStartInfo psi = new ProcessStartInfo(); psi.FileName = ffmpegPath; psi.Arguments = args; psi.UseShellExecute = false; psi.CreateNoWindow = true;
                    Process p = Process.Start(psi); p.WaitForExit();
                }
            }
            if (format == ExportFormat.Sequence) Application.OpenURL("file://" + saveFolder); else Application.OpenURL("file://" + finalDirectory);
        }
        finally
        {
            if (tempCameraRT != null) { cam.targetTexture = null; tempCameraRT.Release(); Destroy(tempCameraRT); }
            isExporting = false; isPlaying = wasPlaying;
        }
    }

    public void ActionAddKeyframe() { AddCameraKeyframe(); }
    public void ActionRemoveKeyframe()
    {
        CameraLayer camLayer = FindCameraLayer();
        if (camLayer != null)
        {
            RemoveKey(camLayer.curvePitch, currentFrame); RemoveKey(camLayer.curveYaw, currentFrame);
            RemoveKey(camLayer.curveRoll, currentFrame); RemoveKey(camLayer.curveZoom, currentFrame);
            RemoveKey(camLayer.curveFisheye, currentFrame);
        }
    }
    void RemoveKey(AnimationCurve curve, float time) { for (int i = 0; i < curve.keys.Length; i++) { if (Mathf.Approximately(curve.keys[i].time, time)) { curve.RemoveKey(i); break; } } }
    public void MoveActiveLayerUp() => MoveLayerIndex(activeLayer, 1);
    public void MoveActiveLayerDown() => MoveLayerIndex(activeLayer, -1);
    public void MoveLayerIndex(LayerNode node, int direction)
    {
        if (node == null || node.parent == null) return; GroupLayer parent = (GroupLayer)node.parent;
        int idx = parent.children.IndexOf(node); int newIdx = idx + direction;
        if (newIdx >= 0 && newIdx < parent.children.Count)
        {
            if (parent is AnimationLayer animParent)
            {
                List<int> keys = new List<int>(animParent.timelineMap.Keys);
                foreach (int f in keys) { if (animParent.timelineMap[f] == idx) animParent.timelineMap[f] = newIdx; else if (animParent.timelineMap[f] == newIdx) animParent.timelineMap[f] = idx; }
            }
            parent.children.RemoveAt(idx); parent.children.Insert(newIdx, node); compositionDirty = true;
        }
    }
    public void MoveActiveLayerIn()
    {
        if (activeLayer == null || activeLayer.parent == null) return; GroupLayer parent = (GroupLayer)activeLayer.parent;
        int idx = parent.children.IndexOf(activeLayer);
        if (idx + 1 < parent.children.Count)
        {
            LayerNode targetGroup = parent.children[idx + 1];
            if (targetGroup is GroupLayer group)
            {
                if (parent is AnimationLayer animParent) RemoveFromAnimTimeline(animParent, idx);
                parent.children.Remove(activeLayer); group.children.Insert(0, activeLayer); activeLayer.parent = group; group.expanded = true; compositionDirty = true;
            }
        }
    }
    public void MoveActiveLayerOut()
    {
        if (activeLayer == null || activeLayer.parent == null) return; GroupLayer currentParent = (GroupLayer)activeLayer.parent;
        if (currentParent == root || currentParent.parent == null) return;
        GroupLayer grandParent = (GroupLayer)currentParent.parent; int parentIdx = grandParent.children.IndexOf(currentParent);
        int currentIdx = currentParent.children.IndexOf(activeLayer);
        if (currentParent is AnimationLayer animParent) RemoveFromAnimTimeline(animParent, currentIdx);
        currentParent.children.Remove(activeLayer); grandParent.children.Insert(parentIdx, activeLayer); activeLayer.parent = grandParent; compositionDirty = true;
    }
    void RemoveFromAnimTimeline(AnimationLayer anim, int removedIndex)
    {
        List<int> frames = new List<int>(anim.timelineMap.Keys);
        foreach (int f in frames) { int idx = anim.timelineMap[f]; if (idx == removedIndex) anim.timelineMap.Remove(f); else if (idx > removedIndex) anim.timelineMap[f] = idx - 1; }
    }
    public void AddLayer(LayerType type)
    {
        int w = fullWidth; int h = fullHeight;
        LayerNode newNode = null;
        switch (type)
        {
            case LayerType.Paint: newNode = new PaintLayer($"Layer {paintCount++}", w, h); break;
            case LayerType.Folder: newNode = new GroupLayer($"Folder {groupCount++}"); break;
            case LayerType.Animation: newNode = new AnimationLayer($"Anim {animCount++}"); break;
            case LayerType.Camera: newNode = new CameraLayer($"Cam {camCount++}"); break;
        }
        if (newNode == null) return; GroupLayer targetGroup = root;
        if (activeLayer != null) { if (activeLayer is GroupLayer group) targetGroup = group; else if (activeLayer.parent is GroupLayer parentGroup) targetGroup = parentGroup; }
        newNode.parent = targetGroup; targetGroup.children.Add(newNode);
        if (targetGroup is AnimationLayer animLayer) { int childIndex = targetGroup.children.Count - 1; animLayer.SetCell(currentFrame, childIndex); newNode.name = (childIndex + 1).ToString(); }
        activeLayer = newNode; compositionDirty = true;
    }
    public void DeleteActiveLayer()
    {
        if (activeLayer == null || activeLayer == root) return; LayerNode nodeToDelete = activeLayer; GroupLayer parent = (GroupLayer)nodeToDelete.parent;
        int idx = parent.children.IndexOf(nodeToDelete); if (idx == -1) return; nodeToDelete.Cleanup();
        if (parent is AnimationLayer animParent) RemoveFromAnimTimeline(animParent, idx);
        parent.children.Remove(nodeToDelete);
        if (parent.children.Count > 0) activeLayer = parent.children[Mathf.Clamp(idx - 1, 0, parent.children.Count - 1)]; else activeLayer = parent; compositionDirty = true;
    }
    public void AddCameraKeyframe()
    {
        CameraLayer camLayer = FindCameraLayer(); if (camLayer == null) return;
        
        Vector3 rot = cam.transform.eulerAngles;
        
        // Fix: Unwrap angles based on previous keyframe values to prevent "spinning the wrong way"
        float unwrappedPitch = GetUnwrappedAngle(camLayer.curvePitch, rot.x);
        float unwrappedYaw   = GetUnwrappedAngle(camLayer.curveYaw, rot.y);
        float unwrappedRoll  = GetUnwrappedAngle(camLayer.curveRoll, rot.z);

        AddKey(camLayer.curvePitch, currentFrame, unwrappedPitch);
        AddKey(camLayer.curveYaw, currentFrame, unwrappedYaw);
        AddKey(camLayer.curveRoll, currentFrame, unwrappedRoll);
        
        AddKey(camLayer.curveZoom, currentFrame, projector.perspective);
        AddKey(camLayer.curveFisheye, currentFrame, projector.fisheyePerspective);
    }

    private float GetUnwrappedAngle(AnimationCurve curve, float newAngle)
    {
        if (curve.length == 0) return newAngle;

        // Get the value of the curve at the current time (or last keyframe)
        float lastVal = curve.Evaluate(currentFrame);
        
        // Find the difference and snap it to the range -180 to 180
        float delta = newAngle - (lastVal % 360f);
        if (delta > 180f) delta -= 360f;
        else if (delta < -180f) delta += 360f;

        return lastVal + delta;
    }

    void AddKey(AnimationCurve curve, float time, float val) { for (int i = 0; i < curve.keys.Length; i++) { if (Mathf.Approximately(curve.keys[i].time, time)) { curve.RemoveKey(i); break; } } curve.AddKey(time, val); }
    public void ApplyCameraAnimation(float frame)
    {
        CameraLayer camLayer = FindCameraLayer(); if (camLayer == null || camLayer.curveZoom.length == 0) return;
        float p = camLayer.curvePitch.Evaluate(frame); float y = camLayer.curveYaw.Evaluate(frame);
        float r = camLayer.curveRoll.Evaluate(frame); float z = camLayer.curveZoom.Evaluate(frame); float f = camLayer.curveFisheye.Evaluate(frame);
        cam.transform.localEulerAngles = new Vector3(p, y, r); projector.perspective = z; projector.fisheyePerspective = f; painter.SyncCameraFromTransform();
    }

    CameraLayer FindCameraLayer()
    {
        // Priority 1: If the user explicitly selected a camera, use it (even if hidden)
        if (activeLayer is CameraLayer cl) return cl;

        // Priority 2: Find the Highest, Visible camera in the hierarchy
        // We search recursively starting from the visual top (end of list) downwards.
        return FindHighestVisibleCameraRecursive(root);
    }

    CameraLayer FindHighestVisibleCameraRecursive(LayerNode node)
    {
        // If it's a group, search its children from Top (Last index) to Bottom (0)
        if (node is GroupLayer group)
        {
            for (int i = group.children.Count - 1; i >= 0; i--)
            {
                CameraLayer found = FindHighestVisibleCameraRecursive(group.children[i]);
                if (found != null) return found;
            }
        }

        // Check if this specific node is a Visible Camera
        if (node is CameraLayer cam && cam.isVisible)
        {
            return cam;
        }

        return null;
    }

    public void StepFrame(int dir)
    {
        int next = currentFrame + dir; if (next >= totalFrames) { if (loop) next = 0; else { next = totalFrames - 1; isPlaying = false; } } else if (next < 0) { if (loop) next = totalFrames - 1; else next = 0; }
        currentFrame = next; if (isPlaying || activeLayer is CameraLayer) ApplyCameraAnimation(currentFrame);
        AnimationLayer al = GetActiveAnimationLayer(); if (al != null) AutoSelectLayerForFrame(al); compositionDirty = true;
    }
    public void JumpToKeyframe(int dir)
    {
        AnimationLayer animLayer = GetActiveAnimationLayer();
        if (animLayer != null && animLayer.timelineMap.Count > 1)
        {
            List<int> keys = animLayer.timelineMap.Keys.ToList(); keys.Sort(); int targetFrame = currentFrame;
            if (dir > 0) { int nextKey = -1; foreach (int k in keys) { if (k > currentFrame) { nextKey = k; break; } } if (nextKey != -1) targetFrame = nextKey; else if (loop) targetFrame = keys[0]; }
            else { int prevKey = -1; for (int i = keys.Count - 1; i >= 0; i--) { if (keys[i] < currentFrame) { prevKey = keys[i]; break; } } if (prevKey != -1) targetFrame = prevKey; else if (loop) targetFrame = keys[keys.Count - 1]; }
            currentFrame = targetFrame; AutoSelectLayerForFrame(animLayer); compositionDirty = true; if (activeLayer is CameraLayer) ApplyCameraAnimation(currentFrame); return;
        }
        StepFrame(dir);
    }
    AnimationLayer GetActiveAnimationLayer() { if (activeLayer is AnimationLayer al) return al; if (activeLayer != null && activeLayer.parent is AnimationLayer pl) return pl; return null; }
    void AutoSelectLayerForFrame(AnimationLayer animLayer) { int cellIndex = animLayer.GetActiveCellIndex(currentFrame); if (cellIndex != -1 && cellIndex < animLayer.children.Count) activeLayer = animLayer.children[cellIndex]; }
    public void SaveActiveLayerState() { if (activeLayer is PaintLayer pl) pl.SaveState(); }
    public void UndoActiveLayer() { if (activeLayer is PaintLayer pl) { pl.Undo(); compositionDirty = true; } }
    public void RedoActiveLayer() { if (activeLayer is PaintLayer pl) { pl.Redo(); compositionDirty = true; } }
}
