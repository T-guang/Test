# 电路运行态流程

## 活动图归属

元件通过 `WorkspaceController.SpawnComponent` 进入活动图，导线由 `WireManager` 管理。标准模板经过 `CircuitTemplateSpawnService`：先校验 DTO，再替换活动工作区图。用户图纸由 `SaveLoadService` 和元件目录恢复。

`Demo.unity` 含有历史/静态对象，不是电气输入来源。Analyzer、校验、仿真和保存加载必须使用 `workspace.Components` 与 `workspace.WireManager.Wires`。扫描场景中全部 `CircuitComponent` 或 `WireView` 会把残留对象重复计入并污染结果。

## 点击开始仿真后的主流程

1. `WorkspaceController` 持有活动图和仿真生命周期。
2. 它针对活动元件和导线构造、运行 `SimulationEngine`。
3. `SimulationEngine` 稳定动态器件、建立连通关系、更新负载/接触器，并推进受支持运行态。
4. `RuntimeStateManager.Shared` 按元件实例 ID 保存 KT、保护和自动往返等可变状态。
5. UI 读取最终元件/运行态，不能自行推进计时或运动。

## Analyzer、Engine 与 RuntimeState 的区别

- `CircuitStateAnalyzer`：读取活动图并推导说明/校验需要的状态快照，不推进时间、不渲染 UI。
- `SimulationEngine`：在一次仿真步进中改变受支持运行态。
- `RuntimeStateManager`：跨步保存运行态，并在图或生命周期边界重置。

因此，调用 Analyzer 不应隐式运行电路；运行 Engine 也不能使用活动工作区外的对象。

## 进入报告的动态状态

路径为：

`LocalInspectorPanel -> InspectionWorkflowService -> Analyzer/规则/格式化器 -> InspectionReportComposer -> 结构化 Block -> UGUI`

`LocalInspectorPanel` 中的窄适配器仍提供稳定的显示修正，包括：KT 阶段和延时、自动往返方向与位置、星/三角阶段、电机运行/方向、热继和接触器显示状态。不要随意调整调用顺序；Inspector 模型测试与 18 模板基线会保护 Block 顺序、Kind、Severity、RuleId 和可见报告结构。
