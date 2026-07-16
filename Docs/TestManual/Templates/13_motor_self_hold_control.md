# 电动机连续运行控制电路

## 1. 模板基本信息

- 模板 ID：motor_self_hold_control
- 分类：工业电路
- 难度：中级
- 元件数量：7
- 导线数量：19
- 使用的器件类型：AC_ThreePhase_Power；Breaker_3P；Button_Start_NO；Button_Stop_NC；Contactor_KM_380V；Fuse_3P；Motor_ThreePhase_380V
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/motor_self_hold_control_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| breaker_3p_1 | 空气开关3P [breaker_3p_1]（根据实例 ID 推导） | Breaker_3P | 空气开关3P | -288, 216 | 额定电流=16A |
| fuse_3p_1 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导） | Fuse_3P | 熔断器3P(FU) | 96, 192 | 额定电流=10A |
| km_1 | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导） | Contactor_KM_380V | 交流接触器(KM)<br>(380V) | 480, 0 | 线圈额定电压=380V |
| motor_1 | 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导） | Motor_ThreePhase_380V | 三相异步电动机<br>(380V) | 312, -240 | 额定电压=380V；额定功率=750W；额定电流=1.8A |
| power_3p_1 | 三相交流电源 [power_3p_1]（根据实例 ID 推导） | AC_ThreePhase_Power | 三相交流电源 | -624, 456 | 相电压=220V；线电压=380V |
| start_1 | 启动按钮(NO) [start_1]（根据实例 ID 推导） | Button_Start_NO | 启动按钮(NO) | -24, -240 | 无模板覆盖参数 |
| stop_1 | 停止按钮(NC) [stop_1]（根据实例 ID 推导） | Button_Stop_NC | 停止按钮(NC) | -432, -240 | 无模板覆盖参数 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1 | L1（L1） | breaker_3p_1 | P1_IN（进1） | #F2C71F | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | breaker_3p_1 | P1_OUT（出1） | fuse_3p_1 | L1_IN（L1） | #F2C71F | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-003 | fuse_3p_1 | L1_OUT（T1） | km_1 | L1（1/L1） | #F2C71F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-004 | km_1 | T1（2/T1） | motor_1 | U（U） | #F2C71F | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-005 | power_3p_1 | L2（L2） | breaker_3p_1 | P2_IN（进2） | #1A59F2 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-006 | breaker_3p_1 | P2_OUT（出2） | fuse_3p_1 | L2_IN（L2） | #1A59F2 | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-007 | fuse_3p_1 | L2_OUT（T2） | km_1 | L2（3/L2） | #14A640 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-008 | km_1 | T2（4/T2） | motor_1 | V（V） | #14A640 | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-009 | power_3p_1 | L3（L3） | breaker_3p_1 | P3_IN（进3） | #F21F1F | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-010 | breaker_3p_1 | P3_OUT（出3） | fuse_3p_1 | L3_IN（L3） | #F2C71F | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-011 | fuse_3p_1 | L3_OUT（T3） | km_1 | L3（5/L3） | #F21F1F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-012 | km_1 | T3（6/T3） | motor_1 | W（W） | #F21F1F | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-013 | power_3p_1 | PE（PE） | motor_1 | PE（PE） | #14A640 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-014 | power_3p_1 | L1（L1） | stop_1 | 11（11） | #E74C3C | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-015 | stop_1 | 12（12） | start_1 | 23（23） | #F21F1F | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-016 | start_1 | 24（24） | km_1 | A1（A1） | #F21F1F | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-017 | km_1 | A2（A2） | power_3p_1 | L2（L2） | #E74C3C | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-018 | stop_1 | 12（12） | km_1 | 13（13） | #F21F1F | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.13 |
| W-019 | km_1 | 14（14） | start_1 | 24（24） | #14A640 | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| breaker_3p_1 | Breaker_3P | P1_IN、P1_OUT、P2_IN、P2_OUT、P3_IN、P3_OUT | 无 |
| fuse_3p_1 | Fuse_3P | L1_IN、L1_OUT、L2_IN、L2_OUT、L3_IN、L3_OUT | 无 |
| km_1 | Contactor_KM_380V | 13、14、A1、A2、L1、L2、L3、T1、T2、T3 | 21、22 |
| motor_1 | Motor_ThreePhase_380V | PE、U、V、W | 无 |
| power_3p_1 | AC_ThreePhase_Power | L1、L2、L3、PE | N |
| start_1 | Button_Start_NO | 23、24 | 无 |
| stop_1 | Button_Stop_NC | 11、12 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

