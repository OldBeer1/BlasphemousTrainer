using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlasphemousTrainer
{
    public sealed partial class Plugin
    {
        private sealed class Notice { internal string Text; internal float Until; internal bool Error; }
        private readonly List<Notice> notices = new List<Notice>();
        private readonly List<Texture2D> skinTextures = new List<Texture2D>();
        private GUIStyle sectionStyle, noticeStyle;
        private bool numberPad;
        private string numberDraft;
        private int savedRow;
        private Vector2 savedScroll;

        private void Notify(string text, bool error = false)
        {

            if (string.IsNullOrEmpty(text)) return;
            // Rapid changes to one control replace its most recent notice, not a wall of popups.
            if (notices.Count > 0 && notices[notices.Count - 1].Text == text && notices[notices.Count - 1].Until > Time.unscaledTime) return;
            notices.Add(new Notice { Text = text, Error = error, Until = Time.unscaledTime + (error ? 4f : 2.5f) });
            Logger.LogInfo("Notice: " + text);
            while (notices.Count > 3) notices.RemoveAt(0);
        }
        private void SavePreferences()
        {
            try { Config.Save(); }
            catch (Exception e) { Logger.LogWarning("Preferences save failed: " + e.Message); Notify("本次操作已生效，但设置／作弊记忆保存失败；下次可能无法恢复。", true); }
        }
        private void ToggleWithNotice(int index)
        {
            if (features == null) { Notify("游戏版本不兼容，无法开启功能。", true); return; }
            bool before = features.Enabled[index];
            features.Toggle(index);
            if (before == features.Enabled[index]) { Notify("无法开启：" + names[index] + (features.Available[index] ? "，玩家尚未就绪。" : "，当前接口不兼容。"), true); return; }
            // 锁定生命 (feature 0) is the "免扣血" state; enabling it clears the reduce layer so the
            // defense controls stay mutually exclusive, and the panel derives its label from feature 0.
            if (index == 0 && features.Enabled[0]) features.DefenseMode = 0;
            Notify((features.Enabled[index] ? "已开启：" : "已关闭：") + names[index]); SaveMemorySelection();
        }
        private void DisableAllWithNotice()
        {
            if (features == null) { Notify("当前没有可用的作弊功能。", true); return; }
            bool any = Array.Exists(features.Enabled, flag => flag) || features.DefenseMode != 0;
            features.DisableAll();
            Notify(any ? "已关闭全部作弊与防御功能。" : "所有作弊与防御功能已处于关闭状态。" ); SaveMemorySelection();
        }
        private void AddTearsWithNotice(string text)
        {
            if (features == null) { Notify("游戏版本不兼容，未调整。", true); return; }
            try
            {
                float before = GameBindings.Player != null ? GameBindings.Player.Stats.Purge.Current : 0;
                string result = features.AddTears(text);
                bool success = result.StartsWith("已增加", StringComparison.Ordinal) || result.StartsWith("已减少", StringComparison.Ordinal);
                if (success)
                {
                    int amount;
                    float after = GameBindings.Player.Stats.Purge.Current;
                    if (!int.TryParse(text, out amount) || Mathf.Abs(after - before - amount) > .5f)
                    { Notify("余额与预期不一致，当前余额 " + after.ToString("0") + "；请检查后再操作。", true); return; }
                }
                Notify(result, !success);
            }
            catch (Exception e) { Logger.LogWarning("Add tears failed: " + e.GetBaseException().Message); Notify("金额操作异常，请核对当前余额后再试。", true); }
        }
        private Texture2D Background(Color fill, Color border)
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color[64];
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) pixels[y * 8 + x] = x == 0 || y == 0 || x == 7 || y == 7 ? border : fill;
            texture.SetPixels(pixels); texture.Apply(); skinTextures.Add(texture); return texture;
        }
        private void StylePanel()
        {
            // Do not inherit game skin line limits/offsets designed for its original font.
            foreach (GUIStyle style in new[] { panelSkin.label, panelSkin.button, panelSkin.textField, panelSkin.window, panelSkin.box })
            {
                style.font = font;
                style.fixedHeight = 0;
                style.contentOffset = Vector2.zero;
                style.clipping = TextClipping.Overflow;
            }
            panelSkin.label.padding = new RectOffset(0, 0, 4, 6);
            panelSkin.label.margin = new RectOffset(0, 0, 3, 3);
            panelSkin.button.alignment = TextAnchor.MiddleCenter;
            panelSkin.textField.alignment = TextAnchor.MiddleLeft;
            Color gold = new Color(.74f, .59f, .33f), text = new Color(.92f, .89f, .8f);
            var background = Background(new Color(.07f, .075f, .08f, .98f), gold);
            var normal = Background(new Color(.15f, .15f, .15f), new Color(.33f, .29f, .22f));
            var hover = Background(new Color(.26f, .23f, .17f), gold);
            panelSkin.window.normal.background = background; panelSkin.window.onNormal.background = background;
            panelSkin.window.normal.textColor = gold; panelSkin.window.onNormal.textColor = gold;
            panelSkin.window.border = new RectOffset(2, 2, 2, 2);
            panelSkin.window.padding = new RectOffset(18, 18, 34, 14);
            panelSkin.label.normal.textColor = text;
            panelSkin.button.normal.background = normal; panelSkin.button.hover.background = hover;
            panelSkin.button.active.background = hover; panelSkin.button.focused.background = hover;
            panelSkin.button.normal.textColor = panelSkin.button.hover.textColor = panelSkin.button.active.textColor = panelSkin.button.focused.textColor = text;
            panelSkin.button.border = new RectOffset(2, 2, 2, 2);
            panelSkin.button.padding = new RectOffset(10, 10, 7, 7);
            panelSkin.button.wordWrap = true;
            panelSkin.textField.normal.background = normal; panelSkin.textField.focused.background = hover;
            panelSkin.textField.normal.textColor = panelSkin.textField.focused.textColor = text;
            panelSkin.textField.padding = new RectOffset(10, 10, 8, 8);
            sectionStyle = new GUIStyle(panelSkin.label) { fontSize = 20, fontStyle = FontStyle.Bold, padding = new RectOffset(0, 0, 12, 6) };
            sectionStyle.normal.textColor = gold;
            noticeStyle = new GUIStyle(panelSkin.box) { font = font, fontSize = 18, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 10, 10), border = new RectOffset(2, 2, 2, 2) };
            noticeStyle.normal.background = background; noticeStyle.normal.textColor = text;
            StyleReferenceLayout();
        }
        private void Section(string title) { GUILayout.Label(title, sectionStyle); }
        private void DrawNotifications()
        {
            notices.RemoveAll(n => n.Until <= Time.unscaledTime);
            if (notices.Count == 0) return;
            Matrix4x4 matrix = GUI.matrix; Color color = GUI.contentColor;
            int depth = GUI.depth;
            try
            {
                GUI.matrix = Matrix4x4.identity; GUI.depth = -9999;
                float width = NoticeLayout.Plan(Screen.width, Screen.height, 0, new[] { 1f })[0].Width;
                float x = Screen.width - width - Mathf.Min(12, Mathf.Min(Screen.width, Screen.height) / 4f), y = 100;
                var heights = new float[notices.Count];
                for (int i = 0; i < notices.Count; i++) heights[i] = noticeStyle.CalcHeight(new GUIContent(notices[i].Text), width);
                float totalHeight = 6 * (notices.Count - 1);
                foreach (float height in heights) totalHeight += height;
                if (visible && x < window.xMax * drawScale && x + width > window.x * drawScale)
                {
                    float below = window.yMax * drawScale + 8;
                    if (below + totalHeight + 12 <= Screen.height) y = below;
                    else y = 12; // At narrow resolutions toasts overlap temporarily but never capture clicks.
                }
                foreach (var placement in NoticeLayout.Plan(Screen.width, Screen.height, y, heights))
                {
                    var notice = notices[placement.Index];
                    GUI.contentColor = notice.Error ? new Color(1f, .65f, .55f) : new Color(.72f, .95f, .75f);
                    string text = notice.Text;
                    if (heights[placement.Index] > placement.Height)
                    {
                        // Full text is logged at creation; fit the visible text instead of overflowing.
                        int low = 0, high = text.Length;
                        const string suffix = "…（详见日志）";
                        while (low < high) {
                            int middle = (low + high + 1) / 2;
                            if (noticeStyle.CalcHeight(new GUIContent(text.Substring(0, middle) + suffix), width) <= placement.Height) low = middle;
                            else high = middle - 1;
                        }
                        text = text.Substring(0, low) + suffix;
                    }
                    GUI.BeginGroup(new Rect(placement.X, placement.Y, placement.Width, placement.Height));
                    try { GUI.Box(new Rect(0, 0, placement.Width, placement.Height), text, noticeStyle); }
                    finally { GUI.EndGroup(); }
                }
            }
            finally { GUI.matrix = matrix; GUI.contentColor = color; GUI.depth = depth; }
        }
        private void OpenNumberPad()
        {
            if (pausedLogic == null) return;
            savedRow = selectedRow; savedScroll = scroll; numberDraft = addition;
            numberPadWindow = new Rect(Mathf.Max(8, (Screen.width / drawScale - 420) / 2), Mathf.Max(8, (Screen.height / drawScale - 590) / 2), 420, 590);
            numberPad = true; scroll = Vector2.zero; selectedRow = 0; navigation.Clear(); pointerTargets.Clear(); pointerClick.Cancel(); GUIFocusReset();
        }
        private void CloseNumberPad(bool accept)
        {
            if (accept) addition = numberDraft;
            numberPad = false; selectedRow = savedRow; scroll = savedScroll;
            navigation.Clear(); pointerTargets.Clear(); pointerClick.Cancel(); GUIFocusReset();
            if (accept) Notify("金额已填写，点击“执行金额调整”才会加减钱。" );
        }
        private void DrawNumberPad()
        {
            Section("输入赎罪之泪金额");
            GUILayout.Label(string.IsNullOrEmpty(numberDraft) ? "（空）" : numberDraft, headingStyle);
            scrollViewHeight = Mathf.Max(80, numberPadWindow.height - 320);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(scrollViewHeight));
            insideScroll = true;
            string[] cells = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "清空", "0", "退格" };
            for (int row = 0; row < 4; row++)
            {
                GUILayout.BeginHorizontal();
                for (int col = 0; col < 3; col++)
                {
                    string cell = cells[row * 3 + col];
                    Button(cell, () => {
                        if (cell == "清空") numberDraft = "";
                        else if (cell == "退格") { if (numberDraft.Length > 0) numberDraft = numberDraft.Substring(0, numberDraft.Length - 1); }
                        else if (numberDraft.Length < 12) numberDraft += cell;
                    });
                }
                GUILayout.EndHorizontal();
            }
            Button("切换正负号（±）", () => {
                if (numberDraft.StartsWith("-", StringComparison.Ordinal)) numberDraft = numberDraft.Substring(1);
                else if (numberDraft.StartsWith("+", StringComparison.Ordinal)) numberDraft = "-" + numberDraft.Substring(1);
                else if (numberDraft.Length < 12) numberDraft = "-" + numberDraft;
                else Notify("金额文本过长，请先退格再切换正负号。", true);
            });
            EndContentScroll();
            GUILayout.Label(MoneyPreview(numberDraft), mutedStyle);
            GUILayout.Label("完成仅填写，不直接调整余额。", mutedStyle);
            GUILayout.BeginHorizontal(); Button("取消", () => CloseNumberPad(false)); Button("完成", () => CloseNumberPad(true)); GUILayout.EndHorizontal();
            GUILayout.Label("方向键／摇杆选择 · 确认键输入 · 返回键取消", mutedStyle);
            GUI.DragWindow(new Rect(0, 0, window.width, 30));
        }
    }
}
