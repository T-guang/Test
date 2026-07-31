# Codex Handoff: ElectricalSimulation2D SPICE

## 当前基线

- 项目：`E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`
- Unity：`2022.3.57f1c1`
- 分支：`feature/spice-t4a-dc-host-integration`
- Player UI、参数编辑与分析模式兼容诊断代码基线：`b6f8e2c fix(spice): clarify analysis mode incompatibilities`
- 文档 HEAD：本文件对应的最终前向提交；以 `git rev-parse HEAD` 为准。
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
- Q2.1.1 已清除 partial 拆分残留的文件按钮错位注释，并由静态证据确认 `SelectComponent` 前不再出现“文件按钮回调”；`SimulationModeOptionVisual` 的摘要为中文。项目自有英文自然语言设计注释已完成逐项复核；技术标识、API 名称、错误码和第三方原文继续保留。
- `RefreshResultStateDependentControls()` 仅有 3 个正式调用点（进入 Running、离开 Running、SetResultStateForTesting），且仅刷新文件操作、分析控件和参数区。
- `SpiceT3WorkspaceValidation` 的 9 个 partial 中，编译生效 Validate 声明为 110、RunPureChecks 直接调用为 110、重复方法名为 0、未调用活动 Validate 为 0；`#if false` 历史入口探针为 8、内部 Validate helper 为 2。
- 项目自有自然语言注释已中文化；保留的中英混合仅为类名、方法名、字段名、枚举/状态值、错误码、JSON 字段和 ngspice 原始技术标识。

## 自动回归

最终 Q2.1.1 原始 batch 日志：

`E:\Builds\ElectricalSimulation2D\SpiceQ211Player\20260731-185111\unity-q2111-editor-final.log`

- C#：0 error；T1：PASS；T2：`Fixtures=22`；T3：PASS。
- AC：`14/14`；AC-C1、OpAmp `4/4`、AC-D 全部专项：PASS。
- Quality-Q1、Quality-Q2 与 Quality-Q2.1 全部专项：PASS。
- `Destroy may not be called from edit mode`、`MissingReferenceException`、`Missing Script`、`StackOverflowException`、`UnobservedTaskException`、未处理异常均为 0。

## Player UI 与工作区几何修复

- `60166be fix(spice): initialize workspace geometry after activation`：`BuildUi()` 只建立 Content、Grid/Wire/Component/Overlay 图层和事件绑定，不再把未激活 Root 下的 Viewport 尺寸固化为 Content 尺寸。
- `SpiceWorkspaceController` 在 Root 激活后和下一帧布局稳定后调用同一个 `SynchronizeWorkspaceGeometry()`；窗口/Canvas 尺寸实际改变时才再次同步。只有有效 Viewport（宽高均不少于 1）才提交 Content 尺寸。
- 同步契约：`Content = Viewport × 3`；Grid、WireLayer、ComponentLayer、OverlayLayer Stretch 到 Content；Grid 标记重绘；ViewController 仅重新夹紧平移。同步不改 Model、不推进 electrical revision、不使结果 Stale，也不改元件逻辑坐标。
- 几何未就绪时，新增元件、元件池落点和元件拖动均不会对零尺寸 Content 应用 Clamp；激活后恢复正式交互。
- 不再创建全宽 `AnalysisControls` 或调用 `ReserveTopSpace`。分析模式改为工具栏“重置视图”后的单一切换按钮：`分析：DC` / `分析：单频 AC`，仍经 `TrySetAnalysisMode` 进入正式 revision、Stale 和元件池兼容矩阵路径。
- `0eacd3d fix(spice): streamline ac source parameter editing`：右侧参数卡恢复紧凑，仅保留所选交流源的 AC 小信号幅值；不再创建 `AcAnalysisSettings`、右侧相位输入或右侧频率输入。双击画布中的交流电压源会打开加大的参数弹窗，统一编辑“AC 小信号幅值”“相位”和“分析频率”，并在全部文本验证通过后经 Controller 正式入口提交。弹窗标题明确为“编辑交流电压源参数”，避免被错误标注为电感。
- `7115800 test(spice): cover standalone grid and component dragging`：T3 Windows Player Harness 实际验证激活后几何、网格、工具栏、模式切换和交流源参数弹窗，并通过 `SpiceWorkspaceComponentView.OnBeginDrag/OnDrag/OnEndDrag` 覆盖横向、纵向和缩放后拖动。它不进入正式 Demo Player。
- `b6f8e2c fix(spice): clarify analysis mode incompatibilities`：元件池与运行前预检共用 `IsComponentKindSupportedInCurrentAnalysis`。DC 仅阻断交流电压源；单频 AC 仅阻断直流电压源、直流电流源和硅二极管。预检在 `GraphBuilder`、`SimulationService` 与 ngspice 之前返回，状态栏和仿真助手按确定性顺序列出中文器件名称与 `InstanceId`；不会删除、转换、保存过滤或导入拒绝这些图纸状态，也不会改变 revision、dirty、结果或网表复制状态。
- 最新 Editor 全量日志：`E:\Builds\ElectricalSimulation2D\SpiceModeCompatibility\20260731-03\unity-mode-compatibility-final.log`；最新 T3 Standalone Harness：`E:\Builds\ElectricalSimulation2D\SpiceT31-EmbeddedHost\run_20260731_145301`。该 Harness 额外确认模式不兼容诊断不进入求解服务，且保留画布、结果状态、dirty 与 revision。

## 仍延期的工作

Windows Player 自动验证已经通过：T1 Player 验证日志位于 `E:\Builds\ElectricalSimulation2D\SpicePlayerUiFix\20260731-205059\T1Harness\unity-t1-player.log`，最新 T3 Player 几何、拖动、交流源参数弹窗与模式不兼容诊断验证见上节。本次 `b6f8e2c` 后尚未构建新的正式 Demo-only Player，因此旧正式包仅可作为历史材料，不能替代本次手动验收。1366×768 客户区已按实际窗口尺寸采集主界面与 Player.log；当前自动化运行桌面的物理显示上限为 1536×864，无法在同一桌面获得完整 1920×1080 或 3840×2160 客户区和可见 SPICE 页截图，因此这些材料仍须在具备对应显示空间的人工验收环境补齐。自动检查不能替代视觉验收。下一步唯一为：基于 `b6f8e2c` 的 Windows Player Build 与三分辨率人工验收；不得自动创建冻结 Tag。

## 历史里程碑

- 单频 AC：`c677c97`、`fa47f5c`、`515c808`、`90c3602`、`8b936b2`、`b185fa4`、`3405933`、`fd7ae48`。
- AC-C1：`bd598e7`、`450119b`；AC-C1.1/1.2：`c5c3ddc`、`e60316c`、`49ceaef`、`fde7102`。
- 理想运放 V1/V1.1：`e04089b`、`7a10aff`、`97fc9d1`、`5e00991`、`c32a946`。
- AC-D：`02884a3`、`f1d955d`、`76b6e5b`、`477fadf`、`e268663`。
