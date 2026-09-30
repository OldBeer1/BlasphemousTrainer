using System;
using BlasphemousTrainer;
class MemoryTests
{
    static int count;
    static void Check(bool value, string label) { count++; if (!value) { Console.WriteLine("FAIL: " + label); Environment.Exit(1); } }
    static void Main()
    {
        var memory = new CheatMemory();
        Check(!memory.BeginRestore(false), "loading cannot consume restore");
        Check(memory.BeginRestore(true), "first ready consumes once");
        Check(!memory.BeginRestore(true), "duplicate ready cannot toggle");
        Check(!memory.BeginRestore(false), "scene or death loading does not rearm");
        Check(!memory.BeginRestore(true), "scene respawn does not restore again");
        memory.EnterMenu(); memory.EnterMenu();
        Check(memory.BeginRestore(true), "menu reentry restores once");
        Check(!memory.BeginRestore(true), "duplicate menu event still one restore");
        bool ignored, reduce;
        // Derived from the production id table so adding a feature cannot silently truncate the sweep.
        int features = CheatMemory.Parse("", out ignored, out reduce).Length;
        var flags = CheatMemory.Parse("life,move,life,unknown,-1000", out ignored, out reduce);
        Check(ignored && !reduce && flags[0] && flags[5] && !flags[4], "unknown ids and money commands ignored");
        Check(CheatMemory.Capture(flags, 0) == "life,move", "stable deduplicated state");
        Check(CheatMemory.Capture(new bool[features], 0) == "", "all off persisted as empty");
        Check(CheatMemory.Capture(new bool[features], 1) == "reduce", "reduce mode stored on its own");
        Check(CheatMemory.Capture(new bool[features], 2) == "", "免扣血 rides on the life feature, not on a mode id");
        var lifeOnly = CheatMemory.Parse("life", out ignored, out reduce);
        Check(!reduce && lifeOnly[0], "life does not imply reduce");
        var reduceOnly = CheatMemory.Parse("reduce", out ignored, out reduce);
        Check(reduce && !reduceOnly[0] && !ignored, "reduce parses without feature flags");
        {
            var both = new bool[features]; both[0] = true;
            string text = CheatMemory.Capture(both, 1);
            CheatMemory.Parse(text, out ignored, out reduce);
            Check(text == "life" && !reduce, "免扣血 wins over a stale reduce entry");
        }
        foreach (int mode in new[] { 0, 1, 2 })
        {
            var parsed = CheatMemory.Parse(CheatMemory.Capture(new bool[features], mode), out ignored, out reduce);
            Check(!ignored && !parsed[0] && reduce == (mode == 1), "defense mode roundtrip " + mode);
        }
        for (int mask = 0; mask < (1 << features); mask++)
        {
            var original = new bool[features]; for (int i = 0; i < features; i++) original[i] = (mask & (1 << i)) != 0;
            var parsed = CheatMemory.Parse(CheatMemory.Capture(original, 0), out ignored, out reduce);
            Check(!ignored && !reduce, "roundtrip valid");
            for (int i = 0; i < features; i++) Check(original[i] == parsed[i], "roundtrip feature");
        }
        Console.WriteLine("PASS: " + count + " memory lifecycle/serialization checks.");
    }
}