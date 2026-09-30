using System;
using System.Reflection;
using BlasphemousTrainer;

// Exercise the production partial class and Harmony callback bodies with deterministic clocks.
// The doubles only stand in for Unity/game/Harmony infrastructure, not trainer behavior.
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }
namespace Gameplay.UI { public class UIController { public static UIController instance; public bool IsShowingMenu; } }
namespace Framework.Managers {
    public class PersistentManager {
        public class PersistentData { }
        public class PersitentPersistenceData : PersistentData { public float Time; }
    }
}
namespace BlasphemousTrainer {
    internal static class GameBindings {
        internal static bool Ready = true;
        internal static object Require(Type type, string name, Type result, params Type[] args) { return null; }
    }
    internal sealed class TestLog { internal void LogInfo(string value) { } internal void LogError(string value) { } }
    internal sealed class TestHarmony { internal void Patch(object original, object postfix = null, object prefix = null) { } }
    internal sealed partial class FeatureController {
        internal static FeatureController Current;
        internal readonly bool[] Enabled = new bool[FeatureCatalog.Count], Available = new bool[FeatureCatalog.Count];
        internal readonly string[] Errors = new string[FeatureCatalog.Count];
        internal readonly TestLog Log = new TestLog();
        internal int DefenseMode;
        internal void InstallGroup(int index, Action<TestHarmony> install) { }
        internal static object Hook(string name) { return null; }
    }
}
class StateFlowTests {
    static int count;
    static void Check(bool value, string label) { count++; if (!value) { Console.WriteLine("FAIL: " + label); Environment.Exit(1); } }
    static object[] Call(string name, params object[] args) {
        typeof(FeatureController).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args); return args;
    }
    static FeatureController Fresh(float now) {
        var f = new FeatureController(); FeatureController.Current = f;
        for (int i = 0; i < f.Available.Length; i++) f.Available[i] = true;
        UnityEngine.Time.realtimeSinceStartup = now;
        Gameplay.UI.UIController.instance = new Gameplay.UI.UIController(); f.SamplePlaytime(); return f;
    }
    static float Read(float raw) { return (float)Call("AdjustPlayTime", raw)[0]; }
    static float Save(float raw) {
        var data = new Framework.Managers.PersistentManager.PersitentPersistenceData { Time = raw };
        Call("AdjustSavedTime", data); return data.Time;
    }
    static void Main() {
        var f = Fresh(100); f.Enabled[9] = true;
        UnityEngine.Time.realtimeSinceStartup = 110; f.SamplePlaytime();
        Check(Read(1010) == 1000, "freeze read excludes ten seconds");
        f.Enabled[9] = false;
        UnityEngine.Time.realtimeSinceStartup = 115; f.SamplePlaytime();
        Check(Read(1015) == 1005, "both switches off retain historical exclusion");
        Check(Save(1015) == 1005 && Save(1015) == 1005, "repeated saves retain correction once");
        Call("ResetExcludedOnLoad", false);
        Check(f.ExcludedTime == 10, "non-loading persistence does not reset baseline");
        Call("ResetExcludedOnLoad", true);
        Check(Read(1005) == 1005 && Save(1005) == 1005, "load corrected save never double subtracts");
        f.Enabled[8] = f.Enabled[9] = true; Gameplay.UI.UIController.instance.IsShowingMenu = true;
        UnityEngine.Time.realtimeSinceStartup = 120; f.SamplePlaytime();
        Check(f.ExcludedTime == 5, "two switches exclude once");
        f.Enabled[9] = false; UnityEngine.Time.realtimeSinceStartup = 125; f.SamplePlaytime();
        Check(f.ExcludedTime == 10, "menu continues after manual freeze off");
        Gameplay.UI.UIController.instance.IsShowingMenu = false;
        UnityEngine.Time.realtimeSinceStartup = 130; f.SamplePlaytime();
        Check(f.ExcludedTime == 10, "closed menu counts normally");
        Call("ResetExcludedOnReset");
        Check(f.ExcludedTime == 0, "new slot resets historical exclusion");
        f = Fresh(200); f.SetEnabled(9, true);
        UnityEngine.Time.realtimeSinceStartup = 210; f.SetEnabled(9, false);
        Check(f.ExcludedTime == 10 && Read(1010) == 1000, "switch commits interval before disabling");
        f.SetEnabled(9, true); UnityEngine.Time.realtimeSinceStartup = 215;
        Check(Save(1015) == 1000, "save samples current interval before adjustment");
        f.DisableAll(); UnityEngine.Time.realtimeSinceStartup = 220;
        Check(Read(1020) == 1005 && Save(1020) == 1005, "disable-all preserves clock and saved correction");
        UnityEngine.Time.realtimeSinceStartup = 300; Call("ResetExcludedOnLoad", true);
        f.SetEnabled(9, true); UnityEngine.Time.realtimeSinceStartup = 301; f.SamplePlaytime();
        Check(f.ExcludedTime == 1, "load refreshes sample baseline without previous-slot gap");
        Check(f.SetDefenseMode(1) && f.DefenseMode == 1 && !f.Enabled[0], "reduce selected alone");
        Check(f.SetEnabled(0, true) && f.DefenseMode == 0 && f.Enabled[0], "legacy life shortcut clears reduce");
        Check(f.SetDefenseMode(1) && !f.Enabled[0], "reduce clears immune");
        Check(f.SetDefenseMode(2) && f.DefenseMode == 0 && f.Enabled[0], "immune clears reduce");
        GameBindings.Ready = false;
        Check(!f.SetDefenseMode(1) && f.Enabled[0], "rejected switch retains original state");
        Check(f.SetDefenseMode(0) && !f.Enabled[0], "off permitted before player ready");
        GameBindings.Ready = true;
        f.Available[0] = false;
        Check(!f.SetDefenseMode(1) && f.DefenseMode == 0, "unavailable defense does not falsely enable");
        f.DisableAll(); f.Available[0] = true; f.SetDefenseMode(1); f.SetEnabled(5, true);
        string snapshot = CheatMemory.Capture(f.Enabled, f.DefenseMode);
        f.DisableAll();
        Check(snapshot == "move,reduce" && !Array.Exists(f.Enabled, x => x) && f.DefenseMode == 0, "menu cleanup leaves stored selection intact");
        var memory = new CheatMemory(); memory.EnterMenu();
        Check(!memory.BeginRestore(false) && memory.BeginRestore(true), "restore waits for ready once");
        bool ignored, reduce; var desired = CheatMemory.Parse(snapshot, out ignored, out reduce);
        for (int i = 0; i < desired.Length; i++) if (desired[i]) f.SetEnabled(i, true);
        if (reduce && !desired[0]) f.SetDefenseMode(1);
        Check(f.DefenseMode == 1 && f.Enabled[5] && !f.Enabled[0] && !memory.BeginRestore(true), "memory restores mutually exclusive mode once");
        f.DisableAll(); snapshot = CheatMemory.Capture(f.Enabled, f.DefenseMode);
        Check(snapshot == "", "explicit all-off captures empty memory");
        foreach (int value in new[] { int.MinValue, int.MinValue + 25, int.MinValue + 50, -1000 })
            Check(DefenseRules.ClampTier(value) == 25, "negative extreme tier safely clamps " + value);
        Check(DefenseRules.ClampTier(int.MaxValue) == 90, "positive extreme tier safely clamps");
        Check(!f.SetEnabled(-1, true) && !f.SetDefenseMode(3), "invalid state requests refused");
        var pointer = new PointerClick();
        pointer.Step(true, false, true, 2, 10, 10); pointer.Cancel();
        Check(pointer.Step(false, true, false, 2, 10, 10) == -1, "cancel pending mouse press never executes on release");
        var press = new PanelPress(); press.Step(false); press.Reset();
        Check(!press.Step(true), "cancelled controller opening waits for release");
        foreach (var size in new[] { new[] {320f, 180f}, new[] {640f, 360f}, new[] {1280f, 720f}, new[] {1920f, 1080f} }) {
            var placements = NoticeLayout.Plan(size[0], size[1], size[1] - 2, new[] {120f, 180f, 600f});
            Check(placements.Length > 0 && placements[placements.Length - 1].Index == 2, "newest notice retained");
            float end = 0;
            foreach (var p in placements) {
                Check(p.X >= 0 && p.Y >= end && p.X + p.Width <= size[0] && p.Y + p.Height <= size[1], "notices fit actual screen without overlap");
                end = p.Y + p.Height;
            }
        }
        var cache = new ControllerMapCache(); int rebuilds = 0;
        for (int frame = 0; frame < 120; frame++) {
            float now = frame / 60f;
            if (cache.Due(now, false)) rebuilds++;
            Check(!cache.Due(now, false), "second same-frame poll reuses map cache");
        }
        Check(rebuilds == 4, "120 frames rebuild only four times");
        Check(cache.Due(2, true) && !cache.Due(2, false), "device change refreshes immediately once");
        cache.Invalidate(); Check(cache.Due(2, false), "remapping or explicit invalidate refreshes immediately");
        Check(FeatureCatalog.Names.Length == FeatureCatalog.Ids.Length && FeatureCatalog.Count == desired.Length, "catalog dimensions align with serialized flags");
        for (int i = 0; i < FeatureCatalog.FactorCount; i++)
            Check(FeatureCatalog.All[FeatureCatalog.FactorToFeature(i)].Factor == i, "factor mapping roundtrip");
        Console.WriteLine("PASS: " + count + " production state-flow/layout/cache/catalog checks.");
    }
}
