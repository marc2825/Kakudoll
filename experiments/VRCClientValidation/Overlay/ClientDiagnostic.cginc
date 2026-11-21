#include "UnityCG.cginc"
float4 _FrameRow0, _FrameRow1, _FrameRow2;
float _Effect, _AsymmetricProbe, _RequireStereoCenter, _Card, _CardId;
sampler2D _Font;
#include "ViewContextCore.cginc"
#include "ViewContextLive.cginc"
VLatViewInput ClientInput()
{
    #if defined(VLAT_CLIENT_NO_ARRAYS)
        // Controlled omission, not evidence that an actual VRChat multipass path is supported.
        VLatViewInput i = (VLatViewInput)0;
        i.drawPosition = _WorldSpaceCameraPos; i.observerDirection = UNITY_MATRIX_V[2].xyz;
        i.orthographic = unity_OrthoParams.w;
        i.mirrorMode = _VRChatMirrorMode; i.cameraMode = _VRChatCameraMode; i.faceMode = _VRChatFaceMirrorMode;
        i.mirrorPosition = _VRChatMirrorCameraPos; i.screenPosition = _VRChatScreenCameraPos; i.photoPosition = _VRChatPhotoCameraPos;
        return i;
    #else
        return VLatLiveInput();
    #endif
}
VLatViewResult ClientEvaluate(VLatViewInput input)
{
    // Client-only diagnostic guard: a chopped/singular frame must not look like a valid view.
    float3 x=unity_ObjectToWorld._m00_m10_m20, y=unity_ObjectToWorld._m01_m11_m21, z=unity_ObjectToWorld._m02_m12_m22;
    float lengths=length(x)*length(y)*length(z);
    if (!VLatFinite3(x) || !VLatFinite3(y) || !VLatFinite3(z) || lengths < 1e-20 ||
        abs(dot(cross(x,y),z)) < lengths*1e-6)
    {
        VLatViewResult invalid=(VLatViewResult)0;invalid.status=10;return invalid;
    }
    VLatViewResult r=VLatEvaluateView(input,_RequireStereoCenter,0);
    if (!VLatFinite3(float3(r.angles,r.weight))) { r.status=6;r.angles=0;r.weight=0; }
    return r;
}
struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
struct v2f
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR;
    nointerpolation float2 vertexProbe : TEXCOORD1;
    UNITY_VERTEX_OUTPUT_STEREO
};
v2f vert(appdata v)
{
    v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VLatViewResult r = ClientEvaluate(ClientInput());
    float3 world = mul(unity_ObjectToWorld,v.vertex).xyz;
    if (_Card < 0.5 && _Effect > 0.5 && r.status == 0 && VLatToFrame(world,1).x > 0.01)
        world += normalize(unity_ObjectToWorld._m00_m10_m20) * r.weight * _AsymmetricProbe;
    o.pos = mul(UNITY_MATRIX_VP,float4(world,1)); o.uv = v.uv; o.color = v.color;
    o.vertexProbe = float2(r.weight,r.status);
    return o;
}
uint DataWord(uint row, VLatViewInput i, VLatViewResult r, float2 vs)
{
    if (row==0) return 0x564c4331u; // VLC1
    if (row==1) return 1u;
    if (row==2) return (uint)_CardId;
    if (row==3) return (uint)i.stereoAvailable;
    if (row==4)
    {
        #if defined(USING_STEREO_MATRICES)
            return unity_StereoEyeIndex;
        #else
            return 2u;
        #endif
    }
    if (row==5) return asuint(i.mirrorMode);
    if (row==6) return asuint(i.cameraMode);
    if (row==7) return asuint(i.faceMode);
    if (row==8) return asuint(r.context);
    if (row==9) return asuint(r.source);
    if (row==10) return asuint(r.status);
    if (row==11) return asuint(r.warnings);
    if (row==12) return asuint(r.sharedStereo);
    if (row==13) return asuint(r.weight);
    if (row==14) return asuint(r.angles.x);
    if (row==15) return asuint(r.angles.y);
    if (row==16) return asuint(vs.x);
    if (row==17) return asuint(vs.y);
    if (row<=20) return asuint(r.position[row-18]);
    if (row<=23) return asuint(i.drawPosition[row-21]);
    if (row<=26) return asuint(i.mirrorPosition[row-24]);
    if (row<=29) return asuint(i.screenPosition[row-27]);
    if (row<=32) return asuint(i.photoPosition[row-30]);
    if (row<=35) return asuint(i.leftPosition[row-33]);
    if (row<=38) return asuint(i.rightPosition[row-36]);
    if (row==39) return asuint(i.orthographic);
    if (row==40) return asuint(_Effect);
    if (row==41) return asuint(_RequireStereoCenter);
    if (row<=44) return asuint(unity_ObjectToWorld[row-42][3]);
    if (row<=47) return asuint(unity_ObjectToWorld[row-45][0]);
    if (row<=50) return asuint(unity_ObjectToWorld[row-48][1]);
    if (row<=53) return asuint(unity_ObjectToWorld[row-51][2]);
    return asuint(_Time.y);
}
float Glyph(uint code, float2 p)
{
    return tex2Dlod(_Font,float4((float2(code%16,7-code/16)+saturate(float2(p.x,1-p.y)))/float2(16,8),0,0)).r;
}
uint Digit(float value, int place)
{
    return 48u + (uint)(floor(abs(value)/pow(10.0,(float)place)))%10u;
}
float4 frag(v2f v) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    VLatViewInput input = ClientInput();
    VLatViewResult r = ClientEvaluate(input);
    if (_Card < 0.5)
    {
        if (_Effect < 0.5) return v.color;
        return r.status > 0 ? float4(1,0,1,1) : float4(lerp(v.color.rgb,float3(r.weight,1-r.weight,saturate(r.angles.y/90+0.5)),0.65),1);
    }
    float2 cell = float2(v.uv.x*34,(1-v.uv.y)*64);
    if (cell.y < 7)
    {
        float2 text = float2(v.uv.x*17,cell.y/1.75); uint column=(uint)text.x, row=(uint)text.y;
        uint code=32u;
        float value=row==0?r.angles.x:(row==1?r.angles.y:(row==2?r.weight*100:r.status));
        if (column==0) code=row==0?89u:(row==1?80u:(row==2?87u:83u)); // Y P W S
        else if (column==2) code=value<0?45u:43u;
        else if (column>=3 && column<=5) code=Digit(value,5-(int)column);
        else if (column==6) code=46u;
        else if (column==7) code=Digit(value*10,0);
        else if (column==9) code=67u; // C
        else if (column==10) code=Digit(r.context,0);
        else if (column==12) code=69u; // E
        else if (column==13)
        {
            #if defined(USING_STEREO_MATRICES)
                code=48u+unity_StereoEyeIndex;
            #else
                code=45u;
            #endif
        }
        else if (column==15) code=48u+(uint)_CardId;
        float3 bg=r.status>0?float3(0.35,0,0.35):(_CardId<0.5?float3(0,0.15,0.2):float3(0.2,0.1,0));
        return float4(lerp(bg,float3(1,1,1),Glyph(code,frac(text))),1);
    }
    if (cell.x < 1) return float4(0,0.2,1,1);
    if (cell.x >= 33) return float4(1,1,0,1);
    if (cell.y < 8 || cell.y >= 64) return float4(1,0,1,1);
    uint row=(uint)floor(cell.y)-8, column=(uint)floor(cell.x)-1;
    uint data;
    [branch] if (row==55)
    {
        data=2166136261u;
        [loop] for(uint j=0;j<55;++j) data = (data ^ DataWord(j,input,r,v.vertexProbe))*16777619u;
    }
    else data=DataWord(row,input,r,v.vertexProbe);
    float bit=(data >> column)&1u;
    return float4(bit,bit,bit,1);
}
