using System;
using Framework.Managers;
using UnityEngine;

namespace BlasphemousTrainer
{
    public sealed partial class Plugin
    {
        private const int PageCount = 3;
        private static readonly int[] BasicOrder = FeatureCatalog.BasicOrder;
        private static readonly int[] AdvancedOrder = FeatureCatalog.AdvancedOrder;
        private GUIStyle headingStyle, mutedStyle, rowStyle, switchOnStyle, switchOffStyle, tabStyle, dangerStyle;
        private Texture2D goldLine;
        private float scrollViewHeight;
        private Rect numberPadWindow;

        private void StyleReferenceLayout()
        {
            Color gold = new Color(.88f, .72f, .42f);
            headingStyle = new GUIStyle(panelSkin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            headingStyle.normal.textColor = gold;
            mutedStyle = new GUIStyle(panelSkin.label) { fontSize = 14 };
            mutedStyle.normal.textColor = new Color(.64f, .65f, .63f);
            rowStyle = new GUIStyle(panelSkin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            switchOffStyle = new GUIStyle(panelSkin.button) { fontSize = 14, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 28, 5, 5), border = new RectOffset(16, 16, 16, 16), wordWrap = false };
            switchOnStyle = new GUIStyle(switchOffStyle);
            switchOffStyle.padding = new RectOffset(32, 7, 5, 5);
            foreach (GUIStyle style in new[] { switchOffStyle, switchOnStyle }) {
                Texture2D texture = SwitchTexture(style == switchOnStyle);
                style.normal.background = style.hover.background = style.active.background = style.focused.background = texture;
            }
            tabStyle = new GUIStyle(panelSkin.button) { fontSize = 18, padding = new RectOffset(12, 12, 6, 6) };
            dangerStyle = new GUIStyle(panelSkin.button);
            dangerStyle.normal.textColor = dangerStyle.hover.textColor = dangerStyle.active.textColor = new Color(1, .43f, .37f);
            goldLine = Background(gold, gold);
            panelSkin.window.padding = new RectOffset(18, 18, 14, 14);
        }

        private Texture2D SwitchTexture(bool on)
        {
            var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            var pixels = new Color[64 * 32];
            for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++) {
                float dx = x < 16 ? x - 16 : x > 47 ? x - 47 : 0, dy = y - 15.5f;
                if (dx * dx + dy * dy > 15 * 15) continue;
                pixels[y * 64 + x] = on ? new Color(.18f, .48f, .27f) : new Color(.25f, .27f, .28f);
                // State text and thumb occupy opposite ends; off/on remain distinguishable.
                float thumbX = x - (on ? 47 : 16);
                if (thumbX * thumbX + dy * dy <= 10 * 10)
                    pixels[y * 64 + x] = on ? new Color(.8f, .97f, .84f) : new Color(.66f, .68f, .68f);
            }
            texture.SetPixels(pixels); texture.Apply(); skinTextures.Add(texture); return texture;
        }

        private void Rule()
        {
            GUILayout.Space(5);
            Rect line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(line, goldLine);
            GUILayout.Space(5);
        }

        private void DrawWindow(int id)
        {
            navigation.Clear();
            if (clearTextFocus) { GUI.FocusControl(null); clearTextFocus = false; }
            if (!editing && (Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp)) Event.current.Use();
            if (numberPad) { DrawNumberPad(); return; }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label("神之亵渎修改器", headingStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label("v" + ProjectInfo.Version, mutedStyle);
            GUILayout.Label(GameBindings.Ready ? "● 玩家已就绪" : "○ 等待玩家", mutedStyle);
            GUILayout.EndHorizontal();
            if (!fingerprintMatches) GUILayout.Label(fingerprintStatus, mutedStyle);
            GUILayout.BeginHorizontal();
            Button(page == 0 ? "◆ 功能" : "功能", () => SetPage(0), true, null, tabStyle, 120);
            Button(page == 1 ? "◆ 扩展功能" : "扩展功能", () => SetPage(1), true, null, tabStyle, 150);
            Button(page == 2 ? "◆ 设置" : "设置", () => SetPage(2), true, null, tabStyle, 120);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            Rule();
            // Leave room for wrapped device/status hints; only the middle content can scroll.
            scrollViewHeight = Mathf.Max(80, window.height - (fingerprintMatches ? 240 : 280));
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(scrollViewHeight));
            insideScroll = true;
            if (page == 2) DrawSettingsPage();
            else DrawFeaturePage(page == 0 ? BasicOrder : AdvancedOrder);
            EndContentScroll();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            Rule();
            GUILayout.BeginHorizontal();
            Button("全部关闭", DisableAllWithNotice, true, null, dangerStyle, 132);
            GUILayout.FlexibleSpace();
            GUILayout.Label(pausedLogic != null ? "↑↓ 选择  ·  ←→ 调整  ·  Enter 确认  ·  Esc 返回" : "未接管暂停，仅鼠标操作", mutedStyle);
            GUILayout.FlexibleSpace();
            Button("关闭面板", () => SetVisible(false), true, null, null, 132);
            GUILayout.EndHorizontal();
            GUILayout.Label(ControllerHint() + "  ·  " + keys[0].Value + " 打开／关闭", mutedStyle);
            editing = GUI.GetNameOfFocusedControl() == "tears-input";
            if (editing && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { GUI.FocusControl(null); editing = false; Event.current.Use(); }
            GUI.DragWindow(new Rect(0, 0, window.width - 65, 40));
        }

        private void EndContentScroll()
        {
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) {
                Rect viewport = ScreenBounds(GUILayoutUtility.GetLastRect());
                foreach (var target in pointerTargets) if (target.InScroll) target.Bounds = Intersection(target.Bounds, viewport);
            }
            insideScroll = false;
        }

        private void DrawFeaturePage(int[] order)
        {
            if (features == null) { GUILayout.Label(fingerprintStatus); return; }
            bool ready = GameBindings.Ready;
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("记住作弊开关", rowStyle);
            GUILayout.Label("进入存档后恢复持续功能，不重复加减钱", mutedStyle);
            GUILayout.EndVertical();
            Button(rememberCheats.Value ? "开启" : "关闭", ToggleMemory, ready, null,
                rememberCheats.Value ? switchOnStyle : switchOffStyle, 100);
            GUILayout.EndHorizontal();
            foreach (int i in order) {
                if (i == 1) { Rule(); Section("生存与资源"); }
                if (i == 3) { Rule(); Section("战斗"); }
                if (i == 5) { Rule(); Section("移动"); }
                if (i == 4) { Rule(); Section("赎罪之泪"); }
                if (i == FeatureController.MenuFeature) {
                    Rule(); Section("成就与计时");
                    GUILayout.Label("当前计时 " + PlaytimeSummary() + "  ·  已排除 " + ExcludedSummary(), mutedStyle);
                }
                DrawFeatureRow(i, ready);
                if (i == 2) DrawDefenseSection();
                if (i == 7) GUILayout.Label("支持：爬行光球、光柱、毒云；其他祷文与反弹不增强。", mutedStyle);
                if (i == 4) DrawTearsSection(ready);
                if (i == FeatureController.MenuFeature) GUILayout.Label("打开背包、地图或暂停菜单时，不累计游戏时长。", mutedStyle);
                if (i == FeatureController.FreezeFeature) GUILayout.Label("开启后任何情况下停止计时；关闭不补回已排除时间。", mutedStyle);
            }
        }

        private void DrawFeatureRow(int feature, bool ready)
        {
            int factor = FeatureToFactor(feature);
            string title = names[feature]; int suffix = title.IndexOf('（');
            if (suffix >= 0) title = title.Substring(0, suffix);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, rowStyle, GUILayout.ExpandWidth(true));
            if (factor >= 0) FactorRow(factor);
            bool active = features.Enabled[feature];
            string status = !features.Available[feature] ? "不可用" : active ? (ready ? "开启" : "等待") : "关闭";
            Button(status, () => ToggleWithNotice(feature), features.Available[feature] && (ready || active),
                factor >= 0 ? (Action<int>)(step => AdjustFactor(factor, step)) : null,
                active ? switchOnStyle : switchOffStyle, 100);
            GUILayout.EndHorizontal();
            if (!features.Available[feature]) GUILayout.Label(features.Errors[feature], mutedStyle);
        }

        private void DrawTearsSection(bool ready)
        {
            GUILayout.Label("当前余额  " + (GameBindings.Player != null ? GameBindings.Player.Stats.Purge.Current.ToString("0") : "—"), rowStyle);
            GUILayout.BeginHorizontal();
            GUI.enabled = pausedLogic != null;
            GUI.SetNextControlName("tears-input"); addition = GUILayout.TextField(addition, 12, GUILayout.Width(145));
            GUI.enabled = true;
            Button("数字键盘", OpenNumberPad, pausedLogic != null, null, null, 112);
            int amount; string action = int.TryParse(addition, out amount) && amount != 0 ?
                (amount > 0 ? "增加 " : "减少 ") + Math.Abs((long)amount).ToString("N0") : "执行金额调整";
            Button(action, () => AddTearsWithNotice(addition), ready);
            GUILayout.EndHorizontal();
            GUILayout.Label(MoneyPreview(addition), mutedStyle);
            GUILayout.BeginHorizontal();
            Button("+1,000", () => AddTearsWithNotice("1000"), ready);
            Button("+10,000", () => AddTearsWithNotice("10000"), ready);
            GUILayout.EndHorizontal();
            GUILayout.Label("金额调整会随游戏保存；收入倍率仅击杀奖励，不放大手动调整。", mutedStyle);
        }
    }
}
