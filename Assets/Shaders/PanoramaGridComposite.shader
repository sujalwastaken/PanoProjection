Shader "Hidden/PanoramaGridComposite"
{
    Properties
    {
        _MainTex ("Paint Texture", 2D) = "white" {}
        _ColorX ("Color X", Color) = (1, 0, 0, 0.5)
        _ColorY ("Color Y", Color) = (0, 1, 0, 0.5)
        _ColorZ ("Color Z", Color) = (0, 0, 1, 0.5)
        
        _Spacing ("Spacing Deg", Float) = 10.0
        _Thickness ("Pixel Thickness", Float) = 1.0
        _Opacity ("Grid Opacity", Float) = 1.0
        
        _Rot0 ("Rot0", Vector) = (1,0,0,0)
        _Rot1 ("Rot1", Vector) = (0,1,0,0)
        _Rot2 ("Rot2", Vector) = (0,0,1,0)
        _UseGrid ("Use Grid", Float) = 0.0
        _ShowDiagonals ("Show Diagonals", Float) = 0.0
        _ActiveAxis ("Active Axis Index", Float) = -1.0
        _GhostNormal ("Ghost Line Normal", Vector) = (0,0,0,0) 
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float2 uv : TEXCOORD0; float4 vertex : SV_POSITION; };

            sampler2D _MainTex;
            float4 _ColorX, _ColorY, _ColorZ;
            float _Spacing, _Thickness, _UseGrid, _Opacity, _ShowDiagonals, _ActiveAxis;
            float4 _Rot0, _Rot1, _Rot2, _GhostNormal;

            #define PI 3.14159265359

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            // Standard Grid Line Calculation
            float GetLineIntensity(float angle, float thickness) {
                float sp = _Spacing * (PI / 180.0);
                float val = fmod(abs(angle), sp);
                float dist = min(val, sp - val);
                float angleDerivative = fwidth(angle) + 1e-5;
                float distInPixels = dist / angleDerivative;
                return 1.0 - smoothstep(thickness * 0.5, thickness * 0.5 + 1.0, distInPixels);
            }

            // ROBUST Ghost Line Calculation
            float DrawGreatCircle(float3 viewDir, float3 normal, float thickness) {
                // Dot product 0 = on the line
                float d = dot(viewDir, normal);
                
                // Convert to screen space pixel distance BEFORE taking absolute value
                float fw = fwidth(d) + 1e-6;
                float distPixels = abs(d) / fw;
                
                // Use standard smoothstep for clean anti-aliasing
                return 1.0 - smoothstep(thickness * 0.5, thickness * 0.5 + 1.2, distPixels);
            }

            float DrawDot(float3 viewDir, float3 targetDir, float relativeSize) {
                float dotVal = abs(dot(viewDir, targetDir));
                float diff = 1.0 - dotVal;
                float threshold = relativeSize * 0.000001; 
                float edgeWidth = fwidth(diff);
                return smoothstep(threshold + edgeWidth, threshold, diff);
            }

            float DrawRing(float3 viewDir, float3 targetDir, float relativeSize) {
                float dotVal = abs(dot(viewDir, targetDir));
                float diff = 1.0 - dotVal;
                float threshold = relativeSize * 0.00001;
                float ringRadius = threshold * 1.8; 
                float ringThick = threshold * 0.8;
                float edge = fwidth(diff);
                float d = abs(diff - ringRadius);
                return 1.0 - smoothstep(ringThick - edge, ringThick, d);
            }
            
            fixed4 frag (v2f i) : SV_Target {
                fixed4 paint = tex2D(_MainTex, i.uv);
                
                // Early exit if everything is off
                if (_ShowDiagonals < 0.5 && _UseGrid < 0.5 && _ActiveAxis < -0.5 && _GhostNormal.w < 0.1) return paint;
                
                float lon = (i.uv.x - 0.5) * 2.0 * PI;
                float lat = (0.5 - i.uv.y) * PI;
                float3 dir = float3(cos(lat)*sin(lon), sin(lat), cos(lat)*cos(lon));
                
                float3x3 rot = float3x3(_Rot0.xyz, _Rot1.xyz, _Rot2.xyz);
                dir = mul(transpose(rot), dir); 
                
                // --- 1. CALCULATE LAYERS ---

                // LAYER A: MARKERS (Dots)
                float4 markerColor = float4(0,0,0,0);
                
                float dX = DrawDot(dir, float3(1,0,0), 1.0);
                float dY = DrawDot(dir, float3(0,1,0), 1.0);
                float dZ = DrawDot(dir, float3(0,0,1), 1.0);
                
                markerColor = max(markerColor, float4(1,0,0,1) * dX);
                markerColor = max(markerColor, float4(0,1,0,1) * dY);
                markerColor = max(markerColor, float4(0,0,1,1) * dZ);

                // Diagonals
                float dXY = max(DrawDot(dir, normalize(float3(1,1,0)), 1.0), DrawDot(dir, normalize(float3(-1,1,0)), 1.0));
                markerColor = max(markerColor, float4(1,1,0,1) * dXY); // Yellow

                float dXZ = max(DrawDot(dir, normalize(float3(1,0,1)), 1.0), DrawDot(dir, normalize(float3(-1,0,1)), 1.0));
                markerColor = max(markerColor, float4(1,0,1,1) * dXZ); // Magenta

                float dYZ = max(DrawDot(dir, normalize(float3(0,1,1)), 1.0), DrawDot(dir, normalize(float3(0,-1,1)), 1.0));
                markerColor = max(markerColor, float4(0,1,1,1) * dYZ); // Cyan

                // LAYER B: HIGHLIGHTS (Ring & Ghost Line)
                float4 ringColor = float4(0,0,0,0);
                float4 ghostColor = float4(0,0,0,0);
                float4 currentAxisColor = float4(1,1,1,1); 

                float ringAlpha = 0;
                
                // Determine Active Color & Ring Alpha
                if(abs(_ActiveAxis - 0.0) < 0.1) { ringAlpha = DrawRing(dir, float3(1,0,0), 1.0); currentAxisColor = float4(1,0,0,1); }
                else if(abs(_ActiveAxis - 1.0) < 0.1) { ringAlpha = DrawRing(dir, float3(0,1,0), 1.0); currentAxisColor = float4(0,1,0,1); }
                else if(abs(_ActiveAxis - 2.0) < 0.1) { ringAlpha = DrawRing(dir, float3(0,0,1), 1.0); currentAxisColor = float4(0,0,1,1); }
                else if(_ActiveAxis > 9.0) {
                    if(abs(_ActiveAxis - 10.0) < 0.1 || abs(_ActiveAxis - 11.0) < 0.1) {
                        currentAxisColor = float4(1,1,0,1);
                        ringAlpha = (abs(_ActiveAxis - 10.0) < 0.1) ? DrawRing(dir, normalize(float3(1,1,0)), 1.0) : DrawRing(dir, normalize(float3(-1,1,0)), 1.0);
                    }
                    else if(abs(_ActiveAxis - 12.0) < 0.1 || abs(_ActiveAxis - 13.0) < 0.1) {
                        currentAxisColor = float4(1,0,1,1);
                        ringAlpha = (abs(_ActiveAxis - 12.0) < 0.1) ? DrawRing(dir, normalize(float3(1,0,1)), 1.0) : DrawRing(dir, normalize(float3(-1,0,1)), 1.0);
                    }
                    else {
                        currentAxisColor = float4(0,1,1,1);
                        ringAlpha = (abs(_ActiveAxis - 14.0) < 0.1) ? DrawRing(dir, normalize(float3(0,1,1)), 1.0) : DrawRing(dir, normalize(float3(0,-1,1)), 1.0);
                    }
                }
                
                // Ring is ALWAYS Black
                ringColor = float4(0,0,0,1) * ringAlpha;

                // Ghost Line
                if (_GhostNormal.w > 0.5) {
                    float ghostWidth = _Thickness * 1.5; // 50% bigger than grid lines
                    float ghostIntensity = DrawGreatCircle(dir, _GhostNormal.xyz, ghostWidth);
                    // Color at FULL brightness, but with lower alpha for transparency
                    ghostColor = currentAxisColor;
                    ghostColor.rgb *= ghostIntensity; // Full brightness
                    ghostColor.a = ghostIntensity * 0.5; // Lower opacity
                }

                // LAYER C: GRID LINES
                float4 gridColor = float4(0,0,0,0);
                if (_UseGrid > 0.5) {
                    float angleY = atan2(dir.x, dir.z);
                    float angleX = atan2(dir.y, dir.z);
                    float angleZ = atan2(dir.y, dir.x);
                    
                    float strengthY = GetLineIntensity(angleY, _Thickness);
                    float strengthX = GetLineIntensity(angleX, _Thickness);
                    float strengthZ = GetLineIntensity(angleZ, _Thickness);
                    
                    float axisThickness = _Thickness * 3.0;
                    float axisX = DrawGreatCircle(dir, float3(1,0,0), axisThickness);
                    float axisY = DrawGreatCircle(dir, float3(0,1,0), axisThickness);
                    float axisZ = DrawGreatCircle(dir, float3(0,0,1), axisThickness);
                    
                    // Sum Grid Components
                    float4 gBase = float4(0,0,0,0);
                    gBase = max(gBase, _ColorX * strengthX);
                    gBase = max(gBase, _ColorY * strengthY);
                    gBase = max(gBase, _ColorZ * strengthZ);
                    
                    float4 aBase = float4(0,0,0,0);
                    aBase = max(aBase, float4(1, 0, 0, 1) * axisX);
                    aBase = max(aBase, float4(0, 1, 0, 1) * axisY);
                    aBase = max(aBase, float4(0, 0, 1, 1) * axisZ);
                    
                    gridColor = lerp(gBase, aBase, aBase.a);
                    gridColor.a *= _Opacity;
                }

                // --- 2. COMPOSITE STACK (Bottom to Top) ---
                // Order: Grid -> GhostLine -> Markers -> Ring
                    
                float4 finalComposite = gridColor;

                // Blend Ghost Line ON TOP of Grid (standard alpha blend)
                finalComposite.rgb = ghostColor.rgb + finalComposite.rgb * (1.0 - ghostColor.a);
                finalComposite.a = ghostColor.a + finalComposite.a * (1.0 - ghostColor.a);

                // Blend Markers ON TOP
                finalComposite.rgb = markerColor.rgb * markerColor.a + finalComposite.rgb * (1.0 - markerColor.a);
                finalComposite.a = markerColor.a + finalComposite.a * (1.0 - markerColor.a);

                // Blend Ring ON TOP (Black Ring cuts through everything)
                finalComposite.rgb = ringColor.rgb * ringColor.a + finalComposite.rgb * (1.0 - ringColor.a);
                finalComposite.a = ringColor.a + finalComposite.a * (1.0 - ringColor.a);

                // --- 3. FINAL OUTPUT ---
                // Blend composite over the original paint texture
                // Preserving Paint's Alpha Channel
                float3 resultColor = lerp(paint.rgb, finalComposite.rgb, finalComposite.a);
                
                return fixed4(resultColor, paint.a);
            }
            ENDCG
        }
    }
}