#include "UnityCG.cginc"
float4 _FrameRow0, _FrameRow1, _FrameRow2;
float _Mode, _RequireStereoCenter, _OrthoDirection, _AsymmetricProbe;
#include "ViewContextCore.cginc"
#if defined(VLAT_INJECTED_TEST)
float4 _VLatTestDraw, _VLatTestLeft, _VLatTestRight, _VLatTestMirror, _VLatTestScreen, _VLatTestPhoto, _VLatTestDirection;
float _VLatTestStereo, _VLatTestMirrorMode, _VLatTestCameraMode, _VLatTestFaceMode, _VLatTestOrtho;
float _VLatTestPerEye, _VLatTestFlipYaw;
VLatViewInput VLatInput()
{
    VLatViewInput i = (VLatViewInput)0;
    i.drawPosition = _VLatTestDraw.xyz; i.leftPosition = _VLatTestLeft.xyz; i.rightPosition = _VLatTestRight.xyz;
    i.mirrorPosition = _VLatTestMirror.xyz; i.screenPosition = _VLatTestScreen.xyz; i.photoPosition = _VLatTestPhoto.xyz;
    i.observerDirection = _VLatTestDirection.xyz; i.stereoAvailable = _VLatTestPerEye > 0 ? 0 : _VLatTestStereo;
    i.mirrorMode = _VLatTestMirrorMode; i.cameraMode = _VLatTestCameraMode; i.faceMode = _VLatTestFaceMode;
    i.orthographic = _VLatTestOrtho;
    return i;
}
#else
#include "ViewContextLive.cginc"
VLatViewInput VLatInput() { return VLatLiveInput(); }
#endif
VLatViewResult VLatView()
{
    VLatViewResult r = VLatEvaluateView(VLatInput(), _RequireStereoCenter, _OrthoDirection);
    #if defined(VLAT_INJECTED_TEST)
        if (_VLatTestFlipYaw > 0) { r.angles.x = -r.angles.x; r.weight = VLatAsymmetricWeight(r.angles.x); }
    #endif
    return r;
}
struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; float2 vertexProbe : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
v2f vert(appdata v)
{
    v2f o;
    UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
    VLatViewResult r = VLatView();
    o.vertexProbe = float2(r.weight, r.status);
    // S2 sign/stereo probe ONLY: intentionally asymmetric, visible and disabled on invalid input.
    // No v0.1 delta semantics / skinning equivalence are claimed by this post-skin world displacement.
    float3 frameRight = normalize(mul((float3x3)unity_ObjectToWorld, float3(1,0,0)));
    float rightSide = VLatToFrame(world, 1).x > 0.01 ? 1 : 0;
    world += frameRight * (r.status == 0 ? rightSide * r.weight * _AsymmetricProbe : 0);
    o.pos = mul(UNITY_MATRIX_VP, float4(world,1)); o.world = world;
    return o;
}
float4 frag(v2f i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    VLatViewResult r = VLatView();
    if (_Mode >= 11)
    {
        #if defined(USING_STEREO_MATRICES)
            return unity_StereoEyeIndex == 0 ? float4(0,1,0,1) : float4(1,0,0,1);
        #else
            return float4(0.4,0.4,0.4,1); // Mono / stereo arrays unavailable.
        #endif
    }
    if (_Mode >= 10)
    {
        if (r.status > 0) return float4(1,0,1,1);
        if (r.context < 0.5) return float4(0.5,0.5,0.5,1);
        if (r.context < 1.5) return float4(0,0.7,0.3,1);
        if (r.context < 2.5) return float4(0.6,0,1,1);
        if (r.context < 3.5) return float4(0,0.7,1,1);
        if (r.context < 4.5) return float4(1,0.4,0,1);
        if (r.context < 5.5) return float4(1,1,0,1);
        if (r.context < 6.5) return float4(0,0.2,1,1);
        return float4(1,0.2,0.5,1);
    }
    if (_Mode >= 9) return float4(i.vertexProbe.x,r.weight,i.vertexProbe.y,1);
    if (_Mode >= 8) return float4(i.world,1);
    if (_Mode >= 7) { VLatViewInput raw = VLatInput(); return float4(raw.mirrorMode, raw.cameraMode, raw.faceMode, 1); }
    if (_Mode >= 6) return float4(_WorldSpaceCameraPos,1);
    if (_Mode >= 5) return float4(r.position,1);
    if (_Mode >= 4) return r.status > 0 ? float4(1,0,1,1) : float4(r.weight, r.sharedStereo, r.warnings > 0 ? 0.3 : 0, 1);
    if (_Mode >= 3) return float4(r.context,r.source,r.status,1);
    if (_Mode >= 2) return float4(r.warnings,r.sharedStereo,r.directional,1);
    if (_Mode >= 1) return float4(r.angles.x/360+0.5,r.angles.y/180+0.5,r.status == 0 ? 1 : 0,1);
    if (r.status > 0) return float4(1,0,1,1);
    return float4(r.weight, 1-r.weight, saturate(r.angles.y/90+0.5),1);
}
