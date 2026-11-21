Shader "VLat/Experiments/ClientDiagnostic"
{
    Properties
    {
        _Font ("Diagnostic font", 2D) = "white" {}
        _FrameRow0 ("Fixed frame X", Vector) = (1,0,0,0)
        _FrameRow1 ("Fixed frame Y", Vector) = (0,1,0,0)
        _FrameRow2 ("Fixed frame Z", Vector) = (0,0,1,0)
        _Effect ("View probe enabled", Float) = 0
        _AsymmetricProbe ("Positive yaw +X probe meters", Float) = 0.02
        _RequireStereoCenter ("Strict stereo center", Float) = 0
        _Card ("Telemetry card", Float) = 0
        _CardId ("0 child / 1 constraint / 2 no-array", Float) = 0
    }
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
            #include "ClientDiagnostic.cginc"
            ENDCG
        }
    }
    Fallback Off
}
