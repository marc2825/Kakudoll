Shader "VLat/Experiments/ClipVertexProbe"
{
    Properties { _Side ("Probe side", Float) = 128 _Mode ("0 clip XYZ / 1 clip W", Float) = 0 }
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
            float _Side, _Mode;
            struct appdata { float4 vertex : POSITION; uint id : SV_VertexID; };
            struct v2f { float4 pos : SV_POSITION; float4 clip : TEXCOORD0; float id : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                o.clip = UnityObjectToClipPos(v.vertex);
                float2 pixel = float2(v.id % (uint)_Side, v.id / (uint)_Side);
                o.pos = float4((pixel + 0.5) / _Side * 2 - 1, 0.5, 1);
                o.id = v.id + 1;
                return o;
            }
            float4 frag(v2f i) : SV_Target { return float4(_Mode < 0.5 ? i.clip.xyz : i.clip.www, i.id); }
            ENDCG
        }
    }
    Fallback Off
}
