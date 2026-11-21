using System;

namespace VLat.Core.Evaluation
{
    /// <summary>UnityEngineに依存しない3次元ベクトルを表します。</summary>
    public readonly struct Float3 : IEquatable<Float3>
    {
        /// <summary>3次元ベクトルを生成します。</summary>
        /// <param name="x">X成分。</param>
        /// <param name="y">Y成分。</param>
        /// <param name="z">Z成分。</param>
        public Float3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>X成分を取得します。</summary>
        public double X { get; }

        /// <summary>Y成分を取得します。</summary>
        public double Y { get; }

        /// <summary>Z成分を取得します。</summary>
        public double Z { get; }

        /// <summary>ベクトルの長さの2乗を取得します。</summary>
        public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

        /// <summary>別の3次元ベクトルと全成分が等しいかを判定します。</summary>
        /// <param name="other">比較する3次元ベクトル。</param>
        /// <returns>全成分が等しいときは<c>true</c>。</returns>
        public bool Equals(Float3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        /// <summary>指定されたオブジェクトとこの3次元ベクトルが等しいかを判定します。</summary>
        /// <param name="obj">比較するオブジェクト。</param>
        /// <returns>等値な<c>Float3</c>のときは<c>true</c>。</returns>
        public override bool Equals(object obj)
        {
            return obj is Float3 other && Equals(other);
        }

        /// <summary>全3成分から生成したハッシュコードを返します。</summary>
        /// <returns>この3次元ベクトルのハッシュコード。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }
    }

    /// <summary>頭部ローカル座標で算出したyawとpitchを表します。</summary>
    public readonly struct ViewAngles : IEquatable<ViewAngles>
    {
        /// <summary>視点角度を生成します。</summary>
        /// <param name="yaw">yaw角度。</param>
        /// <param name="pitch">pitch角度。</param>
        public ViewAngles(double yaw, double pitch)
        {
            Yaw = yaw;
            Pitch = pitch;
        }

        /// <summary>yaw角度を度数で取得します。</summary>
        public double Yaw { get; }

        /// <summary>pitch角度を度数で取得します。</summary>
        public double Pitch { get; }

        /// <summary>別の視点角度とyaw・pitchが等しいかを判定します。</summary>
        /// <param name="other">比較する視点角度。</param>
        /// <returns>yaw・pitchが等しいときは<c>true</c>。</returns>
        public bool Equals(ViewAngles other)
        {
            return Yaw.Equals(other.Yaw) && Pitch.Equals(other.Pitch);
        }

        /// <summary>指定されたオブジェクトとこの視点角度が等しいかを判定します。</summary>
        /// <param name="obj">比較するオブジェクト。</param>
        /// <returns>等値な<c>ViewAngles</c>のときは<c>true</c>。</returns>
        public override bool Equals(object obj)
        {
            return obj is ViewAngles other && Equals(other);
        }

        /// <summary>yaw・pitchから生成したハッシュコードを返します。</summary>
        /// <returns>この視点角度のハッシュコード。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Yaw.GetHashCode() * 397) ^ Pitch.GetHashCode();
            }
        }
    }
}
