using UnityEngine;

[RequireComponent(typeof(PanoramaProjectionEffect))]
[RequireComponent(typeof(PanoramaPaintGPU))]
public class PanoramaUI : MonoBehaviour
{
    [Header("UI Settings")]
    public float scrollSensitivity = 5.0f;
    public Color uiBackgroundColor = new Color(0, 0, 0, 0.5f);
    public Color textColor = Color.white;
    public Color activeToolColor = Color.green; 

    private PanoramaProjectionEffect projection;
    private PanoramaPaintGPU painter;
    private Camera cam;
    private float fps;
    private float deltaTime = 0.0f;
    
    // Textures for UI
    private Texture2D colorSwatch;
    private Texture2D hueRainbowTexture;
    private GUIStyle hueSliderStyle;

    private bool showUI = true;

    // Dimensions
    private float uiWidth = 280f; 

    // --- DARK THEME STYLES ---
    private GUIStyle panelStyle;
    private GUIStyle headerStyle;
    private GUIStyle toolbarButtonStyle;
    private GUIStyle toolbarActiveStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueLabelStyle;
    private GUIStyle darkSliderStyle;
    private GUIStyle darkSliderThumbStyle;
    private bool stylesInitialized = false;

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; ++i) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    private Texture2D MakeBorderTex(int width, int height, Color bg, Color borderCol, int border)
    {
        Color[] pix = new Color[width * height];
        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                if (x < border || x >= width - border || y < border || y >= height - border)
                    pix[y * width + x] = borderCol;
                else
                    pix[y * width + x] = bg;
            }
        }
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    void InitStyles()
    {
        if (stylesInitialized) return;
        stylesInitialized = true;
        
        Color panelBg = new Color(0.18f, 0.18f, 0.18f, 0.98f);
        Color headerBg = new Color(0.12f, 0.12f, 0.12f, 1f);
        Color borderNormal = new Color(0.08f, 0.08f, 0.08f, 1f);
        
        Color btnNormal = new Color(0.22f, 0.22f, 0.22f, 1f);
        Color btnHover = new Color(0.28f, 0.28f, 0.28f, 1f);
        Color btnActive = new Color(0.15f, 0.5f, 0.7f, 1f); 
        Color textNormal = new Color(0.85f, 0.85f, 0.85f, 1f);
        Color textMuted = new Color(0.65f, 0.65f, 0.65f, 1f);

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = MakeBorderTex(16, 16, panelBg, borderNormal, 1);
        panelStyle.padding = new RectOffset(4, 4, 4, 4);
        panelStyle.margin = new RectOffset(0, 0, 0, 0);

        headerStyle = new GUIStyle(GUI.skin.label);
        headerStyle.normal.background = MakeBorderTex(16, 16, headerBg, borderNormal, 1);
        headerStyle.normal.textColor = Color.white;
        headerStyle.fontSize = 11;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.alignment = TextAnchor.MiddleLeft;
        headerStyle.padding = new RectOffset(6, 6, 4, 4);
        headerStyle.margin = new RectOffset(0, 0, 0, 4);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = textNormal;
        labelStyle.fontSize = 11;
        labelStyle.margin = new RectOffset(4, 4, 2, 2);
        
        valueLabelStyle = new GUIStyle(labelStyle);
        valueLabelStyle.alignment = TextAnchor.MiddleRight;
        valueLabelStyle.normal.textColor = textMuted;

        toolbarButtonStyle = new GUIStyle(GUI.skin.button);
        toolbarButtonStyle.normal.background = MakeBorderTex(16, 16, btnNormal, borderNormal, 1);
        toolbarButtonStyle.hover.background = MakeBorderTex(16, 16, btnHover, borderNormal, 1);
        toolbarButtonStyle.normal.textColor = textNormal;
        toolbarButtonStyle.fontSize = 11;
        toolbarButtonStyle.alignment = TextAnchor.MiddleCenter;
        toolbarButtonStyle.margin = new RectOffset(0, 0, 0, 0);
        toolbarButtonStyle.padding = new RectOffset(4, 4, 4, 4);

        toolbarActiveStyle = new GUIStyle(toolbarButtonStyle);
        toolbarActiveStyle.normal.background = MakeBorderTex(16, 16, btnActive, borderNormal, 1);
        toolbarActiveStyle.normal.textColor = Color.white;
        toolbarActiveStyle.hover.background = MakeBorderTex(16, 16, new Color(0.2f, 0.6f, 0.8f, 1f), borderNormal, 1);

        darkSliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
        darkSliderStyle.normal.background = MakeBorderTex(16, 16, new Color(0.1f, 0.1f, 0.1f, 1f), borderNormal, 1);
        darkSliderStyle.fixedHeight = 4;
        darkSliderStyle.margin = new RectOffset(4, 4, 6, 6);
        
        darkSliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
        darkSliderThumbStyle.normal.background = MakeBorderTex(16, 16, new Color(0.4f, 0.4f, 0.4f, 1f), borderNormal, 1);
        darkSliderThumbStyle.hover.background = MakeTex(16, 16, new Color(0.6f, 0.6f, 0.6f, 1f));
        darkSliderThumbStyle.fixedWidth = 10;
        darkSliderThumbStyle.fixedHeight = 10;
    }

    void Start()
    {
        projection = GetComponent<PanoramaProjectionEffect>();
        painter = GetComponent<PanoramaPaintGPU>();
        cam = GetComponent<Camera>();
        
        // 1. Create White Swatch
        colorSwatch = new Texture2D(1, 1);
        colorSwatch.SetPixel(0, 0, Color.white);
        colorSwatch.Apply();

        // 2. Create Rainbow Texture for the Slider
        hueRainbowTexture = new Texture2D(128, 1);
        for (int i = 0; i < 128; i++) {
            hueRainbowTexture.SetPixel(i, 0, Color.HSVToRGB((float)i / 128f, 1, 1));
        }
        hueRainbowTexture.Apply();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.W)) showUI = !showUI;
                                
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        fps = 1.0f / deltaTime;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            projection.perspective -= scroll * scrollSensitivity;
            projection.perspective = Mathf.Clamp(projection.perspective, 1f, 100f);
        }
    }

    void OnGUI()
    {
        InitStyles();

        // Init Custom Style once
        if (hueSliderStyle == null) {
            hueSliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
            hueSliderStyle.normal.background = hueRainbowTexture;
        }

        if (!showUI)
        {
            GUILayout.BeginArea(new Rect(10, 10, 50, 50));
            GUILayout.Label("[W]", labelStyle);
            GUILayout.EndArea();
            return; 
        }

        // --- DYNAMIC HEIGHT CALCULATION ---
        float currentHeight = Screen.height - 20; // Just fill vertically for tools
        uiWidth = 280f; // Professional slim width
        
        GUILayout.BeginArea(new Rect(10, 10, uiWidth, currentHeight));
        GUILayout.BeginVertical(panelStyle);

        // --- HEADER ---
        GUILayout.Label("PANO STUDIO", headerStyle);

        // --- TOOLBAR ---
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Brush [Q]", !painter.isEraser ? toolbarActiveStyle : toolbarButtonStyle)) painter.isEraser = false;
        if (GUILayout.Button("Eraser [E]", painter.isEraser ? toolbarActiveStyle : toolbarButtonStyle)) painter.isEraser = true;
        if (GUILayout.Button("Grid [G]", painter.showGrid ? toolbarActiveStyle : toolbarButtonStyle)) painter.showGrid = !painter.showGrid;
        if (GUILayout.Button("Snap [S]", painter.enableSnapping ? toolbarActiveStyle : toolbarButtonStyle)) painter.enableSnapping = !painter.enableSnapping;
        GUILayout.EndHorizontal();

        GUILayout.Space(8);

        // --- PROPERTIES SECTION ---
        GUILayout.Label("VIEWPORT", headerStyle);
        DrawRow("Zoom (FOV)", $"{projection.perspective:F1}");
        projection.perspective = GUILayout.HorizontalSlider(projection.perspective, 1f, 100f, darkSliderStyle, darkSliderThumbStyle);
        
        DrawRow("Distortion", $"{projection.fisheyePerspective:F1}");
        projection.fisheyePerspective = GUILayout.HorizontalSlider(projection.fisheyePerspective, 0f, 100f, darkSliderStyle, darkSliderThumbStyle);

        Vector3 rot = cam.transform.eulerAngles;
        DrawRow("Rotation", $"{(rot.x>180?rot.x-360:rot.x):F0}°  {rot.y:F0}°  {(rot.z>180?rot.z-360:rot.z):F0}°");
        
        GUILayout.Space(8);

        GUILayout.Label(painter.isEraser ? "ERASER" : "BRUSH", headerStyle);
        if (painter.isEraser)
        {
            DrawRow("Size", $"{painter.eraserSize:F0}");
            painter.eraserSize = GUILayout.HorizontalSlider(painter.eraserSize, 1f, 200f, darkSliderStyle, darkSliderThumbStyle);
        }
        else
        {
            DrawRow("Size", $"{painter.brushSize:F0}");
            painter.brushSize = GUILayout.HorizontalSlider(painter.brushSize, 1f, 200f, darkSliderStyle, darkSliderThumbStyle);
            
            GUILayout.BeginHorizontal();
            painter.useSmartBrush = GUILayout.Toggle(painter.useSmartBrush, " Scale with Zoom", labelStyle);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            
            // Color Picker Group
            GUILayout.BeginVertical(panelStyle);
            
            float h, s, v;
            Color.RGBToHSV(painter.drawColor, out h, out s, out v);
            
            float newH = GUILayout.HorizontalSlider(h, 0f, 1f, hueSliderStyle, GUI.skin.horizontalSliderThumb);
            if (Mathf.Abs(newH - h) > 0.001f) painter.drawColor = Color.HSVToRGB(newH, 1f, 1f);

            GUILayout.BeginHorizontal();
            Rect cRect = GUILayoutUtility.GetRect(uiWidth - 40, 16);
            Color oldC = GUI.color; GUI.color = painter.drawColor;
            GUI.DrawTexture(cRect, colorSwatch, ScaleMode.StretchToFill);
            GUI.color = oldC;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("R", toolbarButtonStyle)) painter.drawColor = Color.red;
            if (GUILayout.Button("G", toolbarButtonStyle)) painter.drawColor = Color.green;
            if (GUILayout.Button("B", toolbarButtonStyle)) painter.drawColor = Color.blue;
            if (GUILayout.Button("W", toolbarButtonStyle)) painter.drawColor = Color.white;
            if (GUILayout.Button("K", toolbarButtonStyle)) painter.drawColor = Color.black;
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        GUILayout.Space(8);

        if (painter.showGrid || painter.enableSnapping)
        {
            GUILayout.Label("GRID & SNAPPING", headerStyle);
            
            DrawRow("Subdivisions", $"{painter.gridSubdivisions}");
            painter.gridSubdivisions = (int)GUILayout.HorizontalSlider(painter.gridSubdivisions, 1, 32, darkSliderStyle, darkSliderThumbStyle);

            DrawRow("Thickness", $"{painter.gridThickness:F1}");
            painter.gridThickness = GUILayout.HorizontalSlider(painter.gridThickness, 0.1f, 5.0f, darkSliderStyle, darkSliderThumbStyle); 

            DrawRow("Opacity", $"{painter.gridOpacity:F2}");
            painter.gridOpacity = GUILayout.HorizontalSlider(painter.gridOpacity, 0.0f, 1.0f, darkSliderStyle, darkSliderThumbStyle);

            DrawRow("Depth Fade", $"{painter.gridFalloff:F2}");
            painter.gridFalloff = GUILayout.HorizontalSlider(painter.gridFalloff, 0.001f, 0.5f, darkSliderStyle, darkSliderThumbStyle);

            GUILayout.Space(4);
            if (GUILayout.Button($"Axis Mode: {painter.activeGridAxis.ToString()}", toolbarButtonStyle))
            {
                painter.activeGridAxis = (PanoramaPaintGPU.GridAxisMode)(((int)painter.activeGridAxis + 1) % 7);
            }
            
            painter.useDiagonalSnapping = GUILayout.Toggle(painter.useDiagonalSnapping, " 45° Diagonals [F]", labelStyle);
            GUILayout.Space(8);
        }

        GUILayout.FlexibleSpace();
        
        // --- SYSTEM INFO ---
        GUILayout.Label("SYSTEM", headerStyle);
        DrawRow("Performance", $"{Mathf.CeilToInt(fps)} FPS");
        if (MemoryTracker.Instance != null)
        {
            float ramMB = MemoryTracker.Instance.PrivateMemoryMB;
            float ramPct = MemoryTracker.Instance.RamUsagePercent;
            DrawRow("Memory", $"{ramMB:F1} MB ({ramPct:F0}%)");
        }
        
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawRow(string label, string val)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(val, valueLabelStyle);
        GUILayout.EndHorizontal();
    }
}