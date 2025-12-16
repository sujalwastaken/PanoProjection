using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class PanoramaProjectionEffect : MonoBehaviour
{
    [Header("Shader Reference")]
    public Shader projectionShader;

    [Header("Textures")]
    public Texture panoramaTexture;
    [Tooltip("The transparent layer we draw on")]
    public Texture overlayTexture;

    [Header("Projection Controls")]
    [Range(1f, 100f)] public float perspective = 50f;
    [Range(0f, 100f)] public float fisheyePerspective = 0f;
    [Range(1f, 179f)] public float minFov = 30f;
    [Range(1f, 179f)] public float maxFov = 160f;

    // These are hidden from the default inspector so we can draw them manually as Read-Only
    [HideInInspector] public float calculatedVerticalFOV;
    [HideInInspector] public float calculatedHorizontalFOV;

    private Material panoramaMaterial;
    private Camera cam;

    // Shader Property IDs (Must match Shader exactly)
    private static readonly int PanoramaTexId = Shader.PropertyToID("_PanoramaTex");
    private static readonly int OverlayTexId = Shader.PropertyToID("_OverlayTex");
    private static readonly int PerspectiveId = Shader.PropertyToID("_Perspective");
    private static readonly int FisheyePerspectiveId = Shader.PropertyToID("_FisheyePerspective");
    private static readonly int MinFovId = Shader.PropertyToID("_MinFov");
    private static readonly int MaxFovId = Shader.PropertyToID("_MaxFov");
    private static readonly int CameraRotationId = Shader.PropertyToID("_CameraRotation");
    private static readonly int AspectRatioId = Shader.PropertyToID("_AspectRatio");

    // Cursor Props
    private static readonly int CursorUVId = Shader.PropertyToID("_CursorUV");
    private static readonly int CursorRadiusId = Shader.PropertyToID("_CursorRadius");
    private static readonly int CursorColorId = Shader.PropertyToID("_CursorColor");

    void OnEnable()
    {
        cam = GetComponent<Camera>();
        CreateMaterialIfNeeded();
    }

    public void UpdateCursor(Vector2 uvPosition, float radiusUV_Y, Color color)
    {
        if (panoramaMaterial == null) return;
        panoramaMaterial.SetVector(CursorUVId, uvPosition);
        panoramaMaterial.SetFloat(CursorRadiusId, radiusUV_Y);
        panoramaMaterial.SetColor(CursorColorId, color);
    }

    public void HideCursor()
    {
        if (panoramaMaterial == null) return;
        panoramaMaterial.SetVector(CursorUVId, new Vector2(-10, -10));
    }

    void CreateMaterialIfNeeded()
    {
        if (panoramaMaterial != null) return;
        
        // Try to find shader if not assigned
        if (projectionShader == null) projectionShader = Shader.Find("Hidden/PanoramaProjection");
        
        if (projectionShader != null) 
        {
            panoramaMaterial = new Material(projectionShader);
            panoramaMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        else
        {
            Debug.LogWarning("Panorama Shader not found! Make sure the shader file is named 'PanoramaProjection' inside a 'Resources' or 'Shaders' folder.");
        }
    }

    void UpdateMaterialProperties()
    {
        if (panoramaMaterial == null) return;

        panoramaMaterial.SetTexture(PanoramaTexId, panoramaTexture);
        panoramaMaterial.SetTexture(OverlayTexId, overlayTexture);
        panoramaMaterial.SetFloat(PerspectiveId, perspective);
        panoramaMaterial.SetFloat(FisheyePerspectiveId, fisheyePerspective);

        float min = Mathf.Min(minFov, maxFov);
        float max = Mathf.Max(minFov, maxFov);
        panoramaMaterial.SetFloat(MinFovId, min);
        panoramaMaterial.SetFloat(MaxFovId, max);

        if (cam != null)
        {
            panoramaMaterial.SetMatrix(CameraRotationId, Matrix4x4.Rotate(cam.transform.rotation));
            panoramaMaterial.SetFloat(AspectRatioId, cam.aspect);
            CalculateCurrentFOV(min, max, cam.aspect);
        }
    }

    void CalculateCurrentFOV(float min, float max, float aspect)
    {
        float t = (perspective - 1.0f) / 99.0f;
        float fisheyeAmount = fisheyePerspective / 100.0f;
        
        // Interpolate scaling factor
        float currentScale = Mathf.Lerp(Mathf.Tan(min * 0.5f * Mathf.Deg2Rad), Mathf.Tan(max * 0.5f * Mathf.Deg2Rad), t);
        
        // Calculate Fisheye Blend amount
        float blend = fisheyeAmount * fisheyeAmount * (3.0f - 2.0f * fisheyeAmount);

        // Calculate Vertical FOV
        float thetaV = Mathf.Lerp(Mathf.Atan(currentScale), 2.0f * Mathf.Atan(currentScale), blend);
        calculatedVerticalFOV = (2.0f * thetaV) * Mathf.Rad2Deg;

        // Calculate Horizontal FOV
        float thetaH = Mathf.Lerp(Mathf.Atan(currentScale * aspect), 2.0f * Mathf.Atan(currentScale * aspect), blend);
        calculatedHorizontalFOV = (2.0f * thetaH) * Mathf.Rad2Deg;
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (panoramaMaterial == null || panoramaTexture == null)
        {
            Graphics.Blit(source, destination);
            return;
        }
        UpdateMaterialProperties();
        Graphics.Blit(source, destination, panoramaMaterial);
    }

    void OnDisable()
    {
        if (panoramaMaterial != null) DestroyImmediate(panoramaMaterial);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(PanoramaProjectionEffect))]
public class PanoramaProjectionEffectEditor : Editor
{
    SerializedProperty calculatedVerticalFOVProp;
    SerializedProperty calculatedHorizontalFOVProp;

    void OnEnable()
    {
        calculatedVerticalFOVProp = serializedObject.FindProperty("calculatedVerticalFOV");
        calculatedHorizontalFOVProp = serializedObject.FindProperty("calculatedHorizontalFOV");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 1. Draw the Default Inspector (This handles all normal sliders automatically)
        DrawDefaultInspector();

        // 2. Draw the Calculated Fields separately (Read Only)
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Calculated FOVs (Read Only)", EditorStyles.boldLabel);

        EditorGUI.BeginDisabledGroup(true); // Start Read-Only Mode
        {
            if (calculatedHorizontalFOVProp != null)
                EditorGUILayout.FloatField("Horizontal FOV", calculatedHorizontalFOVProp.floatValue);
            if (calculatedVerticalFOVProp != null)
                EditorGUILayout.FloatField("Vertical FOV", calculatedVerticalFOVProp.floatValue);
            
        }
        EditorGUI.EndDisabledGroup(); // End Read-Only Mode

        serializedObject.ApplyModifiedProperties();
    }
}
#endif