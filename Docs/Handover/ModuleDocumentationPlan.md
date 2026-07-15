# 模块文档与注释覆盖计划

## 已有交接文档覆盖

| 已有文档 | 已覆盖模块 | 尚需扩充 |
|---|---|---|
| `ProjectOverview.md` | 产品范围、18 模板、边界 | 练习模式与平台能力说明 |
| `ArchitectureMap.md` | 主分层、核心依赖、风险表 | 元件行为、练习网表的细化关系 |
| `CircuitRuntimeFlow.md` | Workspace、仿真、运行态、Inspector 路径 | 量测和视觉 Prefab 的运行态映射 |
| `RuleSystemGuide.md` | Validation、RuleId、Severity | 旧 `CircuitRuleChecker` 与新 Validation 的长期边界 |
| `TemplateAuthoringGuide.md` | 模板 DTO、目录、真实生成路径 | 模板编辑会话与布局更新工具 |
| `ComponentExtensionGuide.md` | 定义、端子、Prefab、Analyzer、规则、保存 | 各元件类别的专用接入例子 |
| `TestingAndReleaseChecklist.md` | 构建、基线、取证、人工回归 | 练习模式和平台文件对话框专项回归 |
| `KnownRisksAndDoNotTouch.md` | 高风险禁改区、TODO 扫描 | 明确的 Demo 场景清理立项标准 |

## 建议新增或扩充的模块文档

1. **PracticeModeGuide**：网表构建、学生/标准图对比、评分和退出练习边界。
2. **ComponentCatalogAndTerminalGuide**：元件定义、端子角色、Visual Prefab 注册和电压规格展示。
3. **UiAssemblyAndLifecycleGuide**：场景预置与运行时创建、页面切换幂等、事件监听约束。
4. **SaveLoadCompatibilityGuide**：用户图纸版本、导入失败、历史兼容和持久化目录策略。
5. **EditorDiagnosticsGuide**：每个诊断窗口的用途、Play Mode 前提、输出目录和不得提交的产物。

这些文档不在 V2.3.9.4.2 创建；应与相应模块的注释批次一起完成，避免空泛文档先行。

## 后续注释批次

每批只补中文注释，不改行为；每批 15 至 25 个脚本，同批以同一模块和同类风险为主。

### Batch A：核心仿真、运行态、分析与接线（18）

`ActualSupplyVoltageResolver.cs`、`CircuitComponent.cs`、`CircuitStateAnalyzer.cs`、`ParameterValueResolver.cs`、`RuntimeStateManager.cs`、`SimulationEngine.cs`、`TeachingParameterCalculationService.cs`、`TerminalView.cs`、`TopologyTraversalLimits.cs`、`WireManager.cs`、`WireView.cs`、`WorkspaceController.cs`、`MeasurementPanel.cs`、`OscilloscopeWaveform.cs`、`WorkspaceGrid.cs`、`MotorVisualController.cs`、`KTTimerVisualController.cs`、`VisualPrefabInstance.cs`。

重点：状态边界、图遍历预算、运行态重置、保存影响。必跑：运行态模板、规则测试、18 模板基线。

### Batch B：规则、校验 Helper、参数与模板/保存（22）

`CircuitValidationService.cs`、`MotorPhaseValidationHelper.cs`、`PowerPotentialValidationHelper.cs`、`ProtectionBypassValidationHelper.cs`、`ReversingPairScopeHelper.cs`、`SelfHoldingBranchValidationHelper.cs`、`ThermalRelayProtectionScopeHelper.cs`、`TimerControlBypassValidationHelper.cs`、`CircuitRuleChecker.cs`、`CircuitRuleCheckTeacherFormatter.cs`、`IndustrialCircuitRuleAnalyzer.cs`、`TeachingCheckReportFormatter.cs`、`CircuitTemplateCatalogLoader.cs`、`CircuitTemplateLoader.cs`、`CircuitTemplateSpawnService.cs`、`TemplateLoadController.cs`、`TemplateSelectionPanel.cs`、`SaveLoadService.cs`、`SaveBlueprintDialog.cs`、`ImportBlueprintPanel.cs`、`SystemTemplateLayoutUpdater.cs`、`UpdateTemplateLayoutController.cs`。

重点：RuleId/Severity 契约、模板兼容、用户数据。必跑：全部安全/负向测试、模板完整性、18 模板、保存加载。

### Batch C：检查助手、练习模式与教学输出（20）

`CircuitSummaryBuilder.cs`、`IndustrialCircuitExplainer.cs`、`InspectionReportComposer.cs`、`InspectionWorkflowService.cs`、`LocalInspectorPanel.cs`、`InspectionReportData.cs`、`ComponentMappingSolver.cs`、`Practice/Netlist/PracticeConnectionChecker.cs`、`StandardNetlistBuilder.cs`、`StudentNetlistBuilder.cs`、`UnionFind.cs`、`Practice/PracticeConnectionChecker.cs`、`PracticeFeedbackFormatter.cs`、`PracticeScoreCalculator.cs`、`PracticeSessionController.cs`、`CircuitRuleCheckFormatter.cs`、`CircuitCheckResult.cs`、`CircuitAnalysisResult.cs`、`IInspectionWorkflowRuntimeAdapter.cs`、`InspectionWorkflowResult.cs`。

重点：报告结构、练习评分边界、Adapter 约束。必跑：Inspector 模型测试、18 模板、练习人工回归。

### Batch D：主 UI、画布、导航与元件池（20）

`DemoRuntimeBootstrap.cs`、`DemoUIController.cs`、`TopNavigationController.cs`、`PageRouter.cs`、`MainUiTheme.cs`、`PaletteController.cs`、`PaletteItem.cs`、`ComponentParameterView.cs`、`BlueprintController.cs`、`BlueprintReferencePanel.cs`、`TemplateListItem.cs`、`SavedBlueprintListItem.cs`、`AppSession.cs`、`LoginController.cs`、`CurrentUserView.cs`、`LocalProfilePageController.cs`、`ParameterPanelDragHandle.cs`、`UiIconLibrary.cs`、`UpdateTemplateLayoutConfirmDialog.cs`、`TemplateEditSession.cs`。

重点：场景预置与动态创建、监听器、布局不变性。必跑：六页面 20 轮切换、保存/加载/模板人工回归。

### Batch E：内容页面、常用工具和视觉 Prefab（18）

`SimulationGalleryPageController.cs`、`EncyclopediaController.cs`、`CommonToolsPageController.cs`、`ElectricianCalculatorController.cs`、`CommonToolsTeachingContent.cs`、`CommonToolsSeedData.cs`、`CommonArticleEntry.cs`、`CommonFormulaEntry.cs`、`ResistorColorEntry.cs`、`KTDelaySettingDialog.cs`、`KTTimerHitArea.cs`、`VisualPrefabConfig.cs`、`VisualPrefabRegistry.cs`、`WireBendHandle.cs`、`ComponentDefinition.cs`、`ComponentParameter.cs`、`ComponentParameterSet.cs`、`ParameterKeys.cs`。

重点：内容数据与交互分界、KT 参数语义、视觉注册。必跑：各页面人工回归、KT 保存加载、1366/1920/4K UI 检查。

### Batch F：Editor、测试、基线与诊断（20）

`ArchitectureBaselineSnapshotWriter.cs`、`DemoSceneBuilder.cs`、`TemplateIntegrityChecker.cs`、`SceneRuntimeEvidenceWindow.cs`、`InspectionReportComposerTests.cs`、`PowerSafetyValidationTests.cs`、`ProtectionBypassValidationTests.cs`、`ThermalTimerBypassValidationTests.cs`、`TopologySafetyTests.cs`、`EditRuntimeUiComparisonWindow.cs`、`FinalUiAuditWindow.cs`、`SimulationUiFontComparisonWindow.cs`、`UiTypographyRuntimeAuditWindow.cs`、`UiTypographyRuntimeAuditRunner.cs`、`CommonToolsAuditor.cs`、`PhaseA_Auditor.cs`、`ExpandCanvasScript.cs`、`ImportUIAssets.cs`、`NativeFileBrowser.cs`、`WindowsFileDialog.cs`。

重点：Editor-only 前提、输出路径、不得写回场景、基线不可自动覆盖。必跑：对应菜单测试与 Console 检查。

### Batch G：DTO、枚举、短适配器与豁免复核（20）

`ElectricalEnums.cs`、`TerminalConstants.cs`、`CircuitValidationCategory.cs`、`CircuitValidationIssue.cs`、`CircuitValidationReport.cs`、`CircuitValidationSeverity.cs`、`MotorPhaseValidationResult.cs`、`CircuitIssue.cs`、`CircuitIssueSeverity.cs`、`CircuitTemplateCatalogDto.cs`、`CircuitTemplateDto.cs`、`SavedBlueprintInfo.cs`、`PageId.cs`、`PracticeConnectionCheckResult.cs`、`PracticeConnectionIssue.cs`、`PracticeNetlist.cs`、`PracticeNetlistConnection.cs`、`PracticeNetlistTerminal.cs`、`PracticeConnectionFeedbackFormatter.cs`、`NativeFileBrowserReceiver.cs`。

重点：确认豁免理由、补必要字段语义，不做冗余逐行注释。必跑：两个程序集和最终清单复核。

## 批次规模结论

建议后续共 **7 批**。各批 18 至 22 个文件；High 风险核心不会与低风险 DTO 混在同一提交中。每批完成后，更新 [CommentCoverageMatrix.md](CommentCoverageMatrix.md)，但不顺带开展架构拆分。
