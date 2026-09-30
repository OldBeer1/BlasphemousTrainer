using System;
using System.Collections.Generic;

namespace BlasphemousTrainer
{
    // Stable feature identifiers only: never serialize amounts, commands, or player references.
    //
    // Ids is aligned index-for-index with FeatureController.Enabled, so it must stay exactly as
    // long as the feature table. "reduce" is the one identifier that is not a feature: the
    // defense mode is a three-way choice, and 免扣血 is already feature 0 ("life"), so only the
    // reduce mode needs a representation of its own.
    internal sealed class CheatMemory
    {
        private static readonly string[] Ids = FeatureCatalog.Ids;
        internal const string ReduceId = "reduce";
        private bool pending = true;
        internal void EnterMenu() { pending = true; }
        internal bool BeginRestore(bool ready)
        {
            if (!pending || !ready) return false;
            pending = false; return true;
        }
        internal static string Capture(bool[] enabled, int defenseMode)
        {
            var ids = new List<string>();
            for (int i = 0; i < Ids.Length && i < enabled.Length; i++) if (enabled[i]) ids.Add(Ids[i]);
            // 免扣血 wins: it is the same switch as feature 0, so the two are never stored together.
            if (defenseMode == 1 && !(enabled.Length > 0 && enabled[0])) ids.Add(ReduceId);
            return string.Join(",", ids.ToArray());
        }
        internal static bool[] Parse(string text, out bool ignored, out bool reduce)
        {
            var enabled = new bool[Ids.Length]; ignored = false; reduce = false;
            if (string.IsNullOrEmpty(text)) return enabled;
            foreach (string id in text.Split(','))
            {
                if (id == ReduceId) { reduce = true; continue; }
                int index = Array.IndexOf(Ids, id);
                if (index >= 0) enabled[index] = true; else ignored = true;
            }
            return enabled;
        }
    }
}
