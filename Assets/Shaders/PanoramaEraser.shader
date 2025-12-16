Shader "Hidden/PanoramaEraser"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        ZWrite Off
        Cull Off
        
        // --- THE MAGIC ---
        // Destination = Destination * (1 - SourceAlpha)
        // If we draw with Alpha 1, we multiply Dest by 0 -> Fully Erased.
        Blend Zero OneMinusSrcAlpha 

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                fixed4 col = tex2D(_MainTex, i.uv);
                // We return Alpha as the "Eraser Strength"
                // The Blend mode above uses this alpha to eat away the destination
                // The RGB output is ignored because of 'Blend Zero ...'
                return fixed4(0, 0, 0, col.a * i.color.a);
            }
            ENDCG
        }
    }
}