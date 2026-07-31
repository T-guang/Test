# 项目总交接：电工仿真完善阶段

## 新窗口快速接管摘要

项目位于 `E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`，Unity 固定使用 `2022.3.57f1c1`，当前开发分支为 `feature/electrical-final-polish`（由本文档提交后的基线创建）。SPICE 已在 `5f484b900fffcc6445960c76af71bd7dedd3e857` 冻结，并有 annotated tag `spice-v1-code-freeze-20260731`。冻结范围包含 DC 工作点、单频 AC、理想线性运放、图纸 schemaVersion 2、工作区网格/自由拖动/工具栏及交流源参数弹窗。它不是发布 Tag，也不代表已由这个提交构建最终 Demo Player；正式 Demo-only Windows Player 必须等电工仿真完善结束后再统一构建并进行三分辨率人工验收。

SPICE 图纸 V2 的 Reader 支持 V1 与 V2，正式 Writer 只输出 V2。V2 保存分析模式和频率、交流源幅值/相位、理想运放、位置旋转、普通 Wire、折点及既有器件参数；不保存结果、网表、ngspice 输出、revision、选择、pending Wire、视图缩放平移、文件路径或按钮状态。模式切换、保存和导入都保留不兼容器件：DC 模式运行前只拦截交流电压源，单频 AC 运行前只拦截直流电压源、直流电流源和硅二极管。该预检与元件池共用同一支持矩阵，在 GraphBuilder、SimulationService 和 ngspice 前返回，只给出状态栏和仿真助手中文诊断，不改图纸、dirty、revision、结果或网表。

交流源的幅值可在右侧紧凑参数区编辑；双击画布上的交流源会打开“编辑交流电压源参数”弹窗，在同一次原子提交中校验并写入幅值、相位、分析频率。用户已手动确认最新 Player 的网格、自由拖动、工具栏、模式切换和该弹窗没有重大问题。自动证据为 Editor 全量日志 `E:\Builds\ElectricalSimulation2D\SpiceModeCompatibility\20260731-03\unity-mode-compatibility-final.log` 与 Windows T3 Harness `E:\Builds\ElectricalSimulation2D\SpiceT31-EmbeddedHost\run_20260731_145301`；后者也实际确认模式不兼容预检不启动求解。

下一阶段的唯一工作对象是既有“电工仿真”系统，不应以 SPICE 重构为入口。先做只读深度审计并固定 18 张真实模板、规则/Inspector 快照、InternalTest 资料和当前用户/同事复现问题；之后按 P0/P1/P2/P3 分批、最小范围修复。任何触及 SPICE 冻结边界、Demo 场景、保存格式、控制模式或模板/规则基线的改动，都必须在编码前说明影响面、列出回归证据并经 ChatGPT 审核。Codex 负责可复现的实现、测试、提交与审查包；ChatGPT 负责审阅差异、验收风险和下一批授权。新窗口应先阅读本文、`CODEX_HANDOFF.md` 的 SPICE 历史和 `Docs/TestManual`，再复现同事问题；不要以旧聊天摘要替代 Git、日志和真实资产的当前检查。

## 1. 项目与 Git 状态

- 项目目录：`E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`
- Unity：`2022.3.57f1c1`
- 冻结提交：`5f484b900fffcc6445960c76af71bd7dedd3e857`
- SPICE 冻结 Tag：`spice-v1-code-freeze-20260731`（annotated，指向上述提交）
- 当前后续开发分支：`feature/electrical-final-polish`
- 冻结时仅保留的未跟踪审查材料：`CONTROL_WIRE_MULTIBEND_ARCHITECTURE_REVIEW.md`、`MOTOR_DIRECTION_RUNTIME_AUDIT.md`、`SourceReviewPackage/`。它们不属于产品源代码，不得随功能提交、删除或移动。

## 2. SPICE 冻结状态

### 功能范围

- DC：DC 电压源、DC 电流源、R/C/L、二极管、理想开关、Ground、电压/电流探针。
- 单频 AC：交流电压源、R/C/L、理想开关、Ground、探针；结果为结构化幅值/相位，频率为单点 `ac lin 1 f f`。
- 理想线性运放：`IdealOperationalAmplifier`，端子稳定顺序为 `nonInverting`（IN+）、`inverting`（IN-）、`output`（OUT），以固定增益 `1e6` VCVS 表达；支持 DC/AC 基础负反馈，不支持供电引脚、饱和、限流、带宽与真实型号。
- 图纸 V2：`LegacySchemaVersion = 1`、`CurrentSchemaVersion = 2`；V1 可读，V2 为唯一正式写出格式。运放只允许同器件 `inverting ↔ output` 反馈接线，规则权威为 `SpiceConnectionRules.IsConnectionAllowed`。

### 模式兼容性

| 当前模式 | 运行前阻断 | 保留行为 |
| --- | --- | --- |
| DC 工作点 | 交流电压源 | 不删除、不转换、不拒绝保存/导入；提示“交流电压源 + InstanceId” |
| 单频 AC | 直流电压源、直流电流源、硅二极管 | 不删除、不转换、不拒绝保存/导入；按确定性顺序提示名称和 InstanceId |

预检在图构建和求解前结束，故不启动 ngspice；它不是电路合法性判断。兼容器件组合的拓扑、浮空、短接和数值问题仍由正式 GraphBuilder/SimulationService 诊断。

### 交流源编辑与 Player UI

- 右侧参数区只保留所选交流源的紧凑幅值输入，避免占用仿真助手。
- 双击交流源弹窗原子编辑幅值、相位和分析频率；非法任一字段不产生部分更新。
- `SynchronizeWorkspaceGeometry()` 在 SPICE Root 激活与布局稳定后以真实 Viewport 同步 Content、Grid、Wire、Component 与 Overlay 图层；Content 为 Viewport 的三倍，不写 Model、不推进 revision。
- 工具栏使用单一“分析：DC / 分析：单频 AC”切换按钮；不再创建全宽 AnalysisControls 横条。网格、拖动和工具栏的生产修复已过 Editor 与 T3 Standalone Harness。

### 冻结证据与已知限制

- Editor 全量回归：T1 PASS、T2 `Fixtures=22`、T3 PASS、AC 真实 fixtures `14/14`、OpAmp `4/4`、AC-C1、AC-D、Quality Q1/Q2/Q2.1 全部通过。
- Windows Player Harness：通过 DC 求解、参数修改、Stale/旧请求丢弃、导入限制、清空状态、异常脱敏、网格、横纵拖动、缩放后拖动、模式切换、交流源弹窗和模式不兼容诊断。
- 用户手动测试：最新 Player 的网格、自由拖动、工具栏、模式切换、交流源参数弹窗无重大问题。
- 限制：尚未从 `5f484b9` 单独构建最终正式 Demo-only Player；1920×1080 和 3840×2160 的完整人工视觉验收仍待整体项目最终 Build 后执行。

## 3. 电工仿真系统当前状态

### 用户功能与运行链

电工仿真与 SPICE 是独立工作区。现有系统提供模拟电路画布、图纸集、仿真广场、元器件百科、常用工具和系统信息入口；可从元件库放置元件、接线、编辑、保存/导入、加载系统模板、开始/停止仿真，并由检查助手展示结构化检查、教学解释和运行状态。核心链包括 `WorkspaceController`、`CircuitComponent`、`WireManager`、`CircuitStateAnalyzer`、`SimulationEngine`、`CircuitValidationService`、Inspector 报告组装与模板/练习服务。

工业控制范围包括三相电源、断路器、熔断器、接触器、启动/停止与复合按钮、热继电器、时间继电器、行程开关、三相电机和星三角电机；家庭范围包括照明、双控、空气开关、电能表与风扇等。元件定义、端子和可视化由 `ComponentDefinition`、运行时视觉目录及终端锚点链共同约束。控制逻辑覆盖点动、自锁、互锁、正反转、自动往返、热继保护、顺序启动和星三角启动；不能把视觉导线折点误当作电气拓扑。

### 18 张系统模板

家庭电路：单开单控照明、双控照明、空气开关控制照明、单相电能表照明、灯泡/风扇并联、单开控制双灯、双开分别控制双灯、空开控制灯泡与风扇并联。

工业电路：电动机点动、连续运行、点动/连续混合、热继保护、正反转、电气互锁正反转、按钮与接触器双重联锁、自动往返、两电机时间继电器顺序启动、星三角降压启动。模板目录权威为 `Assets/Resources/Blueprints/Templates/template_catalog.json`，其数量必须保持 18。

### 规则、保存导入与检查助手

- `CircuitValidationService` 与规则层负责安全、拓扑与教学规则；检查报告的 Section/Block 顺序、RuleId、Severity 和教学文本受 Inspector 快照保护。
- 模板练习由 `PracticeSessionController`、`PracticeConnectionChecker` 与 `PracticeFeedbackFormatter` 处理；模板加载必须先成功读取和验证，再清空现有画布并生成，避免读失败破坏用户图纸。
- 模板 DTO、生成服务、保存/导入和手动 Wire 折线路由是独立职责。导线端点影响连通性；折点是视觉/持久化路径数据。
- 检查助手的报告模型、模板结构与运行态证据已有基线，新增规则或改文案都要同时审查数据、规则、报告和模板快照，而不只看单个 Console PASS。

## 4. InternalTest 与重要历史基线

- `Assets/EditorTests/Baselines/V2.3.9.1/`：`TemplateStaticSnapshots.json`、`InspectorReportSnapshots.json`、`ValidationRuleSnapshots.json`、`DemoSceneRuntimeEvidence.json`。这是 18 张真实模板、规则与 Inspector 报告的回归基线，非运行时数据源。
- `Assets/Scripts/Editor/ArchitectureBaselineSnapshotWriter.cs`：可采集/比较真实模板结构、规则和报告；不要以局部 fixture 替代真实模板路径。
- `Docs/TestManual/`：包含元件端子字典、18 张模板运行时用例和 InternalTest1 覆盖边界；先更新资料或重新生成前，必须确认是否会改变已冻结基线。
- 关键 Editor 测试包括 Template Integrity、拓扑安全、功率/保护、热继/时间继电器、正反转相序、检查报告与手动折线路由测试。它们是定点契约，不能替代完整模板回归。

## 5. 已知限制、问题接入与优先级

### 已知限制与未完事项

1. 正式 Demo-only Player 及 3840×2160、1920×1080、1366×768 人工视觉验收延期至全项目收口。
2. SPICE 冻结后不再添加器件、扫频、瞬态或波形功能；如必须改动冻结边界，需单独设计、审查与完整 SPICE 回归。
3. 电工仿真应先完成系统性审计；当前不能把历史审查材料或单个同事截图直接当作根因结论。
4. Editor 生命周期、模板规则、报告文案、保存兼容性与 Wire 路由是高回归风险区域。

### 同事测试问题的接入方式

每个问题创建一份最小复现记录：项目 HEAD、场景/页面、模板或手工图纸、操作序列、预期/实际、截图/日志、是否可稳定复现、涉及的保存文件。先以当前冻结基线复现，再标注受影响的元件、模板、规则、报告或持久化边界；没有该记录不得直接大范围重构。将可复现问题附上 P 级、预计测试和回归集合，交由 ChatGPT 审核后进入 Codex 实施批次。

### P0/P1/P2/P3 标准

- **P0**：崩溃、数据丢失/覆盖、错误电气危险结论、无法进入核心页面、模板/保存不可恢复；立即止损并最小修复。
- **P1**：18 模板或核心工业控制流程无法正确运行、规则严重误判、关键保存导入破坏、主交互稳定阻塞；在下一批优先修复。
- **P2**：明确可复现但有绕行的交互/文案/视觉问题，或局部规则覆盖缺口；在 P0/P1 清零后集中处理。
- **P3**：优化、非关键重构、扩展功能和无法稳定复现的问题；只记录，功能冻结后再评估。

## 6. 电工仿真深度审计与集中完善计划

1. **只读审计与证据建档**：盘点 Core、UI、保存导入、模板、规则、运行态、报告与测试；记录完整调用方向、权威状态与现有基线。
2. **模板和规则复核**：逐一核对 18 模板的组件、端子、Wire 端点、运行状态、Inspector 报告与 RuleId；优先复现来自测试同事的 P0/P1。
3. **定点修复批次**：每批只处理同一责任边界，先补最小回归，再实现，再跑关联模板和全量回归；不得混入 SPICE 变更。
4. **保存/导入与运行态收口**：专门验证事务性、路径、dirty、模板加载失败保护、手动路由和恢复后的检查/仿真。
5. **整体验收**：电工问题收口后，构建一次正式 Demo-only Windows Player，并进行三分辨率人工验收；再决定最终项目冻结。

## 7. SPICE 冻结后的修改边界

默认禁止修改 `Assets/Scripts/Spice/**`、SPICE 测试、SPICE 图纸格式、ngspice、SPICE UI、`Assets/Scenes/Demo.unity` 的 SPICE 绑定，以及任何影响 V1/V2 兼容性的文件。允许的例外仅是 P0/P1 且有可复现证据的冻结缺陷；例外必须说明为什么电工问题无法隔离、列出最小文件集，并补齐 T1/T2/T3、AC `14/14`、OpAmp `4/4`、AC-D 与 Player Harness 回归。不得因为电工优化而顺带整理 SPICE。

## 8. Codex 与 ChatGPT 协作方式

Codex 负责读取真实 Git/资产/日志、实施明确授权的最小改动、运行验证、生成可审查 diff/日志包、前向提交和交接文档。ChatGPT 负责审查设计与 diff、质疑证据、定义验收边界、确认 P 级与下一批授权。两者均不得用聊天描述取代当前 Git 状态；没有完整验证不得标记 PASS。用户负责最终 Player 人工视觉/交互验收裁决。

## 9. Git、测试、注释与审查包规范

- 每批开始和提交前后均执行 branch、HEAD、status、`git diff --check`、日志与进程检查；保留来源明确的未跟踪审查材料。
- 禁止 `git reset --hard`、`git restore .`、`git clean`、`git stash` 与 `git add .`。逐个确认文件后暂存，采用可独立验证的前向提交。
- 项目自有关键设计注释与用户可见日志以中文为主；保留 API、错误码、JSON 字段、类/方法名和第三方原文。注释说明原因与不变量，不做无证据的全局清理。
- 审查包放在仓库外 `E:\Builds\ElectricalSimulation2D\`，包含 baseline、diff、原始日志、结果数据、MANIFEST 和 SHA-256；不得包含 Library、Temp、完整 Build、Player 二进制或旧审查材料，除非任务明确要求。

## 10. 下一窗口首批任务所需输入

开始“电工仿真深度审计与集中完善”前，应由用户/同事提供：

1. 当前 Player 或 Editor 的问题截图/录屏与原始日志；
2. 可复现步骤、涉及模板 ID 或保存图纸、预期与实际；
3. 问题是否阻断教学/运行、是否涉及数据丢失或安全判断；
4. 目标平台、分辨率、Windows 缩放和输入方式；
5. 允许修改的模块边界，以及需要在何时构建正式 Player。

新窗口启动命令说明：先执行 Git 与进程基线检查；阅读本文、`CODEX_HANDOFF.md` 的 SPICE 历史、`Docs/TestManual/` 与相关模板/基线；随后只读复现并输出 P 级审计计划。未获得针对性授权前，不修改电工仿真代码、不构建 Player，也不移动现有审查材料。
