Shader "Hidden/PanoramaBrush"
{
    Properties
    {
        _Color ("Draw Color", Color) = (1,0,0,1)
        _BrushCenter ("Brush Center (UV)", Vector) = (0,0,0,0)
        _BrushRadius ("Brush Radius (Radians)", Float) = 0.1
        _Hardness ("Hardness", Range(0,1)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        // Standard Alpha Blending
        Blend SrcAlpha OneMinusSrcAlpha 
        ZWrite Off ZTest Always

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

            float4 _Color;
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

                // 2. Calculate Angular Distance (Great Circle Distance)
                // dot(a,b) = cos(angle)
                float dotVal = dot(normalize(pixelDir), normalize(brushDir));
                float angle = acos(clamp(dotVal, -1.0, 1.0));

                // 3. Check Brush Radius
                if (angle > _BrushRadius) discard;

                // 4. Calculate Falloff (Hardness)
                // 0 (center) -> 1 (edge)
                float t = angle / _BrushRadius;
                
                // Hardness logic:
                // If hardness = 1.0, alpha stays 1.0 until edge.
                // If hardness = 0.0, alpha linear fade from center.
                float alpha = 1.0 - smoothstep(_Hardness, 1.0, t);

                return float4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
}