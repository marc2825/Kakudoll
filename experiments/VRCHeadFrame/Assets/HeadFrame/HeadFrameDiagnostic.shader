Shader "VLat/Experiments/HeadFrameDiagnostic"
{
    Properties
    {
        _Mode ("0 Angles / 1 XYZ stripes / 2-5 Object columns / 6 Raw angles / 7 Silhouette / 8 World position", Float) = 0
        _FrameRow0 ("Rest Object-to-Frame row X", Vector) = (1,0,0,0)
        _FrameRow1 ("Rest Object-to-Frame row Y", Vector) = (0,1,0,0)
        _FrameRow2 ("Rest Object-to-Frame row Z", Vector) = (0,0,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float _Mode;
            // Rest-pose calibration ONLY. The harness never updates this for a pose/view.
            float4 _FrameRow0, _FrameRow1, _FrameRow2;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 angles(float3 p)
            {
                float lengthSquared = dot(p, p);
                if (lengthSquared < 1e-12) return float4(0, 0, 0, 1);
                float3 d = p * rsqrt(lengthSquared);
                float yaw = atan2(d.x, d.z) * 57.2957795131;
                if (yaw <= -180) yaw += 360;
                // R/G encode signed degrees; B=valid; alpha=coverage marker.
                return float4(yaw / 360 + 0.5, asin(clamp(d.y, -1, 1)) / 180 * 57.2957795131 + 0.5, 1, 1);
            }
            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                if (_Mode >= 8) return float4(i.world, 1);
                if (_Mode >= 7) return float4(1, 1, 1, 1);
                float3 cameraObject = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1)).xyz;
                if (_Mode >= 6) return angles(cameraObject);
                if (_Mode >= 5) return mul(unity_ObjectToWorld, float4(0, 0, 0, 1));
                if (_Mode >= 4) return float4(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)), 1);
                if (_Mode >= 3) return float4(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)), 1);
                if (_Mode >= 2) return float4(mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0)), 1);
                if (_Mode >= 1)
                {
                    // Left/middle/right thirds encode Object X/Y/Z in WORLD RGB.
                    float x = i.pos.x / _ScreenParams.x;
                    float3 axis = x < 0.3333 ? float3(1,0,0) : (x < 0.6667 ? float3(0,1,0) : float3(0,0,1));
                    return float4(normalize(mul((float3x3)unity_ObjectToWorld, axis)) * 0.5 + 0.5, 1);
                }
                float4 p = float4(cameraObject, 1);
                return angles(float3(dot(_FrameRow0, p), dot(_FrameRow1, p), dot(_FrameRow2, p)));
            }
            ENDCG
        }
    }
    Fallback Off
}
