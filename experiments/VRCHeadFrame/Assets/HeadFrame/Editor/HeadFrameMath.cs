using System;
using UnityEngine;

namespace VLat.HeadFrameExperiment
{
    internal static class HeadFrameMath
    {
        internal static void SetCalibration(Material material, Matrix4x4 matrix)
        {
            material.SetVector("_FrameRow0", matrix.GetRow(0));
            material.SetVector("_FrameRow1", matrix.GetRow(1));
            material.SetVector("_FrameRow2", matrix.GetRow(2));
        }

        internal static Vector2 Angles(Vector3 p)
        {
            if (p.sqrMagnitude < 1e-12f) throw new ArgumentException("頭部原点で角度は定義できません。");
            p.Normalize();
            float yaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            if (yaw <= -180f) yaw += 360f;
            return new Vector2(yaw, Mathf.Asin(Mathf.Clamp(p.y, -1f, 1f)) * Mathf.Rad2Deg);
        }

        internal static float MatrixError(Matrix4x4 a, Matrix4x4 b)
        {
            float error = 0;
            for (int i = 0; i < 16; ++i) error = Mathf.Max(error, Mathf.Abs(a[i] - b[i]));
            return error;
        }

        // General source meshes are intentionally NOT accepted by this experimental converter.
        // The fixture uses an orientation-preserving rigid rebase at rest.
        internal static Mesh RebaseClone(Mesh source, Matrix4x4 t)
        {
            Vector3 x = t.GetColumn(0), y = t.GetColumn(1), z = t.GetColumn(2);
            if (t.determinant <= 0 || Mathf.Abs(x.magnitude - 1) > 1e-4f ||
                Mathf.Abs(y.magnitude - 1) > 1e-4f || Mathf.Abs(z.magnitude - 1) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(x, y)) > 1e-4f || Mathf.Abs(Vector3.Dot(x, z)) > 1e-4f ||
                Mathf.Abs(Vector3.Dot(y, z)) > 1e-4f)
                throw new ArgumentException("この実験のrebaseは正の剛体変換だけを受理します。");

            Mesh clone = UnityEngine.Object.Instantiate(source);
            clone.name = source.name + "_RebasedClone";
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector4[] tangents = source.tangents;
            for (int i = 0; i < vertices.Length; ++i) vertices[i] = t.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Length; ++i) normals[i] = t.MultiplyVector(normals[i]);
            for (int i = 0; i < tangents.Length; ++i)
            {
                Vector3 v = t.MultiplyVector(tangents[i]);
                tangents[i] = new Vector4(v.x, v.y, v.z, tangents[i].w);
            }
            clone.vertices = vertices;
            clone.normals = normals;
            clone.tangents = tangents;
            Matrix4x4[] bindposes = source.bindposes;
            Matrix4x4 inverse = t.inverse;
            for (int i = 0; i < bindposes.Length; ++i) bindposes[i] = bindposes[i] * inverse;
            clone.bindposes = bindposes;

            clone.ClearBlendShapes();
            Vector3[] dp = new Vector3[vertices.Length], dn = new Vector3[vertices.Length], dt = new Vector3[vertices.Length];
            for (int shape = 0; shape < source.blendShapeCount; ++shape)
            {
                for (int frame = 0; frame < source.GetBlendShapeFrameCount(shape); ++frame)
                {
                    source.GetBlendShapeFrameVertices(shape, frame, dp, dn, dt);
                    for (int i = 0; i < vertices.Length; ++i)
                    {
                        dp[i] = t.MultiplyVector(dp[i]);
                        dn[i] = t.MultiplyVector(dn[i]);
                        dt[i] = t.MultiplyVector(dt[i]);
                    }
                    clone.AddBlendShapeFrame(source.GetBlendShapeName(shape), source.GetBlendShapeFrameWeight(shape, frame), dp, dn, dt);
                }
            }
            clone.RecalculateBounds();
            return clone;
        }
    }
}
