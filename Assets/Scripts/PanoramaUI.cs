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

    private bool showUI = true;

    // Dimensions
    private float uiWidth = 450f;
    private float baseHeight = 510f; // Height for standard tools
    private float gridControlsHeight = 160f; // Extra height for grid sliders

    void Start()
    {
        projection = GetComponent<PanoramaProjectionEffect>();
        painter = GetComponent<PanoramaPaintGPU>();
        cam = GetComponent<Camera>();
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
        // Check if we need to show the extra grid controls
        bool showGridControls = painter.showGrid || painter.enableSnapping;
        float currentHeight = showGridControls ? (baseHeight + gridControlsHeight) : baseHeight;
        
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
        
        // Brush
        GUI.color = (!painter.isEraser) ? activeToolColor : textColor;
        GUILayout.Label("[Q] Brush");

        // Eraser
        GUI.color = (painter.isEraser) ? activeToolColor : textColor;
        GUILayout.Label("[E] Eraser");

        // Grid Vis
        GUI.color = (painter.showGrid) ? activeToolColor : textColor;
        GUILayout.Label("[G] Grid");

        // Grid Snap
        GUI.color = (painter.enableSnapping) ? activeToolColor : textColor;
        GUILayout.Label("[S] Snap");

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
            DrawLabelWithShadow($"Brush Size: {painter.brushSize:F0}");
            painter.brushSize = GUILayout.HorizontalSlider(painter.brushSize, 1f, 200f);
        }

        // --- Grid Controls (Conditional) ---
        if (showGridControls)
        {
            GUILayout.Space(15);
            GUI.color = activeToolColor; // Tint section Green
            
            DrawLabelWithShadow($"Grid Spacing: {painter.gridSpacing:F1}");
            painter.gridSpacing = GUILayout.HorizontalSlider(painter.gridSpacing, 2.0f, 45.0f);

            DrawLabelWithShadow($"Grid Thickness: {painter.gridThickness:F2}");
            painter.gridThickness = GUILayout.HorizontalSlider(painter.gridThickness, 0.1f, 5.0f); 

            DrawLabelWithShadow($"Grid Opacity: {painter.gridOpacity:F2}");
            painter.gridOpacity = GUILayout.HorizontalSlider(painter.gridOpacity, 0.0f, 1.0f);

            GUI.color = textColor; // Reset
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawLabelWithShadow(string text)
    {
        var rect = GUILayoutUtility.GetRect(new GUIContent(text), GUI.skin.label);
        Color old = GUI.color;
        GUI.color = Color.black;
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text);
        GUI.color = old;
        GUI.Label(rect, text);
    }
}