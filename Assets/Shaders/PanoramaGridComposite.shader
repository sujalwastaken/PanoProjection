Shader "Hidden/PanoramaGridComposite"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _ColorX ("Color X", Color) = (1,0,0,1)
        _ColorY ("Color Y", Color) = (0,1,0,1)
        _ColorZ ("Color Z", Color) = (0,0,1,1)
        _GhostColor ("Ghost Color", Color) = (1,1,0,1)
        _Subdivisions ("Subdivisions", Float) = 8.0
        _Thickness ("Thickness", Float) = 1.0
        _Opacity ("Opacity", Float) = 0.5
        _Falloff ("Grid Falloff", Float) = 0.15 // NEW: Fades lines at the horizon
        _UseGrid ("Use Grid", Float) = 0.0
        _ShowDiagonals ("Show Diagonals", Float) = 0.0
        _GridAxisMode ("Grid Axis Mode", Float) = 0.0
        _GhostNormal ("Ghost Normal", Vector) = (0,0,0,0)
        _TargetVP ("Target VP", Vector) = (0,0,0,0) // NEW: For the Snapping Ring
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            sampler2D _MainTex;
            float4 _ColorX, _ColorY, _ColorZ, _GhostColor;
            float _Subdivisions, _Thickness, _Opacity, _UseGrid, _ShowDiagonals, _GridAxisMode, _Falloff;
            float4 _GhostNormal, _TargetVP, _Rot0, _Rot1, _Rot2;

            #define PI 3.14159265359

            v2f vert (appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float3 UVToDir(float2 uv) {
                float lon = (uv.x - 0.5) * 2.0 * PI; float lat = (0.5 - uv.y) * PI; float cosLat = cos(lat);
                return float3(cosLat * sin(lon), sin(lat), cosLat * cos(lon));
            }

            void DrawVP(float3 rayDir, float3 vpDir, fixed4 color, float radius, inout fixed4 layer) {
                float angle = acos(clamp(abs(dot(rayDir, normalize(vpDir))), 0.0, 1.0));
                float dotAlpha = smoothstep(radius, radius * 0.8, angle);
                layer = max(layer, dotAlpha * color);
            }

            void DrawPlane(float u, float v, float n, fixed4 colU, fixed4 colV, float planeMask, float baseLineW, inout fixed4 layer) {
                if (planeMask < 0.5 || abs(n) < 0.0001) return;
                
                float2 uv = float2(u, v) / abs(n) * _Subdivisions;
                float2 dist = abs(frac(uv + 0.5) - 0.5);
                float2 fw = fwidth(uv);
                
                // --- GRID FALLOFF ---
                float depth = abs(n);
                float fadeOpacity = smoothstep(_Falloff * 0.1, _Falloff + 0.001, depth); 
                float lineW = baseLineW * lerp(0.2, 1.0, smoothstep(0.0, 0.3, depth)); 
                
                float2 lines = smoothstep(fw * (lineW + 1.0), fw * max(0.01, lineW - 0.5), dist);

                float2 idx = floor(uv + 0.5);
                float2 isEven = step(fmod(abs(idx), 2.0), 0.5);
                fixed4 grey = fixed4(0.5, 0.5, 0.5, 1.0);
                fixed4 cU = lerp(grey, colU, isEven.x);
                fixed4 cV = lerp(grey, colV, isEven.y);

                float2 isZero = step(abs(idx), 0.1);
                cU = lerp(cU, colU * 1.5, isZero.x);
                cV = lerp(cV, colV * 1.5, isZero.y);
                float2 linesM = smoothstep(fw * (lineW*2.0 + 1.0), fw * max(0.01, lineW*2.0 - 0.5), abs(uv));
                lines = max(lines, linesM);

                // --- MODE 5: HORIZON ONLY ---
                if (_GridAxisMode > 4.5 && _GridAxisMode < 5.5) {
                    layer = max(layer, linesM.x * colU * 1.5 * fadeOpacity);
                    layer = max(layer, linesM.y * colV * 1.5 * fadeOpacity);
                    return; // Skip normal grid and diagonals
                }

                layer = max(layer, lines.x * cU * fadeOpacity);
                layer = max(layer, lines.y * cV * fadeOpacity);

                if (_ShowDiagonals > 0.5) {
                    float d1 = abs(frac(uv.x - uv.y + 0.5) - 0.5);
                    float d2 = abs(frac(uv.x + uv.y + 0.5) - 0.5);
                    float fwD1 = fwidth(uv.x - uv.y);
                    float fwD2 = fwidth(uv.x + uv.y);
                    float lD1 = smoothstep(fwD1*(lineW+1.0), fwD1*max(0.01, lineW-0.5), d1);
                    float lD2 = smoothstep(fwD2*(lineW+1.0), fwD2*max(0.01, lineW-0.5), d2);
                    layer = max(layer, max(lD1, lD2) * fixed4(1,1,1,1) * 0.35 * fadeOpacity);
                }
            }

            fixed4 frag (v2f i) : SV_Target {
                float vpSize = 0.001; 
                fixed4 col = tex2D(_MainTex, i.uv);
                float3 dir = UVToDir(i.uv);
                
                float3x3 rot = float3x3(_Rot0.xyz, _Rot1.xyz, _Rot2.xyz);
                float3 localDir = mul(rot, dir);

                fixed4 gridLayer = fixed4(0,0,0,0);

                if (_UseGrid > 0.5)
                {
                    int mode = round(_GridAxisMode);
                    float lineW = _Thickness * 0.5;

                    // --- MODE 6: SPHERICAL GLOBE ---
                    if (mode == 6) {
                        float lon = (atan2(localDir.x, localDir.z) / PI) * _Subdivisions * 2.0; 
                        float lat = (asin(localDir.y) / (PI * 0.5)) * _Subdivisions;
                        
                        float2 uvSph = float2(lon, lat);
                        float2 distSph = abs(frac(uvSph + 0.5) - 0.5);
                        float2 fwSph = fwidth(uvSph);
                        
                        float2 linesSph = smoothstep(fwSph * (lineW + 1.0), fwSph * max(0.01, lineW - 0.5), distSph);
                        
                        // Emphasize Equator and Prime Meridians
                        float2 idx = floor(uvSph + 0.5);
                        float2 isZero = step(abs(idx), 0.1);
                        
                        fixed4 cLon = lerp(_ColorZ, _ColorZ * 1.5, isZero.x);
                        fixed4 cLat = lerp(_ColorY, _ColorY * 1.5, isZero.y);
                        
                        float2 linesM = smoothstep(fwSph * (lineW*2.0 + 1.0), fwSph * max(0.01, lineW*2.0 - 0.5), abs(uvSph));
                        linesSph = max(linesSph, linesM);
                        
                        gridLayer = max(gridLayer, linesSph.x * cLon);
                        gridLayer = max(gridLayer, linesSph.y * cLat);
                    } 
                    else 
                    {
                        // --- CARTESIAN MODES (0 to 5) ---
                        float3 absDir = abs(localDir);
                        float maxDir = max(absDir.x, max(absDir.y, absDir.z));
                        
                        float3 mask = float3(1,1,1);
                        if (mode == 0) {
                            mask.x = step(maxDir - 0.0001, absDir.x);
                            mask.y = step(maxDir - 0.0001, absDir.y);
                            mask.z = step(maxDir - 0.0001, absDir.z);
                        } 
                        else if (mode == 1) { mask = float3(1,0,0); }
                        else if (mode == 2) { mask = float3(0,1,0); }
                        else if (mode == 3) { mask = float3(0,0,1); }
                        // Modes 4 & 5 use mask(1,1,1)

                        DrawPlane(localDir.z, localDir.y, localDir.x, _ColorY, _ColorZ, mask.x, lineW, gridLayer);
                        DrawPlane(localDir.x, localDir.z, localDir.y, _ColorZ, _ColorX, mask.y, lineW, gridLayer);
                        DrawPlane(localDir.x, localDir.y, localDir.z, _ColorY, _ColorX, mask.z, lineW, gridLayer);
                    }

                    // --- VANISHING POINT DOTS ---
                    DrawVP(localDir, float3(1,0,0), _ColorX, vpSize, gridLayer);
                    DrawVP(localDir, float3(0,1,0), _ColorY, vpSize, gridLayer);
                    DrawVP(localDir, float3(0,0,1), _ColorZ, vpSize, gridLayer);

                    if (_ShowDiagonals > 0.5 && mode != 6) {
                        fixed4 colXY = fixed4(1,1,0,1); 
                        fixed4 colXZ = fixed4(1,0,1,1); 
                        fixed4 colYZ = fixed4(0,1,1,1); 
                        
                        DrawVP(localDir, float3(1,1,0), colXY, vpSize, gridLayer); DrawVP(localDir, float3(-1,1,0), colXY, vpSize, gridLayer);
                        DrawVP(localDir, float3(1,0,1), colXZ, vpSize, gridLayer); DrawVP(localDir, float3(-1,0,1), colXZ, vpSize, gridLayer);
                        DrawVP(localDir, float3(0,1,1), colYZ, vpSize, gridLayer); DrawVP(localDir, float3(0,-1,1), colYZ, vpSize, gridLayer);
                    }
                }

                // --- GHOST SNAPPING PLANE (Thickness scales with Grid) ---
                if (length(_GhostNormal.xyz) > 0.1)
                {
                    float ghostDist = abs(dot(localDir, _GhostNormal.xyz));
                    float fwGhost = fwidth(ghostDist);
                    float ghostLineW = _Thickness * 0.2 + 0.4; // THICKNESS CONTROL
                    float ghostLine = smoothstep(fwGhost * (ghostLineW * 2.0 + 1.0), fwGhost * max(0.01, ghostLineW * 2.0 - 0.5), ghostDist);
                    gridLayer = max(gridLayer, ghostLine * _GhostColor); 
                } 

                if (length(_TargetVP.xyz) > 0.1)
                {
                    float vpDot = abs(dot(localDir, _TargetVP.xyz));
                    
                    // --- THE FIX: Ring radius mathematically linked to vpSize ---
                    // Multiply vpSize by 8.0 to make the ring act as a comfortable reticle around the dot
                    float ringAngle = vpSize * 4.0; 
                    float ringRadius = cos(ringAngle); 
                    
                    float ringDist = abs(vpDot - ringRadius);
                    float fwRing = fwidth(vpDot); 
                    float ghostLineW = _Thickness * 0.2 + 0.4;
                    float ringLine = smoothstep(fwRing * (ghostLineW * 2.0 + 1.0), fwRing * max(0.01, ghostLineW * 2.0 - 0.5), ringDist);
                    gridLayer = max(gridLayer, ringLine * _GhostColor);
                }

                gridLayer.a = saturate(gridLayer.a);
                col.rgb = lerp(col.rgb, gridLayer.rgb, gridLayer.a * _Opacity);
                return col;
            }
            ENDCG
        }
    }
}