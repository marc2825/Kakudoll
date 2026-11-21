using System;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    internal sealed class ViewContextCase
    {
        internal string Name;
        internal bool Native, Negative;
        internal Vector3 Draw, Left, Right, Mirror, Screen, Photo, Direction, ExpectedPosition;
        internal float Stereo, MirrorMode, CameraMode, FaceMode, Ortho, Require, DirectionPolicy;
        internal int Context, Source, Status, Warnings, Shared, Directional;
        internal Vector2 Angles;
        internal float Weight;
        internal static readonly string[] Names = {
            "NativeMain", "NativeObserverB", "NativeHeadRelative", "NativeOrthoPosition", "NativeOrthoDirection",
            "InjectedStereo", "InjectedMirrorVR", "InjectedMirrorDesktop", "InjectedHandheldVR", "InjectedHandheldDesktop",
            "InjectedScreenshot", "InjectedScreenshotStereo", "InjectedFaceWithMirror", "InjectedFaceUndefined", "InjectedMirrorZero",
            "InjectedUnknownMirror", "InjectedConflicting", "NativeStrictStereoMissing", "InjectedEmptyStereo", "InjectedNonFinite",
            "InjectedDegenerate", "InjectedOrigin", "InjectedPhotoInactive", "InjectedScreenDecoy", "NegativePerEye", "NegativeMirrorSign" };

        internal static ViewContextCase Build(int index, HeadFrameFixture f, float yaw, float pitch, int eye)
        {
            f.SetCamera(yaw, pitch);
            Vector3 center = f.Camera.transform.position, right = f.Camera.transform.right;
            Vector3 offset = center - f.Canonical.position;
            ViewContextCase c = new ViewContextCase { Name = Names[index], Native = index < 5 || index == 17,
                Draw = center, Left = center - right * 0.032f, Right = center + right * 0.032f,
                Mirror = center - Vector3.forward * (2 * (offset.z - 0.3f)), Direction = offset.normalized,
                ExpectedPosition = center, Source = 1, Warnings = 1 };
            switch (index)
            {
                case 1:
                    c.Draw = f.Canonical.position + Quaternion.AngleAxis(50, Vector3.up) * offset;
                    c.ExpectedPosition = c.Draw; c.Direction = (c.Draw - f.Canonical.position).normalized; break;
                case 2:
                    c.Draw = f.Canonical.TransformPoint(offset); c.ExpectedPosition = c.Draw;
                    c.Direction = (c.Draw - f.Canonical.position).normalized; break;
                case 3: case 4:
                    c.Draw += right * 0.3f; c.ExpectedPosition = c.Draw; c.Ortho = 1;
                    if (index == 4) { c.DirectionPolicy = 1; c.Directional = 1; c.Source = 4; }
                    else c.Warnings += 8;
                    break;
                case 5: case 24:
                    c.Stereo = 1; c.Draw = eye == 0 ? c.Left : c.Right;
                    c.Context = 1; c.Source = 2; c.Warnings = 0; c.Shared = 1;
                    c.Negative = index == 24; break;
                case 6: case 7: case 12: case 25:
                    c.MirrorMode = index == 7 ? 2 : 1; c.Context = index == 7 ? 3 : 2;
                    c.Source = 3; c.Warnings = 0; c.Shared = 1; c.ExpectedPosition = c.Mirror;
                    c.Stereo = 1; c.Draw = eye == 0 ? c.Left : c.Right;
                    if (index == 12) { c.FaceMode = 1; c.Context = 7; c.Warnings = 16; }
                    c.Negative = index == 25; break;
                case 8: case 9:
                    c.CameraMode = index == 8 ? 1 : 2; c.Context = index == 8 ? 4 : 5;
                    c.Draw = f.Canonical.position + Quaternion.AngleAxis(-55, Vector3.up) * offset;
                    c.ExpectedPosition = c.Draw; c.Photo = c.Draw; c.Screen = center;
                    if (index == 8) { c.Stereo = 1; c.Left = c.Draw - right * 0.032f; c.Right = c.Draw + right * 0.032f;
                        c.Draw = eye == 0 ? c.Left : c.Right; c.Source = 2; c.Shared = 1; c.Warnings = 0; }
                    break;
                case 10: case 11:
                    c.CameraMode = 3; c.Context = 6; c.Photo = center + new Vector3(5,3,2);
                    if (index == 11) { c.Stereo = 1; c.Draw = eye == 0 ? c.Left : c.Right; c.Source = 2; c.Shared = 1; c.Warnings = 0; }
                    break;
                case 13:
                    c.FaceMode = 1; c.Context = 7; c.Source = 0; c.Warnings = 0; c.Status = 4; break;
                case 14:
                    c.MirrorMode = 1; c.Mirror = Vector3.zero; c.ExpectedPosition = Vector3.zero;
                    c.Context = 2; c.Source = 3; c.Shared = 1; c.Warnings = 0; c.Status = 3; break;
                case 15:
                    c.MirrorMode = 3; c.Source = 0; c.Warnings = 0; c.Status = 1; break;
                case 16:
                    c.MirrorMode = 1; c.CameraMode = 1; c.Source = 0; c.Warnings = 0; c.Status = 2; break;
                case 17:
                    c.Require = 1; c.Status = 5; break;
                case 18:
                    c.Stereo = 1; c.Left = c.Right = Vector3.zero; c.Context = 1;
                    c.Source = 0; c.Warnings = 0; c.Status = 9; break;
                case 19:
                    c.Draw = new Vector3(float.NaN,0,0); c.Status = 6; break;
                case 20:
                    c.Draw = c.ExpectedPosition = f.Canonical.position; c.Status = 7; break;
                case 21:
                    c.Draw = c.ExpectedPosition = Vector3.zero; break;
                case 22:
                    c.CameraMode = 1; c.Context = 4; c.Warnings = 3; break;
                case 23:
                    c.Screen = center + new Vector3(5,3,2); c.Warnings = 5; break;
            }
            if (c.Status == 0)
            {
                Vector3 local = c.Directional != 0 ? f.Canonical.InverseTransformVector(c.Direction) : f.Canonical.InverseTransformPoint(c.ExpectedPosition);
                double length = Math.Sqrt((double)local.x*local.x + (double)local.y*local.y + (double)local.z*local.z);
                double a = Math.Atan2(local.x, local.z) * 180 / Math.PI;
                if (a <= -180) a += 360;
                c.Angles = new Vector2((float)a, (float)(Math.Asin(Math.Max(-1,Math.Min(1,local.y/length))) * 180 / Math.PI));
                double t = Math.Max(0, Math.Min(1, a/60)); c.Weight = (float)(t*t*(3-2*t));
            }
            return c;
        }

        internal void Apply(Material material)
        {
            material.SetFloat("_RequireStereoCenter", Require); material.SetFloat("_OrthoDirection", DirectionPolicy);
            if (Native) return;
            material.SetFloat("_VLatTestStereo", Stereo); material.SetFloat("_VLatTestMirrorMode", MirrorMode);
            material.SetFloat("_VLatTestCameraMode", CameraMode); material.SetFloat("_VLatTestFaceMode", FaceMode); material.SetFloat("_VLatTestOrtho", Ortho);
            material.SetVector("_VLatTestDraw", Draw); material.SetVector("_VLatTestLeft", Left); material.SetVector("_VLatTestRight", Right);
            material.SetVector("_VLatTestMirror", Mirror); material.SetVector("_VLatTestScreen", Screen); material.SetVector("_VLatTestPhoto", Photo);
            material.SetVector("_VLatTestDirection", Direction);
            material.SetFloat("_VLatTestPerEye", Name == "NegativePerEye" ? 1 : 0);
            material.SetFloat("_VLatTestFlipYaw", Name == "NegativeMirrorSign" ? 1 : 0);
        }
    }
}
