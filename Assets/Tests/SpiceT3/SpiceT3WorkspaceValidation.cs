using System;
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
            if (restoredWire.StartComponentId != source.InstanceId || restoredWire.StartTerminalId != "positive") throw new InvalidOperationException("往返后导线起点不匹配。");
            if (restoredWire.EndComponentId != resistor.InstanceId || restoredWire.EndTerminalId != "positive") throw new InvalidOperationException("往返后导线终点不匹配。");
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
            var expectedKinds = new[]
            {
                SpiceComponentKind.DcVoltageSource, SpiceComponentKind.DcCurrentSource,
                SpiceComponentKind.Resistor, SpiceComponentKind.Capacitor,
                SpiceComponentKind.Inductor, SpiceComponentKind.Ground,
                SpiceComponentKind.IdealSwitch, SpiceComponentKind.SiliconDiode,
                SpiceComponentKind.VoltageProbe, SpiceComponentKind.CurrentProbe
            };
            for (var i = 0; i < 10; i++)
            {
                if (restored.Components[i].Kind != expectedKinds[i])
                    throw new InvalidOperationException("10 类器件往返后第 " + i + " 个器件类型不匹配。");
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
            if (restored.Wires[0].VisualState.RouteMode != SpiceWireRouteMode.Auto) throw new InvalidOperationException("自动路由模式往返后不匹配。");
            if (restored.Wires[1].VisualState.RouteMode != SpiceWireRouteMode.Manual) throw new InvalidOperationException("手工路由模式往返后不匹配。");
            if (restored.Wires[1].VisualState.Waypoints.Count != 3) throw new InvalidOperationException("手工折点数量往返后不匹配。");
            if (restored.Wires[1].VisualState.Waypoints[1] != new Vector2(40f, 80f)) throw new InvalidOperationException("手工折点坐标往返后不匹配。");
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
                position = new SpiceVector2Dto { x = 1f, y = 1f },
                rotationQuarterTurns = 0,
                siValue = 2000d
            });
            if (SpiceDrawingSerializer.TryFromDto(dto, out _, out var error3))
                throw new InvalidOperationException("重复 InstanceId 应被拒绝。");
            if (string.IsNullOrEmpty(error3)) throw new InvalidOperationException("重复 InstanceId 应返回错误信息。");

            // 未知器件类型
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
                        position = new SpiceVector2Dto { x = 0f, y = 0f },
                        rotationQuarterTurns = 0,
                        siValue = 0d
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(unknownTypeDto, out _, out var error4))
                throw new InvalidOperationException("未知器件类型应被拒绝。");
            if (string.IsNullOrEmpty(error4)) throw new InvalidOperationException("未知器件类型应返回错误信息。");

            // 非法参数（0 欧姆电阻）
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
                        position = new SpiceVector2Dto { x = 0f, y = 0f },
                        rotationQuarterTurns = 0,
                        siValue = 0d
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
        }
    }
}
