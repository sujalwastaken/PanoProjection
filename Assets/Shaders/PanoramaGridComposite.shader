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
            float _Spacing, _Thickness, _UseGrid, _Opacity;
            float4 _Rot0, _Rot1, _Rot2;

            #define PI 3.14159265359

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            float GetLineIntensity(float angle, float thickness) {
                float sp = _Spacing * (PI / 180.0);
                float val = fmod(abs(angle), sp);
                float dist = min(val, sp - val);
                float angleDerivative = fwidth(angle) + 1e-5;
                float distInPixels = dist / angleDerivative;
                float targetPixelWidth = thickness;
                return 1.0 - smoothstep(targetPixelWidth / 2.0, targetPixelWidth / 2.0 + 1.0, distInPixels);
            }
            
            // Get intensity for the main axis line (at angle = 0)
            float GetAxisIntensity(float angle, float thickness) {
                float angleDerivative = fwidth(angle) + 1e-5;
                float distInPixels = abs(angle) / angleDerivative;
                float targetPixelWidth = thickness;
                return 1.0 - smoothstep(targetPixelWidth / 2.0, targetPixelWidth / 2.0 + 1.0, distInPixels);
            }
            
            fixed4 frag (v2f i) : SV_Target {
                fixed4 paint = tex2D(_MainTex, i.uv);
                
                if (_UseGrid < 0.5) return paint;
                
                float lon = (i.uv.x - 0.5) * 2.0 * PI;
                float lat = (0.5 - i.uv.y) * PI;
                float3 dir = float3(cos(lat)*sin(lon), sin(lat), cos(lat)*cos(lon));
                
                float3x3 rot = float3x3(_Rot0.xyz, _Rot1.xyz, _Rot2.xyz);
                dir = mul(transpose(rot), dir); 
                
                // Calculate angles for each axis
                float angleY = atan2(dir.x, dir.z);
                float angleX = atan2(dir.y, dir.z);
                float angleZ = atan2(dir.y, dir.x);
                
                // Regular grid lines (thinner)
                float strengthY = GetLineIntensity(angleY, _Thickness);
                float strengthX = GetLineIntensity(angleX, _Thickness);
                float strengthZ = GetLineIntensity(angleZ, _Thickness);
                
                // Main axis lines (bold, 3x thickness)
                float axisThickness = _Thickness * 3.0;
                float axisY = GetAxisIntensity(angleY, axisThickness);
                float axisX = GetAxisIntensity(angleX, axisThickness);
                float axisZ = GetAxisIntensity(angleZ, axisThickness);
                
                // Start with grid color
                float4 gridColor = float4(0,0,0,0);
                gridColor = max(gridColor, _ColorX * strengthX);
                gridColor = max(gridColor, _ColorY * strengthY);
                gridColor = max(gridColor, _ColorZ * strengthZ);
                
                // Overlay the bold axis lines with solid colors
                float4 axisColor = float4(0,0,0,0);
                axisColor = max(axisColor, float4(1, 0, 0, 1) * axisX);  // Red X-axis
                axisColor = max(axisColor, float4(0, 1, 0, 1) * axisY);  // Green Y-axis
                axisColor = max(axisColor, float4(0, 0, 1, 1) * axisZ);  // Blue Z-axis
                
                // Blend axis over grid
                gridColor = lerp(gridColor, axisColor, axisColor.a);
                
                // Apply Global Opacity
                float finalAlpha = gridColor.a * _Opacity;
                return lerp(paint, gridColor, finalAlpha);
            }
            ENDCG
        }
    }
}