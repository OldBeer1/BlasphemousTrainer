using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Configuration;
using Framework.Managers;
using Rewired;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlasphemousTrainer
{
    [BepInPlugin("local.blasphemous.trainer", "Blasphemous Trainer", ProjectInfo.Version)]
    [BepInProcess("Blasphemous.exe")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private const string BaselineHash = "F12C2F1D40AEF47287F07E910A16904DEEB11BB2D007331F30E50327C128A4CE";
        private const string InputBlocker = "local.blasphemous.trainer.panel";
        private readonly string[] names = FeatureCatalog.Names;
        private static readonly Color ActiveTextColor = new Color(.63f, .9f, .7f);
        private readonly ConfigEntry<KeyCode>[] keys = new ConfigEntry<KeyCode>[FeatureCatalog.Count + 2];
        private readonly ConfigEntry<float>[] factors = new ConfigEntry<float>[FeatureCatalog.FactorCount];
        private ConfigEntry<float> posX, posY, scale;
        private ConfigEntry<bool> skipIntro;
        private FeatureController features;
        private IntroSkip introSkip;
        private bool fingerprintMatches, visible, savedCursorVisible, editing;
        private CursorLockMode savedCursorLock;
        private GameModeManager observedModeManager;
        private Framework.Managers.InputManager blockedInput;
        private LogicManager pausedLogic;
        private Texture2D pointer;
        private Font font;
        private GUISkin panelSkin;
        private Rect window = new Rect(32, 48, 690, 680);
        private Vector2 scroll;
        private float drawScale = 1;
        private int page, selectedRow, recording = -1;
        private KeyCode waitRelease;
        private string addition = "1000", fingerprintStatus;
        private readonly PointerClick pointerClick = new PointerClick();
        private readonly List<MenuItem> pointerTargets = new List<MenuItem>();
        private Vector2 guiScreenOrigin;
        private bool clearTextFocus;
        private bool revealSelection, insideScroll, bindingsChecked;
        private Action pending;
        private readonly List<MenuItem> navigation = new List<MenuItem>();
        private sealed class MenuItem { internal Action Run; internal Action<int> Adjust; internal bool Enabled, InScroll; internal Rect Bounds; internal int Row; }

        private void Awake()
        {
            Config.SaveOnConfigSet = false;
            keys[0] = Config.Bind("Interface", "PanelKey", KeyCode.F1, "Panel key; None falls back to F1.");
            for (int i = 1; i <= FeatureCatalog.Count; i++) keys[i] = Config.Bind("Shortcuts", "Feature" + i, KeyCode.None, "Optional feature key; None disables binding.");
            keys[FeatureCatalog.Count + 1] = Config.Bind("Shortcuts", "DisableAll", KeyCode.None, "Optional all-off key.");
            skipIntro = Config.Bind("Interface", "SkipIntro", true, "Skip the startup logo video and landing key press; go straight to the main menu.");
            var used = new HashSet<KeyCode>();
            for (int i = 0; i < keys.Length; i++)
            {
                if (Reserved(keys[i].Value) || !Enum.IsDefined(typeof(KeyCode), keys[i].Value)) keys[i].Value = i == 0 ? KeyCode.F1 : KeyCode.None;
                if (i == 0 && keys[i].Value == KeyCode.None) keys[i].Value = KeyCode.F1;
                if (keys[i].Value != KeyCode.None && !used.Add(keys[i].Value)) keys[i].Value = KeyCode.None;
            }
            for (int i = 0; i < factors.Length; i++)
            {
                factors[i] = Config.Bind("Preferences", FeatureCatalog.ForFactor(i).Preference, FeatureCatalog.ForFactor(i).Default, "Multiplier preference; feature starts disabled.");
                factors[i].Value = TrainerRules.Clamp(factors[i].Value, 1, FeatureCatalog.ForFactor(i).Maximum);
            }
            posX = Config.Bind("Interface", "WindowX", 32f, "Window X in scaled screen coordinates.");
            posY = Config.Bind("Interface", "WindowY", 48f, "Window Y in scaled screen coordinates.");
            scale = Config.Bind("Interface", "Scale", 1f, "UI scale 0.75 to 1.5.");
            scale.Value = TrainerRules.Clamp(scale.Value, .75f, 1.5f);
            window.x = TrainerRules.Clamp(posX.Value, 0, 10000); window.y = TrainerRules.Clamp(posY.Value, 0, 10000);

            InitializeMemory();
            InitializeDefense();
            SavePreferences();
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll")))
                    fingerprintMatches = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == BaselineHash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(Path.Combine(Paths.ManagedPath, "Assembly-CSharp-firstpass.dll")))
                    fingerprintMatches &= BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == "992A8AB3C4C4AD9AE774FB5BD96FF08077A4A76835F0EA1C9A4C87E5E243C024";
                fingerprintStatus = fingerprintMatches ? "游戏版本指纹匹配 · 普通新游戏" : "不兼容：未知指纹，全部玩法功能已禁用";
            }
            catch (Exception e) { fingerprintMatches = false; fingerprintStatus = "版本读取失败，全部玩法功能已禁用"; Logger.LogWarning(e.Message); }
            InstallInputGate();
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
            if (fingerprintMatches) { features = new FeatureController(Logger); ApplyFactors(); features.Install(); }
            if (fingerprintMatches)
            {
                introSkip = new IntroSkip(Logger, skipIntro.Value);
                if (!introSkip.Install()) { introSkip.Dispose(); introSkip = null; }
            }
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Logger.LogInfo("Trainer loaded v" + ProjectInfo.Version + "; all features disabled.");
        }

        private static bool Reserved(KeyCode key)
        { return key >= KeyCode.Mouse0 || key == KeyCode.Escape || key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow || key == KeyCode.LeftAlt || key == KeyCode.RightAlt || key == KeyCode.LeftControl || key == KeyCode.RightControl || key == KeyCode.LeftShift || key == KeyCode.RightShift; }

        private void Update()
        {
            // Sampled ahead of every early return so the real-time delta always covers this frame.
            if (features != null) features.SamplePlaytime();
            if (Core.ready && !ReferenceEquals(observedModeManager, Core.GameModeManager))
            {
                if (observedModeManager != null) observedModeManager.OnEnterMenuMode -= OnEnterMenu;
                observedModeManager = Core.GameModeManager;
                if (observedModeManager != null) { observedModeManager.OnEnterMenuMode += OnEnterMenu; if (observedModeManager.IsCurrentMode(GameModeManager.GAME_MODES.MENU)) OnEnterMenu(); }
            }
            RestoreMemoryWhenReady();
            UpdateInputRelease();
            if (!Application.isFocused) { pending = null; pointerClick.Cancel(); return; }
            if (pending != null) { Action action = pending; pending = null; action(); }
            HandlePointer();
            if (HandleController()) return;
            if (waitRelease != KeyCode.None) { if (!Input.GetKey(waitRelease)) waitRelease = KeyCode.None; return; }
            if (recording >= 0)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { recording = -1; return; }
                foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
                    if (key != KeyCode.None && key < KeyCode.Mouse0 && Input.GetKeyDown(key)) { RecordKey(key); break; }
                return;
            }
            if (editing) return;
            if (Core.ready && Core.ControlRemapManager != null && !Core.ControlRemapManager.ListeningForInputDone) return;
            if (Input.GetKeyDown(keys[0].Value)) { SetVisible(!visible); return; }
            if (visible && Input.GetKeyDown(KeyCode.Escape)) { if (numberPad) CloseNumberPad(false); else SetVisible(false); return; }
            if (visible && pausedLogic != null && navigation.Count > 0)
            {
                selectedRow = Mathf.Clamp(selectedRow, 0, navigation.Count - 1);
                if (numberPad && Input.GetKeyDown(KeyCode.UpArrow)) MovePad(-1, 0);
                else if (numberPad && Input.GetKeyDown(KeyCode.DownArrow)) MovePad(1, 0);
                else if (numberPad && Input.GetKeyDown(KeyCode.LeftArrow)) MovePad(0, -1);
                else if (numberPad && Input.GetKeyDown(KeyCode.RightArrow)) MovePad(0, 1);
                else if (Input.GetKeyDown(KeyCode.UpArrow)) MoveSelection(-1);
                else if (Input.GetKeyDown(KeyCode.DownArrow)) MoveSelection(1);
                else
                {
                    var row = navigation[selectedRow];
                    if (row.Enabled)
                    {
                        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) pending = row.Run;
                        else if (row.Adjust != null && Input.GetKeyDown(KeyCode.LeftArrow)) pending = () => row.Adjust(-1);
                        else if (row.Adjust != null && Input.GetKeyDown(KeyCode.RightArrow)) pending = () => row.Adjust(1);
                    }
                }
            }
            // Outside the panel, only accept feature keys in active, unblocked gameplay.
            if (features != null && GameBindings.Ready && (visible ? pausedLogic != null : Core.Input != null && !Core.Input.InputBlocked && !Core.Logic.IsPaused))
                for (int i = 1; i < keys.Length; i++)
                    if (!numberPad && keys[i].Value != KeyCode.None && Input.GetKeyDown(keys[i].Value)) { if (i == FeatureCatalog.Count + 1) DisableAllWithNotice(); else ToggleWithNotice(i - 1); }
        }

        private void RecordKey(KeyCode key)
        {
            waitRelease = key;
            if (Reserved(key)) { Notify("此键用于导航或系统操作，请换一个键。", true); return; }
            for (int i = 0; i < keys.Length; i++)
                if (i != recording && keys[i].Value == key) { Notify("与修改器已有快捷键重复，请换一个键。", true); return; }
            string conflict = GameKeyConflict(key);
            if (conflict == "conflict") { Notify("此键已被游戏绑定，请换一个键。", true); return; }
            keys[recording].Value = key; recording = -1;
            Notify(conflict == "unknown" ? "按键已设定；游戏按键未自动核验，若冲突请更换。" : "按键已设定；当前游戏键盘映射未发现冲突。", conflict == "unknown");
            SavePreferences();
        }

        private string GameKeyConflict(KeyCode key)
        {
            try
            {
                if (!ReInput.isReady) return "unknown";
                var player = ReInput.players.GetPlayer(0);
                bool foundMap = false;
                foreach (ControllerMap map in player.controllers.maps.GetAllMaps(ControllerType.Keyboard))
                { foundMap = true; foreach (ActionElementMap entry in map.AllMaps) if (entry.keyCode == key) return "conflict"; }
                return foundMap ? "clear" : "unknown";
            }
            catch { return "unknown"; }
        }

        private void MoveSelection(int step)
        {
            GUIFocusReset();
            revealSelection = true;
            for (int i = 0; i < navigation.Count; i++)
            { selectedRow = (selectedRow + step + navigation.Count) % navigation.Count; if (navigation[selectedRow].Enabled) break; }
        }
        private void GUIFocusReset() { editing = false; clearTextFocus = true; }
        private void ApplyFactors()
        {
            if (features == null) return;
            features.Multiplier = factors[0].Value; features.TearsMultiplier = factors[1].Value;
            features.MoveMultiplier = factors[2].Value; features.JumpMultiplier = factors[3].Value; features.PrayerMultiplier = factors[4].Value;
            features.DefenseTier = defenseTier.Value / 100f;
        }
        // Explicit feature/multiplier mapping. Features 3-7 own the five multipliers; every
        // other feature (including the menu time guard) has no multiplier row.
        private static int FeatureToFactor(int feature) { return FeatureCatalog.All[feature].Factor; }
        private static int FactorToFeature(int factor) { return FeatureCatalog.FactorToFeature(factor); }
        private string FactorLabel(int factor)
        {
            for (int i = 0; i < names.Length; i++) if (FeatureToFactor(i) == factor) return names[i];
            return "倍率";
        }
        private string ExcludedSummary()
        {
            if (features == null) return "—";
            float total = features.ExcludedTime;
            if (float.IsNaN(total) || float.IsInfinity(total) || total < 0f) return "—";
            return ((int)(total / 60f)).ToString() + " 分 " + ((int)(total % 60f)).ToString() + " 秒";
        }
        private string PlaytimeSummary()
        {
            try
            {
                if (!Core.ready || Core.Persistence == null) return "等待存档";
                float seconds = Core.Persistence.GetCurrentTimePlayed();
                if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) return "暂不可用";
                return Math.Floor(seconds / 3600d).ToString("0") + ":" +
                    ((int)(seconds / 60f % 60f)).ToString("00") + ":" + ((int)(seconds % 60f)).ToString("00");
            }
            catch { return "暂不可用"; }
        }
        private void AdjustFactor(int index, int step)
        { float before = factors[index].Value; factors[index].Value = (float)Math.Round(TrainerRules.Clamp(before + step * FeatureCatalog.ForFactor(index).Step, 1, FeatureCatalog.ForFactor(index).Maximum), 1); ApplyFactors(); if (before != factors[index].Value) { Notify(FactorLabel(index) + "已设为 " + factors[index].Value.ToString("0.0") + "×" + (features != null && features.Enabled[FactorToFeature(index)] ? "" : "，功能仍关闭")); SavePreferences(); } }
        private void SetPage(int target) { page = target; scroll = Vector2.zero; selectedRow = 0; navigation.Clear(); pointerTargets.Clear(); pointerClick.Cancel(); GUIFocusReset(); }
        private void OnEnterMenu() { memory.EnterMenu(); memoryWarning = false; SetVisible(false); ReleaseInput(); if (features != null) features.DisableAll(); }
        private void OnActiveSceneChanged(Scene previous, Scene current) { controller.InvalidateMaps(); SetVisible(false); ReleaseInput(); Logger.LogInfo("Scene changed; feature selections retained: " + current.name); }
        private void OnApplicationFocus(bool focused) { if (!focused) SetVisible(false); }
        private void LateUpdate() { if (visible && Application.isFocused) { ApplyPanelCursor(); } }

        private void SetVisible(bool value)
        {
            if (visible == value) return;
            if (value && releasingInput) { Notify("请先松开按键和摇杆，再打开面板。", true); return; }
            pending = null; editing = false; recording = -1; waitRelease = KeyCode.None; numberPad = false; openPress.Reset();
            pointerClick.Cancel(); pointerTargets.Clear();
            if (value)
            {
                savedCursorVisible = Cursor.visible; savedCursorLock = Cursor.lockState;
                ApplyPanelCursor();
                if (CanOwnPanel())
                { blockedInput = Core.Input; blockedInput.SetBlocker(InputBlocker, true); pausedLogic = Core.Logic; pausedLogic.PauseGame(); }
                controllerArmed = false;
                if (!bindingsChecked) { CheckBindings(); bindingsChecked = true; }
            }
            else
            {
                if (blockedInput != null) { releasingInput = true; neutralSince = -1; }
                if (Cursor.lockState == CursorLockMode.None) Cursor.lockState = savedCursorLock;
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); Cursor.visible = savedCursorVisible;
                posX.Value = window.x; posY.Value = window.y; SavePreferences();
            }
            visible = value;
        }

        private void OnGUI()
        {
            if (!Application.isFocused) return;
            if (panelSkin == null)
            {
                panelSkin = Instantiate(GUI.skin); if (font != null) panelSkin.font = font;
                panelSkin.label.fontSize = 18; panelSkin.label.wordWrap = true;
                panelSkin.button.fontSize = 18; panelSkin.window.fontSize = 20; panelSkin.textField.fontSize = 18; panelSkin.toggle.fontSize = 18;
                StylePanel();
            }
            var previousSkin = GUI.skin; var previousMatrix = GUI.matrix;
            try
            {
                guiScreenOrigin = GUIUtility.GUIToScreenPoint(Vector2.zero);
                if (Event.current.type == EventType.Repaint) pointerTargets.Clear();
                GUI.skin = panelSkin;
                drawScale = Mathf.Min(scale.Value, Mathf.Max(.3f, Screen.width / 720f));
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(drawScale, drawScale, 1));
                window.width = Mathf.Min(690, Screen.width / drawScale - 16); window.height = Mathf.Min(700, Screen.height / drawScale - 16);
                window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width / drawScale - window.width));
                window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height / drawScale - window.height));
                if (visible) window = GUILayout.Window(174361, window, DrawWindow, "神之亵渎 · 修改器 v" + ProjectInfo.Version);
                GUI.matrix = previousMatrix; DrawNotifications();
            }
            finally { GUI.skin = previousSkin; GUI.matrix = previousMatrix; }
        }

        private void DrawWindow(int id)
        {
            navigation.Clear();
            if (clearTextFocus) { GUI.FocusControl(null); clearTextFocus = false; }
            if (!editing && (Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp)) Event.current.Use();
            if (numberPad) { DrawNumberPad(); return; }
            GUILayout.Space(8); GUILayout.Label(fingerprintStatus);
            GUILayout.BeginHorizontal();
            Button(page == 0 ? "◆ 基础功能" : "基础功能", () => SetPage(0));
            Button(page == 1 ? "◆ 进阶功能" : "进阶功能", () => SetPage(1));
            Button(page == 2 ? "◆ 设置" : "设置", () => SetPage(2));
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(80, window.height - 320)));
            insideScroll = true;
            if (page == 2) DrawSettingsPage();
            else DrawFeaturePage(page == 0 ? BasicOrder : AdvancedOrder);
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint)
            {
                Rect viewport = ScreenBounds(GUILayoutUtility.GetLastRect());
                foreach (var target in pointerTargets)
                    if (target.InScroll) target.Bounds = Intersection(target.Bounds, viewport);
            }
            insideScroll = false;
            GUILayout.Label(pausedLogic != null ? "↑↓ 选择 · ←→ 调整 · Enter 确认 · Esc 返回" : "未接管暂停，仅鼠标操作。关卡内关闭游戏菜单后重新打开面板。");
            GUILayout.Label(ControllerHint());
            GUILayout.BeginHorizontal();
            Button("全部关闭", DisableAllWithNotice);
            Button("关闭面板（" + keys[0].Value + "）", () => SetVisible(false));
            GUILayout.EndHorizontal();
            editing = GUI.GetNameOfFocusedControl() == "tears-input";
            if (editing && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) { GUI.FocusControl(null); editing = false; Event.current.Use(); }
            GUI.DragWindow(new Rect(0, 0, window.width, 30));
        }

        // Two feature blocks so the panel never becomes one long scroll, split so the two pages
        // stay comparable in length: basic is survival and mobility, advanced is damage, economy
        // and timing. Both are plain lists of feature indices, so moving a switch between blocks
        // is a one-line change and the rendering logic is untouched.
        private const int PageCount = 3;
        private static readonly int[] BasicOrder = FeatureCatalog.BasicOrder;
        private static readonly int[] AdvancedOrder = FeatureCatalog.AdvancedOrder;

        private void DrawFeaturePage(int[] order)
        {
            if (features == null) return;
            bool ready = GameBindings.Ready;
            var memoryColor = GUI.contentColor;
            if (rememberCheats.Value) GUI.contentColor = ActiveTextColor;
            Button("记住作弊开关：" + (rememberCheats.Value ? "已开启" : "已关闭"), ToggleMemory, ready);
            GUI.contentColor = memoryColor;
            GUILayout.Label("应用于本机所有普通新游戏存档；只记持续功能，不重复加减钱。");
            GUILayout.Label(ready ? "玩家已就绪；换图保留开关，进档恢复由作弊记忆决定。" : "玩家未就绪：已保留开关，效果暂不执行。");
            foreach (int i in order)
            {
                if (i == 1) Section("生存与资源");
                if (i == 3) Section("战斗");
                if (i == 5) Section("移动");
                if (i == 4) Section("赎罪之泪");
                if (i == FeatureController.MenuFeature)
                {
                    Section("成就与计时");
                    GUILayout.Label("当前游戏计时：" + PlaytimeSummary() + "（时:分:秒）");
                    GUILayout.Label("已排除 " + ExcludedSummary() + "；这部分不计入游戏内时长，AC44「Bronze Medal」按该时长判定。");
                }
                int feature = i;
                int factor = FeatureToFactor(i);
                string status = !features.Available[i] ? "不兼容" : features.Enabled[i] ? (ready ? "已启用" : "等待玩家") : "已关闭";
                var oldColor = GUI.contentColor;
                if (features.Enabled[i]) GUI.contentColor = ActiveTextColor;
                Button(names[i] + "：" + status, () => ToggleWithNotice(feature), features.Available[i] && (ready || features.Enabled[i]), factor >= 0 ? (Action<int>)(step => AdjustFactor(factor, step)) : null);
                GUI.contentColor = oldColor;
                if (!features.Available[i]) GUILayout.Label(features.Errors[i]);
                if (factor >= 0) FactorRow(factor);
                if (i == 2) DrawDefenseSection();
                if (i == 7) GUILayout.Label("祷文覆盖：爬行光球、垂直光柱、毒云。其他祷文与反弹不增强。");
                if (i == 4) DrawTearsSection(ready);
                if (i == FeatureController.MenuFeature) GUILayout.Label("背包、地图与暂停菜单打开期间不计入时长。");
                if (i == FeatureController.FreezeFeature) GUILayout.Label("开启后计时完全停住，直到手动关闭；已经排除的时间不会因为关闭而还回去。");
            }
        }

        private void DrawTearsSection(bool ready)
        {
            GUILayout.Label("收入倍率仅击杀奖励，不增强物品／任务收入。");
            GUILayout.Label("赎罪之泪：" + (GameBindings.Player != null ? GameBindings.Player.Stats.Purge.Current.ToString("0") : "—"));
            GUILayout.Label("正数加钱，负数减钱；会随游戏保存，全部关闭不会撤销。");
            GUILayout.BeginHorizontal();
            Button("+1,000", () => AddTearsWithNotice("1000"), ready);
            Button("+10,000", () => AddTearsWithNotice("10000"), ready);
            GUILayout.EndHorizontal();
            GUI.enabled = pausedLogic != null;
            GUI.SetNextControlName("tears-input"); addition = GUILayout.TextField(addition, 12);
            GUI.enabled = true;
            Button("数字键盘：编辑金额", OpenNumberPad, pausedLogic != null);
            GUILayout.Label(MoneyPreview(addition));
            Button("执行金额调整", () => AddTearsWithNotice(addition), ready);
        }

        private void DrawSettingsPage()
        {
            DrawControllerSettings();
            GUILayout.Label("关卡内面板暂停后可改键：点击后按键盘新键，Esc 或手柄返回键取消。方向键、Enter、Esc、鼠标键和修饰键保留。其他 MOD／系统热键未核验。");
            for (int i = 0; i < keys.Length; i++)
            {
                int keyIndex = i;
                string title = i == 0 ? "面板" : i == FeatureCatalog.Count + 1 ? "全部关闭" : names[i - 1];
                GUILayout.BeginHorizontal();
                Button(title + "：" + keys[i].Value + (recording == i ? "（请按键）" : "（改键）"), () => { recording = keyIndex; Notify("等待新按键，Esc 取消。"); }, pausedLogic != null);
                if (i > 0) Button("清除", () => { keys[keyIndex].Value = KeyCode.None; SavePreferences(); });
                GUILayout.EndHorizontal();
            }
            Button("检查当前快捷键与游戏冲突", CheckBindings);
            Button("恢复默认按键（面板 F1，其余不绑定）", () => { for (int i = 0; i < keys.Length; i++) keys[i].Value = i == 0 ? KeyCode.F1 : KeyCode.None; SavePreferences(); });
            Button("界面缩放 " + scale.Value.ToString("0.00") + "×（← / →）", () => {}, true, step => { scale.Value = TrainerRules.Clamp(scale.Value + step * .05f, .75f, 1.5f); SavePreferences(); });
            GUILayout.BeginHorizontal();
            Button("缩小", () => { scale.Value = TrainerRules.Clamp(scale.Value - .05f, .75f, 1.5f); SavePreferences(); });
            Button("放大", () => { scale.Value = TrainerRules.Clamp(scale.Value + .05f, .75f, 1.5f); SavePreferences(); });
            Button("重置位置与缩放", () => { window.x = 16; window.y = 16; scale.Value = 1; SavePreferences(); });
            GUILayout.EndHorizontal();
        }

        private void CheckBindings()
        {
            string resultMessage = "当前游戏键盘映射未发现冲突；其他软件热键未核验。";
            bool warning = false;
            foreach (var key in keys)
            {
                if (key.Value == KeyCode.None) continue;
                string result = GameKeyConflict(key.Value);
                if (result == "unknown") { resultMessage = "游戏按键未自动核验，请在关卡内重试。"; warning = true; break; }
                if (result == "conflict") { resultMessage = "发现游戏按键冲突：" + key.Value + "，请更换。"; warning = true; break; }
            }
            Notify(resultMessage, warning);
        }
        private void FactorRow(int index)
        {
            GUILayout.BeginHorizontal();
            Button("倍率 " + factors[index].Value.ToString("0.0") + "×（← / →）", () => {}, true, step => AdjustFactor(index, step));
            Button("−", () => AdjustFactor(index, -1)); Button("+", () => AdjustFactor(index, 1));
            GUILayout.EndHorizontal();
        }
        private void Button(string label, Action action, bool enabled = true, Action<int> adjust = null)
        {
            int row = navigation.Count; navigation.Add(new MenuItem { Run = action, Adjust = adjust, Enabled = enabled });
            var color = GUI.backgroundColor; bool wasEnabled = GUI.enabled;
            GUI.enabled = enabled; if (selectedRow == row) GUI.backgroundColor = new Color(1f, .82f, .48f);
            // Deliberately ignore IMGUI's activation result; HandlePointer and keyboard
            // navigation are the only action producers, preventing double execution.
            GUILayout.Button((selectedRow == row ? "▶ " : "") + label, GUILayout.MinHeight(32));
            if (Event.current.type == EventType.Repaint)
                pointerTargets.Add(new MenuItem { Run = action, Enabled = enabled, Row = row, InScroll = insideScroll, Bounds = ScreenBounds(GUILayoutUtility.GetLastRect()) });
            if (insideScroll && revealSelection && selectedRow == row && Event.current.type == EventType.Repaint)
            {
                Rect item = GUILayoutUtility.GetLastRect();
                float viewHeight = Mathf.Max(80, window.height - 320) - 20;
                if (item.y < scroll.y) scroll.y = item.y;
                else if (item.yMax > scroll.y + viewHeight) scroll.y = item.yMax - viewHeight;
                revealSelection = false;
            }
            GUI.enabled = wasEnabled; GUI.backgroundColor = color;
        }

        private Rect ScreenBounds(Rect local)
        {
            Vector2 min = GUIUtility.GUIToScreenPoint(new Vector2(local.xMin, local.yMin)) - guiScreenOrigin;
            Vector2 max = GUIUtility.GUIToScreenPoint(new Vector2(local.xMax, local.yMax)) - guiScreenOrigin;
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }
        private static Rect Intersection(Rect a, Rect b)
        {
            float x = Mathf.Max(a.xMin, b.xMin), y = Mathf.Max(a.yMin, b.yMin);
            return new Rect(x, y, Mathf.Max(0, Mathf.Min(a.xMax, b.xMax) - x), Mathf.Max(0, Mathf.Min(a.yMax, b.yMax) - y));
        }
        private void HandlePointer()
        {
            if (!visible) { pointerClick.Cancel(); return; }
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            int hit = -1;
            for (int i = 0; i < pointerTargets.Count; i++)
                if (pointerTargets[i].Enabled && pointerTargets[i].Bounds.Contains(mouse)) { hit = i; break; }
            int clicked = pointerClick.Step(Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0), Input.GetMouseButton(0), hit, mouse.x, mouse.y);
            if (clicked >= 0 && clicked < pointerTargets.Count)
            {
                selectedRow = pointerTargets[clicked].Row;
                pending = pointerTargets[clicked].Run;
                editing = false; clearTextFocus = true;
            }
        }
        private void ApplyPanelCursor()
        {
            if (pointer == null)
            {
                // Cached white arrow with a black outline; independent of the game's Cursor.visible writes.
                pointer = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                pointer.filterMode = FilterMode.Point;
                Color[] pixels = new Color[32 * 32];
                for (int y = 0; y < 26; y++)
                    for (int x = 0; x <= y / 2 && x < 17; x++)
                    {
                        if (y > 18 && x < 5) continue;
                        pixels[(31 - y) * 32 + x] = x == 0 || x == y / 2 || y == 25 || (y > 18 && x == 5)
                            ? Color.black : Color.white;
                    }
                pointer.SetPixels(pixels);
                pointer.Apply();
            }
            // Unity's cursor layer is above IMGUI windows, unlike an ordinary GUI texture.
            Cursor.lockState = CursorLockMode.None;
            Cursor.SetCursor(pointer, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
        }
        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            if (observedModeManager != null) observedModeManager.OnEnterMenuMode -= OnEnterMenu;
            SetVisible(false); if (features != null) features.Dispose();
            if (introSkip != null) introSkip.Dispose();
            ReleaseInput(); if (inputGate != null) inputGate.Dispose();
            foreach (var texture in skinTextures) Destroy(texture);
            if (panelSkin != null) Destroy(panelSkin); if (font != null) Destroy(font); if (pointer != null) Destroy(pointer);
        }
    }
}
