using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    // 保存、导入及文件会话状态；不改变 Serializer/FileService 的格式与写入语义。
    public sealed partial class SpiceWorkspaceController
    {
        /// <summary>
        /// 文件按钮回调：每次点击都二次校验 Running，避免任何路径在仿真中被触发。
        /// 不直接调用文件 API；仅触发事件，由 Host 协调对话框与确认。
        /// </summary>
        private void HandleSaveButtonClicked()
        {
            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                ShowFileOperationStatus("仿真计算进行中，请稍后操作图纸。");
                return;
            }
            // 无当前路径时“保存”等同“另存为”：交给 Host 走对话框流程。
            if (!fileService.HasCurrentSpiceFilePath)
            {
                SaveAsRequested?.Invoke();
                return;
            }
            SaveRequested?.Invoke();
        }

        private void HandleSaveAsButtonClicked()
        {
            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                ShowFileOperationStatus("仿真计算进行中，请稍后操作图纸。");
                return;
            }
            SaveAsRequested?.Invoke();
        }

        private void HandleImportButtonClicked()
        {
            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                ShowFileOperationStatus("仿真计算进行中，请稍后操作图纸。");
                return;
            }
            ImportRequested?.Invoke();
        }

        /// <summary>
        /// 文件操作状态提示：显示保存、导入等操作的成功、失败或提示消息。仅由 Host 在文件操作完成后调用。
        /// 不覆盖运行计算、参数更新、接线提示或事务式导入成功后的“未计算”等已有状态时机
        /// （Host 负责仅在合适时机调用本方法）。
        /// </summary>
        public void ShowFileOperationStatus(string message)
        {
            if (statusText != null && !string.IsNullOrEmpty(message))
            {
                statusText.text = message;
            }
        }

        /// <summary>
        /// 文件操作按钮可用性：根据 ResultState 刷新保存、另存为和导入按钮的 interactable。
        /// 仅在进入 Running 和离开 Running 时由 RunCalculationAsync 调用一次，
        /// 不依赖全局 Update 轮询。
        /// </summary>
        private void RefreshFileOperationButtonsAvailability()
        {
            var enabled = ResultState != SpiceWorkspaceResultState.Running;
            if (saveFileButton != null) saveFileButton.interactable = enabled;
            if (saveAsFileButton != null) saveAsFileButton.interactable = enabled;
            if (importFileButton != null) importFileButton.interactable = enabled;
        }

        // 仅供 T3 测试验证按钮存在与可交互状态。不在生产路径调用。
        internal Button GetSaveFileButtonForTesting() => saveFileButton;
        internal void RefreshAnalysisControlsForHost() => RefreshAnalysisControls();
        internal Button GetSaveAsFileButtonForTesting() => saveAsFileButton;
        internal Button GetImportFileButtonForTesting() => importFileButton;
        internal Button GetAnalysisModeToggleButtonForTesting() => analysisModeToggleButton;
        internal CanvasGroup GetPaletteCardForTesting(SpiceComponentKind kind) => paletteCardGroups.TryGetValue(kind, out var group) ? group : null;
        internal InputField GetParameterInputForTesting() => parameterInput;
        internal Button GetUnitButtonForTesting() => unitButton;
        internal Text GetOpAmpInfoForTesting() => opAmpInfoText;
        internal Button GetParameterApplyButtonForTesting() => parameterApplyButton;
        internal string GetVisibleResultTextForTesting() => resultText != null ? resultText.text : null;
        internal string GetVisibleDiagnosticTextForTesting() => diagnosticView != null && diagnosticView.Text != null ? diagnosticView.Text.text : null;
        internal string GetSelectedComponentIdForTesting() => selectedComponent != null ? selectedComponent.InstanceId : null;
        internal int GetComponentViewCountForTesting() => componentViews.Count;
        internal int GetWireViewCountForTesting() => wireViews.Count;

        // 仅供 T3 测试直接触发按钮回调，验证 Running 二次保护与事件路由。
        internal void InvokeSaveButtonForTesting() => HandleSaveButtonClicked();
        internal void InvokeSaveAsButtonForTesting() => HandleSaveAsButtonClicked();
        internal void InvokeImportButtonForTesting() => HandleImportButtonClicked();
        /// <summary>
        /// 事务式导入图纸 JSON。先在临时模型上完整解析和校验，成功后才替换当前工作区。
        /// 失败时不修改任何当前状态（画布、元件、Wire、选择、结果、网表、编号）。
        /// 仿真计算进行中时拒绝导入，以避免旧电路异步结果覆盖刚导入电路的结果和网表。
        /// </summary>
        public bool TryImportDrawingJson(string json, out string error)
        {
            EnsureInitialized();
            if (!CanImportDrawing(out error))
            {
                return false;
            }
            // 阶段一：纯解析+校验，构建临时模型。任何失败都直接返回，不触碰当前状态。
            if (!SpiceDrawingSerializer.TryFromJson(json, out var tempModel, out error))
            {
                return false;
            }
            // 阶段二：只有临时模型完整构建成功后才进入提交阶段。
            CommitImportedModel(tempModel);
            return true;
        }

        /// <summary>
        /// 判定当前是否允许导入图纸。仿真计算进行中时拒绝，保留计算结果完整性。
        /// 不取消当前 ngspice，不等待 Task，不修改任何状态。
        /// </summary>
        internal bool CanImportDrawing(out string reason)
        {
            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                reason = "仿真计算进行中，请稍后导入图纸。";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// 仅供 T3 测试受控设置 ResultState，以验证 Running 等状态下的导入保护。
        /// 不在生产路径调用；不触发 ngspice，不修改视图或结果文本。
        /// 同时刷新文件操作按钮可用性，便于测试验证 Running 时按钮禁用。
        /// </summary>
        internal void SetResultStateForTesting(SpiceWorkspaceResultState state)
        {
            ResultState = state;
            RefreshResultStateDependentControls();
        }

        /// <summary>
        /// 仅供 T3 测试读取当前状态栏文本，以验证 Controller 不直接写入保存成功提示。
        /// 不在生产路径调用。
        /// </summary>
        internal string GetStatusTextForTesting()
        {
            return statusText != null ? statusText.text : null;
        }

        /// <summary>
        /// 仅供 T3 测试设置状态栏初始文本，便于验证 正式工作流 未覆盖成功 UI 文案。
        /// 不在生产路径调用。
        /// </summary>
        internal void SetStatusTextForTesting(string text)
        {
            if (statusText != null) statusText.text = text;
        }

        /// <summary>当前会话的图纸文件路径。保存或导入成功后更新；清空画布后清除。</summary>
        public string CurrentSpiceFilePath => fileService.CurrentSpiceFilePath;

        /// <summary>是否已绑定当前会话文件路径。文件工作流的“保存”按钮据此决定是否改走“另存为”。</summary>
        public bool HasCurrentSpiceFilePath => fileService.HasCurrentSpiceFilePath;

        /// <summary>
        /// 将当前 Workspace 保存到指定路径（原子写入 UTF-8）。
        /// 复用 SpiceDrawingSerializer.ToJson；不保存结果、网表、诊断、选择、pending Wire、缩放或平移。
        /// 保存成功后 CurrentSpiceFilePath 更新为规范化路径；失败时保持旧值。
        /// 仿真计算进行中拒绝保存。
        /// </summary>
        public bool TrySaveWorkspaceToPath(string path, out string error)
        {
            EnsureInitialized();
            if (!CanImportDrawing(out error))
            {
                // 仿真进行中拒绝保存：不创建目录、不写临时文件、不取消 ngspice。
                error = "仿真计算进行中，请稍后保存图纸。";
                return false;
            }
            string json;
            try
            {
                json = SpiceDrawingSerializer.ToJson(Model);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = "保存失败，无法生成图纸内容。";
                return false;
            }
            var normalizedPath = SpiceDrawingFileService.NormalizeExtension(path);
            if (!fileService.TrySaveUtf8Atomically(normalizedPath, json, out error))
            {
                // 保存失败：CurrentSpiceFilePath 保持旧值。
                return false;
            }
            fileService.SetCurrentSpiceFilePath(normalizedPath);
            // 保存是现有 Model 的持久化快照，不是电气修改；成功后只清除 dirty，不推进 revision 或使结果过期。
            isDirty = false;
            // Controller 不直接显示保存成功提示；Host 根据 CurrentSpiceFilePath 显示“已保存：<文件名>”。
            return true;
        }

        /// <summary>
        /// 将当前 Workspace 保存到已绑定的 CurrentSpiceFilePath。
        /// 当前路径为空时返回清晰失败，不自行打开对话框。
        /// </summary>
        public bool TrySaveCurrentWorkspace(out string error)
        {
            EnsureInitialized();
            if (!fileService.HasCurrentSpiceFilePath)
            {
                error = "尚未指定保存路径，请使用另存为。";
                return false;
            }
            return TrySaveWorkspaceToPath(fileService.CurrentSpiceFilePath, out error);
        }

        /// <summary>
        /// 从指定路径导入图纸：先进行文件级检查和 UTF-8 读取，
        /// 再将原始 JSON 原封不动交给事务式导入入口 TryImportDrawingJson。
        /// 不在文件层解析器件、坐标、端子或 Wire；不调用 ClearWorkspace；不提前清空当前画布。
        /// 事务式导入失败时原样保留当前 Workspace 和当前路径。
        /// 导入成功后 CurrentSpiceFilePath 更新为导入路径。
        /// </summary>
        public bool TryImportWorkspaceFromPath(string path, out string error)
        {
            EnsureInitialized();
            // 第一道防线：运行中拒绝导入（在读取文件之前）。
            // 保持事务式导入的运行中导入保护作为第二道防线。
            if (!CanImportDrawing(out error))
            {
                error = "仿真计算进行中，请稍后导入图纸。";
                return false;
            }
            if (!fileService.TryReadUtf8File(path, out var json, out error))
            {
                // 读取失败：CurrentSpiceFilePath 保持旧值。
                return false;
            }
            // 保存旧路径，便于事务式导入失败时恢复。
            var previousPath = fileService.CurrentSpiceFilePath;
            if (!TryImportDrawingJson(json, out error))
            {
                // 事务式导入失败：当前 Workspace 和当前路径均不变。
                fileService.SetCurrentSpiceFilePath(previousPath);
                return false;
            }
            // 导入成功：更新当前路径（导入路径不规范化扩展名，保持用户传入的路径）。
            fileService.SetCurrentSpiceFilePath(path);
            // Controller 不直接显示导入成功提示；Host 根据 CurrentSpiceFilePath 显示“已导入：<文件名>”。
            return true;
        }

        /// <summary>清除当前会话文件路径。仅在 ClearWorkspace 成功后调用。</summary>
        private void ClearCurrentSpiceFilePath()
        {
            fileService.ClearCurrentSpiceFilePath();
        }

        /// <summary>
        /// 成功导入提交：销毁旧视图、替换模型、重建视图、清除旧结果。
        /// 只在 TryFromJson 成功后调用，失败路径永远不会进入此方法。
        /// 不复用 ClearWorkspace，避免其 ResetInstanceNaming 副作用与导入编号语义冲突。
        /// </summary>
        private void CommitImportedModel(SpiceWorkspaceModel tempModel)
        {
            // 1. 安全取消所有进行中的交互
            CancelPendingWire();
            CancelPaletteDrag();
            componentDragInProgress = false;

            // 2. 清理选中状态（手动 SetSelected(false) 以清除视觉高亮）
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = null;

            // 3. 销毁旧视图
            foreach (var wire in wireViews.ToList()) wire.Destroy();
            wireViews.Clear();
            foreach (var view in componentViews.Values) SpiceUnityObjectLifetime.Destroy(view.gameObject);
            componentViews.Clear();

            // 4. 替换模型：解除旧订阅 → 替换引用 → 订阅新模型（只订阅一次）
            Model.Changed -= HandleModelChanged;
            Model = tempModel;
            Model.Changed += HandleModelChanged;
            AdvanceElectricalRevision();
            isDirty = false;

            // 5. 按新模型重建所有元件视图（CreateComponentView 保留 InstanceId、Position、Rotation、SiValue）
            foreach (var component in Model.Components)
            {
                CreateComponentView(component);
            }

            // 6. 按新模型重建所有 Wire 视图（SpiceWorkspaceWireView 构造时按 VisualState 重建路由）
            foreach (var wire in Model.Wires)
            {
                var startView = componentViews[wire.StartComponentId];
                var endView = componentViews[wire.EndComponentId];
                wireViews.Add(new SpiceWorkspaceWireView(this, wire, startView, endView));
            }

            // 7. 清除旧仿真结果、旧诊断、旧网表，恢复为未运行状态
            generatedNetlistContent = null;
            generatedNetlistRevision = -1;
            lastOutcomeText = null;
            ResultState = SpiceWorkspaceResultState.NeverRun;
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            ClearParameterPanel();
            // 导入提交后从 Model 快照刷新模式和频率控件，不能依赖导入前 UI 文本。
            RefreshAnalysisControls();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            RefreshCopyResultButton();
            if (statusText != null) statusText.text = StateMessage();

            // 8. 视图适配：有内容时 FitAll，空画布时 ResetView
            if (viewController != null)
            {
                if (Model.Components.Count == 0 && Model.Wires.Count == 0)
                {
                    viewController.ResetView();
                }
                else
                {
                    viewController.FitAll();
                }
            }
        }

    }
}
