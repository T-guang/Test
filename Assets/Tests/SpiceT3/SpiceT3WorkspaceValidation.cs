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
    public static partial class SpiceT3WorkspaceValidation
    {
        public static void RunPureChecks()
        {
            // 执行功能回归前先确认 partial 拆分没有改变验证入口的可发现性或产生重复名称。
            ValidateQualityQ1SuiteSplitIntegrity();
            Debug.Log("[Spice][Quality-Q1] 测试套件拆分完整性：通过");
            ValidateQualityQ1ChineseLogContract();
            Debug.Log("[Spice][Quality-Q1] 中文日志规范：通过");
            ValidateHostBindings();
            ValidateHiddenDemoHostInitialization();
            ValidateSimulationModeSwitching();
            ValidateScrollableTextLayout();
            ValidateOutcomePresentationDiagnostics();
            ValidateFailedRunOutcomePresentation();
            ValidateInstanceNamingReset();
            ValidateElectricalRevisionAndRunningMutationGuards();
            ValidateAcAnalysisSettingsAndSnapshot();
            ValidateAcAnalysisControllerRevisionAndRunningGuard();
            ValidateAcC1AnalysisControls();
            Debug.Log("[Spice][AC-C1] 分析模式控件：通过");
            ValidateAcC1FrequencyInput();
            Debug.Log("[Spice][AC-C1] 频率输入：通过");
            ValidateAcC1PaletteModeMatrix();
            Debug.Log("[Spice][AC-C1] 元件池模式矩阵：通过");
            ValidateAcC1AcSourceParameterEditing();
            Debug.Log("[Spice][AC-C1] 交流源参数编辑：通过");
            ValidateAcC1ResultPresentation();
            Debug.Log("[Spice][AC-C1] 结果展示：通过");
            ValidateAcC1CopyResultConsistency();
            Debug.Log("[Spice][AC-C1] 复制结果一致性：通过");
            ValidateAcC1DcPresentationRegression();
            Debug.Log("[Spice][AC-C1] 直流展示回归：通过");
            ValidateAcC1LayoutStructureSmoke();
            Debug.Log("[Spice][AC-C1] 布局结构冒烟：通过");
            ValidateControllerDcAndAcSimulationPaths();
            ValidateAcAnalysisRevisionDiscardsDelayedResult();
            ValidateAcAnalysisGraphBuilderBoundaries();
            ValidateAcParameterWriteEncapsulation();
            ValidateDrawingV1AcCompatibilityBoundaries();
            ValidateAcDV2Serialization();
            Debug.Log("[Spice][AC-D] V2 序列化：通过");
            ValidateAcDV1Compatibility();
            Debug.Log("[Spice][AC-D] V1 向后兼容：通过");
            ValidateAcDAcDrawingRoundTrip();
            Debug.Log("[Spice][AC-D] AC 图纸往返：通过");
            ValidateAcDOpAmpFeedbackRoundTrip();
            Debug.Log("[Spice][AC-D] 运放反馈图纸往返：通过");
            ValidateAcDImportTransaction();
            Debug.Log("[Spice][AC-D] 导入事务：通过");
            ValidateAcDAtomicFileWrite();
            Debug.Log("[Spice][AC-D] 文件原子写入：通过");
            ValidateAcDD2LimitRegression();
            Debug.Log("[Spice][AC-D] D2 限制回归：通过");
            SpiceAcB2Validation.RunNetlistAndParserChecks();
            SpiceIdealOperationalAmplifierValidation.RunAll();
            ValidateIdealOperationalAmplifierWorkspace();
            Debug.Log("[Spice][OpAmp] 工作区反馈接线：通过");
            ValidateIdealOperationalAmplifierV1FileBoundary();
            Debug.Log("[Spice][OpAmp] V1/V2 文件边界：通过");
            // 复制结果验证：在 Failed 状态下验证复制资格、文本正确性、按钮交互状态和非变性。
            // 有效结果状态需要 ngspice 求解；在 batchmode 中 RunCalculationAsync 会因
            // UnitySynchronizationContext 死锁而无法同步等待。Current 路径的 lastOutcomeText
            // 赋值与 SetResultText 使用同一变量，文本一致性由构造保证。
            ValidateCopyableOutcomeFailedState();
            ValidateCopyableOutcomeNonCopyableStates();
            ValidateCopyEligibilityDoesNotMutate();
            ValidateClearWorkspaceSimulationPresentationState();
            ValidateStaleNetlistCannotBeCopied();
            ValidateImportResetsSimulationPresentationState();
            ValidateUnexpectedSimulationErrorIsSanitized();
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
            ValidateDrawingImportCountLimits();
            ValidateDrawingImportWaypointLimits();
            ValidateDrawingImportCoordinateLimits();
            ValidateDrawingImportStringLimits();
            ValidateDrawingFileImportLimitPreservesWorkspace();
            // 事务式导入与失败状态保护
            ValidateDrawingImportSuccessFullCircuit();
            ValidateIdealSwitchVisualStateSynchronization();
            ValidateDrawingImportFailurePreservesWorkspace();
            ValidateDrawingImportConsecutiveSuccess();
            ValidateDrawingImportEmptyDrawing();
            // 导入运行态保护与必填坐标 JSON 校验
            ValidateDrawingImportRejectedWhileRunning();
            ValidateDrawingImportMissingPositionRejected();
            // 文件路径与原子保存
            ValidateDrawingFileServiceExtensionNormalization();
            ValidateDrawingFileSaveRoundTrip();
            ValidateDrawingFileSaveOverwrite();
            ValidateDrawingFileCurrentPathManagement();
            ValidateDrawingFileImportFailurePreservesWorkspace();
            ValidateDrawingFileRunningGuardBeforeFileAccess();
            ValidateDrawingFileClearResetsPath();
            ValidateDrawingFileChineseAndSpacePath();
            // 文件边界验证与状态提示隔离
            ValidateDrawingFileSaveFailurePreservesPath();
            ValidateDrawingFileOversizedImportRejected();
            ValidateDrawingFileInvalidUtf8ImportRejected();
            ValidateDrawingFileMissingPositionYRejected();
            ValidateDrawingFileSuccessDoesNotWriteStatusText();
            // 文件工具栏与替换确认
            ValidateFileToolbarButtonsCreatedOnce();
            ValidateFileToolbarButtonsDisabledWhileRunning();
            ValidateFileOperationCallbackRunningGuard();
            ValidateSaveRoutesToSaveAsWhenNoCurrentPath();
            ValidateSaveRoutesToSaveCurrentWhenHasPath();
            ValidateShowFileOperationStatusOnlyFileName();
            ValidateShowFileOperationStatusNoStack();
            ValidateReplaceConfirmationDialogCreatedOnceAndReusable();
            ValidateReplaceConfirmationCancelDoesNotImport();
            ValidateReplaceConfirmationConfirmCallsImportOnce();
            ValidateClearWorkspaceRoutesSaveToSaveAs();
            // 弹窗生命周期、Blocker 层级与默认目录失败保护
            ValidateReplaceConfirmationDisposeDestroysAllObjects();
            ValidateReplaceConfirmationOpenSiblingOrder();
            ValidateDefaultDirectoryFailureSkipsDialog();
            // 文件按钮布局与工作目录恢复
            ValidateReplaceConfirmationDialogEnlargedSize();
            ValidateFileToolbarButtonsRightAnchoredLayout();
            // 状态文本横向 Stretch 布局与工具栏区域不重叠
            ValidateStatusTextStretchLayout();
            // 默认文件名、扩展名归一与原生对话框失败分类由对应文件服务测试覆盖。
            ValidateLegacyFileDialogErrorContract();
            ValidateQualityQ1EditorObjectLifetime();
            Debug.Log("[Spice][Quality-Q1] Editor 对象生命周期：通过");
            ValidateQualityQ2TypeMoveIntegrity();
            Debug.Log("[Spice][Quality-Q2] 类型移动完整性：通过");
            ValidateQualityQ2ControllerPartialIntegrity();
            Debug.Log("[Spice][Quality-Q2] Controller partial 完整性：通过");
            ValidateQualityQ2PublicApiCompatibility();
            Debug.Log("[Spice][Quality-Q2] 公共接口兼容：通过");
            ValidateQualityQ2UiHierarchyContract();
            Debug.Log("[Spice][Quality-Q2] UI 层级契约：通过");
            ValidateQualityQ2DirectedDeduplication();
            Debug.Log("[Spice][Quality-Q2] 定向去重：通过");
            AssertQualityQ21Evidence();
            Debug.Log("[Spice][Quality-Q2.1] 注释关联、去重与统计证据：通过");
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
    }
}
