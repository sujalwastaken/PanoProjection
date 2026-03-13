using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(PanoramaProjectionEffect))]
[RequireComponent(typeof(PanoramaPaintGPU))]
public class PanoramaUI : MonoBehaviour
{
    [Header("UI Settings")]
    public float scrollSensitivity = 5.0f;

    private PanoramaProjectionEffect proj;
    private PanoramaPaintGPU paint;
    private Camera cam;
    private float fps, deltaTime;
    
    // Textures
    private Texture2D white, hueTex, panelBg, sectionBg, sliderBg, thumbTex, btnBg, btnHover, sepTex;

    // Styles
    private GUIStyle panelSty, headerSty, lblSty, smallSty, valSty, secSty, btnSty, smBtnSty;
    private GUIStyle togSty, hueSldSty, sldSty, thumbSty, barSty, helpBgSty, helpTitleSty;
    private GUIStyle helpKeySty, helpDescSty, mmSty, colHeaderSty, secTitleSty;
    private bool styInit;

    private bool showUI = true, showHelp = false, showMinimap = true;
    private bool colInfo, colCtrl, colColor, colGrid, colView; // collapsed states
    private Vector2 scrollPos;

    // Colors
    static readonly Color C_PANEL = new Color(0.06f,0.06f,0.08f,0.94f);
    static readonly Color C_SEC = new Color(0.10f,0.10f,0.14f,0.85f);
    static readonly Color C_ACC = new Color(0.3f,0.85f,0.65f,1f);
    static readonly Color C_WARM = new Color(1f,0.55f,0.3f,1f);
    static readonly Color C_BLUE = new Color(0.4f,0.7f,1f,1f);
    static readonly Color C_TXT = new Color(0.92f,0.92f,0.95f,1f);
    static readonly Color C_DIM = new Color(0.55f,0.55f,0.62f,1f);
    static readonly Color C_MUT = new Color(0.35f,0.35f,0.4f,1f);
    static readonly Color C_TRK = new Color(0.18f,0.18f,0.22f,1f);
    static readonly Color C_BTN = new Color(0.13f,0.13f,0.18f,0.95f);
    static readonly Color C_EYEDROP = new Color(0.9f,0.7f,0.2f,1f);

    const float W = 310f, PAD = 10f, MM_W = 240f, MM_H = 120f;

    void Start()
    {
        proj = GetComponent<PanoramaProjectionEffect>();
        paint = GetComponent<PanoramaPaintGPU>();
        cam = GetComponent<Camera>();
        CreateTextures();
    }

    void CreateTextures()
    {
        white = Solid(Color.white);
        hueTex = new Texture2D(256, 1);
        for (int i = 0; i < 256; i++) hueTex.SetPixel(i, 0, Color.HSVToRGB(i/256f, 1, 1));
        hueTex.wrapMode = TextureWrapMode.Clamp; hueTex.Apply();
        panelBg = Solid(C_PANEL); sectionBg = Solid(C_SEC); sliderBg = Solid(C_TRK);
        thumbTex = Circle(16, C_ACC); btnBg = Solid(C_BTN);
        btnHover = Solid(new Color(0.20f,0.20f,0.26f,0.98f));
        sepTex = GradientSep(256);
    }

    Texture2D Solid(Color c) { var t=new Texture2D(2,2); var a=new Color[4]; for(int i=0;i<4;i++) a[i]=c; t.SetPixels(a); t.Apply(); return t; }
    
    Texture2D Circle(int s, Color c)
    {
        var t = new Texture2D(s,s,TextureFormat.ARGB32,false);
        float ctr=s*0.5f, r=ctr-1;
        for(int y=0;y<s;y++) for(int x=0;x<s;x++) {
            float d=Vector2.Distance(new Vector2(x,y),new Vector2(ctr,ctr));
            t.SetPixel(x,y,new Color(c.r,c.g,c.b,c.a*Mathf.Clamp01(1f-(d-r+1.5f))));
        }
        t.Apply(); return t;
    }

    Texture2D GradientSep(int w)
    {
        var t = new Texture2D(w,1,TextureFormat.ARGB32,false);
        for(int i=0;i<w;i++) t.SetPixel(i,0,new Color(C_ACC.r,C_ACC.g,C_ACC.b,Mathf.Sin((float)i/w*Mathf.PI)*0.4f));
        t.Apply(); return t;
    }

    void InitStyles()
    {
        if (styInit) return; styInit = true;
        panelSty = Box(panelBg, new RectOffset(0,0,0,0));
        secSty = Box(sectionBg, new RectOffset(10,10,6,6)); secSty.margin = new RectOffset(6,6,2,2);
        headerSty = Lbl(11, FontStyle.Bold, C_ACC);
        colHeaderSty = Btn(11, FontStyle.Bold, C_ACC); colHeaderSty.alignment = TextAnchor.MiddleLeft;
        colHeaderSty.padding = new RectOffset(10,10,5,5); colHeaderSty.margin = new RectOffset(6,6,2,0);
        colHeaderSty.normal.background = Solid(new Color(0.08f,0.08f,0.11f,0.95f));
        colHeaderSty.hover.background = Solid(new Color(0.12f,0.12f,0.16f,0.95f));
        secTitleSty = Lbl(10, FontStyle.Bold, C_MUT);
        lblSty = Lbl(11, FontStyle.Normal, C_TXT);
        smallSty = Lbl(10, FontStyle.Normal, C_DIM);
        valSty = Lbl(11, FontStyle.Normal, C_ACC); valSty.alignment = TextAnchor.MiddleRight;
        btnSty = Btn(11, FontStyle.Bold, C_TXT); smBtnSty = Btn(10, FontStyle.Bold, C_TXT);
        smBtnSty.padding = new RectOffset(4,4,3,3);
        togSty = new GUIStyle(GUI.skin.toggle); togSty.fontSize=10;
        togSty.normal.textColor=C_DIM; togSty.onNormal.textColor=C_ACC;
        hueSldSty = new GUIStyle(GUI.skin.horizontalSlider); hueSldSty.normal.background=hueTex; hueSldSty.fixedHeight=12;
        sldSty = new GUIStyle(GUI.skin.horizontalSlider); sldSty.normal.background=sliderBg; sldSty.fixedHeight=5;
        thumbSty = new GUIStyle(GUI.skin.horizontalSliderThumb); thumbSty.normal.background=thumbTex; thumbSty.fixedWidth=14; thumbSty.fixedHeight=14;
        barSty = Box(panelBg, new RectOffset(10,10,3,3)); barSty.normal.textColor=C_DIM; barSty.fontSize=10; barSty.alignment=TextAnchor.MiddleCenter;
        helpBgSty = Box(Solid(new Color(0.03f,0.03f,0.05f,0.96f)), new RectOffset(30,30,20,20));
        helpTitleSty = Lbl(20, FontStyle.Bold, C_ACC); helpTitleSty.alignment=TextAnchor.MiddleCenter;
        helpKeySty = Lbl(12, FontStyle.Bold, C_ACC); helpKeySty.alignment=TextAnchor.MiddleRight; helpKeySty.fixedWidth=180;
        helpDescSty = Lbl(12, FontStyle.Normal, C_TXT);
        mmSty = Box(Solid(new Color(0,0,0,0.75f)), new RectOffset(3,3,3,3));
    }

    GUIStyle Box(Texture2D bg, RectOffset pad) { var s=new GUIStyle(GUI.skin.box); s.normal.background=bg; s.padding=pad; return s; }
    GUIStyle Lbl(int sz, FontStyle fs, Color c) { var s=new GUIStyle(GUI.skin.label); s.fontSize=sz; s.fontStyle=fs; s.normal.textColor=c; return s; }
    GUIStyle Btn(int sz, FontStyle fs, Color c) { var s=new GUIStyle(GUI.skin.button); s.fontSize=sz; s.fontStyle=fs; s.normal.textColor=c; s.hover.textColor=C_ACC; s.normal.background=btnBg; s.hover.background=btnHover; s.active.background=btnHover; s.padding=new RectOffset(6,6,4,4); s.margin=new RectOffset(2,2,2,2); return s; }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.W)) showUI = !showUI;
        if (Input.GetKeyDown(KeyCode.H)) showHelp = !showHelp;
        if (Input.GetKeyDown(KeyCode.M)) showMinimap = !showMinimap;
        if (Input.GetKeyDown(KeyCode.Escape) && showHelp) showHelp = false;
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;
        fps = 1f / deltaTime;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            Vector2 mp = Input.mousePosition; mp.y = Screen.height - mp.y;
            if (!(showUI && new Rect(PAD, PAD, W, Screen.height - 60).Contains(mp)))
            {
                proj.perspective = Mathf.Clamp(proj.perspective - scroll * scrollSensitivity, 1f, 100f);
            }
        }
    }

    void OnGUI()
    {
        InitStyles();
        if (showHelp) { DrawHelp(); return; }
        DrawStatusBar();
        if (showMinimap && proj.panoramaTexture != null) DrawMinimap();
        if (!showUI) { GUI.Label(new Rect(PAD,PAD,140,20), "[W] Show Panel", smallSty); return; }

        // Panel with scroll
        float pH = Screen.height - 50;
        Rect panel = new Rect(PAD, PAD, W, pH);
        GUI.Box(panel, GUIContent.none, panelSty);
        GUILayout.BeginArea(panel);
        scrollPos = GUILayout.BeginScrollView(scrollPos, false, false, GUILayout.Width(W), GUILayout.Height(pH));
        GUILayout.BeginVertical(GUILayout.Width(W - 22));
        GUILayout.Space(8);

        // Title
        H(12); GUIStyle ts = new GUIStyle(headerSty); ts.fontSize=14;
        GUILayout.Label("◈  PANORAMA STUDIO", ts); EndH();
        Sep();

        // Tools
        H(6);
        TBtn("✎ Brush", !paint.isEraser && !paint.isEyedropper, ()=>{paint.isEraser=false;paint.isEyedropper=false;});
        TBtn("◌ Eraser", paint.isEraser, ()=>{paint.isEraser=true;paint.isEyedropper=false;});
        TBtn("◉ Pick", paint.isEyedropper, ()=>{paint.isEyedropper=true;paint.isEraser=false;}, C_EYEDROP);
        EndH(6);
        H(6);
        TBtn("▦ Grid", paint.showGrid, ()=>paint.showGrid=!paint.showGrid);
        TBtn("⊕ Snap", paint.enableSnapping, ()=>paint.enableSnapping=!paint.enableSnapping);
        TBtn("⊹ Cross", proj.showCrosshair, ()=>proj.showCrosshair=!proj.showCrosshair, C_BLUE);
        EndH(6);

        GUILayout.Space(2);

        // Info
        if (ColHeader("INFO", ref colInfo)) {
            GUILayout.BeginVertical(secSty);
            InfoRow("FPS", $"{Mathf.CeilToInt(fps)}");
            Vector3 r=cam.transform.eulerAngles; float rx=r.x>180?r.x-360:r.x;
            InfoRow("ROT", $"X:{rx:F0} Y:{r.y:F0} Z:{(r.z>180?r.z-360:r.z):F0}");
            InfoRow("FOV", $"H:{proj.calculatedHorizontalFOV:F0}° V:{proj.calculatedVerticalFOV:F0}°");
            GUILayout.EndVertical();
        }

        // Controls
        if (ColHeader("CONTROLS", ref colCtrl)) {
            GUILayout.BeginVertical(secSty);
            proj.perspective = Slider("Zoom", proj.perspective, 1f, 100f);
            proj.fisheyePerspective = Slider("Fisheye", proj.fisheyePerspective, 0f, 100f);
            proj.vignetteIntensity = Slider("Vignette", proj.vignetteIntensity, 0f, 1f);
            ThinSep();
            if (paint.isEraser) {
                paint.eraserSize = Slider("Size", paint.eraserSize, 1f, 200f);
                paint.hardness = Slider("Hardness", paint.hardness, 0f, 1f);
            } else {
                paint.brushSize = Slider("Size", paint.brushSize, 1f, 200f);
                paint.hardness = Slider("Hardness", paint.hardness, 0f, 1f);
                paint.brushOpacity = Slider("Opacity", paint.brushOpacity, 0f, 1f);
                paint.brushSpacing = Slider("Spacing", paint.brushSpacing, 0.01f, 0.5f);
                paint.useSmartBrush = GUILayout.Toggle(paint.useSmartBrush, " Smart Brush", togSty);
            }
            GUILayout.EndVertical();
        }

        // Color
        if (!paint.isEraser && ColHeader("COLOR", ref colColor)) {
            GUILayout.BeginVertical(secSty);
            float h,s,v; Color.RGBToHSV(paint.drawColor, out h, out s, out v);
            GUILayout.Label("Hue", secTitleSty);
            h = GUILayout.HorizontalSlider(h, 0f, 1f, hueSldSty, thumbSty); GUILayout.Space(3);
            s = Slider("Saturation", s, 0f, 1f);
            v = Slider("Brightness", v, 0f, 1f);
            paint.drawColor = Color.HSVToRGB(h, s, v);
            // Preview
            Rect cr = GUILayoutUtility.GetRect(W-56, 14);
            Color oc = GUI.color; GUI.color = paint.drawColor;
            GUI.DrawTexture(cr, white); GUI.color = oc;
            // Presets
            GUILayout.Space(2); GUILayout.Label("Presets", secTitleSty);
            GUILayout.BeginHorizontal();
            foreach(var c in new[]{Color.red,new Color(1,.5f,0),Color.yellow,Color.green,Color.cyan,new Color(.2f,.5f,1f),Color.magenta,Color.white,new Color(.5f,.5f,.5f),Color.black})
                CDot(c);
            GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            // Recent
            if (paint.recentColors.Count > 0) {
                GUILayout.Space(3); GUILayout.Label("Recent", secTitleSty);
                GUILayout.BeginHorizontal();
                for(int i=0;i<paint.recentColors.Count&&i<12;i++) CDot(paint.recentColors[i]);
                GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        // Grid
        if ((paint.showGrid||paint.enableSnapping) && ColHeader("GRID", ref colGrid)) {
            GUILayout.BeginVertical(secSty);
            paint.gridSpacing = Slider("Spacing", paint.gridSpacing, 2f, 45f);
            paint.gridThickness = Slider("Thickness", paint.gridThickness, 0.1f, 5f);
            paint.gridOpacity = Slider("Opacity", paint.gridOpacity, 0f, 1f);
            paint.useDiagonalSnapping = GUILayout.Toggle(paint.useDiagonalSnapping, " 45° Snap [F]", togSty);
            GUILayout.EndVertical();
        }

        // View
        if (ColHeader("VIEW", ref colView)) {
            GUILayout.BeginVertical(secSty);
            GUILayout.Label("Quick Views", secTitleSty);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Front",smBtnSty))paint.SetView(0,0,0);
            if(GUILayout.Button("Right",smBtnSty))paint.SetView(0,90,0);
            if(GUILayout.Button("Back",smBtnSty))paint.SetView(0,180,0);
            if(GUILayout.Button("Left",smBtnSty))paint.SetView(0,270,0);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Top",smBtnSty))paint.SetView(90,0,0);
            if(GUILayout.Button("Bottom",smBtnSty))paint.SetView(-90,0,0);
            if(GUILayout.Button("Reset",smBtnSty))paint.ResetView();
            GUILayout.EndHorizontal();
            GUILayout.Space(2); GUILayout.Label("Zoom Presets", secTitleSty);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("25%",smBtnSty))proj.perspective=25f;
            if(GUILayout.Button("50%",smBtnSty))proj.perspective=50f;
            if(GUILayout.Button("75%",smBtnSty))proj.perspective=75f;
            if(GUILayout.Button("100%",smBtnSty))proj.perspective=100f;
            GUILayout.EndHorizontal();
            showMinimap = GUILayout.Toggle(showMinimap, " Minimap [M]", togSty);
            GUILayout.EndVertical();
        }

        // Actions
        GUILayout.Space(4);
        H(6);
        if(GUILayout.Button("Clear Canvas",btnSty)) paint.ClearCanvas();
        if(GUILayout.Button("Export PNG",btnSty)) paint.ExportOverlayAsPNG();
        EndH(6);
        GUILayout.Space(8);
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ═══ Helpers ═══
    bool ColHeader(string name, ref bool collapsed) {
        if(GUILayout.Button((collapsed?"▸ ":"▾ ")+name, colHeaderSty, GUILayout.Height(24))) collapsed=!collapsed;
        return !collapsed;
    }
    void Sep() { GUILayout.Space(3); GUI.DrawTexture(GUILayoutUtility.GetRect(W-24,2), sepTex, ScaleMode.StretchToFill); GUILayout.Space(3); }
    void ThinSep() { GUILayout.Space(2); var r=GUILayoutUtility.GetRect(W-50,1); Color o=GUI.color; GUI.color=new Color(1,1,1,0.08f); GUI.DrawTexture(r,white); GUI.color=o; GUILayout.Space(2); }
    void InfoRow(string l, string v) { GUILayout.BeginHorizontal(); GUILayout.Label(l,secTitleSty,GUILayout.Width(35)); GUILayout.Label(v,valSty); GUILayout.EndHorizontal(); }
    float Slider(string l, float v, float mn, float mx) { GUILayout.BeginHorizontal(); GUILayout.Label(l,lblSty,GUILayout.Width(70)); GUILayout.Label(v.ToString("F1"),valSty,GUILayout.Width(36)); GUILayout.EndHorizontal(); float r=GUILayout.HorizontalSlider(v,mn,mx,sldSty,thumbSty); GUILayout.Space(2); return r; }
    void H(float s) { GUILayout.BeginHorizontal(); GUILayout.Space(s); }
    void EndH(float s=0) { if(s>0) GUILayout.Space(s); GUILayout.EndHorizontal(); }

    void TBtn(string label, bool active, System.Action act, Color? col=null)
    {
        Color c=col??C_ACC, o=GUI.backgroundColor;
        GUI.backgroundColor = active ? c : C_BTN;
        var s = new GUIStyle(btnSty); s.fontSize=10;
        if(active) { s.normal.textColor=new Color(0.03f,0.03f,0.05f,1f); s.normal.background=Solid(c); }
        if(GUILayout.Button(label,s,GUILayout.Height(26))) act();
        GUI.backgroundColor = o;
    }

    void CDot(Color c)
    {
        Color o=GUI.backgroundColor; GUI.backgroundColor=c;
        var s=new GUIStyle(GUI.skin.button); s.normal.background=white; s.hover.background=white;
        s.padding=new RectOffset(0,0,0,0); s.margin=new RectOffset(1,1,1,1);
        if(GUILayout.Button(GUIContent.none,s,GUILayout.Width(20),GUILayout.Height(20))) paint.drawColor=c;
        GUI.backgroundColor=o;
    }

    float CalcH()
    {
        float h = 100f; // title+tools
        h += colInfo ? 26 : 80;
        h += colCtrl ? 26 : (paint.isEraser ? 140 : 200);
        if (!paint.isEraser) h += colColor ? 26 : 220;
        if (paint.showGrid||paint.enableSnapping) h += colGrid ? 26 : 130;
        h += colView ? 26 : 160;
        h += 50; // actions
        return h;
    }

    // ═══ Status Bar ═══
    void DrawStatusBar()
    {
        string tool = paint.isEyedropper ? "◉ PICK" : (paint.isEraser ? "◌ ERASER" : "✎ BRUSH");
        string extra = (paint.enableSnapping?" │ ⊕ SNAP":"") + (paint.showGrid?" │ ▦ GRID":"");
        GUI.Box(new Rect(0, Screen.height-24, Screen.width, 24), $"  {tool}{extra} │ MMB: Orbit  Scroll: Zoom  [H] Help", barSty);
    }

    // ═══ Help ═══
    void DrawHelp()
    {
        GUI.Box(new Rect(0,0,Screen.width,Screen.height), GUIContent.none, helpBgSty);
        float w=600, h=620;
        GUILayout.BeginArea(new Rect((Screen.width-w)*0.5f,(Screen.height-h)*0.5f,w,h));
        GUILayout.Space(10); GUILayout.Label("⌨  CONTROLS", helpTitleSty); GUILayout.Space(10);
        HelpSec("NAVIGATION"); HK("Middle Mouse Drag","Orbit / Rotate"); HK("Shift + MMB","Roll");
        HK("Ctrl + MMB","Zoom"); HK("Scroll Wheel","Zoom"); HK("Space + LMB","Orbit (Alt)"); GUILayout.Space(6);
        HelpSec("VIEW PRESETS"); HK("Numpad 1/3/7","Front / Right / Top"); HK("Numpad 5 / Home","Reset"); GUILayout.Space(6);
        HelpSec("PAINTING"); HK("Q / B / P","Brush"); HK("E","Eraser"); HK("I","Eyedropper");
        HK("Alt + Drag","Line Tool"); HK("Ctrl+Z / Ctrl+Shift+Z","Undo / Redo"); GUILayout.Space(6);
        HelpSec("FILE"); HK("Ctrl + S / D","Save / Load Panorama"); GUILayout.Space(6);
        HelpSec("GRID"); HK("G / Shift+G","Grid / Align"); HK("S / F","Snap / 45° Snap"); GUILayout.Space(6);
        HelpSec("UI"); HK("W / H / M","Panel / Help / Minimap");
        GUILayout.FlexibleSpace();
        var cs = new GUIStyle(helpDescSty); cs.alignment=TextAnchor.MiddleCenter; cs.normal.textColor=C_MUT; cs.fontSize=11;
        GUILayout.Label("Press [H] or [Esc] to close", cs); GUILayout.Space(5);
        GUILayout.EndArea();
    }
    void HelpSec(string t) { var s=new GUIStyle(helpDescSty); s.fontStyle=FontStyle.Bold; s.normal.textColor=C_WARM; s.fontSize=11; GUILayout.Label("  "+t,s); GUILayout.Space(2); }
    void HK(string k, string d) { GUILayout.BeginHorizontal(); GUILayout.Label(k,helpKeySty); GUILayout.Space(15); GUILayout.Label(d,helpDescSty); GUILayout.EndHorizontal(); }

    // ═══ Minimap ═══
    void DrawMinimap()
    {
        float mmX = Screen.width - MM_W - PAD, mmY = Screen.height - MM_H - PAD - 28;
        Rect mm = new Rect(mmX, mmY, MM_W, MM_H);
        GUI.Box(mm, GUIContent.none, mmSty);
        Rect tr = new Rect(mm.x+3, mm.y+3, mm.width-6, mm.height-6);

        // Draw panorama + overlay composited via displayTexture if available
        Texture mmTex = proj.overlayTexture != null ? proj.overlayTexture : proj.panoramaTexture;
        // First draw the panorama base
        GUI.DrawTexture(tr, proj.panoramaTexture, ScaleMode.StretchToFill);
        // Then overlay the paint layer on top
        if (proj.overlayTexture != null)
        {
            Color oc = GUI.color;
            GUI.color = new Color(1,1,1,0.8f);
            GUI.DrawTexture(tr, proj.overlayTexture, ScaleMode.StretchToFill);
            GUI.color = oc;
        }

        // Viewport indicator with correct screen aspect ratio
        Vector3 fwd = cam.transform.forward;
        float lon = Mathf.Atan2(fwd.x, fwd.z);
        float lat = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f));
        float uvX = Mathf.Repeat(lon / (2f*Mathf.PI) + 0.5f, 1f);
        float uvY = 0.5f - lat / Mathf.PI;

        // Use screen aspect ratio for the viewport rectangle
        float screenAspect = (float)Screen.width / Screen.height;
        float vFov = proj.calculatedVerticalFOV / 180f;
        float hFov = proj.calculatedHorizontalFOV / 360f;
        
        float vpW = Mathf.Clamp(tr.width * hFov, 8, tr.width);
        float vpH = Mathf.Clamp(tr.height * vFov, 6, tr.height);

        // Ensure aspect ratio matches the screen
        float currentAR = vpW / vpH;
        float targetAR = (tr.width * hFov) / (tr.height * vFov);
        if (targetAR > 0.01f && currentAR / targetAR < 0.9f)
            vpW = vpH * targetAR;

        float vpX = tr.x + uvX * tr.width - vpW * 0.5f;
        float vpY = tr.y + uvY * tr.height - vpH * 0.5f;

        Color old = GUI.color; GUI.color = C_ACC;
        Rect vp = new Rect(vpX, vpY, vpW, vpH);

        // Draw rotated viewport indicator if camera is rolled
        float roll = cam.transform.eulerAngles.z;
        if (Mathf.Abs(roll) > 1f && Mathf.Abs(roll - 360f) > 1f)
        {
            // Draw 4 corner dots to indicate rotation
            float r = (roll > 180 ? roll - 360 : roll) * Mathf.Deg2Rad;
            float cx = vpX + vpW * 0.5f, cy = vpY + vpH * 0.5f;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            float hw = vpW * 0.5f, hh = vpH * 0.5f;
            // Draw rotated rectangle corners
            Vector2[] corners = {
                new Vector2(-hw,-hh), new Vector2(hw,-hh),
                new Vector2(hw,hh), new Vector2(-hw,hh)
            };
            for (int i = 0; i < 4; i++)
            {
                Vector2 c0 = corners[i], c1 = corners[(i+1)%4];
                Vector2 p0 = new Vector2(cx + c0.x*cos - c0.y*sin, cy + c0.x*sin + c0.y*cos);
                Vector2 p1 = new Vector2(cx + c1.x*cos - c1.y*sin, cy + c1.x*sin + c1.y*cos);
                // Approximate line with thin rects
                DrawLine(p0, p1, 2f);
            }
        }
        else
        {
            // Simple axis-aligned rectangle
            GUI.DrawTexture(new Rect(vp.x,vp.y,vp.width,2), white);
            GUI.DrawTexture(new Rect(vp.x,vp.yMax-2,vp.width,2), white);
            GUI.DrawTexture(new Rect(vp.x,vp.y,2,vp.height), white);
            GUI.DrawTexture(new Rect(vp.xMax-2,vp.y,2,vp.height), white);
        }

        // Center dot
        GUI.DrawTexture(new Rect(tr.x+uvX*tr.width-2, tr.y+uvY*tr.height-2, 4, 4), white);
        GUI.color = old;
        GUI.Label(new Rect(mm.x+4, mm.y+2, 60, 14), "MINIMAP", secTitleSty);
    }

    void DrawLine(Vector2 a, Vector2 b, float w)
    {
        float dx=b.x-a.x, dy=b.y-a.y;
        float len = Mathf.Sqrt(dx*dx+dy*dy);
        if (len < 1) return;
        // Use a series of small quads to approximate the line
        int steps = Mathf.CeilToInt(len / 2f);
        float dotW = w;
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float px = a.x + dx * t - dotW * 0.5f;
            float py = a.y + dy * t - dotW * 0.5f;
            GUI.DrawTexture(new Rect(px, py, dotW, dotW), white);
        }
    }
}