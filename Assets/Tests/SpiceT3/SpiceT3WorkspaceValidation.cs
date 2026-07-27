using System;
using System.IO;
using System.Linq;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    public static class SpiceT3WorkspaceValidation
    {
        public static void RunPureChecks()
        {
            ValidateHostBindings();
            ValidateHiddenDemoHostInitialization();
            ValidateSimulationModeSwitching();
            ValidateScrollableTextLayout();
            ValidateOutcomePresentationDiagnostics();
            ValidateFailedRunOutcomePresentation();
            ValidateInstanceNamingReset();
            // 复制结果验证：在 Failed 状态下验证复制资格、文本正确性、按钮交互状态和非变性。
            // Current 状态需要 ngspice 求解，在 batchmode 中 RunCalculationAsync 会因
            // UnitySynchronizationContext 死锁而无法同步等待。Current 路径的 lastOutcomeText
            // 赋值与 SetResultText 使用同一变量，文本一致性由构造保证。
            ValidateCopyableOutcomeFailedState();
            ValidateCopyableOutcomeNonCopyableStates();
            ValidateCopyEligibilityDoesNotMutate();
            ValidateDrawingDataContractRoundTrip();
            ValidateDrawingTenDeviceTypesRoundTrip();
            ValidateDrawingRotationAndSwitchState();
            ValidateDrawingWireRouteModes();
            ValidateDrawingEmptyCanvasRoundTrip();
            ValidateDrawingJsonDeterminism();
            ValidateDrawingInstanceNumberRecovery();
            ValidateDrawingImportRejectionCases();
            ValidateDrawingStableSortOrder();
            ValidateDrawingBidirectionalAutoWireJson();
            ValidateDrawingBidirectionalManualWireJson();
            ValidateDrawingZeroValueAllowed();
            ValidateDrawingInstanceNumber1000RoundTrip();
            ValidateDrawingInstanceIdSuffixBoundary();
            ValidateDrawingSameEndpointDifferentManualPaths();
            ValidateDrawingSameEndpointReverseManualPath();
            ValidateDrawingSameEndpointAutoAndManual();
            ValidateDrawingPositionNullRejected();
            ValidateDrawingPositionZeroAllowed();
            // Batch B：事务式导入核心与失败保护
            ValidateDrawingImportSuccessFullCircuit();
            ValidateDrawingImportFailurePreservesWorkspace();
            ValidateDrawingImportConsecutiveSuccess();
            ValidateDrawingImportEmptyDrawing();
            // Batch B 收口：运行态保护、必填坐标 JSON 校验
            ValidateDrawingImportRejectedWhileRunning();
            ValidateDrawingImportMissingPositionRejected();
            // Batch C1：文件操作核心与路径会话状态
            ValidateDrawingFileServiceExtensionNormalization();
            ValidateDrawingFileSaveRoundTrip();
            ValidateDrawingFileSaveOverwrite();
            ValidateDrawingFileCurrentPathManagement();
            ValidateDrawingFileImportFailurePreservesWorkspace();
            ValidateDrawingFileRunningGuardBeforeFileAccess();
            ValidateDrawingFileClearResetsPath();
            ValidateDrawingFileChineseAndSpacePath();
            // Batch C1 收口：文件边界验证与 UI 职责归位
            ValidateDrawingFileSaveFailurePreservesPath();
            ValidateDrawingFileOversizedImportRejected();
            ValidateDrawingFileInvalidUtf8ImportRejected();
            ValidateDrawingFileMissingPositionYRejected();
            ValidateDrawingFileSuccessDoesNotWriteStatusText();
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            var ground = model.AddComponent(SpiceComponentKind.Ground, Vector2.down);
            if (!model.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive") ||
                !model.AddWire(source.InstanceId, "negative", ground.InstanceId, "ground") ||
                !model.AddWire(resistor.InstanceId, "negative", ground.InstanceId, "ground")) throw new InvalidOperationException("Workspace did not retain valid terminal wires.");
            var circuit = model.BuildCircuitModel();
            if (circuit.Components.Count != 3 || circuit.Wires.Count != 3) throw new InvalidOperationException("Workspace-to-SpiceCircuitModel mapping is incomplete.");
            if (!SpiceParameterUnits.TryToSi(SpiceComponentKind.Resistor, 2d, "kOhm", out var resistance) || Math.Abs(resistance - 2000d) > 1e-12d) throw new InvalidOperationException("Resistance unit conversion failed.");
            if (!SpiceParameterUnits.TryToSi(SpiceComponentKind.Capacitor, 1d, "uF", out var capacitance) || Math.Abs(capacitance - 1e-6d) > 1e-15d) throw new InvalidOperationException("Capacitance unit conversion failed.");
            if (SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Resistor, 2000d) != "2 kΩ" ||
                SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Capacitor, 1e-6d) != "1 μF" ||
                SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Inductor, 0.01d) != "10 mH")
                throw new InvalidOperationException("Engineering-unit annotation formatting is inconsistent with the workspace model.");
            if (model.TrySetParameter(resistor.InstanceId, 0d)) throw new InvalidOperationException("Invalid zero-ohm parameter was accepted.");
            if (model.AddWire(source.InstanceId, "positive", source.InstanceId, "negative")) throw new InvalidOperationException("Workspace accepted a direct same-component wire.");

            var manualModel = new SpiceWorkspaceModel();
            var manualSource = manualModel.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var manualResistor = manualModel.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            if (!manualModel.AddWire(manualSource.InstanceId, "positive", manualResistor.InstanceId, "positive", SpiceWireVisualState.Manual(waypoints))) throw new InvalidOperationException("Workspace did not retain a manual wire route.");
            var manualWire = manualModel.Wires[0];
            if (manualWire.VisualState.RouteMode != SpiceWireRouteMode.Manual || manualWire.VisualState.Waypoints.Count != 3 || manualWire.VisualState.Waypoints[1] != waypoints[1]) throw new InvalidOperationException("Manual waypoint data was not preserved in workspace-local coordinates.");
            var route = SpiceWorkspaceOrthogonalRoute.Build(Vector2.zero, new Vector2(140f, 20f), Vector2.right, manualWire.VisualState);
            for (var index = 1; index < route.Count; index++)
            {
                var delta = route[index] - route[index - 1];
                if (Math.Abs(delta.x) > 0.01f && Math.Abs(delta.y) > 0.01f) throw new InvalidOperationException("Manual wire route produced a diagonal segment.");
            }
            var autoRoute = SpiceWorkspaceOrthogonalRoute.Build(Vector2.zero, new Vector2(140f, 20f), Vector2.right, SpiceWireVisualState.Auto());
            if (autoRoute.Count != 3 || Math.Abs(autoRoute[1].y) > 0.01f || Math.Abs(autoRoute[1].x - 140f) > 0.01f) throw new InvalidOperationException("Auto routing no longer uses the default horizontal-first elbow.");

            var generatedNetlist = "V1 n001 0 DC 10\nR1 n001 0 1000";
            var netlistResult = new SpiceSimulationResult { GeneratedNetlistContent = generatedNetlist };
            if (netlistResult.Netlist != generatedNetlist) throw new InvalidOperationException("Generated netlist compatibility alias no longer exposes the submitted content.");

            var bypassCircuit = new SpiceCircuitModel();
            bypassCircuit.Components.Add(SpiceComponentModel.DcVoltageSource("bypass-source", 10d));
            bypassCircuit.Components.Add(SpiceComponentModel.Ground("bypass-ground"));
            bypassCircuit.Wires.Add(new SpiceWireModel(
                new SpiceTerminalRef("bypass-source", SpiceComponentModel.PositiveTerminalId),
                new SpiceTerminalRef("bypass-source", SpiceComponentModel.NegativeTerminalId)));
            var bypassGraph = SpiceCircuitGraphBuilder.Build(bypassCircuit);
            if (!bypassGraph.Diagnostics.Exists(diagnostic => diagnostic.Code == "SPICE_SAME_COMPONENT_CONNECTION")) throw new InvalidOperationException("Topology validation did not report a bypassed same-component wire.");
            var changes = 0;
            model.Changed += _ => changes++;
            model.MoveComponent(resistor.InstanceId, new Vector2(10f, 20f));
            if (changes != 0) throw new InvalidOperationException("Pure view movement changed the electrical result version.");
            if (!model.RemoveComponent(resistor.InstanceId) || model.Wires.Count != 1) throw new InvalidOperationException("Deleting a component did not remove all associated wires.");
        }

        private static void ValidateInstanceNamingReset()
        {
            var model = new SpiceWorkspaceModel();
            var resistorOne = model.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var resistorTwo = model.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            if (resistorOne.InstanceId != "resistor-001" || resistorTwo.InstanceId != "resistor-002")
                throw new InvalidOperationException("SPICE instance naming did not start at one per component kind.");

            if (!model.RemoveComponent(resistorOne.InstanceId))
                throw new InvalidOperationException("SPICE instance deletion setup failed.");
            var resistorThree = model.AddComponent(SpiceComponentKind.Resistor, Vector2.up);
            if (resistorThree.InstanceId != "resistor-003")
                throw new InvalidOperationException("Single component deletion unexpectedly reused an instance number.");

            model.Clear();
            model.ResetInstanceNaming();
            var resetResistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var resetDiode = model.AddComponent(SpiceComponentKind.SiliconDiode, Vector2.right);
            var resetSource = model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.left);
            var resetVoltageProbe = model.AddComponent(SpiceComponentKind.VoltageProbe, Vector2.up);
            var resetCurrentProbe = model.AddComponent(SpiceComponentKind.CurrentProbe, Vector2.down);
            var resetSwitch = model.AddComponent(SpiceComponentKind.IdealSwitch, Vector2.one);
            if (resetResistor.InstanceId != "resistor-001" || resetDiode.InstanceId != "diode-001" ||
                resetSource.InstanceId != "source-001" || resetVoltageProbe.InstanceId != "voltage-probe-001" ||
                resetCurrentProbe.InstanceId != "current-probe-001" || resetSwitch.InstanceId != "switch-001")
                throw new InvalidOperationException("Clearing the SPICE workspace did not reset per-kind instance naming.");
        }

        private static void ValidateHostBindings()
        {
            var host = new GameObject("SpiceT3BindingValidation");
            try
            {
                var incomplete = host.AddComponent<SpiceWorkspaceViewBindings>();
                var rejected = false;
                try { incomplete.Validate(); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Incomplete host bindings were accepted.");

                var palette = CreateRect(host.transform);
                var viewport = CreateRect(host.transform);
                var wires = CreateRect(host.transform);
                var components = CreateRect(host.transform);
                var overlay = CreateRect(host.transform);
                var assistant = CreateRect(host.transform);
                var parameters = CreateRect(host.transform);
                var results = CreateRect(host.transform);
                var netlist = CreateRect(host.transform);
                var diagnostics = CreateRect(host.transform);
                incomplete.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                    CreateButton(host.transform), CreateButton(host.transform), CreateButton(host.transform), CreateButton(host.transform));
                incomplete.Validate();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateSimulationModeSwitching()
        {
            var host = new GameObject("SimulationModeValidation");
            host.SetActive(false);
            try
            {
                var controller = host.AddComponent<SimulationModeController>();
                var controlTopBar = CreateRoot(host.transform);
                var controlPalette = CreateRoot(host.transform);
                var controlPaletteController = controlPalette.AddComponent<PaletteController>();
                var controlWorkspace = CreateRoot(host.transform);
                var inspector = CreateRoot(host.transform);
                var spiceRoot = CreateRoot(host.transform);
                controller.Configure(controlTopBar, controlPalette, controlPaletteController, controlWorkspace, inspector, spiceRoot);
                controller.Initialize();
                host.SetActive(true);

                if (controller.CurrentMode != SimulationWorkspaceMode.ControlCircuit || !controlTopBar.activeSelf || spiceRoot.activeSelf)
                    throw new InvalidOperationException("Simulation mode controller did not initialize the control mode.");
                controller.SelectSpiceDc();
                if (controller.CurrentMode != SimulationWorkspaceMode.SpiceDc || controlPalette.activeSelf || !spiceRoot.activeSelf)
                    throw new InvalidOperationException("Simulation mode controller did not preserve mutually exclusive roots.");
                controller.SelectControlCircuit();
                if (controller.CurrentMode != SimulationWorkspaceMode.ControlCircuit || !controlWorkspace.activeSelf || spiceRoot.activeSelf)
                    throw new InvalidOperationException("Simulation mode controller did not restore the control roots.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void ValidateHiddenDemoHostInitialization()
        {
            var canvasRoot = new GameObject("SpiceDemoHostValidation", typeof(RectTransform), typeof(Canvas));
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
                bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                    CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform), CreateButton(canvasRoot.transform));

                var hostRoot = CreateRoot(canvasRoot.transform);
                hostRoot.SetActive(false);
                var host = hostRoot.AddComponent<SpiceWorkspaceDemoHost>();
                host.Configure(bindings, workspace);
                host.Initialize();
                host.Initialize();
                if (!host.IsInitialized || host.Controller != workspace)
                    throw new InvalidOperationException("Hidden SPICE Demo host did not initialize exactly once.");
                if (workspace.ViewController == null || workspace.ContentRect == null || workspace.WorkspaceRect != workspace.ContentRect)
                    throw new InvalidOperationException("SPICE workspace did not create one explicit view Content root.");
                if (workspace.ContentRect.parent != viewport || wires.parent != workspace.ContentRect || components.parent != workspace.ContentRect || overlay.parent != workspace.ContentRect)
                    throw new InvalidOperationException("SPICE workspace layers are not sharing the Content coordinate system.");
                workspace.ViewController.ZoomIn();
                if (Math.Abs(workspace.ViewController.CurrentScale - 1.1f) > 0.001f)
                    throw new InvalidOperationException("SPICE workspace zoom-in step is not 10 percent.");
                workspace.ViewController.ResetView();
                if (Math.Abs(workspace.ViewController.CurrentScale - 1f) > 0.001f || workspace.ContentRect.anchoredPosition.sqrMagnitude > 0.001f)
                    throw new InvalidOperationException("SPICE workspace reset view did not restore the default view state.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static RectTransform CreateRect(Transform parent)
        {
            var rect = new GameObject("BindingRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static GameObject CreateRoot(Transform parent)
        {
            var root = new GameObject("ModeRoot", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            return root;
        }

        private static Button CreateButton(Transform parent)
        {
            var button = new GameObject("BindingButton", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false);
            return button;
        }

        private static Text CreateText(Transform parent)
        {
            var text = new GameObject("ModeLabel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            return text;
        }

        private static void ValidateScrollableTextLayout()
        {
            var canvasRoot = new GameObject("SpiceScrollableTextValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var result = CreateScrollableTextForValidation(canvasRoot.transform, "Result", new Vector2(300f, 180f));
                var netlist = CreateScrollableTextForValidation(canvasRoot.transform, "Netlist", new Vector2(300f, 180f));
                var longText = string.Join("\n", new string[40].Select((_, index) => "诊断 " + index + "：该端子尚未通过导线连接。"));

                if (!SpiceScrollableTextLayout.Refresh(result.ScrollRect, result.Text, longText, true))
                    throw new InvalidOperationException("Long SPICE diagnostics did not update the result text.");
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(result.Content);
                if (result.Content.rect.height <= result.Viewport.rect.height)
                    throw new InvalidOperationException("Long SPICE diagnostics did not expand the actual ResultScrollView content height.");
                if (result.Viewport.GetComponent<RectMask2D>() == null || result.Content.GetComponent<VerticalLayoutGroup>() == null ||
                    result.Content.GetComponent<ContentSizeFitter>() == null || result.ScrollRect.content != result.Content || result.ScrollRect.viewport != result.Viewport)
                    throw new InvalidOperationException("SPICE scroll layout is missing its required Viewport, Content, or standard UGUI layout components.");

                result.ScrollRect.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                if (result.Content.anchoredPosition.y <= 0.01f)
                    throw new InvalidOperationException("ResultScrollView could not move its long diagnostic content to the bottom.");
                var userPosition = result.ScrollRect.verticalNormalizedPosition;
                if (SpiceScrollableTextLayout.Refresh(result.ScrollRect, result.Text, longText, true))
                    throw new InvalidOperationException("Unchanged SPICE diagnostics unexpectedly refreshed their scroll layout.");
                if (Math.Abs(result.ScrollRect.verticalNormalizedPosition - userPosition) > 0.001f)
                    throw new InvalidOperationException("Unchanged SPICE diagnostics reset the user's scroll position.");

                if (!SpiceScrollableTextLayout.Refresh(netlist.ScrollRect, netlist.Text, longText, true))
                    throw new InvalidOperationException("Long SPICE netlist did not update independently.");
                netlist.ScrollRect.verticalNormalizedPosition = 0f;
                Canvas.ForceUpdateCanvases();
                if (result.ScrollRect.verticalNormalizedPosition != userPosition || result.ScrollRect.content == netlist.ScrollRect.content)
                    throw new InvalidOperationException("Result and netlist scroll views are not independent.");

                SpiceScrollableTextLayout.Refresh(result.ScrollRect, result.Text, "短结果", true);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(result.Content);
                if (result.Content.rect.height > result.Viewport.rect.height + 0.01f)
                    throw new InvalidOperationException("Short SPICE results left an unnecessary vertical scroll range.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static SpiceScrollableTextView CreateScrollableTextForValidation(Transform parent, string name, Vector2 size)
        {
            var scroll = new GameObject(name + "Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect)).GetComponent<ScrollRect>();
            scroll.transform.SetParent(parent, false);
            var scrollRect = scroll.GetComponent<RectTransform>();
            scrollRect.sizeDelta = size;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = SpiceScrollableTextLayout.ScrollSensitivity;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(scroll.transform, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            SpiceScrollableTextLayout.ConfigureContent(content);

            var text = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(content, false);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.sizeDelta = Vector2.zero;

            scroll.viewport = viewport;
            scroll.content = content;
            return new SpiceScrollableTextView(scroll, viewport, content, text);
        }

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

        public static void ConnectSingleResistor(SpiceWorkspaceController workspace, string source, string resistor, string ground)
        {
            if (!workspace.Connect(source, "positive", resistor, "positive") ||
                !workspace.Connect(source, "negative", ground, "ground") ||
                !workspace.Connect(resistor, "negative", ground, "ground")) throw new InvalidOperationException("Unable to construct the single-resistor T3 topology.");
        }

        // ===== SPICE 图纸数据契约与内存往返验证 =====

        private static void ValidateDrawingDataContractRoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(10f, 20f));
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 100f));
            model.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive");

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("图纸 JSON 往返失败：" + error);

            if (restored.Components.Count != 2) throw new InvalidOperationException("往返后组件数量不匹配。");
            if (restored.Wires.Count != 1) throw new InvalidOperationException("往返后导线数量不匹配。");

            var restoredSource = restored.FindComponent(source.InstanceId);
            if (restoredSource == null) throw new InvalidOperationException("往返后丢失源组件。");
            if (restoredSource.Kind != SpiceComponentKind.DcVoltageSource) throw new InvalidOperationException("往返后器件类型不匹配。");
            if (restoredSource.Position != new Vector2(10f, 20f)) throw new InvalidOperationException("往返后位置不匹配。");
            if (Math.Abs(restoredSource.SiValue - source.SiValue) > 1e-12) throw new InvalidOperationException("往返后参数值不匹配。");

            var restoredWire = restored.Wires[0];
            // 导线方向已规范化：字典序 "resistor-001" < "source-001"（r < s），
            // 因此 start 固定为 resistor，end 固定为 source。
            if (restoredWire.StartComponentId != resistor.InstanceId || restoredWire.StartTerminalId != "positive") throw new InvalidOperationException("往返后导线起点不匹配。");
            if (restoredWire.EndComponentId != source.InstanceId || restoredWire.EndTerminalId != "positive") throw new InvalidOperationException("往返后导线终点不匹配。");
            if (restoredWire.VisualState.RouteMode != SpiceWireRouteMode.Auto) throw new InvalidOperationException("往返后路由模式不匹配。");

            var dto = SpiceDrawingSerializer.ToDto(model);
            if (dto.format != SpiceDrawingFormat.Format) throw new InvalidOperationException("DTO format 不正确。");
            if (dto.schemaVersion != SpiceDrawingFormat.SchemaVersion) throw new InvalidOperationException("DTO schemaVersion 不正确。");
        }

        private static void ValidateDrawingTenDeviceTypesRoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(0f, 0f));
            model.AddComponent(SpiceComponentKind.DcCurrentSource, new Vector2(100f, 0f));
            model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 0f));
            model.AddComponent(SpiceComponentKind.Capacitor, new Vector2(300f, 0f));
            model.AddComponent(SpiceComponentKind.Inductor, new Vector2(400f, 0f));
            model.AddComponent(SpiceComponentKind.Ground, new Vector2(500f, 0f));
            model.AddComponent(SpiceComponentKind.IdealSwitch, new Vector2(600f, 0f));
            model.AddComponent(SpiceComponentKind.SiliconDiode, new Vector2(700f, 0f));
            model.AddComponent(SpiceComponentKind.VoltageProbe, new Vector2(800f, 0f));
            model.AddComponent(SpiceComponentKind.CurrentProbe, new Vector2(900f, 0f));

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("10 类器件往返失败：" + error);

            if (restored.Components.Count != 10) throw new InvalidOperationException("10 类器件往返后数量不匹配。");
            // 往返后组件按 InstanceId 字典序排序，验证类型集合而非顺序
            var expectedKinds = new[]
            {
                SpiceComponentKind.DcVoltageSource, SpiceComponentKind.DcCurrentSource,
                SpiceComponentKind.Resistor, SpiceComponentKind.Capacitor,
                SpiceComponentKind.Inductor, SpiceComponentKind.Ground,
                SpiceComponentKind.IdealSwitch, SpiceComponentKind.SiliconDiode,
                SpiceComponentKind.VoltageProbe, SpiceComponentKind.CurrentProbe
            };
            var restoredKinds = new System.Collections.Generic.HashSet<SpiceComponentKind>();
            foreach (var component in restored.Components)
            {
                if (!restoredKinds.Add(component.Kind))
                    throw new InvalidOperationException("10 类器件往返后出现重复类型：" + component.Kind);
            }
            foreach (var expected in expectedKinds)
            {
                if (!restoredKinds.Contains(expected))
                    throw new InvalidOperationException("10 类器件往返后缺失类型：" + expected);
            }
        }

        private static void ValidateDrawingRotationAndSwitchState()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", new Vector2(10f, 10f), 12d, 2);
            var sw = model.AddComponentWithIdentity(SpiceComponentKind.IdealSwitch, "switch-001", new Vector2(100f, 100f), 1d, 3);

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("旋转/开关往返失败：" + error);

            var restoredSource = restored.FindComponent("source-001");
            if (restoredSource.RotationQuarterTurns != 2) throw new InvalidOperationException("往返后旋转值不匹配。");
            if (Math.Abs(restoredSource.SiValue - 12d) > 1e-12) throw new InvalidOperationException("往返后电压值不匹配。");

            var restoredSwitch = restored.FindComponent("switch-001");
            if (restoredSwitch.RotationQuarterTurns != 3) throw new InvalidOperationException("往返后开关旋转值不匹配。");
            if (Math.Abs(restoredSwitch.SiValue - 1d) > 1e-12) throw new InvalidOperationException("往返后开关状态不匹配。");
        }

        private static void ValidateDrawingWireRouteModes()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 0f));
            var ground = model.AddComponent(SpiceComponentKind.Ground, new Vector2(0f, -200f));

            model.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive");
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            model.AddWire(source.InstanceId, "negative", ground.InstanceId, "ground", SpiceWireVisualState.Manual(waypoints));

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("Wire 路由模式往返失败：" + error);

            if (restored.Wires.Count != 2) throw new InvalidOperationException("往返后导线数量不匹配。");
            // 往返后 Wire 方向已规范化（字典序较小的端点为 start），按端子查找对应 Wire：
            // - Auto wire: source:positive <-> resistor:positive（两端端子相同，方向不影响查找）
            // - Manual wire: 原始 source:negative -> ground:ground，规范化后 ground:ground -> source:negative
            SpiceWorkspaceWireData autoWire = null, manualWire = null;
            foreach (var wire in restored.Wires)
            {
                if (wire.StartTerminalId == "positive" && wire.EndTerminalId == "positive") autoWire = wire;
                else if (wire.StartTerminalId == "ground" && wire.EndTerminalId == "negative") manualWire = wire;
            }
            if (autoWire == null) throw new InvalidOperationException("往返后丢失自动路由导线。");
            if (autoWire.VisualState.RouteMode != SpiceWireRouteMode.Auto) throw new InvalidOperationException("自动路由模式往返后不匹配。");
            if (manualWire == null) throw new InvalidOperationException("往返后丢失手工路由导线。");
            if (manualWire.VisualState.RouteMode != SpiceWireRouteMode.Manual) throw new InvalidOperationException("手工路由模式往返后不匹配。");
            if (manualWire.VisualState.Waypoints.Count != 3) throw new InvalidOperationException("手工折点数量往返后不匹配。");
            // 端点交换后折点倒序：原始 [40,0],[40,80],[120,80] -> 倒序 [120,80],[40,80],[40,0]
            if (manualWire.VisualState.Waypoints[0] != new Vector2(120f, 80f) ||
                manualWire.VisualState.Waypoints[1] != new Vector2(40f, 80f) ||
                manualWire.VisualState.Waypoints[2] != new Vector2(40f, 0f))
                throw new InvalidOperationException("手工折点坐标往返后不匹配（端点交换后应倒序）。");
        }

        private static void ValidateDrawingEmptyCanvasRoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("空画布往返失败：" + error);
            if (restored.Components.Count != 0 || restored.Wires.Count != 0)
                throw new InvalidOperationException("空画布往返后不应有组件或导线。");
        }

        private static void ValidateDrawingJsonDeterminism()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(10f, 20f));
            model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 100f));
            model.AddWire("source-001", "positive", "resistor-001", "positive");

            var json1 = SpiceDrawingSerializer.ToJson(model);
            var json2 = SpiceDrawingSerializer.ToJson(model);
            if (json1 != json2) throw new InvalidOperationException("相同模型的 JSON 输出不确定。");
        }

        private static void ValidateDrawingInstanceNumberRecovery()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.zero, 1000d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-002", Vector2.right, 2000d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-005", Vector2.up, 5000d, 0);
            model.RestoreInstanceNumbersFromExisting();

            var newResistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.down);
            if (newResistor.InstanceId != "resistor-006")
                throw new InvalidOperationException("导入 R1/R2/R5 后新建应得到 R6，实际得到 " + newResistor.InstanceId);
        }

        private static void ValidateDrawingImportRejectionCases()
        {
            // 未知格式
            var badFormat = JsonUtility.ToJson(new SpiceDrawingFileDto { format = "Unknown", schemaVersion = 1 }, true);
            if (SpiceDrawingSerializer.TryFromJson(badFormat, out _, out var error1))
                throw new InvalidOperationException("未知格式应被拒绝。");
            if (string.IsNullOrEmpty(error1)) throw new InvalidOperationException("未知格式应返回错误信息。");

            // 未知版本
            var badVersion = JsonUtility.ToJson(new SpiceDrawingFileDto { format = SpiceDrawingFormat.Format, schemaVersion = 99 }, true);
            if (SpiceDrawingSerializer.TryFromJson(badVersion, out _, out _))
                throw new InvalidOperationException("未知版本应被拒绝。");

            // 空内容
            if (SpiceDrawingSerializer.TryFromJson("", out _, out _))
                throw new InvalidOperationException("空内容应被拒绝。");
            if (SpiceDrawingSerializer.TryFromJson("   ", out _, out _))
                throw new InvalidOperationException("空白内容应被拒绝。");

            // 重复 InstanceId
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.zero, 1000d, 0);
            var dto = SpiceDrawingSerializer.ToDto(model);
            dto.components.Add(new SpiceComponentDto
            {
                instanceId = "resistor-001",
                componentType = SpiceComponentKind.Resistor.ToString(),
                position = new SpiceVector2Dto { x = "1", y = "1" },
                rotationQuarterTurns = 0,
                siValueText = "2000"
            });
            if (SpiceDrawingSerializer.TryFromDto(dto, out _, out var error3))
                throw new InvalidOperationException("重复 InstanceId 应被拒绝。");
            if (string.IsNullOrEmpty(error3)) throw new InvalidOperationException("重复 InstanceId 应返回错误信息。");

            // 未知器件类型（字符串无法解析）
            var unknownTypeDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "unknown-001",
                        componentType = "NonexistentType",
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(unknownTypeDto, out _, out var error4))
                throw new InvalidOperationException("未知器件类型应被拒绝。");
            if (string.IsNullOrEmpty(error4)) throw new InvalidOperationException("未知器件类型应返回错误信息。");

            // 非法参数（0 欧姆电阻）：siValueText 显式提供 "0"，但电阻必须大于 0
            var badParamDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-001",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "0"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(badParamDto, out _, out var error5))
                throw new InvalidOperationException("0 欧姆电阻应被拒绝。");
            if (string.IsNullOrEmpty(error5)) throw new InvalidOperationException("非法参数应返回错误信息。");

            // 导线引用不存在的组件
            var danglingWireDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                wires = new System.Collections.Generic.List<SpiceWireDto>
                {
                    new SpiceWireDto
                    {
                        startComponentId = "resistor-001",
                        startTerminalId = "positive",
                        endComponentId = "resistor-002",
                        endTerminalId = "negative",
                        routeMode = SpiceWireRouteMode.Auto.ToString()
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(danglingWireDto, out _, out var error6))
                throw new InvalidOperationException("引用不存在组件的导线应被拒绝。");
            if (string.IsNullOrEmpty(error6)) throw new InvalidOperationException("悬空导线应返回错误信息。");

            // 数值未定义枚举（"999" 能被 TryParse 解析但不在 IsDefined 范围内）
            var undefinedEnumDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-001",
                        componentType = "999",
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(undefinedEnumDto, out _, out var error7))
                throw new InvalidOperationException("数值未定义枚举 \"999\" 应被 IsDefined 拒绝。");
            if (string.IsNullOrEmpty(error7)) throw new InvalidOperationException("未定义枚举应返回错误信息。");

            // 有参数器件缺失 siValueText（null/空字符串都必须拒绝，以区分字段缺失与数值 0）
            var missingSiValueDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0
                        // siValueText 缺省为 null
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(missingSiValueDto, out _, out var error8))
                throw new InvalidOperationException("有参数器件缺失 siValueText 应被拒绝。");
            if (string.IsNullOrEmpty(error8)) throw new InvalidOperationException("缺失 siValueText 应返回错误信息。");

            // 有参数器件 siValueText 为空白字符串同样视为字段缺失
            var blankSiValueDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "   "
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(blankSiValueDto, out _, out var errorBlank))
                throw new InvalidOperationException("siValueText 为空白字符串应被拒绝。");
            if (string.IsNullOrEmpty(errorBlank)) throw new InvalidOperationException("空白 siValueText 应返回错误信息。");

            // siValueText 非法数值字符串（无法 TryParse）
            var nonNumericDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "not-a-number"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nonNumericDto, out _, out var errorNonNumeric))
                throw new InvalidOperationException("非法数值字符串应被拒绝。");
            if (string.IsNullOrEmpty(errorNonNumeric)) throw new InvalidOperationException("非法数值字符串应返回错误信息。");

            // siValueText 为 NaN 字面量
            var nanDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "NaN"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nanDto, out _, out var errorNan))
                throw new InvalidOperationException("NaN 应被拒绝。");
            if (string.IsNullOrEmpty(errorNan)) throw new InvalidOperationException("NaN 应返回错误信息。");

            // siValueText 为 Infinity 字面量
            var infinityDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "Infinity"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(infinityDto, out _, out var errorInfinity))
                throw new InvalidOperationException("Infinity 应被拒绝。");
            if (string.IsNullOrEmpty(errorInfinity)) throw new InvalidOperationException("Infinity 应返回错误信息。");

            // InstanceId 前缀与器件类型不匹配
            var mismatchedPrefixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(mismatchedPrefixDto, out _, out var error9))
                throw new InvalidOperationException("InstanceId 前缀与器件类型不匹配应被拒绝。");
            if (string.IsNullOrEmpty(error9)) throw new InvalidOperationException("前缀不匹配应返回错误信息。");

            // InstanceId 后缀长度不足 3 位（D3 语义是最少三位）
            var badSuffixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-1",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(badSuffixDto, out _, out var error10))
                throw new InvalidOperationException("InstanceId 后缀长度不足 3 位应被拒绝。");
            if (string.IsNullOrEmpty(error10)) throw new InvalidOperationException("后缀格式错误应返回错误信息。");

            // InstanceId 后缀为 000（非正整数）
            var zeroSuffixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-000",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(zeroSuffixDto, out _, out var error11))
                throw new InvalidOperationException("InstanceId 后缀 000 应被拒绝（非正整数）。");
            if (string.IsNullOrEmpty(error11)) throw new InvalidOperationException("后缀 000 应返回错误信息。");

            // InstanceId 后缀为负数（含负号，非纯数字）
            var negativeSuffixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor--001",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(negativeSuffixDto, out _, out var errorNegative))
                throw new InvalidOperationException("InstanceId 后缀为负数应被拒绝。");
            if (string.IsNullOrEmpty(errorNegative)) throw new InvalidOperationException("负数后缀应返回错误信息。");
        }

        private static void ValidateDrawingStableSortOrder()
        {
            // 验证同一模型两次序列化结果一致
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            model.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            model.AddWire("source-001", "positive", "resistor-001", "positive");

            var json1 = SpiceDrawingSerializer.ToJson(model);
            var json2 = SpiceDrawingSerializer.ToJson(model);
            if (json1 != json2) throw new InvalidOperationException("同一模型两次序列化结果应一致。");

            // 验证组件顺序不影响 JSON：先加电阻再加电压源，JSON 中应按 InstanceId 排序
            var modelReversed = new SpiceWorkspaceModel();
            modelReversed.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelReversed.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);

            var jsonReversed = SpiceDrawingSerializer.ToJson(modelReversed);
            var sourceIndex = jsonReversed.IndexOf("source-001");
            var resistorIndex = jsonReversed.IndexOf("resistor-001");
            if (sourceIndex < 0 || resistorIndex < 0) throw new InvalidOperationException("JSON 中应包含两个组件。");
            // 字典序 "resistor-001" < "source-001"（r < s），resistor 应排在 source 之前
            if (resistorIndex > sourceIndex) throw new InvalidOperationException("组件应按 InstanceId 字典序排序（resistor 应在 source 之前）。");
        }

        // 验证 Auto 路由 Wire 的双向创建生成字节完全一致的 JSON。
        // 规范化方向：比较两端 (componentId, terminalId) 字典序，较小者固定为 start。
        private static void ValidateDrawingBidirectionalAutoWireJson()
        {
            var modelA = new SpiceWorkspaceModel();
            var sourceA = modelA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorA = modelA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 正向：source -> resistor
            modelA.AddWire(sourceA.InstanceId, "positive", resistorA.InstanceId, "positive");

            var modelB = new SpiceWorkspaceModel();
            var sourceB = modelB.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorB = modelB.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 反向：resistor -> source
            modelB.AddWire(resistorB.InstanceId, "positive", sourceB.InstanceId, "positive");

            var jsonA = SpiceDrawingSerializer.ToJson(modelA);
            var jsonB = SpiceDrawingSerializer.ToJson(modelB);
            // 字典序 "resistor-001" < "source-001"，两端都会被规范化为 resistor -> source
            if (jsonA != jsonB) throw new InvalidOperationException("反向创建的 Auto Wire 应生成字节完全一致的 JSON。");
        }

        // 验证 Manual 路由 Wire 的双向创建生成字节完全一致的 JSON。
        // 端点交换时 manualRoutePoints 必须倒序输出，保持折点序列与端点方向一致。
        // 等价 Wire：反向创建时折点也必须倒序，使两端表示同一物理路径。
        private static void ValidateDrawingBidirectionalManualWireJson()
        {
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            var reversedWaypoints = new[] { new Vector2(120f, 80f), new Vector2(40f, 80f), new Vector2(40f, 0f) };

            var modelA = new SpiceWorkspaceModel();
            var sourceA = modelA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorA = modelA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 正向：source -> resistor，折点从 source 侧到 resistor 侧
            modelA.AddWire(sourceA.InstanceId, "positive", resistorA.InstanceId, "positive",
                SpiceWireVisualState.Manual(waypoints));

            var modelB = new SpiceWorkspaceModel();
            var sourceB = modelB.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorB = modelB.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 反向：resistor -> source，折点倒序以表示同一物理路径
            modelB.AddWire(resistorB.InstanceId, "positive", sourceB.InstanceId, "positive",
                SpiceWireVisualState.Manual(reversedWaypoints));

            var jsonA = SpiceDrawingSerializer.ToJson(modelA);
            var jsonB = SpiceDrawingSerializer.ToJson(modelB);
            // modelA：端点交换（source->resistor 变为 resistor->source），折点倒序输出 [120,80],[40,80],[40,0]
            // modelB：端点不交换（已是 resistor->source），折点原序输出 [120,80],[40,80],[40,0]
            // 两者规范化后端点和折点完全一致，JSON 必须字节相同。
            if (jsonA != jsonB) throw new InvalidOperationException("反向创建的等价 Manual Wire 应生成字节完全一致的 JSON。");

            // 验证往返后折点坐标正确（按规范化方向 resistor -> source）
            if (!SpiceDrawingSerializer.TryFromJson(jsonA, out var restored, out var error))
                throw new InvalidOperationException("Manual Wire 往返失败：" + error);
            if (restored.Wires.Count != 1) throw new InvalidOperationException("往返后导线数量不匹配。");
            var restoredWire = restored.Wires[0];
            // 规范化后 start 应为字典序较小的 resistor-001
            if (restoredWire.StartComponentId != "resistor-001" || restoredWire.EndComponentId != "source-001")
                throw new InvalidOperationException("Manual Wire 往返后端点方向未规范化。");
            if (restoredWire.VisualState.Waypoints.Count != 3) throw new InvalidOperationException("往返后折点数量不匹配。");
            // 规范化后折点应为 [120,80], [40,80], [40,0]
            if (restoredWire.VisualState.Waypoints[0] != new Vector2(120f, 80f) ||
                restoredWire.VisualState.Waypoints[1] != new Vector2(40f, 80f) ||
                restoredWire.VisualState.Waypoints[2] != new Vector2(40f, 0f))
                throw new InvalidOperationException("端点交换后折点未正确倒序。");
        }

        // 验证 0 V 电压源与 0 A 电流源必须被允许（siValueText="0" 合法），
        // 而电阻/电容/电感仍必须大于 0。
        private static void ValidateDrawingZeroValueAllowed()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", Vector2.zero, 0d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.DcCurrentSource, "current-source-001", Vector2.right, 0d, 0);

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("0V/0A 往返失败：" + error);

            var restoredSource = restored.FindComponent("source-001");
            if (restoredSource == null) throw new InvalidOperationException("往返后丢失电压源。");
            if (Math.Abs(restoredSource.SiValue - 0d) > 1e-12) throw new InvalidOperationException("往返后电压源 0V 值不匹配。");

            var restoredCurrent = restored.FindComponent("current-source-001");
            if (restoredCurrent == null) throw new InvalidOperationException("往返后丢失电流源。");
            if (Math.Abs(restoredCurrent.SiValue - 0d) > 1e-12) throw new InvalidOperationException("往返后电流源 0A 值不匹配。");

            // 验证 DTO 中 siValueText 确实为 "0"（区分字段缺失与数值 0）
            var dto = SpiceDrawingSerializer.ToDto(model);
            var sourceDto = dto.components.Find(c => c.instanceId == "source-001");
            if (sourceDto == null || sourceDto.siValueText != "0") throw new InvalidOperationException("0V 电压源的 siValueText 应为 \"0\"。");
            var currentDto = dto.components.Find(c => c.instanceId == "current-source-001");
            if (currentDto == null || currentDto.siValueText != "0") throw new InvalidOperationException("0A 电流源的 siValueText 应为 \"0\"。");
        }

        // 验证第 1000 个编号的 JSON 往返及后续编号恢复。
        // D3 语义是最少三位，不是最多三位：resistor-1000 必须被接受。
        private static void ValidateDrawingInstanceNumber1000RoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-1000", Vector2.zero, 1000d, 0);
            model.RestoreInstanceNumbersFromExisting();

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("resistor-1000 往返失败：" + error);

            var restoredResistor = restored.FindComponent("resistor-1000");
            if (restoredResistor == null) throw new InvalidOperationException("往返后丢失 resistor-1000。");
            if (restoredResistor.Kind != SpiceComponentKind.Resistor) throw new InvalidOperationException("往返后类型不匹配。");

            // 验证后续编号恢复：下一个新建电阻应为 resistor-1001
            var newResistor = restored.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            if (newResistor.InstanceId != "resistor-1001")
                throw new InvalidOperationException("resistor-1000 往返后新建应得到 resistor-1001，实际得到 " + newResistor.InstanceId);
        }

        // 验证 InstanceId 后缀规则的边界情况：
        // 接受 resistor-001（最少三位）和 resistor-1000（四位），
        // 拒绝 resistor-1（不足三位）、resistor-000（非正整数）、resistor--001（负数）。
        private static void ValidateDrawingInstanceIdSuffixBoundary()
        {
            // 直接校验 IsValidInstanceId 的接受与拒绝
            if (!SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-001"))
                throw new InvalidOperationException("resistor-001 应被接受。");
            if (!SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-1000"))
                throw new InvalidOperationException("resistor-1000 应被接受。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-1"))
                throw new InvalidOperationException("resistor-1 应被拒绝（后缀不足三位）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-000"))
                throw new InvalidOperationException("resistor-000 应被拒绝（非正整数）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor--001"))
                throw new InvalidOperationException("resistor--001 应被拒绝（负数后缀）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-abc"))
                throw new InvalidOperationException("resistor-abc 应被拒绝（非数字后缀）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.DcVoltageSource, "resistor-001"))
                throw new InvalidOperationException("resistor-001 与 DcVoltageSource 前缀不匹配应被拒绝。");

            // 纯数字严格校验：不接受加号、减号、空白或其他非数字字符
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-+001"))
                throw new InvalidOperationException("resistor-+001 应被拒绝（加号非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor- 001"))
                throw new InvalidOperationException("resistor- 001 应被拒绝（前导空格非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-001 "))
                throw new InvalidOperationException("resistor-001 (末尾空格) 应被拒绝（末尾空格非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-00a1"))
                throw new InvalidOperationException("resistor-00a1 应被拒绝（含字母非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor--001"))
                throw new InvalidOperationException("resistor--001 应被拒绝（负号非纯数字）。");

            // 验证 resistor-001 与 resistor-1000 均能完整往返
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.zero, 1000d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-1000", Vector2.right, 2000d, 0);
            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("resistor-001 + resistor-1000 往返失败：" + error);
            if (restored.Components.Count != 2) throw new InvalidOperationException("往返后组件数量不匹配。");
            if (restored.FindComponent("resistor-001") == null) throw new InvalidOperationException("往返后丢失 resistor-001。");
            if (restored.FindComponent("resistor-1000") == null) throw new InvalidOperationException("往返后丢失 resistor-1000。");
        }

        // 验证同端点不同 Manual 路径的 Wire 排序稳定性。
        // 两条端点相同、waypoint 不同的 Manual Wire，交换添加顺序后 JSON 必须字节完全一致。
        private static void ValidateDrawingSameEndpointDifferentManualPaths()
        {
            var pathA = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            var pathB = new[] { new Vector2(0f, 40f), new Vector2(80f, 40f) };

            // 模型 1：先 A 后 B
            var modelAB = new SpiceWorkspaceModel();
            var sourceAB = modelAB.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorAB = modelAB.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelAB.AddWire(sourceAB.InstanceId, "positive", resistorAB.InstanceId, "positive", SpiceWireVisualState.Manual(pathA));
            modelAB.AddWire(sourceAB.InstanceId, "positive", resistorAB.InstanceId, "positive", SpiceWireVisualState.Manual(pathB));

            // 模型 2：先 B 后 A
            var modelBA = new SpiceWorkspaceModel();
            var sourceBA = modelBA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorBA = modelBA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelBA.AddWire(sourceBA.InstanceId, "positive", resistorBA.InstanceId, "positive", SpiceWireVisualState.Manual(pathB));
            modelBA.AddWire(sourceBA.InstanceId, "positive", resistorBA.InstanceId, "positive", SpiceWireVisualState.Manual(pathA));

            var jsonAB = SpiceDrawingSerializer.ToJson(modelAB);
            var jsonBA = SpiceDrawingSerializer.ToJson(modelBA);
            if (jsonAB != jsonBA) throw new InvalidOperationException("同端点不同 Manual 路径交换添加顺序后 JSON 应字节一致。");
        }

        // 验证同端点 Manual Wire 的反向端点创建与正向等价版本 JSON 字节一致。
        // 反向创建时 waypoint 必须倒序以表示同一物理路径。
        private static void ValidateDrawingSameEndpointReverseManualPath()
        {
            var pathForward = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            var pathReversed = new[] { new Vector2(120f, 80f), new Vector2(40f, 80f), new Vector2(40f, 0f) };

            // 模型 1：正向 source -> resistor
            var modelFwd = new SpiceWorkspaceModel();
            var sourceFwd = modelFwd.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorFwd = modelFwd.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelFwd.AddWire(sourceFwd.InstanceId, "positive", resistorFwd.InstanceId, "positive", SpiceWireVisualState.Manual(pathForward));

            // 模型 2：反向 resistor -> source，waypoint 倒序
            var modelRev = new SpiceWorkspaceModel();
            var sourceRev = modelRev.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorRev = modelRev.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelRev.AddWire(resistorRev.InstanceId, "positive", sourceRev.InstanceId, "positive", SpiceWireVisualState.Manual(pathReversed));

            var jsonFwd = SpiceDrawingSerializer.ToJson(modelFwd);
            var jsonRev = SpiceDrawingSerializer.ToJson(modelRev);
            if (jsonFwd != jsonRev) throw new InvalidOperationException("同端点反向 Manual Wire 与正向等价版本 JSON 应字节一致。");
        }

        // 验证同端点 Auto 与 Manual Wire 共存时，交换添加顺序后 JSON 字节一致。
        private static void ValidateDrawingSameEndpointAutoAndManual()
        {
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };

            // 模型 1：先 Auto 后 Manual
            var modelAM = new SpiceWorkspaceModel();
            var sourceAM = modelAM.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorAM = modelAM.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelAM.AddWire(sourceAM.InstanceId, "positive", resistorAM.InstanceId, "positive");
            modelAM.AddWire(sourceAM.InstanceId, "positive", resistorAM.InstanceId, "positive", SpiceWireVisualState.Manual(waypoints));

            // 模型 2：先 Manual 后 Auto
            var modelMA = new SpiceWorkspaceModel();
            var sourceMA = modelMA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorMA = modelMA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelMA.AddWire(sourceMA.InstanceId, "positive", resistorMA.InstanceId, "positive", SpiceWireVisualState.Manual(waypoints));
            modelMA.AddWire(sourceMA.InstanceId, "positive", resistorMA.InstanceId, "positive");

            var jsonAM = SpiceDrawingSerializer.ToJson(modelAM);
            var jsonMA = SpiceDrawingSerializer.ToJson(modelMA);
            if (jsonAM != jsonMA) throw new InvalidOperationException("同端点 Auto/Manual 共存交换添加顺序后 JSON 应字节一致。");
        }

        // 验证 position 缺失（null）必须被拒绝，且错误信息包含 InstanceId。
        // SpiceVector2Dto 为 class，缺失时为 null，不再静默变为 (0,0)。
        private static void ValidateDrawingPositionNullRejected()
        {
            var nullPositionDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = null,
                        rotationQuarterTurns = 0,
                        siValueText = "10"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nullPositionDto, out _, out var error))
                throw new InvalidOperationException("position=null 应被拒绝。");
            if (string.IsNullOrEmpty(error)) throw new InvalidOperationException("position=null 应返回错误信息。");
            if (error.IndexOf("source-001", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("position=null 错误信息应包含 InstanceId。");
        }

        // 验证显式 position=(0,0) 必须被允许且往返后保持不变。
        private static void ValidateDrawingPositionZeroAllowed()
        {
            var zeroPositionDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "10"
                    }
                }
            };
            if (!SpiceDrawingSerializer.TryFromDto(zeroPositionDto, out var restored, out var error))
                throw new InvalidOperationException("position=(0,0) 应被允许：" + error);
            var restoredSource = restored.FindComponent("source-001");
            if (restoredSource == null) throw new InvalidOperationException("往返后丢失组件。");
            if (restoredSource.Position != Vector2.zero) throw new InvalidOperationException("往返后 position 应保持 (0,0)。");
        }

        // Test A: 成功导入包含十类器件的完整电路，验证所有字段恢复正确。
        private static void ValidateDrawingImportSuccessFullCircuit()
        {
            var canvasRoot = new GameObject("SpiceImportSuccessValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 构建包含十类器件的图纸模型，含旋转和开关状态
                var sourceModel = new SpiceWorkspaceModel();
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", new Vector2(0f, 0f), 10d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.DcCurrentSource, "current-source-001", new Vector2(100f, 0f), 0.001d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", new Vector2(200f, 0f), 2000d, 1);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Capacitor, "capacitor-001", new Vector2(300f, 0f), 1e-6d, 2);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Inductor, "inductor-001", new Vector2(400f, 0f), 0.01d, 3);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Ground, "ground-001", new Vector2(0f, -100f), 0d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.IdealSwitch, "switch-001", new Vector2(100f, -100f), 1d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.SiliconDiode, "diode-001", new Vector2(200f, -100f), 0d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.VoltageProbe, "voltage-probe-001", new Vector2(300f, -100f), 0d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.CurrentProbe, "current-probe-001", new Vector2(400f, -100f), 0d, 0);
                sourceModel.RestoreInstanceNumbersFromExisting();

                // 添加 Auto Wire 和三折点 Manual Wire
                // Auto Wire：source-001 -> resistor-001（"resistor-001" < "source-001"，序列化会规范化方向）
                sourceModel.AddWire("source-001", "positive", "resistor-001", "positive");
                // Manual Wire：使用已规范化方向 ground-001 -> source-001（字典序较小者在 start），避免端点交换导致折点倒序
                var waypoints = new[] { new Vector2(50f, 0f), new Vector2(50f, 50f), new Vector2(150f, 50f) };
                sourceModel.AddWire("ground-001", "ground", "source-001", "negative", SpiceWireVisualState.Manual(waypoints));

                var json = SpiceDrawingSerializer.ToJson(sourceModel);

                if (!workspace.TryImportDrawingJson(json, out var error))
                    throw new InvalidOperationException("导入完整电路应成功：" + error);

                // 验证组件数量
                if (workspace.Model.Components.Count != 10)
                    throw new InvalidOperationException("导入后组件数量应为 10，实际 " + workspace.Model.Components.Count);

                // 验证各组件字段（InstanceId、Kind、Position、Rotation、SiValue）
                var source = workspace.Model.FindComponent("source-001");
                if (source == null || source.Kind != SpiceComponentKind.DcVoltageSource) throw new InvalidOperationException("导入后电压源丢失或类型错误。");
                if (source.Position != new Vector2(0f, 0f)) throw new InvalidOperationException("导入后电压源位置不匹配。");
                if (source.RotationQuarterTurns != 0) throw new InvalidOperationException("导入后电压源旋转不匹配。");
                if (Math.Abs(source.SiValue - 10d) > 1e-12) throw new InvalidOperationException("导入后电压源参数不匹配。");

                var resistor = workspace.Model.FindComponent("resistor-001");
                if (resistor == null || resistor.RotationQuarterTurns != 1) throw new InvalidOperationException("导入后电阻旋转应为 1。");
                if (Math.Abs(resistor.SiValue - 2000d) > 1e-12) throw new InvalidOperationException("导入后电阻参数不匹配。");

                var capacitor = workspace.Model.FindComponent("capacitor-001");
                if (capacitor == null || capacitor.RotationQuarterTurns != 2) throw new InvalidOperationException("导入后电容旋转应为 2。");

                var inductor = workspace.Model.FindComponent("inductor-001");
                if (inductor == null || inductor.RotationQuarterTurns != 3) throw new InvalidOperationException("导入后电感旋转应为 3。");

                // 验证开关状态（Closed = SiValue 1）
                var switchComponent = workspace.Model.FindComponent("switch-001");
                if (switchComponent == null || Math.Abs(switchComponent.SiValue - 1d) > 1e-12)
                    throw new InvalidOperationException("导入后开关状态不匹配（应为 Closed=1）。");

                // 验证 Wire 数量和路由模式
                if (workspace.Model.Wires.Count != 2) throw new InvalidOperationException("导入后导线数量应为 2。");
                SpiceWorkspaceWireData manualWire = null, autoWire = null;
                foreach (var wire in workspace.Model.Wires)
                {
                    if (wire.VisualState.RouteMode == SpiceWireRouteMode.Manual) manualWire = wire;
                    else if (wire.VisualState.RouteMode == SpiceWireRouteMode.Auto) autoWire = wire;
                }
                if (autoWire == null) throw new InvalidOperationException("导入后应存在 Auto Wire。");
                if (manualWire == null) throw new InvalidOperationException("导入后应存在 Manual Wire。");

                // 逐点验证 ManualRoutePoints 坐标与顺序（端点已规范化为 ground-001 -> source-001，无交换）
                if (manualWire.StartComponentId != "ground-001" || manualWire.StartTerminalId != "ground")
                    throw new InvalidOperationException("导入后 Manual Wire 起始端点不匹配：" + manualWire.StartComponentId + "/" + manualWire.StartTerminalId);
                if (manualWire.EndComponentId != "source-001" || manualWire.EndTerminalId != "negative")
                    throw new InvalidOperationException("导入后 Manual Wire 结束端点不匹配：" + manualWire.EndComponentId + "/" + manualWire.EndTerminalId);
                if (manualWire.VisualState.RouteMode != SpiceWireRouteMode.Manual)
                    throw new InvalidOperationException("导入后 Manual Wire RouteMode 应为 Manual。");
                if (manualWire.VisualState.Waypoints.Count != 3)
                    throw new InvalidOperationException("导入后 Manual Wire 折点数量应为 3，实际 " + manualWire.VisualState.Waypoints.Count);
                var wpTolerance = 1e-5f;
                if (Mathf.Abs(manualWire.VisualState.Waypoints[0].x - 50f) > wpTolerance || Mathf.Abs(manualWire.VisualState.Waypoints[0].y - 0f) > wpTolerance)
                    throw new InvalidOperationException("导入后 Manual Wire Waypoints[0] 应为 (50, 0)，实际 " + manualWire.VisualState.Waypoints[0]);
                if (Mathf.Abs(manualWire.VisualState.Waypoints[1].x - 50f) > wpTolerance || Mathf.Abs(manualWire.VisualState.Waypoints[1].y - 50f) > wpTolerance)
                    throw new InvalidOperationException("导入后 Manual Wire Waypoints[1] 应为 (50, 50)，实际 " + manualWire.VisualState.Waypoints[1]);
                if (Mathf.Abs(manualWire.VisualState.Waypoints[2].x - 150f) > wpTolerance || Mathf.Abs(manualWire.VisualState.Waypoints[2].y - 50f) > wpTolerance)
                    throw new InvalidOperationException("导入后 Manual Wire Waypoints[2] 应为 (150, 50)，实际 " + manualWire.VisualState.Waypoints[2]);

                // 验证 Auto Wire 仍为 Auto
                if (autoWire.VisualState.RouteMode != SpiceWireRouteMode.Auto)
                    throw new InvalidOperationException("导入后 Auto Wire RouteMode 应为 Auto。");

                // 验证编号恢复：导入 R1 后新建电阻应为 R2
                var newResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.up * 200f);
                if (newResistor.InstanceId != "resistor-002")
                    throw new InvalidOperationException("导入后新建电阻应为 resistor-002，实际 " + newResistor.InstanceId);

                // 验证结果状态和无 pending wire
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun)
                    throw new InvalidOperationException("导入后结果状态应为 NeverRun。");
                if (workspace.HasPendingWire)
                    throw new InvalidOperationException("导入后不应有 pending wire。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // Test B: 无效导入完全不破坏当前 Workspace。
        private static void ValidateDrawingImportFailurePreservesWorkspace()
        {
            var canvasRoot = new GameObject("SpiceImportFailureValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 建立非空旧画布
                var oldSource = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                var oldResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 100f);
                workspace.Connect(oldSource.InstanceId, "positive", oldResistor.InstanceId, "positive");
                var oldModel = workspace.Model;
                var oldComponentCount = oldModel.Components.Count;
                var oldWireCount = oldModel.Wires.Count;
                var oldResultState = workspace.ResultState;

                // 各种无效 JSON
                var invalidInputs = new[]
                {
                    "",                                                                                              // 空 JSON
                    "{\"format\":\"Wrong\",\"schemaVersion\":1,\"components\":[],\"wires\":[]}",                          // 错误 format
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":99,\"components\":[],\"wires\":[]}", // 未知 schemaVersion
                    // 缺失完整 position（x/y 为字符串字段，缺失时为 null，TryFromJson 必须拒绝）
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}",
                    // 非法 InstanceId
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"resistor-1\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1000\"}],\"wires\":[]}",
                    // 悬空 Wire 引用
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[],\"wires\":[{\"startComponentId\":\"resistor-001\",\"startTerminalId\":\"positive\",\"endComponentId\":\"resistor-002\",\"endTerminalId\":\"negative\",\"routeMode\":\"Auto\"}]}",
                    // 非法参数（0 欧姆电阻）
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"resistor-001\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0\"}],\"wires\":[]}"
                };

                for (var i = 0; i < invalidInputs.Length; i++)
                {
                    var invalidJson = invalidInputs[i];
                    if (workspace.TryImportDrawingJson(invalidJson, out var error))
                        throw new InvalidOperationException("无效 JSON #" + i + " 应被拒绝：" + invalidJson.Substring(0, Math.Min(80, invalidJson.Length)));
                    if (string.IsNullOrEmpty(error)) throw new InvalidOperationException("无效 JSON #" + i + " 应返回错误信息。");

                    // 旧 Model 引用不变
                    if (!ReferenceEquals(workspace.Model, oldModel))
                        throw new InvalidOperationException("失败导入后 Model 引用不应改变。");

                    // 旧画布数据不变
                    if (workspace.Model.Components.Count != oldComponentCount)
                        throw new InvalidOperationException("失败导入后组件数量不应改变。");
                    if (workspace.Model.Wires.Count != oldWireCount)
                        throw new InvalidOperationException("失败导入后导线数量不应改变。");

                    // 旧组件字段不变
                    var stillSource = workspace.Model.FindComponent(oldSource.InstanceId);
                    if (stillSource == null || stillSource.Position != oldSource.Position || stillSource.SiValue != oldSource.SiValue)
                        throw new InvalidOperationException("失败导入后旧组件字段不应改变。");

                    // 旧结果状态不变
                    if (workspace.ResultState != oldResultState)
                        throw new InvalidOperationException("失败导入后结果状态不应改变。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // Test C: 连续成功导入，最终只有第二次导入的内容。
        private static void ValidateDrawingImportConsecutiveSuccess()
        {
            var canvasRoot = new GameObject("SpiceImportConsecutiveValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 图纸 A：包含 source-001 和 resistor-001
                var modelA = new SpiceWorkspaceModel();
                modelA.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", Vector2.zero, 10d, 0);
                modelA.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.right, 1000d, 0);
                modelA.RestoreInstanceNumbersFromExisting();
                modelA.AddWire("source-001", "positive", "resistor-001", "positive");
                var jsonA = SpiceDrawingSerializer.ToJson(modelA);

                // 图纸 B：包含 capacitor-001 和 inductor-001
                var modelB = new SpiceWorkspaceModel();
                modelB.AddComponentWithIdentity(SpiceComponentKind.Capacitor, "capacitor-001", Vector2.left, 1e-6d, 0);
                modelB.AddComponentWithIdentity(SpiceComponentKind.Inductor, "inductor-001", Vector2.right, 0.01d, 0);
                modelB.RestoreInstanceNumbersFromExisting();
                modelB.AddWire("capacitor-001", "positive", "inductor-001", "positive");
                var jsonB = SpiceDrawingSerializer.ToJson(modelB);

                // 先导入 A
                if (!workspace.TryImportDrawingJson(jsonA, out var errorA))
                    throw new InvalidOperationException("导入图纸 A 应成功：" + errorA);
                if (workspace.Model.Components.Count != 2 || workspace.Model.FindComponent("source-001") == null)
                    throw new InvalidOperationException("导入 A 后应包含 source-001。");

                // 再导入 B
                if (!workspace.TryImportDrawingJson(jsonB, out var errorB))
                    throw new InvalidOperationException("导入图纸 B 应成功：" + errorB);

                // 最终只有 B 的内容
                if (workspace.Model.Components.Count != 2)
                    throw new InvalidOperationException("导入 B 后组件数量应为 2。");
                if (workspace.Model.FindComponent("source-001") != null)
                    throw new InvalidOperationException("导入 B 后不应残留 source-001。");
                if (workspace.Model.FindComponent("resistor-001") != null)
                    throw new InvalidOperationException("导入 B 后不应残留 resistor-001。");
                if (workspace.Model.FindComponent("capacitor-001") == null)
                    throw new InvalidOperationException("导入 B 后应包含 capacitor-001。");
                if (workspace.Model.FindComponent("inductor-001") == null)
                    throw new InvalidOperationException("导入 B 后应包含 inductor-001。");
                if (workspace.Model.Wires.Count != 1)
                    throw new InvalidOperationException("导入 B 后导线数量应为 1。");

                // 验证编号恢复：新建电容应为 capacitor-002
                var newCapacitor = workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.up);
                if (newCapacitor.InstanceId != "capacitor-002")
                    throw new InvalidOperationException("导入 B 后新建电容应为 capacitor-002，实际 " + newCapacitor.InstanceId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // Test D: 空图纸导入，画布清空，编号从 1 开始。
        private static void ValidateDrawingImportEmptyDrawing()
        {
            var canvasRoot = new GameObject("SpiceImportEmptyValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 先建立非空画布
                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right);

                // 导入空图纸
                var emptyJson = SpiceDrawingSerializer.ToJson(new SpiceWorkspaceModel());
                if (!workspace.TryImportDrawingJson(emptyJson, out var error))
                    throw new InvalidOperationException("导入空图纸应成功：" + error);

                // 画布为空
                if (workspace.Model.Components.Count != 0)
                    throw new InvalidOperationException("导入空图纸后组件数量应为 0。");
                if (workspace.Model.Wires.Count != 0)
                    throw new InvalidOperationException("导入空图纸后导线数量应为 0。");

                // 编号从 1 开始
                var newResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                if (newResistor.InstanceId != "resistor-001")
                    throw new InvalidOperationException("空图纸导入后新建电阻应为 resistor-001，实际 " + newResistor.InstanceId);

                // 结果状态为 NeverRun
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun)
                    throw new InvalidOperationException("空图纸导入后结果状态应为 NeverRun。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // Test E: 仿真计算进行中（Running）禁止导入，保留当前计算和状态。
        private static void ValidateDrawingImportRejectedWhileRunning()
        {
            var canvasRoot = new GameObject("SpiceImportRunningGuardValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 建立非空旧画布
                var oldSource = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                var oldResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 100f);
                workspace.Connect(oldSource.InstanceId, "positive", oldResistor.InstanceId, "positive");
                var oldModel = workspace.Model;
                var oldComponentCount = oldModel.Components.Count;
                var oldWireCount = oldModel.Wires.Count;

                // 构造一个合法的导入 JSON
                var importModel = new SpiceWorkspaceModel();
                importModel.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", Vector2.left, 5d, 0);
                importModel.RestoreInstanceNumbersFromExisting();
                var validJson = SpiceDrawingSerializer.ToJson(importModel);

                // 设置 Running 状态（不实际启动 ngspice）
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);
                if (workspace.ResultState != SpiceWorkspaceResultState.Running)
                    throw new InvalidOperationException("测试前置：无法设置 Running 状态。");

                // 尝试导入合法 JSON，应被拒绝
                if (workspace.TryImportDrawingJson(validJson, out var error))
                    throw new InvalidOperationException("Running 状态下导入应被拒绝。");
                if (string.IsNullOrEmpty(error))
                    throw new InvalidOperationException("Running 状态下应返回错误信息。");
                // 错误信息应包含"计算"或"运行"
                if (error.IndexOf("计算", StringComparison.Ordinal) < 0 && error.IndexOf("运行", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Running 状态错误信息应包含'计算'或'运行'，实际：" + error);

                // Model 引用不变
                if (!ReferenceEquals(workspace.Model, oldModel))
                    throw new InvalidOperationException("Running 拒绝导入后 Model 引用不应改变。");
                // 组件/Wire 数量不变
                if (workspace.Model.Components.Count != oldComponentCount)
                    throw new InvalidOperationException("Running 拒绝导入后组件数量不应改变。");
                if (workspace.Model.Wires.Count != oldWireCount)
                    throw new InvalidOperationException("Running 拒绝导入后导线数量不应改变。");
                // ResultState 仍为 Running
                if (workspace.ResultState != SpiceWorkspaceResultState.Running)
                    throw new InvalidOperationException("Running 拒绝导入后 ResultState 仍应为 Running。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // Test F: 真实 JSON 缺失 position 必须被 TryFromJson 拒绝。
        // 覆盖：缺失完整 position、缺失 x、缺失 y、显式 (0,0) 成功、DTO null position 继续拒绝。
        private static void ValidateDrawingImportMissingPositionRejected()
        {
            // A. 缺失完整 position
            var missingPositionJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(missingPositionJson, out _, out var errorA))
                throw new InvalidOperationException("缺失完整 position 的 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(errorA)) throw new InvalidOperationException("缺失 position 应返回错误信息。");

            // B. position 存在但缺失 x（JsonUtility 将缺失的 string 字段置为 null）
            var missingXJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(missingXJson, out _, out var errorB))
                throw new InvalidOperationException("缺失 position.x 的 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(errorB)) throw new InvalidOperationException("缺失 position.x 应返回错误信息。");

            // C. position 存在但缺失 y
            var missingYJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(missingYJson, out _, out var errorC))
                throw new InvalidOperationException("缺失 position.y 的 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(errorC)) throw new InvalidOperationException("缺失 position.y 应返回错误信息。");

            // D. position x/y 显式为零必须成功
            var zeroPositionJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (!SpiceDrawingSerializer.TryFromJson(zeroPositionJson, out var zeroModel, out var errorD))
                throw new InvalidOperationException("显式 (0,0) position 应被允许：" + errorD);
            var zeroSource = zeroModel.FindComponent("source-001");
            if (zeroSource == null || zeroSource.Position != Vector2.zero)
                throw new InvalidOperationException("显式 (0,0) position 导入后位置应为 (0,0)。");

            // E. DTO 级 position = null 继续被拒绝
            var nullPositionDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = null,
                        rotationQuarterTurns = 0,
                        siValueText = "10"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nullPositionDto, out _, out var errorE))
                throw new InvalidOperationException("DTO 级 position=null 应继续被拒绝。");
            if (string.IsNullOrEmpty(errorE)) throw new InvalidOperationException("position=null 应返回错误信息。");
        }

        // ===== Batch C1：文件操作核心与路径会话状态 =====

        // 扩展名规范化：无扩展名补 .spicejson；.spicejson/.SPICEJSON 不重复追加；非 .spicejson 保持不改。
        private static void ValidateDrawingFileServiceExtensionNormalization()
        {
            if (SpiceDrawingFileService.NormalizeExtension("circuit") != "circuit.spicejson")
                throw new InvalidOperationException("无扩展名应自动追加 .spicejson。");
            if (SpiceDrawingFileService.NormalizeExtension("circuit.spicejson") != "circuit.spicejson")
                throw new InvalidOperationException(".spicejson 不应重复追加。");
            if (SpiceDrawingFileService.NormalizeExtension("circuit.SPICEJSON") != "circuit.SPICEJSON")
                throw new InvalidOperationException(".SPICEJSON 不应重复追加。");
            if (SpiceDrawingFileService.NormalizeExtension("circuit.txt") != "circuit.txt")
                throw new InvalidOperationException("非 .spicejson 的现有扩展名应保持不改。");
            if (SpiceDrawingFileService.NormalizeExtension("path/with/dir/circuit") != "path/with/dir/circuit.spicejson")
                throw new InvalidOperationException("带目录的无扩展名路径应自动追加 .spicejson。");
        }

        // 保存有效电路到新路径：文件存在、非空、UTF-8、format/schemaVersion 正确、可被 Batch B 导入、不改变原 Workspace。
        private static void ValidateDrawingFileSaveRoundTrip()
        {
            var tempDir = CreateUniqueTempDir("SaveRoundTrip");
            try
            {
                var canvasRoot = new GameObject("SpiceFileSaveRoundTrip", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 50f);
                    var sourceComp = workspace.Model.Components[0];
                    var resistorComp = workspace.Model.Components[1];
                    workspace.Connect(sourceComp.InstanceId, "positive", resistorComp.InstanceId, "positive");

                    var beforeModel = workspace.Model;
                    var beforeComponentCount = workspace.Model.Components.Count;
                    var beforeWireCount = workspace.Model.Wires.Count;

                    var path = Path.Combine(tempDir, "round_trip");
                    if (!workspace.TrySaveWorkspaceToPath(path, out var error))
                        throw new InvalidOperationException("保存应成功：" + error);

                    // 文件存在、非空
                    var savedPath = Path.Combine(tempDir, "round_trip.spicejson");
                    if (!File.Exists(savedPath)) throw new InvalidOperationException("保存后文件应存在。");
                    var fileBytes = new FileInfo(savedPath).Length;
                    if (fileBytes <= 0) throw new InvalidOperationException("保存的文件不应为空。");

                    // UTF-8 可读
                    var json = File.ReadAllText(savedPath, System.Text.Encoding.UTF8);
                    if (!json.Contains("\"format\": \"ElectricalSimulation2D.SpiceDrawing\""))
                        throw new InvalidOperationException("保存的 JSON 应包含正确的 format。");
                    if (!json.Contains("\"schemaVersion\": 1"))
                        throw new InvalidOperationException("保存的 JSON 应包含 schemaVersion=1。");

                    // 保存不改变原 Workspace
                    if (!ReferenceEquals(workspace.Model, beforeModel))
                        throw new InvalidOperationException("保存不应改变 Model 引用。");
                    if (workspace.Model.Components.Count != beforeComponentCount)
                        throw new InvalidOperationException("保存不应改变组件数量。");
                    if (workspace.Model.Wires.Count != beforeWireCount)
                        throw new InvalidOperationException("保存不应改变导线数量。");

                    // 当前路径已更新为规范化路径
                    if (workspace.CurrentSpiceFilePath != savedPath)
                        throw new InvalidOperationException("保存后当前路径应为规范化路径：" + savedPath + " 实际 " + workspace.CurrentSpiceFilePath);

                    // 内容可被 Batch B 导入（用另一个工作区导入）
                    var importRoot = new GameObject("SpiceFileSaveRoundTripImport", typeof(RectTransform), typeof(Canvas));
                    try
                    {
                        var importWorkspace = CreateInitializedWorkspaceForCopy(importRoot.transform, out _);
                        if (!importWorkspace.TryImportWorkspaceFromPath(savedPath, out var importError))
                            throw new InvalidOperationException("保存的文件应可被导入：" + importError);
                        if (importWorkspace.Model.Components.Count != beforeComponentCount)
                            throw new InvalidOperationException("导入后组件数量应匹配。");
                        if (importWorkspace.Model.Wires.Count != beforeWireCount)
                            throw new InvalidOperationException("导入后导线数量应匹配。");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(importRoot);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 覆盖已有文件：最终文件为新 JSON、无 .tmp 残留、不产生 0 字节文件。
        private static void ValidateDrawingFileSaveOverwrite()
        {
            var tempDir = CreateUniqueTempDir("SaveOverwrite");
            try
            {
                var canvasRoot = new GameObject("SpiceFileSaveOverwrite", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    var path = Path.Combine(tempDir, "overwrite.spicejson");

                    // 第一次保存：1 个电阻
                    workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                    if (!workspace.TrySaveWorkspaceToPath(path, out var error1))
                        throw new InvalidOperationException("第一次保存应成功：" + error1);
                    var firstContent = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    var firstBytes = new FileInfo(path).Length;

                    // 第二次保存：清空后改为 2 个电阻，覆盖
                    workspace.ClearWorkspace();
                    workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                    workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 50f);
                    if (!workspace.TrySaveWorkspaceToPath(path, out var error2))
                        throw new InvalidOperationException("第二次保存（覆盖）应成功：" + error2);
                    var secondContent = File.ReadAllText(path, System.Text.Encoding.UTF8);
                    var secondBytes = new FileInfo(path).Length;

                    if (firstContent == secondContent)
                        throw new InvalidOperationException("覆盖后文件内容应不同。");
                    if (secondBytes <= 0)
                        throw new InvalidOperationException("覆盖后不应产生 0 字节文件。");
                    if (firstBytes == secondBytes)
                        throw new InvalidOperationException("覆盖后文件大小应反映新内容。");

                    // 无 .tmp 残留
                    var tmpFiles = Directory.GetFiles(tempDir, "*.tmp*", SearchOption.TopDirectoryOnly);
                    if (tmpFiles.Length > 0)
                        throw new InvalidOperationException("覆盖后不应残留 .tmp 文件：" + tmpFiles.Length + " 个。");
                    var backupFiles = Directory.GetFiles(tempDir, "*.backup*", SearchOption.TopDirectoryOnly);
                    if (backupFiles.Length > 0)
                        throw new InvalidOperationException("覆盖后不应残留 .backup 文件：" + backupFiles.Length + " 个。");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 当前路径管理：SaveToPath 设置路径；TrySaveCurrentWorkspace 写回；切换路径；保存失败不改旧路径。
        private static void ValidateDrawingFileCurrentPathManagement()
        {
            var tempDir = CreateUniqueTempDir("CurrentPath");
            try
            {
                var canvasRoot = new GameObject("SpiceFilePathManagement", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    if (workspace.HasCurrentSpiceFilePath)
                        throw new InvalidOperationException("初始状态不应有当前路径。");

                    // 空路径时 TrySaveCurrentWorkspace 失败
                    if (workspace.TrySaveCurrentWorkspace(out var emptyError))
                        throw new InvalidOperationException("空路径时 TrySaveCurrentWorkspace 应失败。");
                    if (string.IsNullOrEmpty(emptyError))
                        throw new InvalidOperationException("空路径应返回错误信息。");

                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var path1 = Path.Combine(tempDir, "file1");
                    if (!workspace.TrySaveWorkspaceToPath(path1, out var error1))
                        throw new InvalidOperationException("保存到 path1 应成功：" + error1);
                    if (!workspace.HasCurrentSpiceFilePath)
                        throw new InvalidOperationException("保存后应有当前路径。");

                    // TrySaveCurrentWorkspace 写回该路径
                    if (!workspace.TrySaveCurrentWorkspace(out var error2))
                        throw new InvalidOperationException("TrySaveCurrentWorkspace 应成功：" + error2);

                    // 另一个 SaveToPath 成功后切换为新路径
                    var path2 = Path.Combine(tempDir, "file2.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(path2, out var error3))
                        throw new InvalidOperationException("保存到 path2 应成功：" + error3);
                    if (workspace.CurrentSpiceFilePath != path2)
                        throw new InvalidOperationException("当前路径应切换为 path2：" + workspace.CurrentSpiceFilePath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 导入失败：损坏 JSON、未知版本、缺失 position/x/y、非法 Wire 均失败；失败后 Workspace 和当前路径不变。
        private static void ValidateDrawingFileImportFailurePreservesWorkspace()
        {
            var tempDir = CreateUniqueTempDir("ImportFailure");
            try
            {
                var canvasRoot = new GameObject("SpiceFileImportFailure", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    // 建立非空旧画布并保存到一个文件，确立当前路径
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var validPath = Path.Combine(tempDir, "valid.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(validPath, out _))
                        throw new InvalidOperationException("测试前置：保存应成功。");

                    var oldModel = workspace.Model;
                    var oldComponentCount = workspace.Model.Components.Count;
                    var oldPath = workspace.CurrentSpiceFilePath;

                    // 各种无效文件内容
                    var invalidCases = new (string name, string content)[]
                    {
                        ("损坏 JSON", "{ this is not valid json"),
                        ("未知 schemaVersion", "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":99,\"components\":[],\"wires\":[]}"),
                        ("缺失 position", "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}"),
                        ("缺失 position.x", "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}"),
                        ("非法 Wire", "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[],\"wires\":[{\"startComponentId\":\"resistor-001\",\"startTerminalId\":\"positive\",\"endComponentId\":\"resistor-002\",\"endTerminalId\":\"negative\",\"routeMode\":\"Auto\"}]}"),
                    };

                    foreach (var (name, content) in invalidCases)
                    {
                        var badPath = Path.Combine(tempDir, "bad_" + name.GetHashCode() + ".spicejson");
                        File.WriteAllText(badPath, content, System.Text.Encoding.UTF8);

                        if (workspace.TryImportWorkspaceFromPath(badPath, out var error))
                            throw new InvalidOperationException("[" + name + "] 导入应失败。");
                        if (string.IsNullOrEmpty(error))
                            throw new InvalidOperationException("[" + name + "] 应返回错误信息。");

                        // Workspace 和当前路径不变
                        if (!ReferenceEquals(workspace.Model, oldModel))
                            throw new InvalidOperationException("[" + name + "] Model 引用不应改变。");
                        if (workspace.Model.Components.Count != oldComponentCount)
                            throw new InvalidOperationException("[" + name + "] 组件数量不应改变。");
                        if (workspace.CurrentSpiceFilePath != oldPath)
                            throw new InvalidOperationException("[" + name + "] 当前路径不应改变。");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 运行中保护：保存和导入均在读取/写入前拒绝；导入传不存在路径错误仍为"计算进行中"。
        private static void ValidateDrawingFileRunningGuardBeforeFileAccess()
        {
            var tempDir = CreateUniqueTempDir("RunningGuard");
            try
            {
                var canvasRoot = new GameObject("SpiceFileRunningGuard", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);

                    workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);

                    // 保存拒绝：不创建目录、不写临时文件
                    var savePath = Path.Combine(tempDir, "should_not_exist.spicejson");
                    if (workspace.TrySaveWorkspaceToPath(savePath, out var saveError))
                        throw new InvalidOperationException("Running 状态下保存应被拒绝。");
                    if (File.Exists(savePath))
                        throw new InvalidOperationException("Running 拒绝保存不应创建文件。");
                    if (saveError.IndexOf("计算", StringComparison.Ordinal) < 0 && saveError.IndexOf("运行", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("Running 保存错误应包含'计算'或'运行'：" + saveError);

                    // 导入拒绝：传不存在的路径，错误仍必须是"计算进行中"而不是"文件不存在"
                    var importPath = Path.Combine(tempDir, "definitely_does_not_exist.spicejson");
                    if (workspace.TryImportWorkspaceFromPath(importPath, out var importError))
                        throw new InvalidOperationException("Running 状态下导入应被拒绝。");
                    if (importError.IndexOf("计算", StringComparison.Ordinal) < 0 && importError.IndexOf("运行", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("Running 导入错误应为'计算进行中'而非'文件不存在'：" + importError);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 清空：保存或导入后执行 ClearWorkspace；CurrentSpiceFilePath 变 null；TrySaveCurrentWorkspace 失败提示另存。
        private static void ValidateDrawingFileClearResetsPath()
        {
            var tempDir = CreateUniqueTempDir("ClearResetsPath");
            try
            {
                var canvasRoot = new GameObject("SpiceFileClearReset", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var path = Path.Combine(tempDir, "clear_test.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(path, out _))
                        throw new InvalidOperationException("测试前置：保存应成功。");
                    if (!workspace.HasCurrentSpiceFilePath)
                        throw new InvalidOperationException("保存后应有当前路径。");

                    workspace.ClearWorkspace();

                    if (workspace.HasCurrentSpiceFilePath)
                        throw new InvalidOperationException("清空后当前路径应已清除。");
                    if (workspace.Model.Components.Count != 0)
                        throw new InvalidOperationException("清空后画布应为空。");

                    // 再调用 TrySaveCurrentWorkspace 必须失败且提示需要另存路径
                    if (workspace.TrySaveCurrentWorkspace(out var error))
                        throw new InvalidOperationException("清空后无路径时 TrySaveCurrentWorkspace 应失败。");
                    if (string.IsNullOrEmpty(error) || error.IndexOf("另存", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("清空后错误应提示另存路径：" + error);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 中文与空格路径：临时目录包含中文和空格；保存与导入都成功。
        private static void ValidateDrawingFileChineseAndSpacePath()
        {
            var tempDir = CreateUniqueTempDir("中文 路径 测试");
            try
            {
                var canvasRoot = new GameObject("SpiceFileChinesePath", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 50f);

                    var path = Path.Combine(tempDir, "我的 电路.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(path, out var saveError))
                        throw new InvalidOperationException("中文+空格路径保存应成功：" + saveError);
                    if (!File.Exists(path))
                        throw new InvalidOperationException("中文+空格路径保存后文件应存在。");

                    // 导入
                    var importRoot = new GameObject("SpiceFileChinesePathImport", typeof(RectTransform), typeof(Canvas));
                    try
                    {
                        var importWorkspace = CreateInitializedWorkspaceForCopy(importRoot.transform, out _);
                        if (!importWorkspace.TryImportWorkspaceFromPath(path, out var importError))
                            throw new InvalidOperationException("中文+空格路径导入应成功：" + importError);
                        if (importWorkspace.Model.Components.Count != 2)
                            throw new InvalidOperationException("中文+空格路径导入后组件数量应匹配。");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(importRoot);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 保存失败：目录本身作为目标文件的无效路径；保存必须失败；CurrentSpiceFilePath 不变；原文件内容不变；无 .tmp/.backup 残留。
        private static void ValidateDrawingFileSaveFailurePreservesPath()
        {
            var tempDir = CreateUniqueTempDir("SaveFailure");
            try
            {
                var canvasRoot = new GameObject("SpiceFileSaveFailure", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);

                    // 先成功保存到有效路径 A
                    var validPath = Path.Combine(tempDir, "valid");
                    if (!workspace.TrySaveWorkspaceToPath(validPath, out var error1))
                        throw new InvalidOperationException("测试前置：保存到有效路径应成功：" + error1);
                    var savedPathA = Path.Combine(tempDir, "valid.spicejson");
                    var originalContent = File.ReadAllText(savedPathA, System.Text.Encoding.UTF8);
                    var currentPath = workspace.CurrentSpiceFilePath;

                    // 尝试保存到一个“目录本身作为目标文件”的无效路径。
                    // 目录名必须以 .spicejson 结尾，否则 NormalizeExtension 会追加扩展名，
                    // 使实际写入目标变成 tempDir/subdir.spicejson（父目录下的普通文件），保存反而成功。
                    var invalidPath = Path.Combine(tempDir, "subdir.spicejson");
                    Directory.CreateDirectory(invalidPath); // invalidPath 现在是一个目录且扩展名已规范化
                    if (workspace.TrySaveWorkspaceToPath(invalidPath, out var error2))
                        throw new InvalidOperationException("保存到目录路径应失败。");
                    if (string.IsNullOrEmpty(error2))
                        throw new InvalidOperationException("保存失败应返回错误信息。");

                    // CurrentSpiceFilePath 仍等于 A
                    if (workspace.CurrentSpiceFilePath != currentPath)
                        throw new InvalidOperationException("保存失败后 CurrentSpiceFilePath 应不变：" + workspace.CurrentSpiceFilePath);

                    // A 的原文件内容不变
                    var afterContent = File.ReadAllText(savedPathA, System.Text.Encoding.UTF8);
                    if (afterContent != originalContent)
                        throw new InvalidOperationException("保存失败后原文件内容不应改变。");

                    // 无 .tmp 或 .backup 残留
                    var tmpFiles = Directory.GetFiles(tempDir, "*.tmp*", SearchOption.AllDirectories);
                    if (tmpFiles.Length > 0)
                        throw new InvalidOperationException("保存失败不应残留 .tmp 文件：" + tmpFiles.Length + " 个。");
                    var backupFiles = Directory.GetFiles(tempDir, "*.backup*", SearchOption.AllDirectories);
                    if (backupFiles.Length > 0)
                        throw new InvalidOperationException("保存失败不应残留 .backup 文件：" + backupFiles.Length + " 个。");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 超大文件：创建大于 MaxFileBytes 的 .spicejson；导入必须失败；错误包含“1MB”或“超过”；Workspace 与路径不变。
        private static void ValidateDrawingFileOversizedImportRejected()
        {
            var tempDir = CreateUniqueTempDir("Oversized");
            try
            {
                var canvasRoot = new GameObject("SpiceFileOversized", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    // 建立非空旧画布并确立当前路径
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var validPath = Path.Combine(tempDir, "valid.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(validPath, out _))
                        throw new InvalidOperationException("测试前置：保存应成功。");
                    var oldModel = workspace.Model;
                    var oldComponentCount = workspace.Model.Components.Count;
                    var oldPath = workspace.CurrentSpiceFilePath;

                    // 创建大于 MaxFileBytes 的文件
                    var oversizedPath = Path.Combine(tempDir, "oversized.spicejson");
                    var oversizedBytes = new byte[SpiceDrawingFileService.MaxFileBytes + 1];
                    for (var i = 0; i < oversizedBytes.Length; i++) oversizedBytes[i] = (byte)'a';
                    File.WriteAllBytes(oversizedPath, oversizedBytes);

                    if (workspace.TryImportWorkspaceFromPath(oversizedPath, out var error))
                        throw new InvalidOperationException("超大文件导入应失败。");
                    if (string.IsNullOrEmpty(error))
                        throw new InvalidOperationException("超大文件应返回错误信息。");
                    if (error.IndexOf("1MB", StringComparison.Ordinal) < 0 && error.IndexOf("超过", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("超大文件错误应包含'1MB'或'超过'：" + error);

                    // Workspace 与路径不变
                    if (!ReferenceEquals(workspace.Model, oldModel))
                        throw new InvalidOperationException("超大文件拒绝后 Model 引用不应改变。");
                    if (workspace.Model.Components.Count != oldComponentCount)
                        throw new InvalidOperationException("超大文件拒绝后组件数量不应改变。");
                    if (workspace.CurrentSpiceFilePath != oldPath)
                        throw new InvalidOperationException("超大文件拒绝后当前路径不应改变。");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 非法 UTF-8：写入包含非法字节序列（0xC3, 0x28）的文件；导入必须失败；错误包含“UTF-8”；Workspace 与路径不变。
        private static void ValidateDrawingFileInvalidUtf8ImportRejected()
        {
            var tempDir = CreateUniqueTempDir("InvalidUtf8");
            try
            {
                var canvasRoot = new GameObject("SpiceFileInvalidUtf8", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var validPath = Path.Combine(tempDir, "valid.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(validPath, out _))
                        throw new InvalidOperationException("测试前置：保存应成功。");
                    var oldModel = workspace.Model;
                    var oldPath = workspace.CurrentSpiceFilePath;

                    // 写入非法 UTF-8 字节序列 0xC3 0x28
                    var invalidPath = Path.Combine(tempDir, "invalid_utf8.spicejson");
                    File.WriteAllBytes(invalidPath, new byte[] { 0xC3, 0x28, 0x7B, 0x7D });

                    if (workspace.TryImportWorkspaceFromPath(invalidPath, out var error))
                        throw new InvalidOperationException("非法 UTF-8 文件导入应失败。");
                    if (string.IsNullOrEmpty(error))
                        throw new InvalidOperationException("非法 UTF-8 应返回错误信息。");
                    if (error.IndexOf("UTF-8", StringComparison.Ordinal) < 0)
                        throw new InvalidOperationException("非法 UTF-8 错误应包含'UTF-8'：" + error);

                    if (!ReferenceEquals(workspace.Model, oldModel))
                        throw new InvalidOperationException("非法 UTF-8 拒绝后 Model 引用不应改变。");
                    if (workspace.CurrentSpiceFilePath != oldPath)
                        throw new InvalidOperationException("非法 UTF-8 拒绝后当前路径不应改变。");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 缺失 position.y：真实 JSON 文件，position 只有 x 没有 y；路径级导入必须失败；Workspace 与路径不变。
        private static void ValidateDrawingFileMissingPositionYRejected()
        {
            var tempDir = CreateUniqueTempDir("MissingPosY");
            try
            {
                var canvasRoot = new GameObject("SpiceFileMissingPosY", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var validPath = Path.Combine(tempDir, "valid.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(validPath, out _))
                        throw new InvalidOperationException("测试前置：保存应成功。");
                    var oldModel = workspace.Model;
                    var oldPath = workspace.CurrentSpiceFilePath;

                    // 写入缺失 position.y 的 JSON
                    var missingYJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
                    var badPath = Path.Combine(tempDir, "missing_y.spicejson");
                    File.WriteAllText(badPath, missingYJson, System.Text.Encoding.UTF8);

                    if (workspace.TryImportWorkspaceFromPath(badPath, out var error))
                        throw new InvalidOperationException("缺失 position.y 的文件导入应失败。");
                    if (string.IsNullOrEmpty(error))
                        throw new InvalidOperationException("缺失 position.y 应返回错误信息。");

                    if (!ReferenceEquals(workspace.Model, oldModel))
                        throw new InvalidOperationException("缺失 position.y 拒绝后 Model 引用不应改变。");
                    if (workspace.CurrentSpiceFilePath != oldPath)
                        throw new InvalidOperationException("缺失 position.y 拒绝后当前路径不应改变。");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 成功路径级操作不得写入 statusText：保存成功、导入成功后验证 C1 没有写“已保存到：绝对路径”或“已从以下路径导入：绝对路径”。
        // 注意：Batch B 的 CommitImportedModel 在导入提交阶段会合法地把 statusText 重置为“未计算”（清空旧结果），
        // 这不属于 C1 的成功 UI 文案。本测试只验证 C1 没有写入“已保存到”/“已从以下路径导入”/完整路径三类成功提示。
        private static void ValidateDrawingFileSuccessDoesNotWriteStatusText()
        {
            var tempDir = CreateUniqueTempDir("NoStatusText");
            try
            {
                var canvasRoot = new GameObject("SpiceFileNoStatusText", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);

                    // 设置状态栏初始文本为已知标记
                    var marker = "T3_MARKER_BEFORE_FILE_OP";
                    workspace.SetStatusTextForTesting(marker);

                    // 保存成功后状态栏不应被 C1 改写（TrySaveWorkspaceToPath 不调用 CommitImportedModel）
                    var savePath = Path.Combine(tempDir, "save");
                    if (!workspace.TrySaveWorkspaceToPath(savePath, out var saveError))
                        throw new InvalidOperationException("保存应成功：" + saveError);
                    var statusAfterSave = workspace.GetStatusTextForTesting();
                    if (statusAfterSave != marker)
                        throw new InvalidOperationException("C1 保存成功后不应写入 statusText。预期：" + marker + " 实际：" + statusAfterSave);
                    AssertNoSuccessUiText(statusAfterSave, savePath, "保存");

                    // 导入成功后状态栏可被 Batch B 的 CommitImportedModel 重置为“未计算”，
                    // 但 C1 不得写入“已从以下路径导入：绝对路径”之类的成功 UI 文案。
                    var savedFile = Path.Combine(tempDir, "save.spicejson");
                    var importRoot = new GameObject("SpiceFileNoStatusTextImport", typeof(RectTransform), typeof(Canvas));
                    try
                    {
                        var importWorkspace = CreateInitializedWorkspaceForCopy(importRoot.transform, out _);
                        importWorkspace.SetStatusTextForTesting(marker);
                        if (!importWorkspace.TryImportWorkspaceFromPath(savedFile, out var importError))
                            throw new InvalidOperationException("导入应成功：" + importError);
                        var statusAfterImport = importWorkspace.GetStatusTextForTesting();
                        AssertNoSuccessUiText(statusAfterImport, savedFile, "导入");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(importRoot);
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(canvasRoot);
                }
            }
            finally
            {
                CleanupTempDir(tempDir);
            }
        }

        // 校验 statusText 不包含 C1 的成功 UI 文案：“已保存到”、“已从以下路径导入”、或完整绝对路径本身。
        private static void AssertNoSuccessUiText(string statusText, string fullPath, string operation)
        {
            if (statusText == null) return;
            if (statusText.IndexOf("已保存到", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("C1 " + operation + " 不应写入“已保存到”文案：" + statusText);
            if (statusText.IndexOf("已从以下路径导入", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("C1 " + operation + " 不应写入“已从以下路径导入”文案：" + statusText);
            if (!string.IsNullOrEmpty(fullPath) && statusText.IndexOf(fullPath, StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("C1 " + operation + " 不应在 statusText 暴露完整路径：" + statusText);
        }

        // 创建唯一临时目录（可包含中文/空格），位于系统 Temp 下，避免污染仓库。
        private static string CreateUniqueTempDir(string label)
        {
            var path = Path.Combine(Path.GetTempPath(), "SpiceT3_" + label + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        // 清理临时目录及其所有文件。测试不得把临时 JSON 留在仓库。
        private static void CleanupTempDir(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception exception)
            {
                Console.WriteLine("[SpiceT3] 清理临时目录失败：" + path + " " + exception);
            }
        }
    }
}
