#ifndef VLAT_EXPERIMENT_VIEW_CONTEXT_CORE
#define VLAT_EXPERIMENT_VIEW_CONTEXT_CORE
// Pure selection/evaluation shared by the live adapter and explicitly injected tests.
struct VLatViewInput
{
    float3 drawPosition, leftPosition, rightPosition, mirrorPosition, screenPosition, photoPosition;
    float3 observerDirection;
    float stereoAvailable, mirrorMode, cameraMode, faceMode, orthographic;
};
struct VLatViewResult
{
    float3 position;
    float2 angles;
    float context, source, status, warnings, sharedStereo, weight, directional;
};
// context: 0 mono/unproven, 1 stereo, 2 VR mirror, 3 desktop mirror, 4/5 handheld, 6 screenshot, 7 face mirror.
// source: 0 none, 1 current draw, 2 stereo center, 3 mirror center, 4 orthographic direction.
// status: 0 valid, 1 unknown modes, 2 conflicting modes, 3 ambiguous mirror zero, 4 face undefined,
//         5 required stereo unavailable, 6 non-finite, 7 degenerate direction, 8 ortho mirror direction, 9 empty stereo.
// warnings bit values: 1 mono/multipass unproven, 2 photo inactive, 4 auxiliary position differs,
//                      8 orthographic position policy, 16 face/mirror overlap unverified.
bool VLatFinite3(float3 p) { return all(p == p) && all(abs(p) < 1e20); }
bool VLatMode(float p, float maximum) { return p == p && p >= 0 && p <= maximum && p == floor(p); }
float VLatAsymmetricWeight(float yaw)
{
    float t = saturate(yaw / 60.0);
    return t * t * (3.0 - 2.0 * t);
}
float3 VLatToFrame(float3 value, float w)
{
    float4 p = mul(unity_WorldToObject, float4(value, w));
    return float3(dot(_FrameRow0, p), dot(_FrameRow1, p), dot(_FrameRow2, p));
}
VLatViewResult VLatEvaluateView(VLatViewInput i, float requireStereo, float orthoDirection)
{
    VLatViewResult r = (VLatViewResult)0;
    if (!VLatMode(i.mirrorMode, 2) || !VLatMode(i.cameraMode, 3) || !VLatMode(i.faceMode, 1))
        { r.status = 1; return r; }
    if (i.mirrorMode > 0 && i.cameraMode > 0) { r.status = 2; return r; }
    if (i.mirrorMode > 0)
    {
        r.context = i.faceMode > 0 ? 7 : i.mirrorMode + 1;
        r.source = 3; r.position = i.mirrorPosition; r.sharedStereo = 1;
        if (!VLatFinite3(r.position)) { r.status = 6; return r; }
        if (dot(r.position, r.position) == 0) { r.status = 3; return r; }
        if (i.faceMode > 0) r.warnings = 16;
    }
    else
    {
        if (i.faceMode > 0) { r.context = 7; r.status = 4; return r; }
        r.context = i.cameraMode > 0 ? i.cameraMode + 3 : i.stereoAvailable;
        if (i.stereoAvailable > 0)
        {
            if (!VLatFinite3(i.leftPosition) || !VLatFinite3(i.rightPosition)) { r.status = 6; return r; }
            if (dot(i.leftPosition, i.leftPosition) + dot(i.rightPosition, i.rightPosition) == 0)
                { r.status = 9; return r; }
            r.position = (i.leftPosition + i.rightPosition) * 0.5;
            r.source = 2; r.sharedStereo = 1;
        }
        else
        {
            r.source = 1; r.position = i.drawPosition; r.warnings = 1;
            if (requireStereo > 0) { r.status = 5; return r; }
        }
        if (i.cameraMode == 1 || i.cameraMode == 2)
        {
            if (dot(i.photoPosition, i.photoPosition) == 0) r.warnings += 2;
            else if (VLatFinite3(i.photoPosition) && distance(i.photoPosition, r.position) > 0.01) r.warnings += 4;
        }
        else if (i.cameraMode == 0 && VLatFinite3(i.screenPosition) && dot(i.screenPosition, i.screenPosition) > 0 &&
            distance(i.screenPosition, r.position) > 0.01) r.warnings += 4;
    }
    if (!VLatFinite3(r.position)) { r.status = 6; return r; }
    float3 direction = float3(0,0,0);
    if (i.orthographic > 0 && orthoDirection > 0)
    {
        if (i.mirrorMode > 0) { r.status = 8; return r; }
        r.directional = 1; r.source = 4;
        if (!VLatFinite3(i.observerDirection)) { r.status = 6; return r; }
        direction = VLatToFrame(i.observerDirection, 0);
    }
    else
    {
        if (i.orthographic > 0) r.warnings += 8;
        direction = VLatToFrame(r.position, 1);
    }
    if (dot(direction, direction) < 1e-12) { r.status = 7; return r; }
    direction = normalize(direction);
    float yaw = atan2(direction.x, direction.z) * 57.2957795131;
    if (yaw <= -180) yaw += 360;
    r.angles = float2(yaw, asin(clamp(direction.y, -1, 1)) * 57.2957795131);
    r.weight = VLatAsymmetricWeight(yaw);
    return r;
}
#endif
