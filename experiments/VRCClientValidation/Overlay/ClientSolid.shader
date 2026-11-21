Shader "VLat/Experiments/ClientSolid"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos=mul(UNITY_MATRIX_VP,mul(unity_ObjectToWorld,v.vertex));o.color=v.color;return o;
            }
            float4 frag(v2f v) : SV_Target { UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v); return v.color; }
            ENDCG
        }
    }
    Fallback Off
}
