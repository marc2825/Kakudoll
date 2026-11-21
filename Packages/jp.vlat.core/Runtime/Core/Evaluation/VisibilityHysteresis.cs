using VLat.Core.Model;

namespace VLat.Core.Evaluation
{
    /// <summary>visibility Modifierごとのヒステリシス状態を保持します。</summary>
    public sealed class VisibilityHysteresis
    {
        /// <summary>現在バンド側の状態が選択されているかを取得します。</summary>
        public bool IsActive { get; private set; }

        /// <summary>現在角度と生の帯重みからvisibility状態を更新します。</summary>
        /// <param name="band">対象のAngleBand。</param>
        /// <param name="yaw">現在のyaw角度。</param>
        /// <param name="pitch">現在のpitch角度。</param>
        /// <param name="rawWeight">ヒステリシス適用前の帯重み。</param>
        /// <returns>バンド側のvisibility状態が選択されているときは<c>true</c>。</returns>
        public bool Evaluate(AngleBand band, double yaw, double pitch, double rawWeight)
        {
            if (!IsActive)
            {
                IsActive = rawWeight >= 0.5d;
                return IsActive;
            }

            if (!AngleEvaluator.IsWithinExpandedHalfWeightRange(band, yaw, pitch))
            {
                IsActive = false;
            }

            return IsActive;
        }

        /// <summary>保持状態を指定値へ戻します。</summary>
        /// <param name="active">リセット後の状態。</param>
        public void Reset(bool active = false)
        {
            IsActive = active;
        }
    }
}
