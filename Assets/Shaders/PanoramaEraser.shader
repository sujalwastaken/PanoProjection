Shader "Hidden/PanoramaEraser"
{
    Properties
    {
        // Color is unused for erasing, but kept for compatibility with script calls
        _Color ("Draw Color", Color) = (1,1,1,1) 
        _BrushCenter ("Brush Center (UV)", Vector) = (0,0,0,0)
        _BrushRadius ("Brush Radius (Radians)", Float) = 0.1
        _Hardness ("Hardness", Range(0,1)) = 0.8
    }
    SubShader
    {
        // RenderOnTop to ensure it blends with existing buffers
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }
        
        // Multiplicative Blend: DestColor * SourceColor
        // We will output white (1,1,1) with varying alpha to scale down the destination.
        Blend DstColor Zero
        
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

            float4 _BrushCenter; // xy = UV center
            float _BrushRadius;  // in Radians
            float _Hardness;

            #define PI 3.14159265359

            // Convert UV to 3D Direction Vector
            float3 UVToDir(float2 uv) {
                float lon = (uv.x - 0.5) * 2.0 * PI;
                float lat = (0.5 - uv.y) * PI;
                float cosLat = cos(lat);
                return float3(cosLat * sin(lon), sin(lat), cosLat * cos(lon));
            }

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                // 1. Get directions for current pixel & brush center
                float3 pixelDir = UVToDir(i.uv);
                float3 brushDir = UVToDir(_BrushCenter.xy);

                // 2. Calculate Angular Distance
                float dotVal = dot(normalize(pixelDir), normalize(brushDir));
                float angle = acos(clamp(dotVal, -1.0, 1.0));

                // 3. Check Brush Radius (optimization, though keepFactor handles it too)
                if (angle > _BrushRadius) return float4(1,1,1,1); // Keep destination unchanged

                // 4. Calculate "Erase Amount" based on hardness
                // 1.0 at center, 0.0 at edge
                float t = angle / _BrushRadius;
                float eraseAmount = 1.0 - smoothstep(_Hardness, 1.0, t);
                
                // 5. Calculate "Keep Factor" for multiplicative blend
                // 0.0 at center (fully erase), 1.0 at edge (keep fully)
                float keepFactor = 1.0 - eraseAmount;

                // Output white with keepFactor in alpha.
                // Blend DstColor Zero result: Dest.rgba * float4(1,1,1, keepFactor)
                return float4(1, 1, 1, keepFactor);
            }
            ENDCG
        }
    }
}