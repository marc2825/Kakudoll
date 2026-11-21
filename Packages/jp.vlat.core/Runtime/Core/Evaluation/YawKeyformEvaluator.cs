using System;
using VLat.Core.Model;
using VLat.Core.Validation;

namespace VLat.Core.Evaluation
{
    /// <summary>Yawキーを局所凸補間するCPU参照評価器です。生成後の評価では割り当てを行いません。</summary>
    public sealed class YawKeyformEvaluator
    {
        private readonly KeyformGroup _group;

        /// <summary>定義を検証して参照評価器を生成します。</summary>
        public YawKeyformEvaluator(KeyformGroup group)
        {
            var diagnostics = VlatValidator.ValidateKeyformDefinition(group);
            if (diagnostics.Count != 0) throw new ArgumentException(diagnostics[0].Message, nameof(group));
            _group = group;
        }

        /// <summary>全キーの重みを上書きし、基準形状の残余重みを返します。Yawは任意の有限角度です。</summary>
        /// <param name="yaw">頭部ローカルYaw角度。度単位。</param>
        /// <param name="weights">キー数と同じ長さの呼び出し側バッファ。</param>
        /// <returns>支持区間外では1、キー間では0となる基準形状の重み。</returns>
        public double Evaluate(double yaw, double[] weights)
        {
            if (weights == null || weights.Length != _group.Keys.Count)
                throw new ArgumentException("キー数と同じ長さの重みバッファが必要です。", nameof(weights));
            Array.Clear(weights, 0, weights.Length);
            if (double.IsNaN(yaw) || double.IsInfinity(yaw))
                throw new ArgumentOutOfRangeException(nameof(yaw), "Yawは有限値である必要があります。");
            // 先に剰余を取り、巨大な有限角度で引き算がoverflowするのを防ぎます。
            var offset = ((yaw % 360) - _group.SupportStart) % 360;
            if (offset < 0) offset += 360;
            var angle = _group.SupportStart + offset;
            if (angle <= _group.SupportStart || angle >= _group.SupportEnd) return 1;

            var first = _group.Keys[0].Yaw;
            if (angle <= first)
            {
                weights[0] = Smooth((angle - _group.SupportStart) / (first - _group.SupportStart));
                return 1 - weights[0];
            }
            for (var i = 1; i < weights.Length; i++)
            {
                if (angle > _group.Keys[i].Yaw) continue;
                var t = Smooth((angle - _group.Keys[i - 1].Yaw) / (_group.Keys[i].Yaw - _group.Keys[i - 1].Yaw));
                weights[i - 1] = 1 - t;
                weights[i] = t;
                return 0;
            }
            var last = weights.Length - 1;
            weights[last] = 1 - Smooth((angle - _group.Keys[last].Yaw) / (_group.SupportEnd - _group.Keys[last].Yaw));
            return 1 - weights[last];
        }

        private static double Smooth(double t) => t * t * (3 - 2 * t);
    }
}
