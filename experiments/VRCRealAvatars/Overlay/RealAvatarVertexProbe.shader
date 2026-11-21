Shader "VLat/Experiments/RealAvatarVertexProbe"
{
    Properties { _Side ("Probe texture side", Float) = 128 }
    SubShader
    {
        Tags { "DisableBatching"="True" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Side;
            struct appdata { float4 vertex : POSITION; uint id : SV_VertexID; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float id : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                float2 pixel = float2(v.id % (uint)_Side, v.id / (uint)_Side);
                o.pos = float4((pixel + 0.5) / _Side * 2 - 1, 0.5, 1);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.id = v.id + 1;
                return o;
            }
            float4 frag(v2f i) : SV_Target { return float4(i.world, i.id); }
            ENDCG
        }
    }
    Fallback Off
}
