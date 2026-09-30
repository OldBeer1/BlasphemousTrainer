using System;
using System.Globalization;

namespace BlasphemousTrainer
{
    // Pure limits shared by the UI, gameplay hooks and executable boundary tests.
    internal static class TrainerRules
    {
        internal const int TearsLimit = 9999999;
        internal static float Clamp(float value, float min, float max)
        { return float.IsNaN(value) || float.IsInfinity(value) ? min : Math.Max(min, Math.Min(max, value)); }

        internal static bool TryAdjustment(string text, float current, float gameMax, out int amount, out float result, out string error)
        {
            amount = 0; result = current; error = "请输入非零整数，可带 + 或 -，不接受空格、小数或超长数字。";
            int parsed;
            if (string.IsNullOrEmpty(text) || !int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed) || parsed == 0) return false;
            if (float.IsNaN(current) || float.IsInfinity(current) || current < 0 || float.IsNaN(gameMax) || gameMax < 0)
            { error = "当前余额或上限异常，未调整。"; return false; }
            double next = (double)current + parsed;
            if (next < 0) { error = "余额不足，整笔调整已拒绝。"; return false; }
            if (parsed > 0 && next > Math.Min(gameMax, TearsLimit))
            { error = "调整后超过游戏上限或 9,999,999，整笔调整已拒绝。"; return false; }
            // The original API takes float. Reject unrepresentable deltas instead of silently rounding.
            float representedAmount = parsed, representedResult = (float)next;
            if ((double)representedAmount != parsed || Math.Abs((double)representedResult - next) > .01)
            { error = "该余额下无法精确调整此金额，请使用可精确表示的金额。"; return false; }
            amount = parsed; result = (float)next; error = ""; return true;
        }
        internal static float Reward(float current, float requested, float gameMax, float multiplier)
        {
            if (float.IsNaN(current) || float.IsInfinity(current) || float.IsNaN(requested) || float.IsInfinity(requested) || float.IsNaN(gameMax)) return requested;
            if (requested <= current || current >= Math.Min(gameMax, TearsLimit)) return requested;
            return (float)Math.Min(Math.Min(gameMax, TearsLimit), current + ((double)requested - current) * Clamp(multiplier, 1, 10));
        }
    }
}
