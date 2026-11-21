Shader "VLat/Experiments/SurfaceProbe"
{
    Properties
    {
        _Mode ("0 world / 1 primitive / 2 depth / 3 normal / 4 solid / 5 primitive color", Float) = 0
        _Slot ("Submesh slot", Float) = 0
        _Select ("Only this local primitive (-1 all)", Float) = -1
        _WorldClip ("Use world then VP (diagnostic alternative)", Float) = 0
        _Cull ("Cull mode", Float) = 0
        _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }
        Pass
        {
            Cull [_Cull] ZTest [_ZTest] ZWrite On
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Mode, _Slot, _Select, _WorldClip;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = _WorldClip > 0.5 ? mul(UNITY_MATRIX_VP, float4(o.world, 1)) : UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(v2f i, uint primitive : SV_PrimitiveID) : SV_Target
            {
                if (_Select >= 0 && primitive != (uint)_Select) discard;
                if (_Mode < 0.5) return float4(i.world, 1);
                if (_Mode < 1.5) return float4(primitive + 1, _Slot, i.pos.z, 1);
                if (_Mode < 2.5) return float4(i.pos.z, -mul(UNITY_MATRIX_V, float4(i.world, 1)).z, 0, 1);
                if (_Mode < 3.5) return float4(normalize(i.normal), 1);
                if (_Mode < 4.5) return float4(0.25, 0.5, 0.75, 1);
                return float4(frac((primitive + 1 + _Slot * 65536) * float3(0.1031, 0.11369, 0.13787)), 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
