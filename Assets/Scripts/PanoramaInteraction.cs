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

    [Header("Interaction Settings")]
    public float rotateSpeed = 2.0f;
    public float zoomSpeed = 2.0f;
    public Color drawColor = Color.red;
    
    [Header("Perspective Ruler (CSP Style)")]
    public bool useRuler = false;
    [Tooltip("Rotate the Vanishing Points to match your scene")]
    public Vector3 gridRotation = Vector3.zero; 
    [Tooltip("How far you must drag before the ruler locks direction")]
    public float rulerLockThreshold = 0.005f; 
    [Tooltip("Angle between grid lines (affects snapping precision)")]
    [Range(2.0f, 45.0f)] public float gridSpacing = 10.0f; 

    [Header("Brush Settings")]
    [Range(1, 200)] public float brushSize = 50.0f;
    [Range(1, 200)] public float eraserSize = 100.0f;
    [Range(0.01f, 0.5f)] public float brushSpacing = 0.05f;
    [Range(0.0f, 1.0f)] public float hardness = 0.8f;

    [Header("Undo Settings")]
    [Range(1, 20)] public int maxUndoSteps = 10;

    [HideInInspector] public bool isEraser = false; 

    private Camera cam;
    private PanoramaProjectionEffect projectionEffect;
    
    private RenderTexture overlayTexture; 
    private Texture2D brushTexture;
    private Material brushMaterial;
    private Material eraserMaterial;
    
    private List<RenderTexture> undoHistory = new List<RenderTexture>();
    private List<RenderTexture> redoHistory = new List<RenderTexture>();
    private bool strokeInProgress = false;

    private Vector2? lastUvPos = null;
    private Vector3 lastMousePos; 
    private const float sensitivityMultiplier = 0.1f; 
    private const float TWO_PI = Mathf.PI * 2.0f; 

    // --- RULER STATE ---
    private Vector3 strokeStartP3D; // The exact 3D point where you clicked
    private int lockedAxis = -1; // -1: Free, 0: X-Axis, 1: Y-Axis, 2: Z-Axis
    
    void Start()
    {
        cam = GetComponent<Camera>();
        projectionEffect = GetComponent<PanoramaProjectionEffect>();
        
        if (brushShader == null) brushShader = Shader.Find("Sprites/Default");
        if (eraserShader == null) eraserShader = Shader.Find("Hidden/PanoramaEraser");

        InitializeGPUResources();
    }

    // --- MEMORY CLEANUP ---
    void OnDestroy() {
        CleanupList(undoHistory);
        CleanupList(redoHistory);
        if (overlayTexture != null) overlayTexture.Release();
    }
    void CleanupList(List<RenderTexture> list) {
        foreach (var rt in list) if (rt != null) { rt.Release(); Destroy(rt); }
        list.Clear();
    }
    // ----------------------

    void InitializeGPUResources()
    {
        if (projectionEffect.panoramaTexture == null) return;
        Texture source = projectionEffect.panoramaTexture; 
        
        if (overlayTexture != null) overlayTexture.Release();
        overlayTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        overlayTexture.enableRandomWrite = true;
        overlayTexture.Create();
        ClearRenderTexture(overlayTexture);

        projectionEffect.overlayTexture = overlayTexture;
        GenerateBrushTexture();

        if (brushShader != null) {
            brushMaterial = new Material(brushShader);
            brushMaterial.mainTexture = brushTexture;
        }
        if (eraserShader != null) {
            eraserMaterial = new Material(eraserShader);
            eraserMaterial.mainTexture = brushTexture;
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

        // --- SHORTCUTS ---
        if (isCtrl && Input.GetKeyDown(KeyCode.S)) { SavePanorama(); return; }
        if (isCtrl && Input.GetKeyDown(KeyCode.D)) { LoadPanorama(); return; }
        
        HandleToolInput();

        bool isSpace = Input.GetKey(KeyCode.Space);

        // --- CURSOR CALCULATION ---
        Vector2 screenPos = Input.mousePosition;
        Vector2 screenUV = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        
        // 1. Get Raw 3D Point on Sphere (Before UV conversion)
        Vector3 currentP3D = ScreenPointToSphereVector(screenUV);
        
        // 2. Apply Ruler Logic if Painting
        bool isPainting = Input.GetMouseButton(0) && !isSpace && !isCtrl;
        
        // --- CSP-STYLE RULER LOGIC ---
        // Snap if grid is toggled ON, OR if Shift is held down
        bool shouldSnap = useRuler || isShift; 

        if (shouldSnap && isPainting)
        {
            // If just started clicking, record start point and reset lock
            if (Input.GetMouseButtonDown(0))
            {
                strokeStartP3D = currentP3D;
                lockedAxis = -1;
            }

            // Apply Directional Constraint
            currentP3D = ApplyPerspectiveRuler(currentP3D);
        }

        // 3. Convert Final 3D Point to UV for Painting
        Vector2 currentCursorUV = SphereVectorToUV(currentP3D);

        // --------------------------------

        float currentSize = isEraser ? eraserSize : brushSize;
        Color cursorCol = isEraser ? Color.black : new Color(drawColor.r, drawColor.g, drawColor.b, 1.0f);
        float radiusUV = (currentSize / overlayTexture.height) * 0.5f;

        if (GUIUtility.hotControl == 0 && overlayTexture != null)
            projectionEffect.UpdateCursor(currentCursorUV, radiusUV, cursorCol);
        else
            projectionEffect.HideCursor();

        if (GUIUtility.hotControl != 0) return; 

        // --- INPUT TRACKING ---
        if (Input.GetMouseButtonDown(0)) lastMousePos = Input.mousePosition;

        bool isClick = Input.GetMouseButton(0);

        if (isSpace && isClick)
        {
            // Navigation Logic (Unchanged)
            Vector3 currentPos = Input.mousePosition;
            Vector3 delta = currentPos - lastMousePos;
            float t = projectionEffect.perspective / 100.0f;
            float damping = Mathf.Lerp(0.1f, 1.0f, t);
            float dx = delta.x * sensitivityMultiplier;
            float dy = delta.y * sensitivityMultiplier;

            if (isCtrl) {
                float proportionalStep = projectionEffect.perspective * 0.05f; 
                float zoomChange = dx * zoomSpeed * proportionalStep;
                projectionEffect.perspective = Mathf.Clamp(projectionEffect.perspective - zoomChange, 1f, 100f);
            }
            else if (isShift) transform.Rotate(Vector3.forward, -dx * rotateSpeed * damping, Space.Self);
            else {
                transform.Rotate(Vector3.up, -dx * rotateSpeed * damping, Space.World);
                transform.Rotate(Vector3.right, -dy * rotateSpeed * damping, Space.Self);
            }
            lastUvPos = null; 
        }
        else if (isPainting) // Paint
        {
            if (!strokeInProgress) { 
                SaveUndoState(); 
                strokeInProgress = true; 
                CleanupList(redoHistory); 
            }
            if (lastUvPos == null) lastUvPos = currentCursorUV;
            
            PaintStroke(lastUvPos.Value, currentCursorUV, isEraser);
            lastUvPos = currentCursorUV;
        }
        else
        {
            lastUvPos = null;
            strokeInProgress = false;
        }

        if (isClick) lastMousePos = Input.mousePosition;
    }

    // ===================================================================================
    //  CSP-STYLE PERSPECTIVE RULER LOGIC
    // ===================================================================================
    Vector3 ApplyPerspectiveRuler(Vector3 currentP)
    {
        // Grid Orientation
        Quaternion gridRot = Quaternion.Euler(gridRotation);
        
        // Define Vanishing Point Directions (Axes)
        Vector3 vpX = gridRot * Vector3.right;   // X Axis
        Vector3 vpY = gridRot * Vector3.up;      // Y Axis
        Vector3 vpZ = gridRot * Vector3.forward; // Z Axis

        // If we haven't locked an axis yet, try to detect one
        if (lockedAxis == -1)
        {
            float dist = Vector3.Distance(currentP, strokeStartP3D);
            if (dist > rulerLockThreshold)
            {
                // Calculate which VP axis matches the stroke direction best
                Vector3 strokeDir = (currentP - strokeStartP3D).normalized;
                
                // Determine tangents of Great Circles connecting StartPoint to VPs
                // Tangent = Normal x StartPoint
                // Normal = StartPoint x VP
                
                Vector3 normX = Vector3.Cross(strokeStartP3D, vpX).normalized;
                Vector3 tanX = Vector3.Cross(normX, strokeStartP3D).normalized;

                Vector3 normY = Vector3.Cross(strokeStartP3D, vpY).normalized;
                Vector3 tanY = Vector3.Cross(normY, strokeStartP3D).normalized;

                Vector3 normZ = Vector3.Cross(strokeStartP3D, vpZ).normalized;
                Vector3 tanZ = Vector3.Cross(normZ, strokeStartP3D).normalized;

                // Compare alignment (Dot Product)
                float dotX = Mathf.Abs(Vector3.Dot(strokeDir, tanX));
                float dotY = Mathf.Abs(Vector3.Dot(strokeDir, tanY));
                float dotZ = Mathf.Abs(Vector3.Dot(strokeDir, tanZ));

                // Lock the best fitting axis
                if (dotX > dotY && dotX > dotZ) lockedAxis = 0;
                else if (dotY > dotX && dotY > dotZ) lockedAxis = 1;
                else lockedAxis = 2;
            }
            else
            {
                // Hasn't moved far enough to lock, return raw position (freehand feel at start)
                return currentP;
            }
        }

        // Apply Constraint based on Locked Axis
        Vector3 targetVP = Vector3.zero;
        if (lockedAxis == 0) targetVP = vpX;
        if (lockedAxis == 1) targetVP = vpY;
        if (lockedAxis == 2) targetVP = vpZ;

        // 1. Calculate the Plane defined by: StartPoint, VP, and Origin(0,0,0)
        // Normal of this plane is perpendicular to both StartPoint and VP
        Vector3 planeNormal = Vector3.Cross(strokeStartP3D, targetVP).normalized;

        // 2. Project current cursor position onto this plane
        // This effectively "flattens" your movement onto the Great Circle
        Vector3 projectedP = Vector3.ProjectOnPlane(currentP, planeNormal);

        // 3. Normalize to put it back on the sphere surface
        return projectedP.normalized;
    }


    // --- HELPERS FOR 3D VECTOR MATH ---
    Vector3 ScreenPointToSphereVector(Vector2 screenUV)
    {
        // Inverse of standard projection logic
        Vector2 coord = (screenUV - new Vector2(0.5f, 0.5f)) * 2.0f; 
        coord.x *= cam.aspect;
        float r = coord.magnitude; 
        Vector3 rayDir = new Vector3(0, 0, 1);
        
        if (r > 0.0001f) {
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
        
        // Transform by camera rotation to get World Direction
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
    // ----------------------------------

    void HandleToolInput()
    {
        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Q)) isEraser = false;
        if (Input.GetKeyDown(KeyCode.E)) isEraser = true;
        
        // --- GRID CONTROLS ---
        if (Input.GetKeyDown(KeyCode.G)) {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                gridRotation = cam.transform.eulerAngles; // Align Ruler to View
            }
            else 
            {
                useRuler = !useRuler; // Toggle Ruler
            }
        }

        if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) PerformRedo(); else PerformUndo();
        }
    }

    // --- SAVE / LOAD / UNDO / PAINT
    public void SavePanorama() {
        Texture source = projectionEffect.panoramaTexture;
        if (source == null || overlayTexture == null) return;
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string defaultName = $"Panorama_{timestamp}.jpg";
        
        // Call class wrapper directly (No using statement)
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
        Destroy(resultTex); Destroy(compositor);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh(); 
#endif
    }
    public void LoadPanorama() {
        // Call class wrapper directly (No using statement)
        string path = SimpleFileBrowser.OpenFile("Load Panorama");
        if (string.IsNullOrEmpty(path)) return;

        if (File.Exists(path))
        {
            float keptPerspective = projectionEffect.perspective;
            float keptFisheye = projectionEffect.fisheyePerspective;
            float keptMinFov = projectionEffect.minFov;
            float keptMaxFov = projectionEffect.maxFov;
            Quaternion keptRotation = transform.rotation;
            float keptBrushSize = brushSize;
            float keptEraserSize = eraserSize;
            Color keptColor = drawColor;
            bool keptTool = isEraser;

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
            projectionEffect.minFov = keptMinFov;
            projectionEffect.maxFov = keptMaxFov;
            transform.rotation = keptRotation;
            brushSize = keptBrushSize;
            eraserSize = keptEraserSize;
            drawColor = keptColor;
            isEraser = keptTool;
        }
    }
    void SaveUndoState() {
        RenderTexture snapshot = new RenderTexture(overlayTexture.descriptor);
        snapshot.Create(); Graphics.CopyTexture(overlayTexture, snapshot);
        undoHistory.Add(snapshot);
        if (undoHistory.Count > maxUndoSteps) { RenderTexture old = undoHistory[0]; undoHistory.RemoveAt(0); old.Release(); Destroy(old); }
    }
    void PerformUndo() {
        if (undoHistory.Count == 0) return;
        RenderTexture redoSnapshot = new RenderTexture(overlayTexture.descriptor);
        Graphics.CopyTexture(overlayTexture, redoSnapshot); redoHistory.Add(redoSnapshot);
        RenderTexture lastState = undoHistory[undoHistory.Count - 1];
        Graphics.CopyTexture(lastState, overlayTexture);
        undoHistory.RemoveAt(undoHistory.Count - 1); lastState.Release(); Destroy(lastState);
    }
    void PerformRedo() {
        if (redoHistory.Count == 0) return;
        RenderTexture undoSnapshot = new RenderTexture(overlayTexture.descriptor);
        Graphics.CopyTexture(overlayTexture, undoSnapshot); undoHistory.Add(undoSnapshot);
        RenderTexture lastState = redoHistory[redoHistory.Count - 1];
        Graphics.CopyTexture(lastState, overlayTexture);
        redoHistory.RemoveAt(redoHistory.Count - 1); lastState.Release(); Destroy(lastState);
    }
    void PaintStroke(Vector2 startUV, Vector2 endUV, bool eraseMode) {
        if (eraseMode && eraserMaterial == null) return;
        if (!eraseMode && brushMaterial == null) return;

        RenderTexture.active = overlayTexture;
        GL.PushMatrix(); GL.LoadPixelMatrix(0, 1, 0, 1); 

        if (eraseMode) { eraserMaterial.SetPass(0); GL.Begin(GL.QUADS); GL.Color(new Color(0, 0, 0, 1)); }
        else { brushMaterial.SetPass(0); GL.Begin(GL.QUADS); GL.Color(drawColor); }

        float dx = endUV.x - startUV.x;
        if (dx > 0.5f) endUV.x -= 1.0f; else if (dx < -0.5f) endUV.x += 1.0f;

        float currentSize = eraseMode ? eraserSize : brushSize;
        float brushScaleY = currentSize / overlayTexture.height; 
        float brushScaleX = brushScaleY * ((float)overlayTexture.height / overlayTexture.width); 

        float distance = Vector2.Distance(startUV, endUV);
        float stepSize = Mathf.Max(0.0001f, brushScaleY * brushSpacing);
        int steps = Mathf.CeilToInt(distance / stepSize);
        if (steps <= 0) steps = 1;

        for (int i = 0; i <= steps; i++) {
            float t = (float)i / steps;
            Vector2 pos = Vector2.Lerp(startUV, endUV, t);
            DrawBrushQuad(pos, brushScaleX, brushScaleY);
            if (pos.x < brushScaleX) DrawBrushQuad(new Vector2(pos.x + 1.0f, pos.y), brushScaleX, brushScaleY);
            if (pos.x > 1.0f - brushScaleX) DrawBrushQuad(new Vector2(pos.x - 1.0f, pos.y), brushScaleX, brushScaleY);
        }
        GL.End(); GL.PopMatrix(); RenderTexture.active = null;
    }
    void DrawBrushQuad(Vector2 center, float sizeX, float sizeY) {
        float halfX = sizeX * 0.5f; float halfY = sizeY * 0.5f;
        GL.TexCoord2(0, 0); GL.Vertex3(center.x - halfX, center.y - halfY, 0);
        GL.TexCoord2(0, 1); GL.Vertex3(center.x - halfX, center.y + halfY, 0);
        GL.TexCoord2(1, 1); GL.Vertex3(center.x + halfX, center.y + halfY, 0);
        GL.TexCoord2(1, 0); GL.Vertex3(center.x + halfX, center.y - halfY, 0);
    }
    void GenerateBrushTexture() {
        int res = 128; brushTexture = new Texture2D(res, res, TextureFormat.ARGB32, false);
        Color[] colors = new Color[res * res]; Vector2 center = new Vector2(res*0.5f, res*0.5f); float maxRadius = res*0.5f;
        for (int y=0; y<res; y++) { for (int x=0; x<res; x++) {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = 1.0f - Mathf.Clamp01((dist - (maxRadius * hardness)) / (maxRadius * (1f - hardness + 0.01f)));
                colors[y * res + x] = new Color(1, 1, 1, alpha);
        }}
        brushTexture.SetPixels(colors); brushTexture.Apply();
    }
}