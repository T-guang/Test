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
        // 文件工作流：替换确认弹窗与待导入候选路径。
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

            // 文件工作流：创建替换确认弹窗（一次性，复用）并订阅文件操作事件。
            InitializeFileWorkflow(modeController.PopupLayer);

            modeController.ModeChanged += HandleModeChanged;
        }

        /// <summary>
        /// 文件工作流初始化：创建替换确认弹窗并订阅 Controller 的文件操作事件。
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

        /// <summary>仅供 T3 测试在不依赖 SimulationModeController 的情况下初始化文件工作流。</summary>
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

        // ============ 文件工作流：保存 / 另存为 / 导入 ============
        // Host 负责 Windows 文件对话框、替换确认和用户反馈。
        // 所有文件读写均复用 Controller 的 TrySaveWorkspaceToPath / TrySaveCurrentWorkspace / TryImportWorkspaceFromPath，
        // 不重写或绕过 正式工作流/事务式导入 的原子写入、UTF-8 严格读取、事务导入与路径会话状态。

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
            // 文件对话框调用使用带 out error 的重载，区分失败与取消。
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
            // 文件对话框调用使用带 out error 的重载，区分失败与取消。
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
                // 失败消息面向普通用户；技术详情已由 正式工作流 写入 Console/Player.log。
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
            // 正式工作流 的 NormalizeExtension 仍会做二次保障，确保不产生 .spicejson.spicejson。
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

        // ============ 文件工作流 内部测试接缝（不用于生产路径） ============
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
}
