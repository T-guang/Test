using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// T3 工作区自动验证。partial 文件按功能分组，RunPureChecks 保持唯一且明确的回归执行顺序。
    /// </summary>
    public static partial class SpiceT3WorkspaceValidation
    {
        private static void ValidateOutcomePresentationDiagnostics()
        {
            var canvasRoot = new GameObject("SpiceOutcomePresentationValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var result = CreateScrollableTextForValidation(canvasRoot.transform, "Result", new Vector2(300f, 180f));
                var diagnosticSinkRoot = new GameObject("DiagnosticSink", typeof(RectTransform));
                diagnosticSinkRoot.transform.SetParent(canvasRoot.transform, false);
                var diagnosticText = new GameObject("DiagnosticText", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
                diagnosticText.transform.SetParent(diagnosticSinkRoot.transform, false);
                diagnosticText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var runButton = CreateButton(canvasRoot.transform);
                var presentation = result.ScrollRect.gameObject.AddComponent<SpiceAssistantOutcomePresentation>();
                presentation.Initialize(result.Text, diagnosticText, runButton);
                diagnosticSinkRoot.SetActive(false);

                var diagnostics = string.Join("\n", new string[30].Select((_, index) => "存在悬空端子：error-" + index));
                diagnosticText.text = diagnostics;
                presentation.RefreshNow();
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(result.Content);
                if (result.Text.text != diagnostics || result.Text.color != MainUiTheme.DangerRed)
                    throw new InvalidOperationException("SPICE outcome presentation did not show hidden blocking diagnostics in the formal result panel.");
                if (result.Content.rect.height <= result.Viewport.rect.height)
                    throw new InvalidOperationException("SPICE outcome presentation did not make long blocking diagnostics scrollable.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateFailedRunOutcomePresentation()
        {
            var canvasRoot = new GameObject("SpiceFailedOutcomeValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var spiceRoot = CreateRoot(canvasRoot.transform);
                spiceRoot.SetActive(false);
                var bindings = spiceRoot.AddComponent<SpiceWorkspaceViewBindings>();
                var workspace = spiceRoot.AddComponent<SpiceWorkspaceController>();
                var palette = CreateRect(spiceRoot.transform);
                var viewport = CreateRect(spiceRoot.transform);
                var wires = CreateRect(viewport);
                var components = CreateRect(viewport);
                var overlay = CreateRect(viewport);
                var assistant = CreateRect(spiceRoot.transform);
                var parameters = CreateRect(assistant);
                var results = CreateRect(assistant);
                var netlist = CreateRect(assistant);
                var diagnostics = CreateRect(assistant);
                var runButton = CreateButton(canvasRoot.transform);
                bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                    runButton, CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform));

                var hostRoot = CreateRoot(canvasRoot.transform);
                hostRoot.SetActive(false);
                var host = hostRoot.AddComponent<SpiceWorkspaceDemoHost>();
                host.Configure(bindings, workspace);
                host.Initialize();
                spiceRoot.SetActive(true);
                hostRoot.SetActive(true);

                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var failed = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (failed == null || failed.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("The invalid SPICE circuit did not enter the failed result state.");

                var resultText = bindings.ResultRoot.Find("ResultScrollView/Viewport/Content/ResultText")?.GetComponent<Text>();
                var outcome = bindings.ResultRoot.GetComponent<SpiceAssistantOutcomePresentation>();
                if (resultText == null || outcome == null)
                    throw new InvalidOperationException("The formal SPICE result presentation was not initialized.");
                outcome.RefreshNow();
                if (!resultText.text.Contains("SPICE_GROUND_MISSING") || !resultText.text.Contains("SPICE_FLOATING_TERMINAL") || resultText.color != MainUiTheme.DangerRed)
                {
                    var diagnosticText = bindings.DiagnosticRoot.Find("DiagnosticScrollView/Viewport/Content/DiagnosticText")?.GetComponent<Text>();
                    throw new InvalidOperationException("Blocking SPICE diagnostics were replaced by the empty-result placeholder instead of appearing in the formal result panel. Sink='" +
                        (diagnosticText == null ? "<missing>" : diagnosticText.text) + "' Result='" + resultText.text + "'.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static SpiceWorkspaceController CreateInitializedWorkspaceForCopy(Transform parent, out SpiceWorkspaceViewBindings bindings)
        {
            var spiceRoot = CreateRoot(parent);
            spiceRoot.SetActive(false);
            bindings = spiceRoot.AddComponent<SpiceWorkspaceViewBindings>();
            var workspace = spiceRoot.AddComponent<SpiceWorkspaceController>();
            var palette = CreateRect(spiceRoot.transform);
            var viewport = CreateRect(spiceRoot.transform);
            var wires = CreateRect(viewport);
            var components = CreateRect(viewport);
            var overlay = CreateRect(viewport);
            var assistant = CreateRect(spiceRoot.transform);
            var parameters = CreateRect(assistant);
            var results = CreateRect(assistant);
            var netlist = CreateRect(assistant);
            var diagnostics = CreateRect(assistant);
            bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                CreateButton(parent), CreateButton(parent), CreateButton(parent), CreateButton(parent));

            var hostRoot = CreateRoot(parent);
            hostRoot.SetActive(false);
            var host = hostRoot.AddComponent<SpiceWorkspaceDemoHost>();
            host.Configure(bindings, workspace);
            host.Initialize();
            spiceRoot.SetActive(true);
            hostRoot.SetActive(true);
            return workspace;
        }

        private static void ValidateCopyableOutcomeFailedState()
        {
            var canvasRoot = new GameObject("SpiceCopyFailedValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var failed = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (failed == null || failed.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("The invalid SPICE circuit did not enter the failed result state for copy validation.");

                if (!workspace.TryGetCopyableOutcomeText(out var copyText))
                    throw new InvalidOperationException("SPICE blocking diagnostics were not eligible for copy.");
                if (!copyText.Contains("SPICE_GROUND_MISSING") && !copyText.Contains("SPICE_FLOATING_TERMINAL"))
                    throw new InvalidOperationException("SPICE copyable outcome text does not contain blocking diagnostics.");

                var copyButton = bindings.ResultRoot.Find("ResultHeader/CopyResult")?.GetComponent<Button>();
                if (copyButton == null)
                    throw new InvalidOperationException("SPICE copy result button was not created.");
                if (!copyButton.interactable)
                    throw new InvalidOperationException("SPICE copy result button was not interactable in the failed state.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // NeverRun / Failed / Clear 状态的复制资格和按钮交互验证。
        // Running / Stale 状态需要 ngspice 求解或 Current 前置，在 batchmode 中无法同步等待
        // （RunCalculationAsync 的 await 会捕获 UnitySynchronizationContext 导致死锁）。
        // Running 路径在 RunCalculationAsync 入口即设 lastOutcomeText = null（L324），
        // Stale 路径在 HandleModelChanged 中设 lastOutcomeText = null（L905），不可复制由构造保证。
        private static void ValidateCopyableOutcomeNonCopyableStates()
        {
            var canvasRoot = new GameObject("SpiceCopyNonCopyableValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                var copyButton = bindings.ResultRoot.Find("ResultHeader/CopyResult")?.GetComponent<Button>();
                if (copyButton == null)
                    throw new InvalidOperationException("SPICE copy result button was not created.");

                // NeverRun：刚初始化，未运行，不可复制
                if (workspace.TryGetCopyableOutcomeText(out _))
                    throw new InvalidOperationException("SPICE workspace was copyable before any calculation ran.");
                if (copyButton.interactable)
                    throw new InvalidOperationException("SPICE copy result button was interactable before any calculation ran.");

                // 运行无效电路进入 Failed，阻断诊断可复制
                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var failed = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (failed == null || failed.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("The invalid SPICE circuit did not enter the failed result state for non-copyable validation.");
                if (!workspace.TryGetCopyableOutcomeText(out _))
                    throw new InvalidOperationException("SPICE failed result was not eligible for copy.");
                if (!copyButton.interactable)
                    throw new InvalidOperationException("SPICE copy result button was not interactable in the failed state.");

                // 修改电路后，上一轮阻断诊断不再代表当前电路，必须失效且禁止复制。
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.up * 80f);
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("SPICE failed diagnostics did not become stale after the circuit changed.");
                if (workspace.TryGetCopyableOutcomeText(out _))
                    throw new InvalidOperationException("SPICE stale diagnostics were still eligible for copy.");
                if (copyButton.interactable)
                    throw new InvalidOperationException("SPICE copy result button stayed interactable after diagnostics became stale.");

                // Clear：清空后不可复制
                workspace.ClearWorkspace();
                if (workspace.TryGetCopyableOutcomeText(out _))
                    throw new InvalidOperationException("SPICE cleared workspace was eligible for copy.");
                if (copyButton.interactable)
                    throw new InvalidOperationException("SPICE copy result button was interactable after clearing the workspace.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateCopyEligibilityDoesNotMutate()
        {
            var canvasRoot = new GameObject("SpiceCopyNoMutateValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var failed = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (failed == null || failed.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("The invalid SPICE circuit did not enter the failed result state for mutation validation.");

                var componentCount = workspace.Model.Components.Count;
                var wireCount = workspace.Model.Wires.Count;
                var resultState = workspace.ResultState;

                for (var index = 0; index < 5; index++)
                {
                    if (!workspace.TryGetCopyableOutcomeText(out _))
                        throw new InvalidOperationException("SPICE copy eligibility check returned false during repeated calls.");
                }

                if (workspace.Model.Components.Count != componentCount || workspace.Model.Wires.Count != wireCount || workspace.ResultState != resultState)
                    throw new InvalidOperationException("SPICE copy eligibility check mutated the workspace model, wires, or result state.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateClearWorkspaceSimulationPresentationState()
        {
            var canvasRoot = new GameObject("SpiceD3ClearStateValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                workspace.SetSimulationOverrideForTesting((_, __) =>
                    System.Threading.Tasks.Task.FromResult(CreateD3SuccessfulResult(resistor.InstanceId, "R1 n001 0 1000")));

                var result = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (result == null || !result.Success || workspace.ResultState != SpiceWorkspaceResultState.Current)
                    throw new InvalidOperationException("D3 清空验证无法建立 Current 结果状态。");
                if (!workspace.TryGetCopyableOutcomeText(out _) || !workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("D3 清空验证的当前结果或网表未进入可复制状态。");

                workspace.ClearWorkspace();

                var resultText = bindings.ResultRoot.Find("ResultScrollView/Viewport/Content/ResultText")?.GetComponent<Text>();
                var diagnosticText = bindings.DiagnosticRoot.Find("DiagnosticScrollView/Viewport/Content/DiagnosticText")?.GetComponent<Text>();
                var copyResult = bindings.ResultRoot.Find("ResultHeader/CopyResult")?.GetComponent<Button>();
                var copyNetlist = bindings.NetlistRoot.Find("NetlistHeader/Copy")?.GetComponent<Button>();
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun ||
                    workspace.Model.Components.Count != 0 || workspace.Model.Wires.Count != 0)
                    throw new InvalidOperationException("ClearWorkspace 未恢复 NeverRun 空画布状态。");
                if (resultText == null || resultText.text.Contains("D3_RESULT_MARKER") ||
                    diagnosticText == null || !string.IsNullOrEmpty(diagnosticText.text))
                    throw new InvalidOperationException("ClearWorkspace 后仍残留旧结果或诊断文本。");
                if (workspace.TryGetCopyableOutcomeText(out _) || workspace.TryGetCopyableNetlistText(out _) ||
                    copyResult == null || copyResult.interactable || copyNetlist == null || copyNetlist.interactable)
                    throw new InvalidOperationException("ClearWorkspace 后结果或网表仍可复制。");
                if (workspace.HasCurrentSpiceFilePath)
                    throw new InvalidOperationException("ClearWorkspace 后仍保留当前 SPICE 文件路径。");

                var recreated = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                if (recreated == null || recreated.InstanceId != "resistor-001")
                    throw new InvalidOperationException("ClearWorkspace 后器件编号未从 1 重新开始。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }

            var failedRoot = new GameObject("SpiceD3ClearFailedValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(failedRoot.transform, out var bindings);
                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var failed = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (failed == null || failed.Success || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("D3 清空验证无法建立 Failed 诊断状态。");

                workspace.ClearWorkspace();
                var resultText = bindings.ResultRoot.Find("ResultScrollView/Viewport/Content/ResultText")?.GetComponent<Text>();
                var diagnosticText = bindings.DiagnosticRoot.Find("DiagnosticScrollView/Viewport/Content/DiagnosticText")?.GetComponent<Text>();
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun ||
                    workspace.TryGetCopyableOutcomeText(out _) ||
                    resultText == null || resultText.text.Contains("SPICE_GROUND_MISSING") ||
                    diagnosticText == null || !string.IsNullOrEmpty(diagnosticText.text))
                    throw new InvalidOperationException("清空 Failed 工作区后仍残留阻断诊断。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(failedRoot);
            }
        }

        private static void ValidateStaleNetlistCannotBeCopied()
        {
            var canvasRoot = new GameObject("SpiceD3NetlistRevisionValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 100f);
                var switchData = workspace.CreateComponent(SpiceComponentKind.IdealSwitch, Vector2.up * 100f);
                workspace.SetSimulationOverrideForTesting((_, __) =>
                    System.Threading.Tasks.Task.FromResult(CreateD3SuccessfulResult(resistor.InstanceId, "R1 n001 0 1000")));
                workspace.RunCalculationAsync().GetAwaiter().GetResult();

                var copyButton = bindings.NetlistRoot.Find("NetlistHeader/Copy")?.GetComponent<Button>();
                var netlistText = bindings.NetlistRoot.Find("NetlistScrollView/Viewport/Content/NetlistText")?.GetComponent<Text>();
                var netlistStatus = bindings.NetlistRoot.Find("NetlistHeader/Status")?.GetComponent<Text>();
                if (!workspace.TryGetCopyableNetlistText(out var originalNetlist) ||
                    originalNetlist != "R1 n001 0 1000" || copyButton == null || !copyButton.interactable)
                    throw new InvalidOperationException("当前电气修订的网表未进入可复制状态。");

                GUIUtility.systemCopyBuffer = "D3_CLIPBOARD_SENTINEL";
                if (!workspace.TrySetParameter(resistor.InstanceId, 2d, "kOhm"))
                    throw new InvalidOperationException("D3 网表修订验证无法修改电阻参数。");
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale ||
                    workspace.TryGetCopyableNetlistText(out _) || copyButton.interactable)
                    throw new InvalidOperationException("参数变化后旧网表仍可复制。");
                copyButton.onClick.Invoke();
                if (GUIUtility.systemCopyBuffer != "D3_CLIPBOARD_SENTINEL")
                    throw new InvalidOperationException("旧网表的点击处理器绕过了修订资格检查。");
                if (netlistText == null || netlistText.text != originalNetlist ||
                    netlistStatus == null || !netlistStatus.text.Contains("已过期"))
                    throw new InvalidOperationException("旧网表未按既有行为保留显示并明确标记过期。");

                workspace.MoveComponent(resistor.InstanceId, Vector2.right * 50f);
                var view = workspace.GetComponentViewForTesting(resistor.InstanceId);
                workspace.SelectComponent(view);
                workspace.RotateSelectedComponent();
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("纯视觉移动或旋转不应改变既有 Stale 状态。");

                workspace.SetSimulationOverrideForTesting((_, __) =>
                    System.Threading.Tasks.Task.FromResult(CreateD3SuccessfulResult(resistor.InstanceId, "R1 n001 0 2000")));
                workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (!workspace.TryGetCopyableNetlistText(out var updatedNetlist) ||
                    updatedNetlist != "R1 n001 0 2000" || !copyButton.interactable)
                    throw new InvalidOperationException("重新运行后当前修订网表未恢复可复制状态。");

                workspace.MoveComponent(resistor.InstanceId, Vector2.right * 75f);
                workspace.SelectComponent(workspace.GetComponentViewForTesting(resistor.InstanceId));
                workspace.RotateSelectedComponent();
                if (workspace.ResultState != SpiceWorkspaceResultState.Current ||
                    !workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("纯视觉移动或旋转不应使当前网表过期。");

                if (!workspace.TrySetSwitchState(switchData.InstanceId, true) ||
                    workspace.ResultState != SpiceWorkspaceResultState.Stale ||
                    workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("切换开关后旧网表仍可复制。");

                workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (!workspace.Connect(source.InstanceId, "positive", resistor.InstanceId, "positive") ||
                    workspace.ResultState != SpiceWorkspaceResultState.Stale ||
                    workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("新增 Wire 后旧网表仍可复制。");

                workspace.RunCalculationAsync().GetAwaiter().GetResult();
                var wire = workspace.Model.Wires.First();
                if (!workspace.Model.RemoveWire(wire) ||
                    workspace.ResultState != SpiceWorkspaceResultState.Stale ||
                    workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("删除 Wire 后旧网表仍可复制。");

                workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.one * 120f) == null ||
                    workspace.ResultState != SpiceWorkspaceResultState.Stale ||
                    workspace.TryGetCopyableNetlistText(out _))
                    throw new InvalidOperationException("新增器件后旧网表仍可复制。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateImportResetsSimulationPresentationState()
        {
            var canvasRoot = new GameObject("SpiceD3ImportStateValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                workspace.SetSimulationOverrideForTesting((_, __) =>
                    System.Threading.Tasks.Task.FromResult(CreateD3SuccessfulResult(resistor.InstanceId, "R1 n001 0 1000")));
                workspace.RunCalculationAsync().GetAwaiter().GetResult();

                var imported = new SpiceWorkspaceModel();
                imported.AddComponentWithIdentity(SpiceComponentKind.Capacitor, "capacitor-001", Vector2.one * 20f, 1e-6d, 0);
                imported.RestoreInstanceNumbersFromExisting();
                if (!workspace.TryImportDrawingJson(SpiceDrawingSerializer.ToJson(imported), out var error))
                    throw new InvalidOperationException("D3 导入状态验证失败：" + error);

                var resultText = bindings.ResultRoot.Find("ResultScrollView/Viewport/Content/ResultText")?.GetComponent<Text>();
                var diagnosticText = bindings.DiagnosticRoot.Find("DiagnosticScrollView/Viewport/Content/DiagnosticText")?.GetComponent<Text>();
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun ||
                    workspace.TryGetCopyableOutcomeText(out _) || workspace.TryGetCopyableNetlistText(out _) ||
                    resultText == null || resultText.text.Contains("D3_RESULT_MARKER") ||
                    diagnosticText == null || !string.IsNullOrEmpty(diagnosticText.text))
                    throw new InvalidOperationException("成功导入后旧结果、诊断或网表状态未清理。");

                var stateBeforeFailure = workspace.ResultState;
                var modelBeforeFailure = workspace.Model;
                if (workspace.TryImportDrawingJson("{\"format\":\"broken\"}", out _))
                    throw new InvalidOperationException("D3 导入状态验证的损坏 JSON 不应成功。");
                if (!ReferenceEquals(modelBeforeFailure, workspace.Model) || workspace.ResultState != stateBeforeFailure)
                    throw new InvalidOperationException("失败导入改变了当前模型或结果状态。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static SpiceSimulationResult CreateD3SuccessfulResult(string componentId, string netlist)
        {
            var result = new SpiceSimulationResult
            {
                Success = true,
                GeneratedNetlistContent = netlist
            };
            result.ComponentResults[componentId] = new SpiceComponentResult
            {
                ComponentId = componentId,
                ComponentKind = "Resistor",
                Voltage = 1d,
                Current = 0.001d,
                VoltageDirection = "positive-to-negative",
                CurrentDirection = "positive-to-negative",
                Notes = "D3_RESULT_MARKER"
            };
            return result;
        }

        private static void ValidateUnexpectedSimulationErrorIsSanitized()
        {
            const string secretPath = @"C:\Users\TestUser\Secret\solver.tmp";
            var canvasRoot = new GameObject("SpiceD31UnexpectedErrorValidation", typeof(RectTransform), typeof(Canvas));
            var technicalLogObserved = false;
            Application.LogCallback logCallback = (condition, stackTrace, type) =>
            {
                if (type == LogType.Exception &&
                    ((condition != null && condition.Contains(secretPath)) ||
                     (stackTrace != null && stackTrace.Contains(secretPath))))
                {
                    technicalLogObserved = true;
                }
            };

            Application.logMessageReceived += logCallback;
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out var bindings);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                workspace.SetSimulationOverrideForTesting((_, __) =>
                    System.Threading.Tasks.Task.FromException<SpiceSimulationResult>(
                        new NullReferenceException("Unexpected solver failure at " + secretPath)));

                var result = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (result != null || workspace.ResultState != SpiceWorkspaceResultState.Failed)
                    throw new InvalidOperationException("未预期异常未进入 Failed 状态。");
                if (!workspace.TryGetCopyableOutcomeText(out var copyText))
                    throw new InvalidOperationException("安全异常提示未作为正式失败结果提供。");
                if (!copyText.Contains("SPICE_RUNTIME_UNEXPECTED") ||
                    copyText.Contains(secretPath) || copyText.Contains("NullReferenceException"))
                    throw new InvalidOperationException("可复制结果泄露了内部异常路径或类型。");

                var presentation = bindings.ResultRoot.GetComponent<SpiceAssistantOutcomePresentation>();
                presentation?.RefreshNow();
                var resultText = bindings.ResultRoot.Find("ResultScrollView/Viewport/Content/ResultText")?.GetComponent<Text>();
                var diagnosticText = bindings.DiagnosticRoot.Find("DiagnosticScrollView/Viewport/Content/DiagnosticText")?.GetComponent<Text>();
                if (resultText == null || !resultText.text.Contains("SPICE_RUNTIME_UNEXPECTED") ||
                    resultText.text.Contains(secretPath) || resultText.text.Contains("NullReferenceException") ||
                    diagnosticText == null || diagnosticText.text.Contains(secretPath) ||
                    diagnosticText.text.Contains("NullReferenceException"))
                    throw new InvalidOperationException("正式结果或诊断区域泄露了内部异常详情。");
                if (!technicalLogObserved)
                    throw new InvalidOperationException("开发日志未保留未预期异常的完整技术信息。");
            }
            finally
            {
                Application.logMessageReceived -= logCallback;
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        public static void ConnectSingleResistor(SpiceWorkspaceController workspace, string source, string resistor, string ground)
        {
            if (!workspace.Connect(source, "positive", resistor, "positive") ||
                !workspace.Connect(source, "negative", ground, "ground") ||
                !workspace.Connect(resistor, "negative", ground, "ground")) throw new InvalidOperationException("Unable to construct the single-resistor T3 topology.");
        }
    }
}
