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

    // Toggle State
    private bool showUI = true;

    void Start()
    {
        projection = GetComponent<PanoramaProjectionEffect>();
        painter = GetComponent<PanoramaPaintGPU>();
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        // --- Toggle UI Hotkey ---
        if (Input.GetKeyDown(KeyCode.W))
        {
            showUI = !showUI;
        }

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

        // --- HIDDEN MODE ---
        if (!showUI)
        {
            // Just show "W" in the corner
            GUILayout.BeginArea(new Rect(10, 10, 50, 50));
            DrawLabelWithShadow("[W]");
            GUILayout.EndArea();
            return; // Stop drawing the rest
        }

        // --- FULL UI MODE ---
        float width = 400;
        float height = 500;
        float padding = 10;

        GUILayout.BeginArea(new Rect(padding, padding, width, height), GUI.skin.box);
        GUILayout.BeginVertical();

        // --- Info ---
        DrawLabelWithShadow($"FPS: {Mathf.CeilToInt(fps)}");

        // --- Camera Rotation ---
        Vector3 rot = cam.transform.eulerAngles;
        float x = (rot.x > 180) ? rot.x - 360 : rot.x;
        float y = rot.y;
        float z = (rot.z > 180) ? rot.z - 360 : rot.z;
        DrawLabelWithShadow($"ROTATION X: {x:F1}  Y: {y:F1}  Z: {z:F1}");

        DrawLabelWithShadow($"H-FOV: {projection.calculatedHorizontalFOV:F1}  V-FOV: {projection.calculatedVerticalFOV:F1}");
        GUILayout.Space(10);

        // --- Tools Status ---
        GUILayout.BeginHorizontal();
        GUI.color = painter.isEraser ? textColor : activeToolColor;
        GUILayout.Label("[Q] Brush");
        GUI.color = painter.isEraser ? activeToolColor : textColor;
        GUILayout.Label("[E] Eraser");
        GUI.color = textColor;
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        DrawLabelWithShadow("Hide Window: [W]"); // Added reminder
        DrawLabelWithShadow("Undo: Ctrl+Z");
        DrawLabelWithShadow("Redo: Ctrl+Shift+Z");
        DrawLabelWithShadow("Save: Ctrl+S");
        DrawLabelWithShadow("Load: Ctrl+D");

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