# Codex Handoff: ElectricalSimulation2D SPICE

## 当前基线

- 项目：`E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`
- Unity：`2022.3.57f1c1`
- 分支：`feature/spice-t4a-dc-host-integration`
- Q2.1 代码基线：`1769e95 test(spice): correct q2 quality evidence`
- 文档 HEAD：本文件的最终前向提交；以 `git rev-parse HEAD` 为准。
- 正式场景仍为 `Assets/Scenes/Demo.unity`。Q2 未修改场景、ProjectSettings、控制模式、数值语义、图纸格式、接线/路由或 Player Harness。

## 当前功能边界

- 器件范围已冻结：DC/AC 电压源、DC 电流源、R/C/L、二极管、理想开关、电压/电流探针、理想运算放大器（线性）。
- 支持 DC 工作点和单频 AC；单频 AC 使用结构化幅值/相位结果，AC 真实 ngspice fixtures 为 `14/14`。
- 理想运放枚举为 `IdealOperationalAmplifier`，端子顺序固定为 `nonInverting`（IN+）、`inverting`（IN-）、`output`（OUT）；统一 VCVS 模型为 `EOP out 0 in_plus in_minus 1e6`。
- 同器件 Wire 的唯一例外是同一理想运放 `inverting ↔ output`。规则仅由 `SpiceConnectionRules.IsConnectionAllowed` 定义，Model、Controller 悬停校验和 GraphBuilder 共用。

## 图纸格式与事务边界

- `LegacySchemaVersion = 1`，`CurrentSchemaVersion = 2`。
- Reader 支持 V1/V2；正式 writer 只输出 V2。
- V2 保存分析模式、频率、交流源幅值/相位、运放、普通 Wire、运放反馈 Wire、折点、位置、旋转、开关和既有参数。
- 不保存求解结果、网表、ngspice 输出、revision、请求号、选择、pending Wire、缩放/平移、文件路径和按钮状态。
- 导入先构建并校验临时 `SpiceWorkspaceModel`，成功后一次性 `CommitImportedModel`；失败不得改变 Model、路径、dirty、结果、复制资格、选择或 View。
- 保存沿用 FileService 的原子写入语义；成功保存不推进 electrical revision。

## Workspace 状态与刷新所有权

- `SpiceWorkspaceModel` 与 `SpiceCircuitModel` 是唯一电气状态权威。View 和 DemoHost 只绑定、展示并转发用户事件。
- 有效电气变更由 Controller 的 `HandleModelChanged` 只推进一次 electrical revision，并使已有结果失效；等价值写入不推进 revision。
- Controller 集中承担 Running 防线、异步 request/revision/cancellation 提交保护，以及现有元件池、参数区、画布和结果状态刷新。
- `SpiceWorkspaceController` 已按同一 partial 类型整理：主文件只保留状态与生命周期；Simulation、Interaction、Persistence、Presentation 分别承担既有职责，不新增运行时对象或状态机。
- `SpiceUnityObjectLifetime` 是动态 SPICE View 的统一销毁入口：Editor 非运行态使用 `DestroyImmediate`，Play Mode/Player 使用 `Destroy`。不要机械替换 Dialog、测试自身或 Unity 生命周期回调中的显式清理。

## Q1、Q2 与 Q2.1 质量收口

- Q1：`645c109`（T3 partial 测试拆分）、`46ca7a6`（中文日志和注释）、`eb20f67`（Edit Mode 生命周期）。
- Q2：`6da3295`（DemoHost 支持类型移出）、`4944dc1`（Controller partial 整理）、`9641cb0`（动态组件符号生命周期）、`4d1391a`（中文注释与 Q2 质量证据）、`6492559`（剩余注释与参数弹窗生命周期）。
- Q2.1：`8d3490f`（修复 Controller partial 注释关联并完成项目自有自然语言注释中文化）、`1769e95`（去重边界、活动测试清单与统计证据）。
- Q2 保持完整类型名、访问修饰符、字段、UI GameObject 名称、`AddComponent<T>` 目标和 ViewBindings 序列化字段不变。初始 Scene/Prefab/Asset 引用扫描未发现被移动类型的序列化脚本引用。
- Q2 的定向去重仅包含左右锚点工具栏按钮的共同样式，以及 ResultState 改变时原本完全相同的三个控件刷新；未将网表、结果或其他 Refresh 收进万能入口。
- Q2.1 已将文件按钮、事务式导入、分析设置、运行中导入和统一电气编辑防线的说明重新关联到正式方法；`DiscardOutdatedCalculation` 保持原有的分析控件与参数区刷新边界。
- `RefreshResultStateDependentControls()` 仅有 3 个正式调用点（进入 Running、离开 Running、SetResultStateForTesting），且仅刷新文件操作、分析控件和参数区。
- `SpiceT3WorkspaceValidation` 的 9 个 partial 中，编译生效 Validate 声明为 109、RunPureChecks 直接调用为 109、重复方法名为 0、未调用活动 Validate 为 0；`#if false` 历史入口探针为 8、内部 Validate helper 为 2。
- 项目自有自然语言注释已中文化；保留的中英混合仅为类名、方法名、字段名、枚举/状态值、错误码、JSON 字段和 ngspice 原始技术标识。

## 自动回归

最终 Q2.1 原始 batch 日志：

`E:\Builds\ElectricalSimulation2D\SpiceFinalQualityQ21\20260731-183254\unity-q21-final.log`

- C#：0 error；T1：PASS；T2：`Fixtures=22`；T3：PASS。
- AC：`14/14`；AC-C1、OpAmp `4/4`、AC-D 全部专项：PASS。
- Quality-Q1、Quality-Q2 与 Quality-Q2.1 全部专项：PASS。
- `Destroy may not be called from edit mode`、`MissingReferenceException`、`Missing Script`、`StackOverflowException`、`UnobservedTaskException`、未处理异常均为 0。

## 仍延期的工作

下一步唯一为：Windows Player Build，以及 3840×2160 / 1920×1080 / 1366×768 三分辨率人工验收。不得自动构建 Player 或开始人工验收。

## 历史里程碑

- 单频 AC：`c677c97`、`fa47f5c`、`515c808`、`90c3602`、`8b936b2`、`b185fa4`、`3405933`、`fd7ae48`。
- AC-C1：`bd598e7`、`450119b`；AC-C1.1/1.2：`c5c3ddc`、`e60316c`、`49ceaef`、`fde7102`。
- 理想运放 V1/V1.1：`e04089b`、`7a10aff`、`97fc9d1`、`5e00991`、`c32a946`。
- AC-D：`02884a3`、`f1d955d`、`76b6e5b`、`477fadf`、`e268663`。
