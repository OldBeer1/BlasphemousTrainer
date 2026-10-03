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
            Section("防御");
            bool ready = GameBindings.Ready;
            int mode = DefenseDisplayMode();
            string status = mode == 0 ? "已关闭" : (ready ? "已生效" : "等待玩家");
            var oldColor = GUI.contentColor;
            if (mode != 0) GUI.contentColor = ActiveTextColor;
            Button("防御模式：" + DefenseModeNames[mode] + "：" + status + "（点击切换）",
                CycleDefenseMode, features.Available[0] && (ready || mode != 0));
            GUI.contentColor = oldColor;
            if (mode == 1)
                GUILayout.Label("减伤作用于原始伤害（元素抗性与防御结算之前），实际掉血减少不低于所选档位。");
            GUILayout.BeginHorizontal();
            Button("减伤档位 " + defenseTier.Value + "％（← / →）", () => { }, ready, step => AdjustDefenseTier(step));
            Button("−", () => AdjustDefenseTier(-1), ready);
            Button("+", () => AdjustDefenseTier(1), ready);
            GUILayout.EndHorizontal();
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
