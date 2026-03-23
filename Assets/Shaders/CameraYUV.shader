Shader "NomadGo/CameraYUV"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FlipY ("Flip Y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Background" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _FlipY;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // Flip Y if needed (Android camera is often upside down)
                if (_FlipY > 0.5)
                    o.uv.y = 1.0 - o.uv.y;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // WebCamTexture on Android returns RGBA correctly after Unity processes it
                // Just output as-is — Unity handles YUV internally
                return fixed4(col.r, col.g, col.b, 1.0);
            }
            ENDCG
        }
    }
}
