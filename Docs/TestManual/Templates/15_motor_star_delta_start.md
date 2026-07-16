# 星三角降压启动控制电路

## 1. 模板基本信息

- 模板 ID：motor_star_delta_start
- 分类：工业电路
- 难度：高级
- 元件数量：11
- 导线数量：40
- 使用的器件类型：AC_ThreePhase_Power；Breaker_3P；Button_Start_NO；Button_Stop_NC；Contactor_KM_380V；Fuse_3P；Motor_StarDelta_380V；TerminalBlock_2H；Timer_OnDelay_380V
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/motor_star_delta_start_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| breaker_3p_1 | 空气开关3P [breaker_3p_1]（根据实例 ID 推导） | Breaker_3P | 空气开关3P | -648, 480 | 无模板覆盖参数 |
| fuse_3p_1 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导） | Fuse_3P | 熔断器3P(FU) | -336, 480 | 无模板覆盖参数 |
| km_delta | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导） | Contactor_KM_380V | 交流接触器(KM)<br>(380V) | 624, -96 | 无模板覆盖参数 |
| km_main | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导） | Contactor_KM_380V | 交流接触器(KM)<br>(380V) | 120, 480 | 无模板覆盖参数 |
| km_star | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导） | Contactor_KM_380V | 交流接触器(KM)<br>(380V) | 216, -96 | 无模板覆盖参数 |
| kt_timer | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导） | Timer_OnDelay_380V | 通电延时时间继电器(KT)<br>(380V) | -72, -288 | 无模板覆盖参数 |
| motor_star_delta | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导） | Motor_StarDelta_380V | 星三角电机<br>(380V) | 504, 216 | 无模板覆盖参数 |
| power_3p_1 | 三相交流电源 [power_3p_1]（根据实例 ID 推导） | AC_ThreePhase_Power | 三相交流电源 | -1008, 480 | 无模板覆盖参数 |
| star_point | 2位端子横排 [star_point]（根据实例 ID 推导） | TerminalBlock_2H | 2位端子横排 | 360, -384 | 无模板覆盖参数 |
| start_1 | 启动按钮(NO) [start_1]（根据实例 ID 推导） | Button_Start_NO | 启动按钮(NO) | -648, -312 | 无模板覆盖参数 |
| stop_1 | 停止按钮(NC) [stop_1]（根据实例 ID 推导） | Button_Stop_NC | 停止按钮(NC) | -912, -312 | 无模板覆盖参数 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1 | L1（L1） | breaker_3p_1 | P1_IN（进1） | #F2C71F | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1 | L2（L2） | breaker_3p_1 | P2_IN（进2） | #14A640 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1 | L3（L3） | breaker_3p_1 | P3_IN（进3） | #F21F1F | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1 | P1_OUT（出1） | fuse_3p_1 | L1_IN（L1） | #F2C71F | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1 | P2_OUT（出2） | fuse_3p_1 | L2_IN（L2） | #14A640 | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1 | P3_OUT（出3） | fuse_3p_1 | L3_IN（L3） | #F21F1F | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1 | L1_OUT（T1） | km_main | L1（1/L1） | #F2C71F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1 | L2_OUT（T2） | km_main | L2（3/L2） | #14A640 | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1 | L3_OUT（T3） | km_main | L3（5/L3） | #F21F1F | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.5/L3 |
| W-010 | km_main | T1（2/T1） | motor_star_delta | U1（U1） | #F2C71F | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.2/T1 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U1 |
| W-011 | km_main | T2（4/T2） | motor_star_delta | V1（V1） | #14A640 | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.4/T2 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V1 |
| W-012 | km_main | T3（6/T3） | motor_star_delta | W1（W1） | #F21F1F | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.6/T3 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W1 |
| W-013 | power_3p_1 | PE（PE） | motor_star_delta | PE（PE） | #14A640 | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.PE |
| W-014 | motor_star_delta | U2（U2） | km_star | L1（1/L1） | #F2C71F | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U2 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.1/L1 |
| W-015 | motor_star_delta | V2（V2） | km_star | L2（3/L2） | #14A640 | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V2 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.3/L2 |
| W-016 | motor_star_delta | W2（W2） | km_star | L3（5/L3） | #F21F1F | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W2 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.5/L3 |
| W-017 | km_star | T1（2/T1） | star_point | T1（1A） | #2563EB | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.2/T1 → 2位端子横排 [star_point]（根据实例 ID 推导）.1A |
| W-018 | km_star | T2（4/T2） | star_point | T1（1A） | #2563EB | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.4/T2 → 2位端子横排 [star_point]（根据实例 ID 推导）.1A |
| W-019 | km_star | T3（6/T3） | star_point | T1（1A） | #2563EB | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.6/T3 → 2位端子横排 [star_point]（根据实例 ID 推导）.1A |
| W-020 | km_delta | L1（1/L1） | motor_star_delta | U1（U1） | #F2C71F | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.1/L1 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U1 |
| W-021 | km_delta | T1（2/T1） | motor_star_delta | W2（W2） | #F2C71F | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.2/T1 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W2 |
| W-022 | km_delta | L2（3/L2） | motor_star_delta | V1（V1） | #14A640 | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.3/L2 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V1 |
| W-023 | km_delta | T2（4/T2） | motor_star_delta | U2（U2） | #14A640 | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.4/T2 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U2 |
| W-024 | km_delta | L3（5/L3） | motor_star_delta | W1（W1） | #F21F1F | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.5/L3 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W1 |
| W-025 | km_delta | T3（6/T3） | motor_star_delta | V2（V2） | #F21F1F | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.6/T3 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V2 |
| W-026 | power_3p_1 | L1（L1） | stop_1 | 11（11） | #B91C1C | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-027 | stop_1 | 12（12） | start_1 | 23（23） | #B91C1C | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-028 | start_1 | 24（24） | km_main | A1（A1） | #B91C1C | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A1 |
| W-029 | stop_1 | 12（12） | km_main | 13（13） | #B91C1C | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.13 |
| W-030 | km_main | 14（14） | start_1 | 24（24） | #B91C1C | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |
| W-031 | km_main | A1（A1） | kt_timer | A1（A1） | #B91C1C | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A1 → 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.A1 |
| W-032 | km_main | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-033 | kt_timer | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-034 | km_main | A1（A1） | kt_timer | 15（15） | #2563EB | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A1 → 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.15 |
| W-035 | kt_timer | 16（16） | km_delta | 21（21） | #2563EB | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.16 → 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.21 |
| W-036 | km_delta | 22（22） | km_star | A1（A1） | #2563EB | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.A1 |
| W-037 | km_star | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-038 | kt_timer | 18（18） | km_star | 21（21） | #2563EB | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.18 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.21 |
| W-039 | km_star | 22（22） | km_delta | A1（A1） | #2563EB | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.A1 |
| W-040 | km_delta | A2（A2） | power_3p_1 | L2（L2） | #14A640 | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| breaker_3p_1 | Breaker_3P | P1_IN、P1_OUT、P2_IN、P2_OUT、P3_IN、P3_OUT | 无 |
| fuse_3p_1 | Fuse_3P | L1_IN、L1_OUT、L2_IN、L2_OUT、L3_IN、L3_OUT | 无 |
| km_delta | Contactor_KM_380V | 21、22、A1、A2、L1、L2、L3、T1、T2、T3 | 13、14 |
| km_main | Contactor_KM_380V | 13、14、A1、A2、L1、L2、L3、T1、T2、T3 | 21、22 |
| km_star | Contactor_KM_380V | 21、22、A1、A2、L1、L2、L3、T1、T2、T3 | 13、14 |
| kt_timer | Timer_OnDelay_380V | 15、16、18、A1、A2 | 无 |
| motor_star_delta | Motor_StarDelta_380V | PE、U1、U2、V1、V2、W1、W2 | 无 |
| power_3p_1 | AC_ThreePhase_Power | L1、L2、L3、PE | N |
| star_point | TerminalBlock_2H | T1 | B1、B2、T2 |
| start_1 | Button_Start_NO | 23、24 | 无 |
| stop_1 | Button_Stop_NC | 11、12 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

