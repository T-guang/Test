using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Workspace;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    [Serializable]
    public sealed class SpiceT3PlayerValidationReport
    {
        public bool success;
        public double firstCurrent;
        public double updatedCurrent;
        public bool d1StaleDiscardPassed;
        public bool d2ImportLimitsPassed;
        public bool d3ClearStatePassed;
        public bool d3NetlistRevisionPassed;
        public bool unexpectedErrorSanitizationPassed;
        public bool workspaceGeometryPassed;
        public bool gridGeometryPassed;
        public bool horizontalDraggingPassed;
        public bool verticalDraggingPassed;
        public bool zoomDraggingPassed;
        public bool toolbarLayoutPassed;
        public bool analysisModeTogglePassed;
        public bool acFrequencySettingsPassed;
        public bool modeIncompatibilityDiagnosticPassed;
        public bool paletteBottomRowFullyVisible;
        public float paletteViewportHeight;
        public float paletteContentHeight;
        public float paletteMaxScroll;
        public float paletteBottomPosition;
        public float pnpVisibleHeight;
        public string failure;
    }

    /// <summary>
    /// 独立 T3 Player 验证入口：通过真实工作区建立单电阻回路、修改参数并写出结果，
    /// 不属于正式原型场景的 UI 功能或保存格式。
    /// </summary>
    public sealed class SpiceT3PlayerValidationHarness : MonoBehaviour
    {
        private async void Start()
        {
            var report = new SpiceT3PlayerValidationReport();
            try
            {
                // 场景基础设施已序列化，Bootstrap 在全部 Awake 完成前绑定宿主并初始化 Controller；Start 无需等待一帧。
                var bootstrap = GetComponent<SpiceWorkspacePrototypeBootstrap>();
                if (bootstrap == null)
                    throw new InvalidOperationException("SpiceWorkspacePrototypeBootstrap is missing from the validation host.");
                var workspace = bootstrap.Controller;
                if (workspace == null)
                    throw new InvalidOperationException("Spice workspace was not initialized by Bootstrap.Awake.");
                workspace.ValidateAssistantPanelLayout();
                ValidateWorkspaceGeometry(workspace, report);
                ValidatePaletteBottomRowGeometry(bootstrap.GetComponent<SpiceWorkspaceViewBindings>(), report);
                ValidateWorkspaceUiControls(workspace, report);
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-160f, 40f));
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(120f, 40f));
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, new Vector2(0f, -140f));
                SpiceT3WorkspaceValidation.ConnectSingleResistor(workspace, source.InstanceId, resistor.InstanceId, ground.InstanceId);
                ValidateComponentDragging(workspace, resistor, report);
                var first = await workspace.RunCalculationAsync();
                if (first == null || !first.Success || Math.Abs(first.ComponentResults[resistor.InstanceId].Current - 0.01d) > 1e-8d) throw new InvalidOperationException("Single-resistor Player flow failed.");
                report.firstCurrent = first.ComponentResults[resistor.InstanceId].Current;
                if (!workspace.TrySetParameter(resistor.InstanceId, 2d, "kOhm") || workspace.ResultState != SpiceWorkspaceResultState.Stale) throw new InvalidOperationException("Player parameter change did not stale the result.");
                if (workspace.TryGetCopyableNetlistText(out _)) throw new InvalidOperationException("Stale Player netlist remained copyable.");
                var updated = await workspace.RunCalculationAsync();
                if (updated == null || !updated.Success || Math.Abs(updated.ComponentResults[resistor.InstanceId].Current - 0.005d) > 1e-8d) throw new InvalidOperationException("Updated Player result is incorrect.");
                report.updatedCurrent = updated.ComponentResults[resistor.InstanceId].Current;
                if (!workspace.TryGetCopyableNetlistText(out _)) throw new InvalidOperationException("Updated Player netlist was not copyable.");
                report.d3NetlistRevisionPassed = true;

                await ValidateD1StaleResultDiscard(workspace, resistor);
                report.d1StaleDiscardPassed = true;
                report.d2ImportLimitsPassed = ValidateD2PathImportLimit(workspace);

                var currentAfterDiscard = await workspace.RunCalculationAsync();
                if (currentAfterDiscard == null || !currentAfterDiscard.Success ||
                    workspace.ResultState != SpiceWorkspaceResultState.Current ||
                    !workspace.TryGetCopyableOutcomeText(out _) ||
                    !workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("Player flow did not recover a current result after D1 discard.");

                workspace.ClearWorkspace();
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun ||
                    workspace.TryGetCopyableOutcomeText(out _) ||
                    workspace.TryGetCopyableNetlistText(out _) ||
                    workspace.Model.Components.Count != 0 || workspace.Model.Wires.Count != 0)
                    throw new InvalidOperationException("Player ClearWorkspace did not clear result and netlist state.");
                report.d3ClearStatePassed = true;

                ValidateAnalysisModeIncompatibility(workspace, report);

                source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-160f, 40f));
                resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(120f, 40f));
                ground = workspace.CreateComponent(SpiceComponentKind.Ground, new Vector2(0f, -140f));
                SpiceT3WorkspaceValidation.ConnectSingleResistor(workspace, source.InstanceId, resistor.InstanceId, ground.InstanceId);
                var rebuilt = await workspace.RunCalculationAsync();
                if (rebuilt == null || !rebuilt.Success) throw new InvalidOperationException("Rebuilt Player circuit failed before invalid-topology regression.");
                workspace.Model.RemoveWire(workspace.Model.Wires[0]);
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale) throw new InvalidOperationException("Player topology change did not stale the result.");
                var invalid = await workspace.RunCalculationAsync();
                if (invalid == null || invalid.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed) throw new InvalidOperationException("Invalid Player topology was incorrectly marked current.");

                report.unexpectedErrorSanitizationPassed = await ValidateUnexpectedErrorSanitization(workspace);
                if (!report.workspaceGeometryPassed || !report.gridGeometryPassed || !report.horizontalDraggingPassed ||
                    !report.verticalDraggingPassed || !report.zoomDraggingPassed || !report.toolbarLayoutPassed ||
                    !report.analysisModeTogglePassed || !report.acFrequencySettingsPassed || !report.d1StaleDiscardPassed || !report.d2ImportLimitsPassed ||
                    !report.d3ClearStatePassed || !report.d3NetlistRevisionPassed ||
                    !report.unexpectedErrorSanitizationPassed || !report.modeIncompatibilityDiagnosticPassed ||
                    !report.paletteBottomRowFullyVisible)
                    throw new InvalidOperationException("One or more stabilization Player checks did not pass.");
                report.success = true;
            }
            catch (Exception exception)
            {
                report.failure = exception.ToString();
                Debug.LogError("[SpiceT3] " + report.failure);
            }

            var path = ResolveResultPath();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("[Spice][T3] Player 验证报告：" + path);
            Application.Quit(report.success ? 0 : 1);
        }

        /// <summary>
        /// Player 中的预检必须在求解服务之前返回。这里通过受控回调计数确认没有进入仿真，
        /// 同时确认切换模式后遗留的交流源仍留在图纸上，结果状态和 revision 不被清空或推进。
        /// </summary>
        private static void ValidateAnalysisModeIncompatibility(SpiceWorkspaceController workspace, SpiceT3PlayerValidationReport report)
        {
            var simulationCalls = 0;
            workspace.SetSimulationOverrideForTesting((_, __) =>
            {
                simulationCalls++;
                return Task.FromResult(new SpiceSimulationResult { Success = true });
            });
            var acSource = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
            var revision = workspace.ElectricalRevisionForTesting;
            var dirty = workspace.IsDirty;
            workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
            try
            {
                if (workspace.RunCalculationAsync().GetAwaiter().GetResult() != null || simulationCalls != 0 ||
                    workspace.ElectricalRevisionForTesting != revision || workspace.IsDirty != dirty ||
                    workspace.ResultState != SpiceWorkspaceResultState.Current || workspace.Model.FindComponent(acSource.InstanceId) == null ||
                    !workspace.GetVisibleDiagnosticTextForTesting().Contains("交流电压源 " + acSource.InstanceId))
                    throw new InvalidOperationException("Player analysis-mode incompatibility did not block before simulation or preserve the drawing state.");
                report.modeIncompatibilityDiagnosticPassed = true;
                Debug.Log("[Spice][Player-UX] 模式不兼容器件诊断：通过");
            }
            finally
            {
                workspace.SetSimulationOverrideForTesting(null);
                workspace.ClearWorkspace();
            }
        }

        private static void ValidateWorkspaceUiControls(SpiceWorkspaceController workspace, SpiceT3PlayerValidationReport report)
        {
            var modeButton = workspace.GetAnalysisModeToggleButtonForTesting();
            var toolbar = modeButton != null ? modeButton.transform.parent as RectTransform : null;
            if (modeButton == null || toolbar == null || toolbar.Find("AnalysisControls") != null)
                throw new InvalidOperationException("Player 工作区未使用单一工具栏分析模式切换按钮。");
            var buttons = toolbar.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            for (var index = 0; index < buttons.Length; index++)
            {
                if (!buttons[index].gameObject.activeInHierarchy || !Contains(toolbar, buttons[index].GetComponent<RectTransform>()))
                    throw new InvalidOperationException("Player 工具栏按钮超出工具栏边界。");
                for (var next = index + 1; next < buttons.Length; next++)
                    if (buttons[next].gameObject.activeInHierarchy && Overlaps(buttons[index].GetComponent<RectTransform>(), buttons[next].GetComponent<RectTransform>()))
                        throw new InvalidOperationException("Player 工具栏按钮发生重叠。");
            }
            report.toolbarLayoutPassed = true;
            Debug.Log("[Spice][Player-UI] 工具栏无重叠：通过");

            var revision = workspace.ElectricalRevisionForTesting;
            modeButton.onClick.Invoke();
            if (workspace.Model.AnalysisMode != SpiceAnalysisMode.AcSingleFrequency || workspace.ElectricalRevisionForTesting != revision + 1 ||
                modeButton.GetComponentInChildren<UnityEngine.UI.Text>().text != "分析：单频 AC")
                throw new InvalidOperationException("Player 分析模式切换没有走正式 Controller 路径。");
            report.analysisModeTogglePassed = true;
            Debug.Log("[Spice][Player-UI] 分析模式切换：通过");

            var source = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
            var dialogObject = new GameObject("SpicePlayerAcParameterDialog", typeof(RectTransform), typeof(SpiceComponentParameterDialog));
            dialogObject.transform.SetParent(workspace.OverlayLayer, false);
            var dialog = dialogObject.GetComponent<SpiceComponentParameterDialog>();
            dialog.Initialize(workspace.OverlayLayer, workspace);
            dialog.Open(source);
            if (!dialog.IsEditingAcVoltageSourceForTesting || !dialog.GetMagnitudeInputForTesting().interactable ||
                !dialog.GetPhaseInputForTesting().gameObject.activeSelf || !dialog.GetFrequencyInputForTesting().gameObject.activeSelf)
                throw new InvalidOperationException("Player 交流源参数弹窗未提供频率和相位设置。");
            dialog.GetMagnitudeInputForTesting().text = "2";
            dialog.GetPhaseInputForTesting().text = "30";
            dialog.GetFrequencyInputForTesting().text = "2000";
            dialog.GetApplyButtonForTesting().onClick.Invoke();
            if (source.SiValue != 2d || source.AcPhaseDegrees != 30d || Math.Abs(workspace.Model.AcFrequencyHz - 2000d) > 1e-9d)
                throw new InvalidOperationException("Player 交流源参数弹窗没有通过正式 Controller 路径提交设置。");
            report.acFrequencySettingsPassed = true;
            Debug.Log("[Spice][Player-UI] 交流源参数设置：通过");
            dialog.Dispose();
            Destroy(dialogObject);
            workspace.ClearWorkspace();
            modeButton.onClick.Invoke();
            if (workspace.Model.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint)
                throw new InvalidOperationException("Player 切回 DC 后分析模式不正确。");
        }

        /// <summary>
        /// Player 中记录一次实际屏幕、Canvas 和工作区尺寸，并用 Controller 的同一几何契约验证
        /// Content 为 Viewport 三倍且所有图层共享 Content 尺寸。正式 Release 不会持续输出这些验证日志。
        /// </summary>
        private static void ValidateWorkspaceGeometry(SpiceWorkspaceController workspace, SpiceT3PlayerValidationReport report)
        {
            if (!workspace.ValidateWorkspaceGeometryForTesting(out var error))
                throw new InvalidOperationException("Player 工作区几何验证失败：" + error + " " + workspace.GetWorkspaceGeometryDiagnosticsForTesting());
            report.workspaceGeometryPassed = true;
            report.gridGeometryPassed = true;
            Debug.Log("[Spice][Player-UI] 工作区几何初始化：通过 " + workspace.GetWorkspaceGeometryDiagnosticsForTesting());
            Debug.Log("[Spice][Player-UI] 网格几何：通过");
            Debug.Log("[Spice][Player-UI] 工作区激活后布局：通过");
            Debug.Log("[Spice][Player-UI] 网格显示契约：通过");
        }

        /// <summary>
        /// 真实 Player 中在最终 Canvas/ScrollRect 布局完成后验证元件池最后一排。
        /// 仅以 Viewport 的实际可见区域判断，避免用理论 Content 高度掩盖底部裁切。
        /// </summary>
        private static void ValidatePaletteBottomRowGeometry(SpiceWorkspaceViewBindings bindings, SpiceT3PlayerValidationReport report)
        {
            if (bindings == null)
                throw new InvalidOperationException("Player 元件池验证缺少 SpiceWorkspaceViewBindings。");

            var scroll = bindings.PaletteRoot.Find("PaletteScroll")?.GetComponent<ScrollRect>();
            var viewport = scroll?.viewport;
            var content = scroll?.content;
            var npn = content?.Find(SpiceComponentKind.GenericNpnBjt + "Card") as RectTransform;
            var pnp = content?.Find(SpiceComponentKind.GenericPnpBjt + "Card") as RectTransform;
            if (scroll == null || viewport == null || content == null || npn == null || pnp == null)
                throw new InvalidOperationException("Player 元件池滚动层级或 BJT 卡片不完整。");

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
            scroll.Rebuild(CanvasUpdate.PostLayout);
            scroll.verticalNormalizedPosition = 0f;
            Canvas.ForceUpdateCanvases();
            scroll.Rebuild(CanvasUpdate.PostLayout);

            const float safeMargin = 8f;
            var viewportBounds = ViewportVerticalBounds(viewport, viewport);
            var npnBounds = ViewportVerticalBounds(npn, viewport);
            var pnpBounds = ViewportVerticalBounds(pnp, viewport);
            var visibleHeight = Mathf.Max(0f, Mathf.Min(pnpBounds.top, viewportBounds.top) - Mathf.Max(pnpBounds.bottom, viewportBounds.bottom));
            var fullyVisible = pnpBounds.bottom >= viewportBounds.bottom + safeMargin && pnpBounds.top <= viewportBounds.top + 0.1f;
            var canvas = bindings.PaletteRoot.GetComponentInParent<Canvas>();
            report.paletteViewportHeight = viewport.rect.height;
            report.paletteContentHeight = content.rect.height;
            report.paletteMaxScroll = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            report.paletteBottomPosition = content.anchoredPosition.y;
            report.pnpVisibleHeight = visibleHeight;
            report.paletteBottomRowFullyVisible = fullyVisible;

            Debug.Log($"PALETTE_SCREEN={Screen.width}x{Screen.height}; PALETTE_CANVAS_SCALE={canvas?.scaleFactor ?? 0f:F3}; " +
                      $"PALETTE_ROOT_HEIGHT={bindings.PaletteRoot.rect.height:F3}; PALETTE_SCROLL_HEIGHT={(scroll.transform as RectTransform).rect.height:F3}; " +
                      $"PALETTE_VIEWPORT_HEIGHT={report.paletteViewportHeight:F3}; PALETTE_CONTENT_HEIGHT={report.paletteContentHeight:F3}; " +
                      $"PALETTE_MAX_SCROLL={report.paletteMaxScroll:F3}; PALETTE_BOTTOM_POSITION={report.paletteBottomPosition:F3}; " +
                      $"PALETTE_NORMALIZED={scroll.verticalNormalizedPosition:F3}; " +
                      $"NPN_BOTTOM={npnBounds.bottom:F3}; NPN_TOP={npnBounds.top:F3}; PNP_BOTTOM={pnpBounds.bottom:F3}; PNP_TOP={pnpBounds.top:F3}; " +
                      $"VIEWPORT_BOTTOM={viewportBounds.bottom:F3}; VIEWPORT_TOP={viewportBounds.top:F3}; " +
                      $"PNP_VISIBLE_HEIGHT={visibleHeight:F3}; PNP_FULLY_VISIBLE={fullyVisible}");

            if (!fullyVisible)
                throw new InvalidOperationException("Player 元件池滚至底部后 PNP 卡片未完整进入 Viewport 安全区。");
        }

        private static (float bottom, float top) ViewportVerticalBounds(RectTransform rect, RectTransform viewport)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var bottom = float.MaxValue;
            var top = float.MinValue;
            for (var index = 0; index < corners.Length; index++)
            {
                var local = viewport.InverseTransformPoint(corners[index]);
                bottom = Mathf.Min(bottom, local.y);
                top = Mathf.Max(top, local.y);
            }

            return (bottom, top);
        }

        /// <summary>
        /// 通过正式 ComponentView 指针回调覆盖 Screen→Workspace local→Clamp→Controller→Model→Wire 刷新链。
        /// 不直接写 Model.Position，确保纵向位移不会被错误 Content 边界压成零或固定值。
        /// </summary>
        private static void ValidateComponentDragging(SpiceWorkspaceController workspace, SpiceWorkspaceComponentData component, SpiceT3PlayerValidationReport report)
        {
            var view = workspace.GetComponentViewForTesting(component.InstanceId);
            if (view == null || EventSystem.current == null)
                throw new InvalidOperationException("Player 拖动验证缺少元件视图或 EventSystem。");

            var initial = component.Position;
            DragView(view, new Vector2(200f, 0f));
            var afterHorizontal = component.Position;
            if (afterHorizontal.x - initial.x < 100f || Mathf.Abs(afterHorizontal.y - initial.y) > 1f)
                throw new InvalidOperationException("Player 横向拖动未沿正式路径更新模型位置。");
            report.horizontalDraggingPassed = true;
            Debug.Log("[Spice][Player-UI] 元件横向拖动：通过");

            DragView(view, new Vector2(0f, 200f));
            var afterVertical = component.Position;
            if (afterVertical.y - afterHorizontal.y < 100f || Mathf.Abs(afterVertical.y) >= workspace.WorkspaceRect.rect.height * 0.5f)
                throw new InvalidOperationException("Player 纵向拖动被无效工作区边界压缩。");
            report.verticalDraggingPassed = true;
            Debug.Log("[Spice][Player-UI] 元件纵向拖动：通过");

            workspace.ViewController.ZoomIn();
            DragView(view, new Vector2(-160f, -160f));
            var afterZoom = component.Position;
            if (Mathf.Abs(afterZoom.x - afterVertical.x) < 50f || Mathf.Abs(afterZoom.y - afterVertical.y) < 50f)
                throw new InvalidOperationException("Player 缩放后拖动未更新两个坐标轴。");
            report.zoomDraggingPassed = true;
            Debug.Log("[Spice][Player-UI] 缩放后拖动：通过");
            Debug.Log("[Spice][Player-UI] 横纵拖动：通过");
        }

        private static void DragView(SpiceWorkspaceComponentView view, Vector2 screenDelta)
        {
            var start = RectTransformUtility.WorldToScreenPoint(null, view.transform.position);
            var eventData = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = start,
                pressPosition = start
            };
            view.OnBeginDrag(eventData);
            eventData.position = start + screenDelta;
            view.OnDrag(eventData);
            view.OnEndDrag(eventData);
        }

        private static bool Contains(RectTransform outer, RectTransform inner)
        {
            GetBounds(outer, out var outerMin, out var outerMax);
            GetBounds(inner, out var innerMin, out var innerMax);
            return innerMin.x >= outerMin.x - 0.1f && innerMax.x <= outerMax.x + 0.1f &&
                innerMin.y >= outerMin.y - 0.1f && innerMax.y <= outerMax.y + 0.1f;
        }

        private static bool Overlaps(RectTransform left, RectTransform right)
        {
            GetBounds(left, out var leftMin, out var leftMax);
            GetBounds(right, out var rightMin, out var rightMax);
            return leftMin.x < rightMax.x && leftMax.x > rightMin.x && leftMin.y < rightMax.y && leftMax.y > rightMin.y;
        }

        private static void GetBounds(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            min = corners[0];
            max = corners[2];
        }

        private static bool ValidateD2PathImportLimit(SpiceWorkspaceController workspace)
        {
            var path = Path.Combine(Application.persistentDataPath, "SpiceT3_D2_ImportLimit_" + Guid.NewGuid().ToString("N") + ".spicejson");
            try
            {
                var oversized = new SpiceWorkspaceModel();
                for (var index = 0; index <= SpiceDrawingLimits.MaxComponents; index++)
                    oversized.AddComponent(SpiceComponentKind.Resistor, new Vector2(index % 20, index / 20));
                File.WriteAllText(path, SpiceDrawingSerializer.ToJson(oversized));
                if (new FileInfo(path).Length >= SpiceDrawingLimits.MaxFileBytes)
                    throw new InvalidOperationException("D2 Player fixture unexpectedly exceeded the file-size limit.");

                var modelBefore = workspace.Model;
                var componentCountBefore = workspace.Model.Components.Count;
                var wireCountBefore = workspace.Model.Wires.Count;
                var pathBefore = workspace.CurrentSpiceFilePath;
                if (workspace.TryImportWorkspaceFromPath(path, out var error))
                    throw new InvalidOperationException("D2 Player path import accepted an over-limit drawing.");
                if (string.IsNullOrEmpty(error) || !error.Contains("器件数量"))
                    throw new InvalidOperationException("D2 Player path import returned an unstable error message: " + error);
                if (!ReferenceEquals(modelBefore, workspace.Model) ||
                    workspace.Model.Components.Count != componentCountBefore ||
                    workspace.Model.Wires.Count != wireCountBefore ||
                    workspace.CurrentSpiceFilePath != pathBefore)
                    throw new InvalidOperationException("D2 Player rejected import changed the workspace or current path.");
                return true;
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static async Task<bool> ValidateUnexpectedErrorSanitization(SpiceWorkspaceController workspace)
        {
            const string secretPath = @"C:\Users\TestUser\Secret\solver.tmp";
            workspace.SetSimulationOverrideForTesting((_, __) =>
                Task.FromException<SpiceSimulationResult>(
                    new NullReferenceException("Unexpected Player solver failure at " + secretPath)));
            try
            {
                var result = await workspace.RunCalculationAsync();
                if (result != null || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("Unexpected Player error did not enter Failed state.");
                if (!workspace.TryGetCopyableOutcomeText(out var text) ||
                    !text.Contains("SPICE_RUNTIME_UNEXPECTED") ||
                    text.Contains(secretPath) || text.Contains("NullReferenceException"))
                    throw new InvalidOperationException("Unexpected Player error leaked internal details to the result.");
                return true;
            }
            finally
            {
                workspace.SetSimulationOverrideForTesting(null);
            }
        }

        private static async Task ValidateD1StaleResultDiscard(SpiceWorkspaceController workspace, SpiceWorkspaceComponentData resistor)
        {
            var completion = new TaskCompletionSource<SpiceSimulationResult>();
            workspace.SetSimulationOverrideForTesting((_, __) => completion.Task);
            try
            {
                var calculation = workspace.RunCalculationAsync();
                if (workspace.ResultState != SpiceWorkspaceResultState.Running)
                    throw new InvalidOperationException("D1 delayed calculation did not enter Running state.");
                if (workspace.TrySetParameter(resistor.InstanceId, 3d, "kOhm"))
                    throw new InvalidOperationException("D1 rejected parameter mutation was accepted while Running.");
                if (workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.zero) != null)
                    throw new InvalidOperationException("D1 rejected component creation was accepted while Running.");

                // 生产防线会拒绝 UI 修改。此处受控的 Model 修改用于证明：
                // 即使未来路径绕过第一层，第二层仍会丢弃过期请求。
                if (!workspace.Model.TrySetParameter(resistor.InstanceId, 4000d))
                    throw new InvalidOperationException("D1 controlled model mutation failed.");

                completion.SetResult(new SpiceSimulationResult
                {
                    Success = true,
                    GeneratedNetlistContent = "* D1 delayed result"
                });
                var stale = await calculation;
                if (stale != null || workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("D1 outdated calculation result was not discarded.");
            }
            finally
            {
                workspace.SetSimulationOverrideForTesting(null);
            }
        }

        private static string ResolveResultPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                const string prefix = "--spice-t3-result=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return argument.Substring(prefix.Length).Trim('"');
            }
            return Path.Combine(Application.persistentDataPath, "SpiceT3PlayerResult.json");
        }
    }
}
