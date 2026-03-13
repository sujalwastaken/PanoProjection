Shader "Hidden/PanoramaProjection"
{
    Properties
    {
        _PanoramaTex ("Panorama", 2D) = "white" {}
        _OverlayTex ("Drawing Overlay", 2D) = "black" {} 
        _Perspective ("Perspective", Range(1, 100)) = 50
        _FisheyePerspective ("Fisheye Perspective", Range(0, 100)) = 0
        _MinFov ("Min FOV (deg)", Range(1, 179)) = 6.5
        _MaxFov ("Max FOV (deg)", Range(1, 179)) = 160
        _AspectRatio ("Aspect Ratio", Float) = 1.0
        
        // Cursor Props
        _CursorUV ("Cursor Position (UV)", Vector) = (-1,-1,0,0)
        _CursorRadius ("Cursor Radius (Normalized UV)", Float) = 0.05
        _CursorColor ("Cursor Color", Color) = (1,1,1,0.5)
        
        // Vignette
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0.3
        
        // Crosshair
        _ShowCrosshair ("Show Crosshair", Float) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _PanoramaTex;
            sampler2D _OverlayTex;
            float _Perspective;
            float _FisheyePerspective;
            float4x4 _CameraRotation; 
            float _MinFov;
            float _MaxFov;
            float _AspectRatio;

            float2 _CursorUV;
            float _CursorRadius;
            fixed4 _CursorColor;
            
            float _VignetteIntensity;
            float _ShowCrosshair;

            v2f vert(appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Helper to get 3D vector from UV
            float3 UVToDirection(float2 uv) {
                float lon = (uv.x - 0.5) * 2.0 * UNITY_PI;
                float lat = (0.5 - uv.y) * UNITY_PI;
                float cosLat = cos(lat);
                return float3(cosLat * sin(lon), sin(lat), cosLat * cos(lon));
            }

            fixed4 frag(v2f i) : SV_Target {
                const float INV_2PI = 1.0 / (2.0 * UNITY_PI);
                const float INV_PI = 1.0 / UNITY_PI;

                // --- 1. RAY PROJECTION (Screen -> 3D) ---
                float2 coord = (i.uv - 0.5) * 2.0;
                coord.x *= _AspectRatio;

                float r = length(coord);
                float3 rayDir = float3(0.0, 0.0, 1.0);

                if (r > 1e-4) {
                    float fisheyeAmount = _FisheyePerspective / 100.0;
                    float t = (_Perspective - 1.0) / 99.0;
                    float minScale = tan(_MinFov * 0.5 * UNITY_PI / 180.0);
                    float maxScale = tan(_MaxFov * 0.5 * UNITY_PI / 180.0);
                    float perspScale = lerp(minScale, maxScale, t);
                    float scaledR = r * perspScale;
                    
                    float theta = lerp(atan(scaledR), 2.0 * atan(scaledR), fisheyeAmount * fisheyeAmount * (3.0 - 2.0 * fisheyeAmount));
                    float sinTheta = sin(theta);
                    float cosTheta = cos(theta);
                    float2 dir2D = coord / r;

                    rayDir = float3(dir2D.x * sinTheta, -dir2D.y * sinTheta, cosTheta);
                }

                float3 worldRay = mul((float3x3)_CameraRotation, normalize(rayDir));
                
                // --- 2. TEXTURE SAMPLING ---
                float lon = atan2(worldRay.x, worldRay.z);
                float lat = asin(clamp(worldRay.y, -1.0, 1.0));

                float2 panoUV;
                panoUV.x = frac(lon * INV_2PI + 0.5);
                panoUV.y = saturate(0.5 - lat * INV_PI);

                fixed4 panoCol = tex2D(_PanoramaTex, panoUV);
                fixed4 drawCol = tex2D(_OverlayTex, panoUV);
                fixed4 finalCol = lerp(panoCol, drawCol, drawCol.a);

                // --- 3. ANIMATED CURSOR (Pulsing, Anti-aliased, Inner Glow) ---
                float3 cursorDir = UVToDirection(_CursorUV);
                float dotVal = dot(normalize(worldRay), normalize(cursorDir));
                float angle = acos(clamp(dotVal, -1.0, 1.0));
                float targetRadiusRad = _CursorRadius * UNITY_PI;
                
                // Pulse animation: subtle breathing effect
                float pulse = 0.85 + 0.15 * sin(_Time.y * 3.0);
                
                // Anti-aliased ring with smooth edges
                float ringOuter = targetRadiusRad;
                float ringInner = targetRadiusRad * 0.88;
                float edgeSmooth = targetRadiusRad * 0.04; // AA edge width
                
                // Outer edge AA
                float outerMask = 1.0 - smoothstep(ringOuter - edgeSmooth, ringOuter + edgeSmooth, angle);
                // Inner edge AA
                float innerMask = smoothstep(ringInner - edgeSmooth, ringInner + edgeSmooth, angle);
                // Ring = intersection
                float ringMask = outerMask * innerMask * pulse;
                
                // Inner glow: subtle radial gradient inside the circle
                float glowRadius = targetRadiusRad * 0.85;
                float glowMask = (1.0 - smoothstep(glowRadius * 0.5, glowRadius, angle)) * 0.08 * pulse;
                
                // Combine cursor effects
                float cursorAlpha = saturate(ringMask * _CursorColor.a + glowMask);
                float3 cursorRGB = _CursorColor.rgb;
                finalCol.rgb = lerp(finalCol.rgb, cursorRGB, cursorAlpha);

                // --- 4. VIGNETTE ---
                float2 vigUV = i.uv - 0.5;
                float vigDist = length(vigUV) * 1.4142; // normalize so corners = 1
                float vignette = 1.0 - _VignetteIntensity * smoothstep(0.4, 1.2, vigDist);
                finalCol.rgb *= vignette;
                
                // --- 5. CROSSHAIR AT SCREEN CENTER ---
                if (_ShowCrosshair > 0.5)
                {
                    float2 screenCenter = i.uv - 0.5;
                    float crossLen = 0.012;
                    float crossThick = 0.001;
                    float crossGap = 0.003;
                    
                    float hLine = step(abs(screenCenter.y), crossThick) 
                                * step(crossGap, abs(screenCenter.x)) 
                                * step(abs(screenCenter.x), crossLen);
                    float vLine = step(abs(screenCenter.x), crossThick) 
                                * step(crossGap, abs(screenCenter.y)) 
                                * step(abs(screenCenter.y), crossLen);
                    
                    float crossMask = saturate(hLine + vLine) * 0.5;
                    finalCol.rgb = lerp(finalCol.rgb, float3(1, 1, 1), crossMask);
                }

                return finalCol;
            }
            ENDCG
        }
    }
}