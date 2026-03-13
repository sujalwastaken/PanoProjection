using UnityEngine;
using System.Collections.Generic;
using System.IO;

[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(PanoramaProjectionEffect))]
public class PanoramaPaintGPU : MonoBehaviour
{
    [Header("Shader References")]
    public Shader brushShader;
    public Shader eraserShader;
    public Shader gridCompositeShader;
    public Shader sphereBrushShader; // Ensure this is set to "Hidden/PanoramaBrush"
    private Material sphereBrushMat;

    [Header("Interaction Settings")]
    public float rotateSpeed = 2.0f;
    public float zoomSpeed = 2.0f;
    public Color drawColor = Color.red;

    [Header("Cursor Settings")]
    [Tooltip("If left null, a triangle will be generated automatically.")]
    public Texture2D customPenCursor; 
    public Vector2 cursorHotspot = Vector2.zero;

    [Header("Perspective Ruler")]
    [Tooltip("Toggle with 'S'")]
    public bool enableSnapping = false;
    [Tooltip("Toggle with 'F'")]
    public bool useDiagonalSnapping = false;
    [Tooltip("Toggle with 'G'")]
    public bool showGrid = false;

    public Vector3 gridRotation = Vector3.zero;

    [Header("Grid Visualization")]
    [Range(2.0f, 45.0f)] public float gridSpacing = 10.0f;
    [Range(0.1f, 5.0f)] public float gridThickness = 1.0f;
    [Range(0.0f, 1.0f)] public float gridOpacity = 0.5f;

    public Color gridColorX = new Color(1, 0, 0, 1.0f);
    public Color gridColorY = new Color(0, 1, 0, 1.0f);
    public Color gridColorZ = new Color(0, 0, 1, 1.0f);

    // Track previous values to detect changes
    private float lastGridSpacing;
    private float lastGridThickness;
    private float lastGridOpacity;
    private Color lastGridColorX;
    private Color lastGridColorY;
    private Color lastGridColorZ;
    private Vector3 lastGridRotation;
    private bool lastShowGrid;
    private bool lastDiagonalMode;
    private bool isSnappingActive = false;

    [Header("Snapping")]
    public float rulerLockThreshold = 0.005f;

    [Header("Brush Settings")]
    [Range(1, 200)] public float brushSize = 50.0f;
    [Range(1, 200)] public float eraserSize = 100.0f;
    [Range(0.01f, 0.5f)] public float brushSpacing = 0.05f;
    [Range(0.0f, 1.0f)] public float hardness = 0.8f;
    [Range(0.0f, 1.0f)] public float brushOpacity = 1.0f;

    [Tooltip("Scales brush size with FOV to keep line width consistent on screen.")]
    public bool useSmartBrush = false; 
    private const float REFERENCE_FOV = 90.0f; // The baseline FOV

    [Header("Undo Settings")]
    [Range(1, 20)] public int maxUndoSteps = 10;

    [HideInInspector] public bool isEraser = false;
    [HideInInspector] public bool isEyedropper = false;
    private bool wasEraser = false; // To track state changes

    // Color history
    [HideInInspector] public List<Color> recentColors = new List<Color>();
    [HideInInspector] public int maxRecentColors = 12;
    private Color lastStrokeColor;

    private Camera cam;
    private PanoramaProjectionEffect projectionEffect;

    // Paint canvas + composite display (replaces targetTexture / layerManager)
    private RenderTexture overlayTexture;
    private RenderTexture displayTexture;

    // Undo / Redo
    private List<RenderTexture> undoHistory = new List<RenderTexture>();
    private List<RenderTexture> redoHistory = new List<RenderTexture>();

    private Texture2D brushTexture;
    private Material brushMaterial;
    private Material eraserMaterial;
    private Material gridCompositeMat;

    private bool strokeInProgress = false;

    private Vector2? lastUvPos = null;
    private Vector3 lastMousePos;
    private const float sensitivityMultiplier = 0.1f;
    private const float TWO_PI = Mathf.PI * 2.0f;

    private float camPitch = 0f;
    private float camYaw = 0f;
    private float camRoll = 0f;

    // Grid display texture
    // (displayTexture declared above, alongside overlayTexture)

    // Ruler state
    private Vector3 strokeStartP3D;
    private int lockedAxis = -1; // -1: None, 0-2: Primary, 10-15: Diagonals
    private Vector3 activeSnapNormal = Vector3.zero;

    // Line tool state (Alt key)
    private bool lineToolActive = false;
    private Vector2 lineStartUV;
    private Vector3 lineStartP3D;
    private RenderTexture linePreviewRT;

    void Start()
    {
        cam = GetComponent<Camera>();
        projectionEffect = GetComponent<PanoramaProjectionEffect>();

        if (brushShader == null) brushShader = Shader.Find("Sprites/Default");
        if (eraserShader == null) eraserShader = Shader.Find("Hidden/PanoramaEraser");
        if (gridCompositeShader == null) gridCompositeShader = Shader.Find("Hidden/PanoramaGridComposite");
        if (sphereBrushShader == null) sphereBrushShader = Shader.Find("Hidden/PanoramaBrush");

        SyncCameraFromTransform();
        InitializeGPUResources();
        
        // --- CURSOR GENERATION ---
        if (customPenCursor == null)
        {
            customPenCursor = GenerateTriangleCursor();
        }
        UpdateHardwareCursor(); // Set initial cursor
        // -------------------------

        // Initialize grid tracking values
        lastGridSpacing = gridSpacing;
        lastGridThickness = gridThickness;
        lastGridOpacity = gridOpacity;
        lastGridColorX = gridColorX;
        lastGridColorY = gridColorY;
        lastGridColorZ = gridColorZ;
        lastGridRotation = gridRotation;
        lastShowGrid = showGrid;
        lastDiagonalMode = useDiagonalSnapping;
    }

    void OnDestroy()
    {
        CleanupList(undoHistory);
        CleanupList(redoHistory);
        if (overlayTexture != null) overlayTexture.Release();
        if (displayTexture != null) displayTexture.Release();
        if (linePreviewRT != null) linePreviewRT.Release();
        // Reset cursor on exit
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void CleanupList(List<RenderTexture> list)
    {
        foreach (var rt in list) if (rt != null) { rt.Release(); Destroy(rt); }
        list.Clear();
    }

    void InitializeGPUResources()
    {
        if (projectionEffect.panoramaTexture == null) return;
        Texture source = projectionEffect.panoramaTexture;

        // Paint canvas
        if (overlayTexture != null) overlayTexture.Release();
        overlayTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        overlayTexture.enableRandomWrite = true;
        overlayTexture.Create();
        ClearRenderTexture(overlayTexture);

        // Composite display
        if (displayTexture != null) displayTexture.Release();
        displayTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        displayTexture.Create();

        projectionEffect.overlayTexture = displayTexture;

        if (sphereBrushShader != null)
        {
            sphereBrushMat = new Material(sphereBrushShader);
        }
        GenerateBrushTexture();
        if (brushShader != null)
        {
            brushMaterial = new Material(brushShader);
            brushMaterial.mainTexture = brushTexture;
        }
        if (eraserShader != null)
        {
            eraserMaterial = new Material(eraserShader);
            eraserMaterial.mainTexture = brushTexture;
        }
        if (gridCompositeShader != null)
        {
            gridCompositeMat = new Material(gridCompositeShader);
        }
    }

    void ClearRenderTexture(RenderTexture rt)
    {
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.clear);
        RenderTexture.active = null;
    }

    void Update()
    {
        bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool isAlt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        if (isCtrl && Input.GetKeyDown(KeyCode.S)) { SavePanorama(); return; }
        if (isCtrl && Input.GetKeyDown(KeyCode.D)) { LoadPanorama(); return; }

        HandleToolInput();

        isSnappingActive = false;
        // --- UPDATE CURSOR STATE ---
        // Only update if the tool changed or we entered/exited UI mode (implied by targetTexture null/not null check potentially)
        if (isEraser != wasEraser)
        {
            UpdateHardwareCursor();
            wasEraser = isEraser;
        }
        // ---------------------------

        // Cursor logic (Projected Brush Preview)
        Vector2 screenPos = Input.mousePosition;
        Vector2 screenUV = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        Vector3 currentP3D = ScreenPointToSphereVector(screenUV);

        bool isSpace = Input.GetKey(KeyCode.Space);
        bool isClick = Input.GetMouseButton(0);
        bool isMMB = Input.GetMouseButton(2); // Middle mouse button (Blender style)
        bool isPainting = isClick && !isSpace && !isCtrl && !isEyedropper;

        // Snapping logic
        bool shouldSnap = enableSnapping || isShift;

        if (shouldSnap && isPainting && !isAlt)
        {
            if (Input.GetMouseButtonDown(0))
            {
                strokeStartP3D = currentP3D;
                lockedAxis = -1;
            }
            currentP3D = ApplyPerspectiveRuler(currentP3D);
            if (lockedAxis != -1)
            {
                isSnappingActive = true;
            }
        }
        else
        {
            // Reset locked axis when not painting
            if (lockedAxis != -1)
            {
                lockedAxis = -1;
            }
        }

        Vector2 currentCursorUV = SphereVectorToUV(currentP3D);
        
        // Drawing the 3D projected brush ring
        if (GUIUtility.hotControl == 0 && overlayTexture != null)
        {
            float currentSize = isEraser ? eraserSize : brushSize;
            Color cursorCol = isEraser ? Color.black : new Color(drawColor.r, drawColor.g, drawColor.b, 1.0f);
            float effectiveSize = GetEffectiveBrushSize(currentSize);
            float radiusUV = (effectiveSize / overlayTexture.height) * 0.5f;
            projectionEffect.UpdateCursor(currentCursorUV, radiusUV, cursorCol);
            
            // Re-enforce hardware cursor in case UI overrode it
            if (!isEraser && Cursor.visible) UpdateHardwareCursor(); 
        }
        else
        {
            projectionEffect.HideCursor();
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        if (GUIUtility.hotControl != 0)
        {
            UpdateGridDisplay();
            return;
        }

        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) lastMousePos = Input.mousePosition;

        // Blender-style: MMB = orbit, or Space+LMB = orbit (fallback)
        bool isNavigation = isMMB || (isSpace && isClick);

        if (isNavigation)
        {
            Vector3 currentPos = Input.mousePosition;
            Vector3 delta = currentPos - lastMousePos;
            float t = projectionEffect.perspective / 100.0f;
            float damping = Mathf.Lerp(0.1f, 1.0f, t);
            float dx = delta.x * sensitivityMultiplier;
            float dy = delta.y * sensitivityMultiplier;

            if (isCtrl)
            {
                float proportionalStep = projectionEffect.perspective * 0.05f;
                float zoomChange = dx * zoomSpeed * proportionalStep;
                projectionEffect.perspective = Mathf.Clamp(projectionEffect.perspective - zoomChange, 1f, 100f);
            }
            else if (isShift)
            {
                camRoll -= dx * rotateSpeed * damping;
                ApplyRotation();
            }
            else
            {
                camYaw -= dx * rotateSpeed * damping;
                camPitch -= dy * rotateSpeed * damping;
                ApplyRotation();
            }
            lastUvPos = null;
        }
        else if (isPainting)
        {
            if (!strokeInProgress)
            {
                SaveUndoState();
                strokeInProgress = true;
                CleanupList(redoHistory);
            }

            // Line tool mode with Alt
            if (isAlt)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    lineToolActive = true;
                    
                    // --- SNAP START POINT ---
                    lockedAxis = -1; 
                    strokeStartP3D = currentP3D;
                    
                    if (enableSnapping || isShift)
                    {
                        lineStartP3D = ApplyPerspectiveRuler(currentP3D);
                        // FIX: If start point snapped, show the ghost line immediately
                        if (lockedAxis != -1) isSnappingActive = true; 
                    }
                    else
                    {
                        lineStartP3D = currentP3D;
                    }
                    
                    lineStartUV = SphereVectorToUV(lineStartP3D);
                    // ------------------------
                    
                    if (linePreviewRT == null || linePreviewRT.width != overlayTexture.width || linePreviewRT.height != overlayTexture.height)
                    {
                        if (linePreviewRT != null) linePreviewRT.Release();
                        linePreviewRT = new RenderTexture(overlayTexture.width, overlayTexture.height, 0, RenderTextureFormat.ARGB32);
                        linePreviewRT.Create();
                    }
                    Graphics.Blit(overlayTexture, linePreviewRT);
                }
                
                // Show preview while dragging
                if (lineToolActive)
                {
                    Graphics.Blit(linePreviewRT, overlayTexture);
                    
                    Vector3 endP3D = currentP3D;
                    
                    if (enableSnapping || isShift)
                    {
                        strokeStartP3D = lineStartP3D; 
                        endP3D = ApplyPerspectiveRuler(currentP3D);

                        // --- FIX: TELL SHADER WE ARE SNAPPING ---
                        if (lockedAxis != -1)
                        {
                            isSnappingActive = true;
                        }
                        // ----------------------------------------
                    }

                    DrawLineStraight(lineStartP3D, endP3D);
                }
            }
            else
            {
                // Normal brush behavior
                if (lastUvPos == null) lastUvPos = currentCursorUV;
                PaintStroke(lastUvPos.Value, currentCursorUV);
                lastUvPos = currentCursorUV;
            }
        }
        else if (isEyedropper && isClick)
        {
            // Eyedropper: sample color from panorama at current UV
            SampleColorAtUV(currentCursorUV);
            isEyedropper = false;
            isEraser = false;
        }
        else
        {
            // Mouse released
            lineToolActive = false;
            lastUvPos = null;
            if (strokeInProgress)
            {
                // Track color history when stroke ends
                AddToColorHistory(drawColor);
            }
            strokeInProgress = false;
        }

        if (isClick || isMMB) lastMousePos = Input.mousePosition;

        UpdateGridDisplay();
    }

    // --- Undo / Redo ---
    void SaveUndoState()
    {
        RenderTexture snapshot = new RenderTexture(overlayTexture.descriptor);
        snapshot.Create();
        Graphics.CopyTexture(overlayTexture, snapshot);
        undoHistory.Add(snapshot);
        if (undoHistory.Count > maxUndoSteps)
        {
            RenderTexture old = undoHistory[0];
            undoHistory.RemoveAt(0);
            old.Release();
            Destroy(old);
        }
    }

    void PerformUndo()
    {
        if (undoHistory.Count == 0) return;
        RenderTexture redoSnap = new RenderTexture(overlayTexture.descriptor);
        Graphics.CopyTexture(overlayTexture, redoSnap);
        redoHistory.Add(redoSnap);
        RenderTexture last = undoHistory[undoHistory.Count - 1];
        Graphics.CopyTexture(last, overlayTexture);
        undoHistory.RemoveAt(undoHistory.Count - 1);
        last.Release();
        Destroy(last);
    }

    void PerformRedo()
    {
        if (redoHistory.Count == 0) return;
        RenderTexture undoSnap = new RenderTexture(overlayTexture.descriptor);
        Graphics.CopyTexture(overlayTexture, undoSnap);
        undoHistory.Add(undoSnap);
        RenderTexture last = redoHistory[redoHistory.Count - 1];
        Graphics.CopyTexture(last, overlayTexture);
        redoHistory.RemoveAt(redoHistory.Count - 1);
        last.Release();
        Destroy(last);
    }

    // --- NEW: Hardware Cursor Logic ---
    void UpdateHardwareCursor()
    {
        if (isEraser)
        {
            // Reset to default arrow or specific eraser cursor if you have one
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        else
        {
            // Set custom triangle cursor
            // Hotspot is usually top-left (0,0) for this type of arrow
            Cursor.SetCursor(customPenCursor, cursorHotspot, CursorMode.Auto);
        }
    }

    Texture2D GenerateTriangleCursor()
    {
        int s = 32;
        Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        Color[] c = new Color[s * s];
        for (int i = 0; i < c.Length; i++) c[i] = Color.clear;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++) {
                float u = x, v = s - 1 - y;
                if (u < 18 && v < 18 && v >= u * 0.7f && u >= v * 0.7f && u + v < 25)
                    c[y * s + x] = Color.black;
            }
        // White border
        Color[] bc = (Color[])c.Clone();
        for (int y = 1; y < s - 1; y++)
            for (int x = 1; x < s - 1; x++)
                if (c[y*s+x].a > 0.9f)
                    foreach (var d in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                        if (bc[(y+d.Item2)*s+(x+d.Item1)].a == 0) bc[(y+d.Item2)*s+(x+d.Item1)] = Color.white;
        for (int i = 0; i < c.Length; i++) if (c[i].a > 0) bc[i] = c[i];
        tex.SetPixels(bc);
        tex.Apply();
        return tex;
    }

    void HandleToolInput()
    {
        bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Q)) { isEraser = false; isEyedropper = false; }
        if (Input.GetKeyDown(KeyCode.E)) { isEraser = true; isEyedropper = false; }
        if (Input.GetKeyDown(KeyCode.I)) { isEyedropper = true; isEraser = false; }

        if (Input.GetKeyDown(KeyCode.G))
        {
            if (isShift) gridRotation = cam.transform.eulerAngles;
            else showGrid = !showGrid;
        }

        if (Input.GetKeyDown(KeyCode.S) && !isCtrl)
        {
            enableSnapping = !enableSnapping;
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            useDiagonalSnapping = !useDiagonalSnapping;
        }

        if (Input.GetKeyDown(KeyCode.Z) && isCtrl)
        {
            if (isShift) PerformRedo();
            else PerformUndo();
        }

        // --- NUMPAD VIEW PRESETS (Blender-style) ---
        if (Input.GetKeyDown(KeyCode.Keypad1) || Input.GetKeyDown(KeyCode.Alpha1) && isCtrl)
        {
            SetView(0, 0, 0); // Front
        }
        if (Input.GetKeyDown(KeyCode.Keypad3) || Input.GetKeyDown(KeyCode.Alpha3) && isCtrl)
        {
            SetView(0, 90, 0); // Right
        }
        if (Input.GetKeyDown(KeyCode.Keypad7) || Input.GetKeyDown(KeyCode.Alpha7) && isCtrl)
        {
            SetView(90, 0, 0); // Top
        }
        if (Input.GetKeyDown(KeyCode.Keypad5))
        {
            ResetView(); // Full reset
        }
        if (Input.GetKeyDown(KeyCode.Home))
        {
            ResetView();
        }
    }

    // --- VIEW PRESETS ---
    public void SetView(float pitch, float yaw, float roll)
    {
        camPitch = pitch;
        camYaw = yaw;
        camRoll = roll;
        ApplyRotation();
    }

    public void ResetView()
    {
        camPitch = 0;
        camYaw = 0;
        camRoll = 0;
        projectionEffect.perspective = 50f;
        ApplyRotation();
    }

    // --- EYEDROPPER ---
    void SampleColorAtUV(Vector2 uv)
    {
        Texture source = projectionEffect.panoramaTexture;
        if (source == null) return;

        // Read from the panorama texture
        if (source is Texture2D tex2D)
        {
            int px = Mathf.Clamp(Mathf.FloorToInt(uv.x * tex2D.width), 0, tex2D.width - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt((1f - uv.y) * tex2D.height), 0, tex2D.height - 1);
            drawColor = tex2D.GetPixel(px, py);
            drawColor.a = 1f;
            AddToColorHistory(drawColor);
        }
        else if (source is RenderTexture rt)
        {
            RenderTexture.active = rt;
            Texture2D tmp = new Texture2D(1, 1, TextureFormat.RGB24, false);
            int px = Mathf.Clamp(Mathf.FloorToInt(uv.x * rt.width), 0, rt.width - 1);
            int py = Mathf.Clamp(Mathf.FloorToInt((1f - uv.y) * rt.height), 0, rt.height - 1);
            tmp.ReadPixels(new Rect(px, py, 1, 1), 0, 0);
            tmp.Apply();
            drawColor = tmp.GetPixel(0, 0);
            drawColor.a = 1f;
            RenderTexture.active = null;
            Destroy(tmp);
            AddToColorHistory(drawColor);
        }
    }

    // --- COLOR HISTORY ---
    void AddToColorHistory(Color col)
    {
        // Don't add duplicates of the most recent
        if (recentColors.Count > 0)
        {
            Color last = recentColors[0];
            if (Mathf.Abs(last.r - col.r) < 0.01f && Mathf.Abs(last.g - col.g) < 0.01f && Mathf.Abs(last.b - col.b) < 0.01f)
                return;
        }
        recentColors.Insert(0, col);
        if (recentColors.Count > maxRecentColors)
            recentColors.RemoveAt(recentColors.Count - 1);
    }

    bool HasGridSettingsChanged()
    {
        bool changed = false;

        if (gridSpacing != lastGridSpacing) { lastGridSpacing = gridSpacing; changed = true; }
        if (gridThickness != lastGridThickness) { lastGridThickness = gridThickness; changed = true; }
        if (gridOpacity != lastGridOpacity) { lastGridOpacity = gridOpacity; changed = true; }
        if (gridColorX != lastGridColorX) { lastGridColorX = gridColorX; changed = true; }
        if (gridColorY != lastGridColorY) { lastGridColorY = gridColorY; changed = true; }
        if (gridColorZ != lastGridColorZ) { lastGridColorZ = gridColorZ; changed = true; }
        if (gridRotation != lastGridRotation) { lastGridRotation = gridRotation; changed = true; }
        if (showGrid != lastShowGrid) { lastShowGrid = showGrid; changed = true; }
        if (useDiagonalSnapping != lastDiagonalMode) { lastDiagonalMode = useDiagonalSnapping; changed = true; }

        return changed;
    }

    void UpdateGridDisplay()
    {
        if (gridCompositeMat == null || overlayTexture == null || displayTexture == null) return;

        gridCompositeMat.SetFloat("_UseGrid", showGrid ? 1.0f : 0.0f);
        // Pass diagonal mode to shader
        gridCompositeMat.SetFloat("_ShowDiagonals", (useDiagonalSnapping || showGrid) ? 1.0f : 0.0f);

        float axisToSend = isSnappingActive ? (float)lockedAxis : -1.0f;
        
        gridCompositeMat.SetFloat("_ActiveAxis", axisToSend);

        if (isSnappingActive)
        {
            // Transform ghost normal to grid-local space
            Quaternion gridRot = Quaternion.Euler(gridRotation);
            Vector3 localNormal = Quaternion.Inverse(gridRot) * activeSnapNormal;
            gridCompositeMat.SetVector("_GhostNormal", new Vector4(localNormal.x, localNormal.y, localNormal.z, 1.0f));
        }
        else
        {
            gridCompositeMat.SetVector("_GhostNormal", Vector4.zero);
        }

        // Always upload — markers and ghost line need rotation matrix even when grid is hidden
        gridCompositeMat.SetColor("_ColorX", gridColorX);
        gridCompositeMat.SetColor("_ColorY", gridColorY);
        gridCompositeMat.SetColor("_ColorZ", gridColorZ);
        gridCompositeMat.SetFloat("_Spacing", gridSpacing);
        gridCompositeMat.SetFloat("_Thickness", gridThickness);
        gridCompositeMat.SetFloat("_Opacity", gridOpacity);

        Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Euler(gridRotation));
        gridCompositeMat.SetVector("_Rot0", m.GetRow(0));
        gridCompositeMat.SetVector("_Rot1", m.GetRow(1));
        gridCompositeMat.SetVector("_Rot2", m.GetRow(2));

        Graphics.Blit(overlayTexture, displayTexture, gridCompositeMat);

        if (projectionEffect.overlayTexture != displayTexture)
            projectionEffect.overlayTexture = displayTexture;
    }

    // --- UPDATED PERSPECTIVE RULER WITH DIAGONAL SUPPORT ---
    Vector3 ApplyPerspectiveRuler(Vector3 currentP)
    {
        Quaternion gridRot = Quaternion.Euler(gridRotation);
        
        // Define Vanishing Points (Normal Axes)
        Vector3[] primaryVPs = new Vector3[] 
        {
            gridRot * Vector3.right,   // X
            gridRot * Vector3.up,      // Y
            gridRot * Vector3.forward  // Z
        };

        // Define Vanishing Points (Diagonal Axes - 6 Axes, 12 intersections)
        // Midpoints on great circles (XY, XZ, YZ)
        Vector3[] diagonalVPs = new Vector3[]
        {
            gridRot * new Vector3(1, 1, 0).normalized,  // XY+
            gridRot * new Vector3(-1, 1, 0).normalized, // XY-
            gridRot * new Vector3(1, 0, 1).normalized,  // XZ+
            gridRot * new Vector3(-1, 0, 1).normalized, // XZ-
            gridRot * new Vector3(0, 1, 1).normalized,  // YZ+
            gridRot * new Vector3(0, -1, 1).normalized  // YZ-
        };

        Vector3[] targetAxes = useDiagonalSnapping ? diagonalVPs : primaryVPs;
        int startIndex = useDiagonalSnapping ? 10 : 0; // Just an offset for lockedAxis ID

        if (lockedAxis == -1)
        {
            float dist = Vector3.Distance(currentP, strokeStartP3D);
            if (dist > rulerLockThreshold)
            {
                Vector3 strokeDir = (currentP - strokeStartP3D).normalized;
                
                float bestDot = -1.0f;
                int bestIndex = -1;

                // Iterate through available axes to find best alignment
                for(int i=0; i<targetAxes.Length; i++)
                {
                    Vector3 vp = targetAxes[i];
                    Vector3 planeNormal = Vector3.Cross(strokeStartP3D, vp).normalized;
                    Vector3 tangent = Vector3.Cross(planeNormal, strokeStartP3D).normalized;
                    
                    float dot = Mathf.Abs(Vector3.Dot(strokeDir, tangent));
                    if(dot > bestDot)
                    {
                        bestDot = dot;
                        bestIndex = i;
                    }
                }
                
                lockedAxis = startIndex + bestIndex;
            }
            else return currentP;
        }

        int index = lockedAxis - startIndex;
        if(index < 0 || index >= targetAxes.Length) index = 0; // Fallback

        Vector3 targetVP = targetAxes[index];
        Vector3 finalNormal = Vector3.Cross(strokeStartP3D, targetVP).normalized;
        activeSnapNormal = finalNormal;
        return Vector3.ProjectOnPlane(currentP, finalNormal).normalized;
    }

    Vector3 ScreenPointToSphereVector(Vector2 screenUV)
    {
        Vector2 coord = (screenUV - new Vector2(0.5f, 0.5f)) * 2.0f;
        coord.x *= cam.aspect;
        float r = coord.magnitude;
        Vector3 rayDir = new Vector3(0, 0, 1);
        if (r > 0.0001f)
        {
            float fisheyeAmt = projectionEffect.fisheyePerspective / 100.0f;
            float t = (projectionEffect.perspective - 1.0f) / 99.0f;
            float minScale = Mathf.Tan(projectionEffect.minFov * 0.5f * Mathf.Deg2Rad);
            float maxScale = Mathf.Tan(projectionEffect.maxFov * 0.5f * Mathf.Deg2Rad);
            float perspScale = Mathf.Lerp(minScale, maxScale, t);
            float scaledR = r * perspScale;
            float theta = Mathf.Lerp(Mathf.Atan(scaledR), 2.0f * Mathf.Atan(scaledR), fisheyeAmt * fisheyeAmt * (3.0f - 2.0f * fisheyeAmt));
            float sinTheta = Mathf.Sin(theta);
            float cosTheta = Mathf.Cos(theta);
            Vector2 dir2D = coord / r;
            rayDir.x = dir2D.x * sinTheta;
            rayDir.y = -dir2D.y * sinTheta;
            rayDir.z = cosTheta;
        }
        return (cam.transform.rotation * rayDir).normalized;
    }

    Vector2 SphereVectorToUV(Vector3 p)
    {
        float lon = Mathf.Atan2(p.x, p.z);
        float lat = Mathf.Asin(Mathf.Clamp(p.y, -1.0f, 1.0f));
        Vector2 uv;
        uv.x = Mathf.Repeat(lon / TWO_PI + 0.5f, 1.0f);
        uv.y = Mathf.Clamp01(0.5f - lat / Mathf.PI);
        return uv;
    }

    public void SyncCameraFromTransform()
    {
        Vector3 rot = cam.transform.eulerAngles;
        camPitch = rot.x;
        camYaw = rot.y;
        camRoll = rot.z;
        if (camPitch > 180) camPitch -= 360;
    }

    void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(camPitch, camYaw, camRoll);
    }

    public void SavePanorama()
    {
        Texture source = projectionEffect.panoramaTexture;
        if (source == null || overlayTexture == null) return;
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string defaultName = $"Panorama_{timestamp}.jpg";
        string path = SimpleFileBrowser.SaveFile("Save Panorama", defaultName, "jpg");

        if (string.IsNullOrEmpty(path)) return;

        RenderTexture bakedRT = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, bakedRT);
        Material compositor = new Material(Shader.Find("Sprites/Default"));

        Graphics.Blit(overlayTexture, bakedRT, compositor);

        Texture2D resultTex = new Texture2D(source.width, source.height, TextureFormat.RGB24, false);
        RenderTexture.active = bakedRT;
        resultTex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        resultTex.Apply();
        RenderTexture.active = null;

        byte[] bytes = resultTex.EncodeToJPG(90);
        if (!path.ToLower().EndsWith(".jpg") && !path.ToLower().EndsWith(".jpeg")) path += ".jpg";
        File.WriteAllBytes(path, bytes);

        RenderTexture.ReleaseTemporary(bakedRT);
        Destroy(resultTex);
        Destroy(compositor);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }

    public void LoadPanorama()
    {
        string path = SimpleFileBrowser.OpenFile("Load Panorama");
        if (string.IsNullOrEmpty(path)) return;

        if (File.Exists(path))
        {
            float keptPerspective = projectionEffect.perspective;
            float keptFisheye = projectionEffect.fisheyePerspective;
            Quaternion keptRotation = transform.rotation;

            byte[] bytes = File.ReadAllBytes(path);
            Texture2D newPano = new Texture2D(2, 2);
            if (newPano.LoadImage(bytes))
            {
                projectionEffect.panoramaTexture = newPano;
                InitializeGPUResources();
                CleanupList(undoHistory);
                CleanupList(redoHistory);
            }

            projectionEffect.perspective = keptPerspective;
            projectionEffect.fisheyePerspective = keptFisheye;
            transform.rotation = keptRotation;
        }
    }
    
    // Shared bounding box drawing (used by PaintStroke and DrawLineStraight)
    void DrawBrushQuad(Vector2 uv, float radiusUV)
    {
        float lat = (uv.y - 0.5f) * Mathf.PI;
        float yMin = uv.y - radiusUV, yMax = uv.y + radiusUV;
        if (Mathf.Abs(lat) > 1.2f) {
            DrawQuad(0, 1, yMin, yMax);
        } else {
            float xR = radiusUV * 0.5f / Mathf.Cos(lat);
            float xMin = uv.x - xR, xMax = uv.x + xR;
            DrawQuad(xMin, xMax, yMin, yMax);
            if (xMin < 0) DrawQuad(xMin+1, xMax+1, yMin, yMax);
            else if (xMax > 1) DrawQuad(xMin-1, xMax-1, yMin, yMax);
        }
    }

    void DrawQuad(float x0, float x1, float y0, float y1)
    {
        GL.Begin(GL.QUADS);
        GL.TexCoord2(x0,y0); GL.Vertex3(x0,y0,0);
        GL.TexCoord2(x0,y1); GL.Vertex3(x0,y1,0);
        GL.TexCoord2(x1,y1); GL.Vertex3(x1,y1,0);
        GL.TexCoord2(x1,y0); GL.Vertex3(x1,y0,0);
        GL.End();
    }

    Material SetupBrushMaterial(out float radiusUV)
    {
        Material mat = isEraser ? eraserMaterial : sphereBrushMat;
        float effective = GetEffectiveBrushSize(isEraser ? eraserSize : brushSize);
        radiusUV = (effective / overlayTexture.height) * 0.5f;
        mat.SetColor("_Color", drawColor);
        mat.SetFloat("_Hardness", hardness);
        mat.SetFloat("_BrushRadius", radiusUV * Mathf.PI);
        if (!isEraser) mat.SetFloat("_Opacity", brushOpacity);
        return mat;
    }

    void PaintStroke(Vector2 startUV, Vector2 endUV)
    {
        if (overlayTexture == null) return;
        float radiusUV;
        Material mat = SetupBrushMaterial(out radiusUV);
        if (mat == null) return;

        RenderTexture.active = overlayTexture;
        GL.PushMatrix(); GL.LoadOrtho();

        float dx = endUV.x - startUV.x;
        if (dx > 0.5f) endUV.x -= 1f; else if (dx < -0.5f) endUV.x += 1f;
        float dist = Vector2.Distance(startUV, endUV);
        float step = Mathf.Max(0.0001f, radiusUV * 2f * brushSpacing);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));

        for (int i = 0; i <= steps; i++)
        {
            Vector2 pos = Vector2.Lerp(startUV, endUV, (float)i / steps);
            pos.x = Mathf.Repeat(pos.x, 1f);
            mat.SetVector("_BrushCenter", new Vector4(pos.x, pos.y, 0, 0));
            mat.SetPass(0);
            DrawBrushQuad(pos, radiusUV);
        }

        GL.PopMatrix();
        RenderTexture.active = null;
    }

    void DrawLineStraight(Vector3 startP3D, Vector3 endP3D)
    {
        if (overlayTexture == null) return;
        float radiusUV;
        Material mat = SetupBrushMaterial(out radiusUV);
        if (mat == null) return;

        RenderTexture.active = overlayTexture;
        GL.PushMatrix(); GL.LoadOrtho();

        float step = Mathf.Max(0.0001f, radiusUV * 2f * brushSpacing);
        float arc = Mathf.Acos(Mathf.Clamp(Vector3.Dot(startP3D, endP3D), -1f, 1f));
        int steps = Mathf.Max(1, Mathf.CeilToInt(arc / step));

        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Slerp(startP3D, endP3D, (float)i / steps).normalized;
            Vector2 uv = SphereVectorToUV(p);
            mat.SetVector("_BrushCenter", new Vector4(uv.x, uv.y, 0, 0));
            mat.SetPass(0);
            DrawBrushQuad(uv, radiusUV);
        }

        GL.PopMatrix();
        RenderTexture.active = null;
    }

    void GenerateBrushTexture()
    {
        int res = 128;
        brushTexture = new Texture2D(res, res, TextureFormat.ARGB32, false);
        Color[] colors = new Color[res * res];
        Vector2 center = new Vector2(res * 0.5f, res * 0.5f);
        float maxRadius = res * 0.5f;
        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = 1.0f - Mathf.Clamp01((dist - (maxRadius * hardness)) / (maxRadius * (1f - hardness + 0.01f)));
                colors[y * res + x] = new Color(1, 1, 1, alpha);
            }
        }
        brushTexture.SetPixels(colors);
        brushTexture.Apply();
    }

    public float GetEffectiveBrushSize(float baseSize)
    {
        if (!useSmartBrush || projectionEffect == null) return baseSize;

        // Formula: baseSize * (CurrentVerticalFOV / ReferenceFOV)
        // Uses calculatedVerticalFOV instead of perspective
        float currentFOV = projectionEffect.calculatedVerticalFOV; 
        return baseSize * (currentFOV / REFERENCE_FOV);
    }

    // --- Clear Canvas ---
    public void ClearCanvas()
    {
        if (overlayTexture == null) return;
        SaveUndoState();
        ClearRenderTexture(overlayTexture);
        CleanupList(redoHistory);
    }

    // --- Export Overlay as transparent PNG ---
    public void ExportOverlayAsPNG()
    {
        if (overlayTexture == null) return;
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string defaultName = $"Overlay_{timestamp}.png";
        string path = SimpleFileBrowser.SaveFile("Export Overlay", defaultName, "png");
        if (string.IsNullOrEmpty(path)) return;

        Texture2D resultTex = new Texture2D(overlayTexture.width, overlayTexture.height, TextureFormat.ARGB32, false);
        RenderTexture.active = overlayTexture;
        resultTex.ReadPixels(new Rect(0, 0, overlayTexture.width, overlayTexture.height), 0, 0);
        resultTex.Apply();
        RenderTexture.active = null;

        byte[] bytes = resultTex.EncodeToPNG();
        if (!path.ToLower().EndsWith(".png")) path += ".png";
        File.WriteAllBytes(path, bytes);
        Destroy(resultTex);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
    }
}