using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using Framework.Managers;

namespace BlasphemousTrainer
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> rememberCheats;
        private ConfigEntry<string> rememberedFeatures;
        private readonly CheatMemory memory = new CheatMemory();
        private bool memoryWarning;

        private void InitializeMemory()
        {
            rememberCheats = Config.Bind("Memory", "RememberCheats", false, "Apply sustained feature switches to all supported local normal-game saves. Never replay currency adjustments.");
            rememberedFeatures = Config.Bind("Memory", "Features", "", "Stable comma-separated feature ids. Empty means all off.");
            if (!rememberCheats.Value) rememberedFeatures.Value = "";
        }
        private void SaveMemorySelection()
        {
            if (!rememberCheats.Value || features == null) return;
            rememberedFeatures.Value = CheatMemory.Capture(features.Enabled, DefenseDisplayMode());
            SavePreferences();
        }
        private void ToggleMemory()
        {
            if (!GameBindings.Ready || features == null) { Notify("请进入受支持的普通存档后设置作弊记忆。", true); return; }
            rememberCheats.Value = !rememberCheats.Value;
            rememberedFeatures.Value = rememberCheats.Value ? CheatMemory.Capture(features.Enabled, DefenseDisplayMode()) : "";
            Notify(rememberCheats.Value ? "已开启作弊记忆，保存当前持续功能开关。" : "已关闭作弊记忆；当前功能保持，下次进入存档不再自动恢复。");
            SavePreferences();
        }
        private void RestoreMemoryWhenReady()
        {
            if (!GameBindings.Ready || !fingerprintMatches || features == null)
            {
                if (rememberCheats.Value && !memoryWarning && Core.ready && Core.GameModeManager != null &&
                    !Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.MENU) &&
                    (!fingerprintMatches || !Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.NEW_GAME)))
                { memoryWarning = true; Notify("当前版本或模式不支持恢复作弊记忆，记录已保留。", true); }
                return;
            }
            if (!memory.BeginRestore(true) || !rememberCheats.Value) return;
            bool ignored, reduce;
            bool[] desired = CheatMemory.Parse(rememberedFeatures.Value, out ignored, out reduce);
            var restored = new List<string>(); var skipped = new List<string>();
            ApplyFactors();
            for (int i = 0; i < desired.Length; i++)
            {
                if (!desired[i]) continue;
                if (features.SetEnabled(i, true)) restored.Add(names[i]); else skipped.Add(names[i]);
            }
            // The remembered reduce mode only applies when 免扣血 is not the remembered state:
            // they are the same switch, and feature 0 always wins over a stale "reduce" entry.
            if (reduce && !desired[0])
            {
                if (features.SetDefenseMode(1)) restored.Add("防御减伤 " + defenseTier.Value + "％");
                else skipped.Add("防御减伤");
            }
            Logger.LogInfo("Cheat memory restored: " + string.Join(", ", restored.ToArray()) + "; skipped: " + string.Join(", ", skipped.ToArray()));
            string summary = restored.Count == 0 ? "作弊记忆已开启，无需恢复功能。" : "作弊记忆：已恢复 " + restored.Count + " 项，状态请见面板。";
            if (skipped.Count > 0) summary += "不可用已跳过 " + skipped.Count + " 项，详情见日志。";
            if (ignored) summary += "；已忽略无法识别的记忆配置。";
            // Menu cleanup and partial restores must never overwrite the user's stored selection.
            Notify(summary, skipped.Count > 0 || ignored);
        }
        private string MoneyPreview(string text)
        {
            if (!GameBindings.Ready) return "余额预览：玩家未就绪";
            int amount; float result; string error;
            var resource = GameBindings.Player.Stats.Purge;
            if (!TrainerRules.TryAdjustment(text, resource.Current, resource.CurrentMax, out amount, out result, out error)) return "余额预览：" + error;
            return "余额预览：" + resource.Current.ToString("0.##") + " → " + result.ToString("0.##") + "（" + (amount > 0 ? "+" : "") + amount + "）；执行时再次校验";
        }
    }
}
