using System;
using BlasphemousTrainer;
class RulesTests
{
    static int count;
    static void Check(bool condition, string name) { count++; if (!condition) { Console.WriteLine("FAIL: " + name); Environment.Exit(1); } }
    static bool Near(float a, float b) { return Math.Abs(a - b) < .001f; }
    static bool Try(string text, float current, float max, out int amount)
    { float next; string error; return TrainerRules.TryAdjustment(text, current, max, out amount, out next, out error); }
    static void Main()
    {
        int amount;
        foreach (string invalid in new[] { "", "0", "1.5", "NaN", "Infinity", "1e3", " 1000", "2147483648", "999999999999" })
            Check(!Try(invalid, 0, float.MaxValue, out amount), "Reject " + invalid);
        Check(Try("1000", 0, float.MaxValue, out amount) && amount == 1000, "1000 addition");
        Check(Try("10000", 300, float.MaxValue, out amount) && amount == 10000, "10000 addition");
        Check(Try("999", 100, 1099, out amount), "exact game cap");
        Check(!Try("1000", 100, 1099, out amount), "game cap overflow");
        Check(!Try("1", 9999999, float.MaxValue, out amount), "precision cap");
        Check(!Try("1", float.NaN, float.MaxValue, out amount), "invalid balance");
        Check(Try("1000", .1f, float.MaxValue, out amount), "fractional balance tolerates sub-cent float rounding");
        Check(Try("+1000", 100, float.MaxValue, out amount) && amount == 1000, "explicit positive");
        Check(Try("-100", 100, float.MaxValue, out amount) && amount == -100, "exact zero");
        Check(!Try("-101", 100, float.MaxValue, out amount), "insufficient whole rejection");
        Check(!Try("-1", 0, float.MaxValue, out amount), "zero balance subtraction");
        Check(Try("-1000", 10001000, float.MaxValue, out amount), "above plugin cap may decrease");
        Check(!Try("-2147483648", 100, float.MaxValue, out amount), "min integer insufficient without negation overflow");
        Check(!Try("-1", 33554432, float.MaxValue, out amount), "reject float precision loss");
        foreach (string invalid in new[] { "+", "-", "--1", "+-1", "1 ", " 1", "\t1", "1\n" })
            Check(!Try(invalid, 100, float.MaxValue, out amount), "strict signed format " + invalid);
        for (int balance = 0; balance < 200; balance++)
            for (int delta = -210; delta <= 210; delta++)
            {
                float result; string error;
                bool ok = TrainerRules.TryAdjustment(delta.ToString(System.Globalization.CultureInfo.InvariantCulture), balance, 300, out amount, out result, out error);
                bool valid = delta != 0 && balance + delta >= 0 && balance + delta <= 300;
                Check(ok == valid && (!ok || (amount == delta && result == balance + delta)), "signed boundary matrix");
            }
        Check(TrainerRules.Reward(100, 120, float.MaxValue, 3) == 160, "reward delta only");
        Check(TrainerRules.Reward(100, 80, float.MaxValue, 3) == 80, "expense unchanged");
        Check(TrainerRules.Reward(100, 100, float.MaxValue, 3) == 100, "assignment unchanged");
        Check(TrainerRules.Reward(9999990, 9999995, float.MaxValue, 10) == 9999999, "reward cap");
        Check(TrainerRules.Reward(100, 110, 115, 10) == 115, "game reward cap");
        Check(TrainerRules.Clamp(float.NaN, 1, 2) == 1, "NaN default");
        Check(TrainerRules.Clamp(float.PositiveInfinity, 1, 2) == 1, "infinity default");
        Check(TrainerRules.Clamp(20, 1, 2) == 2, "move cap");
        Check(TrainerRules.Clamp(2, 1, 1.5f) == 1.5f, "jump cap");
        Check(DefenseRules.ClampTier(0) == 25 && DefenseRules.ClampTier(100) == 90 && DefenseRules.ClampTier(50) == 50, "tier clamp set membership");
        Check(DefenseRules.ClampTier(40) == 50 && DefenseRules.ClampTier(60) == 50 && DefenseRules.ClampTier(88) == 90, "tier clamp nearest");
        Check(DefenseRules.CycleTier(25, 1) == 50 && DefenseRules.CycleTier(90, 1) == 25 && DefenseRules.CycleTier(25, -1) == 90 && DefenseRules.CycleTier(40, 1) == 50 && DefenseRules.CycleTier(50, -1) == 25, "tier cycle wrap");
        Check(Near(DefenseRules.Reduce(100f, .25f), 75f) && Near(DefenseRules.Reduce(100f, .5f), 50f) && Near(DefenseRules.Reduce(100f, .75f), 25f) && Near(DefenseRules.Reduce(100f, .9f), 10f), "reduce tiers");
        Check(DefenseRules.Reduce(0f, .5f) == 0f && DefenseRules.Reduce(-5f, .5f) == -5f, "reduce guards non-positive");
        Check(float.IsNaN(DefenseRules.Reduce(float.NaN, .5f)) && float.IsInfinity(DefenseRules.Reduce(float.PositiveInfinity, .5f)), "reduce guards non-finite");
        for (int i = 0; i < 1000; i++)
        {
            float d = i * 0.77f; float r = DefenseRules.Reduce(d, .9f);
            Check(r >= 0 && r <= d, "bounded reduction " + i);
        }
        for (int i = 0; i < 1000; i++)
        {
            float initial = i * 9000; float next = TrainerRules.Reward(initial, initial + 99, float.MaxValue, 10);
            Check(next >= initial && next <= TrainerRules.TearsLimit, "bounded reward " + i);
        }
        Check(PlaytimeRules.IsCountable(.016f) && !PlaytimeRules.IsCountable(0f) && !PlaytimeRules.IsCountable(-1f), "frame delta must be positive");
        Check(!PlaytimeRules.IsCountable(float.NaN) && !PlaytimeRules.IsCountable(float.PositiveInfinity), "frame delta must be finite");
        Check(PlaytimeRules.Accumulate(10f, 1f, true) == 11f, "excluded frame accumulates");
        Check(PlaytimeRules.Accumulate(10f, 1f, false) == 10f, "counted frame keeps total");
        Check(PlaytimeRules.Accumulate(10f, float.NaN, true) == 10f, "NaN sample keeps total");
        Check(PlaytimeRules.Accumulate(10f, float.PositiveInfinity, true) == 10f, "infinite sample keeps total");
        Check(PlaytimeRules.Accumulate(10f, -3f, true) == 10f, "negative sample keeps total");
        Check(PlaytimeRules.Accumulate(10f, 0f, true) == 10f, "zero sample keeps total");
        Check(PlaytimeRules.Accumulate(float.MaxValue, float.MaxValue, true) == float.MaxValue, "overflow keeps total");
        {
            // Exact integers on purpose: accumulating 1/60 would drift far past the 0.001
            // tolerance, and this case asserts the accumulate/adjust contract, not float drift.
            float excluded = 0f;
            for (int frame = 0; frame < 600; frame++) excluded = PlaytimeRules.Accumulate(excluded, 1f, frame < 300);
            Check(excluded == 300f && PlaytimeRules.Adjusted(1000f, excluded) == 700f, "freeze covers the frozen interval only");
        }
        Check(PlaytimeRules.Adjusted(100f, 40f) == 60f, "menu time removed");
        Check(PlaytimeRules.Adjusted(10f, 40f) == 0f, "clock never negative");
        Check(PlaytimeRules.Adjusted(100f, 0f) == 100f && PlaytimeRules.Adjusted(100f, -5f) == 100f, "no exclusion passes through");
        Check(PlaytimeRules.Adjusted(100f, float.NaN) == 100f, "NaN exclusion passes through");
        Check(float.IsNaN(PlaytimeRules.Adjusted(float.NaN, 5f)) && float.IsInfinity(PlaytimeRules.Adjusted(float.PositiveInfinity, 5f)), "non-finite raw passes through");
        for (int i = 0; i < 1000; i++)
        {
            float raw = i * 3.5f; float excluded = i * 1.25f; float adjusted = PlaytimeRules.Adjusted(raw, excluded);
            Check(adjusted >= 0f && adjusted <= raw, "bounded exclusion " + i);
        }
        Console.WriteLine("PASS: " + count + " numeric checks (production TrainerRules.cs, DefenseRules.cs, PlaytimeRules.cs).");
    }
}
