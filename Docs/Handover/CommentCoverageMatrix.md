# 注释覆盖矩阵

## 状态定义

| 状态 | 含义 | 数量 |
|---|---|---:|
| 已完成 | 已有中文类级职责/边界说明；复杂类仍可能在后续批次补方法级说明。 | 14 |
| 需要详细注释 | 状态多、顺序敏感、影响规则/保存/模板/场景；需要类级与关键方法约束。 | 30 |
| 只需类级注释 | 普通控制器、页面、列表、格式化器或中等复杂服务。 | 67 |
| 可以豁免 | DTO、枚举、短常量/适配器或极短且用途清楚脚本。 | 38 |

完整逐脚本的路径、代码行数、模块、风险和当前注释状态见 [CodebaseInventory.md](CodebaseInventory.md)。本矩阵规定后续处理方式、豁免理由、测试和批次；任何脚本即使豁免也不从盘点中消失。

## Batch A2 当前完成状态

> 本表记录本批 6 个测量、画布显示与 Visual Prefab 运行脚本的实际完成状态；目标等级为“详细注释”。
> 示波器当前没有可操作的元件池入口，本轮只记录该人工验证缺口，不新增入口或改变器件池。

| 脚本 | 当前完成状态 | 目标注释等级 | 修改后必跑测试 |
|---|---|---|---|
| `UI/MeasurementPanel.cs` | 类级、测量显示数据来源和空闲显示边界已完成 | 详细注释 | 选中元件、测量/参数面板、Inspector 模型测试 |
| `UI/OscilloscopeWaveform.cs` | 类级、教学波形边界和 UGUI 重绘约束已完成 | 详细注释 | 当前示波器入口不可达，后续恢复入口后检查波形与分辨率 |
| `UI/WorkspaceGrid.cs` | 类级、纯视觉网格与交互边界已完成 | 详细注释 | 1366×768、1920×1080、4K 画布显示 |
| `UI/VisualPrefab/MotorVisualController.cs` | 类级、父级状态读取和动画边界已完成 | 详细注释 | 普通电机、正反转、自动往返、星三角 |
| `UI/VisualPrefab/KTTimerVisualController.cs` | 类级、KT 参数/运行态显示边界已完成 | 详细注释 | KT 设置、倒计时、停止复位、保存导入 |
| `UI/VisualPrefab/VisualPrefabInstance.cs` | 类级、视觉实例、端子锚点缓存和保守回退约束已完成 | 详细注释 | 普通元件、KT、电机 Visual Prefab、保存导入 |

## Batch A1 当前完成状态

> 本表记录本批 12 个核心脚本的实际完成状态；目标等级为“详细注释”，
> 已补充类级职责、关键顺序或生命周期约束。原有统计口径待全部批次结束后统一重算。

| 脚本 | 当前完成状态 | 目标注释等级 | 后续补充 |
|---|---|---|---|
| `Core/ActualSupplyVoltageResolver.cs` | 类级与关键解析约束已完成 | 详细注释 | 无 |
| `Core/CircuitComponent.cs` | 类级、实例参数、端子和视觉 Prefab 约束已完成 | 详细注释 | 无 |
| `Core/CircuitStateAnalyzer.cs` | 类级与危险方法约束已完成 | 详细注释 | 无 |
| `Core/ParameterValueResolver.cs` | 类级与实例/定义回退边界已完成 | 详细注释 | 无 |
| `Core/RuntimeStateManager.cs` | 类级与运行态清理边界已完成 | 详细注释 | 无 |
| `Core/SimulationEngine.cs` | 类级与稳定循环、KT 顺序约束已完成 | 详细注释 | 无 |
| `Core/TeachingParameterCalculationService.cs` | 类级与教学估算边界已完成 | 详细注释 | 无 |
| `Core/TerminalView.cs` | 类级与端子引用生命周期已完成 | 详细注释 | 无 |
| `Core/TopologyTraversalLimits.cs` | 类级与安全预算约束已完成 | 详细注释 | 无 |
| `Core/WireManager.cs` | 类级与活动导线集合约束已完成 | 详细注释 | 无 |
| `Core/WireView.cs` | 类级与路由、颜色、保存边界已完成 | 详细注释 | 无 |
| `Core/WorkspaceController.cs` | 类级与活动图、快照、仿真生命周期已完成 | 详细注释 | 无 |

## 已完成

| 脚本 | 覆盖内容 | 后续 | 测试 |
|---|---|---|---|
| `Core/CircuitStateAnalyzer.cs` | 职责、活动图输入、稳定循环约束 | Batch A 补危险方法 | 18 模板、规则、运行态 |
| `Core/SimulationEngine.cs` | 步进职责、非 SPICE 边界、稳定顺序 | Batch A 补状态更新方法 | 运行态模板 |
| `Core/RuntimeStateManager.cs` | 运行态归属和重置边界 | Batch A 复核 | 运行态模板 |
| `Core/WorkspaceController.cs` | 活动图权威来源、场景扫描禁令 | Batch A 补历史/仿真方法 | 模板、保存加载 |
| `Core/WireManager.cs` | 活动导线权威输入 | Batch A 复核 | 接线、保存加载 |
| `Core/Validation/CircuitValidationService.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。聚合职责、规则契约、调用顺序。 | 无 | Inspector 报告模型测试；18 张真实模板架构基线；四组 Validation 规则测试 |
| `Core/Validation/MotorPhaseValidationHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。相线与星三角证据边界。 | 无 | 18 模板基线；Topology Safety；普通三相电机与星三角负向用例 |
| `Core/Validation/PowerPotentialValidationHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。电位证据边界。 | 无 | Power Safety Validation Tests；18 模板基线 |
| `Core/Validation/ProtectionBypassValidationHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。旁路检测与保守遍历。 | 无 | Protection Bypass Validation Tests；18 模板基线 |
| `Core/Validation/ReversingPairScopeHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。正反转作用域与互锁边界。 | 无 | Protection Bypass Validation Tests；正反转互锁与冲突人工回归；Topology Safety |
| `Core/Validation/SelfHoldingBranchValidationHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。自锁与点动分支边界。 | 无 | 连续运行、点动、自锁支路人工回归；18 模板基线；Topology Safety |
| `Core/Validation/ThermalRelayProtectionScopeHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。热继电器保护作用域。 | 无 | Thermal / Timer Bypass Validation Tests；热继保护模板人工回归；Topology Safety |
| `Core/Validation/TimerControlBypassValidationHelper.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B1。KT 控制旁路证据边界。 | 无 | Thermal / Timer Bypass Validation Tests；两电机顺序启动与星三角人工回归；Topology Safety |
| `Rules/CircuitRuleChecker.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B2。教学规则入口、双图证据和误报过滤边界。 | 无 | Inspector 报告模型测试；18 模板架构基线；家庭与普通工业检查人工回归 |
| `Rules/CircuitRuleCheckTeacherFormatter.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B2。教学文本分组、严重级别读取和快照顺序约束。 | 无 | Inspector 报告模型测试；报告 Section 顺序和正文快照；正常、Warning、Error 三类报告人工回归 |
| `AI/IndustrialCircuitRuleAnalyzer.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B2。工业教学范围、活动工作区证据和类型识别边界。 | 无 | 18 模板基线；正反转、自动往返、两电机顺序启动、星三角人工回归；正常工业模板无新增误报 |
| `AI/TeachingCheckReportFormatter.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B2。报告 Section 组装、调试信息边界和结构化 Block 前置契约。 | 无 | Inspector 报告模型测试；18 模板 UI blocks 和 modelBlocks；参数估算、教学说明和开发调试开关人工回归 |
| `Templates/CircuitTemplateCatalogLoader.cs` | 当前完成状态：详细注释已完成；目标注释等级：类级；完成批次：Batch B3-A。目录读取、Resources 路径与 Catalog 契约。 | 无 | Template Integrity；18 模板基线；Resources 路径检查 |
| `Templates/CircuitTemplateLoader.cs` | 当前完成状态：详细注释已完成；目标注释等级：类级；完成批次：Batch B3-A。单模板读取、Editor/Resources 读取边界。 | 无 | Template Integrity；18 模板基线；Resources 路径检查 |
| `Templates/CircuitTemplateSpawnService.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B3-A。先校验再清空、实例映射和生成失败边界。 | 无 | Template Integrity；18 模板真实生成基线；清空后重新加载家庭和工业模板；模板生成失败边界检查 |
| `UI/TemplateLoadController.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B3-A。目录、单模板 Loader 与 SpawnService 的协调边界。 | 无 | 图纸集模板加载；仿真广场模板加载；家庭和工业模板人工回归 |
| `UI/TemplateSelectionPanel.cs` | 当前完成状态：详细注释已完成；目标注释等级：类级；完成批次：Batch B3-A。Catalog 卡片、分类选择与重复初始化边界。 | 无 | 家庭/工业分类；模板数量；重复打开关闭；卡片加载动作 |
| `UI/SystemTemplateLayoutUpdater.cs` | 当前完成状态：详细注释已完成；目标注释等级：详细；完成批次：Batch B3-A。Editor 布局回写、结构校验、备份与字段边界。 | 无 | 更新布局确认流程；不改变元件、导线、参数和 RuleId；18 模板基线 |
| `UI/UpdateTemplateLayoutController.cs` | 当前完成状态：详细注释已完成；目标注释等级：类级；完成批次：Batch B3-A。维护入口、确认反馈与 Editor 写回边界。 | 无 | 确认、取消和失败提示；当前模板识别；更新后模板重新加载 |
| `Templates/CircuitTemplateSpawnService.cs` | 先校验后清空的模板生成约束 | Batch B 补生成/回滚方法 | 18 模板 |
| `UI/SaveLoadService.cs` | 用户图纸兼容与目录边界 | Batch B 补兼容路径 | 保存加载 |
| `AI/LocalInspectorPanel.cs` | UGUI 宿主与 Workflow 边界 | Batch C 补报告/练习方法 | Inspector、18 模板 |
| `AI/InspectionWorkflowService.cs` | 无 UGUI 编排与顺序约束 | Batch C 复核 | Inspector、18 模板 |
| `AI/InspectionReportComposer.cs` | 结构化 Block 契约 | Batch C 复核 | 模型测试 |

## 需要详细注释（30）

| 模块 | 脚本 | 必须说明 | 批次 |
|---|---|---|---|
| Core | `ActualSupplyVoltageResolver`、`CircuitComponent`、`TeachingParameterCalculationService`、`WireView` | 电压来源、元件状态、参数估算、导线副作用 | A |
| Rules/Inspector | `CircuitRuleChecker`、`IndustrialCircuitRuleAnalyzer`、`TeachingCheckReportFormatter` | 旧规则/工业规则与教学文本边界 | B/C |
| Templates/SaveLoad | `TemplateLoadController`、`SystemTemplateLayoutUpdater` | 模板入口、布局更新、副作用 | B |
| Practice | `ComponentMappingSolver`、`Practice/PracticeConnectionChecker`、`PracticeSessionController` | 网表匹配、评分、会话状态 | C |
| UI | `ComponentParameterView`、`DemoRuntimeBootstrap`、`DemoUIController`、`PageRouter`、`PaletteController`、`VisualPrefab/KTDelaySettingDialog`、`VisualPrefabRegistry` | 场景注入、动态创建、监听器、KT 保存边界 | D/E |
| Editor | `ArchitectureBaselineSnapshotWriter`、`DemoSceneBuilder`、`TemplateIntegrityChecker` | 基线不可覆盖、场景构建、真实模板路径 | F |

## 只需类级注释（67）

处理原则：写清“负责什么、不负责什么、主要依赖/调用方、修改后跑什么”，不逐行解释 UI 创建代码。具体文件分批如下：

- **Batch A**：`ParameterValueResolver`、`TerminalView`、`TopologyTraversalLimits`、`MeasurementPanel`、`OscilloscopeWaveform`、`WorkspaceGrid`、`KTTimerVisualController`、`VisualPrefabInstance`。
- **Batch B**：`CircuitRuleCheckTeacherFormatter`、`CircuitTemplateCatalogLoader`、`CircuitTemplateLoader`、`TemplateSelectionPanel`、`ImportBlueprintPanel`、`SaveBlueprintDialog`、`UpdateTemplateLayoutController`。
- **Batch C**：`CircuitSummaryBuilder`、`IndustrialCircuitExplainer`、`Practice/Netlist/PracticeConnectionChecker`、`StandardNetlistBuilder`、`StudentNetlistBuilder`、`UnionFind`、`PracticeScoreCalculator`、`CircuitRuleCheckFormatter`。
- **Batch D**：`AppSession`、`BlueprintController`、`BlueprintReferencePanel`、`CurrentUserView`、`LocalProfilePageController`、`LoginController`、`MainUiTheme`、`PaletteItem`、`SavedBlueprintListItem`、`SimulationGalleryPageController`、`TemplateListItem`、`TopNavigationController`、`UiIconLibrary`、`UpdateTemplateLayoutConfirmDialog`。
- **Batch E**：`EncyclopediaController`、`CommonToolsPageController`、`CommonToolsTeachingContent`、`ElectricianCalculatorController`、`KTTimerHitArea`、`VisualPrefabConfig`、`MotorVisualController`。
- **Batch F**：`EditRuntimeUiComparisonWindow`、`FinalUiAuditWindow`、`SimulationUiFontComparisonWindow`、`UiTypographyRuntimeAuditWindow`、`UiTypographyRuntimeAuditRunner`、`SceneRuntimeEvidenceWindow`、`InspectionReportComposerTests`、`PowerSafetyValidationTests`、`ProtectionBypassValidationTests`、`ThermalTimerBypassValidationTests`、`TopologySafetyTests`、`CommonToolsAuditor`、`PhaseA_Auditor`、`ExpandCanvasScript`、`ImportUIAssets`。

## 可以豁免（27）

| 类别 | 文件 | 原因 |
|---|---|---|
| 数据/DTO | `CircuitAnalysisResult`、`InspectionReportData`、`ComponentDefinition`、`ComponentParameter`、`ComponentParameterSet`、`CircuitValidationIssue`、`CircuitValidationReport`、`MotorPhaseValidationResult`、`CircuitCheckResult`、`CircuitIssue`、`CircuitTemplateCatalogDto`、`CircuitTemplateDto`、`SavedBlueprintInfo`、`TemplateEditSession`、`PracticeConnectionCheckResult`、`PracticeConnectionIssue`、`PracticeNetlist`、`PracticeNetlistConnection`、`PracticeNetlistTerminal`、`CommonArticleEntry`、`CommonFormulaEntry`、`CommonToolsSeedData`、`ResistorColorEntry`、`VisualPrefabConfig` | 字段意义清楚或纯数据承载；必要时仅补字段说明。 |
| 枚举/常量/短适配器 | `ElectricalEnums`、`TerminalConstants`、`ParameterKeys`、`CircuitValidationCategory`、`CircuitValidationSeverity`、`CircuitIssueSeverity`、`PageId`、`IInspectionWorkflowRuntimeAdapter`、`InspectionWorkflowResult`、`WireBendHandle`、`ParameterPanelDragHandle`、`NativeFileBrowser`、`NativeFileBrowserReceiver`、`WindowsFileDialog` | 代码短且职责明确；只需保留清单和调用点。 |

## 尚待人工确认

`PhaseA_Auditor.cs`、`UiTypographyRuntimeAuditWindow.cs`、`LoginController.cs`、`CurrentUserView.cs`、`DemoSceneBuilder.cs`、平台文件对话框包装层，需要在后续批次确认是否仍有菜单、场景、Prefab、Build 或反射引用。确认前不得标记废弃。

## 覆盖率结论

首批已对 14/138 个脚本完成类级职责覆盖，约 **10.1%**。若按“必须详细注释”的 High 风险脚本计算，已有首批覆盖但未完全方法级封口的核心类不应被误计为完成；仍有约 18 个 High 风险脚本缺少清晰说明。
