using System;
using System.Globalization;
using ElectricalSim.Platform;
using ElectricalSim.Spice.Core;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// Demo 模拟电路页面中的正式 SPICE 宿主。它只消费场景序列化的局部 Root 绑定，
    /// 初始化独立工作区一次；不创建 Canvas、EventSystem、Camera 或页面外壳。
    /// </summary>
    public sealed class SpiceWorkspaceDemoHost : MonoBehaviour
    {
        private const string FileDialogFilter = "SPICE 电路文件 (*.spicejson)|*.spicejson";
        private const string FileDialogExtension = "spicejson";
        private const string SaveDialogTitle = "保存 SPICE 图纸";
        private const string ImportDialogTitle = "导入 SPICE 图纸";

        [SerializeField] private SpiceWorkspaceViewBindings viewBindings;
        [SerializeField] private SpiceWorkspaceController workspaceController;

        private bool initialized;
        private SimulationModeController modeController;
        private SpiceComponentParameterDialog parameterDialog;
        // C2 文件工作流：替换确认弹窗 + 待导入候选路径
        private SpiceDrawingReplaceConfirmationDialog replaceConfirmationDialog;
        private string pendingImportPath;

        public SpiceWorkspaceController Controller => workspaceController;
        public bool IsInitialized => initialized;

        /// <summary>仅供场景装配器写入已创建的正式引用；不会自动创建或查找 Root。</summary>
        public void Configure(SpiceWorkspaceViewBindings bindings, SpiceWorkspaceController controller)
        {
            if (initialized)
            {
                throw new InvalidOperationException("Spice Demo host cannot be configured after initialization.");
            }

            viewBindings = bindings;
            workspaceController = controller;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            modeController = GetComponent<SimulationModeController>();
            if (modeController == null || modeController.PopupLayer == null)
            {
                throw new InvalidOperationException("Spice Demo host requires the configured simulation mode popup layer.");
            }

            var dialogRoot = new GameObject("SpiceParameterDialog", typeof(RectTransform), typeof(SpiceComponentParameterDialog));
            dialogRoot.transform.SetParent(modeController.PopupLayer, false);
            parameterDialog = dialogRoot.GetComponent<SpiceComponentParameterDialog>();
            parameterDialog.Initialize(modeController.PopupLayer, workspaceController);
            workspaceController.ParameterDialogRequested += parameterDialog.Open;
            workspaceController.ConfigureModalInputGuard(() => parameterDialog.IsOpen || (replaceConfirmationDialog != null && replaceConfirmationDialog.IsOpen));

            // C2：创建替换确认弹窗（一次性，复用）并订阅文件操作事件。
            InitializeFileWorkflow(modeController.PopupLayer);

            modeController.ModeChanged += HandleModeChanged;
        }

        /// <summary>
        /// C2 文件工作流初始化：创建替换确认弹窗并订阅 Controller 的文件操作事件。
        /// 生产路径由 Start() 调用；T3 测试通过 InitializeFileWorkflowForTesting 直接调用，
        /// 无需 SimulationModeController，便于验证 Host 协调逻辑。
        /// </summary>
        private void InitializeFileWorkflow(RectTransform popupLayer)
        {
            if (replaceConfirmationDialog != null) return;
            var confirmRoot = new GameObject("SpiceReplaceConfirmDialog", typeof(RectTransform), typeof(SpiceDrawingReplaceConfirmationDialog));
            confirmRoot.transform.SetParent(popupLayer, false);
            replaceConfirmationDialog = confirmRoot.GetComponent<SpiceDrawingReplaceConfirmationDialog>();
            replaceConfirmationDialog.Initialize(popupLayer);
            replaceConfirmationDialog.ConfirmRequested += HandleReplaceConfirmationConfirmed;
            replaceConfirmationDialog.Cancelled += HandleReplaceConfirmationCancelled;

            workspaceController.SaveRequested += HandleSaveRequested;
            workspaceController.SaveAsRequested += HandleSaveAsRequested;
            workspaceController.ImportRequested += HandleImportRequested;
        }

        /// <summary>仅供 T3 测试在不依赖 SimulationModeController 的情况下初始化 C2 文件工作流。</summary>
        internal void InitializeFileWorkflowForTesting(RectTransform popupLayer)
        {
            InitializeFileWorkflow(popupLayer);
        }

        private void OnDestroy()
        {
            if (workspaceController != null)
            {
                if (parameterDialog != null)
                {
                    workspaceController.ParameterDialogRequested -= parameterDialog.Open;
                }
                workspaceController.SaveRequested -= HandleSaveRequested;
                workspaceController.SaveAsRequested -= HandleSaveAsRequested;
                workspaceController.ImportRequested -= HandleImportRequested;
            }

            if (replaceConfirmationDialog != null)
            {
                replaceConfirmationDialog.ConfirmRequested -= HandleReplaceConfirmationConfirmed;
                replaceConfirmationDialog.Cancelled -= HandleReplaceConfirmationCancelled;
                replaceConfirmationDialog.Dispose();
                // Dispose 已销毁弹窗根对象及其 Blocker/Panel，清空引用避免悬空访问。
                replaceConfirmationDialog = null;
            }

            if (modeController != null)
            {
                modeController.ModeChanged -= HandleModeChanged;
            }

            parameterDialog?.Dispose();
            pendingImportPath = null;
        }

        private void HandleModeChanged(SimulationWorkspaceMode mode)
        {
            if (mode != SimulationWorkspaceMode.SpiceDc)
            {
                parameterDialog?.CloseWithoutApply();
                // 离开 SPICE 时关闭替换确认，不执行导入；丢弃待导入候选路径。
                replaceConfirmationDialog?.CloseWithoutApply();
                pendingImportPath = null;
            }
        }

        // ============ C2 文件工作流：保存 / 另存为 / 导入 ============
        // Host 负责 Windows 文件对话框、替换确认和用户反馈。
        // 所有文件读写均复用 C1 的 TrySaveWorkspaceToPath / TrySaveCurrentWorkspace / TryImportWorkspaceFromPath，
        // 不重写或绕过 C1/Batch B 的原子写入、UTF-8 严格读取、事务导入与路径会话状态。

        private void HandleSaveRequested()
        {
            // 已绑定路径：直接保存，不打开对话框。
            if (!workspaceController.TrySaveCurrentWorkspace(out var error))
            {
                workspaceController.ShowFileOperationStatus("保存失败，无法写入所选位置。");
                return;
            }
            workspaceController.ShowFileOperationStatus("已保存：" + ExtractFileName(workspaceController.CurrentSpiceFilePath));
        }

        private void HandleSaveAsRequested()
        {
            if (!EnsureDefaultDirectoryForFileWorkflow())
            {
                workspaceController.ShowFileOperationStatus("保存失败，无法创建默认目录。");
                return;
            }
            var defaultFileName = BuildDefaultSaveFileName();
            // C2.4：使用带 out error 的重载，区分失败与取消。
            // 失败：dialogError 非空 → 显示简洁中文提示（技术详情已由 WindowsFileDialog 写入日志）。
            // 取消：dialogError 为空且 path 为空 → 不显示错误、不写文件、不改路径。
            var path = WindowsFileDialog.SaveFile(SaveDialogTitle, FileDialogFilter, FileDialogExtension, SpiceDrawingFileService.DefaultDirectory, defaultFileName, out var dialogError);
            if (!string.IsNullOrEmpty(dialogError))
            {
                workspaceController.ShowFileOperationStatus(dialogError);
                return;
            }
            if (string.IsNullOrEmpty(path))
            {
                // 用户取消：不写文件、不改路径、不显示错误、不覆盖已有有效状态提示。
                return;
            }
            if (!workspaceController.TrySaveWorkspaceToPath(path, out var error))
            {
                workspaceController.ShowFileOperationStatus("保存失败，无法写入所选位置。");
                return;
            }
            workspaceController.ShowFileOperationStatus("已保存：" + ExtractFileName(workspaceController.CurrentSpiceFilePath));
        }

        private void HandleImportRequested()
        {
            if (!EnsureDefaultDirectoryForFileWorkflow())
            {
                workspaceController.ShowFileOperationStatus("导入失败，无法创建默认目录。");
                return;
            }
            // C2.4：使用带 out error 的重载，区分失败与取消。
            var path = WindowsFileDialog.OpenFile(ImportDialogTitle, FileDialogFilter, FileDialogExtension, SpiceDrawingFileService.DefaultDirectory, out var dialogError);
            if (!string.IsNullOrEmpty(dialogError))
            {
                workspaceController.ShowFileOperationStatus(dialogError);
                return;
            }
            if (string.IsNullOrEmpty(path))
            {
                // 用户取消：不改 Workspace、不改路径、不显示错误。
                return;
            }

            // 空画布直接导入；非空画布先弹替换确认。
            if (IsWorkspaceEmpty())
            {
                ExecuteImportFromPath(path);
                return;
            }
            pendingImportPath = path;
            replaceConfirmationDialog?.Open();
        }

        /// <summary>
        /// 确保默认目录存在。失败时不打开文件对话框、不改路径、不改 Workspace。
        /// 测试可通过 EnsureDefaultDirectoryExistsOverrideForTesting 注入失败结果。
        /// </summary>
        private bool EnsureDefaultDirectoryForFileWorkflow()
        {
            return EnsureDefaultDirectoryExistsOverrideForTesting?.Invoke()
                ?? SpiceDrawingFileService.EnsureDefaultDirectoryExists();
        }

        private void HandleReplaceConfirmationConfirmed()
        {
            if (string.IsNullOrEmpty(pendingImportPath))
            {
                return;
            }
            var path = pendingImportPath;
            pendingImportPath = null;
            ExecuteImportFromPath(path);
        }

        private void HandleReplaceConfirmationCancelled()
        {
            // 用户取消或 Esc：丢弃待导入候选路径，不改 Workspace、不改 CurrentSpiceFilePath。
            pendingImportPath = null;
        }

        private void ExecuteImportFromPath(string path)
        {
            if (!workspaceController.TryImportWorkspaceFromPath(path, out var error))
            {
                // 失败消息面向普通用户；技术详情已由 C1 写入 Console/Player.log。
                workspaceController.ShowFileOperationStatus(BuildImportFailureMessage(error));
                return;
            }
            workspaceController.ShowFileOperationStatus("已导入：" + ExtractFileName(workspaceController.CurrentSpiceFilePath));
        }

        private bool IsWorkspaceEmpty()
        {
            return workspaceController.Model == null
                || (workspaceController.Model.Components.Count == 0 && workspaceController.Model.Wires.Count == 0);
        }

        private static string BuildDefaultSaveFileName()
        {
            // 默认文件名不含扩展名，由原生对话框的默认扩展名机制补成一次 .spicejson。
            // C1 的 NormalizeExtension 仍会做二次保障，确保不产生 .spicejson.spicejson。
            return "SPICE电路_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        private static string ExtractFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            var name = System.IO.Path.GetFileName(path);
            return string.IsNullOrEmpty(name) ? path : name;
        }

        private static string BuildImportFailureMessage(string error)
        {
            // 简洁分类，不暴露堆栈/异常类型/绝对路径。
            if (string.IsNullOrEmpty(error)) return "导入失败，文件格式无效。";
            if (error.IndexOf("UTF-8", StringComparison.Ordinal) >= 0) return "导入失败，文件不是有效的 UTF-8 文本。";
            if (error.IndexOf("超过", StringComparison.Ordinal) >= 0 || error.IndexOf("1MB", StringComparison.Ordinal) >= 0) return "导入失败，文件超过 1MB 限制。";
            if (error.IndexOf("position", StringComparison.OrdinalIgnoreCase) >= 0) return "导入失败，器件坐标缺失。";
            if (error.IndexOf("InstanceId", StringComparison.Ordinal) >= 0) return "导入失败，器件编号格式无效。";
            if (error.IndexOf("componentType", StringComparison.Ordinal) >= 0) return "导入失败，包含未知器件类型。";
            if (error.IndexOf("schemaVersion", StringComparison.Ordinal) >= 0) return "导入失败，图纸版本不支持。";
            return "导入失败，文件格式无效。";
        }

        // ============ C2 内部测试接缝（不用于生产路径） ============
        // 仅供 T3 验证 Host 协调逻辑，不暴露文件对话框或确认弹窗的内部状态。
        internal SpiceDrawingReplaceConfirmationDialog GetReplaceConfirmationDialogForTesting() => replaceConfirmationDialog;

        /// <summary>
        /// 仅供 T3 测试：覆盖默认目录检查结果。设为返回 false 的委托可验证
        /// "默认目录创建失败时不打开文件对话框、不改路径、不改 Workspace"。
        /// 设为 null 恢复生产路径（调用 SpiceDrawingFileService.EnsureDefaultDirectoryExists）。
        /// </summary>
        internal Func<bool> EnsureDefaultDirectoryExistsOverrideForTesting { private get; set; }

        /// <summary>
        /// 仅供 T3 测试：直接将候选路径送入非空画布导入流程，跳过 Windows 文件对话框
        /// （batchmode 无法调用原生对话框）。流程后续的替换确认/取消/导入仍走生产路径。
        /// </summary>
        internal bool TryBeginImportFromPathForTesting(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (IsWorkspaceEmpty())
            {
                ExecuteImportFromPath(path);
                return true;
            }
            pendingImportPath = path;
            replaceConfirmationDialog?.Open();
            return replaceConfirmationDialog != null && replaceConfirmationDialog.IsOpen;
        }

        internal string GetPendingImportPathForTesting() => pendingImportPath;

        /// <summary>
        /// 场景加载时调用一次。模式切换仅显隐 Root，不会再次绑定或重建 SPICE 工作区。
        /// </summary>
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (viewBindings == null || workspaceController == null)
            {
                throw new InvalidOperationException(
                    "Spice Demo host is missing serialized workspace bindings or controller.");
            }

            viewBindings.Validate();
            workspaceController.Initialize(viewBindings);
            SpiceWorkspacePresentationAdapter.Apply(viewBindings);
            workspaceController.RefreshAnalysisControlsForHost();
            initialized = true;
        }
    }

    internal static class SpiceWorkspacePresentationAdapter
    {
        private const float ToolbarButtonTop = -20f;
        private const float ToolbarButtonBottom = 20f;
        private const float PaletteCardWidth = 120f;
        private const float PaletteCardHeight = 116f;
        private const float PaletteCardGap = 12f;
        private const float AssistantSectionHeaderHeight = 42f;
        private const float ParameterSectionHeight = 166f;
        private const float NetlistSectionHeight = 180f;
        private const float AssistantSectionGap = 8f;
        private const float ParameterInputWidth = 130f;
        private const float ParameterUnitWidth = 60f;
        private const float ParameterApplyButtonWidth = 62f;
        private const float ParameterControlHeight = 32f;

        public static void Apply(SpiceWorkspaceViewBindings bindings)
        {
            if (bindings == null) return;

            StyleToolbarButton(bindings.RunButton, "运行计算", true, false, 18f, 132f);
            StyleToolbarButton(bindings.RotateButton, "旋转", false, false, 144f, 238f);
            StyleToolbarButton(bindings.DeleteButton, "删除", false, true, 250f, 344f);
            StyleToolbarButton(bindings.ClearButton, "清空", false, true, 356f, 450f);
            StyleToolbarButton(FindToolbarButton(bindings, "ZoomOut"), "－", false, false, 464f, 498f);
            StyleToolbarButton(FindToolbarButton(bindings, "ZoomIn"), "＋", false, false, 564f, 598f);
            StyleToolbarButton(FindToolbarButton(bindings, "FitAll"), "适配全部", false, false, 606f, 676f);
            StyleToolbarButton(FindToolbarButton(bindings, "ResetView"), "重置视图", false, false, 684f, 754f);
            // C2 文件操作工具栏按钮：保存 / 另存为 / 导入，使用右锚点布局，从右到左排列。
            // 导入最靠右（右边距 24，宽 80），另存为和保存依次向左，按钮间距 12。
            StyleToolbarButtonRightAnchored(FindToolbarButton(bindings, "ImportFile"), "导入", false, false, 24f, 104f);
            StyleToolbarButtonRightAnchored(FindToolbarButton(bindings, "SaveAsFile"), "另存为", false, false, 116f, 196f);
            StyleToolbarButtonRightAnchored(FindToolbarButton(bindings, "SaveFile"), "保存", false, false, 208f, 288f);
            StyleZoomLabel(bindings);
            StyleStatusTextRightPadding(bindings);

            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.DcVoltageSource, "直流电压源", "10 V", 0, 0);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.DcCurrentSource, "直流电流源", "1 mA", 1, 2);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.IdealSwitch, "理想开关", "断开", 0, 3);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.SiliconDiode, "通用硅二极管", "D_GENERIC", 1, 3);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Resistor, "电阻", "1 kΩ", 1, 0);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Capacitor, "电容", "1 μF", 0, 1);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Inductor, "电感", "10 mH", 1, 1);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.Ground, "接地", "GND", 0, 2);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.VoltageProbe, "电压探针", "V+ - V-", 0, 4);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.CurrentProbe, "电流探针", "IN → OUT", 1, 4);

            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.AcVoltageSource, "交流电压源", "~  AC", 0, 5);
            StylePaletteCard(bindings.PaletteRoot, SpiceComponentKind.IdealOperationalAmplifier, "理想运算放大器", "OP  +  −", 1, 5);

            StyleAssistant(bindings);
        }

        private static void StyleToolbarButton(Button button, string label, bool primary, bool danger, float left, float right)
        {
            if (button == null) return;

            Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(left, ToolbarButtonTop), new Vector2(right, ToolbarButtonBottom));

            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(10);
            image.type = Image.Type.Sliced;
            image.color = primary ? MainUiTheme.PrimaryBlue : Color.white;
            button.targetGraphic = image;

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : danger ? MainUiTheme.DangerBorder : MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                MainUiTheme.ApplyTextRole(text, primary ? MainUiTheme.UiTextRole.ToolbarPrimaryButton : danger ? MainUiTheme.UiTextRole.ToolbarDangerButton : MainUiTheme.UiTextRole.ToolbarButton);
                text.color = primary ? Color.white : danger ? MainUiTheme.DangerRed : MainUiTheme.NormalText;
                Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            }

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? MainUiTheme.Hex("1D4ED8") : danger ? MainUiTheme.Hex("FEF2F2") : MainUiTheme.Hex("F8FAFC");
            colors.pressedColor = primary ? MainUiTheme.Hex("1E40AF") : danger ? MainUiTheme.Hex("FEE2E2") : MainUiTheme.Hex("EAF2FF");
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = MainUiTheme.Hex("E5E7EB");
            button.colors = colors;
        }

        private static Button FindToolbarButton(SpiceWorkspaceViewBindings bindings, string name)
        {
            var toolbar = bindings.RunButton.transform.parent;
            var transform = toolbar.Find(name);
            return transform != null ? transform.GetComponent<Button>() : null;
        }

        /// <summary>
        /// 右锚点工具栏按钮：anchorMin/Max=(1,0.5)，offsetMin=(-rightOuter, -20)，offsetMax=(-rightInner, 20)。
        /// rightInner/rightOuter 均为距工具栏右边的正偏移；rightOuter > rightInner，宽度 = rightOuter - rightInner。
        /// 与既有左锚点按钮共享样式逻辑，仅锚定方式不同。
        /// </summary>
        private static void StyleToolbarButtonRightAnchored(Button button, string label, bool primary, bool danger, float rightInner, float rightOuter)
        {
            if (button == null) return;

            Anchor(button.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-rightOuter, ToolbarButtonTop), new Vector2(-rightInner, ToolbarButtonBottom));

            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(10);
            image.type = Image.Type.Sliced;
            image.color = primary ? MainUiTheme.PrimaryBlue : Color.white;
            button.targetGraphic = image;

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : danger ? MainUiTheme.DangerBorder : MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
                MainUiTheme.ApplyTextRole(text, primary ? MainUiTheme.UiTextRole.ToolbarPrimaryButton : danger ? MainUiTheme.UiTextRole.ToolbarDangerButton : MainUiTheme.UiTextRole.ToolbarButton);
                text.color = primary ? Color.white : danger ? MainUiTheme.DangerRed : MainUiTheme.NormalText;
                Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            }

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? MainUiTheme.Hex("1D4ED8") : danger ? MainUiTheme.Hex("FEF2F2") : MainUiTheme.Hex("F8FAFC");
            colors.pressedColor = primary ? MainUiTheme.Hex("1E40AF") : danger ? MainUiTheme.Hex("FEE2E2") : MainUiTheme.Hex("EAF2FF");
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = MainUiTheme.Hex("E5E7EB");
            button.colors = colors;
        }

        /// <summary>
        /// 状态文本区域采用横向 Stretch：anchorMin=(0,0) anchorMax=(1,1)，
        /// offsetMin=(762,0) offsetMax=(-304,0)。左侧避开"重置视图"（结束 754，留 8 间距），
        /// 右侧避开保存按钮左边界（距右 288，留 16 间距）。1366 宽度下实际宽度约 300。
        /// </summary>
        private static void StyleStatusTextRightPadding(SpiceWorkspaceViewBindings bindings)
        {
            var toolbar = bindings.RunButton.transform.parent;
            var statusTransform = toolbar.Find("Status");
            if (statusTransform == null) return;
            // 横向 Stretch：左侧避开"重置视图"结束位置 754（留 8 间距），
            // 右侧避开保存按钮左边界（距右 288，留 16 间距 → offsetMax.x=-304）。
            // 1366 宽度下实际宽度约 300，与左侧工具栏按钮和右侧文件按钮均不重叠。
            Anchor(statusTransform as RectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(762f, 0f), new Vector2(-304f, 0f));
        }

        private static void StyleZoomLabel(SpiceWorkspaceViewBindings bindings)
        {
            var toolbar = bindings.RunButton.transform.parent;
            var labelTransform = toolbar.Find("ZoomLabel");
            if (labelTransform == null) return;
            var text = labelTransform.GetComponent<Text>();
            if (text == null) return;
            MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.ToolbarButton);
            text.color = MainUiTheme.SecondaryText;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static void StylePaletteCard(RectTransform paletteRoot, SpiceComponentKind kind, string title, string summary, int column, int row)
        {
            if (paletteRoot == null) return;

            var cardTransform = paletteRoot.Find(kind + "Card") as RectTransform;
            if (cardTransform == null) return;

            var left = 16f + column * (PaletteCardWidth + PaletteCardGap);
            var top = -66f - row * (PaletteCardHeight + PaletteCardGap);
            Anchor(cardTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(left, top - PaletteCardHeight), new Vector2(left + PaletteCardWidth, top));

            var image = cardTransform.GetComponent<Image>() ?? cardTransform.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;
            var interaction = cardTransform.GetComponent<SpiceWorkspacePaletteCardPresentation>() ?? cardTransform.gameObject.AddComponent<SpiceWorkspacePaletteCardPresentation>();
            interaction.Initialize(image);

            var outline = cardTransform.GetComponent<Outline>() ?? cardTransform.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);

            RemoveChild(cardTransform, "Symbol");
            CreatePaletteSymbol(cardTransform, kind);
            StyleCardText(cardTransform, "Title", title, new Vector2(8f, 32f), new Vector2(-8f, 58f), MainUiTheme.DeepText, MainUiTheme.UiTextRole.PaletteCardTitle);
            StyleCardText(cardTransform, "Summary", summary, new Vector2(8f, 8f), new Vector2(-8f, 32f), MainUiTheme.MutedText, MainUiTheme.UiTextRole.MetaText);
        }

        private static void StyleCardText(RectTransform card, string name, string value, Vector2 offsetMin, Vector2 offsetMax, Color color, MainUiTheme.UiTextRole role)
        {
            var textTransform = card.Find(name) as RectTransform;
            var text = textTransform != null ? textTransform.GetComponent<Text>() : null;
            if (text == null) return;

            text.text = value;
            MainUiTheme.ApplyTextRole(text, role);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            Anchor(text.rectTransform, Vector2.zero, new Vector2(1f, 0f), offsetMin, offsetMax);
        }

        private static void CreatePaletteSymbol(RectTransform card, SpiceComponentKind kind)
        {
            var symbol = new GameObject("Symbol", typeof(RectTransform)).GetComponent<RectTransform>();
            symbol.SetParent(card, false);
            Anchor(symbol, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -70f), new Vector2(-18f, -14f));

            switch (kind)
            {
                case SpiceComponentKind.DcVoltageSource:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(36f, 0f));
                    AddLine(symbol, "LeadRight", new Vector2(66f, 0f), new Vector2(94f, 0f));
                    AddLine(symbol, "Source", new Vector2(36f, -15f), new Vector2(66f, 15f), 3f);
                    AddText(symbol, "Plus", "+", new Vector2(44f, 6f), 13);
                    AddText(symbol, "Minus", "-", new Vector2(58f, -7f), 13);
                    break;
                case SpiceComponentKind.AcVoltageSource:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(34f, 0f));
                    AddLine(symbol, "Source", new Vector2(34f, -14f), new Vector2(64f, 14f), 3f);
                    AddText(symbol, "Ac", "~", new Vector2(50f, 0f), 20);
                    AddLine(symbol, "LeadRight", new Vector2(64f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.IdealOperationalAmplifier:
                    AddLine(symbol, "TriangleLeft", new Vector2(34f, -18f), new Vector2(34f, 18f));
                    AddLine(symbol, "TriangleTop", new Vector2(34f, 18f), new Vector2(70f, 0f));
                    AddLine(symbol, "TriangleBottom", new Vector2(70f, 0f), new Vector2(34f, -18f));
                    AddText(symbol, "Op", "OP", new Vector2(48f, 0f), 12);
                    AddText(symbol, "Plus", "+", new Vector2(24f, 11f), 12);
                    AddText(symbol, "Minus", "−", new Vector2(24f, -11f), 12);
                    break;
                case SpiceComponentKind.DcCurrentSource:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(34f, 0f));
                    AddLine(symbol, "Source", new Vector2(34f, -14f), new Vector2(64f, 14f), 3f);
                    AddLine(symbol, "ArrowA", new Vector2(52f, 0f), new Vector2(64f, 0f), 3f);
                    AddLine(symbol, "ArrowB", new Vector2(58f, -6f), new Vector2(64f, 0f), 3f);
                    AddLine(symbol, "ArrowC", new Vector2(58f, 6f), new Vector2(64f, 0f), 3f);
                    AddLine(symbol, "LeadRight", new Vector2(64f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.IdealSwitch:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(42f, 0f));
                    AddLine(symbol, "Blade", new Vector2(42f, 0f), new Vector2(62f, 14f));
                    AddLine(symbol, "LeadRight", new Vector2(66f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.SiliconDiode:
                    // A 在左、K 在右；三角指向阴极，阴极竖线在右侧，与画布元件符号约定一致。
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(40f, 0f));
                    AddLine(symbol, "AnodeBar", new Vector2(40f, -12f), new Vector2(40f, 12f));
                    AddLine(symbol, "TriangleTop", new Vector2(40f, 12f), new Vector2(60f, 0f));
                    AddLine(symbol, "TriangleBottom", new Vector2(40f, -12f), new Vector2(60f, 0f));
                    AddLine(symbol, "CathodeBar", new Vector2(64f, -14f), new Vector2(64f, 14f));
                    AddLine(symbol, "LeadRight", new Vector2(64f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.Resistor:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(28f, 0f));
                    AddLine(symbol, "Body", new Vector2(28f, 0f), new Vector2(74f, 0f), 10f);
                    AddLine(symbol, "LeadRight", new Vector2(74f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.Capacitor:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(42f, 0f));
                    AddLine(symbol, "PlateA", new Vector2(42f, -15f), new Vector2(42f, 15f));
                    AddLine(symbol, "PlateB", new Vector2(58f, -15f), new Vector2(58f, 15f));
                    AddLine(symbol, "LeadRight", new Vector2(58f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.Inductor:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(24f, 0f));
                    AddLine(symbol, "CoilA", new Vector2(24f, 0f), new Vector2(36f, 12f));
                    AddLine(symbol, "CoilB", new Vector2(36f, 12f), new Vector2(48f, -12f));
                    AddLine(symbol, "CoilC", new Vector2(48f, -12f), new Vector2(60f, 12f));
                    AddLine(symbol, "CoilD", new Vector2(60f, 12f), new Vector2(72f, 0f));
                    AddLine(symbol, "LeadRight", new Vector2(72f, 0f), new Vector2(94f, 0f));
                    break;
                case SpiceComponentKind.VoltageProbe:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(38f, 0f));
                    AddLine(symbol, "LeadRight", new Vector2(62f, 0f), new Vector2(92f, 0f));
                    AddLine(symbol, "BoxTop", new Vector2(38f, -14f), new Vector2(62f, -14f));
                    AddLine(symbol, "BoxBottom", new Vector2(38f, 14f), new Vector2(62f, 14f));
                    AddLine(symbol, "BoxLeft", new Vector2(38f, -14f), new Vector2(38f, 14f));
                    AddLine(symbol, "BoxRight", new Vector2(62f, -14f), new Vector2(62f, 14f));
                    AddText(symbol, "V", "V", new Vector2(50f, 0f), 14);
                    break;
                case SpiceComponentKind.CurrentProbe:
                    AddLine(symbol, "LeadLeft", new Vector2(8f, 0f), new Vector2(38f, 0f));
                    AddLine(symbol, "LeadRight", new Vector2(62f, 0f), new Vector2(92f, 0f));
                    AddLine(symbol, "CircleTop", new Vector2(42f, -14f), new Vector2(58f, -14f));
                    AddLine(symbol, "CircleBottom", new Vector2(42f, 14f), new Vector2(58f, 14f));
                    AddLine(symbol, "CircleLeft", new Vector2(42f, -14f), new Vector2(42f, 14f));
                    AddLine(symbol, "CircleRight", new Vector2(58f, -14f), new Vector2(58f, 14f));
                    AddText(symbol, "ALabel", "A", new Vector2(50f, 0f), 13);
                    break;
                default:
                    AddLine(symbol, "Stem", new Vector2(51f, 20f), new Vector2(51f, -4f));
                    AddLine(symbol, "GroundA", new Vector2(34f, -4f), new Vector2(68f, -4f));
                    AddLine(symbol, "GroundB", new Vector2(39f, -12f), new Vector2(63f, -12f));
                    AddLine(symbol, "GroundC", new Vector2(44f, -20f), new Vector2(58f, -20f));
                    break;
            }
        }

        private static void AddLine(RectTransform parent, string name, Vector2 start, Vector2 end, float thickness = 3f)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = MainUiTheme.PrimaryBlue;
            image.raycastTarget = false;
            image.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            image.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            image.rectTransform.pivot = new Vector2(0.5f, 0.5f);

            var delta = end - start;
            image.rectTransform.anchoredPosition = start + delta * 0.5f;
            image.rectTransform.sizeDelta = new Vector2(Mathf.Max(2f, delta.magnitude), thickness);
            image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Rad2Deg * Mathf.Atan2(delta.y, delta.x));
        }

        private static void AddText(RectTransform parent, string name, string value, Vector2 position, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.MetaTextBold);
            text.text = value;
            text.fontSize = size;
            text.color = MainUiTheme.PrimaryBlue;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            text.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = new Vector2(18f, 18f);
        }

        private static void RemoveChild(RectTransform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null) UnityEngine.Object.Destroy(child.gameObject);
        }

        private static void StyleAssistant(SpiceWorkspaceViewBindings bindings)
        {
            var root = bindings.AssistantRoot;
            if (root == null) return;

            var image = root.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            image.color = MainUiTheme.PanelBackground;
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            var outline = root.GetComponent<Outline>() ?? root.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(-1f, 0f);

            var title = root.Find("AssistantTitle") as RectTransform;
            if (title != null)
            {
                Anchor(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
                var titleText = title.GetComponent<Text>();
                if (titleText != null)
                {
                    titleText.text = "仿真助手";
                    MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.InspectorTitle);
                    titleText.color = MainUiTheme.DeepText;
                    titleText.alignment = TextAnchor.MiddleLeft;
                }
            }

            LayoutAssistantSections(bindings);
            StyleParameterSection(bindings.ParameterRoot);
            var outcomeText = StyleTextSection(bindings.ResultRoot, "ResultHeader", "仿真结果", "ResultScrollView", "ResultText", "尚无仿真结果\n完成接线后点击“运行计算”", false, MainUiTheme.NormalText);
            StyleResultCopyButton(bindings.ResultRoot);
            StyleNetlistSection(bindings.NetlistRoot);
            var diagnosticText = StyleTextSection(bindings.DiagnosticRoot, "DiagnosticHeader", "诊断信息", "DiagnosticScrollView", "DiagnosticText", "暂无诊断信息", false, MainUiTheme.DangerRed);
            bindings.DiagnosticRoot.gameObject.SetActive(false);

            var outcome = bindings.ResultRoot.GetComponent<SpiceAssistantOutcomePresentation>() ?? bindings.ResultRoot.gameObject.AddComponent<SpiceAssistantOutcomePresentation>();
            outcome.Initialize(outcomeText, diagnosticText, bindings.RunButton);
        }

        private static void LayoutAssistantSections(SpiceWorkspaceViewBindings bindings)
        {
            Anchor(bindings.ParameterRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -54f - ParameterSectionHeight), new Vector2(-12f, -54f));

            var resultTop = -54f - ParameterSectionHeight - AssistantSectionGap;
            Anchor(bindings.ResultRoot, Vector2.zero, Vector2.one, new Vector2(12f, 18f + NetlistSectionHeight + AssistantSectionGap), new Vector2(-12f, resultTop));
            Anchor(bindings.NetlistRoot, Vector2.zero, new Vector2(1f, 0f), new Vector2(12f, 18f), new Vector2(-12f, 18f + NetlistSectionHeight));
        }

        private static void StyleParameterSection(RectTransform section)
        {
            if (section == null) return;

            StyleSectionPanel(section);
            var header = EnsureHeader(section, "ParameterHeader", "参数设置");
            var title = section.Find("ParameterTitle") as RectTransform;
            if (title != null)
            {
                title.SetParent(header, false);
                Stretch(title, new Vector2(14f, 0f), new Vector2(-14f, 0f));
                var titleText = title.GetComponent<Text>();
                if (titleText != null)
                {
                    titleText.text = "参数设置";
                    MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.InspectorCardTitle);
                    titleText.alignment = TextAnchor.MiddleLeft;
                    titleText.color = MainUiTheme.SecondaryText;
                }
            }

            var subtitle = EnsureText(section, "ParameterSubtitle", "请选择画布中的元件以编辑参数", MainUiTheme.MutedText, MainUiTheme.UiTextRole.InspectorBody);
            Anchor(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -80f), new Vector2(-14f, -48f));

            var input = section.Find("ParameterInput") as RectTransform;
            var unit = section.Find("Unit") as RectTransform;
            var phaseLabel = section.Find("AcPhaseLabel") as RectTransform;
            var phaseInput = section.Find("AcPhaseInput") as RectTransform;
            var phaseUnit = section.Find("AcPhaseUnit") as RectTransform;
            var apply = section.Find("Apply") as RectTransform;
            if (input != null)
            {
                Anchor(input, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -112f), new Vector2(14f + ParameterInputWidth, -112f + ParameterControlHeight));
                StyleInput(input.GetComponent<InputField>());
            }
            if (unit != null)
            {
                var unitLeft = 14f + ParameterInputWidth + AssistantSectionGap;
                Anchor(unit, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(unitLeft, -112f), new Vector2(unitLeft + ParameterUnitWidth, -112f + ParameterControlHeight));
                StyleSmallButton(unit.GetComponent<Button>(), false);
            }
            if (phaseLabel != null)
            {
                Anchor(phaseLabel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -152f), new Vector2(70f, -120f));
            }
            if (phaseInput != null)
            {
                Anchor(phaseInput, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(74f, -152f), new Vector2(14f + ParameterInputWidth, -120f));
                StyleInput(phaseInput.GetComponent<InputField>());
            }
            if (phaseUnit != null)
            {
                Anchor(phaseUnit, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f + ParameterInputWidth + AssistantSectionGap, -152f), new Vector2(14f + ParameterInputWidth + AssistantSectionGap + ParameterUnitWidth, -120f));
            }
            if (apply != null)
            {
                var applyLeft = 14f + ParameterInputWidth + AssistantSectionGap + ParameterUnitWidth + AssistantSectionGap;
                Anchor(apply, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(applyLeft, -152f), new Vector2(applyLeft + ParameterApplyButtonWidth, -120f));
                StyleSmallButton(apply.GetComponent<Button>(), true, "应用");
            }

            var presenter = section.GetComponent<SpiceAssistantParameterPresentation>() ?? section.gameObject.AddComponent<SpiceAssistantParameterPresentation>();
            presenter.Initialize(title != null ? title.GetComponent<Text>() : null, subtitle, input != null ? input.GetComponent<InputField>() : null, unit != null ? unit.GetComponent<Button>() : null, apply != null ? apply.gameObject : null);
        }

        private static void StyleResultCopyButton(RectTransform resultRoot)
        {
            if (resultRoot == null) return;
            var header = resultRoot.Find("ResultHeader") as RectTransform;
            if (header == null) return;

            // 调整标题右边距，为复制按钮留出空间
            var title = header.Find("Title") as RectTransform;
            if (title != null) Anchor(title, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-134f, -4f));

            var buttonTransform = header.Find("CopyResult") as RectTransform;
            if (buttonTransform == null) return;
            StyleSmallButton(buttonTransform.GetComponent<Button>(), false, "复制结果");
            Anchor(buttonTransform, new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-128f, 2f), new Vector2(-14f, -2f));
        }

        private static void StyleNetlistSection(RectTransform section)
        {
            if (section == null) return;

            StyleSectionPanel(section);
            var header = section.Find("NetlistHeader") as RectTransform;
            if (header != null)
            {
                StyleHeader(header, "生成网表");
                var title = header.Find("Title") as RectTransform;
                if (title != null) Anchor(title, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-194f, -4f));
                var status = header.Find("Status") as RectTransform;
                if (status != null)
                {
                    Anchor(status, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(14f, 4f), new Vector2(-14f, 0f));
                    var text = status.GetComponent<Text>();
                    if (text != null)
                    {
                        MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.MetaText);
                        text.color = MainUiTheme.MutedText;
                    }
                }

                StyleSmallButton((header.Find("Toggle") as RectTransform)?.GetComponent<Button>(), false);
                StyleSmallButton((header.Find("Copy") as RectTransform)?.GetComponent<Button>(), false);
            }

            var view = StyleScrollableText(section, "NetlistScrollView", "NetlistText", true, MainUiTheme.NormalText);
            if (view != null && string.IsNullOrWhiteSpace(view.text)) view.text = "尚未生成网表";
        }

        private static Text StyleTextSection(RectTransform section, string headerName, string title, string scrollName, string textName, string emptyText, bool monospace, Color color)
        {
            if (section == null) return null;

            StyleSectionPanel(section);
            var header = section.Find(headerName) as RectTransform;
            if (header != null) StyleHeader(header, title);

            var text = StyleScrollableText(section, scrollName, textName, monospace, color);
            if (text != null && string.IsNullOrWhiteSpace(text.text)) text.text = emptyText;
            return text;
        }

        private static void StyleSectionPanel(RectTransform section)
        {
            var image = section.GetComponent<Image>() ?? section.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;

            var outline = section.GetComponent<Outline>() ?? section.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static RectTransform EnsureHeader(RectTransform section, string name, string title)
        {
            var header = section.Find(name) as RectTransform;
            if (header == null)
            {
                header = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                header.SetParent(section, false);
            }

            StyleHeader(header, title);
            header.SetAsFirstSibling();
            return header;
        }

        private static void StyleHeader(RectTransform header, string title)
        {
            Anchor(header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -AssistantSectionHeaderHeight), new Vector2(-10f, 0f));
            var image = header.GetComponent<Image>() ?? header.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = MainUiTheme.Hex("F8FBFF");

            var text = header.Find("Title") as RectTransform;
            if (text == null)
            {
                text = new GameObject("Title", typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
                text.SetParent(header, false);
            }
            Stretch(text, new Vector2(14f, 0f), new Vector2(-14f, 0f));
            var label = text.GetComponent<Text>();
            if (label != null)
            {
                label.text = title;
                MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.InspectorCardTitle);
                label.alignment = TextAnchor.MiddleLeft;
                label.color = MainUiTheme.SecondaryText;
            }
        }

        private static Text StyleScrollableText(RectTransform section, string scrollName, string textName, bool monospace, Color color)
        {
            var scrollRect = section.Find(scrollName) as RectTransform;
            if (scrollRect == null) return null;

            Anchor(scrollRect, Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -50f));
            var scrollImage = scrollRect.GetComponent<Image>() ?? scrollRect.gameObject.AddComponent<Image>();
            scrollImage.color = MainUiTheme.Hex("FBFDFF");
            scrollImage.sprite = UiThemeTokens.GetRoundedSprite(6);
            scrollImage.type = Image.Type.Sliced;

            var scroll = scrollRect.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;
            }

            var textRect = scrollRect.Find("Viewport/Content/" + textName) as RectTransform;
            var text = textRect != null ? textRect.GetComponent<Text>() : null;
            if (text != null)
            {
                if (monospace)
                {
                    text.font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, 14);
                    text.fontSize = 14;
                    text.fontStyle = FontStyle.Normal;
                }
                else
                {
                    MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.InspectorBody);
                }
                text.color = color;
                text.alignment = TextAnchor.UpperLeft;
                text.lineSpacing = monospace ? 1.1f : 1.28f;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
            }

            return text;
        }

        private static Text EnsureText(RectTransform parent, string name, string value, Color color, MainUiTheme.UiTextRole role)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<RectTransform>();
                rect.SetParent(parent, false);
            }

            var text = rect.GetComponent<Text>();
            text.text = value;
            MainUiTheme.ApplyTextRole(text, role);
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            return text;
        }

        private static void StyleInput(InputField input)
        {
            if (input == null) return;
            var image = input.GetComponent<Image>() ?? input.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var outline = input.GetComponent<Outline>() ?? input.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("D8DEE8");
            outline.effectDistance = new Vector2(1f, -1f);
            if (input.textComponent != null)
            {
                MainUiTheme.ApplyTextRole(input.textComponent, MainUiTheme.UiTextRole.SearchInputText);
                input.textComponent.alignment = TextAnchor.MiddleLeft;
                input.textComponent.color = MainUiTheme.NormalText;
            }
        }

        private static void StyleSmallButton(Button button, bool primary, string label = null)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = primary ? MainUiTheme.PrimaryBlue : MainUiTheme.FilterButton;
            button.targetGraphic = image;
            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = primary ? MainUiTheme.PrimaryBlue : MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                if (!string.IsNullOrEmpty(label)) text.text = label;
                MainUiTheme.ApplyTextRole(text, primary ? MainUiTheme.UiTextRole.InspectorButton : MainUiTheme.UiTextRole.FilterText);
                text.alignment = TextAnchor.MiddleCenter;
                text.color = primary ? Color.white : MainUiTheme.SecondaryText;
                Stretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            }
        }

        private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = min;
            rect.offsetMax = max;
        }

        private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }

    internal sealed class SpiceWorkspacePaletteCardPresentation : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Image image;
        private bool hovered;
        private bool pressed;

        public void Initialize(Image target)
        {
            image = target;
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            Apply();
        }

        private void Apply()
        {
            if (image == null) return;
            image.color = pressed ? MainUiTheme.Hex("DBEAFE") : hovered ? MainUiTheme.Hex("F8FBFF") : Color.white;
        }
    }

    /// <summary>
    /// Keeps the existing result and diagnostic producers intact while presenting either outcome in ResultRoot.
    /// The hidden diagnostic view remains the controller's explicit diagnostic sink.
    /// </summary>
    internal sealed class SpiceAssistantOutcomePresentation : MonoBehaviour
    {
        private const string EmptyOutcome = "尚无仿真结果\n完成接线后点击“运行计算”";
        private const string RunningOutcome = "正在计算……";

        private Text outcomeText;
        private Text diagnosticText;
        private Button runButton;
        private ScrollRect scrollRect;

        public void Initialize(Text result, Text diagnostics, Button run)
        {
            outcomeText = result;
            diagnosticText = diagnostics;
            runButton = run;
            scrollRect = result != null ? result.GetComponentInParent<ScrollRect>() : null;
            Refresh();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        internal void RefreshNow()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (outcomeText == null) return;

            var diagnostics = diagnosticText != null ? diagnosticText.text : string.Empty;
            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                Render(diagnostics, MainUiTheme.DangerRed);
                return;
            }

            if (runButton != null && !runButton.interactable)
            {
                Render(RunningOutcome, MainUiTheme.MutedText);
                return;
            }

            var source = outcomeText.text;
            Render(string.IsNullOrWhiteSpace(source) || source == "尚无结果" ? EmptyOutcome : source, MainUiTheme.NormalText);
        }

        private void Render(string value, Color color)
        {
            var contentChanged = outcomeText.text != value || outcomeText.color != color;
            if (!contentChanged) return;

            outcomeText.color = color;
            SpiceScrollableTextLayout.Refresh(scrollRect, outcomeText, value, true);
        }
    }

    internal sealed class SpiceAssistantParameterPresentation : MonoBehaviour
    {
        private Text title;
        private Text subtitle;
        private InputField input;
        private Button unitButton;
        private GameObject applyButton;
        private string lastTitle;
        private bool lastInteractable;

        public void Initialize(Text titleText, Text subtitleText, InputField parameterInput, Button unit, GameObject apply)
        {
            title = titleText;
            subtitle = subtitleText;
            input = parameterInput;
            unitButton = unit;
            applyButton = apply;
            lastTitle = null;
            Apply();
        }

        private void LateUpdate()
        {
            if (title == null) return;
            var interactable = input != null && input.interactable;
            if (lastTitle == title.text && lastInteractable == interactable) return;
            Apply();
        }

        private void Apply()
        {
            if (title == null) return;

            var rawTitle = string.IsNullOrWhiteSpace(title.text) ? "参数设置" : title.text;
            var hasSelection = input != null && input.interactable;
            var componentText = rawTitle.EndsWith(" 参数设置", StringComparison.Ordinal)
                ? rawTitle.Substring(0, rawTitle.Length - " 参数设置".Length)
                : string.Empty;

            title.text = "参数设置";
            if (subtitle != null)
            {
                subtitle.text = hasSelection && !string.IsNullOrEmpty(componentText)
                    ? "元件：" + componentText
                    : "请选择画布中的元件以编辑参数";
                subtitle.color = hasSelection ? MainUiTheme.SecondaryText : MainUiTheme.MutedText;
            }

            if (input != null) input.gameObject.SetActive(hasSelection);
            if (unitButton != null) unitButton.gameObject.SetActive(hasSelection);
            if (applyButton != null) applyButton.SetActive(hasSelection);

            lastTitle = title.text;
            lastInteractable = hasSelection;
        }
    }
}
