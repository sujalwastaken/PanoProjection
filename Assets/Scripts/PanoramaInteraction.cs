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

    [Header("Snapping")]
    public float rulerLockThreshold = 0.005f;

    [Header("Brush Settings")]
    [Range(1, 200)] public float brushSize = 50.0f;
    [Range(1, 200)] public float eraserSize = 100.0f;
    [Range(0.01f, 0.5f)] public float brushSpacing = 0.05f;
    [Range(0.0f, 1.0f)] public float hardness = 0.8f;

    [Tooltip("Scales brush size with FOV to keep line width consistent on screen.")]
    public bool useSmartBrush = false; 
    private const float REFERENCE_FOV = 90.0f; // The baseline FOV

    [HideInInspector] public bool isEraser = false;
    private bool wasEraser = false; // To track state changes

    private Camera cam;
    private PanoramaProjectionEffect projectionEffect;

    // Reference to Manager
    private PanoramaLayerManager layerManager;

    [HideInInspector] public RenderTexture targetTexture;
    [HideInInspector] public bool allowCameraMovement = false;

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
    private RenderTexture displayTexture;

    // Ruler state
    private Vector3 strokeStartP3D;
    private int lockedAxis = -1;

    void Start()
    {
        cam = GetComponent<Camera>();
        projectionEffect = GetComponent<PanoramaProjectionEffect>();
        layerManager = GetComponent<PanoramaLayerManager>();

        if (brushShader == null) brushShader = Shader.Find("Sprites/Default");
        if (eraserShader == null) eraserShader = Shader.Find("Hidden/PanoramaEraser");
        if (gridCompositeShader == null) gridCompositeShader = Shader.Find("Hidden/PanoramaGridComposite");

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
    }

    void OnDestroy()
    {
        if (displayTexture != null) displayTexture.Release();
        // Reset cursor on exit
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

    void InitializeGPUResources()
    {
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

        // Create display texture for grid composite
        if (projectionEffect.panoramaTexture != null)
        {
            Texture source = projectionEffect.panoramaTexture;
            displayTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            displayTexture.Create();
        }
    }

    void Update()
    {
        bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (isCtrl && Input.GetKeyDown(KeyCode.S)) { SavePanorama(); return; }
        if (isCtrl && Input.GetKeyDown(KeyCode.D)) { LoadPanorama(); return; }

        HandleToolInput();

        // --- UPDATE CURSOR STATE ---
        // Only update if the tool changed or we entered/exited UI mode (implied by targetTexture null/not null check potentially)
        if (isEraser != wasEraser)
        {
            UpdateHardwareCursor();
            wasEraser = isEraser;
        }
        // ---------------------------

        // Check if grid settings changed
        if (HasGridSettingsChanged())
        {
            if (layerManager != null) layerManager.compositionDirty = true;
        }

        if (targetTexture == null && !allowCameraMovement)
        {
            projectionEffect.HideCursor();
            // Ensure default cursor when not painting
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); 
            return;
        }

        // Cursor logic (Projected Brush Preview)
        Vector2 screenPos = Input.mousePosition;
        Vector2 screenUV = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        Vector3 currentP3D = ScreenPointToSphereVector(screenUV);

        bool isSpace = Input.GetKey(KeyCode.Space);
        bool isClick = Input.GetMouseButton(0);
        bool isPainting = isClick && !isSpace && !isCtrl;

        // Snapping logic
        bool shouldSnap = enableSnapping || isShift;

        if (shouldSnap && isPainting)
        {
            if (Input.GetMouseButtonDown(0))
            {
                strokeStartP3D = currentP3D;
                lockedAxis = -1;
            }
            currentP3D = ApplyPerspectiveRuler(currentP3D);
        }

        Vector2 currentCursorUV = SphereVectorToUV(currentP3D);
        
        // Drawing the 3D projected brush ring
        if (GUIUtility.hotControl == 0 && targetTexture != null)
        {
            float currentSize = isEraser ? eraserSize : brushSize;
            Color cursorCol = isEraser ? Color.black : new Color(drawColor.r, drawColor.g, drawColor.b, 1.0f);
            float effectiveSize = GetEffectiveBrushSize(currentSize);
            float radiusUV = (effectiveSize / targetTexture.height) * 0.5f;
            projectionEffect.UpdateCursor(currentCursorUV, radiusUV, cursorCol);
            
            // Re-enforce hardware cursor in case UI overrode it
            if (!isEraser && Cursor.visible) UpdateHardwareCursor(); 
        }
        else
        {
            projectionEffect.HideCursor();
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        if (GUIUtility.hotControl != 0) return;

        if (Input.GetMouseButtonDown(0)) lastMousePos = Input.mousePosition;

        bool isNavigation = (isSpace && isClick) || (isClick && targetTexture == null && allowCameraMovement);

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
                camPitch = Mathf.Clamp(camPitch, -89.5f, 89.5f);
                ApplyRotation();
            }
            lastUvPos = null;
        }
        else if (isPainting && targetTexture != null)
        {
            if (!strokeInProgress)
            {
                if (layerManager != null) layerManager.SaveActiveLayerState();
                strokeInProgress = true;
            }
            if (lastUvPos == null) lastUvPos = currentCursorUV;
            PaintStroke(lastUvPos.Value, currentCursorUV);
            lastUvPos = currentCursorUV;
        }
        else
        {
            lastUvPos = null;
            strokeInProgress = false;
        }

        if (isClick) lastMousePos = Input.mousePosition;
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

    // --- NEW: Procedurally Generate the Triangle Cursor ---
    Texture2D GenerateTriangleCursor()
    {
        int size = 32;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point; // Keep it sharp
        Color[] colors = new Color[size * size];

        // Initialize transparent
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.clear;

        // Draw Triangle Arrow
        // The image is a sharp arrow pointing to Top-Left.
        // In Unity Texture2D (0,0) is Bottom-Left. 
        // We want the tip at (0, size-1) effectively.
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Invert Y for drawing logic (0 is top)
                int drawY = size - 1 - y;

                // Simple inequality for a right-angled looking arrow
                // x < y means we are below the diagonal
                // x < 10 width
                // y < 15 height
                // This creates a basic arrowhead shape
                
                // Logic: Draw pixels if they are inside the "arrow" shape
                // Main diagonal slope: x == drawY (This is the diagonal spine)
                // Bottom slope: drawY == x * 2 (steeper)
                
                bool inside = false;
                
                // Coordinates relative to Top-Left
                float u = x;
                float v = drawY;

                // Define the 3 corners of the arrow
                // Tip: (0,0)
                // Right: (18, 10)
                // Bottom: (10, 18)
                // Inner: (10, 10) - concave part

                // Check using barycentric coordinates or simple slope logic
                // Simplified: Is it within the main arrow wedge?
                if (u >= 0 && v >= 0)
                {
                    // 1. Extend Bounds (was 14 -> 18) to allow a longer, sharper tip
                    if (u < 18 && v < 18)
                    {
                        // 2. Increase Slope (was 0.5 -> 0.7) to make it narrower
                        if (v >= u * 0.7f)
                        {
                            if (u >= v * 0.7f)
                            {
                                // 3. Adjust Cutoff (was 22 -> 28) for the new length
                                if (u + v < 25) 
                                {
                                     inside = true;
                                }
                            }
                        }
                    }
                }

                if (inside)
                {
                    colors[y * size + x] = Color.black;
                }
            }
        }
        
        // Add a 1px white border around the black pixels for contrast
        Color[] borderedColors = (Color[])colors.Clone();
        for (int y = 1; y < size - 1; y++)
        {
            for (int x = 1; x < size - 1; x++)
            {
                int idx = y * size + x;
                if (colors[idx].a > 0.9f) // If current is black
                {
                    // Check neighbors, if transparent, make them white in the new array
                    SetBorderPixel(x+1, y, size, borderedColors);
                    SetBorderPixel(x-1, y, size, borderedColors);
                    SetBorderPixel(x, y+1, size, borderedColors);
                    SetBorderPixel(x, y-1, size, borderedColors);
                }
            }
        }

        // Apply black over white
        for(int i=0; i<colors.Length; i++) {
            if (colors[i].a > 0) borderedColors[i] = colors[i];
        }

        tex.SetPixels(borderedColors);
        tex.Apply();
        return tex;
    }

    void SetBorderPixel(int x, int y, int size, Color[] cols)
    {
        int idx = y * size + x;
        if (cols[idx].a == 0) cols[idx] = Color.white;
    }

    void HandleToolInput()
    {
        bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Q)) isEraser = false;
        if (Input.GetKeyDown(KeyCode.E)) isEraser = true;

        if (Input.GetKeyDown(KeyCode.G))
        {
            if (isShift) gridRotation = cam.transform.eulerAngles;
            else showGrid = !showGrid;
            if (layerManager != null) layerManager.compositionDirty = true;
        }

        if (Input.GetKeyDown(KeyCode.S) && !isCtrl)
        {
            enableSnapping = !enableSnapping;
        }

        if (Input.GetKeyDown(KeyCode.Z) && isCtrl)
        {
            if (layerManager != null)
            {
                if (isShift) layerManager.RedoActiveLayer();
                else layerManager.UndoActiveLayer();
            }
        }
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

        return changed;
    }

    public void UpdateGridDisplay(RenderTexture sourceComposite)
    {
        if (gridCompositeMat == null || sourceComposite == null) return;

        // Ensure display texture matches source dimensions
        if (displayTexture == null || displayTexture.width != sourceComposite.width || displayTexture.height != sourceComposite.height)
        {
            if (displayTexture != null) displayTexture.Release();
            displayTexture = new RenderTexture(sourceComposite.width, sourceComposite.height, 0, RenderTextureFormat.ARGB32);
            displayTexture.Create();
        }

        gridCompositeMat.SetFloat("_UseGrid", showGrid ? 1.0f : 0.0f);

        if (showGrid)
        {
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
        }

        Graphics.Blit(sourceComposite, displayTexture, gridCompositeMat);
        projectionEffect.overlayTexture = displayTexture;
    }

    Vector3 ApplyPerspectiveRuler(Vector3 currentP)
    {
        Quaternion gridRot = Quaternion.Euler(gridRotation);
        Vector3 vpX = gridRot * Vector3.right;
        Vector3 vpY = gridRot * Vector3.up;
        Vector3 vpZ = gridRot * Vector3.forward;

        if (lockedAxis == -1)
        {
            float dist = Vector3.Distance(currentP, strokeStartP3D);
            if (dist > rulerLockThreshold)
            {
                Vector3 strokeDir = (currentP - strokeStartP3D).normalized;
                Vector3 normX = Vector3.Cross(strokeStartP3D, vpX).normalized;
                Vector3 tanX = Vector3.Cross(normX, strokeStartP3D).normalized;
                Vector3 normY = Vector3.Cross(strokeStartP3D, vpY).normalized;
                Vector3 tanY = Vector3.Cross(normY, strokeStartP3D).normalized;
                Vector3 normZ = Vector3.Cross(strokeStartP3D, vpZ).normalized;
                Vector3 tanZ = Vector3.Cross(normZ, strokeStartP3D).normalized;

                float dotX = Mathf.Abs(Vector3.Dot(strokeDir, tanX));
                float dotY = Mathf.Abs(Vector3.Dot(strokeDir, tanY));
                float dotZ = Mathf.Abs(Vector3.Dot(strokeDir, tanZ));

                if (dotX > dotY && dotX > dotZ) lockedAxis = 0;
                else if (dotY > dotX && dotY > dotZ) lockedAxis = 1;
                else lockedAxis = 2;
            }
            else return currentP;
        }

        Vector3 targetVP = Vector3.zero;
        if (lockedAxis == 0) targetVP = vpX;
        if (lockedAxis == 1) targetVP = vpY;
        if (lockedAxis == 2) targetVP = vpZ;

        Vector3 planeNormal = Vector3.Cross(strokeStartP3D, targetVP).normalized;
        return Vector3.ProjectOnPlane(currentP, planeNormal).normalized;
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
        camPitch = Mathf.Clamp(camPitch, -89.5f, 89.5f);
    }

    void ApplyRotation()
    {
        transform.rotation = Quaternion.Euler(camPitch, camYaw, camRoll);
    }

    public void SavePanorama()
    {
        Texture source = projectionEffect.panoramaTexture;
        if (source == null || targetTexture == null) return;
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string defaultName = $"Panorama_{timestamp}.jpg";
        string path = SimpleFileBrowser.SaveFile("Save Panorama", defaultName, "jpg");

        if (string.IsNullOrEmpty(path)) return;

        RenderTexture bakedRT = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(source, bakedRT);
        Material compositor = new Material(Shader.Find("Sprites/Default"));

        Graphics.Blit(targetTexture, bakedRT, compositor);

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
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D newPano = new Texture2D(2, 2);
            if (newPano.LoadImage(bytes))
            {
                projectionEffect.panoramaTexture = newPano;
                InitializeGPUResources();
            }
        }
    }

    void PaintStroke(Vector2 startUV, Vector2 endUV)
    {
        if (isEraser && eraserMaterial == null) return;
        if (!isEraser && brushMaterial == null) return;
        if (targetTexture == null) return;

        RenderTexture.active = targetTexture;
        GL.PushMatrix();
        GL.LoadPixelMatrix(0, 1, 0, 1);

        if (isEraser)
        {
            eraserMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(new Color(0, 0, 0, 1));
        }
        else
        {
            brushMaterial.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(drawColor);
        }

        float dx = endUV.x - startUV.x;
        if (dx > 0.5f) endUV.x -= 1.0f;
        else if (dx < -0.5f) endUV.x += 1.0f;

        float currentSize = isEraser ? eraserSize : brushSize;
        float effectiveSize = GetEffectiveBrushSize(currentSize);
        float brushScaleY = effectiveSize / targetTexture.height;
        float brushScaleX = brushScaleY * ((float)targetTexture.height / targetTexture.width);

        float distance = Vector2.Distance(startUV, endUV);
        float stepSize = Mathf.Max(0.0001f, brushScaleY * brushSpacing);
        int steps = Mathf.CeilToInt(distance / stepSize);
        if (steps <= 0) steps = 1;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector2 pos = Vector2.Lerp(startUV, endUV, t);
            DrawBrushQuad(pos, brushScaleX, brushScaleY);
            if (pos.x < brushScaleX) DrawBrushQuad(new Vector2(pos.x + 1.0f, pos.y), brushScaleX, brushScaleY);
            if (pos.x > 1.0f - brushScaleX) DrawBrushQuad(new Vector2(pos.x - 1.0f, pos.y), brushScaleX, brushScaleY);
        }
        GL.End();
        GL.PopMatrix();
        RenderTexture.active = null;
    }

    void DrawBrushQuad(Vector2 center, float sizeX, float sizeY)
    {
        float halfX = sizeX * 0.5f;
        float halfY = sizeY * 0.5f;
        GL.TexCoord2(0, 0); GL.Vertex3(center.x - halfX, center.y - halfY, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(center.x - halfX, center.y + halfY, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(center.x + halfX, center.y + halfY, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(center.x + halfX, center.y - halfY, 0);
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
}