using System;
using BepInEx.Configuration;
using UnityEngine;

namespace BlasphemousTrainer
{
    public sealed partial class Plugin
    {
        private ConfigEntry<int> defenseTier;
        private static readonly string[] DefenseModeNames = { "关闭", "减伤", "免扣血" };

        private void InitializeDefense()
        {
            defenseTier = Config.Bind("Preferences", "DefenseTier", 50,
                "Damage reduction percent (25/50/75/90). Applies only when defense mode is Reduce.");
            defenseTier.Value = DefenseRules.ClampTier(defenseTier.Value);
        }

        // Anti-duplication rule: "免扣血" IS feature 0 (锁定生命). The displayed mode is derived
        // from feature 0 so the two buttons can never disagree, and reduce never stacks with it.
        private int DefenseDisplayMode()
        {
            if (features == null) return 0;
            if (features.Enabled[0]) return 2;
            return features.DefenseMode == 1 ? 1 : 0;
        }

        private void DrawDefenseSection()
        {
            if (features == null) return;
            bool ready = GameBindings.Ready;
            int mode = DefenseDisplayMode();
            GUILayout.BeginHorizontal();
            GUILayout.Label("防御模式", rowStyle, GUILayout.ExpandWidth(true));
            Button("−", () => AdjustDefenseTier(-1), ready, null, null, 36);
            Button(defenseTier.Value + "％", () => { }, ready, step => AdjustDefenseTier(step), null, 75);
            Button("+", () => AdjustDefenseTier(1), ready, null, null, 36);
            GUILayout.Space(12);
            Button(DefenseModeNames[mode], CycleDefenseMode, features.Available[0] && (ready || mode != 0), null, null, 100);
            GUILayout.EndHorizontal();
            GUILayout.Label(mode == 1 ? "减伤档位作用于原始伤害；元素抗性与防御随后结算。" :
                "点击切换：关闭／减伤／免扣血；免扣血仅普通受击。", mutedStyle);
        }
        private void CycleDefenseMode()
        {
            if (features == null) { Notify("游戏版本不兼容，无法切换防御模式。", true); return; }
            int mode = DefenseDisplayMode();
            int next = (mode + 1) % 3;
            if (!features.SetDefenseMode(next)) { Notify("防御切换失败：玩家未就绪或接口不可用，原状态保留。", true); return; }
            Notify(next == 1 ? "防御模式：减伤 " + defenseTier.Value + "％（元素抗性结算前）。" :
                next == 2 ? "防御模式：免扣血（仅普通受击）。" : "防御模式：关闭。");
            // The defense mode is a sustained effect, so it belongs in the cheat memory snapshot.
            SaveMemorySelection();
        }

        private void AdjustDefenseTier(int step)
        {
            int next = DefenseRules.CycleTier(defenseTier.Value, step);
            if (next == defenseTier.Value) return;
            defenseTier.Value = next;
            ApplyFactors();
            Notify("减伤档位已设为 " + next + "％" + (DefenseDisplayMode() == 1 ? "" : "；防御模式非减伤时暂不生效"));
            SavePreferences();
        }
    }
}
