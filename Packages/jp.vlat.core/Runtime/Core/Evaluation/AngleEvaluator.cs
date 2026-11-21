using System;
using System.Collections.Generic;
using VLat.Core.Model;

namespace VLat.Core.Evaluation
{
    /// <summary>頭部ローカルのカメラ方向から視点角度とAngleBand重みを評価します。</summary>
    public sealed class AngleEvaluator
    {
        private const double RadiansToDegrees = 180d / Math.PI;
        private const double DirectionEpsilon = 1e-24d;
        private readonly AngleBand[] _bands;

        /// <summary>評価するAngleBandの一覧を指定して生成します。</summary>
        /// <param name="bands">評価するAngleBand。</param>
        public AngleEvaluator(IReadOnlyList<AngleBand> bands)
        {
            if (bands == null)
            {
                throw new ArgumentNullException(nameof(bands));
            }

            _bands = new AngleBand[bands.Count];
            for (var index = 0; index < bands.Count; index++)
            {
                _bands[index] = bands[index];
            }
        }

        /// <summary>評価対象のAngleBand数を取得します。</summary>
        public int BandCount => _bands.Length;

        /// <summary>カメラ方向から全AngleBandの重みを計算し、呼び出し側の配列へ書き込みます。</summary>
        /// <param name="cameraPositionHeadLocal">頭部原点から見たカメラ位置。</param>
        /// <param name="destination">帯重みの書き込み先。</param>
        /// <returns>方向が有効で評価できたときは<c>true</c>。</returns>
        public bool Evaluate(Float3 cameraPositionHeadLocal, double[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < _bands.Length)
            {
                throw new ArgumentException("AngleBand数以上の出力配列が必要です。", nameof(destination));
            }

            if (!TryCalculateAngles(cameraPositionHeadLocal, out var angles))
            {
                Array.Clear(destination, 0, _bands.Length);
                return false;
            }

            for (var index = 0; index < _bands.Length; index++)
            {
                destination[index] = EvaluateBand(_bands[index], angles.Yaw, angles.Pitch);
            }

            return true;
        }

        /// <summary>頭部ローカルのカメラ位置からyawとpitchを算出します。</summary>
        /// <param name="cameraPositionHeadLocal">頭部原点から見たカメラ位置。</param>
        /// <param name="angles">算出された視点角度。</param>
        /// <returns>方向ベクトルが有効なときは<c>true</c>。</returns>
        public static bool TryCalculateAngles(Float3 cameraPositionHeadLocal, out ViewAngles angles)
        {
            var lengthSquared = cameraPositionHeadLocal.LengthSquared;
            if (double.IsNaN(lengthSquared) || double.IsInfinity(lengthSquared) || lengthSquared <= DirectionEpsilon)
            {
                angles = default;
                return false;
            }

            var inverseLength = 1d / Math.Sqrt(lengthSquared);
            var x = cameraPositionHeadLocal.X * inverseLength;
            var y = Clamp(cameraPositionHeadLocal.Y * inverseLength, -1d, 1d);
            var z = cameraPositionHeadLocal.Z * inverseLength;
            var yaw = NormalizeYaw(Math.Atan2(x, z) * RadiansToDegrees);
            var pitch = Math.Asin(y) * RadiansToDegrees;
            angles = new ViewAngles(yaw, pitch);
            return true;
        }

        /// <summary>指定したyawとpitchに対するAngleBand重みを計算します。</summary>
        /// <param name="band">評価するAngleBand。</param>
        /// <param name="yaw">yaw角度。</param>
        /// <param name="pitch">pitch角度。</param>
        /// <returns>0から1の帯重み。</returns>
        public static double EvaluateBand(AngleBand band, double yaw, double pitch)
        {
            if (band == null || band.Yaw == null || band.Pitch == null || band.Feather == null)
            {
                return 0d;
            }

            if (double.IsNaN(yaw) || double.IsNaN(pitch) || double.IsInfinity(yaw) || double.IsInfinity(pitch))
            {
                return 0d;
            }

            var yawWeight = EvaluateYawAxis(band.Yaw, band.Feather.Yaw, yaw);
            if (yawWeight <= 0d)
            {
                return 0d;
            }

            return yawWeight * EvaluateLinearAxis(band.Pitch, band.Feather.Pitch, pitch);
        }

        /// <summary>smootherstep関数を0から1の範囲で評価します。</summary>
        /// <param name="value">入力値。</param>
        /// <returns>smootherstepの評価結果。</returns>
        public static double SmootherStep(double value)
        {
            var t = Clamp(value, 0d, 1d);
            return t * t * t * ((t * ((t * 6d) - 15d)) + 10d);
        }

        internal static bool IsWithinExpandedHalfWeightRange(AngleBand band, double yaw, double pitch)
        {
            if (band == null || band.Yaw == null || band.Pitch == null || band.Feather == null)
            {
                return false;
            }

            var expansion = Math.Max(0d, band.Hysteresis);
            var yawDistance = DistanceOutsideYawRange(band.Yaw, yaw);
            var pitchDistance = DistanceOutsideLinearRange(band.Pitch, pitch);
            return yawDistance <= (Math.Max(0d, band.Feather.Yaw) * 0.5d) + expansion
                && pitchDistance <= (Math.Max(0d, band.Feather.Pitch) * 0.5d) + expansion;
        }

        private static double EvaluateYawAxis(AngleRange range, double feather, double angle)
        {
            return EvaluateDistance(DistanceOutsideYawRange(range, angle), feather);
        }

        private static double EvaluateLinearAxis(AngleRange range, double feather, double angle)
        {
            return EvaluateDistance(DistanceOutsideLinearRange(range, angle), feather);
        }

        private static double EvaluateDistance(double distance, double feather)
        {
            if (distance <= 0d)
            {
                return 1d;
            }

            if (feather <= 0d || distance >= feather)
            {
                return 0d;
            }

            return SmootherStep(1d - (distance / feather));
        }

        private static double DistanceOutsideYawRange(AngleRange range, double angle)
        {
            if (range == null || double.IsNaN(angle))
            {
                return double.PositiveInfinity;
            }

            if (range.Max - range.Min >= 360d)
            {
                return 0d;
            }

            var normalizedAngle = NormalizeYaw(angle);
            var minimum = NormalizeYaw(range.Min);
            var maximum = NormalizeYaw(range.Max);
            var isInside = minimum <= maximum
                ? normalizedAngle >= minimum && normalizedAngle <= maximum
                : normalizedAngle >= minimum || normalizedAngle <= maximum;
            if (isInside)
            {
                return 0d;
            }

            return Math.Min(DeltaAngle(normalizedAngle, minimum), DeltaAngle(normalizedAngle, maximum));
        }

        private static double DistanceOutsideLinearRange(AngleRange range, double angle)
        {
            if (range == null || double.IsNaN(angle) || range.Min > range.Max)
            {
                return double.PositiveInfinity;
            }

            if (angle < range.Min)
            {
                return range.Min - angle;
            }

            return angle > range.Max ? angle - range.Max : 0d;
        }

        private static double DeltaAngle(double first, double second)
        {
            var difference = Math.Abs(first - second) % 360d;
            return difference > 180d ? 360d - difference : difference;
        }

        private static double NormalizeYaw(double angle)
        {
            var normalized = angle % 360d;
            if (normalized <= -180d)
            {
                normalized += 360d;
            }
            else if (normalized > 180d)
            {
                normalized -= 360d;
            }

            return normalized;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            return value > maximum ? maximum : value;
        }
    }
}
