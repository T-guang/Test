# ElectricalSimulation2D

基于 Unity 2022.3.57f1c1 的二维电气教学与仿真项目。正式入口为
`Assets/Scenes/Demo.unity`，同一 Simulation 页面内包含彼此隔离的两种模式：

- 控制电路模式：继电器、接触器、按钮、指示灯等既有教学流程。
- SPICE DC 模式：本地 `ngspice_con.exe` 驱动的直流工作点仿真。

本项目用于教学和演示，不是工业级 SPICE、PLC 或电气设计软件。

## SPICE DC 功能

当前支持 10 类器件：

- 直流电压源、直流电流源
- 电阻、电容、电感
- GND
- 理想手动开关
- 通用硅二极管
- 电压探针、电流探针

工作区支持：

- 器件拖放、移动、旋转和参数编辑
- 点击端子创建 Wire、手工多折点、折点撤销和取消
- Delete/Backspace 删除选中的器件或 Wire
- 缩放、平移、适配全部和重置视图
- DC 结果、阻断诊断和生成网表的独立滚动与复制
- 清空后器件编号从 1 重新开始；单个删除不复用编号
- `.spicejson` 图纸保存、另存为和事务式导入

控制模式和 SPICE 模式只共享页面外壳，不共享组件、Wire、DTO、求解器或保存数据。
SPICE 权威数据为 `SpiceWorkspaceModel.Components` 和 `SpiceWorkspaceModel.Wires`。

## 稳定性边界

- D1：计算请求使用 electrical revision、request ID 和 Model 代际校验，过期异步结果不能覆盖当前画布。
- D2：导入限制为 1 MB、500 个器件、1000 条 Wire、单 Wire 128 个折点、
  全图 10000 个折点、坐标绝对值 20000，并限制标识符和参数幅度。
- D3：清空和成功导入会恢复 `NeverRun`，清除旧结果、诊断与网表复制资格；
  网表只有在对应当前 electrical revision 时可复制。
- D3.1：未预期运行时异常只向用户显示 `SPICE_RUNTIME_UNEXPECTED`，
  完整异常仅写入开发日志。

## 保存/导入 V1

图纸保存器件类型、InstanceId、逻辑位置、旋转、SI 参数、开关状态、
Wire 两端、路由模式和手工折点。不会保存仿真结果、网表、诊断、选择、
pending Wire、滚动位置、缩放或平移。

已知限制：手工 Wire 的电气连接和折点数据可以往返，但保存时的端点方向规范化
没有记录“原始第一段方向”，导入后的正交视觉路径可能与保存前不同。当前接线交互
和 DC 电气语义保持不变；是否升级视觉保真应作为独立 V2 任务处理。

## 验证

关闭 Unity 后运行 Editor 验证：

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.57f1c1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "E:\Projects\Unity\ElectricalSimulation2D_SpiceT2" `
  -logFile "<log-path>" `
  -executeMethod ElectricalSim.EditorTools.SpiceT3.SpiceT3PrototypeTools.RunAllFromCommandLine
```

成功条件：

- T1：本地 ngspice 单电阻验证通过
- T2：`Fixtures=22`
- T3：Editor workspace mapping and invalidation validation passed

构建现有 Player Harness，不创建或重写场景：

```powershell
& "C:\Program Files\Unity\Hub\Editor\2022.3.57f1c1\Editor\Unity.exe" `
  -batchmode -nographics -quit `
  -projectPath "E:\Projects\Unity\ElectricalSimulation2D_SpiceT2" `
  -executeMethod ElectricalSim.EditorTools.SpiceT3.SpiceT3PrototypeTools.BuildPlayerValidationFromCommandLine `
  --spice-t3-build="E:\Builds\...\SpiceT3-StabilizationHarness.exe"
```

运行 Harness：

```powershell
& "E:\Builds\...\SpiceT3-StabilizationHarness.exe" `
  -batchmode -nographics `
  -logFile "E:\Builds\...\Player.log" `
  --spice-t3-result="E:\Builds\...\SpiceT3PlayerResult.json"
```

报告必须同时通过 D1 旧结果丢弃、D2 路径级超限导入、D3 清空状态、
网表修订和未预期异常信息收口。

## 构建与场景纪律

- 正式 Windows Player 只显式构建 `Assets/Scenes/Demo.unity`。
- `Demo.unity` 应为 buildIndex 0，LoginScene 不进入正式构建。
- Build 输出必须放在仓库外。
- 不要运行 `Tools/Electrical Demo/Build Demo Scene`。
- 不要运行 `DemoSceneBuilder`、`BindDemoScene` 或任何会重建正式 UI/Scene 的旧工具。
- 不要把 `SpiceWorkspacePrototypeBootstrap` 放入 `Demo.unity`；它只属于 T3 测试场景。
- 不要手工编辑 `Demo.unity` YAML。

## 下一步

1. 决定是否为手工 Wire 视觉保真设计保存格式 V2。
2. 若接受 V1 已知限制，再进入单频 AC V1 的只读契约审计。

AC、瞬态、波形、运放、BJT/MOSF 和新器件均尚未开始。
