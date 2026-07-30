# Codex Handoff: ElectricalSimulation2D SPICE

## 当前基线（唯一）

- 项目：`E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`；Unity：`2022.3.57f1c1`；分支：`feature/spice-t4a-dc-host-integration`。
- Code baseline：`477fadf test(spice): bind drawing v2 evidence to real checks`。这是 AC-D 后的最新代码提交；七项 AC-D 日志均在对应独立校验无异常返回后才输出。
- Docs HEAD：本文件的最终前向提交；以 `git rev-parse HEAD` 为准。后续历史章节仅供追溯，不构成当前基线。
- 理想运算放大器 V1：`e04089b`（Core/求解）和 `7a10aff`（工作区）；V1.1 反馈拓扑：`5e00991`。
- 正式场景仍为 `Assets/Scenes/Demo.unity`；不得修改场景、ProjectSettings、控制模式、Wire 交互或 Player Harness。

## SPICE Workspace 状态与刷新所有权

- `SpiceWorkspaceModel` 与 `SpiceCircuitModel` 是唯一电气状态权威来源。新增、删除、接线和参数修改只经正式 Model API；View 与 DemoHost 只绑定、展示并转发事件。
- 每次有效 Model 变更由 Controller 的单一 `HandleModelChanged` 推进一次 electrical revision，并按既有规则将已有结果标为 Stale；等价值写入不产生变更。
- Controller 集中负责 Running 防线、异步 revision/request 提交保护，以及元件池、参数区、画布与结果状态刷新。运放复用这条链路，不存在 `RefreshOpAmp`、`InvalidateOpAmp` 或 `MarkOpAmp` 分支。
- View 和 DemoHost 不推进 revision、不修改结果状态，也不直接运行求解器。

## 理想运算放大器（线性）V1

- 枚举：`IdealOperationalAmplifier`；稳定端子顺序为 `nonInverting`（IN+）、`inverting`（IN-）、`output`（OUT）。
- 固定模型是 `EOP out 0 in_plus in_minus 1e6`。有限 1e6 增益保留可验证的反馈误差并避免无穷增益数值约束；DC 与单频 AC 复用同一 VCVS，没有隐藏电源。
- V1 输出只定义为 OUT 相对 GND；不提供电源引脚、饱和、限流、增益带宽、压摆率、失调、真实型号、瞬态或扫频。
- 项目 ngspice 45.2 的最小验证和 4 个真实 fixture 均可读取 `i(EOP...)`，因此结果显示 VCVS 输出支路电流并标注 ngspice 支路约定；不会伪造输入端电流。
- V1 兼容规则只属于 Reader 与 V1 专用 Helper；正式保存已由 AC-D 改为 V2，不再拒绝交流源、非默认频率、理想运放或其 IN-↔OUT 反馈线。
- 三分辨率真实验收仍延期到最终代码收口后的正式 Player 人工验收。

### V1.1 反馈拓扑边界

- `SpiceConnectionRules.IsConnectionAllowed` 是同器件接线的唯一纯规则来源，供 `SpiceWorkspaceModel.AddWire`、Controller 悬停目标判定和 `SpiceCircuitGraphBuilder` 共用。仅允许同一 `IdealOperationalAmplifier` 的 `inverting ↔ output`；其余同器件端子组合仍拒绝。
- GraphBuilder 不再把 IN+、IN-、OUT 视为拓扑星形。VCVS 只让 OUT 在连通性检查中通向 GND；IN+、IN- 保持高阻控制端。用户实际画出的 IN-↔OUT 反馈 Wire 仍由 Union-Find 合并，因此跟随器和反相负反馈均有效，而独立浮空输入源会在调用 ngspice 前稳定报告 `SPICE_FLOATING_SUBCIRCUIT`。
- OUT 被直接并到节点 0 时，GraphBuilder 在生成 EOP 网表前报告 `SPICE_OPAMP_OUTPUT_SHORTED`。UI 中文提示说明 OUT 不能直接接 GND，因为 V1 EOP 输出本身就是相对于 GND 的理想受控电压源。
- 正式 Workspace 覆盖 IN-→OUT 与 OUT→IN-、WireView 创建、单次 revision/Stale、旋转和删除后的 Wire 完整性，并通过 `SpiceSimulationService` 求解实际跟随器。V1 文件保存覆盖不存在目标、已有哨兵文件、临时/备份文件和当前路径均不被拒绝操作改变；普通 DC V1 文件仍可保存。

## SPICE 图纸 schemaVersion 2

- `LegacySchemaVersion = 1`，`CurrentSchemaVersion = 2`。Reader 保留 V1/V2 分发：V1 导入固定恢复为 DC 与默认频率；正式 writer 只输出 V2，避免丢失 AC 配置、交流源相位与运放反馈拓扑。
- V2 显式保存 `analysis.mode`、`analysis.frequencyHz`、既有元件/位置/旋转/参数/Wire/折点，以及交流源 `phaseDegrees`。理想运放只依赖 Kind、身份、位置、旋转与 Wire，不写无意义参数。
- 保存不包含结果、网表、ngspice 输出、revision、请求号、选择、pending Wire、缩放、平移、文件路径或按钮状态；成功保存只清除 dirty，不推进 electrical revision。
- 导入始终先构建并验证临时 `SpiceWorkspaceModel`，组件全部完成后才验证 Wire。V2 同器件反馈必须调用 `SpiceConnectionRules.IsConnectionAllowed`；失败不替换正式 Model、路径、结果或视图。
- 下一步唯一为 SPICE 最终代码、中文注释与项目日志收口；Windows Player Build 与三分辨率人工验收在收口后执行。

## AC-D 证据与回归

- `ValidateAcDV2Serialization` 验证 V2 确定性 JSON、字节一致性、SHA-256 和普通 DC 图纸恢复。
- `ValidateAcDV1Compatibility` 覆盖空图纸、Ground、开关和电阻旧图纸读取，以及默认 DC/频率和重新保存升级。
- `ValidateAcDAcDrawingRoundTrip` 用真实 ngspice 验证 1234.5 Hz、2.5 V ∠ -170° RC 图纸恢复。
- `ValidateAcDOpAmpFeedbackRoundTrip` 用真实 ngspice 验证 DC 跟随器和 AC 反相器，覆盖运放反馈 Wire、交流相位及有限开环增益结果。
- `ValidateAcDImportTransaction` 比较失败导入前后的 Model 引用及序列化快照、revision、路径、dirty、ResultState、可复制结果/网表、选择、pending Wire 和组件/Wire View 数量。
- `ValidateAcDAtomicFileWrite` 覆盖新目标创建、已有目标原子替换、故意失败时哨兵 SHA-256、临时/备份清理、路径/dirty 保持，以及成功保存不推进 revision、不使 Current 结果变 Stale。
- `ValidateAcDD2LimitRegression` 以 V2 DTO 覆盖器件/Wire 数量、单 Wire/全图折点、InstanceId/TerminalId 长度、坐标、参数、frequency、phase 与 1 MB 文件边界。
- 最近完整 batch 原始日志：`E:\Builds\ElectricalSimulation2D\SpiceFinalQuality\20260730-142807\unity-ac-d-evidence-final.log`。包含 C# 0 error、T1、T2 Fixtures=22、T3、14/14 AC fixtures、全部 AC-C1、运放 4/4 与全部 AC-D 专项通过。

<!-- 历史归档：以下内容仅保留追溯，不构成当前基线或后续指令。

## Current AC baseline

- Historical AC-C1.1 code baseline: `c5c3ddc fix(spice): close ac workspace presentation gaps`; current baseline is the authoritative section above.
- Historical docs entry; current Docs HEAD is declared in the authoritative section above.
- AC-B1: `c677c97`; AC-B1.1: `515c808`; AC-B2: `b185fa4`; AC-B2.1: `3405933`.
- AC-C1: `bd598e7` and `450119b`; AC-C1.1: `c5c3ddc`.
- Next: AC-D — 保存/导入 schemaVersion 2.
- The three-resolution check is only a layout-structure smoke test. Real 3840×2160, 1920×1080, and 1366×768 acceptance remains deferred to formal Player manual acceptance after AC-D.

## Current Code Baseline

- Branch: `feature/spice-t4a-dc-host-integration`
- Historical AC-B2.1 baseline: `3405933 test(spice): bind ac fixtures to explicit output nodes`.
- It is retained only to identify the AC-B2.1 milestone, not as the current code baseline.

## 当前基线

- 项目：`E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`
- Unity：`2022.3.57f1c1`
- 分支：`feature/spice-t4a-dc-host-integration`
- 当前功能/测试基线：`450119b feat(spice): present single-frequency ac results`
- 正式场景：`Assets/Scenes/Demo.unity`
- 本轮未修改 Demo、Build Settings、控制模式、Wire 接线交互或 Windows P/Invoke。

文档提交后的实际 HEAD 以 `git rev-parse HEAD` 为准。

## 关键提交

```text
3405933 test(spice): bind ac fixtures to explicit output nodes (AC-B2.1)
b185fa4 feat(spice): dispatch single-frequency ac simulation (AC-B2)
8b936b2 feat(spice): map single-frequency ac results (AC-B1.1)
90c3602 feat(spice): add single-frequency ac netlist parsing (AC-B1)
f77493b test(spice): extend stabilization player coverage
7618b25 fix(spice): sanitize unexpected simulation errors
7e2947c fix(spice): synchronize simulation presentation state
da15f1e fix(spice): enforce drawing import limits
6174faa fix(spice): discard stale calculation results
4413101 fix(spice): 同步理想开关视觉状态
45214fb fix(spice): 回退不稳定的文件对话框 COM 实现
71bcae9 feat(spice): 接入图纸保存导入工作流
c5a4781 feat(spice): 增加图纸文件操作核心
ecc1ed6 fix(spice): 补强事务导入运行态保护
bd35644 feat(spice): 增加事务式图纸导入核心
74dcd1d fix(spice): 补强图纸契约边界校验
```

## 功能与架构

SPICE DC 已完成 10 类器件、本地 ngspice 求解、现有点击端子接线与多折点、
删除、缩放和平移、结果与网表滚动/复制、编号重置，以及 `.spicejson`
保存、另存为和事务式导入。

SPICE 权威数据：

- `SpiceWorkspaceModel.Components`
- `SpiceWorkspaceModel.Wires`

控制模式与 SPICE 只共享页面外壳，不共享 Model、Wire、DTO、编号器、保存文件或求解器。
生产宿主是 `SpiceWorkspaceDemoHost`；`SpiceWorkspacePrototypeBootstrap` 只属于测试场景。

## 保存/导入边界

- format：`ElectricalSimulation2D.SpiceDrawing`
- schemaVersion：`1`
- 扩展名：`.spicejson`
- 默认目录：`Application.persistentDataPath/SavedSpiceDrawings`

保存器件身份、类型、位置、旋转、SI 参数、开关状态，以及 Wire 端点、路由模式和折点。
不保存结果、网表、诊断、选择、pending Wire、缩放、平移或当前文件路径。

导入链：

```text
WindowsFileDialog
→ SpiceWorkspaceDemoHost
→ TryImportWorkspaceFromPath
→ SpiceDrawingFileService
→ TryImportDrawingJson
→ SpiceDrawingSerializer
→ 临时 SpiceWorkspaceModel
→ CommitImportedModel
```

失败导入不替换当前 Model、不更新文件路径、不创建部分视图。

## D1/D2/D3

### D1

- electrical revision、request ID 和 Model 引用共同保护异步结果提交。
- 运行期间拒绝电气修改；移动、旋转、缩放和平移仍可用。
- 旧请求的 catch/finally 不能覆盖新请求状态。

### D2

集中限制位于 `SpiceDrawingLimits`：

```text
文件 1 MB；器件 500；Wire 1000
单 Wire 折点 128；全图折点 10000
InstanceId 128；TerminalId 64；通用字符串 256
坐标绝对值 20000；导入参数绝对值 1e15
```

数量和折点总量在创建临时 Model 前预检；越界坐标整体拒绝，不 Clamp。

### D3 / D3.1

- `generatedNetlistRevision` 记录网表对应的 electrical revision。
- 按钮与 handler 共用 `TryGetCopyableNetlistText`。
- 模型变化后旧网表可显示并标记过期，但不可复制。
- 清空和成功导入恢复 `NeverRun` 并清除旧结果、诊断和复制资格。
- 未预期异常只向用户显示 `SPICE_RUNTIME_UNEXPECTED`；完整异常仅进日志。

## Single-frequency AC B1 through B2.1

- `SpiceSimulationService` is the sole workspace simulation dispatcher. It routes
  `DcOperatingPoint` to `SpiceDcSimulationService` and `AcSingleFrequency` to
  `SpiceAcSimulationService`; unsupported modes return a diagnostic without invoking either service.
- `SpiceWorkspaceController` uses that dispatcher for both production paths. AC
  successes are presented as magnitude/phase phasors, while DC presentation stays on
  the existing scalar result path.
- AC-B1/B1.1/B2 add the netlist/parser contract, structured phasor mapping, and
  unified dispatcher. AC-B2.1 binds RC and dual-source Vout checks to explicit graph
  terminals rather than dictionary-value ordering.
- Regression coverage includes dispatcher call counts, controller DC and AC paths,
  D1 delayed-AC stale-result rejection, parser duplicate result-key rejection, and
  14 real-ngspice AC fixtures. Do not replace these with mocked-only coverage.

## 验证结果

Editor batchmode：

- T1 通过，10 V / 1 kOhm 电流 0.01 A
- T2 通过，`Fixtures=22`
- T3 通过

Windows x64 Harness：

- 使用现有 `Assets/Tests/SpiceT3/SpiceT3PlayerValidation.unity`
- 非 Development Build
- 三次运行退出码 `0 / 0 / 0`
- 每份 JSON 的 success、D1、D2、D3、网表修订和异常收口字段均为 true
- 无 Player/ngspice 残留
- 日志无 MissingReferenceException、CleanupEngine、StackOverflow 或未观察 Task 异常

仓库外构建：

```text
E:\Builds\ElectricalSimulation2D\SpiceStabilizationD32\20260728-111235\
```

正式 Demo Windows x64 Player 构建成功：

```text
E:\Builds\ElectricalSimulation2D\SpiceStabilizationD32\20260728-111235\DemoPlayer\ElectricalSimulation2D-SpiceD3.exe
```

仍需人工：

- 正式 Player 控制模式/SPICE 冒烟
- 原生保存/导入窗口各打开和取消 5 次
- 3840x2160、1920x1080、1366x768 快速布局检查
- 产品内退出、窗口关闭和三轮 Editor Play/退出

## 验证命令

Editor：

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.57f1c1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "E:\Projects\Unity\ElectricalSimulation2D_SpiceT2" `
  -logFile "<log-path>" `
  -executeMethod ElectricalSim.EditorTools.SpiceT3.SpiceT3PrototypeTools.RunAllFromCommandLine
```

Harness 构建入口：

```text
ElectricalSim.EditorTools.SpiceT3.SpiceT3PrototypeTools.BuildPlayerValidationFromCommandLine
--spice-t3-build=<outside-repo>\SpiceT3-StabilizationHarness.exe
```

Demo 构建入口：

```text
ElectricalSim.EditorTools.SpiceT3.SpiceT3PrototypeTools.BuildDemoPlayerFromCommandLine
--spice-demo-build=<outside-repo>\ElectricalSimulation2D.exe
```

两个入口都显式指定 Scene，不修改 Build Settings，也不创建或重写 Scene。

## 未跟踪审查材料

保留且不得提交、删除或移动：

```text
CONTROL_WIRE_MULTIBEND_ARCHITECTURE_REVIEW.md
MOTOR_DIRECTION_RUNTIME_AUDIT.md
SourceReviewPackage.zip
SourceReviewPackage/
```

准确状态应写为：已跟踪工作区干净；存在上述已确认保留的未跟踪材料。

## 已知限制

V1 保存 Wire 的电气端点和折点数据，但未保存原始第一段方向。端点规范化后，
导入重建的正交视觉路径可能与保存前不同；电气连接、网表和 DC 求解不受影响。
不要在稳定性批次中修改现有接线方式。视觉保真应作为独立 V2 契约任务。

## 禁止事项

- 不运行 `Tools/Electrical Demo/Build Demo Scene`
- 不运行 `DemoSceneBuilder`、`BindDemoScene` 或生产 UI 重建工具
- 不调用 `SpiceT3PrototypeTools.CreateScene` 重建正式场景
- 不修改 `Demo.unity` YAML
- 不把 PrototypeBootstrap 放入 Demo
- 不混用控制模式与 SPICE 数据/DTO
- 不自动开始瞬态、波形或新器件

## 下一步

1. AC-D：保存/导入 schemaVersion 2。

<!--
以下内容是 6105399 时期的归档 handoff，已经过时，仅保留历史上下文。
不要执行其中的旧基线、旧计划或 Scene Builder 指令。

# Codex Handoff: Unity Electrical Simulation SPICE Work

## 1. Project Goal And Current Stage

This repository contains a Unity electrical teaching simulation project. The SPICE workstream adds an isolated, local ngspice-backed DC circuit simulator without replacing the existing electrical-control simulator.

The current phase is **T4-A3-A: DC workspace central-canvas visual shell alignment**. The preceding mode entry, mode switching, state retention, host structure, popup menu, and T4-A2 region alignment work have been committed.

The current T4-A3-A implementation adds a control-style visual grid and matching background color below the SPICE wire/component/overlay layers. It does not change SPICE electrical behavior, coordinate conversion, host geometry, or the existing electrical-control workspace.

Do not start transient analysis, save/load, Undo/Redo, new components, DemoSceneBuilder, or branch merging unless a later explicit task authorizes them.

## 2. Project Directory And Unity Version

- Worktree: `E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`
- Unity: `2022.3.57f1c1`
- ngspice package used by the project: `E:\Projects\Unity\ngspice-45.2_64`
- T1 runtime files are kept under `Assets/StreamingAssets/ThirdParty/ngspice/`.

## 3. Current Git State

Verified at handoff creation:

- Branch: `feature/spice-t4a-dc-host-integration`
- HEAD: `610539937bf7c890b981c98b7355889dd1d4960c`
- HEAD subject: `fix(spice): add canvas renderer to DC grid`
- Working tree: clean (`git status --short` had no output).

Recent commits, newest first:

1. `6105399 fix(spice): add canvas renderer to DC grid`
2. `9ec298a fix(spice): align DC workspace presentation`
3. `9ad1e56 fix(spice): align DC host regions with control layout`
4. `570f8b4 refactor(spice): host DC mode in simulation shell`
5. `422f7f1 fix(ui): constrain simulation mode dropdown`
6. `7f25b35 fix(ui): render simulation mode menu above pages`
7. `2e69dc1 fix(spice): prevent host overlay input blocking`
8. `68c026d fix(spice): embed mode switching in simulation page`
9. `6d9f31a feat(spice): bind DC workspace to Demo simulation page`
10. `e319111 feat(spice): add DC simulation host controllers`

Existing worktrees include the main `develop/v2.4-stabilization` worktree, T1, and this T2/T4 worktree. Do not delete existing worktrees.

## 4. Completed Work And Verification Results

### T1: Local ngspice process bridge

- Fixed DC netlist execution through `ngspice_con.exe` was implemented and committed at `db7f72d` on `feature/spice-t1-ngspice-bridge`.
- T1 establishes the local process runner, output capture, fixed-netlist parsing, and StreamingAssets deployment path.

### T2 and T2.1: Structured DC core and topology invariants

- Pure-data `SpiceCircuitModel`, structured components/parameters/wires, graph construction, deterministic node names and SPICE names, DC netlist generation, result mapping, and diagnostics were implemented.
- DC support covers voltage sources, resistors, capacitors, inductors, and ground.
- SI units are the internal representation.
- Ground reachability is validated through a graph of electrical nodes connected by component edges; disconnected closed subnetworks are rejected before ngspice runs.
- Deterministic node naming uses the canonical, ordinal-sorted terminal identity rather than Union-Find root identity.
- T2.1 source commit recorded in earlier work: `ab5a63f89568a4e875afc01574b399831de35285`.

### T3 and T3.1-A: Standalone DC prototype and interaction

- The prototype provides drag/drop creation, movement, rotation, parameter editing, automatic orthogonal wiring, manual orthogonal waypoints, right-click waypoint undo, Esc cancel, diagnostics, generated-netlist display/copy, and DC results.
- SPICE component state is independent from Unity GameObjects and from the existing electrical-control workspace.
- Parameter/topology edits mark results and netlist stale; movement and rotation do not.
- Same-component terminal connections are rejected.
- The prototype uses `SpiceWorkspacePrototypeBootstrap` only for T3 test scenes. It must never be used by `Demo.unity`.

### T3.1-B: Embeddable host structure

- `SpiceWorkspaceController` accepts explicit `SpiceWorkspaceViewBindings`; it no longer owns global Canvas, EventSystem, Camera, or full application-page creation.
- T3 prototype infrastructure is serialized by editor tooling and kept separate from the production Demo host.
- `SpiceWorkspacePrototypeBootstrap` remains limited to the prototype and player-validation scenes.
- Player harness lifecycle was simplified to access `SpiceWorkspacePrototypeBootstrap.Controller` in `Start`, without a `Task.Yield` frame delay.

### T4-A1 / T4-A2: Demo host integration and mode switching

- Production host: `SpiceWorkspaceDemoHost`.
- Mode controller: `SimulationModeController` with `ControlCircuit` and `SpiceDc` modes.
- SPICE is initialized once and preserved in memory while hidden. Mode changes use `SetActive`; they do not change scene, rebuild the workspace, or share business state.
- The Simulation mode menu is a global popup layer and was manually accepted: it closes on outside click/navigation, switches either mode, and remains in bounds at 3840x2160, 1920x1080, and 1366x768.
- Control and SPICE roots are independent. Existing `WorkspaceController`, `WireManager`, control components, control wires, templates, validation, and save data are not reused by SPICE.
- `LeftPaletteCollapseHandle` is hidden in SPICE mode and restored in control mode.

### T4-A3-A: Central workspace visual shell

Committed in two forward-only commits:

- `9ec298a fix(spice): align DC workspace presentation`
  - Adds `SpiceGridVisual` as the first child of `SpiceWorkspaceViewport`.
  - Uses the existing pure-visual `WorkspaceGrid` component.
  - Copies the existing control Workspace background `Image.color` to the SPICE viewport.
  - Keeps the viewport Image raycastable as the SPICE input surface; the grid itself has `raycastTarget = false`.
- `6105399 fix(spice): add canvas renderer to DC grid`
  - Adds the required `CanvasRenderer` to `SpiceGridVisual` in both editor creation and the serialized scene.
  - Prevents the `MissingComponentException` from `RectMask2D` clipping a `MaskableGraphic`.

Verification already completed during the prior session:

- `git diff --check` passed before both commits.
- T3 editor workspace mapping/invalidation validation passed after the visual-shell change.
- The project compiled successfully in Unity batch mode as part of that validation.
- No Unity Editor, `ngspice_con.exe`, or `taskkill.exe` process remained after those batch runs.

## 5. Current Problem Being Handled

There is no uncommitted source or scene change at handoff time.

The last reported runtime error was:

`MissingComponentException: There is no 'CanvasRenderer' attached to the 'SpiceGridVisual' game object.`

It was caused by creating a UGUI `MaskableGraphic` (`WorkspaceGrid`) without its required `CanvasRenderer`. Commit `6105399` contains the forward fix. **Manual Unity Play Mode confirmation after this commit is still required**: open `Demo.unity`, enter Play, switch to Basic Circuit Principles / SPICE mode, and confirm the exception no longer appears.

## 6. Remaining Work

### Immediate manual acceptance for T4-A3-A

1. Open `Assets/Scenes/Demo.unity` and enter Play Mode.
2. Switch to the SPICE DC mode from the Simulate Circuit mode menu.
3. Confirm no `MissingComponentException`, especially no CanvasRenderer or RectMask2D error.
4. Confirm the SPICE central viewport has the light background and grid below wires/components.
5. Confirm the grid does not block drag/drop, blank-click cancellation, terminal click, automatic wiring, or manual waypoint wiring.
6. Check at 3840x2160, 1920x1080, and 1366x768 if available.
7. Confirm control mode remains visually and behaviorally unchanged.

### Later tasks that are explicitly not started

- T4-A3-B: formal toolbar and component-palette visual alignment.
- T4-A3-C: formal SPICE assistant visual alignment.
- Any AC, transient, waveform, save/load, template, Undo/Redo, new component, or merge work.

## 7. Confirmed Technical Decisions And Prohibitions

### Architecture boundaries

- SPICE uses its own `SpiceWorkspaceController.Components` and `.Wires` as the only SPICE workspace facts.
- SPICE does not use `WorkspaceController.Components`, `WireManager.Wires`, `RuntimeStateManager`, `DrawingDto`, existing RuleId values, `LocalInspectorPanel`, or existing control-simulation save data.
- `SpiceWorkspacePrototypeBootstrap` belongs only to T3 prototype/player-validation scenes. It must not be placed in `Demo.unity` or extended with a production mode.
- The production `SpiceWorkspaceDemoHost` receives explicit scene references and initializes the controller once.
- Do not use `FindObjectOfType`, `GameObject.Find`, scene scans, a global singleton, or automatic creation of missing production roots.
- Do not create a second EventSystem or camera in Demo. The only added popup Canvas is `GlobalPopupLayer`, with `overrideSorting = true` and sorting order 100.

### UI and coordinate rules

- Mode selection belongs under the existing top "模拟电路" navigation item; do not reintroduce a page-right mode selector.
- The global NavBar remains visible in both modes.
- SPICE and control modes share visual regions but not controllers or circuit data.
- SPICE component positions, manual wire waypoints, previews, and route rendering use `SpiceWorkspaceViewport` local coordinates.
- Do not change SPICE coordinate conversion, wire routing, component positions, or host geometry merely to accommodate a visual background.
- `SpiceGridVisual` is a visual-only first sibling, behind `SpiceWireLayer`, `SpiceComponentLayer`, and `SpiceOverlayLayer`. It has `WorkspaceGrid`, `CanvasRenderer`, and `raycastTarget = false`.
- The parent `SpiceWorkspaceViewport` Image remains raycastable because it supplies SPICE blank-click/input behavior.

### Explicitly prohibited unless later authorized

- Do not modify `WorkspaceController`, `WireManager`, `RuntimeStateManager`, control analyzer/validation systems, RuleId, save/load, DrawingDto, templates, snapshots, Runtime Catalog, `VisualPrefabRegistry`, `ComponentVisualRuntimeCatalog`, `DemoSceneBuilder`, `ImportUIAssets`, or formal Build Settings.
- Do not run `DemoSceneBuilder`, `BindDemoScene`, or `SpiceT3PrototypeTools.CreateScene` while editing the production Demo scene.
- Do not use `git reset --hard`, `git restore .`, `git checkout .`, `git clean`, automatic stash, or deletion of other worktrees.
- Do not start T4-B, transient simulation, waveform work, saving, importing, templates, or a broad UI refactor.

## 8. Important Files And Code Paths

### SPICE core and infrastructure

- `Assets/Scripts/Spice/Core/`
- `Assets/Scripts/Spice/Topology/`
- `Assets/Scripts/Spice/Netlist/`
- `Assets/Scripts/Spice/Results/`
- `Assets/Scripts/Spice/Infrastructure/NgspiceProcessRunner.cs`

These are stable in the current visual phase and should not be changed for T4-A3-A.

### SPICE workspace and UI

- `Assets/Scripts/Spice/Workspace/SpiceWorkspaceController.cs`
- `Assets/Scripts/Spice/Workspace/SpiceWorkspaceViewBindings.cs`
- `Assets/Scripts/Spice/Workspace/SpiceWorkspaceDemoHost.cs`
- `Assets/Scripts/Spice/Workspace/SimulationModeController.cs`
- `Assets/Scripts/Spice/UI/`
- `Assets/Scripts/Spice/Workspace/SpiceWorkspacePrototypeBootstrap.cs` (prototype-only)

### Demo integration and visual grid

- `Assets/Scenes/Demo.unity`
- `Assets/Editor/SpiceT4/SpiceT4DemoIntegrationTools.cs`
- `Assets/Scripts/UI/WorkspaceGrid.cs`
- `Assets/Scripts/UI/MainUiTheme.cs`
- `Assets/Scripts/UI/PaletteController.cs`

### Tests and prototype tools

- `Assets/Tests/SpiceT3/`
- `Assets/Editor/SpiceT3/SpiceT3PrototypeTools.cs`
- `Assets/Tests/SpiceT3/SpiceT3WorkspacePrototype.unity`
- `Assets/Tests/SpiceT3/SpiceT3PlayerValidation.unity`

## 9. Known Risks And Pitfalls

1. **UGUI graphic requirement:** any object with `WorkspaceGrid` or another `MaskableGraphic` must have `CanvasRenderer`. Under `RectMask2D`, omission causes a `MissingComponentException` during clipping.
2. **Do not disable the viewport Image raycast target:** only the grid is non-interactive. Turning off the viewport Image can break SPICE blank-click cancellation and input handling.
3. **Unity scene-save noise:** Unity can add trailing whitespace throughout `Demo.unity`. Do not commit that noise. The prior controlled approach removed only trailing spaces and normalized line endings after a narrowly scoped Editor save, then reviewed the semantic diff. Never hand-edit scene YAML fields.
4. **Project lock:** Unity batch runs can fail with "Project already open" if a Unity Editor instance or a just-exiting batch instance still owns the project. Verify the editor is closed and wait for it to exit before running a second batch command.
5. **Existing T4 geometry assertion:** `Tools/Spice/T4/Validate Demo DC Workspace Binding` currently reports a pre-existing mismatch because it expects the SPICE Workspace right offset `-344`, while the scene reports `-332`. This was not changed in T4-A3-A because the visual task explicitly prohibited host geometry changes. Do not silently alter geometry under a visual task; resolve it only in a separately authorized geometry task.
6. **T4 static validation and visual grid:** the T4 validation tool now expects `SpiceGridVisual`, its `WorkspaceGrid`, `CanvasRenderer`, first-sibling order, `raycastTarget = false`, matching background color, and a viewport `RectMask2D`. The existing geometry assertion runs earlier and can still stop the validation before these newer checks.
7. **Global popup behavior:** `GlobalPopupLayer` must stay a final child of the main Canvas, have its own `Canvas` with `overrideSorting = true` / order 100, no mask, and no permanent full-screen blocker while its menu is closed.
8. **Manual acceptance is still essential:** the T4-A3-A grid fix has a committed source/scene change, but visual appearance, input behavior, all target resolutions, and control-mode regression need manual confirmation after `6105399`.

## 10. Recommended Next-Step Order

1. Start with the Git checks in section 11. Stop if tracked changes exist.
2. Open `Demo.unity` in Unity 2022.3.57f1c1 and perform the T4-A3-A manual acceptance steps in section 6.
3. If the CanvasRenderer exception is gone and grid/input behavior is accepted, record the result and treat T4-A3-A as closed.
4. Do not start a later visual batch automatically. Wait for explicit approval and scope for T4-A3-B or T4-A3-C.
5. If a task explicitly authorizes resolution of the existing `-332` vs `-344` geometry mismatch, first perform a read-only four-region geometry audit and then make only the approved targeted change.

## 11. Mandatory Git Checks For A New Conversation

Run these in `E:\Projects\Unity\ElectricalSimulation2D_SpiceT2` before making any change:

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git log -5 --oneline
git worktree list
git diff --check
```

Expected handoff baseline:

```text
feature/spice-t4a-dc-host-integration
610539937bf7c890b981c98b7355889dd1d4960c
<no output from git status --short>
```

If the working tree is not clean, report the exact files and stop. Do not stash, reset, restore, clean, delete, or overwrite anything automatically.

## 12. Long-Term User Requirements Stated In This Conversation

- Work in isolated Git branches/worktrees and make small, independently verifiable commits.
- Preserve existing formal electrical-control functionality and keep SPICE data/logic isolated from it.
- Never modify prohibited business systems merely for convenience.
- Never use SceneManager to switch to a separate SPICE scene for the formal Demo integration; the two modes must remain inside `Demo.unity` / `SimulationPage`.
- Keep the global NavBar visible in both modes.
- Use explicit serialized references for production UI binding; do not search the scene by name or type.
- Treat external process, paths, files, ngspice stdout/stderr, cancellation, and numeric parsing as real defensive boundaries. Do not hide failures with default values or swallowed exceptions.
- Use SI doubles internally for SPICE parameters; do not store free-form unit strings as core data.
- Parameter/topology changes stale results; moving and rotating components do not stale results.
- Do not introduce AC, transient, waveforms, save/load, templates, import/export, Undo/Redo, new components, or a large UI/architecture rewrite without explicit authorization.
- Do not run `DemoSceneBuilder` or rebuild production scene structures unless a task explicitly requests it.
- Before a commit, inspect `git diff --check`, status, stat, and changed file names; do not mix UI importer noise, temporary logs, test builds, templates, or unrelated scene changes into a feature commit.
- Stop for manual acceptance at stated phase boundaries rather than silently continuing into the next phase.

## Handoff Completion Status

This document was created while the repository was clean at commit `6105399`. Creating this file itself makes the working tree dirty until it is reviewed and committed. The only intended uncommitted file after creation is `CODEX_HANDOFF.md`.
-->
