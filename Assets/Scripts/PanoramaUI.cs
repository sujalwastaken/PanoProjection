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
    
    // For Color Preview
    private Texture2D colorSwatch;

    private bool showUI = true;

    // Dimensions
    private float uiWidth = 450f;
    private float baseHeight = 510f; 
    private float colorPickerHeight = 200f; // Height needed for color controls
    private float gridControlsHeight = 160f; 

    void Start()
    {
        projection = GetComponent<PanoramaProjectionEffect>();
        painter = GetComponent<PanoramaPaintGPU>();
        cam = GetComponent<Camera>();
        
        // Create a simple white texture for drawing the color preview box
        colorSwatch = new Texture2D(1, 1);
        colorSwatch.SetPixel(0, 0, Color.white);
        colorSwatch.Apply();
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
        GUI.backgroundColor = uiBackgroundColor;
        GUI.contentColor = textColor;
        GUI.skin.label.fontSize = 21;

        if (!showUI)
        {
            GUILayout.BeginArea(new Rect(10, 10, 50, 50));
            DrawLabelWithShadow("[W]");
            GUILayout.EndArea();
            return; 
        }

        // --- DYNAMIC HEIGHT CALCULATION ---
        bool showGridControls = painter.showGrid || painter.enableSnapping;
        bool showColorPicker = !painter.isEraser; // Only show color picker for Brush

        float currentHeight = baseHeight;
        if (showGridControls) currentHeight += gridControlsHeight;
        if (showColorPicker) currentHeight += colorPickerHeight;
        
        float padding = 10;

        GUILayout.BeginArea(new Rect(padding, padding, uiWidth, currentHeight), GUI.skin.box);
        GUILayout.BeginVertical();

        // --- Info ---
        DrawLabelWithShadow($"FPS: {Mathf.CeilToInt(fps)}");

        Vector3 rot = cam.transform.eulerAngles;
        float x = (rot.x > 180) ? rot.x - 360 : rot.x;
        float y = rot.y;
        float z = (rot.z > 180) ? rot.z - 360 : rot.z;
        DrawLabelWithShadow($"ROTATION X: {x:F1}  Y: {y:F1}  Z: {z:F1}");

        DrawLabelWithShadow($"H-FOV: {projection.calculatedHorizontalFOV:F1}  V-FOV: {projection.calculatedVerticalFOV:F1}");
        GUILayout.Space(10);

        // --- Tools Status ---
        GUILayout.BeginHorizontal();
        GUI.color = (!painter.isEraser) ? activeToolColor : textColor;
        GUILayout.Label("Brush: [Q]");
        GUI.color = (painter.isEraser) ? activeToolColor : textColor;
        GUILayout.Label("Eraser: [E]");
        GUI.color = (painter.showGrid) ? activeToolColor : textColor;
        GUILayout.Label("Grid: [G]");
        GUI.color = (painter.enableSnapping) ? activeToolColor : textColor;
        GUILayout.Label("Snap: [S]");
        GUI.color = textColor; 
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        
        // --- Instructions ---
        DrawLabelWithShadow("Hide Window: [W]"); 
        DrawLabelWithShadow("Undo: [Ctrl+Z]  |  Redo: [Ctrl+Shift+Z]");
        DrawLabelWithShadow("Save: [Ctrl+S]  |  Load: [Ctrl+D]");
        GUILayout.Space(5);
        DrawLabelWithShadow("Align Grid to View: [Shift+G]");
        DrawLabelWithShadow("Temp Snap: Hold [Shift]"); 

        GUILayout.Space(15);
        
        // --- Controls ---
        DrawLabelWithShadow($"Perspective (Zoom): {projection.perspective:F1}");
        projection.perspective = GUILayout.HorizontalSlider(projection.perspective, 1f, 100f);
        GUILayout.Space(5);

        DrawLabelWithShadow($"Fisheye Distortion: {projection.fisheyePerspective:F1}");
        projection.fisheyePerspective = GUILayout.HorizontalSlider(projection.fisheyePerspective, 0f, 100f);
        GUILayout.Space(5);

        if (painter.isEraser)
        {
            DrawLabelWithShadow($"Eraser Size: {painter.eraserSize:F0}");
            painter.eraserSize = GUILayout.HorizontalSlider(painter.eraserSize, 1f, 200f);
        }
        else
        {
            // --- BRUSH CONTROLS ---
            DrawLabelWithShadow($"Brush Size: {painter.brushSize:F0}");
            painter.brushSize = GUILayout.HorizontalSlider(painter.brushSize, 1f, 200f);
            
            GUILayout.Space(10);
            
            // --- COLOR PICKER ---
            GUILayout.BeginVertical(GUI.skin.box);
            DrawLabelWithShadow("Brush Color");
            
            // --- FIX START: Use GUI.DrawTexture instead of Box ---
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            // 1. Reserve the rectangle space
            Rect cRect = GUILayoutUtility.GetRect(uiWidth - 40, 20);
            
            // 2. Draw the texture stretched to fill that space
            Color oldC = GUI.color;
            GUI.color = painter.drawColor;
            GUI.DrawTexture(cRect, colorSwatch, ScaleMode.StretchToFill);
            GUI.color = oldC;
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            // --- FIX END ---

            // RGB Sliders
            GUILayout.Space(5);
            Color c = painter.drawColor;
            
            GUILayout.BeginHorizontal();
            DrawLabelWithShadow("R", 20); c.r = GUILayout.HorizontalSlider(c.r, 0f, 1f);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            DrawLabelWithShadow("G", 20); c.g = GUILayout.HorizontalSlider(c.g, 0f, 1f);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            DrawLabelWithShadow("B", 20); c.b = GUILayout.HorizontalSlider(c.b, 0f, 1f);
            GUILayout.EndHorizontal();

            painter.drawColor = c;

            // Quick Presets
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Red")) painter.drawColor = Color.red;
            if (GUILayout.Button("Green")) painter.drawColor = Color.green;
            if (GUILayout.Button("Blue")) painter.drawColor = Color.blue;
            if (GUILayout.Button("Black")) painter.drawColor = Color.black;
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        // --- Grid Controls (Conditional) ---
        if (showGridControls)
        {
            GUILayout.Space(15);
            GUI.color = activeToolColor; 
            
            DrawLabelWithShadow($"Grid Spacing: {painter.gridSpacing:F1}");
            painter.gridSpacing = GUILayout.HorizontalSlider(painter.gridSpacing, 2.0f, 45.0f);

            DrawLabelWithShadow($"Grid Thickness: {painter.gridThickness:F2}");
            painter.gridThickness = GUILayout.HorizontalSlider(painter.gridThickness, 0.1f, 5.0f); 

            DrawLabelWithShadow($"Grid Opacity: {painter.gridOpacity:F2}");
            painter.gridOpacity = GUILayout.HorizontalSlider(painter.gridOpacity, 0.0f, 1.0f);

            GUI.color = textColor; 
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawLabelWithShadow(string text, float width = -1)
    {
        GUIContent content = new GUIContent(text);
        Rect rect;
        if(width > 0) 
            rect = GUILayoutUtility.GetRect(content, GUI.skin.label, GUILayout.Width(width));
        else 
            rect = GUILayoutUtility.GetRect(content, GUI.skin.label);

        Color old = GUI.color;
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text);
        GUI.color = old;
        GUI.Label(rect, text);
    }
}