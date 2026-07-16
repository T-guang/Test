# 已确认模板运行用例

## 自动往返（motor_auto_reciprocating_control）
| 测试对象 | 实例 ID | DefinitionName | 可观察状态 / 说明 |
| --- | --- | --- | --- |
| 启动按钮 | `start_1` | `Button_Start_NO` | 按下并释放；之后不人工操作 SQ。 |
| 正向 KM（推导测试称呼） | `km_forward` | `Contactor_KM_380V` | `IsEnergized`；正向称呼仅根据实例 ID 推导。 |
| 反向 KM（推导测试称呼） | `km_reverse` | `Contactor_KM_380V` | `IsEnergized`；反向称呼仅根据实例 ID 推导。 |
| 电机与虚拟运动 | `motor_1` | `Motor_ThreePhase_380V` | `MotionRuntimeState.Direction`、`Position`、`LeftLimitTriggered`、`RightLimitTriggered`。 |
| 虚拟限位 | `sq_left` / `sq_right` | `LimitSwitch_Compound` | 分别读取 `motor_1` 的左/右虚拟限位触发状态。 |

1. 加载模板，点击开始仿真，按下并释放 `start_1`，之后不手动操作 SQ。
2. 观察 `km_forward`（正向测试称呼，根据实例 ID 推导）与 `km_reverse`（反向测试称呼，根据实例 ID 推导）自动交替；二者不得同时吸合。
3. 观察 `motor_1` 的 `MotionDirection`、`Position`，以及 `sq_left` / `sq_right` 的虚拟触发；建议观察至少 6 秒，覆盖初始 Position=50、Speed=20 下的一次边界到达。
4. 停止仿真后，RuntimeStateManager 清除运动状态；再次进入会以 Position=50、Direction=Stopped 重新建立。

## 两电机顺序启动（motor_sequential_start_timer）
| 测试对象 | 实例 ID | DefinitionName | 可观察状态 / 说明 |
| --- | --- | --- | --- |
| 启动按钮 | `start_1` | `Button_Start_NO` | 按下并释放。 |
| 第一回路 | `km_1` / `motor_1` | `Contactor_KM_380V` / `Motor_ThreePhase_380V` | 接触器 `IsEnergized` 与电机运行态。 |
| 第二回路 | `km_2` / `motor_2` | `Contactor_KM_380V` / `Motor_ThreePhase_380V` | 初始停止，KT 到期后运行。 |
| 延时器 | `kt_1` | `Timer_OnDelay_380V` | `TimerRuntimeState.Phase`、`ElapsedSeconds`、`DelaySeconds`；当前模板未覆盖延时参数，运行逻辑默认约 3 秒。 |

1. 点击开始仿真，按下并释放 `start_1`，不操作 `kt_1`。
2. 观察 `km_1` / `motor_1` 先运行；`km_2` / `motor_2` 初始停止。
3. 等待默认约 3 秒，观察 `kt_1` 从 Timing 到 Elapsed，随后 `km_2` / `motor_2` 运行；之后两台电机保持运行。
4. 点击停止并再次启动时，KT 运行态复位后重新计时。

## 星三角启动（motor_star_delta_start）
| 测试对象 | 实例 ID | DefinitionName | 可观察状态 / 说明 |
| --- | --- | --- | --- |
| 启动按钮 | `start_1` | `Button_Start_NO` | 按下并释放。 |
| 主 KM | `km_main` | `Contactor_KM_380V` | `IsEnergized`。 |
| 星形 KM | `km_star` | `Contactor_KM_380V` | `IsEnergized`；0 至约 3 秒为观察重点。 |
| 三角 KM | `km_delta` | `Contactor_KM_380V` | `IsEnergized`；约 3 秒后为观察重点。 |
| 延时器 | `kt_timer` | `Timer_OnDelay_380V` | `TimerRuntimeState.Phase`、`ElapsedSeconds`、`DelaySeconds`；当前模板未覆盖延时参数，运行逻辑默认约 3 秒。 |
| 六端子电机 | `motor_star_delta` | `Motor_StarDelta_380V` | 观察正常运行视觉；不以参数估算数值判定。 |

1. 点击开始仿真，按下并释放 `start_1`，不手动操作 `kt_timer`、`km_star` 或 `km_delta`。
2. 0 至约 3 秒观察 `km_main`、`km_star` 与 `kt_timer` 的启动/计时状态；`motor_star_delta` 可持续显示正常运行。
3. 约 3 秒后观察 `km_star` 释放、`km_delta` 吸合，且星/三角接触器不得同时吸合。当前代码没有单独承诺一个可视的停机间隙；按实际状态切换观察。
4. 点击停止并再次启动时，KT 复位后重新计时。

## 测试政策
- 参数估算、电流、电压与功率不属于正式人工测试范围；遗留报告内容不作为通过/失败依据。
- SQ 与 KT 不要求测试人员手动操作；自动往返限位和 KT 计时由运行态自动推进。
- 以上实例 ID 来自三份模板 JSON；运行态规则来自 RuntimeStateManager、SimulationEngine 与 WorkspaceController。

