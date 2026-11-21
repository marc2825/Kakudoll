Shader "VLat/Experiments/ViewContextDiagnostic"
{
    Properties
    {
        _Mode ("0 View color / 1 angles / 2 warnings / 3 context-source-status / 4 weight / 5 eval world / 6 draw world / 7 raw modes / 8 vertex world / 9 VS-PS weight / 10 mode color / 11 native eye color", Float) = 0
        _FrameRow0 ("Rest frame X", Vector) = (1,0,0,0)
        _FrameRow1 ("Rest frame Y", Vector) = (0,1,0,0)
        _FrameRow2 ("Rest frame Z", Vector) = (0,0,1,0)
        _RequireStereoCenter ("Reject missing stereo arrays (strict probe)", Float) = 0
        _OrthoDirection ("Orthographic: 0 position / 1 direction", Float) = 0
        _AsymmetricProbe ("S2 positive-yaw-only displacement in meters", Float) = 0
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
            #include "ViewContextDiagnostic.cginc"
            ENDCG
        }
    }
    Fallback Off
}
