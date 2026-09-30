namespace BlasphemousTrainer
{
    internal sealed class FeatureDefinition
    {
        internal readonly string Id, Name, Preference;
        internal readonly int Factor;
        internal readonly float Maximum, Default, Step;
        internal FeatureDefinition(string id, string name, int factor = -1, string preference = null,
            float maximum = 1, float initial = 1, float step = 1)
        { Id = id; Name = name; Factor = factor; Preference = preference; Maximum = maximum; Default = initial; Step = step; }
    }
    internal static class FeatureCatalog
    {
        // Indices/ids remain stable: existing shortcuts and saved cheat selections keep their meaning.
        internal static readonly FeatureDefinition[] All = {
            new FeatureDefinition("life", "防御免扣血（普通受击）"),
            new FeatureDefinition("fervour", "无限热情"),
            new FeatureDefinition("flasks", "血瓶不消耗"),
            new FeatureDefinition("sword", "剑击伤害倍率", 0, "DamageMultiplier", 10, 2),
            new FeatureDefinition("income", "击杀赎罪之泪倍率", 1, "TearsMultiplier", 10),
            new FeatureDefinition("move", "移动速度倍率", 2, "MoveMultiplier", 2, 1, .1f),
            new FeatureDefinition("jump", "跳跃增强", 3, "JumpMultiplier", 1.5f, 1, .1f),
            new FeatureDefinition("prayer", "祷文伤害倍率（部分）", 4, "PrayerMultiplier", 10),
            new FeatureDefinition("menutime", "菜单暂停计时（背包/地图）"),
            new FeatureDefinition("freeze", "自由暂停计时")
        };
        // Defense replaces the duplicate life row, but its stable id/shortcut remain available.
        internal static readonly int[] BasicOrder = { 1, 2, 5, 6 };
        internal static readonly int[] AdvancedOrder = { 3, 7, 4, 8, 9 };
        internal static int Count { get { return All.Length; } }
        internal static int FactorCount { get { int count = 0; foreach (var f in All) if (f.Factor >= 0) count++; return count; } }
        internal static FeatureDefinition ForFactor(int factor) { return All[FactorToFeature(factor)]; }
        internal static int FactorToFeature(int factor) { for (int i = 0; i < Count; i++) if (All[i].Factor == factor) return i; return -1; }
        internal static string[] Names { get { var result = new string[Count]; for (int i = 0; i < Count; i++) result[i] = All[i].Name; return result; } }
        internal static string[] Ids { get { var result = new string[Count]; for (int i = 0; i < Count; i++) result[i] = All[i].Id; return result; } }
    }
}
