#ifndef VLAT_EXPERIMENT_VIEW_CONTEXT_LIVE
#define VLAT_EXPERIMENT_VIEW_CONTEXT_LIVE
// Documented VRChat globals ONLY; deliberately absent from Material Properties.
float _VRChatMirrorMode, _VRChatCameraMode, _VRChatFaceMirrorMode;
float3 _VRChatMirrorCameraPos, _VRChatScreenCameraPos, _VRChatPhotoCameraPos;
VLatViewInput VLatLiveInput()
{
    VLatViewInput i = (VLatViewInput)0;
    i.drawPosition = _WorldSpaceCameraPos;
    i.observerDirection = UNITY_MATRIX_V[2].xyz; // Head -> observer = -camera forward (Unity view Z is flipped).
    #if defined(USING_STEREO_MATRICES)
        i.stereoAvailable = 1;
        i.leftPosition = unity_StereoWorldSpaceCameraPos[0];
        i.rightPosition = unity_StereoWorldSpaceCameraPos[1];
        i.observerDirection = unity_StereoMatrixV[0][2].xyz + unity_StereoMatrixV[1][2].xyz;
    #endif
    i.orthographic = unity_OrthoParams.w;
    i.mirrorMode = _VRChatMirrorMode; i.cameraMode = _VRChatCameraMode; i.faceMode = _VRChatFaceMirrorMode;
    i.mirrorPosition = _VRChatMirrorCameraPos; i.screenPosition = _VRChatScreenCameraPos; i.photoPosition = _VRChatPhotoCameraPos;
    return i;
}
#endif
