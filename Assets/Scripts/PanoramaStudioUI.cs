using UnityEngine;

[RequireComponent(typeof(PanoramaLayerManager))]
public class PanoramaStudioUI : MonoBehaviour
{
    private PanoramaLayerManager layerManager;
    private bool showUI = true;

    // --- WINDOW RECTS ---
    private Rect timelineRect;
    private Rect layersRect;
    private Rect graphRect;

    // --- SCROLL POSITIONS ---
    private Vector2 layerScroll;
    private Vector2 timelineScroll;

    // --- LAYOUT CONSTANTS ---
    private float layerRowHeight = 28f;
    private float timelineRowHeight = 26f;
    private float cellWidth = 32f;
    private float layerNameColWidth = 150f;
    private float visColWidth = 30f;
    private float typeColWidth = 40f;

    // --- RENAMING STATE ---
    private LayerNode renamingNode = null;
    private string renameBuffer = "";

    // --- COLORS ---
    private Color cellHighlightBlue = new Color(0.2f, 0.6f, 1.0f);
    private Color gridLineColor = new Color(0.4f, 0.4f, 0.4f, 0.4f);
    private Color labelColor = new Color(0.8f, 0.8f, 0.8f);

    // --- GRID COLORS ---
    private Color gridColA = new Color(0.95f, 0.95f, 0.95f, 1f);
    private Color gridColB = new Color(0.20f, 0.20f, 0.20f, 1f);

    // Curve Colors
    private Color colPitch = new Color(1f, 0.3f, 0.3f);
    private Color colYaw = new Color(0.3f, 1f, 0.3f);
    private Color colRoll = new Color(0.3f, 0.6f, 1f);
    private Color colZoom = new Color(0f, 1f, 1f);
    private Color colFish = new Color(1f, 0f, 1f);

    // Key Mode Colors
    private Color keySmooth = new Color(0.3f, 0.6f, 1f);
    private Color keyLinear = new Color(0.4f, 1f, 0.4f);
    private Color keyHold = new Color(1f, 0.8f, 0.2f);

    // --- OPTIMIZATION: CACHED STYLES ---
    private GUIStyle timeRulerStyle;
    private GUIStyle fpsStyle;

    // --- GRAPH EDITOR STATE ---
    private bool showGraph = false;
    private bool fitGraphPending = false;
    private int selectedCurveIndex = 0;
    private bool[] visibleCurves = new bool[] { true, false, false, false, false };
    private Vector2 graphPan = new Vector2(50, 250);
    private Vector2 graphZoom = new Vector2(40f, 5f);

    private int selectedKeyIndex = -1;
    private int draggingKeyIndex = -1;
    private int draggingHandle = 0;
    private bool isPanning = false;
    private bool handlesPaired = true;

    private static Texture2D lineTex;

    // --- PLAYBACK FPS COUNTER STATE ---
    private float playbackFpsTimer = 0f;
    private int playbackFrameCount = 0;
    private int lastPlaybackFrame = -1;
    private float currentPlaybackFps = 0f;

    void Start()
    {
        layerManager = GetComponent<PanoramaLayerManager>();
        if (lineTex == null) { lineTex = new Texture2D(1, 1); lineTex.SetPixel(0, 0, Color.white); lineTex.Apply(); }

        timelineRect = new Rect(20, Screen.height - 320, Screen.width - 50, 300);
        layersRect = new Rect(Screen.width - 350, 20, 320, 650);
        graphRect = new Rect(350, 20, 800, 500);

        // OPTIMIZATION: Initialize Styles ONCE
        timeRulerStyle = new GUIStyle();
        timeRulerStyle.alignment = TextAnchor.MiddleLeft;
        timeRulerStyle.fontSize = 10;
        timeRulerStyle.normal.textColor = Color.gray;

        fpsStyle = new GUIStyle();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Tab)) showUI = !showUI;

        // Track playback FPS
        if (layerManager.isPlaying)
        {
            // Use Time.unscaledDeltaTime for the counter so UI updates 
            // are independent of game time scaling
            playbackFpsTimer += Time.unscaledDeltaTime;

            if (lastPlaybackFrame != layerManager.currentFrame)
            {
                // Calculate how many frames we actually stepped since the last UI update
                int frameDelta = layerManager.currentFrame - lastPlaybackFrame;

                // Handle looping (e.g., going from frame 23 back to 0)
                if (frameDelta < 0) frameDelta += layerManager.totalFrames;

                playbackFrameCount += frameDelta;
                lastPlaybackFrame = layerManager.currentFrame;
            }

            if (playbackFpsTimer >= 0.5f)
            {
                currentPlaybackFps = playbackFrameCount / playbackFpsTimer;
                playbackFrameCount = 0;
                playbackFpsTimer = 0f;
            }
        }
        else
        {
            playbackFpsTimer = 0f;
            playbackFrameCount = 0;
            currentPlaybackFps = 0f;
            lastPlaybackFrame = layerManager.currentFrame;
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        // Ensure default skin is loaded before modifying
        if (GUI.skin.label.fontSize != 12)
        {
            GUI.skin.label.fontSize = 12;
            GUI.skin.label.alignment = TextAnchor.MiddleLeft;
            GUI.skin.button.fontSize = 12;
            GUI.skin.button.alignment = TextAnchor.MiddleCenter;
            GUI.skin.textField.fontSize = 12;
        }

        layerManager.ignoreShortcuts = (GUIUtility.keyboardControl != 0);

        bool mouseOverGraph = showGraph && graphRect.Contains(Event.current.mousePosition);
        bool mouseOverLayers = layersRect.Contains(Event.current.mousePosition);
        bool mouseOverTimeline = timelineRect.Contains(Event.current.mousePosition);

        if (mouseOverGraph || mouseOverLayers || mouseOverTimeline || isPanning || draggingKeyIndex != -1 || draggingHandle != 0)
            layerManager.isHoveringGraph = true;
        else
            layerManager.isHoveringGraph = false;

        if (layerManager.isExporting)
        {
            GUI.Label(new Rect(Screen.width / 2 - 50, Screen.height / 2, 200, 50),
                $"Exporting Frame {layerManager.currentFrame}/{layerManager.totalFrames}...",
                new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = Color.red } });
            return;
        }

        timelineRect.width = Screen.width - 40;

        DrawTimelineWindow();
        DrawLayerWindow();
        if (showGraph) DrawGraphWindow();
    }

    // =================================================================================================
    // 1. GRAPH WINDOW
    // =================================================================================================

    CameraLayer FindRelevantCamera()
    {
        if (layerManager.activeLayer is CameraLayer cl) return cl;
        LayerNode p = layerManager.activeLayer?.parent;
        while (p != null) { if (p is CameraLayer pcl) return pcl; p = p.parent; }
        return FindCameraRecursive(layerManager.root);
    }

    CameraLayer FindCameraRecursive(LayerNode node)
    {
        if (node is CameraLayer cl) return cl;
        if (node is GroupLayer gl) { foreach (var child in gl.children) { var res = FindCameraRecursive(child); if (res != null) return res; } }
        return null;
    }

    private Vector2 windowDragMouseAnchor;
    private Vector2 windowDragPosAnchor;

    private bool invertPanY = false;
    void DrawGraphWindow()
    {
        CameraLayer camLayer = FindRelevantCamera();
        if (camLayer == null) { if (showGraph) showGraph = false; return; }

        Event e = Event.current;
        if (fitGraphPending || (e.type == EventType.KeyDown && e.keyCode == KeyCode.F))
        {
            FitGraphView(camLayer);
            fitGraphPending = false;
            if (e.type == EventType.KeyDown) e.Use();
        }

        // --- WINDOW DRAGGING ---
        Rect headerRect = new Rect(graphRect.x, graphRect.y, graphRect.width, 25);
        if (e.type == EventType.MouseDown && headerRect.Contains(e.mousePosition) && e.button == 0)
        {
            GUIUtility.hotControl = 1001;
            windowDragMouseAnchor = e.mousePosition;
            windowDragPosAnchor = graphRect.position;
            e.Use();
        }
        if (GUIUtility.hotControl == 1001)
        {
            if (e.type == EventType.MouseDrag) { graphRect.position = windowDragPosAnchor + (e.mousePosition - windowDragMouseAnchor); e.Use(); }
            else if (e.type == EventType.MouseUp) { GUIUtility.hotControl = 0; e.Use(); }
        }

        graphRect = GUI.Window(99, graphRect, (id) =>
        {
            GUILayout.BeginVertical();

            // --- HEADER ---
            GUILayout.BeginHorizontal();
            string[] names = { "Pitch", "Yaw", "Roll", "Zoom", "Fish" };
            Color[] colors = { colPitch, colYaw, colRoll, colZoom, colFish };
            for (int i = 0; i < 5; i++)
            {
                GUI.backgroundColor = visibleCurves[i] ? colors[i] : new Color(0.2f, 0.2f, 0.2f);
                string prefix = (selectedCurveIndex == i) ? "> " : "";
                if (GUILayout.Toggle(visibleCurves[i], prefix + names[i], GUI.skin.button, GUILayout.Width(70)) != visibleCurves[i])
                {
                    visibleCurves[i] = !visibleCurves[i];
                    if (visibleCurves[i]) selectedCurveIndex = i;
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
            invertPanY = GUILayout.Toggle(invertPanY, "Pen Mode", GUI.skin.button, GUILayout.Width(70));
            if (GUILayout.Button("Fit (F)", GUILayout.Width(50))) FitGraphView(camLayer);
            if (GUILayout.Button("X", GUILayout.Width(25))) showGraph = false;
            GUILayout.EndHorizontal();

            // --- GRAPH AREA ---
            Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(area, "");
            HandleGraphEvents(area, camLayer);

            GUI.BeginGroup(area);
            DrawGridWithLabels(area);
            DrawPlayhead(area);
            for (int i = 0; i < 5; i++) { if (visibleCurves[i]) DrawCurve(area, GetCurveByIndex(camLayer, i), i); }
            if (visibleCurves[selectedCurveIndex]) DrawHandles(area, GetSelectedCurve(camLayer));
            GUI.EndGroup();

            // --- FOOTER ---
            if (selectedKeyIndex != -1)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                AnimationCurve curve = GetSelectedCurve(camLayer);
                Keyframe k = curve.keys[selectedKeyIndex];
                
                // Determine Mode based on Color
                Color kCol = GetColorForKey(k);
                
                // Compare colors (approximate comparison for safety)
                bool isSmooth = (kCol == keySmooth);
                bool isLinear = (kCol == keyLinear);
                bool isHold = (kCol == keyHold);

                GUI.backgroundColor = isSmooth ? keySmooth : new Color(0.2f,0.2f,0.2f);
                if (GUILayout.Button("Smooth", GUILayout.Width(60))) SetKeyMode(curve, selectedKeyIndex, 0);
                
                GUI.backgroundColor = isLinear ? keyLinear : new Color(0.2f, 0.2f, 0.2f);
                if (GUILayout.Button("Linear", GUILayout.Width(60))) SetKeyMode(curve, selectedKeyIndex, 1);
                
                GUI.backgroundColor = isHold ? keyHold : new Color(0.2f, 0.2f, 0.2f);
                if (GUILayout.Button("Hold", GUILayout.Width(50))) SetKeyMode(curve, selectedKeyIndex, 2);
                
                GUI.backgroundColor = Color.white;
                GUILayout.Space(10);
                if (GUILayout.Button(handlesPaired ? "Linked" : "Broken", GUILayout.Width(70))) {
                    handlesPaired = !handlesPaired;
                    if (handlesPaired) { k.outTangent = k.inTangent; curve.MoveKey(selectedKeyIndex, k); }
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Delete", GUILayout.Width(60))) { curve.RemoveKey(selectedKeyIndex); selectedKeyIndex = -1; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }, $"Curve Editor - {camLayer.name}");
    }

    AnimationCurve GetCurveByIndex(CameraLayer layer, int index)
    {
        switch (index)
        {
            case 0: return layer.curvePitch;
            case 1: return layer.curveYaw;
            case 2: return layer.curveRoll;
            case 3: return layer.curveZoom;
            case 4: return layer.curveFisheye;
            default: return layer.curvePitch;
        }
    }

    void SetKeyMode(AnimationCurve curve, int index, int mode) { Keyframe k = curve.keys[index]; if (mode == 0) { k.weightedMode = WeightedMode.None; k.inTangent = 0; k.outTangent = 0; curve.MoveKey(index, k); curve.SmoothTangents(index, 0f); handlesPaired = true; } else if (mode == 1) { k.weightedMode = WeightedMode.None; RecalculateLinearTangents(curve, index, ref k); curve.MoveKey(index, k); handlesPaired = false; } else if (mode == 2) { k.weightedMode = WeightedMode.None; k.inTangent = float.PositiveInfinity; k.outTangent = float.PositiveInfinity; curve.MoveKey(index, k); handlesPaired = false; } UpdateNeighborTangents(curve, index); layerManager.ApplyCameraAnimation(layerManager.currentFrame); }
    void RecalculateLinearTangents(AnimationCurve curve, int index, ref Keyframe k) { float inSlope = 0, outSlope = 0; if (index > 0) { Keyframe prev = curve.keys[index - 1]; inSlope = (k.value - prev.value) / (k.time - prev.time); } if (index < curve.length - 1) { Keyframe next = curve.keys[index + 1]; outSlope = (next.value - k.value) / (next.time - k.time); } k.inTangent = inSlope; k.outTangent = outSlope; }
    void UpdateNeighborTangents(AnimationCurve curve, int movedIndex) { if (movedIndex > 0) { Keyframe prev = curve.keys[movedIndex - 1]; if (IsKeyLinear(prev, curve, movedIndex - 1)) { float slope = (curve.keys[movedIndex].value - prev.value) / (curve.keys[movedIndex].time - prev.time); prev.outTangent = slope; curve.MoveKey(movedIndex - 1, prev); } } if (movedIndex < curve.length - 1) { Keyframe next = curve.keys[movedIndex + 1]; if (IsKeyLinear(next, curve, movedIndex + 1)) { float slope = (next.value - curve.keys[movedIndex].value) / (next.time - curve.keys[movedIndex].time); next.inTangent = slope; curve.MoveKey(movedIndex + 1, next); } } }
    bool IsKeyLinear(Keyframe k, AnimationCurve curve, int index) 
    { 
        if (float.IsInfinity(k.inTangent) || float.IsInfinity(k.outTangent)) return false; 
        
        float inTarget = 0, outTarget = 0; 
        // We use a very small epsilon (0.001f) for strictness
        if (index > 0) {
            inTarget = (k.value - curve.keys[index - 1].value) / (k.time - curve.keys[index - 1].time);
            if (Mathf.Abs(k.inTangent - inTarget) > 0.001f) return false;
        }
        if (index < curve.length - 1) {
            outTarget = (curve.keys[index + 1].value - k.value) / (curve.keys[index + 1].time - k.time);
            if (Mathf.Abs(k.outTangent - outTarget) > 0.001f) return false;
        }
        return true; 
    }
    AnimationCurve GetSelectedCurve(CameraLayer layer) { switch (selectedCurveIndex) { case 0: return layer.curvePitch; case 1: return layer.curveYaw; case 2: return layer.curveRoll; case 3: return layer.curveZoom; case 4: return layer.curveFisheye; default: return layer.curvePitch; } }
    Color GetColorForCurve() { switch (selectedCurveIndex) { case 0: return colPitch; case 1: return colYaw; case 2: return colRoll; case 3: return colZoom; case 4: return colFish; default: return Color.white; } }
    Color GetColorForKey(Keyframe k) { if (float.IsInfinity(k.outTangent)) return keyHold; if (Mathf.Abs(k.inTangent - k.outTangent) > 0.01f) return keyLinear; return keySmooth; }
    void HandleGraphEvents(Rect rect, CameraLayer layer)
    {
        Event e = Event.current; 
        Vector2 localMouse = e.mousePosition - new Vector2(rect.x, rect.y); 
        float time = (localMouse.x - graphPan.x) / graphZoom.x; 
        float val = -((localMouse.y - rect.height / 2.0f - graphPan.y) / graphZoom.y); 
        bool mouseInGraph = rect.Contains(e.mousePosition); 

        // --- PANNING ---
        if (mouseInGraph && e.alt && !e.control && e.type == EventType.MouseDrag) 
        { 
            // Apply X normally
            graphPan.x += e.delta.x;

            // Apply Y based on the Toggle
            float yDelta = e.delta.y;
            
            // If "Pen Mode" is ON, we FLIP the Y delta.
            // If "Pen Mode" is OFF (Mouse), we subtract Y normally.
            if (invertPanY) 
            {
                graphPan.y += yDelta; // Pen behavior
            }
            else 
            {
                graphPan.y -= yDelta; // Mouse behavior
            }
            
            e.Use(); 
            return; 
        }
        // --- CTRL + ALT + DRAG ZOOM ---
        if (mouseInGraph && e.alt && e.control && e.type == EventType.MouseDrag)
        {
            float zoomDelta = e.delta.x - e.delta.y;
            graphZoom *= (1.0f + zoomDelta * 0.01f);
            graphZoom.x = Mathf.Max(0.1f, graphZoom.x);
            graphZoom.y = Mathf.Max(0.01f, graphZoom.y);
            e.Use();
            return;
        }

        if (mouseInGraph)
        {
            int hitKey = -1; int hitCurve = -1; int hitHandle = 0;

            // Handle Detection (Active Curve Only)
            AnimationCurve activeCurve = GetSelectedCurve(layer);
            if (selectedKeyIndex != -1 && selectedKeyIndex < activeCurve.length)
            {
                Keyframe k = activeCurve.keys[selectedKeyIndex];
                if (!float.IsInfinity(k.outTangent))
                {
                    Vector2 keyPos = GraphToScreen(rect, k.time, k.value);
                    if (Vector2.Distance(keyPos + GetHandleOffset(k.inTangent, -1), localMouse) < 12f) hitHandle = -1;
                    else if (Vector2.Distance(keyPos + GetHandleOffset(k.outTangent, 1), localMouse) < 12f) hitHandle = 1;
                }
            }

            // Multi-Curve Key Detection
            if (hitHandle == 0)
            {
                float closestDist = 15f;
                for (int c = 0; c < 5; c++)
                {
                    if (!visibleCurves[c]) continue;
                    AnimationCurve curve = GetCurveByIndex(layer, c);
                    for (int i = 0; i < curve.keys.Length; i++)
                    {
                        Vector2 kPos = GraphToScreen(rect, curve.keys[i].time, curve.keys[i].value);
                        float d = Vector2.Distance(kPos, localMouse);
                        if (d < closestDist) { closestDist = d; hitKey = i; hitCurve = c; }
                    }
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                if (hitHandle != 0) draggingHandle = hitHandle;
                else if (hitKey != -1) { selectedCurveIndex = hitCurve; selectedKeyIndex = hitKey; draggingKeyIndex = hitKey; }
                else
                {
                    selectedKeyIndex = -1;
                    if (e.clickCount == 2) { GetSelectedCurve(layer).AddKey(time, val); }
                }
                e.Use();
            }

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                AnimationCurve curve = GetSelectedCurve(layer);
                if (draggingHandle != 0 && selectedKeyIndex != -1)
                {
                    Keyframe k = curve.keys[selectedKeyIndex];
                    Vector2 handleDir = localMouse - GraphToScreen(rect, k.time, k.value);
                    float slope = (-handleDir.y / graphZoom.y) / (Mathf.Abs(handleDir.x / graphZoom.x) < 0.0001f ? 0.0001f : handleDir.x / graphZoom.x);
                    if (draggingHandle == -1) k.inTangent = slope; else k.outTangent = slope;
                    if (handlesPaired) { if (draggingHandle == -1) k.outTangent = slope; else k.inTangent = slope; }
                    curve.MoveKey(selectedKeyIndex, k);
                }
                else if (draggingKeyIndex != -1)
                {
                    Keyframe k = curve.keys[draggingKeyIndex]; k.time = time; k.value = val;
                    if (float.IsInfinity(k.inTangent)) { k.inTangent = float.PositiveInfinity; k.outTangent = float.PositiveInfinity; }
                    else if (IsKeyLinear(k, curve, draggingKeyIndex)) { RecalculateLinearTangents(curve, draggingKeyIndex, ref k); }
                    curve.MoveKey(draggingKeyIndex, k); UpdateNeighborTangents(curve, draggingKeyIndex);
                }
                layerManager.ApplyCameraAnimation(layerManager.currentFrame); e.Use();
            }
            if (e.type == EventType.MouseUp) { draggingKeyIndex = -1; draggingHandle = 0; GUIUtility.hotControl = 0; }
        }
    }
    Vector2 GetHandleOffset(float tangent, int dir) { float len = 40f; float screenX = 1.0f * graphZoom.x; float screenY = -tangent * graphZoom.y; return new Vector2(screenX, screenY).normalized * len * dir; }
    void DrawHandles(Rect rect, AnimationCurve curve) { if (selectedKeyIndex == -1 || selectedKeyIndex >= curve.length) return; Keyframe k = curve.keys[selectedKeyIndex]; if (float.IsInfinity(k.inTangent) || float.IsInfinity(k.outTangent)) return; Vector2 keyPos = GraphToScreen(rect, k.time, k.value); Vector2 inPos = keyPos + GetHandleOffset(k.inTangent, -1); Vector2 outPos = keyPos + GetHandleOffset(k.outTangent, 1); DrawLine(keyPos, inPos, Color.gray, 1.5f); DrawLine(keyPos, outPos, Color.gray, 1.5f); DrawRectCentered(inPos, 6, Color.yellow); DrawRectCentered(outPos, 6, Color.yellow); }
    void DrawCurve(Rect rect, AnimationCurve curve, int curveIndex)
    {
        if (curve.length < 1) return;

        // Base colors for each curve
        Color c = Color.white;
        if (curveIndex == 0) c = colPitch;
        else if (curveIndex == 1) c = colYaw;
        else if (curveIndex == 2) c = colRoll;
        else if (curveIndex == 3) c = colZoom;
        else if (curveIndex == 4) c = colFish;

        // If it's the currently "selected" curve for tangent editing, keep it bright
        if (curveIndex != selectedCurveIndex) c.a = 0.6f;

        Vector2 prevPos = GraphToScreen(rect, curve.keys[0].time, curve.keys[0].value);
        float step = 3.0f / graphZoom.x;
        float endTime = curve.keys[curve.length - 1].time;
        float startT = Mathf.Max(curve.keys[0].time, (-graphPan.x / graphZoom.x));
        float endT = Mathf.Min(endTime, ((rect.width - graphPan.x) / graphZoom.x));

        for (float t = startT; t <= endT; t += step)
        {
            Vector2 pos = GraphToScreen(rect, t, curve.Evaluate(t));
            if (Mathf.Abs(pos.y - prevPos.y) < rect.height * 2f)
                DrawLine(prevPos, pos, c, 2);
            prevPos = pos;
        }

        for (int i = 0; i < curve.keys.Length; i++)
        {
            Vector2 pos = GraphToScreen(rect, curve.keys[i].time, curve.keys[i].value);
            if (pos.x >= -5 && pos.x <= rect.width + 5)
            {
                Color keyCol = GetColorForKey(curve.keys[i]);
                if (curveIndex == selectedCurveIndex && i == selectedKeyIndex) keyCol = Color.white;
                else if (curveIndex != selectedCurveIndex) keyCol.a = 0.6f;

                DrawDiamond(pos, 8, keyCol);
            }
        }
    }
    // OPTIMIZED: Use cached style
    void DrawGridWithLabels(Rect rect)
    {
        float timeStep = (graphZoom.x > 50) ? 1f : (graphZoom.x > 20 ? 5f : 10f);
        float valStep = (graphZoom.y > 20) ? 10f : (graphZoom.y > 5 ? 45f : 90f);

        // --- DRAW VERTICAL GRID LINES (Time) ---
        float startFrame = -graphPan.x / graphZoom.x; 
        float endFrame = (rect.width - graphPan.x) / graphZoom.x;
        
        for (float t = Mathf.Floor(startFrame); t < endFrame; t += timeStep) { 
            float x = graphPan.x + t * graphZoom.x; 
            // Use Direct Rect Drawing for perfect vertical lines
            DrawRect(new Rect(x, 0, 1, rect.height), gridLineColor);
            GUI.Label(new Rect(x + 2, rect.height - 20, 40, 20), t.ToString("F0"), timeRulerStyle); 
        }

        // --- DRAW HORIZONTAL GRID LINES (Value) ---
        float centerY = rect.height / 2 + graphPan.y; 
        DrawRect(new Rect(0, centerY, rect.width, 2), new Color(1, 1, 1, 0.5f)); // Horizon
        
        for (float v = -360; v <= 360; v += valStep) { 
            if (v == 0) continue; 
            float y = centerY - v * graphZoom.y; 
            if (y >= 0 && y <= rect.height) { 
                DrawRect(new Rect(0, y, rect.width, 1), gridLineColor);
                GUI.Label(new Rect(5, y - 10, 40, 20), v.ToString("F0"), timeRulerStyle); 
            } 
        }

        // --- START LINE (Cyan) ---
        float startX = graphPan.x; // Frame 0
        if (startX >= -2 && startX <= rect.width + 2) {
            // Force full height manually
            DrawRect(new Rect(startX, 0, 2, rect.height), Color.cyan);
            GUI.Label(new Rect(startX + 4, 5, 40, 20), "START", new GUIStyle(timeRulerStyle) { normal = { textColor = Color.cyan } });
        }

        // --- END LINE (Yellow) ---
        float endX = graphPan.x + (layerManager.totalFrames - 1) * graphZoom.x;

        if (endX >= -2 && endX <= rect.width + 2) {
            DrawRect(new Rect(endX, 0, 2, rect.height), Color.yellow);
            GUI.Label(new Rect(endX - 35, 5, 40, 20), "END", new GUIStyle(timeRulerStyle) { alignment = TextAnchor.MiddleRight, normal = { textColor = Color.yellow } });
        }
    }

    void DrawPlayhead(Rect rect) { float x = graphPan.x + layerManager.currentFrame * graphZoom.x; if (x >= 0 && x <= rect.width) DrawRect(new Rect(x, 0, 2, rect.height), Color.red); }
    Vector2 GraphToScreen(Rect rect, float time, float val)
    {
        // The '+ rect.height / 2' is what 'graphPan.y = centerVal * graphZoom.y' targets
        return new Vector2(
            graphPan.x + time * graphZoom.x,
            (rect.height / 2f + graphPan.y) - val * graphZoom.y
        );
    }
    void FitGraphView(CameraLayer layer) 
    { 
        float minT = float.MaxValue, maxT = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;
        bool foundData = false;

        for (int i = 0; i < 5; i++) {
            if (!visibleCurves[i]) continue;
            AnimationCurve c = GetCurveByIndex(layer, i);
            if (c == null || c.length == 0) continue;
            foundData = true;

            // 1. Get Time Bounds from Keys
            minT = Mathf.Min(minT, c.keys[0].time);
            maxT = Mathf.Max(maxT, c.keys[c.length - 1].time);

            // 2. Sample Curve for Value Bounds (Catch Overshoots)
            // We verify the value at every key...
            foreach (var k in c.keys) {
                minV = Mathf.Min(minV, k.value);
                maxV = Mathf.Max(maxV, k.value);
            }
            
            // ...AND we sample between keys to catch bezier curves that go higher/lower
            float duration = maxT - minT;
            if (duration > 0) {
                float sampleRate = Mathf.Max(0.1f, duration / 50f); // 50 samples per curve
                for (float t = minT; t <= maxT; t += sampleRate) {
                    float v = c.Evaluate(t);
                    minV = Mathf.Min(minV, v);
                    maxV = Mathf.Max(maxV, v);
                }
            }
        }

        if (!foundData) { graphPan = new Vector2(50, 0); graphZoom = new Vector2(40f, 5f); return; } 

        float timeRange = Mathf.Max(0.1f, maxT - minT); 
        float valRange = Mathf.Max(0.1f, maxV - minV); 

        // Padding
        float areaWidth = graphRect.width - 60;
        float areaHeight = graphRect.height - 120; 

        graphZoom.x = areaWidth / (timeRange * 1.1f); 
        graphZoom.y = areaHeight / (valRange * 1.2f); 

        graphPan.x = -minT * graphZoom.x + 30;
        float centerValue = (minV + maxV) / 2.0f;
        graphPan.y = centerValue * graphZoom.y; 
    }
    void DrawLine(Vector2 start, Vector2 end, Color color, float width) { if (Event.current.type != EventType.Repaint) return; Color old = GUI.color; GUI.color = color; Vector2 d = end - start; float a = Mathf.Rad2Deg * Mathf.Atan2(d.y, d.x); GUIUtility.RotateAroundPivot(a, start); GUI.DrawTexture(new Rect(start.x, start.y - width / 2, d.magnitude, width), lineTex); GUIUtility.RotateAroundPivot(-a, start); GUI.color = old; }
    void DrawRect(Rect r, Color c) { Color old = GUI.color; GUI.color = c; GUI.DrawTexture(r, lineTex); GUI.color = old; }
    void DrawRectCentered(Vector2 center, float size, Color c) { DrawRect(new Rect(center.x - size / 2, center.y - size / 2, size, size), c); }
    void DrawDiamond(Vector2 center, float size, Color c) { if (Event.current.type != EventType.Repaint) return; Color old = GUI.color; GUI.color = c; Rect r = new Rect(center.x - size / 2, center.y - size / 2, size, size); GUIUtility.RotateAroundPivot(45, center); GUI.DrawTexture(r, lineTex); GUIUtility.RotateAroundPivot(-45, center); GUI.color = old; }

    // =================================================================================================
    // 2. LAYER WINDOW
    // =================================================================================================
    void DrawLayerWindow()
    {
        layersRect.x = Mathf.Clamp(layersRect.x, 0, Screen.width - 50);
        layersRect.y = Mathf.Clamp(layersRect.y, 0, Screen.height - 50);

        layersRect = GUI.Window(0, layersRect, (id) =>
        {
            GUILayout.BeginVertical();

            // Buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+Layer")) layerManager.AddLayer(LayerType.Paint);
            if (GUILayout.Button("+Anim")) layerManager.AddLayer(LayerType.Animation);
            if (GUILayout.Button("+Folder")) layerManager.AddLayer(LayerType.Folder);
            if (GUILayout.Button("+Cam")) layerManager.AddLayer(LayerType.Camera);
            GUILayout.EndHorizontal();

            // Scroll Area
            layerScroll = GUILayout.BeginScrollView(layerScroll, GUI.skin.box);
            if (layerManager.root != null) { for (int i = layerManager.root.children.Count - 1; i >= 0; i--) DrawLayerRow(layerManager.root.children[i], 0); }
            GUILayout.EndScrollView();

            // --- OPACITY SLIDER ---
            if (layerManager.activeLayer != null && !(layerManager.activeLayer is CameraLayer))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Opacity:", GUILayout.Width(80));

                float oldOp = layerManager.activeLayer.opacity;
                float newOp = GUILayout.HorizontalSlider(oldOp, 0f, 1f);
                GUILayout.Label((newOp * 100).ToString("F0") + "%", GUILayout.Width(60));

                if (!Mathf.Approximately(oldOp, newOp))
                {
                    layerManager.activeLayer.opacity = newOp;
                    layerManager.compositionDirty = true;
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            // ----------------------

            // Layer Controls
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rename", GUILayout.Width(70))) { if (layerManager.activeLayer != null) { renamingNode = layerManager.activeLayer; renameBuffer = layerManager.activeLayer.name; } }
            if (GUILayout.Button("Delete", GUILayout.Width(60))) layerManager.DeleteActiveLayer();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▲", GUILayout.Width(30))) layerManager.MoveActiveLayerUp();
            if (GUILayout.Button("▼", GUILayout.Width(30))) layerManager.MoveActiveLayerDown();
            if (GUILayout.Button("▶", GUILayout.Width(30))) layerManager.MoveActiveLayerIn();
            if (GUILayout.Button("◀", GUILayout.Width(30))) layerManager.MoveActiveLayerOut();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }, "Layers");
    }

    void DrawLayerRow(LayerNode node, int indent)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(layerRowHeight));
        string vis = node.isVisible ? "👁" : "○";
        if (GUILayout.Button(vis, GUILayout.Width(visColWidth), GUILayout.Height(layerRowHeight))) { node.isVisible = !node.isVisible; layerManager.compositionDirty = true; }
        string typeIcon = "L"; if (node is PaintLayer) typeIcon = "P"; else if (node is CameraLayer) typeIcon = "C"; else if (node is AnimationLayer) typeIcon = "A"; else if (node is GroupLayer) typeIcon = "F";
        GUILayout.Label(typeIcon, GUILayout.Width(typeColWidth), GUILayout.Height(layerRowHeight));
        GUILayout.Space(indent * 15);
        if (node is GroupLayer) { string arrow = node.expanded ? "▼" : "►"; if (GUILayout.Button(arrow, GUILayout.Width(20), GUILayout.Height(layerRowHeight))) node.expanded = !node.expanded; } else GUILayout.Space(20);
        GUI.color = (layerManager.activeLayer == node) ? Color.green : Color.white;
        if (renamingNode == node)
        {
            renameBuffer = GUILayout.TextField(renameBuffer, GUILayout.Height(layerRowHeight));
            if (Event.current.isKey && Event.current.keyCode == KeyCode.Return) { node.name = renameBuffer; renamingNode = null; Event.current.Use(); }
        }
        else
        {
            if (GUILayout.Button(node.name, GUI.skin.label, GUILayout.Height(layerRowHeight), GUILayout.ExpandWidth(true))) { layerManager.activeLayer = node; if (node is PaintLayer) layerManager.compositionDirty = true; }
        }
        GUI.color = Color.white;
        GUILayout.EndHorizontal();
        if (node is GroupLayer group && node.expanded) { for (int i = group.children.Count - 1; i >= 0; i--) DrawLayerRow(group.children[i], indent + 1); }
    }

    // =================================================================================================
    // 3. TIMELINE WINDOW (OPTIMIZED)
    // =================================================================================================
    void DrawTimelineWindow()
    {
        timelineRect = GUI.Window(1, timelineRect, (id) =>
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();

            // Controls
            if (GUILayout.Button(layerManager.isPlaying ? "■ STOP" : "▶ PLAY", GUILayout.Width(80))) layerManager.isPlaying = !layerManager.isPlaying;
            GUILayout.Space(10);

            if (GUILayout.Button("+ Cell", GUILayout.Width(50))) layerManager.ActionAddCell();
            if (GUILayout.Button("- Cell", GUILayout.Width(50)))
            {
                // Logic to remove the cell assignment at the current frame
                AnimationLayer animLayer = null;
                if (layerManager.activeLayer is AnimationLayer al) animLayer = al;
                else if (layerManager.activeLayer != null && layerManager.activeLayer.parent is AnimationLayer pl) animLayer = pl;

                if (animLayer != null && animLayer.timelineMap.ContainsKey(layerManager.currentFrame))
                {
                    animLayer.timelineMap.Remove(layerManager.currentFrame);
                    layerManager.compositionDirty = true;
                }
            }
            GUILayout.Space(10);
            if (GUILayout.Button("+ Key", GUILayout.Width(50))) layerManager.ActionAddKeyframe();
            if (GUILayout.Button("- Key", GUILayout.Width(50))) layerManager.ActionRemoveKeyframe();
            GUILayout.Space(10);
            if (GUILayout.Button(showGraph ? "Hide Graph" : "Show Graph", GUILayout.Width(80))) { showGraph = !showGraph; fitGraphPending = true; }
            GUILayout.Space(20);

            // Stats
            GUILayout.Label($"Frame: {layerManager.currentFrame + 1}", GUILayout.Width(100));
            GUILayout.Label("Total:", GUILayout.Width(60));
            string totalStr = GUILayout.TextField(layerManager.totalFrames.ToString(), GUILayout.Width(50));
            if (int.TryParse(totalStr, out int newTotal)) layerManager.totalFrames = Mathf.Clamp(newTotal, 1, 720);

            GUILayout.Label("FPS:", GUILayout.Width(50));
            string fpsStr = GUILayout.TextField(layerManager.fps.ToString(), GUILayout.Width(30));
            if (int.TryParse(fpsStr, out int newFps)) layerManager.fps = Mathf.Clamp(newFps, 1, 120);

            // PLAYBACK FPS
            if (layerManager.isPlaying)
            {
                if (currentPlaybackFps < layerManager.fps * 0.9f)
                {
                    fpsStyle.normal.textColor = Color.red;
                    fpsStyle.fontStyle = FontStyle.Bold;
                }
                else
                {
                    fpsStyle.normal.textColor = Color.green;
                    fpsStyle.fontStyle = FontStyle.Normal;
                }
                GUILayout.Label($" (Real: {currentPlaybackFps:F1})", fpsStyle, GUILayout.Width(100));
            }
            else
            {
                fpsStyle.normal.textColor = Color.gray;
                fpsStyle.fontStyle = FontStyle.Normal;
                GUILayout.Label(" (Real: ---)", fpsStyle, GUILayout.Width(100));
            }

            layerManager.loop = GUILayout.Toggle(layerManager.loop, "Loop");

            // Compact onion quick controls
            if (layerManager != null)
            {
                GUILayout.Label($"B:{layerManager.onionBefore}", GUILayout.Width(36));
                if (GUILayout.Button("-", GUILayout.Width(20))) { layerManager.onionBefore = Mathf.Max(0, layerManager.onionBefore - 1); layerManager.compositionDirty = true; }
                if (GUILayout.Button("+", GUILayout.Width(20))) { layerManager.onionBefore = Mathf.Min(5, layerManager.onionBefore + 1); layerManager.compositionDirty = true; }
                GUILayout.Space(20);

                GUILayout.Label($"A:{layerManager.onionAfter}", GUILayout.Width(36));
                if (GUILayout.Button("-", GUILayout.Width(20))) { layerManager.onionAfter = Mathf.Max(0, layerManager.onionAfter - 1); layerManager.compositionDirty = true; }
                if (GUILayout.Button("+", GUILayout.Width(20))) { layerManager.onionAfter = Mathf.Min(5, layerManager.onionAfter + 1); layerManager.compositionDirty = true; }
                GUILayout.Space(20);

                GUILayout.Label($"Opacity:{layerManager.onionOpacity:F2}", GUILayout.Width(130));
                float prevOpacity = layerManager.onionOpacity;
                layerManager.onionOpacity = GUILayout.HorizontalSlider(layerManager.onionOpacity, 0f, 1f, GUILayout.Width(120));
                if (!Mathf.Approximately(prevOpacity, layerManager.onionOpacity)) layerManager.compositionDirty = true;
                GUILayout.Space(20);

                bool prevOnion = layerManager.onionEnabled;
                layerManager.onionEnabled = GUILayout.Toggle(layerManager.onionEnabled, "Onion");
                if (prevOnion != layerManager.onionEnabled) layerManager.compositionDirty = true;
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Scrollbar
            timelineScroll = GUILayout.BeginScrollView(timelineScroll, false, true, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, GUI.skin.box);

            float totalContentWidth = layerNameColWidth + (layerManager.totalFrames * cellWidth) + 50;
            GUILayout.BeginHorizontal(GUILayout.Width(totalContentWidth));
            GUILayout.BeginVertical();

            // Time Ruler (OPTIMIZED: Uses cached style)
            GUILayout.BeginHorizontal();
            GUILayout.Box("", GUIStyle.none, GUILayout.Width(layerNameColWidth), GUILayout.Height(15));
            for (int f = 0; f < layerManager.totalFrames; f++)
            {
                if (f % layerManager.fps == 0) GUILayout.Label((f / layerManager.fps) + "s", timeRulerStyle, GUILayout.Width(cellWidth));
                else GUILayout.Label("", GUILayout.Width(cellWidth));
            }
            GUILayout.EndHorizontal();

            // Header Frame Numbers
            GUILayout.BeginHorizontal();
            GUILayout.Box("", GUIStyle.none, GUILayout.Width(layerNameColWidth), GUILayout.Height(timelineRowHeight));

            for (int f = 0; f < layerManager.totalFrames; f++)
            {
                float framesPerQuarter = layerManager.fps / 4.0f;
                int quarterIndex = Mathf.FloorToInt(f / framesPerQuarter);
                bool isAlt = (quarterIndex % 2 == 0);

                if (f == layerManager.currentFrame) GUI.backgroundColor = Color.red;
                else GUI.backgroundColor = isAlt ? gridColA : gridColB;

                if (GUILayout.Button((f + 1).ToString(), GUILayout.Width(cellWidth))) { layerManager.currentFrame = f; layerManager.isPlaying = false; layerManager.StepFrame(0); }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            DrawTimelineRecursively(layerManager.root);

            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }, "Timeline");
    }

    void DrawTimelineRecursively(LayerNode node)
    {
        if (node is AnimationLayer || node is CameraLayer) DrawOneTimelineRow(node);
        if (node is GroupLayer group) foreach (var child in group.children) DrawTimelineRecursively(child);
    }

    void DrawOneTimelineRow(LayerNode node)
    {
        GUILayout.BeginHorizontal(GUILayout.Height(timelineRowHeight));
        string prefix = (node is CameraLayer) ? "[CAM] " : "";
        if (layerManager.activeLayer == node) GUI.color = Color.green;
        if (GUILayout.Button(prefix + node.name, GUI.skin.label, GUILayout.Width(layerNameColWidth), GUILayout.Height(timelineRowHeight))) layerManager.activeLayer = node;
        GUI.color = Color.white;

        AnimationLayer animLayer = node as AnimationLayer; CameraLayer camLayer = node as CameraLayer; bool isCam = (camLayer != null);

        for (int f = 0; f < layerManager.totalFrames; f++)
        {
            float framesPerQuarter = layerManager.fps / 4.0f;
            int quarterIndex = Mathf.FloorToInt(f / framesPerQuarter);
            bool isAlt = (quarterIndex % 2 == 0);
            Color bgCol = isAlt ? gridColA : gridColB;

            string label = "";
            if (isCam)
            {
                if (camLayer.HasKeyframe(f))
                {
                    label = "♦";
                    for (int k = 0; k < camLayer.curvePitch.keys.Length; k++)
                    {
                        if (Mathf.Approximately(camLayer.curvePitch.keys[k].time, f))
                        {
                            bgCol = GetColorForKey(camLayer.curvePitch.keys[k]);
                            break;
                        }
                    }
                }
            }
            else
            {
                if (animLayer.timelineMap.ContainsKey(f)) { label = (animLayer.timelineMap[f] + 1).ToString(); bgCol = new Color(0.8f, 0.8f, 0.8f); }
                else if (animLayer.GetActiveCellIndex(f) != -1) label = ".";
            }

            if (f == layerManager.currentFrame) bgCol = cellHighlightBlue;

            GUI.backgroundColor = bgCol;

            if (GUILayout.Button(label, GUILayout.Width(cellWidth), GUILayout.Height(timelineRowHeight)))
            {
                layerManager.currentFrame = f; layerManager.activeLayer = node;
                if (!isCam && animLayer.children.Count > 0)
                {
                    int currentIdx = animLayer.timelineMap.ContainsKey(f) ? animLayer.timelineMap[f] : -1;
                    int nextIdx = currentIdx + 1;
                    if (nextIdx >= animLayer.children.Count) animLayer.timelineMap.Remove(f); else animLayer.SetCell(f, nextIdx);
                }
                layerManager.StepFrame(0);
            }
            GUI.backgroundColor = Color.white;
        }
        GUILayout.EndHorizontal();
    }
}
