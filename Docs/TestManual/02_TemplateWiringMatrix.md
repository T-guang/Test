# 18 张标准模板逐线接线总表

导线编号保持模板 JSON 顺序。回路类型只在端子信息足够明确时作推导，否则显示“未自动判定”。

## 空开控制灯泡与风扇并联电路（breaker_lamp_fan_parallel）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | breaker_1.P1_IN | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-002 | power_1.N | breaker_1.P2_IN | #1A59F2 | 是 | 6 | 测量回路（推导） | 220V电源 [power_1]（根据实例 ID 推导）.N → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-003 | breaker_1.P1_OUT | switch_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-004 | breaker_1.P1_OUT | switch_2.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_2]（根据实例 ID 推导）.L |
| W-005 | switch_1.L1 | lamp_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-006 | switch_2.L1 | fan_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_2]（根据实例 ID 推导）.L1 → 电风扇(220V) [fan_1]（根据实例 ID 推导）.L |
| W-007 | breaker_1.P2_OUT | lamp_1.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |
| W-008 | breaker_1.P2_OUT | fan_1.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电风扇(220V) [fan_1]（根据实例 ID 推导）.N |

## 空气开关控制照明电路（breaker_lamp_template）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | breaker_1.P1_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-002 | power_1.N | breaker_1.P2_IN | #1A59F2 | 是 | 6 | 测量回路（推导） | 220V电源 [power_1]（根据实例 ID 推导）.N → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-003 | breaker_1.P1_OUT | switch_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-004 | switch_1.L1 | lamp_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-005 | breaker_1.P2_OUT | lamp_1.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |

## 双控照明电路（double_control_lamp_template）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | switch_a.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开双控开关 [switch_a]（根据实例 ID 推导）.L |
| W-002 | switch_a.L1 | switch_b.L1 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开双控开关 [switch_a]（根据实例 ID 推导）.L1 → 单开双控开关 [switch_b]（根据实例 ID 推导）.L1 |
| W-003 | switch_a.L2 | switch_b.L2 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开双控开关 [switch_a]（根据实例 ID 推导）.L2 → 单开双控开关 [switch_b]（根据实例 ID 推导）.L2 |
| W-004 | switch_b.L | lamp_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 单开双控开关 [switch_b]（根据实例 ID 推导）.L → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-005 | power_1.N | lamp_1.N | #1A59F2 | 是 | 6 | 未自动判定 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |

## 单开控制双灯电路（double_lamp_single_switch）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | breaker_1.P1_IN | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-002 | power_1.N | breaker_1.P2_IN | #1A59F2 | 是 | 6 | 测量回路（推导） | 220V电源 [power_1]（根据实例 ID 推导）.N → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-003 | breaker_1.P1_OUT | switch_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-004 | switch_1.L1 | lamp_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-005 | switch_1.L1 | lamp_2.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_2]（根据实例 ID 推导）.L |
| W-006 | breaker_1.P2_OUT | lamp_1.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |
| W-007 | breaker_1.P2_OUT | lamp_2.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_2]（根据实例 ID 推导）.N |

## 电灯泡与电风扇并联控制电路（lamp_fan_parallel_template）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | switch_lamp.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开单控开关 [switch_lamp]（根据实例 ID 推导）.L |
| W-002 | switch_lamp.L1 | lamp_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_lamp]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-003 | power_1.N | lamp_1.N | #1A59F2 | 是 | 6 | 未自动判定 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |
| W-004 | power_1.L | switch_fan.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开单控开关 [switch_fan]（根据实例 ID 推导）.L |
| W-005 | switch_fan.L1 | fan_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_fan]（根据实例 ID 推导）.L1 → 电风扇(220V) [fan_1]（根据实例 ID 推导）.L |
| W-006 | power_1.N | fan_1.N | #1A59F2 | 是 | 6 | 未自动判定 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电风扇(220V) [fan_1]（根据实例 ID 推导）.N |

## 单相电能表照明电路（meter_lamp_template）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | meter_1.L_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 单相电能表(220V) [meter_1]（根据实例 ID 推导）.L进 |
| W-002 | power_1.N | meter_1.N_IN | #1A59F2 | 是 | 6 | 未自动判定 | 220V电源 [power_1]（根据实例 ID 推导）.N → 单相电能表(220V) [meter_1]（根据实例 ID 推导）.N进 |
| W-003 | meter_1.L_OUT | breaker_1.P1_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单相电能表(220V) [meter_1]（根据实例 ID 推导）.L出 → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-004 | meter_1.N_OUT | breaker_1.P2_IN | #1A59F2 | 是 | 6 | 测量回路（推导） | 单相电能表(220V) [meter_1]（根据实例 ID 推导）.N出 → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-005 | breaker_1.P1_OUT | switch_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-006 | switch_1.L1 | lamp_1.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-007 | breaker_1.P2_OUT | lamp_1.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |

## 自动往返电动机控制电路（motor_auto_reciprocating_control）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #14A640 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_forward.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_forward.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_forward.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.5/L3 |
| W-010 | fuse_3p_1.L1_OUT | km_reverse.L1 | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.1/L1 |
| W-011 | fuse_3p_1.L2_OUT | km_reverse.L2 | #14A640 | 否 | 0 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.3/L2 |
| W-012 | fuse_3p_1.L3_OUT | km_reverse.L3 | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.5/L3 |
| W-013 | km_forward.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-014 | km_forward.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-015 | km_forward.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | km_reverse.T1 | motor_1.W | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-017 | km_reverse.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-018 | km_reverse.T3 | motor_1.U | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-019 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-020 | power_3p_1.L1 | stop_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-021 | stop_1.12 | start_1.23 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-022 | start_1.24 | sq_right.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 行程开关 SQ（限位开关） [sq_right]（根据实例 ID 推导）.11 |
| W-023 | stop_1.12 | km_forward.13 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.13 |
| W-024 | km_forward.14 | sq_right.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.14 → 行程开关 SQ（限位开关） [sq_right]（根据实例 ID 推导）.11 |
| W-025 | stop_1.12 | sq_left.23 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 行程开关 SQ（限位开关） [sq_left]（根据实例 ID 推导）.23 |
| W-026 | sq_left.24 | sq_right.11 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 行程开关 SQ（限位开关） [sq_left]（根据实例 ID 推导）.24 → 行程开关 SQ（限位开关） [sq_right]（根据实例 ID 推导）.11 |
| W-027 | sq_right.12 | km_reverse.21 | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 行程开关 SQ（限位开关） [sq_right]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.21 |
| W-028 | km_reverse.22 | km_forward.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A1 |
| W-029 | km_forward.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-030 | stop_1.12 | sq_right.23 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 行程开关 SQ（限位开关） [sq_right]（根据实例 ID 推导）.23 |
| W-031 | sq_right.24 | sq_left.11 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 行程开关 SQ（限位开关） [sq_right]（根据实例 ID 推导）.24 → 行程开关 SQ（限位开关） [sq_left]（根据实例 ID 推导）.11 |
| W-032 | stop_1.12 | km_reverse.13 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.13 |
| W-033 | km_reverse.14 | sq_left.11 | #2563EB | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.14 → 行程开关 SQ（限位开关） [sq_left]（根据实例 ID 推导）.11 |
| W-034 | sq_left.12 | km_forward.21 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 行程开关 SQ（限位开关） [sq_left]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.21 |
| W-035 | km_forward.22 | km_reverse.A1 | #F21F1F | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A1 |
| W-036 | km_reverse.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 电动机正反转控制电路（motor_forward_reverse_control）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #1A59F2 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_forward.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_forward.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_forward.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.5/L3 |
| W-010 | fuse_3p_1.L1_OUT | km_reverse.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.1/L1 |
| W-011 | fuse_3p_1.L2_OUT | km_reverse.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.3/L2 |
| W-012 | fuse_3p_1.L3_OUT | km_reverse.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.5/L3 |
| W-013 | km_forward.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-014 | km_forward.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-015 | km_forward.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | km_reverse.T1 | motor_1.W | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-017 | km_reverse.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-018 | km_reverse.T3 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-019 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-020 | power_3p_1.L1 | stop_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-021 | stop_1.12 | km_reverse.21 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.21 |
| W-022 | km_reverse.22 | forward_button_1.23 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.22 → 启动按钮(NO) [forward_button_1]（根据实例 ID 推导）.23 |
| W-023 | forward_button_1.24 | km_forward.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [forward_button_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A1 |
| W-024 | km_forward.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-025 | stop_1.12 | km_forward.21 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.21 |
| W-026 | km_forward.22 | reverse_button_1.23 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.22 → 启动按钮(NO) [reverse_button_1]（根据实例 ID 推导）.23 |
| W-027 | reverse_button_1.24 | km_reverse.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [reverse_button_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A1 |
| W-028 | km_reverse.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 按钮和接触器双重联锁正反转控制电路（motor_forward_reverse_double_interlock）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #1A59F2 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_forward.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_forward.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_forward.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.5/L3 |
| W-010 | fuse_3p_1.L1_OUT | km_reverse.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.1/L1 |
| W-011 | fuse_3p_1.L2_OUT | km_reverse.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.3/L2 |
| W-012 | fuse_3p_1.L3_OUT | km_reverse.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.5/L3 |
| W-013 | km_forward.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-014 | km_forward.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-015 | km_forward.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | km_reverse.T1 | motor_1.W | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-017 | km_reverse.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-018 | km_reverse.T3 | motor_1.U | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-019 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-020 | power_3p_1.L1 | stop_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-021 | stop_1.12 | forward_button_1.23 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 复合按钮SB(绿) [forward_button_1]（根据实例 ID 推导）.23 |
| W-022 | stop_1.12 | reverse_button_1.23 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 复合按钮SB(红) [reverse_button_1]（根据实例 ID 推导）.23 |
| W-023 | forward_button_1.24 | reverse_button_1.11 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 复合按钮SB(绿) [forward_button_1]（根据实例 ID 推导）.24 → 复合按钮SB(红) [reverse_button_1]（根据实例 ID 推导）.11 |
| W-024 | reverse_button_1.12 | km_reverse.21 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 复合按钮SB(红) [reverse_button_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.21 |
| W-025 | km_reverse.22 | km_forward.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A1 |
| W-026 | km_forward.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-027 | reverse_button_1.24 | forward_button_1.11 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 复合按钮SB(红) [reverse_button_1]（根据实例 ID 推导）.24 → 复合按钮SB(绿) [forward_button_1]（根据实例 ID 推导）.11 |
| W-028 | forward_button_1.12 | km_forward.21 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 复合按钮SB(绿) [forward_button_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.21 |
| W-029 | km_forward.22 | km_reverse.A1 | #2563EB | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A1 |
| W-030 | km_reverse.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-031 | stop_1.12 | km_forward.13 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.13 |
| W-032 | km_forward.14 | reverse_button_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.14 → 复合按钮SB(红) [reverse_button_1]（根据实例 ID 推导）.11 |
| W-033 | stop_1.12 | km_reverse.13 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.13 |
| W-034 | km_reverse.14 | forward_button_1.11 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.14 → 复合按钮SB(绿) [forward_button_1]（根据实例 ID 推导）.11 |

## 电气互锁正反转控制电路（motor_forward_reverse_interlock）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #1A59F2 | 否 | 0 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_forward.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_forward.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_forward.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.5/L3 |
| W-010 | fuse_3p_1.L1_OUT | km_reverse.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.1/L1 |
| W-011 | fuse_3p_1.L2_OUT | km_reverse.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.3/L2 |
| W-012 | fuse_3p_1.L3_OUT | km_reverse.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.5/L3 |
| W-013 | km_forward.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-014 | km_forward.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-015 | km_forward.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | km_reverse.T1 | motor_1.W | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-017 | km_reverse.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-018 | km_reverse.T3 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-019 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-020 | power_3p_1.L1 | stop_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-021 | stop_1.12 | forward_button_1.23 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [forward_button_1]（根据实例 ID 推导）.23 |
| W-022 | stop_1.12 | reverse_button_1.23 | #2563EB | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [reverse_button_1]（根据实例 ID 推导）.23 |
| W-023 | forward_button_1.24 | km_reverse.21 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 启动按钮(NO) [forward_button_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.21 |
| W-024 | km_reverse.22 | km_forward.A1 | #B91C1C | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A1 |
| W-025 | km_forward.A2 | power_3p_1.L2 | #14A640 | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-026 | reverse_button_1.24 | km_forward.21 | #2563EB | 否 | 0 | 主回路或供电端（推导） | 启动按钮(NO) [reverse_button_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.21 |
| W-027 | km_forward.22 | km_reverse.A1 | #2563EB | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A1 |
| W-028 | km_reverse.A2 | power_3p_1.L2 | #14A640 | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-029 | stop_1.12 | km_forward.13 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.13 |
| W-030 | km_forward.14 | forward_button_1.24 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_forward]（根据实例 ID 推导）.14 → 启动按钮(NO) [forward_button_1]（根据实例 ID 推导）.24 |
| W-031 | stop_1.12 | km_reverse.13 | #2563EB | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.13 |
| W-032 | km_reverse.14 | reverse_button_1.24 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_reverse]（根据实例 ID 推导）.14 → 启动按钮(NO) [reverse_button_1]（根据实例 ID 推导）.24 |

## 点动与连续运行混合控制电路（motor_jog_continuous）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #E74C3C | 否 | 0 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #14A640 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_1.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_1.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_1.L3 | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-010 | km_1.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-011 | km_1.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-012 | km_1.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-013 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-014 | power_3p_1.L1 | stop_1.11 | #D92D20 | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-015 | stop_1.12 | jog_1.23 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 复合按钮SB(绿) [jog_1]（根据实例 ID 推导）.23 |
| W-016 | jog_1.24 | km_1.A1 | #D92D20 | 是 | 6 | 控制回路（推导） | 复合按钮SB(绿) [jog_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-017 | stop_1.12 | jog_1.11 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 复合按钮SB(绿) [jog_1]（根据实例 ID 推导）.11 |
| W-018 | jog_1.12 | start_1.23 | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 复合按钮SB(绿) [jog_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-019 | start_1.24 | km_1.A1 | #B42318 | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-020 | jog_1.12 | km_1.13 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 复合按钮SB(绿) [jog_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.13 |
| W-021 | km_1.14 | start_1.24 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |
| W-022 | km_1.A2 | power_3p_1.L2 | #1A59F2 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 电动机点动控制电路（motor_jog_control）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #1A59F2 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_1.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_1.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_1.L3 | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-010 | km_1.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-011 | km_1.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-012 | km_1.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-013 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-014 | power_3p_1.L1 | stop_1.11 | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-015 | stop_1.12 | start_1.23 | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-016 | start_1.24 | km_1.A1 | #F21F1F | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-017 | km_1.A2 | power_3p_1.L2 | #E74C3C | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 电动机连续运行控制电路（motor_self_hold_control）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-003 | fuse_3p_1.L1_OUT | km_1.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-004 | km_1.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-005 | power_3p_1.L2 | breaker_3p_1.P2_IN | #1A59F2 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-006 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #1A59F2 | 否 | 0 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-007 | fuse_3p_1.L2_OUT | km_1.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-008 | km_1.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-009 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-010 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-011 | fuse_3p_1.L3_OUT | km_1.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-012 | km_1.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-013 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-014 | power_3p_1.L1 | stop_1.11 | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-015 | stop_1.12 | start_1.23 | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-016 | start_1.24 | km_1.A1 | #F21F1F | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-017 | km_1.A2 | power_3p_1.L2 | #E74C3C | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-018 | stop_1.12 | km_1.13 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.13 |
| W-019 | km_1.14 | start_1.24 | #14A640 | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |

## 两电机时间继电器顺序启动控制电路（motor_sequential_start_timer）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #14A640 | 否 | 0 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_1.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_1.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_1.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-010 | fuse_3p_1.L1_OUT | km_2.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.1/L1 |
| W-011 | fuse_3p_1.L2_OUT | km_2.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.3/L2 |
| W-012 | fuse_3p_1.L3_OUT | km_2.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.5/L3 |
| W-013 | km_1.T1 | motor_1.U | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-014 | km_1.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-015 | km_1.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | km_2.T1 | motor_2.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.2/T1 → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.U |
| W-017 | km_2.T2 | motor_2.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.4/T2 → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.V |
| W-018 | km_2.T3 | motor_2.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.6/T3 → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.W |
| W-019 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-020 | power_3p_1.PE | motor_2.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_2]（根据实例 ID 推导）.PE |
| W-021 | power_3p_1.L1 | stop_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-022 | stop_1.12 | start_1.23 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-023 | start_1.24 | km_1.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-024 | stop_1.12 | km_1.13 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.13 |
| W-025 | km_1.14 | start_1.24 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |
| W-026 | km_1.A1 | kt_1.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 → 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.A1 |
| W-027 | km_1.A2 | power_3p_1.L2 | #14A640 | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-028 | kt_1.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-029 | stop_1.12 | kt_1.15 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.15 |
| W-030 | kt_1.18 | km_2.A1 | #2563EB | 是 | 6 | 控制回路（推导） | 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.18 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.A1 |
| W-031 | stop_1.12 | km_2.13 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.13 |
| W-032 | km_2.14 | kt_1.18 | #14A640 | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.14 → 通电延时时间继电器(KT) (380V) [kt_1]（根据实例 ID 推导）.18 |
| W-033 | km_2.A2 | power_3p_1.L2 | #14A640 | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_2]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 星三角降压启动控制电路（motor_star_delta_start）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | power_3p_1.L2 | breaker_3p_1.P2_IN | #14A640 | 否 | 0 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-003 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-004 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-005 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #14A640 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-006 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-007 | fuse_3p_1.L1_OUT | km_main.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.1/L1 |
| W-008 | fuse_3p_1.L2_OUT | km_main.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.3/L2 |
| W-009 | fuse_3p_1.L3_OUT | km_main.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.5/L3 |
| W-010 | km_main.T1 | motor_star_delta.U1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.2/T1 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U1 |
| W-011 | km_main.T2 | motor_star_delta.V1 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.4/T2 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V1 |
| W-012 | km_main.T3 | motor_star_delta.W1 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.6/T3 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W1 |
| W-013 | power_3p_1.PE | motor_star_delta.PE | #14A640 | 否 | 0 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.PE |
| W-014 | motor_star_delta.U2 | km_star.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U2 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.1/L1 |
| W-015 | motor_star_delta.V2 | km_star.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V2 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.3/L2 |
| W-016 | motor_star_delta.W2 | km_star.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W2 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.5/L3 |
| W-017 | km_star.T1 | star_point.T1 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.2/T1 → 2位端子横排 [star_point]（根据实例 ID 推导）.1A |
| W-018 | km_star.T2 | star_point.T1 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.4/T2 → 2位端子横排 [star_point]（根据实例 ID 推导）.1A |
| W-019 | km_star.T3 | star_point.T1 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.6/T3 → 2位端子横排 [star_point]（根据实例 ID 推导）.1A |
| W-020 | km_delta.L1 | motor_star_delta.U1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.1/L1 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U1 |
| W-021 | km_delta.T1 | motor_star_delta.W2 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.2/T1 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W2 |
| W-022 | km_delta.L2 | motor_star_delta.V1 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.3/L2 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V1 |
| W-023 | km_delta.T2 | motor_star_delta.U2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.4/T2 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.U2 |
| W-024 | km_delta.L3 | motor_star_delta.W1 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.5/L3 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.W1 |
| W-025 | km_delta.T3 | motor_star_delta.V2 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.6/T3 → 星三角电机 (380V) [motor_star_delta]（根据实例 ID 推导）.V2 |
| W-026 | power_3p_1.L1 | stop_1.11 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-027 | stop_1.12 | start_1.23 | #B91C1C | 否 | 0 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-028 | start_1.24 | km_main.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A1 |
| W-029 | stop_1.12 | km_main.13 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.13 |
| W-030 | km_main.14 | start_1.24 | #B91C1C | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |
| W-031 | km_main.A1 | kt_timer.A1 | #B91C1C | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A1 → 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.A1 |
| W-032 | km_main.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-033 | kt_timer.A2 | power_3p_1.L2 | #14A640 | 是 | 6 | 控制回路（推导） | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-034 | km_main.A1 | kt_timer.15 | #2563EB | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_main]（根据实例 ID 推导）.A1 → 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.15 |
| W-035 | kt_timer.16 | km_delta.21 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.16 → 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.21 |
| W-036 | km_delta.22 | km_star.A1 | #2563EB | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.A1 |
| W-037 | km_star.A2 | power_3p_1.L2 | #14A640 | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-038 | kt_timer.18 | km_star.21 | #2563EB | 是 | 6 | 主回路或供电端（推导） | 通电延时时间继电器(KT) (380V) [kt_timer]（根据实例 ID 推导）.18 → 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.21 |
| W-039 | km_star.22 | km_delta.A1 | #2563EB | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_star]（根据实例 ID 推导）.22 → 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.A1 |
| W-040 | km_delta.A2 | power_3p_1.L2 | #14A640 | 否 | 0 | 控制回路（推导） | 交流接触器(KM) (380V) [km_delta]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |

## 热继电器保护电动机控制电路（motor_thermal_protection）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_3p_1.L1 | breaker_3p_1.P1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进1 |
| W-002 | breaker_3p_1.P1_OUT | fuse_3p_1.L1_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出1 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L1 |
| W-003 | fuse_3p_1.L1_OUT | km_1.L1 | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T1 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.1/L1 |
| W-004 | km_1.T1 | fr_1.L1 | #F2C71F | 否 | 0 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.2/T1 → 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.L1 |
| W-005 | fr_1.T1 | motor_1.U | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.T1 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.U |
| W-006 | power_3p_1.L2 | breaker_3p_1.P2_IN | #1A59F2 | 是 | 6 | 主回路（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进2 |
| W-007 | breaker_3p_1.P2_OUT | fuse_3p_1.L2_IN | #1A59F2 | 是 | 6 | 主回路（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出2 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L2 |
| W-008 | fuse_3p_1.L2_OUT | km_1.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T2 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.3/L2 |
| W-009 | km_1.T2 | fr_1.L2 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.4/T2 → 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.L2 |
| W-010 | fr_1.T2 | motor_1.V | #14A640 | 是 | 6 | 主回路或供电端（推导） | 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.T2 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.V |
| W-011 | power_3p_1.L3 | breaker_3p_1.P3_IN | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L3 → 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.进3 |
| W-012 | breaker_3p_1.P3_OUT | fuse_3p_1.L3_IN | #F2C71F | 是 | 6 | 主回路或供电端（推导） | 空气开关3P [breaker_3p_1]（根据实例 ID 推导）.出3 → 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.L3 |
| W-013 | fuse_3p_1.L3_OUT | km_1.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 熔断器3P(FU) [fuse_3p_1]（根据实例 ID 推导）.T3 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.5/L3 |
| W-014 | km_1.T3 | fr_1.L3 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.6/T3 → 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.L3 |
| W-015 | fr_1.T3 | motor_1.W | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.T3 → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.W |
| W-016 | power_3p_1.PE | motor_1.PE | #14A640 | 是 | 6 | 测量端子（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.PE → 三相异步电动机 (380V) [motor_1]（根据实例 ID 推导）.PE |
| W-017 | power_3p_1.L1 | stop_1.11 | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L1 → 停止按钮(NC) [stop_1]（根据实例 ID 推导）.11 |
| W-018 | stop_1.12 | fr_1.95 | #E74C3C | 是 | 6 | 主回路或供电端（推导） | 停止按钮(NC) [stop_1]（根据实例 ID 推导）.12 → 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.95 |
| W-019 | fr_1.96 | start_1.23 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.96 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.23 |
| W-020 | start_1.24 | km_1.A1 | #E74C3C | 是 | 6 | 控制回路（推导） | 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A1 |
| W-021 | km_1.A2 | power_3p_1.L2 | #1A59F2 | 是 | 6 | 控制回路（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.A2 → 三相交流电源 [power_3p_1]（根据实例 ID 推导）.L2 |
| W-022 | fr_1.96 | km_1.13 | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 热继电器(FR) (380V) [fr_1]（根据实例 ID 推导）.96 → 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.13 |
| W-023 | km_1.14 | start_1.24 | #14A640 | 是 | 6 | 主回路或供电端（推导） | 交流接触器(KM) (380V) [km_1]（根据实例 ID 推导）.14 → 启动按钮(NO) [start_1]（根据实例 ID 推导）.24 |

## 单开单控照明电路（single_lamp_template）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | switch_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-002 | switch_1.L1 | lamp_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-003 | power_1.N | lamp_1.N | #1A59F2 | 是 | 6 | 未自动判定 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |

## 双开分别控制双灯电路（two_switch_two_lamp）

| 编号 | 起点 | 终点 | 颜色 | 手动折线 | 路径点 | 回路类型 | 说明 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1.L | breaker_1.P1_IN | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 220V电源 [power_1]（根据实例 ID 推导）.L → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-002 | power_1.N | breaker_1.P2_IN | #1A59F2 | 是 | 6 | 测量回路（推导） | 220V电源 [power_1]（根据实例 ID 推导）.N → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-003 | breaker_1.P1_OUT | switch_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-004 | breaker_1.P1_OUT | switch_2.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_2]（根据实例 ID 推导）.L |
| W-005 | switch_1.L1 | lamp_1.L | #F21F1F | 否 | 0 | 主回路或供电端（推导） | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-006 | switch_2.L1 | lamp_2.L | #F21F1F | 是 | 6 | 主回路或供电端（推导） | 单开单控开关 [switch_2]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_2]（根据实例 ID 推导）.L |
| W-007 | breaker_1.P2_OUT | lamp_1.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |
| W-008 | breaker_1.P2_OUT | lamp_2.N | #1A59F2 | 是 | 6 | 测量回路（推导） | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_2]（根据实例 ID 推导）.N |

