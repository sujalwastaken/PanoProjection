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
    
    [Header("Perspective Rulers (6-Point)")]
    public bool useGrid = false;
    [Tooltip("Angle between grid lines")]
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
    
    // --- PEN TRACKING VARS ---
    private Vector3 lastMousePos; 
    private const float sensitivityMultiplier = 0.1f; 
    private const float TWO_PI = Mathf.PI * 2.0f; 

    void Start()
    {
        cam = GetComponent<Camera>();
        projectionEffect = GetComponent<PanoramaProjectionEffect>();
        
        if (brushShader == null) brushShader = Shader.Find("Sprites/Default");
        if (eraserShader == null) eraserShader = Shader.Find("Hidden/PanoramaEraser");

        InitializeGPUResources();
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

    // --- MEMORY CLEANUP (CRITICAL FIX) ---
    void OnDestroy()
    {
        CleanupList(undoHistory);
        CleanupList(redoHistory);
        if (overlayTexture != null) overlayTexture.Release();
    }

    void CleanupList(List<RenderTexture> list)
    {
        foreach (var rt in list)
        {
            if (rt != null) rt.Release();
        }
        list.Clear();
    }
    // -------------------------------------

    void Update()
    {
        bool isCtrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // --- SHORTCUTS ---
        if (isCtrl && Input.GetKeyDown(KeyCode.S)) { SavePanorama(); return; }
        if (isCtrl && Input.GetKeyDown(KeyCode.D)) { LoadPanorama(); return; }
        
        HandleToolInput();

        bool isSpace = Input.GetKey(KeyCode.Space);

        // --- CURSOR UPDATE ---
        Vector2 screenPos = Input.mousePosition;
        Vector2 screenUV = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        Vector2 currentCursorUV = ScreenPointToPanoUV(screenUV);

        if (useGrid && !isSpace) 
        {
            currentCursorUV = SnapToPerspectiveLines(currentCursorUV);
        }

        float currentSize = isEraser ? eraserSize : brushSize;
        Color cursorCol = isEraser ? Color.black : new Color(drawColor.r, drawColor.g, drawColor.b, 1.0f);
        float radiusUV = (currentSize / overlayTexture.height) * 0.5f;

        if (GUIUtility.hotControl == 0 && overlayTexture != null)
            projectionEffect.UpdateCursor(currentCursorUV, radiusUV, cursorCol);
        else
            projectionEffect.HideCursor();

        if (GUIUtility.hotControl != 0) return; 

        // --- INPUT TRACKING ---
        if (Input.GetMouseButtonDown(0)) 
        {
            lastMousePos = Input.mousePosition;
        }

        bool isClick = Input.GetMouseButton(0);
        bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (isSpace && isClick)
        {
            Vector3 currentPos = Input.mousePosition;
            Vector3 delta = currentPos - lastMousePos;

            float t = projectionEffect.perspective / 100.0f;
            float damping = Mathf.Lerp(0.1f, 1.0f, t);

            float dx = delta.x * sensitivityMultiplier;
            float dy = delta.y * sensitivityMultiplier;

            if (isCtrl) // Zoom
            {
                float proportionalStep = projectionEffect.perspective * 0.05f; 
                float zoomChange = dx * zoomSpeed * proportionalStep;
                projectionEffect.perspective = Mathf.Clamp(projectionEffect.perspective - zoomChange, 1f, 100f);
            }
            else if (isShift) // Roll
            {
                transform.Rotate(Vector3.forward, -dx * rotateSpeed * damping, Space.Self);
            }
            else // Orbit
            {
                transform.Rotate(Vector3.up, -dx * rotateSpeed * damping, Space.World);
                transform.Rotate(Vector3.right, -dy * rotateSpeed * damping, Space.Self);
            }
            
            lastUvPos = null; 
        }
        else if (isClick && !isSpace && !isCtrl) // Paint
        {
            if (!strokeInProgress) 
            { 
                SaveUndoState(); 
                strokeInProgress = true; 
                
                // --- FIX: DESTROY REDO TEXTURES BEFORE CLEARING LIST ---
                CleanupList(redoHistory); 
                // ------------------------------------------------------
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

        if (isClick)
        {
            lastMousePos = Input.mousePosition;
        }
    }

    Vector2 SnapToPerspectiveLines(Vector2 uv)
    {
        float lon = (uv.x - 0.5f) * TWO_PI;
        float lat = (0.5f - uv.y) * Mathf.PI;
        float cosLat = Mathf.Cos(lat);
        Vector3 P = new Vector3(
            cosLat * Mathf.Sin(lon),
            Mathf.Sin(lat),
            cosLat * Mathf.Cos(lon)
        );

        float radStep = gridSpacing * Mathf.Deg2Rad;

        float angY = Mathf.Atan2(P.x, P.z);
        float snapAngY = Mathf.Round(angY / radStep) * radStep;
        Vector3 normY = new Vector3(Mathf.Cos(snapAngY), 0, -Mathf.Sin(snapAngY));
        Vector3 snapP_Y = Vector3.ProjectOnPlane(P, normY).normalized;
        float distY = Vector3.Distance(P, snapP_Y);

        float angX = Mathf.Atan2(P.y, P.z);
        float snapAngX = Mathf.Round(angX / radStep) * radStep;
        Vector3 normX = new Vector3(0, Mathf.Cos(snapAngX), -Mathf.Sin(snapAngX));
        Vector3 snapP_X = Vector3.ProjectOnPlane(P, normX).normalized;
        float distX = Vector3.Distance(P, snapP_X);

        float angZ = Mathf.Atan2(P.y, P.x);
        float snapAngZ = Mathf.Round(angZ / radStep) * radStep;
        Vector3 normZ = new Vector3(-Mathf.Sin(snapAngZ), Mathf.Cos(snapAngZ), 0);
        Vector3 snapP_Z = Vector3.ProjectOnPlane(P, normZ).normalized;
        float distZ = Vector3.Distance(P, snapP_Z);

        Vector3 bestSnap = snapP_Y;
        if (distX < distY && distX < distZ) bestSnap = snapP_X;
        else if (distZ < distY && distZ < distX) bestSnap = snapP_Z;

        float newLon = Mathf.Atan2(bestSnap.x, bestSnap.z);
        float newLat = Mathf.Asin(Mathf.Clamp(bestSnap.y, -1f, 1f));

        Vector2 snappedUV;
        snappedUV.x = Mathf.Repeat((newLon / TWO_PI) + 0.5f, 1.0f);
        snappedUV.y = Mathf.Clamp01(0.5f - (newLat / Mathf.PI));

        return snappedUV;
    }

    void HandleToolInput()
    {
        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Q)) isEraser = false;
        if (Input.GetKeyDown(KeyCode.E)) isEraser = true;
        if (Input.GetKeyDown(KeyCode.G)) useGrid = !useGrid;

        if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) PerformRedo(); else PerformUndo();
        }
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
        Destroy(resultTex); Destroy(compositor);
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
                CleanupList(undoHistory); // Clean memory on load
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
        if (undoHistory.Count > maxUndoSteps) { 
            RenderTexture old = undoHistory[0]; 
            undoHistory.RemoveAt(0); 
            old.Release(); // Properly release memory
            Destroy(old); 
        }
    }
    
    void PerformUndo() {
        if (undoHistory.Count == 0) return;
        RenderTexture redoSnapshot = new RenderTexture(overlayTexture.descriptor);
        Graphics.CopyTexture(overlayTexture, redoSnapshot); redoHistory.Add(redoSnapshot);
        RenderTexture lastState = undoHistory[undoHistory.Count - 1];
        Graphics.CopyTexture(lastState, overlayTexture);
        undoHistory.RemoveAt(undoHistory.Count - 1); 
        lastState.Release(); 
        Destroy(lastState);
    }
    
    void PerformRedo() {
        if (redoHistory.Count == 0) return;
        RenderTexture undoSnapshot = new RenderTexture(overlayTexture.descriptor);
        Graphics.CopyTexture(overlayTexture, undoSnapshot); undoHistory.Add(undoSnapshot);
        RenderTexture lastState = redoHistory[redoHistory.Count - 1];
        Graphics.CopyTexture(lastState, overlayTexture);
        redoHistory.RemoveAt(redoHistory.Count - 1); 
        lastState.Release();
        Destroy(lastState);
    }

    void PaintStroke(Vector2 startUV, Vector2 endUV, bool eraseMode)
    {
        if (eraseMode && eraserMaterial == null) return;
        if (!eraseMode && brushMaterial == null) return;

        RenderTexture.active = overlayTexture;
        GL.PushMatrix(); GL.LoadPixelMatrix(0, 1, 0, 1); 

        if (eraseMode) 
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

    Vector2 ScreenPointToPanoUV(Vector2 screenUV) {
        Vector2 coord = (screenUV - new Vector2(0.5f, 0.5f)) * 2.0f; coord.x *= cam.aspect;
        float r = coord.magnitude; Vector3 rayDir = new Vector3(0, 0, 1);
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
        rayDir = cam.transform.rotation * rayDir; rayDir.Normalize();
        float lon = Mathf.Atan2(rayDir.x, rayDir.z); float lat = Mathf.Asin(Mathf.Clamp(rayDir.y, -1.0f, 1.0f));
        Vector2 panoUV; panoUV.x = Mathf.Repeat(lon / TWO_PI + 0.5f, 1.0f); panoUV.y = Mathf.Clamp01(0.5f - lat / Mathf.PI);
        return panoUV;
    }
}