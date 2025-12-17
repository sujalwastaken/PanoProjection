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
    
    [Header("Perspective Ruler")]
    [Tooltip("Toggle with 'S'")]
    public bool enableSnapping = false; 
    [Tooltip("Toggle with 'G'")]
    public bool showGrid = false;       

    public Vector3 gridRotation = Vector3.zero; 
    
    [Header("Grid Visualization")]
    [Range(2.0f, 45.0f)] public float gridSpacing = 10.0f; 
    [Range(0.1f, 5.0f)] public float gridThickness = 1.0f; // Adjusted default for pixel width
    [Range(0.0f, 1.0f)] public float gridOpacity = 0.5f;   // <--- NEW: Global Opacity
    
    public Color gridColorX = new Color(1, 0, 0, 1.0f); // Colors can be full opacity now
    public Color gridColorY = new Color(0, 1, 0, 1.0f); 
    public Color gridColorZ = new Color(0, 0, 1, 1.0f); 

    [Header("Snapping")]
    public float rulerLockThreshold = 0.005f; 

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
    private RenderTexture displayTexture; 
    
    private Texture2D brushTexture;
    private Material brushMaterial;
    private Material eraserMaterial;
    private Material gridCompositeMat;
    
    private List<RenderTexture> undoHistory = new List<RenderTexture>();
    private List<RenderTexture> redoHistory = new List<RenderTexture>();
    private bool strokeInProgress = false;

    private Vector2? lastUvPos = null;
    private Vector3 lastMousePos; 
    private const float sensitivityMultiplier = 0.1f; 
    private const float TWO_PI = Mathf.PI * 2.0f; 

    private Vector3 strokeStartP3D; 
    private int lockedAxis = -1; 
    
    void Start()
    {
        cam = GetComponent<Camera>();
        projectionEffect = GetComponent<PanoramaProjectionEffect>();
        
        if (brushShader == null) brushShader = Shader.Find("Sprites/Default");
        if (eraserShader == null) eraserShader = Shader.Find("Hidden/PanoramaEraser");
        if (gridCompositeShader == null) gridCompositeShader = Shader.Find("Hidden/PanoramaGridComposite");

        InitializeGPUResources();
    }

    void OnDestroy() {
        CleanupList(undoHistory);
        CleanupList(redoHistory);
        if (overlayTexture != null) overlayTexture.Release();
        if (displayTexture != null) displayTexture.Release();
    }
    void CleanupList(List<RenderTexture> list) {
        foreach (var rt in list) if (rt != null) { rt.Release(); Destroy(rt); }
        list.Clear();
    }

    void InitializeGPUResources()
    {
        if (projectionEffect.panoramaTexture == null) return;
        Texture source = projectionEffect.panoramaTexture; 
        
        if (overlayTexture != null) overlayTexture.Release();
        overlayTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        overlayTexture.enableRandomWrite = true;
        overlayTexture.Create();
        ClearRenderTexture(overlayTexture);

        if (displayTexture != null) displayTexture.Release();
        displayTexture = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        displayTexture.Create();

        projectionEffect.overlayTexture = displayTexture;

        GenerateBrushTexture();

        if (brushShader != null) {
            brushMaterial = new Material(brushShader);
            brushMaterial.mainTexture = brushTexture;
        }
        if (eraserShader != null) {
            eraserMaterial = new Material(eraserShader);
            eraserMaterial.mainTexture = brushTexture;
        }
        if (gridCompositeShader != null) {
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

        if (isCtrl && Input.GetKeyDown(KeyCode.S)) { SavePanorama(); return; }
        if (isCtrl && Input.GetKeyDown(KeyCode.D)) { LoadPanorama(); return; }
        
        HandleToolInput(); 

        // --- GRID RENDERING ---
        // Changed: Removed 'isShift'. Visibility is strictly controlled by toggle 'G'.
        UpdateGridDisplay(showGrid);

        bool isSpace = Input.GetKey(KeyCode.Space);
        Vector2 screenPos = Input.mousePosition;
        Vector2 screenUV = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        Vector3 currentP3D = ScreenPointToSphereVector(screenUV);
        
        bool isPainting = Input.GetMouseButton(0) && !isSpace && !isCtrl;
        
        // --- SNAPPING LOGIC ---
        // Changed: Shift still enables snapping temporarily.
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
        float currentSize = isEraser ? eraserSize : brushSize;
        Color cursorCol = isEraser ? Color.black : new Color(drawColor.r, drawColor.g, drawColor.b, 1.0f);
        float radiusUV = (currentSize / overlayTexture.height) * 0.5f;

        if (GUIUtility.hotControl == 0 && projectionEffect.overlayTexture != null)
            projectionEffect.UpdateCursor(currentCursorUV, radiusUV, cursorCol);
        else
            projectionEffect.HideCursor();

        if (GUIUtility.hotControl != 0) return; 

        if (Input.GetMouseButtonDown(0)) lastMousePos = Input.mousePosition;
        bool isClick = Input.GetMouseButton(0);

        if (isSpace && isClick)
        {
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
        else if (isPainting) 
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

    void HandleToolInput()
    {
        bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Q)) isEraser = false;
        if (Input.GetKeyDown(KeyCode.E)) isEraser = true;
        
        if (Input.GetKeyDown(KeyCode.G)) {
            if (isShift) gridRotation = cam.transform.eulerAngles;
            else showGrid = !showGrid;
        }

        if (Input.GetKeyDown(KeyCode.S) && !isCtrl) {
            enableSnapping = !enableSnapping;
        }

        if (Input.GetKeyDown(KeyCode.Z) && isCtrl) {
            if (isShift) PerformRedo(); else PerformUndo();
        }
    }

    void UpdateGridDisplay(bool visible)
    {
        if (gridCompositeMat == null || overlayTexture == null || displayTexture == null) return;

        gridCompositeMat.SetFloat("_UseGrid", visible ? 1.0f : 0.0f);
        
        if (visible)
        {
            gridCompositeMat.SetColor("_ColorX", gridColorX);
            gridCompositeMat.SetColor("_ColorY", gridColorY);
            gridCompositeMat.SetColor("_ColorZ", gridColorZ);
            gridCompositeMat.SetFloat("_Spacing", gridSpacing);
            gridCompositeMat.SetFloat("_Thickness", gridThickness);
            gridCompositeMat.SetFloat("_Opacity", gridOpacity); // <--- PASS OPACITY
            
            Matrix4x4 m = Matrix4x4.Rotate(Quaternion.Euler(gridRotation));
            gridCompositeMat.SetVector("_Rot0", m.GetRow(0));
            gridCompositeMat.SetVector("_Rot1", m.GetRow(1));
            gridCompositeMat.SetVector("_Rot2", m.GetRow(2));
        }

        Graphics.Blit(overlayTexture, displayTexture, gridCompositeMat);
        
        if (projectionEffect.overlayTexture != displayTexture)
            projectionEffect.overlayTexture = displayTexture;
    }

    // --- RULER MATH & HELPERS (UNCHANGED) ---
    Vector3 ApplyPerspectiveRuler(Vector3 currentP) {
        Quaternion gridRot = Quaternion.Euler(gridRotation);
        Vector3 vpX = gridRot * Vector3.right;
        Vector3 vpY = gridRot * Vector3.up;
        Vector3 vpZ = gridRot * Vector3.forward;

        if (lockedAxis == -1) {
            float dist = Vector3.Distance(currentP, strokeStartP3D);
            if (dist > rulerLockThreshold) {
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

    Vector3 ScreenPointToSphereVector(Vector2 screenUV) {
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
            float sinTheta = Mathf.Sin(theta); float cosTheta = Mathf.Cos(theta);
            Vector2 dir2D = coord / r; rayDir.x = dir2D.x * sinTheta; rayDir.y = -dir2D.y * sinTheta; rayDir.z = cosTheta;
        }
        return (cam.transform.rotation * rayDir).normalized;
    }

    Vector2 SphereVectorToUV(Vector3 p) {
        float lon = Mathf.Atan2(p.x, p.z);
        float lat = Mathf.Asin(Mathf.Clamp(p.y, -1.0f, 1.0f));
        Vector2 uv;
        uv.x = Mathf.Repeat(lon / TWO_PI + 0.5f, 1.0f);
        uv.y = Mathf.Clamp01(0.5f - lat / Mathf.PI);
        return uv;
    }

    public void SavePanorama() {
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
        File.WriteAllBytes(path, bytes);
        RenderTexture.ReleaseTemporary(bakedRT);
        Destroy(resultTex); Destroy(compositor);
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh(); 
#endif
    }

    public void LoadPanorama() {
        string path = SimpleFileBrowser.OpenFile("Load Panorama");
        if (string.IsNullOrEmpty(path)) return;
        if (File.Exists(path)) {
            float keptPerspective = projectionEffect.perspective;
            float keptFisheye = projectionEffect.fisheyePerspective;
            Quaternion keptRotation = transform.rotation;
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D newPano = new Texture2D(2, 2);
            if (newPano.LoadImage(bytes)) {
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