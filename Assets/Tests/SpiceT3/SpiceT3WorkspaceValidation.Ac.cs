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
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// T3 工作区自动验证。partial 文件按功能分组，RunPureChecks 保持唯一且明确的回归执行顺序。
    /// </summary>
    public static partial class SpiceT3WorkspaceValidation
    {
        private static void ValidateAcC1AnalysisControls()
        {
            var canvasRoot = new GameObject("SpiceAcC1AnalysisControls", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var toggle = workspace.GetAnalysisModeToggleButtonForTesting();
                var toggleImage = toggle != null ? toggle.GetComponent<Image>() : null;
                if (toggle == null || !toggle.interactable || toggle.GetComponentInChildren<Text>().text != "分析：DC" ||
                    toggleImage == null || toggleImage.sprite == null || toggleImage.type != Image.Type.Sliced ||
                    toggleImage.color != MainUiTheme.PrimaryBlue)
                    throw new InvalidOperationException("AC-C1 default DC analysis mode toggle state is incorrect.");

                var beforeAc = workspace.ElectricalRevisionForTesting;
                toggle.onClick.Invoke();
                if (workspace.Model.AnalysisMode != SpiceAnalysisMode.AcSingleFrequency || workspace.ElectricalRevisionForTesting != beforeAc + 1 ||
                    !toggle.interactable || workspace.ResultState != SpiceWorkspaceResultState.NeverRun || toggle.GetComponentInChildren<Text>().text != "分析：单频 AC" ||
                    toggle.GetComponent<Image>().color != MainUiTheme.PrimaryBlue)
                    throw new InvalidOperationException("AC-C1 DC to AC mode control did not use the formal controller path.");

                var sameModeRevision = workspace.ElectricalRevisionForTesting;
                if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || workspace.ElectricalRevisionForTesting != sameModeRevision)
                    throw new InvalidOperationException("AC-C1 same analysis mode advanced the electrical revision.");

                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                toggle.onClick.Invoke();
                if (workspace.Model.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint || workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("AC-C1 AC to DC did not stale the previous outcome.");

                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);
                if (toggle.interactable || workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency))
                    throw new InvalidOperationException("AC-C1 running calculation allowed an analysis-mode change.");
                if (toggle.GetComponentInChildren<Text>().text != "分析：DC" || toggle.GetComponent<Image>().color != MainUiTheme.PrimaryBlue)
                    throw new InvalidOperationException("AC-C1 running state lost the current analysis mode label.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateAcC1FrequencyInput()
        {
            var canvasRoot = new GameObject("SpiceAcC1Frequency", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency))
                    throw new InvalidOperationException("AC-C1 frequency setup could not enter AC mode.");
                var input = workspace.GetAcFrequencyInputForTesting();
                var apply = workspace.GetApplyAcFrequencyButtonForTesting();
                if (workspace.GetAcAnalysisSettingsRootForTesting() == null || !workspace.GetAcAnalysisSettingsRootForTesting().gameObject.activeSelf ||
                    input == null || apply == null || !input.interactable || !apply.interactable)
                    throw new InvalidOperationException("AC-C1 frequency control was not enabled in AC mode.");

                foreach (var accepted in new[] { "1000", "1e3", SpiceAnalysisLimits.MinFrequencyHz.ToString("G17", System.Globalization.CultureInfo.InvariantCulture), SpiceAnalysisLimits.MaxFrequencyHz.ToString("G17", System.Globalization.CultureInfo.InvariantCulture) })
                {
                    input.text = accepted;
                    apply.onClick.Invoke();
                }
                if (Math.Abs(workspace.Model.AcFrequencyHz - SpiceAnalysisLimits.MaxFrequencyHz) > 1e-9d)
                    throw new InvalidOperationException("AC-C1 frequency input did not accept a valid boundary value.");

                var revision = workspace.ElectricalRevisionForTesting;
                var frequency = workspace.Model.AcFrequencyHz;
                foreach (var rejected in new[] { string.Empty, "not-a-number", "0", "-1", "NaN", "Infinity", "0.0001", "10000001" })
                {
                    input.text = rejected;
                    apply.onClick.Invoke();
                    if (workspace.Model.AcFrequencyHz != frequency || workspace.ElectricalRevisionForTesting != revision)
                        throw new InvalidOperationException("AC-C1 invalid frequency changed the formal model.");
                }

                input.text = frequency.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
                apply.onClick.Invoke();
                if (workspace.ElectricalRevisionForTesting != revision)
                    throw new InvalidOperationException("AC-C1 equivalent frequency advanced the electrical revision.");
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);
                if (input.interactable || apply.interactable || workspace.TrySetAcFrequency(500d))
                    throw new InvalidOperationException("AC-C1 running calculation allowed a frequency update.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateAcC1PaletteModeMatrix()
        {
            var canvasRoot = new GameObject("SpiceAcC1Palette", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var dcKinds = new[] { SpiceComponentKind.DcVoltageSource, SpiceComponentKind.DcCurrentSource, SpiceComponentKind.Resistor, SpiceComponentKind.Capacitor, SpiceComponentKind.Inductor, SpiceComponentKind.Ground, SpiceComponentKind.IdealSwitch, SpiceComponentKind.SiliconDiode, SpiceComponentKind.VoltageProbe, SpiceComponentKind.CurrentProbe };
                foreach (var kind in dcKinds)
                    if (workspace.GetPaletteCardForTesting(kind) == null || !workspace.GetPaletteCardForTesting(kind).interactable)
                        throw new InvalidOperationException("AC-C1 DC palette matrix disabled a supported card: " + kind + ".");
                if (workspace.GetPaletteCardForTesting(SpiceComponentKind.AcVoltageSource).interactable)
                    throw new InvalidOperationException("AC-C1 DC palette allowed a new AC source.");

                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || workspace.Model.FindComponent(resistor.InstanceId) == null)
                    throw new InvalidOperationException("AC-C1 mode switch removed an existing component.");
                var acKinds = new[] { SpiceComponentKind.AcVoltageSource, SpiceComponentKind.Resistor, SpiceComponentKind.Capacitor, SpiceComponentKind.Inductor, SpiceComponentKind.Ground, SpiceComponentKind.IdealSwitch, SpiceComponentKind.VoltageProbe, SpiceComponentKind.CurrentProbe };
                foreach (var kind in acKinds)
                    if (workspace.GetPaletteCardForTesting(kind) == null || !workspace.GetPaletteCardForTesting(kind).interactable)
                        throw new InvalidOperationException("AC-C1 AC palette matrix disabled a supported card: " + kind + ".");
                foreach (var kind in new[] { SpiceComponentKind.DcVoltageSource, SpiceComponentKind.DcCurrentSource, SpiceComponentKind.SiliconDiode })
                    if (workspace.GetPaletteCardForTesting(kind).interactable)
                        throw new InvalidOperationException("AC-C1 AC palette allowed an unsupported card: " + kind + ".");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateAcC1AcSourceParameterEditing()
        {
            var canvasRoot = new GameObject("SpiceAcC1Parameters", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency);
                var source = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
                var view = workspace.GetComponentViewForTesting(source.InstanceId);
                if (source == null || source.SiValue != 1d || source.AcPhaseDegrees != 0d || view == null ||
                    view.transform.Find("SymbolRoot/Symbol/AcMark") == null || !view.transform.Find("AnnotationRoot/Summary").GetComponent<Text>().text.Contains("∠ 0°"))
                    throw new InvalidOperationException("AC-C1 AC source default view or core-owned defaults are incorrect.");

                var changes = 0;
                workspace.Model.Changed += _ => changes++;
                var revision = workspace.ElectricalRevisionForTesting;
                workspace.GetAcPhaseInputForTesting().text = "30";
                var parameterInput = view.transform.parent.GetComponentInChildren<InputField>();
                var allInputs = workspace.GetAcPhaseInputForTesting().transform.parent.GetComponentsInChildren<InputField>(true);
                var magnitudeInput = allInputs.First(input => input.name == "ParameterInput");
                magnitudeInput.text = "2";
                workspace.GetParameterApplyButtonForTesting().onClick.Invoke();
                if (source.SiValue != 2d || source.AcPhaseDegrees != 30d || changes != 1 || workspace.ElectricalRevisionForTesting != revision + 1)
                    throw new InvalidOperationException("AC-C1 AC source parameters were not atomically applied once.");

                magnitudeInput.text = "0";
                workspace.GetAcPhaseInputForTesting().text = "45";
                workspace.GetParameterApplyButtonForTesting().onClick.Invoke();
                if (source.SiValue != 2d || source.AcPhaseDegrees != 30d)
                    throw new InvalidOperationException("AC-C1 invalid magnitude partially updated AC source parameters.");
                magnitudeInput.text = "3";
                workspace.GetAcPhaseInputForTesting().text = "bad";
                workspace.GetParameterApplyButtonForTesting().onClick.Invoke();
                if (source.SiValue != 2d || source.AcPhaseDegrees != 30d)
                    throw new InvalidOperationException("AC-C1 invalid phase partially updated AC source parameters.");

                if (!workspace.TrySetAcVoltageSourceParameters(source.InstanceId, 2d, -170d))
                    throw new InvalidOperationException("AC-C1 phase equivalence setup failed.");
                revision = workspace.ElectricalRevisionForTesting;
                if (!workspace.TrySetAcVoltageSourceParameters(source.InstanceId, 2d, 190d) || workspace.ElectricalRevisionForTesting != revision)
                    throw new InvalidOperationException("AC-C1 equivalent normalized phase advanced revision.");

                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 60f);
                workspace.SelectComponent(workspace.GetComponentViewForTesting(resistor.InstanceId));
                if (workspace.GetAcPhaseInputForTesting().gameObject.activeSelf)
                    throw new InvalidOperationException("AC-C1 phase field leaked into a normal parameter editor.");
                workspace.SelectComponent(view);
                if (!workspace.GetAcPhaseInputForTesting().gameObject.activeSelf)
                    throw new InvalidOperationException("AC-C1 phase field did not return for an AC source.");
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 60f);
                workspace.SelectComponent(workspace.GetComponentViewForTesting(ground.InstanceId));
                if (workspace.GetAcPhaseInputForTesting().gameObject.activeSelf)
                    throw new InvalidOperationException("AC-C1 phase field leaked into ground selection.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateAcC1ResultPresentation()
        {
            var result = new SpiceSimulationResult
            {
                Success = true,
                AnalysisSettings = new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d)
            };
            result.AcComponentResults["VP1"] = new SpiceAcComponentResult { ComponentId = "VP1", ComponentKind = "VoltageProbe", Voltage = new SpicePhasor(0.5d, -0.5d) };
            result.AcComponentResults["V1"] = new SpiceAcComponentResult { ComponentId = "V1", ComponentKind = "AcVoltageSource", Voltage = new SpicePhasor(1d, 0d), Current = SpicePhasor.Zero };
            result.AcComponentResults["R1"] = new SpiceAcComponentResult { ComponentId = "R1", ComponentKind = "Resistor", Voltage = new SpicePhasor(0d, 0d), Current = new SpicePhasor(707.107e-9d, 0d) };
            result.AcComponentResults["SW1"] = new SpiceAcComponentResult { ComponentId = "SW1", ComponentKind = "IdealSwitch", Voltage = new SpicePhasor(1d, 0d), Current = new SpicePhasor(1e-3d, 0d), CurrentDirection = "A-to-B" };
            var text = SpiceAcResultFormatter.Format(result);
            if (!text.StartsWith("分析：单频 AC\n频率：1 kHz", StringComparison.Ordinal) ||
                !text.Contains("707.107 mV ∠ -45.000°") || !text.Contains("707.107 nA ∠ 0.000°") ||
                !text.Contains("0 V ∠ --") || !text.Contains("0 A ∠ --") || !text.Contains("参考方向：A → B") || text.Contains("VP1  VoltageProbe\n差分电压  707.107 mV ∠ -45.000°\n电流"))
                throw new InvalidOperationException("AC-C1 formal phasor result formatting is incomplete.");
            if (text.IndexOf("R1  Resistor", StringComparison.Ordinal) > text.IndexOf("V1  AcVoltageSource", StringComparison.Ordinal) ||
                text.IndexOf("V1  AcVoltageSource", StringComparison.Ordinal) > text.IndexOf("VP1  VoltageProbe", StringComparison.Ordinal))
                throw new InvalidOperationException("AC-C1 result ordering is not ordinal by component id.");
        }

        private static void ValidateAcC1CopyResultConsistency()
        {
            var root = new GameObject("SpiceAcC1Copy", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(root.transform, out var bindings);
                workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency);
                var result = CreateControllerAcResult(workspace.Model.BuildCircuitModel());
                workspace.SetSimulationOverrideForTesting((_, __) => System.Threading.Tasks.Task.FromResult(result));
                workspace.RunCalculationAsync().GetAwaiter().GetResult();
                var expected = SpiceAcResultFormatter.Format(result);
                if (workspace.ResultState != SpiceWorkspaceResultState.Current || workspace.GetVisibleResultTextForTesting() != expected ||
                    !workspace.TryGetCopyableOutcomeText(out var copyText) || copyText != expected ||
                    !bindings.ResultRoot.Find("ResultHeader/CopyResult").GetComponent<Button>().interactable)
                    throw new InvalidOperationException("AC-C1 copyable AC outcome did not use the formal visible formatter text.");
                workspace.TrySetAcFrequency(2000d);
                if (workspace.ResultState != SpiceWorkspaceResultState.Stale || workspace.TryGetCopyableOutcomeText(out _))
                    throw new InvalidOperationException("AC-C1 stale outcome remained copyable.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ValidateAcC1DcPresentationRegression()
        {
            var root = new GameObject("SpiceAcC1DcText", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(root.transform, out _);
                var result = CreateControllerDcResult(workspace.Model.BuildCircuitModel());
                workspace.SetSimulationOverrideForTesting((_, __) => System.Threading.Tasks.Task.FromResult(result));
                workspace.RunCalculationAsync().GetAwaiter().GetResult();
                const string expected = "resistor-001  Resistor\n电压  1 V\n电流  0.001 A\n参考方向：正端 → 负端\nDC_CONTROLLER_MARKER";
                if (workspace.GetVisibleResultTextForTesting() != expected || !workspace.TryGetCopyableOutcomeText(out var copy) || copy != expected)
                    throw new InvalidOperationException("AC-C1 changed the exact DC presentation contract.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void ValidateAcC1LayoutStructureSmoke()
        {
            foreach (var size in new[] { new Vector2(3840f, 2160f), new Vector2(1920f, 1080f), new Vector2(1366f, 768f) })
            {
                var root = new GameObject("SpiceAcC1Layout", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                try
                {
                    var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = size;
                    var workspace = CreateInitializedWorkspaceForCopy(root.transform, out var bindings);
                    Canvas.ForceUpdateCanvases();
                    if (!workspace.ValidateWorkspaceGeometryForTesting(out var geometryError))
                        throw new InvalidOperationException("AC-C1 layout geometry contract failed at " + size + ": " + geometryError);
                    var toolbar = bindings.RunButton.transform.parent as RectTransform;
                    var modeToggle = workspace.GetAnalysisModeToggleButtonForTesting();
                    if (toolbar == null || toolbar.Find("AnalysisControls") != null || modeToggle == null || modeToggle.transform.parent != toolbar ||
                        bindings.PaletteRoot.Find("AcVoltageSourceCard") == null || bindings.ResultRoot.Find("ResultScrollView/Viewport/Content/ResultText") == null ||
                        workspace.GetAcFrequencyInputForTesting() == null || workspace.GetApplyAcFrequencyButtonForTesting() == null)
                        throw new InvalidOperationException("AC-C1 layout structure smoke did not create required controls at " + size + ".");
                    var buttons = toolbar.GetComponentsInChildren<Button>(true).Where(button => button.gameObject.activeInHierarchy).ToArray();
                    if (buttons.Any(button => !ContainsRect(toolbar, button.GetComponent<RectTransform>())) ||
                        buttons.SelectMany((left, index) => buttons.Skip(index + 1).Select(right => new { left, right }))
                            .Any(pair => Overlaps(pair.left.GetComponent<RectTransform>(), pair.right.GetComponent<RectTransform>())))
                        throw new InvalidOperationException("AC-C1 toolbar buttons overlap or extend outside the toolbar at " + size + ".");
                    if (Overlaps(toolbar, bindings.WorkspaceViewport) || Overlaps(toolbar, bindings.PaletteRoot) || Overlaps(toolbar, bindings.AssistantRoot))
                        throw new InvalidOperationException("AC-C1 toolbar overlaps a workspace region at " + size + ".");
                    if (!ContainsRect(workspace.GetGridLayerForTesting(), bindings.WorkspaceViewport))
                        throw new InvalidOperationException("AC-C1 grid no longer covers the viewport's visible top edge at " + size + ".");
                    if (workspace.GetAcAnalysisSettingsRootForTesting().gameObject.activeSelf)
                        throw new InvalidOperationException("AC-C1 DC mode must hide the right-side frequency settings.");
                    workspace.GetAnalysisModeToggleButtonForTesting().onClick.Invoke();
                    if (!workspace.GetAcAnalysisSettingsRootForTesting().gameObject.activeSelf)
                        throw new InvalidOperationException("AC-C1 AC mode must show the right-side frequency settings.");
                    var source = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
                    var parameterInput = workspace.GetParameterInputForTesting();
                    var phaseInput = workspace.GetAcPhaseInputForTesting();
                    var unit = workspace.GetUnitButtonForTesting();
                    var apply = workspace.GetParameterApplyButtonForTesting();
                    var frequencyInput = workspace.GetAcFrequencyInputForTesting();
                    var frequencyApply = workspace.GetApplyAcFrequencyButtonForTesting();
                    var parameterElements = new[]
                    {
                        parameterInput.GetComponent<RectTransform>(), phaseInput.GetComponent<RectTransform>(),
                        unit.GetComponent<RectTransform>(), apply.GetComponent<RectTransform>(),
                        frequencyInput.GetComponent<RectTransform>(), frequencyApply.GetComponent<RectTransform>()
                    };
                    if (source == null || parameterElements.Any(element => !ContainsRect(bindings.ParameterRoot, element)) ||
                        parameterElements.SelectMany((left, index) => parameterElements.Skip(index + 1)
                            .Select(right => new { left, right })).Any(pair => Overlaps(pair.left, pair.right)))
                        throw new InvalidOperationException("AC-C1 AC frequency and component parameter controls overlap at " + size + ".");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        private static bool ContainsRect(RectTransform outer, RectTransform inner)
        {
            GetWorldBounds(outer, out var outerMin, out var outerMax);
            GetWorldBounds(inner, out var innerMin, out var innerMax);
            return innerMin.x >= outerMin.x - 0.1f && innerMax.x <= outerMax.x + 0.1f &&
                innerMin.y >= outerMin.y - 0.1f && innerMax.y <= outerMax.y + 0.1f;
        }

        private static bool Overlaps(RectTransform left, RectTransform right)
        {
            GetWorldBounds(left, out var leftMin, out var leftMax);
            GetWorldBounds(right, out var rightMin, out var rightMax);
            return leftMin.x < rightMax.x && leftMax.x > rightMin.x && leftMin.y < rightMax.y && leftMax.y > rightMin.y;
        }

        private static void GetWorldBounds(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            min = corners[0];
            max = corners[2];
        }

        private static void ValidateAcAnalysisSettingsAndSnapshot()
        {
            var model = new SpiceWorkspaceModel();
            if (model.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint || Math.Abs(model.AcFrequencyHz - SpiceAnalysisLimits.DefaultFrequencyHz) > 1e-12d)
                throw new InvalidOperationException("AC analysis defaults are incorrect.");

            var changes = 0;
            model.Changed += _ => changes++;
            if (!model.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || changes != 1)
                throw new InvalidOperationException("Changing to AC analysis should raise exactly one model change.");
            if (!model.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || changes != 1)
                throw new InvalidOperationException("Writing the same analysis mode should not raise a model change.");

            foreach (var invalidFrequency in new[] { 0d, -1d, double.NaN, double.PositiveInfinity, SpiceAnalysisLimits.MinFrequencyHz * 0.5d, SpiceAnalysisLimits.MaxFrequencyHz * 2d })
            {
                if (model.TrySetAcFrequency(invalidFrequency))
                    throw new InvalidOperationException("Invalid AC frequency was accepted.");
            }
            if (!model.TrySetAcFrequency(SpiceAnalysisLimits.MinFrequencyHz) || !model.TrySetAcFrequency(SpiceAnalysisLimits.MaxFrequencyHz))
                throw new InvalidOperationException("AC frequency boundary was rejected.");
            if (!model.TrySetAcFrequency(1000d)) throw new InvalidOperationException("Valid AC frequency was rejected.");
            var changesAfterFrequency = changes;
            if (!model.TrySetAcFrequency(1000d) || changes != changesAfterFrequency)
                throw new InvalidOperationException("Writing the same AC frequency should not raise a model change.");

            if (SpiceAnalysisLimits.NormalizePhaseDegrees(180d) != -180d ||
                SpiceAnalysisLimits.NormalizePhaseDegrees(190d) != -170d ||
                SpiceAnalysisLimits.NormalizePhaseDegrees(-190d) != 170d ||
                SpiceAnalysisLimits.NormalizePhaseDegrees(540d) != -180d ||
                SpiceAnalysisLimits.NormalizePhaseDegrees(-540d) != -180d ||
                BitConverter.DoubleToInt64Bits(SpiceAnalysisLimits.NormalizePhaseDegrees(-0d)) < 0)
                throw new InvalidOperationException("AC phase normalization is incorrect.");

            var acSource = model.AddComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
            if (acSource.InstanceId != "ac-source-001" || Math.Abs(acSource.SiValue - 1d) > 1e-12d || acSource.AcPhaseDegrees != 0d)
                throw new InvalidOperationException("AC voltage source defaults are incorrect.");
            if (!SpiceParameterUnits.TryToSi(SpiceComponentKind.AcVoltageSource, 2d, "V", out var magnitude) || magnitude != 2d)
                throw new InvalidOperationException("AC voltage magnitude unit conversion failed.");

            changes = 0;
            if (!model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2d, 30d) || changes != 1 ||
                acSource.SiValue != 2d || acSource.AcPhaseDegrees != 30d)
                throw new InvalidOperationException("AC source atomic parameter update failed.");
            if (model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 0d, 45d) ||
                acSource.SiValue != 2d || acSource.AcPhaseDegrees != 30d || changes != 1)
                throw new InvalidOperationException("Invalid AC magnitude should reject the entire atomic update.");
            if (model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 3d, double.NaN) ||
                acSource.SiValue != 2d || acSource.AcPhaseDegrees != 30d || changes != 1)
                throw new InvalidOperationException("Invalid AC phase should reject the entire atomic update.");
            if (!model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2d, 390d) || acSource.AcPhaseDegrees != 30d || changes != 1)
                throw new InvalidOperationException("Equivalent normalized AC phase should not raise a model change.");

            var snapshot = model.BuildCircuitModel();
            if (ReferenceEquals(snapshot.AnalysisSettings, model.AnalysisSettingsSnapshot) ||
                snapshot.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency || snapshot.AnalysisSettings.FrequencyHz != 1000d)
                throw new InvalidOperationException("Circuit snapshot did not copy immutable AC analysis settings.");
            var snapshottedSource = snapshot.Components.Single(component => component.InstanceId == acSource.InstanceId);
            if (snapshottedSource.GetRequiredParameter(SpiceParameterKey.AcMagnitude) != 2d || snapshottedSource.AcPhaseDegrees != 30d)
                throw new InvalidOperationException("Circuit snapshot did not preserve AC source parameters.");

            if (!model.TrySetAcFrequency(10000d) || !model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 3d, -190d))
                throw new InvalidOperationException("Unable to mutate workspace after AC snapshot.");
            if (snapshot.AnalysisSettings.FrequencyHz != 1000d || snapshottedSource.GetRequiredParameter(SpiceParameterKey.AcMagnitude) != 2d || snapshottedSource.AcPhaseDegrees != 30d)
                throw new InvalidOperationException("AC circuit snapshot retained mutable workspace data.");
        }

        private static void ValidateAcAnalysisControllerRevisionAndRunningGuard()
        {
            var canvasRoot = new GameObject("SpiceAcAnalysisControllerValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var initialRevision = workspace.ElectricalRevisionForTesting;
                if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || workspace.ElectricalRevisionForTesting != initialRevision + 1)
                    throw new InvalidOperationException("Controller analysis mode update did not advance D1 revision exactly once.");
                if (!workspace.TrySetAcFrequency(2000d) || workspace.ElectricalRevisionForTesting != initialRevision + 2)
                    throw new InvalidOperationException("Controller AC frequency update did not advance D1 revision exactly once.");

                var acSource = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
                var revisionAfterCreate = workspace.ElectricalRevisionForTesting;
                if (acSource == null || !workspace.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2d, 30d) ||
                    workspace.ElectricalRevisionForTesting != revisionAfterCreate + 1)
                    throw new InvalidOperationException("Controller AC source update did not advance D1 revision exactly once.");
                if (!workspace.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2d, 390d) ||
                    workspace.ElectricalRevisionForTesting != revisionAfterCreate + 1)
                    throw new InvalidOperationException("Equivalent AC source update changed D1 revision.");

                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);
                var guardedRevision = workspace.ElectricalRevisionForTesting;
                if (workspace.TrySetAnalysisMode(SpiceAnalysisMode.DcOperatingPoint) || workspace.TrySetAcFrequency(500d) ||
                    workspace.TrySetAcVoltageSourceParameters(acSource.InstanceId, 3d, 45d))
                    throw new InvalidOperationException("Running calculation accepted an AC analysis mutation.");
                if (workspace.Model.AnalysisMode != SpiceAnalysisMode.AcSingleFrequency || workspace.Model.AcFrequencyHz != 2000d ||
                    acSource.SiValue != 2d || acSource.AcPhaseDegrees != 30d || workspace.ElectricalRevisionForTesting != guardedRevision)
                    throw new InvalidOperationException("Rejected AC mutation changed model state or D1 revision.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateControllerDcAndAcSimulationPaths()
        {
            var canvasRoot = new GameObject("SpiceControllerAnalysisDispatchValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var dcCalls = 0;
                var acCalls = 0;
                workspace.SetSimulationServiceForTesting(new SpiceSimulationService(
                    (circuit, _) =>
                    {
                        dcCalls++;
                        return System.Threading.Tasks.Task.FromResult(CreateControllerDcResult(circuit));
                    },
                    (circuit, _) =>
                    {
                        acCalls++;
                        return System.Threading.Tasks.Task.FromResult(CreateControllerAcResult(circuit));
                    }));

                var dcSource = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                var dcResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var dcGround = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 80f);
                ConnectSingleResistor(workspace, dcSource.InstanceId, dcResistor.InstanceId, dcGround.InstanceId);
                var dcResult = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (dcResult == null || !dcResult.Success || workspace.ResultState != SpiceWorkspaceResultState.Current || dcCalls != 1 || acCalls != 0 ||
                    !workspace.TryGetCopyableOutcomeText(out var dcText) || !dcText.Contains("DC_CONTROLLER_MARKER"))
                    throw new InvalidOperationException("Controller DC calculation did not use the unified simulation-service path.");

                workspace.ClearWorkspace();
                if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || !workspace.TrySetAcFrequency(1000d))
                    throw new InvalidOperationException("Unable to configure the controller AC calculation path.");
                var acSource = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.left * 80f);
                var acResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var acGround = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 80f);
                ConnectSingleResistor(workspace, acSource.InstanceId, acResistor.InstanceId, acGround.InstanceId);
                var acResult = workspace.RunCalculationAsync().GetAwaiter().GetResult();
                if (acResult == null || !acResult.Success || workspace.ResultState != SpiceWorkspaceResultState.Current || dcCalls != 1 || acCalls != 1 ||
                    !workspace.TryGetCopyableOutcomeText(out var acText) || !acText.Contains("AC_CONTROLLER_MARKER") || !acText.Contains("∠"))
                    throw new InvalidOperationException("Controller AC calculation did not use the unified simulation-service path or present phasor results.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static SpiceSimulationResult CreateControllerDcResult(SpiceCircuitModel circuit)
        {
            if (circuit.AnalysisSettings.Mode != SpiceAnalysisMode.DcOperatingPoint)
                throw new InvalidOperationException("Controller dispatched a non-DC circuit to the DC simulation service.");
            var result = new SpiceSimulationResult { Success = true, AnalysisSettings = circuit.AnalysisSettings.Copy(), GeneratedNetlistContent = "* controller DC" };
            result.ComponentResults["resistor-001"] = new SpiceComponentResult
            {
                ComponentId = "resistor-001", ComponentKind = "Resistor", Voltage = 1d, Current = .001d,
                VoltageDirection = "positive-to-negative", CurrentDirection = "positive-to-negative", Notes = "DC_CONTROLLER_MARKER"
            };
            return result;
        }

        private static SpiceSimulationResult CreateControllerAcResult(SpiceCircuitModel circuit)
        {
            if (circuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency)
                throw new InvalidOperationException("Controller dispatched a non-AC circuit to the AC simulation service.");
            var result = new SpiceSimulationResult { Success = true, AnalysisSettings = circuit.AnalysisSettings.Copy(), GeneratedNetlistContent = "* controller AC" };
            result.AcComponentResults["resistor-001"] = new SpiceAcComponentResult
            {
                ComponentId = "resistor-001", ComponentKind = "Resistor", Voltage = new SpicePhasor(1d, 0d), Current = new SpicePhasor(.001d, 0d),
                VoltageDirection = "positive-to-negative", CurrentDirection = "positive-to-negative", Notes = "AC_CONTROLLER_MARKER"
            };
            return result;
        }

        private static void ValidateAcAnalysisRevisionDiscardsDelayedResult()
        {
            var canvasRoot = new GameObject("SpiceAcAnalysisDelayedRevisionValidation", typeof(RectTransform), typeof(Canvas));
            var originalContext = System.Threading.SynchronizationContext.Current;
            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) || !workspace.TrySetAcFrequency(1000d))
                    throw new InvalidOperationException("Unable to configure delayed AC analysis validation.");
                var source = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
                var completion = new System.Threading.Tasks.TaskCompletionSource<SpiceSimulationResult>();
                SpiceCircuitModel capturedCircuit = null;
                workspace.SetSimulationOverrideForTesting((circuit, _) =>
                {
                    capturedCircuit = circuit;
                    return completion.Task;
                });

                var calculation = workspace.RunCalculationAsync();
                if (workspace.ResultState != SpiceWorkspaceResultState.Running || capturedCircuit == null ||
                    capturedCircuit.AnalysisSettings.Mode != SpiceAnalysisMode.AcSingleFrequency ||
                    capturedCircuit.AnalysisSettings.FrequencyHz != 1000d ||
                    capturedCircuit.Components.Single(component => component.InstanceId == source.InstanceId).AcPhaseDegrees != 0d)
                    throw new InvalidOperationException("Delayed calculation did not capture an immutable AC analysis snapshot.");

                if (!workspace.Model.TrySetAcVoltageSourceParameters(source.InstanceId, 2d, 30d))
                    throw new InvalidOperationException("Controlled AC source parameter mutation was rejected before D1 stale-result verification.");
                completion.SetResult(CreateD3SuccessfulResult(source.InstanceId, "* delayed AC result"));
                if (calculation.GetAwaiter().GetResult() != null || workspace.ResultState != SpiceWorkspaceResultState.Stale)
                    throw new InvalidOperationException("Delayed result was not discarded after AC source parameters changed.");
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(originalContext);
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateAcParameterWriteEncapsulation()
        {
            var siValueSetter = typeof(SpiceWorkspaceComponentData).GetProperty(nameof(SpiceWorkspaceComponentData.SiValue))?.GetSetMethod(true);
            var phaseSetter = typeof(SpiceWorkspaceComponentData).GetProperty(nameof(SpiceWorkspaceComponentData.AcPhaseDegrees))?.GetSetMethod(true);
            if (siValueSetter == null || siValueSetter.IsPublic || phaseSetter == null || phaseSetter.IsPublic)
                throw new InvalidOperationException("Workspace electrical parameter setters must not be public.");

            var model = new SpiceWorkspaceModel();
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var changes = 0;
            model.Changed += _ => changes++;
            if (!model.TrySetParameter(resistor.InstanceId, 2000d) || resistor.SiValue != 2000d || changes != 1)
                throw new InvalidOperationException("Model parameter API did not update a resistor through one change notification.");
            if (!model.TrySetParameter(resistor.InstanceId, 2000d) || changes != 1)
                throw new InvalidOperationException("Equivalent generic parameter update should not raise Changed.");

            if (!model.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) ||
                !model.TrySetAnalysisMode(SpiceAnalysisMode.DcOperatingPoint) || changes != 3)
                throw new InvalidOperationException("AC-to-DC mode transition did not use the model change path exactly once per change.");
            if (model.TrySetAnalysisMode((SpiceAnalysisMode)999))
                throw new InvalidOperationException("Undefined analysis mode was accepted.");

            var acSource = model.AddComponent(SpiceComponentKind.AcVoltageSource, Vector2.right);
            changes = 0;
            foreach (var invalidMagnitude in new[] { -1d, 0d, SpiceAnalysisLimits.MaxAcMagnitudeVolts * 2d })
            {
                if (model.TrySetAcVoltageSourceParameters(acSource.InstanceId, invalidMagnitude, 45d))
                    throw new InvalidOperationException("Invalid AC magnitude was accepted by the atomic model API.");
            }
            if (model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2d, double.PositiveInfinity) ||
                model.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2d, double.NegativeInfinity) ||
                changes != 0 || acSource.SiValue != 1d || acSource.AcPhaseDegrees != 0d)
                throw new InvalidOperationException("Invalid AC phase partially changed atomic source parameters.");

            var canvasRoot = new GameObject("SpiceGenericParameterRevisionValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var workspaceResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                var revisionBeforeParameter = workspace.ElectricalRevisionForTesting;
                if (!workspace.TrySetParameter(workspaceResistor.InstanceId, 2d, "kOhm") ||
                    workspace.ElectricalRevisionForTesting != revisionBeforeParameter + 1 ||
                    Math.Abs(workspaceResistor.SiValue - 2000d) > 1e-12d)
                    throw new InvalidOperationException("Controller generic parameter update did not advance D1 revision exactly once.");
                if (!workspace.TrySetParameter(workspaceResistor.InstanceId, 2d, "kOhm") ||
                    workspace.ElectricalRevisionForTesting != revisionBeforeParameter + 1)
                    throw new InvalidOperationException("Equivalent Controller parameter update changed D1 revision.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        private static void ValidateDrawingV1AcCompatibilityBoundaries()
        {
            const string acSourceJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"ac-source-001\",\"componentType\":\"AcVoltageSource\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(acSourceJson, out _, out var dtoError) || dtoError.IndexOf("V1", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Schema V1 must reject AcVoltageSource before constructing a temporary model.");
            if (SpiceDrawingSerializer.TryFromJson(acSourceJson.Replace("AcVoltageSource", "acvoltagesource"), out _, out _))
                throw new InvalidOperationException("Component type case changes must not bypass schema V1 validation.");

            var dcModel = new SpiceWorkspaceModel();
            dcModel.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var dcJson = SpiceDrawingSerializer.ToJson(dcModel);
            string dcError = null;
            if (dcJson.IndexOf("\"schemaVersion\": 2", StringComparison.Ordinal) < 0 || dcJson.IndexOf("analysis", StringComparison.OrdinalIgnoreCase) < 0 ||
                !SpiceDrawingSerializer.TryFromJson(dcJson, out var restoredDcModel, out dcError) ||
                restoredDcModel.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint || restoredDcModel.AcFrequencyHz != SpiceAnalysisLimits.DefaultFrequencyHz)
                throw new InvalidOperationException("V2 DC drawing contract changed: " + dcError);

            var acModel = new SpiceWorkspaceModel();
            var acSource = acModel.AddComponent(SpiceComponentKind.AcVoltageSource, Vector2.zero);
            acModel.TrySetAcVoltageSourceParameters(acSource.InstanceId, 1d, 60d);
            if (SpiceDrawingSerializer.TryValidateSchemaV1SaveCompatibility(acModel, out var compatibilityError) ||
                compatibilityError.IndexOf("V1", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Schema V1 save compatibility preflight accepted an AC source.");
            var acJson = SpiceDrawingSerializer.ToJson(acModel);
            SpiceWorkspaceModel restoredAcModel = null;
            string restoredAcError = null;
            if (!acJson.Contains("\"schemaVersion\": 2") || !SpiceDrawingSerializer.TryFromJson(acJson, out restoredAcModel, out restoredAcError) ||
                restoredAcModel.FindComponent(acSource.InstanceId).AcPhaseDegrees != 60d)
                throw new InvalidOperationException("V2 writer did not preserve AC source phase: " + restoredAcError);

            var tempDir = CreateUniqueTempDir("AcV1Compatibility");
            try
            {
                var canvasRoot = new GameObject("SpiceAcV1CompatibilityValidation", typeof(RectTransform), typeof(Canvas));
                try
                {
                    var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                    workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                    var originalPath = Path.Combine(tempDir, "original.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(originalPath, out var originalSaveError))
                        throw new InvalidOperationException("V1 compatibility test setup save failed: " + originalSaveError);

                    var originalModel = workspace.Model;
                    var originalRevision = workspace.ElectricalRevisionForTesting;
                    var originalPathState = workspace.CurrentSpiceFilePath;
                    var rejectedImportPath = Path.Combine(tempDir, "ac-source.spicejson");
                    File.WriteAllText(rejectedImportPath, acSourceJson, System.Text.Encoding.UTF8);
                    if (workspace.TryImportWorkspaceFromPath(rejectedImportPath, out var importError) ||
                        importError.IndexOf("V1", StringComparison.Ordinal) < 0 ||
                        !ReferenceEquals(workspace.Model, originalModel) ||
                        workspace.ElectricalRevisionForTesting != originalRevision ||
                        workspace.CurrentSpiceFilePath != originalPathState ||
                        workspace.Model.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint ||
                        workspace.Model.AcFrequencyHz != SpiceAnalysisLimits.DefaultFrequencyHz)
                        throw new InvalidOperationException("Rejected V1 AC import changed workspace, revision, analysis settings, or current path.");

                    var source = workspace.CreateComponent(SpiceComponentKind.AcVoltageSource, Vector2.right);
                    if (!workspace.TrySetAcVoltageSourceParameters(source.InstanceId, 1d, 60d))
                        throw new InvalidOperationException("V1 save compatibility test could not configure AC source phase.");
                    var savedAcPath = Path.Combine(tempDir, "ac-source.spicejson");
                    if (!workspace.TrySaveWorkspaceToPath(savedAcPath, out var sourceSaveError) || !File.Exists(savedAcPath) ||
                        workspace.CurrentSpiceFilePath != savedAcPath || source.AcPhaseDegrees != 60d || sourceSaveError != null)
                        throw new InvalidOperationException("V2 AC source save did not preserve phase or current path.");

                    var sentinelPath = Path.Combine(tempDir, "sentinel.spicejson");
                    const string sentinel = "DO_NOT_OVERWRITE";
                    File.WriteAllText(sentinelPath, sentinel, System.Text.Encoding.UTF8);
                    if (!workspace.TrySaveWorkspaceToPath(sentinelPath, out _) || File.ReadAllText(sentinelPath, System.Text.Encoding.UTF8) == sentinel ||
                        Directory.GetFiles(tempDir, "*.tmp*", SearchOption.TopDirectoryOnly).Length != 0 ||
                        Directory.GetFiles(tempDir, "*.backup*", SearchOption.TopDirectoryOnly).Length != 0)
                        throw new InvalidOperationException("V2 atomic save did not replace the target safely.");

                    workspace.ClearWorkspace();
                    if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency) ||
                        !workspace.TrySaveWorkspaceToPath(Path.Combine(tempDir, "ac-mode.spicejson"), out var acModeSaveError) || acModeSaveError != null)
                        throw new InvalidOperationException("V2 writer must preserve AC analysis mode.");
                    if (!workspace.TrySetAnalysisMode(SpiceAnalysisMode.DcOperatingPoint) || !workspace.TrySetAcFrequency(2000d) ||
                        !workspace.TrySaveWorkspaceToPath(Path.Combine(tempDir, "nondefault-frequency.spicejson"), out var frequencySaveError) || frequencySaveError != null)
                        throw new InvalidOperationException("V2 writer must preserve a non-default stored frequency.");
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

        private static void ValidateAcDV2Serialization()
        {
            var dc = new SpiceWorkspaceModel();
            var source = dc.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-120f, 0f));
            var resistor = dc.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var ground = dc.AddComponent(SpiceComponentKind.Ground, new Vector2(120f, -60f));
            if (!dc.AddWire(source.InstanceId, SpiceComponentModel.PositiveTerminalId, resistor.InstanceId, SpiceComponentModel.PositiveTerminalId) ||
                !dc.AddWire(resistor.InstanceId, SpiceComponentModel.NegativeTerminalId, ground.InstanceId, SpiceComponentModel.GroundTerminalId) ||
                !dc.AddWire(source.InstanceId, SpiceComponentModel.NegativeTerminalId, ground.InstanceId, SpiceComponentModel.GroundTerminalId))
                throw new InvalidOperationException("AC-D DC fixture construction failed.");
            var dcJsonA = SpiceDrawingSerializer.ToJson(dc);
            var dcJsonB = SpiceDrawingSerializer.ToJson(dc);
            SpiceWorkspaceModel restoredDc = null;
            string dcError = null;
            if (dcJsonA != dcJsonB || !dcJsonA.Contains("\"schemaVersion\": 2") ||
                Convert.ToBase64String(SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(dcJsonA))) != Convert.ToBase64String(SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(dcJsonB))) ||
                !SpiceDrawingSerializer.TryFromJson(dcJsonA, out restoredDc, out dcError) || restoredDc.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint ||
                Math.Abs(restoredDc.AcFrequencyHz - SpiceAnalysisLimits.DefaultFrequencyHz) > 1e-12d)
                throw new InvalidOperationException("AC-D V2 DC deterministic round-trip failed: " + dcError);
        }

        private static void ValidateAcDAcDrawingRoundTrip()
        {

            var ac = new SpiceWorkspaceModel();
            ac.TrySetAcFrequency(1234.5d);
            ac.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency);
            var acSource = ac.AddComponent(SpiceComponentKind.AcVoltageSource, new Vector2(-180f, 0f));
            ac.TrySetAcVoltageSourceParameters(acSource.InstanceId, 2.5d, -170d);
            var currentProbe = ac.AddComponent(SpiceComponentKind.CurrentProbe, new Vector2(-100f, 0f));
            var acResistor = ac.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            var capacitor = ac.AddComponent(SpiceComponentKind.Capacitor, new Vector2(100f, 0f));
            var acGround = ac.AddComponent(SpiceComponentKind.Ground, new Vector2(100f, -100f));
            var voltageProbe = ac.AddComponent(SpiceComponentKind.VoltageProbe, new Vector2(160f, 50f));
            AddWorkspaceWire(ac, acSource, SpiceComponentModel.PositiveTerminalId, currentProbe, SpiceComponentModel.PositiveTerminalId);
            AddWorkspaceWire(ac, currentProbe, SpiceComponentModel.NegativeTerminalId, acResistor, SpiceComponentModel.PositiveTerminalId);
            AddWorkspaceWire(ac, acResistor, SpiceComponentModel.NegativeTerminalId, capacitor, SpiceComponentModel.PositiveTerminalId);
            AddWorkspaceWire(ac, capacitor, SpiceComponentModel.NegativeTerminalId, acGround, SpiceComponentModel.GroundTerminalId);
            AddWorkspaceWire(ac, acSource, SpiceComponentModel.NegativeTerminalId, acGround, SpiceComponentModel.GroundTerminalId);
            AddWorkspaceWire(ac, voltageProbe, SpiceComponentModel.PositiveTerminalId, capacitor, SpiceComponentModel.PositiveTerminalId);
            AddWorkspaceWire(ac, voltageProbe, SpiceComponentModel.NegativeTerminalId, acGround, SpiceComponentModel.GroundTerminalId);
            var acBefore = new SpiceSimulationService().SimulateAsync(ac.BuildCircuitModel()).GetAwaiter().GetResult();
            var acJson = SpiceDrawingSerializer.ToJson(ac);
            SpiceWorkspaceModel restoredAc = null;
            string acError = null;
            if (!SpiceDrawingSerializer.TryFromJson(acJson, out restoredAc, out acError) || restoredAc.AnalysisMode != SpiceAnalysisMode.AcSingleFrequency ||
                Math.Abs(restoredAc.AcFrequencyHz - 1234.5d) > 1e-12d || restoredAc.FindComponent(acSource.InstanceId).SiValue != 2.5d ||
                restoredAc.FindComponent(acSource.InstanceId).AcPhaseDegrees != -170d)
                throw new InvalidOperationException("AC-D AC settings/source round-trip failed: " + acError);
            var acAfter = new SpiceSimulationService().SimulateAsync(restoredAc.BuildCircuitModel()).GetAwaiter().GetResult();
            if (!acBefore.Success || !acAfter.Success || Math.Abs(acBefore.AcComponentResults[voltageProbe.InstanceId].Voltage.Magnitude - acAfter.AcComponentResults[voltageProbe.InstanceId].Voltage.Magnitude) > 1e-9d)
                throw new InvalidOperationException("AC-D RC fixture did not retain its real AC result.");
        }

        private static void ValidateAcDOpAmpFeedbackRoundTrip()
        {

            var follower = new SpiceWorkspaceModel();
            var followerSource = follower.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-120f, 0f));
            var opAmp = follower.AddComponent(SpiceComponentKind.IdealOperationalAmplifier, Vector2.zero);
            var followerGround = follower.AddComponent(SpiceComponentKind.Ground, new Vector2(0f, -100f));
            AddWorkspaceWire(follower, followerSource, SpiceComponentModel.PositiveTerminalId, opAmp, SpiceComponentModel.NonInvertingTerminalId);
            AddWorkspaceWire(follower, followerSource, SpiceComponentModel.NegativeTerminalId, followerGround, SpiceComponentModel.GroundTerminalId);
            AddWorkspaceWire(follower, opAmp, SpiceComponentModel.InvertingTerminalId, opAmp, SpiceComponentModel.OutputTerminalId);
            var followerJson = SpiceDrawingSerializer.ToJson(follower);
            if (!SpiceDrawingSerializer.TryFromJson(followerJson, out var restoredFollower, out var followerError) || restoredFollower.Wires.Count != 3 ||
                !new SpiceSimulationService().SimulateAsync(restoredFollower.BuildCircuitModel()).GetAwaiter().GetResult().Success)
                throw new InvalidOperationException("AC-D op-amp feedback round-trip failed: " + followerError);

            var inverter = new SpiceWorkspaceModel();
            inverter.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency);
            var inverterSource = inverter.AddComponent(SpiceComponentKind.AcVoltageSource, new Vector2(-200f, 0f));
            inverter.TrySetAcVoltageSourceParameters(inverterSource.InstanceId, 1d, 30d);
            var rin = inverter.AddComponent(SpiceComponentKind.Resistor, new Vector2(-80f, 0f));
            inverter.TrySetParameter(rin.InstanceId, 1000d);
            var rf = inverter.AddComponent(SpiceComponentKind.Resistor, new Vector2(50f, 80f));
            inverter.TrySetParameter(rf.InstanceId, 10000d);
            var inverterOpAmp = inverter.AddComponent(SpiceComponentKind.IdealOperationalAmplifier, Vector2.zero);
            var inverterGround = inverter.AddComponent(SpiceComponentKind.Ground, new Vector2(0f, -100f));
            AddWorkspaceWire(inverter, inverterSource, SpiceComponentModel.PositiveTerminalId, rin, SpiceComponentModel.PositiveTerminalId);
            AddWorkspaceWire(inverter, rin, SpiceComponentModel.NegativeTerminalId, inverterOpAmp, SpiceComponentModel.InvertingTerminalId);
            AddWorkspaceWire(inverter, rf, SpiceComponentModel.NegativeTerminalId, inverterOpAmp, SpiceComponentModel.InvertingTerminalId);
            AddWorkspaceWire(inverter, rf, SpiceComponentModel.PositiveTerminalId, inverterOpAmp, SpiceComponentModel.OutputTerminalId);
            AddWorkspaceWire(inverter, inverterSource, SpiceComponentModel.NegativeTerminalId, inverterGround, SpiceComponentModel.GroundTerminalId);
            AddWorkspaceWire(inverter, inverterOpAmp, SpiceComponentModel.NonInvertingTerminalId, inverterGround, SpiceComponentModel.GroundTerminalId);
            var inverterJson = SpiceDrawingSerializer.ToJson(inverter);
            if (!SpiceDrawingSerializer.TryFromJson(inverterJson, out var restoredInverter, out var inverterError))
                throw new InvalidOperationException("AC-D AC op-amp import failed: " + inverterError);
            var inverterResult = new SpiceSimulationService().SimulateAsync(restoredInverter.BuildCircuitModel()).GetAwaiter().GetResult();
            var inverterVoltage = inverterResult.Success ? inverterResult.AcComponentResults[inverterOpAmp.InstanceId].Voltage : default;
            if (!inverterResult.Success || Math.Abs(inverterVoltage.Magnitude - 9.99989000121d) > 2.1e-4d || Math.Abs(SpiceAnalysisLimits.NormalizePhaseDegrees(inverterVoltage.PhaseDegrees + 150d)) > 0.02d)
                throw new InvalidOperationException("AC-D AC op-amp result did not survive round-trip.");
        }

        private static void ValidateAcDD2LimitRegression()
        {
            // 所有 D2 边界均从 V2 DTO 进入反序列化器，防止版本分发绕过旧有资源限制。
            ValidateDrawingImportCountLimits();
            ValidateDrawingImportWaypointLimits();
            ValidateDrawingImportCoordinateLimits();
            ValidateDrawingImportStringLimits();

            var overLimit = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.CurrentSchemaVersion,
                analysis = new SpiceAnalysisDto { mode = SpiceAnalysisMode.DcOperatingPoint.ToString(), frequencyHz = "1000" }
            };
            for (var index = 0; index <= SpiceDrawingLimits.MaxComponents; index++) overLimit.components.Add(new SpiceComponentDto());
            if (SpiceDrawingSerializer.TryFromDto(overLimit, out _, out _))
                throw new InvalidOperationException("AC-D V2 import bypassed the existing component-count D2 limit.");
            var invalidFrequency = CreateDrawingLimitDto();
            invalidFrequency.analysis.frequencyHz = "0";
            if (SpiceDrawingSerializer.TryFromDto(invalidFrequency, out _, out _))
                throw new InvalidOperationException("AC-D V2 import accepted an invalid analysis frequency.");
            var invalidPhase = CreateDrawingLimitDto();
            var acSource = CreateLimitComponent("ac-source-001");
            acSource.componentType = SpiceComponentKind.AcVoltageSource.ToString();
            acSource.siValueText = "1";
            acSource.phaseDegrees = "NaN";
            invalidPhase.components.Add(acSource);
            if (SpiceDrawingSerializer.TryFromDto(invalidPhase, out _, out _))
                throw new InvalidOperationException("AC-D V2 import accepted a non-finite AC phase.");
            var oversizedJson = new string(' ', (int)SpiceDrawingLimits.MaxFileBytes + 1);
            if (SpiceDrawingSerializer.TryFromJson(oversizedJson, out _, out _))
                throw new InvalidOperationException("AC-D V2 import bypassed the 1 MB file limit.");
        }

        private static void ValidateAcDImportTransaction()
        {

            var transactionRoot = new GameObject("SpiceAcDImportTransaction", typeof(RectTransform), typeof(Canvas));
            var tempDir = CreateUniqueTempDir("AcDImportTransaction");
            try
            {
                var transactionWorkspace = CreateInitializedWorkspaceForCopy(transactionRoot.transform, out _);
                var resistor = transactionWorkspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                var source = transactionWorkspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 100f);
                transactionWorkspace.Connect(source.InstanceId, SpiceComponentModel.PositiveTerminalId, resistor.InstanceId, SpiceComponentModel.PositiveTerminalId);
                var currentPath = Path.Combine(tempDir, "current.spicejson");
                if (!transactionWorkspace.TrySaveWorkspaceToPath(currentPath, out var saveError))
                    throw new InvalidOperationException("AC-D transaction setup save failed: " + saveError);
                transactionWorkspace.SelectComponent(transactionWorkspace.GetComponentViewForTesting(resistor.InstanceId));
                transactionWorkspace.HandleTerminalClick(transactionWorkspace.GetComponentViewForTesting(resistor.InstanceId), SpiceComponentModel.PositiveTerminalId);
                var beforeModel = transactionWorkspace.Model;
                var beforeJson = SpiceDrawingSerializer.ToJson(beforeModel);
                var beforeRevision = transactionWorkspace.ElectricalRevisionForTesting;
                var beforePath = transactionWorkspace.CurrentSpiceFilePath;
                var beforeDirty = transactionWorkspace.IsDirty;
                var beforeSelected = transactionWorkspace.GetSelectedComponentIdForTesting();
                var beforePending = transactionWorkspace.HasPendingWire;
                var beforeComponentViews = transactionWorkspace.GetComponentViewCountForTesting();
                var beforeWireViews = transactionWorkspace.GetWireViewCountForTesting();
                transactionWorkspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                var beforeResultState = transactionWorkspace.ResultState;
                var beforeCopyResult = transactionWorkspace.TryGetCopyableOutcomeText(out var beforeResultText);
                var beforeCopyNetlist = transactionWorkspace.TryGetCopyableNetlistText(out var beforeNetlistText);
                const string invalidV2 = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":2,\"analysis\":{\"mode\":\"Unknown\",\"frequencyHz\":\"1000\"},\"components\":[],\"wires\":[]}";
                if (transactionWorkspace.TryImportDrawingJson(invalidV2, out _) || !ReferenceEquals(beforeModel, transactionWorkspace.Model) ||
                    beforeJson != SpiceDrawingSerializer.ToJson(transactionWorkspace.Model) || beforeRevision != transactionWorkspace.ElectricalRevisionForTesting ||
                    beforePath != transactionWorkspace.CurrentSpiceFilePath || beforeDirty != transactionWorkspace.IsDirty ||
                    beforeResultState != transactionWorkspace.ResultState || beforeCopyResult != transactionWorkspace.TryGetCopyableOutcomeText(out var afterResultText) ||
                    beforeResultText != afterResultText || beforeCopyNetlist != transactionWorkspace.TryGetCopyableNetlistText(out var afterNetlistText) ||
                    beforeNetlistText != afterNetlistText || beforeSelected != transactionWorkspace.GetSelectedComponentIdForTesting() ||
                    beforePending != transactionWorkspace.HasPendingWire || beforeComponentViews != transactionWorkspace.GetComponentViewCountForTesting() ||
                    beforeWireViews != transactionWorkspace.GetWireViewCountForTesting())
                    throw new InvalidOperationException("AC-D invalid V2 import was not transactional.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(transactionRoot);
                CleanupTempDir(tempDir);
            }
        }

        private static void ValidateAcDAtomicFileWrite()
        {
            var root = new GameObject("SpiceAcDAtomicFileWrite", typeof(RectTransform), typeof(Canvas));
            var tempDir = CreateUniqueTempDir("AcDAtomicFileWrite");
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(root.transform, out _);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                var initialRevision = workspace.ElectricalRevisionForTesting;
                var newTarget = Path.Combine(tempDir, "new-target.spicejson");
                if (!workspace.TrySaveWorkspaceToPath(newTarget, out var newError) || !File.Exists(newTarget) || newError != null ||
                    workspace.ElectricalRevisionForTesting != initialRevision || workspace.ResultState != SpiceWorkspaceResultState.Current || workspace.IsDirty)
                    throw new InvalidOperationException("AC-D atomic write did not create a new drawing without changing electrical state.");

                var sentinelPath = Path.Combine(tempDir, "existing-target.spicejson");
                const string sentinel = "AC-D-ATOMIC-SENTINEL";
                File.WriteAllText(sentinelPath, sentinel, System.Text.Encoding.UTF8);
                var sentinelHash = Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(sentinelPath)));
                if (!workspace.TrySaveWorkspaceToPath(sentinelPath, out var replaceError) || replaceError != null ||
                    Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(sentinelPath))) == sentinelHash ||
                    workspace.ElectricalRevisionForTesting != initialRevision || workspace.ResultState != SpiceWorkspaceResultState.Current)
                    throw new InvalidOperationException("AC-D atomic write did not replace an existing target atomically.");

                // 目标文件作为父目录会使文件服务在任何临时文件创建前失败，用于验证失败路径不破坏哨兵和会话状态。
                var beforeFailedPath = workspace.CurrentSpiceFilePath;
                var beforeFailedDirty = workspace.IsDirty;
                var beforeFailedHash = Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(sentinelPath)));
                var invalidTarget = Path.Combine(sentinelPath, "cannot-create.spicejson");
                if (workspace.TrySaveWorkspaceToPath(invalidTarget, out _) || workspace.CurrentSpiceFilePath != beforeFailedPath ||
                    workspace.IsDirty != beforeFailedDirty || Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes(sentinelPath))) != beforeFailedHash ||
                    Directory.GetFiles(tempDir, "*.tmp", SearchOption.AllDirectories).Length != 0 ||
                    Directory.GetFiles(tempDir, "*.backup", SearchOption.AllDirectories).Length != 0)
                    throw new InvalidOperationException("AC-D atomic write failure changed a sentinel file or left temporary artifacts.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                CleanupTempDir(tempDir);
            }
        }

        private static void ValidateAcDV1Compatibility()
        {

            var legacyFixtures = new[]
            {
                "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[],\"wires\":[]}",
                "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"ground-001\",\"componentType\":\"Ground\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0}],\"wires\":[]}",
                "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"switch-001\",\"componentType\":\"IdealSwitch\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":1,\"siValueText\":\"1\"}],\"wires\":[]}",
                "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"resistor-001\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1000\"}],\"wires\":[]}"
            };
            foreach (var legacyJson in legacyFixtures)
            {
                if (!SpiceDrawingSerializer.TryFromJson(legacyJson, out var legacy, out var legacyError) || legacy.AnalysisMode != SpiceAnalysisMode.DcOperatingPoint ||
                    legacy.AcFrequencyHz != SpiceAnalysisLimits.DefaultFrequencyHz || !SpiceDrawingSerializer.ToJson(legacy).Contains("\"schemaVersion\": 2"))
                    throw new InvalidOperationException("AC-D V1 compatibility fixture failed: " + legacyError);
            }
        }

        private static void AddWorkspaceWire(SpiceWorkspaceModel model, SpiceWorkspaceComponentData start, string startTerminal, SpiceWorkspaceComponentData end, string endTerminal)
        {
            if (!model.AddWire(start.InstanceId, startTerminal, end.InstanceId, endTerminal))
                throw new InvalidOperationException("AC-D fixture wire construction failed.");
        }

        private static void ValidateAcAnalysisGraphBuilderBoundaries()
        {
            var supported = CreateBasicAcCircuit();
            var capacitor = SpiceComponentModel.Capacitor("capacitor-001", 1e-6d);
            var inductor = SpiceComponentModel.Inductor("inductor-001", 0.01d);
            var switchComponent = SpiceComponentModel.IdealSwitch("switch-001", false);
            var voltageProbe = SpiceComponentModel.VoltageProbe("voltage-probe-001");
            var currentProbe = SpiceComponentModel.CurrentProbe("current-probe-001");
            supported.Components.Add(capacitor);
            supported.Components.Add(inductor);
            supported.Components.Add(switchComponent);
            supported.Components.Add(voltageProbe);
            supported.Components.Add(currentProbe);
            supported.Wires.Clear();
            AddWire(supported, "ac-source-001", "positive", "current-probe-001", "positive");
            AddWire(supported, "current-probe-001", "negative", "switch-001", "positive");
            AddWire(supported, "switch-001", "negative", "resistor-001", "positive");
            AddWire(supported, "resistor-001", "negative", "capacitor-001", "positive");
            AddWire(supported, "capacitor-001", "negative", "inductor-001", "positive");
            AddWire(supported, "inductor-001", "negative", "ground-001", "ground");
            AddWire(supported, "ac-source-001", "negative", "ground-001", "ground");
            AddWire(supported, "voltage-probe-001", "positive", "resistor-001", "positive");
            AddWire(supported, "voltage-probe-001", "negative", "ground-001", "ground");
            var supportedGraph = SpiceCircuitGraphBuilder.Build(supported);
            if (!supportedGraph.IsValid || supportedGraph.SpiceNameByComponentId["ac-source-001"] != "V1")
                throw new InvalidOperationException("Supported AC V1 component set was rejected or AC source did not use V prefix: " +
                    string.Join(",", supportedGraph.Diagnostics.Select(diagnostic => diagnostic.Code)));

            var multipleSources = CreateBasicAcCircuit();
            multipleSources.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-002", 2d, 30d));
            AddWire(multipleSources, "ac-source-002", "positive", "resistor-001", "positive");
            AddWire(multipleSources, "ac-source-002", "negative", "ground-001", "ground");
            var multipleGraph = SpiceCircuitGraphBuilder.Build(multipleSources);
            if (!multipleGraph.IsValid || multipleGraph.SpiceNameByComponentId["ac-source-001"] != "V1" || multipleGraph.SpiceNameByComponentId["ac-source-002"] != "V2")
                throw new InvalidOperationException("Multiple AC voltage sources should be valid and receive stable V names.");

            var dcCircuit = new SpiceCircuitModel();
            dcCircuit.Components.Add(SpiceComponentModel.DcVoltageSource("source-001", 5d));
            dcCircuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            dcCircuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            AddWire(dcCircuit, "source-001", "positive", "resistor-001", "positive");
            AddWire(dcCircuit, "source-001", "negative", "ground-001", "ground");
            AddWire(dcCircuit, "resistor-001", "negative", "ground-001", "ground");
            var dcGraph = SpiceCircuitGraphBuilder.Build(dcCircuit);
            if (!dcGraph.IsValid || dcGraph.SpiceNameByComponentId["source-001"] != "V1" || dcGraph.SpiceNameByComponentId["resistor-001"] != "R1")
                throw new InvalidOperationException("Existing DC graph names changed after AC model addition.");

            var missingSource = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, 1000d));
            missingSource.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            missingSource.Components.Add(SpiceComponentModel.Ground("ground-001"));
            AddWire(missingSource, "resistor-001", "positive", "ground-001", "ground");
            AddWire(missingSource, "resistor-001", "negative", "ground-001", "ground");
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(missingSource), "SPICE_AC_SOURCE_MISSING");

            var unsupportedAcFactories = new Func<SpiceComponentModel>[]
            {
                () => SpiceComponentModel.DcVoltageSource("source-001", 5d),
                () => SpiceComponentModel.DcCurrentSource("current-source-001", 0.001d),
                () => SpiceComponentModel.SiliconDiode("diode-001")
            };
            foreach (var createUnsupported in unsupportedAcFactories)
            {
                var unsupportedAc = CreateBasicAcCircuit();
                unsupportedAc.Components.Add(createUnsupported());
                AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(unsupportedAc), "SPICE_AC_COMPONENT_UNSUPPORTED");
            }

            var unsupportedDc = new SpiceCircuitModel();
            unsupportedDc.Components.AddRange(CreateBasicAcCircuit().Components);
            unsupportedDc.Wires.AddRange(CreateBasicAcCircuit().Wires);
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(unsupportedDc), "SPICE_DC_COMPONENT_UNSUPPORTED");

            var invalidFrequency = CreateBasicAcCircuit(0d);
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(invalidFrequency), "SPICE_AC_FREQUENCY_INVALID");

            var invalidMagnitude = CreateBasicAcCircuit();
            invalidMagnitude.Components[0] = new SpiceComponentModel("ac-source-001", SpiceComponentKind.AcVoltageSource)
                .With(SpiceParameterKey.AcMagnitude, 0d);
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(invalidMagnitude), "SPICE_INVALID_PARAMETER");

            var invalidPhase = CreateBasicAcCircuit();
            invalidPhase.Components[0] = new SpiceComponentModel("ac-source-001", SpiceComponentKind.AcVoltageSource, double.NaN)
                .With(SpiceParameterKey.AcMagnitude, 1d);
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(invalidPhase), "SPICE_AC_SOURCE_PHASE_INVALID");

            var unnormalizedPhase = CreateBasicAcCircuit();
            unnormalizedPhase.Components[0] = new SpiceComponentModel("ac-source-001", SpiceComponentKind.AcVoltageSource, 190d)
                .With(SpiceParameterKey.AcMagnitude, 1d);
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(unnormalizedPhase), "SPICE_AC_SOURCE_PHASE_INVALID");

            var sourceShort = CreateBasicAcCircuit();
            AddWire(sourceShort, "ac-source-001", "positive", "ground-001", "ground");
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(sourceShort), "SPICE_SOURCE_SHORTED");

            var probeConflict = CreateBasicAcCircuit();
            probeConflict.Components.Add(SpiceComponentModel.CurrentProbe("current-probe-001"));
            AddWire(probeConflict, "current-probe-001", "positive", "resistor-001", "positive");
            AddWire(probeConflict, "current-probe-001", "negative", "ground-001", "ground");
            AssertGraphHasDiagnostic(SpiceCircuitGraphBuilder.Build(probeConflict), "SPICE_CURRENT_PROBE_CONSTRAINT_CONFLICT");
        }

        private static SpiceCircuitModel CreateBasicAcCircuit(double frequencyHz = 1000d)
        {
            var circuit = new SpiceCircuitModel(new SpiceAnalysisSettings(SpiceAnalysisMode.AcSingleFrequency, frequencyHz));
            circuit.Components.Add(SpiceComponentModel.AcVoltageSource("ac-source-001", 1d, 0d));
            circuit.Components.Add(SpiceComponentModel.Resistor("resistor-001", 1000d));
            circuit.Components.Add(SpiceComponentModel.Ground("ground-001"));
            AddWire(circuit, "ac-source-001", "positive", "resistor-001", "positive");
            AddWire(circuit, "ac-source-001", "negative", "ground-001", "ground");
            AddWire(circuit, "resistor-001", "negative", "ground-001", "ground");
            return circuit;
        }

        private static void AddWire(SpiceCircuitModel circuit, string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            circuit.Wires.Add(new SpiceWireModel(
                new SpiceTerminalRef(startComponentId, startTerminalId),
                new SpiceTerminalRef(endComponentId, endTerminalId)));
        }

        private static void AssertGraphHasDiagnostic(SpiceCircuitGraph graph, string code)
        {
            if (!graph.Diagnostics.Any(diagnostic => diagnostic.Code == code))
                throw new InvalidOperationException("Expected graph diagnostic was missing: " + code);
        }

        private sealed class ImmediateSynchronizationContext : System.Threading.SynchronizationContext
        {
            public override void Post(System.Threading.SendOrPostCallback callback, object state)
            {
                callback(state);
            }
        }
    }
}
