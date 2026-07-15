# 全项目脚本盘点

## 盘点口径

本清单扫描 `Assets/**/*.cs`，不计入 `Library`、`Temp`、`Obj`、`Packages`、构建输出、根目录 `gpt/` 备份、`Reports/` 和 `temp_demo*.cs`。统计时间为 V2.3.9.4.2，基线提交为 `b989b6f`。

| 指标 | 数量 | 说明 |
|---|---:|---|
| C# 文件总数 | 138 | Assets 内的维护范围脚本，包含当前未跟踪的 Editor 诊断脚本。 |
| 生产运行时代码 | 111 | 运行时代码 116 个，扣除 5 个简单 DTO/数据承载脚本。 |
| Editor 工具 | 12 | 不含 5 个 Editor 测试。 |
| 自动化测试 | 5 | 均位于 `Assets/Scripts/Editor/`。 |
| DTO/简单数据 | 5 | 属于运行时程序集，但可按 DTO 豁免原则处理。 |
| 本次排除的根目录 C# 候选 | 13 | `gpt/` 副本与 `temp_demo*.cs`，不属于 Assets 维护范围。 |
| 代码总行数 | 51,103 | 仅用于规模判断，不以行数单独判定风险。 |
| High / Medium / Low | 30 / 63 / 45 | 基于状态、副作用、兼容性、规则和场景影响的人工分级。 |
| 已有类级职责注释 | 14 | V2.3.9.4 首批核心注释。 |
| 仍需补注释 | 86 | 不含可豁免的简单数据、枚举和短适配器。 |
| 可豁免或仅登记 | 38 | DTO、枚举、短数据类、平台适配器或极短 Visual 脚本。 |

## 模块职责、依赖与主要调用方

| 模块 | 数量 | 主要职责 | 主要依赖 | 主要调用方 | 默认测试 |
|---|---:|---|---|---|---|
| Core | 19 | 元件、端子、工作区、仿真和状态基础 | Unity UI、运行态、导线 | UI、模板、规则、Inspector | 模板基线、运行态、安全测试 |
| Validation | 13 | 生产规则与拓扑 Helper | 活动图、Analyzer 结果 | `CircuitValidationService`、规则检查 | 规则/负向测试、18 模板 |
| Rules | 6 | 旧/教学规则检查与格式化 | Workspace、校验模型 | Inspector、练习 | 规则测试、Inspector 基线 |
| Inspector | 11 | 检查、解释、结构化报告 | Core、Rules、Validation | 右侧检查助手 | 模型测试、18 模板 |
| Templates | 5 | 模板 DTO、加载、校验、生成 | Catalog、Workspace | 模板 UI、基线工具 | 模板完整性、18 模板 |
| UI | 40 | 导航、页面、面板、保存加载、视觉 Prefab | Workspace、模板、主题 | Demo 启动与页面路由 | 页面人工回归、20 轮切换 |
| CommonTools | 7 | 公式、色环、计算器与资料 | UI 主题、种子数据 | 常用工具页 | 页面人工回归 |
| Encyclopedia | 1 | 百科卡片与详情 | 数据/资源、UI | 页面路由 | 搜索与详情人工回归 |
| Practice | 15 | 练习网表、比对、评分与会话 | Workspace、模板 | Inspector/练习页面 | 练习人工回归 |
| EditorTools | 11 | 基线、模板完整性、规则测试、场景取证 | 生产模板路径 | Unity 菜单 | 对应 Editor 测试 |
| Diagnostics | 4 | UI/字体/最终巡检 | Play Mode UGUI | Unity 菜单 | 仅诊断，不进发布逻辑 |
| Other / Platform | 6 | 平台文件对话框、UI 诊断 Runner、资产导入 | Unity/系统 API | UI 和编辑器 | 平台/人工验证 |

## 完整脚本索引

标记格式：`路径（行数；类型；风险；注释状态）`。职责、依赖、调用方和测试以所在模块表为默认值；High 脚本的定点说明与处理批次见 [CommentCoverageMatrix.md](CommentCoverageMatrix.md) 和 [ModuleDocumentationPlan.md](ModuleDocumentationPlan.md)。

### Diagnostics（4）

- `Assets/Editor/Diagnostics/EditRuntimeUiComparisonWindow.cs`（770；Editor；Medium；已有类级）
- `Assets/Editor/Diagnostics/FinalUiAudit/FinalUiAuditWindow.cs`（296；Editor；Medium；已有类级）
- `Assets/Editor/Diagnostics/SimulationUiFontComparisonWindow.cs`（406；Editor；Medium；已有类级）
- `Assets/Editor/Diagnostics/UiTypographyRuntimeAuditWindow.cs`（36；Editor；Low；待确认）

### Other 与 Platform（6）

- `Assets/Editor/ExpandCanvasScript.cs`（51；Editor；Low；豁免候选）
- `Assets/Editor/ImportUIAssets.cs`（42；Editor；Low；豁免候选）
- `Assets/Scripts/Diagnostics/UiTypographyRuntimeAuditRunner.cs`（1670；Runtime；Medium；需详细注释）
- `Assets/Scripts/Platform/NativeFileBrowser.cs`（39；Runtime；Low；豁免候选）
- `Assets/Scripts/Platform/NativeFileBrowserReceiver.cs`（50；Runtime；Low；豁免候选）
- `Assets/Scripts/Platform/WindowsFileDialog.cs`（66；Runtime；Low；豁免候选）

### Inspector（11）

- `Assets/Scripts/AI/CircuitAnalysisResult.cs`（64；Runtime；Low；DTO 豁免）
- `Assets/Scripts/AI/CircuitSummaryBuilder.cs`（178；Runtime；Medium；类级）
- `Assets/Scripts/AI/IInspectionWorkflowRuntimeAdapter.cs`（22；Runtime；Low；豁免）
- `Assets/Scripts/AI/IndustrialCircuitExplainer.cs`（128；Runtime；Medium；类级）
- `Assets/Scripts/AI/IndustrialCircuitRuleAnalyzer.cs`（425；Runtime；High；详细）
- `Assets/Scripts/AI/InspectionReportComposer.cs`（276；Runtime；Medium；已完成）
- `Assets/Scripts/AI/InspectionReportData.cs`（96；Data；Low；DTO 豁免）
- `Assets/Scripts/AI/InspectionWorkflowResult.cs`（32；Runtime；Low；豁免）
- `Assets/Scripts/AI/InspectionWorkflowService.cs`（152；Runtime；Medium；已完成）
- `Assets/Scripts/AI/LocalInspectorPanel.cs`（2728；Runtime；High；已完成类级，仍需方法级）
- `Assets/Scripts/AI/TeachingCheckReportFormatter.cs`（589；Runtime；High；详细）

### Core（19）

- `Assets/Scripts/Core/ActualSupplyVoltageResolver.cs`（223；Runtime；High；详细）
- `Assets/Scripts/Core/CircuitComponent.cs`（2201；Runtime；High；详细）
- `Assets/Scripts/Core/CircuitStateAnalyzer.cs`（4005；Runtime；High；已完成类级，仍需方法级）
- `Assets/Scripts/Core/ComponentDefinition.cs`（47；Runtime；Low；DTO 豁免）
- `Assets/Scripts/Core/ComponentParameter.cs`（39；Runtime；Low；DTO 豁免）
- `Assets/Scripts/Core/ComponentParameterSet.cs`（68；Runtime；Low；DTO 豁免）
- `Assets/Scripts/Core/ElectricalEnums.cs`（69；Runtime；Low；豁免）
- `Assets/Scripts/Core/ParameterKeys.cs`（86；Runtime；Low；豁免）
- `Assets/Scripts/Core/ParameterValueResolver.cs`（94；Runtime；Medium；类级）
- `Assets/Scripts/Core/RuntimeStateManager.cs`（248；Runtime；High；已完成）
- `Assets/Scripts/Core/SimulationEngine.cs`（1698；Runtime；High；已完成类级，仍需方法级）
- `Assets/Scripts/Core/TeachingParameterCalculationService.cs`（544；Runtime；High；详细）
- `Assets/Scripts/Core/TerminalConstants.cs`（45；Runtime；Low；豁免）
- `Assets/Scripts/Core/TerminalView.cs`（122；Runtime；Medium；类级）
- `Assets/Scripts/Core/TopologyTraversalLimits.cs`（55；Runtime；Medium；类级）
- `Assets/Scripts/Core/WireBendHandle.cs`（45；Runtime；Low；豁免）
- `Assets/Scripts/Core/WireManager.cs`（182；Runtime；High；已完成）
- `Assets/Scripts/Core/WireView.cs`（886；Runtime；High；详细）
- `Assets/Scripts/Core/WorkspaceController.cs`（1340；Runtime；High；已完成类级，仍需方法级）

### Validation（13）

- `CircuitValidationCategory.cs`（18；Runtime；Low；枚举豁免）
- `CircuitValidationIssue.cs`（17；Runtime；Low；DTO 豁免）
- `CircuitValidationReport.cs`（38；Runtime；Low；DTO 豁免）
- `CircuitValidationService.cs`（918；Runtime；High；已完成类级，仍需方法级）
- `CircuitValidationSeverity.cs`（9；Runtime；Low；枚举豁免）
- `MotorPhaseValidationHelper.cs`（773；Runtime；High；详细）
- `MotorPhaseValidationResult.cs`（17；Runtime；Low；DTO 豁免）
- `PowerPotentialValidationHelper.cs`（521；Runtime；High；已完成类级，仍需方法级）
- `ProtectionBypassValidationHelper.cs`（884；Runtime；High；已完成类级，仍需方法级）
- `ReversingPairScopeHelper.cs`（408；Runtime；High；详细）
- `SelfHoldingBranchValidationHelper.cs`（384；Runtime；High；详细）
- `ThermalRelayProtectionScopeHelper.cs`（347；Runtime；High；详细）
- `TimerControlBypassValidationHelper.cs`（389；Runtime；High；已完成类级，仍需方法级）

### EditorTools 与 Tests（11）

- `ArchitectureBaselineSnapshotWriter.cs`（845；Editor；High；已有文件头说明，待类级）
- `DemoSceneBuilder.cs`（2043；Editor；High；详细）
- `Diagnostics/CommonToolsAuditor.cs`（92；Editor；Low；类级）
- `Diagnostics/PhaseA_Auditor.cs`（122；Editor；Low；尚待人工确认）
- `InspectionReportComposerTests.cs`（144；Test；Medium；类级）
- `PowerSafetyValidationTests.cs`（228；Test；Medium；类级）
- `ProtectionBypassValidationTests.cs`（319；Test；Medium；类级）
- `SceneRuntimeEvidenceWindow.cs`（342；Editor；Medium；类级）
- `TemplateIntegrityChecker.cs`（603；Editor；High；详细）
- `ThermalTimerBypassValidationTests.cs`（367；Test；Medium；类级）
- `TopologySafetyTests.cs`（390；Test；Medium；类级）

### Practice（15）

- `Practice/Netlist/ComponentMappingSolver.cs`（233；Runtime；High；详细）
- `Practice/Netlist/PracticeConnectionChecker.cs`（155；Runtime；Medium；类级）
- `Practice/Netlist/PracticeConnectionCheckResult.cs`（25；Runtime；Low；DTO 豁免）
- `Practice/Netlist/PracticeConnectionFeedbackFormatter.cs`（67；Runtime；Low；豁免）
- `Practice/Netlist/PracticeConnectionIssue.cs`（25；Runtime；Low；DTO 豁免）
- `Practice/Netlist/PracticeNetlist.cs`（96；Runtime；Low；DTO 豁免）
- `Practice/Netlist/PracticeNetlistConnection.cs`（27；Runtime；Low；DTO 豁免）
- `Practice/Netlist/PracticeNetlistTerminal.cs`（25；Runtime；Low；DTO 豁免）
- `Practice/Netlist/StandardNetlistBuilder.cs`（44；Runtime；Medium；类级）
- `Practice/Netlist/StudentNetlistBuilder.cs`（65；Runtime；Medium；类级）
- `Practice/Netlist/UnionFind.cs`（75；Runtime；Medium；类级）
- `Practice/PracticeConnectionChecker.cs`（326；Runtime；High；详细）
- `Practice/PracticeFeedbackFormatter.cs`（66；Runtime；Low；豁免）
- `Practice/PracticeScoreCalculator.cs`（48；Runtime；Medium；类级）
- `Practice/PracticeSessionController.cs`（362；Runtime；High；详细）

### Rules（6）

- `CircuitCheckResult.cs`（49；Runtime；Low；DTO 豁免）
- `CircuitIssue.cs`（13；Runtime；Low；DTO 豁免）
- `CircuitIssueSeverity.cs`（9；Runtime；Low；枚举豁免）
- `CircuitRuleChecker.cs`（1213；Runtime；High；详细）
- `CircuitRuleCheckFormatter.cs`（67；Runtime；Low；类级）
- `CircuitRuleCheckTeacherFormatter.cs`（212；Runtime；Medium；类级）

### Templates（5）

- `CircuitTemplateCatalogDto.cs`（28；Data；Low；DTO 豁免）
- `CircuitTemplateCatalogLoader.cs`（44；Runtime；Medium；类级）
- `CircuitTemplateDto.cs`（46；Data；Low；DTO 豁免）
- `CircuitTemplateLoader.cs`（75；Runtime；Medium；类级）
- `CircuitTemplateSpawnService.cs`（334；Runtime；High；已完成类级，仍需方法级）

### CommonTools（7）

- `CommonArticleEntry.cs`（27；Runtime；Low；DTO 豁免）
- `CommonFormulaEntry.cs`（24；Runtime；Low；DTO 豁免）
- `CommonToolsPageController.cs`（973；Runtime；Medium；类级）
- `CommonToolsSeedData.cs`（93；Data；Low；DTO 豁免）
- `CommonToolsTeachingContent.cs`（318；Runtime；Medium；类级）
- `ElectricianCalculatorController.cs`（806；Runtime；Medium；详细）
- `ResistorColorEntry.cs`（30；Runtime；Low；DTO 豁免）

### Encyclopedia（1）

- `EncyclopediaController.cs`（1706；Runtime；Medium；详细）

### UI（40）

- `AppSession.cs`（65；Runtime；Medium；类级）
- `BlueprintController.cs`（1222；Runtime；Medium；详细）
- `BlueprintReferencePanel.cs`（108；Runtime；Low；类级）
- `ComponentParameterView.cs`（558；Runtime；High；详细）
- `CurrentUserView.cs`（58；Runtime；Low；类级）
- `DemoRuntimeBootstrap.cs`（81；Runtime；High；详细）
- `DemoUIController.cs`（1220；Runtime；High；详细）
- `ImportBlueprintPanel.cs`（390；Runtime；Medium；类级）
- `LocalProfilePageController.cs`（321；Runtime；Medium；类级）
- `LoginController.cs`（279；Runtime；Medium；类级）
- `MainUiTheme.cs`（559；Runtime；Medium；详细）
- `MeasurementPanel.cs`（263；Runtime；Medium；类级）
- `OscilloscopeWaveform.cs`（97；Runtime；Low；类级）
- `PageId.cs`（12；Runtime；Low；枚举豁免）
- `PageRouter.cs`（220；Runtime；High；详细）
- `PaletteController.cs`（1413；Runtime；High；详细）
- `PaletteItem.cs`（158；Runtime；Medium；类级）
- `ParameterPanelDragHandle.cs`（87；Runtime；Low；豁免候选）
- `SaveBlueprintDialog.cs`（335；Runtime；Medium；类级）
- `SavedBlueprintInfo.cs`（15；Data；Low；DTO 豁免）
- `SavedBlueprintListItem.cs`（126；Runtime；Low；类级）
- `SaveLoadService.cs`（677；Runtime；High；已完成类级，仍需方法级）
- `SimulationGalleryPageController.cs`（1337；Runtime；Medium；详细）
- `SystemTemplateLayoutUpdater.cs`（373；Runtime；High；详细）
- `TemplateEditSession.cs`（25；Runtime；Low；DTO 豁免）
- `TemplateListItem.cs`（94；Runtime；Low；类级）
- `TemplateLoadController.cs`（501；Runtime；High；详细）
- `TemplateSelectionPanel.cs`（397；Runtime；Medium；类级）
- `TopNavigationController.cs`（322；Runtime；Medium；已完成类级）
- `UiIconLibrary.cs`（110；Runtime；Low；类级）
- `UpdateTemplateLayoutConfirmDialog.cs`（116；Runtime；Low；类级）
- `UpdateTemplateLayoutController.cs`（102；Runtime；Medium；类级）
- `VisualPrefab/KTDelaySettingDialog.cs`（310；Runtime；High；详细）
- `VisualPrefab/KTTimerHitArea.cs`（36；Runtime；Low；豁免候选）
- `VisualPrefab/KTTimerVisualController.cs`（240；Runtime；Medium；类级）
- `VisualPrefab/MotorVisualController.cs`（50；Runtime；Low；豁免候选）
- `VisualPrefab/VisualPrefabConfig.cs`（54；Runtime；Low；DTO 豁免）
- `VisualPrefab/VisualPrefabInstance.cs`（195；Runtime；Medium；类级）
- `VisualPrefab/VisualPrefabRegistry.cs`（462；Runtime；High；详细）
- `WorkspaceGrid.cs`（78；Runtime；Low；类级）

## 引用和调用方判定说明

本轮不把“零文本引用”直接判定为未使用：Unity 组件可能由场景、Prefab、反射、菜单或 Resources 路径引用。High 和 Medium 脚本的主要依赖/调用方以模块调用图和现有入口为证据；低风险短类只记录用途与所属模块。疑似孤立、重复或临时文件仅列入 [SourceCleanupCandidates.md](SourceCleanupCandidates.md)，不做删除结论。
