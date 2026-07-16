# 两电机时间继电器顺序启动控制电路

## 1. 模板基本信息

- 模板 ID：motor_sequential_start_timer
- 分类：工业电路
- 难度：中高级
- 元件数量：10
- 导线数量：33
- 使用的器件类型：AC_ThreePhase_Power；Breaker_3P；Button_Start_NO；Button_Stop_NC；Contactor_KM_380V；Fuse_3P；Motor_ThreePhase_380V；Timer_OnDelay_380V
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/motor_sequential_start_timer_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| breaker_3p_1 | 空气开关3P [breaker_3p_1]（根据实例 ID 推导） | Breaker_3P | 空气开关3P | -480, 432 | 无模板覆盖参数 |
| fuse_3p_1 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导） | Fuse_3P | 熔断器3P(FU) | -120, 432 | 无模板覆盖参数 |
| km_1 | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导） | Contactor_KM_380V | 交流接触器(KM)<br>(380V) | 240, 192 | 无模板覆盖参数 |
| km_2 | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导） | Contactor_KM_380V | 交流接触器(KM)<br>(380V) | 600, 192 | 无模板覆盖参数 |
| kt_1 | 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导） | Timer_OnDelay_380V | 通电延时时间继电器(KT)<br>(380V) | 240, -144 | 无模板覆盖参数 |
| motor_1 | 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导） | Motor_ThreePhase_380V | 三相异步电动机<br>(380V) | 144, -432 | 无模板覆盖参数 |
| motor_2 | 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导） | Motor_ThreePhase_380V | 三相异步电动机<br>(380V) | 504, -432 | 无模板覆盖参数 |
| power_3p_1 | 三相交流电源 [power_3p_1]（根据实例 ID 推导） | AC_ThreePhase_Power | 三相交流电源 | -840, 432 | 无模板覆盖参数 |
| start_1 | 启动按钮(NO) [start_1]（根据实例 ID 推导） | Button_Start_NO | 启动按钮(NO) | -432, -192 | 无模板覆盖参数 |
| stop_1 | 停止按钮(NC) [stop_1]（根据实例 ID 推导） | Button_Stop_NC | 停止按钮(NC) | -768, -192 | 无模板覆盖参数 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1 | L1（L1） | breaker_3p_1 | P1_IN（进1） | #F2C71F | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1 | L2（L2） | breaker_3p_1 | P2_IN（进2） | #14A640 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1 | L3（L3） | breaker_3p_1 | P3_IN（进3） | #F21F1F | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1 | P1_OUT（出1） | fuse_3p_1 | L1_IN（L1） | #F2C71F | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1 | P2_OUT（出2） | fuse_3p_1 | L2_IN（L2） | #14A640 | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1 | P3_OUT（出3） | fuse_3p_1 | L3_IN（L3） | #F21F1F | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1 | L1_OUT（T1） | km_1 | L1（1/L1） | #F2C71F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1 | L2_OUT（T2） | km_1 | L2（3/L2） | #14A640 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1 | L3_OUT（T3） | km_1 | L3（5/L3） | #F21F1F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-010 | fuse_3p_1 | L1_OUT（T1） | km_2 | L1（1/L1） | #F2C71F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.1/L1 |
| W-011 | fuse_3p_1 | L2_OUT（T2） | km_2 | L2（3/L2） | #14A640 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.3/L2 |
| W-012 | fuse_3p_1 | L3_OUT（T3） | km_2 | L3（5/L3） | #F21F1F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.5/L3 |
| W-013 | km_1 | T1（2/T1） | motor_1 | U（U） | #F2C71F | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-014 | km_1 | T2（4/T2） | motor_1 | V（V） | #14A640 | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-015 | km_1 | T3（6/T3） | motor_1 | W（W） | #F21F1F | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | km_2 | T1（2/T1） | motor_2 | U（U） | #F2C71F | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.U |
| W-017 | km_2 | T2（4/T2） | motor_2 | V（V） | #14A640 | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.V |
| W-018 | km_2 | T3（6/T3） | motor_2 | W（W） | #F21F1F | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.W |
| W-019 | power_3p_1 | PE（PE） | motor_1 | PE（PE） | #14A640 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-020 | power_3p_1 | PE（PE） | motor_2 | PE（PE） | #14A640 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.PE |
| W-021 | power_3p_1 | L1（L1） | stop_1 | 11（11） | #B91C1C | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-022 | stop_1 | 12（12） | start_1 | 23（23） | #B91C1C | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-023 | start_1 | 24（24） | km_1 | A1（A1） | #B91C1C | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-024 | stop_1 | 12（12） | km_1 | 13（13） | #B91C1C | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.13 |
| W-025 | km_1 | 14（14） | start_1 | 24（24） | #B91C1C | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |
| W-026 | km_1 | A1（A1） | kt_1 | A1（A1） | #B91C1C | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 → 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.A1 |
| W-027 | km_1 | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-028 | kt_1 | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-029 | stop_1 | 12（12） | kt_1 | 15（15） | #2563EB | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.15 |
| W-030 | kt_1 | 18（18） | km_2 | A1（A1） | #2563EB | 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.18 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.A1 |
| W-031 | stop_1 | 12（12） | km_2 | 13（13） | #2563EB | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.13 |
| W-032 | km_2 | 14（14） | kt_1 | 18（18） | #14A640 | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.14 → 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.18 |
| W-033 | km_2 | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| breaker_3p_1 | Breaker_3P | P1_IN、P1_OUT、P2_IN、P2_OUT、P3_IN、P3_OUT | 无 |
| fuse_3p_1 | Fuse_3P | L1_IN、L1_OUT、L2_IN、L2_OUT、L3_IN、L3_OUT | 无 |
| km_1 | Contactor_KM_380V | 13、14、A1、A2、L1、L2、L3、T1、T2、T3 | 21、22 |
| km_2 | Contactor_KM_380V | 13、14、A1、A2、L1、L2、L3、T1、T2、T3 | 21、22 |
| kt_1 | Timer_OnDelay_380V | 15、18、A1、A2 | 16 |
| motor_1 | Motor_ThreePhase_380V | PE、U、V、W | 无 |
| motor_2 | Motor_ThreePhase_380V | PE、U、V、W | 无 |
| power_3p_1 | AC_ThreePhase_Power | L1、L2、L3、PE | N |
| start_1 | Button_Start_NO | 23、24 | 无 |
| stop_1 | Button_Stop_NC | 11、12 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

