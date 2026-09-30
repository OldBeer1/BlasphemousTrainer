using System;

using Framework.Managers;
using UnityEngine;

namespace BlasphemousTrainer
{
    public sealed partial class Plugin
    {
        private readonly PanelController controller = new PanelController();
        private readonly PanelRepeat horizontalRepeat = new PanelRepeat(), verticalRepeat = new PanelRepeat();
        private readonly PanelPress openPress = new PanelPress();
        private PanelInputGate inputGate;
        private bool gateReady, controllerArmed, releasingInput;
        private bool lastConfirm, lastBack, lastLeft, lastRight;
        private float neutralSince = -1;
        private int openButton = -1;
        private bool wasRemapping;
        private string openStatus = "等待识别右摇杆按下（R3）";
        private void InstallInputGate()
        {
            if (!fingerprintMatches) return;
            inputGate = new PanelInputGate();
            try
            {
                PanelInputGate.OwnsInput = () => blockedInput != null || OpeningButtonHeld();
                inputGate.Install(); gateReady = true;
                Logger.LogInfo("Panel Rewired input isolation installed.");
            }
            catch (Exception e)
            {
                inputGate.Dispose(); inputGate = null; gateReady = false;
                Logger.LogWarning("Panel controller disabled: " + e.GetBaseException().Message);
            }
        }
        private bool OpeningButtonHeld()
        {
            // Consume R3 before any Player action reads, even if game Update runs first.
            if (!gateReady || !Application.isFocused || visible || openButton < 0) return false;
            try { return CanOwnPanel() && controller.Held(openButton); }
            catch { return false; }
        }
        private bool CanOwnPanel()
        {
            return !releasingInput && fingerprintMatches && GameBindings.Ready && Core.Input != null && !Core.Input.InputBlocked && Core.Logic != null && !Core.Logic.IsPaused && Core.Logic.CurrentLevelConfig != null;
        }
        private void ReleaseInput()
        {
            if (blockedInput != null)
            {
                blockedInput.SetBlocker(InputBlocker, false);
                if (pausedLogic != null && ReferenceEquals(pausedLogic, Core.Logic) && !blockedInput.InputBlocked && pausedLogic.IsPaused) pausedLogic.ResumeGame();
            }
            blockedInput = null; pausedLogic = null; releasingInput = false; neutralSince = -1;
        }
        private void UpdateInputRelease()
        {
            if (!releasingInput) return;
            if (!Application.isFocused || !GameBindings.Ready || !ReferenceEquals(pausedLogic, Core.Logic)) { ReleaseInput(); return; }
            bool neutral;
            try { controller.Refresh(); neutral = controller.Neutral() && !Input.anyKey; }
            catch { neutral = !Input.anyKey; }
            if (!neutral) { neutralSince = -1; return; }
            if (neutralSince < 0) neutralSince = Time.unscaledTime;
            // Keep consuming the button-up frame and a short quiet interval before resuming.
            if (Time.unscaledTime - neutralSince >= .12f) ReleaseInput();
        }
        private void RefreshOpenButton()
        {
            openButton = controller.FindStick(false);
            openStatus = openButton >= 0 ? "单按右摇杆（R3）打开面板" : "未识别右摇杆按下，请用 " + keys[0].Value + " 打开面板";
        }
        private bool HandleController()
        {
            if (!gateReady || !Application.isFocused) { openPress.Reset(); return false; }
            try
            {
                bool remapping = Core.ready && Core.ControlRemapManager != null && !Core.ControlRemapManager.ListeningForInputDone;
                if (remapping != wasRemapping) controller.InvalidateMaps();
                wasRemapping = remapping;
                bool changed = controller.Refresh();
                if (changed)
                {
                    controllerArmed = false; pending = null;
                    openPress.Reset(); horizontalRepeat.Reset(); verticalRepeat.Reset();
                    if (visible) Notify(controller.Connected ? "已连接手柄，请先松开按键。" : "手柄已断开，可继续使用键盘和鼠标。", !controller.Connected);
                }
                if (changed || openButton < 0) RefreshOpenButton();
                if (remapping) { pending = null; controllerArmed = false; openPress.Reset(); return false; }
                if (changed && controller.Connected) Logger.LogInfo("Controller: " + controller.Status + "; submit=" + controller.Label(50) + "; back=" + controller.Label(51) + "; panel=" + openStatus);
                if (!controller.Connected) return false;
                if (releasingInput) return true;
                if (!visible)
                {
                    bool pressed = openPress.Step(controller.Held(openButton));
                    if (CanOwnPanel() && openButton >= 0 && pressed) { SetVisible(true); return true; }
                    if (!CanOwnPanel() || openButton < 0) openPress.Reset();
                    return false;
                }
                if (pausedLogic == null) return false;
                if (!controllerArmed)
                {
                    if (controller.Neutral()) { controllerArmed = true; lastConfirm = lastBack = lastLeft = lastRight = false; horizontalRepeat.Reset(); verticalRepeat.Reset(); }
                    return false;
                }
                bool confirm = controller.Read(50) > .5f, back = controller.Read(51) > .5f;
                bool left = controller.Read(28) > .5f, right = controller.Read(29) > .5f;
                bool pressConfirm = confirm && !lastConfirm, pressBack = back && !lastBack;
                bool pressLeft = left && !lastLeft, pressRight = right && !lastRight;
                lastConfirm = confirm; lastBack = back; lastLeft = left; lastRight = right;
                if (pressBack)
                {
                    if (recording >= 0) recording = -1;
                    else if (editing) GUIFocusReset();
                    else if (numberPad) CloseNumberPad(false);
                    else SetVisible(false);
                    return true;
                }
                if (recording >= 0) return false;
                if (editing && pressConfirm) { OpenNumberPad(); return true; }
                if (editing) return false;
                if (!numberPad && (pressLeft || pressRight)) { SetPage(pressRight ? (page + 1) % PageCount : (page + PageCount - 1) % PageCount); return true; }
                float horizontal = controller.Read(48), vertical = controller.Read(49);
                if (Mathf.Abs(horizontal) < .3f) horizontal = controller.Read(0);
                if (Mathf.Abs(vertical) < .3f) vertical = controller.Read(4);
                int dy = verticalRepeat.Step(vertical, Time.unscaledTime), dx = horizontalRepeat.Step(horizontal, Time.unscaledTime);
                if (navigation.Count == 0) return false;
                selectedRow = Mathf.Clamp(selectedRow, 0, navigation.Count - 1);
                if (numberPad)
                {
                    if (dy != 0) { MovePad(-dy, 0); return true; }
                    if (dx != 0) { MovePad(0, dx); return true; }
                }
                else if (dy != 0) { MoveSelection(-dy); return true; }
                var row = navigation[selectedRow];
                if (row.Enabled)
                {
                    if (pressConfirm) { pending = row.Run; return true; }
                    if (dx != 0 && row.Adjust != null) { int step = dx; pending = () => row.Adjust(step); return true; }
                }
            }
            catch (Exception e)
            {
                pending = null; controllerArmed = false; openPress.Reset();
                gateReady = false; Notify("手柄读取失败，已停用手柄操作；键鼠仍可使用。", true);
                Logger.LogWarning("Controller read failed: " + e.GetBaseException().Message);
            }
            return false;
        }
        private void MovePad(int vertical, int horizontal)
        {
            selectedRow = PanelGrid.Move(selectedRow, vertical, horizontal);
        }
        private string ControllerHint()
        {
            if (!gateReady) return "手柄输入隔离未就绪；请使用键盘／鼠标。";
            if (!controller.Connected) return "手柄：未连接 · 键盘／鼠标可用";
            return "手柄确认 " + controller.Label(50) + " · 返回 " + controller.Label(51) + " · 左右／翻页键循环切页";
        }
        private void DrawControllerSettings()
        {
            Section("手柄"); GUILayout.Label(controller.Status); GUILayout.Label(openStatus);
            if (!controller.HasAction(50) || !controller.HasAction(51)) GUILayout.Label("确认／返回映射缺失，先在游戏控制设置中检查手柄。");
            GUILayout.Label("按下右摇杆即可打开，无需组合或长按；返回键关闭。旧组合键设置不再使用。");
            Button("刷新手柄映射", () => { controller.InvalidateMaps(); controller.Refresh(); RefreshOpenButton(); controllerArmed = false; pending = null; Notify("已刷新手柄映射，请先松开按键。"); });
            Section("键盘与显示");
        }
    }
}
