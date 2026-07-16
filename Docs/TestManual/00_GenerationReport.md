# 测试资料数据生成报告

- 生成时间：2026-07-15 17:47:13
- Git 提交：7e6196fbd0bda90d81fb08d4947b1e8c7f98ac52
- Git 分支：develop/v2.4-stabilization
- 模板总数：18
- 家庭模板数量：8
- 工业模板数量：10
- 支持器件数量：42
- 当前可见器件数量：43
- 端子总数：222
- 导线总数：333
- 无效 Definition 数量：0
- 无效 terminalId 数量：0
- 重复实例 ID 数量：0
- 重复导线数量：0
- 缺失 Visual Sprite 数量：1
- 缺失 Visual Prefab 数量：0

## 每张模板统计

| 序号 | 模板 ID | 模板名称 | 分类 | 元件数 | 导线数 | 错误 | 警告 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | breaker_lamp_fan_parallel | 空开控制灯泡与风扇并联电路 | 家庭电路 | 6 | 8 | 0 | 0 |
| 2 | breaker_lamp_template | 空气开关控制照明电路 | 家庭电路 | 4 | 5 | 0 | 0 |
| 3 | double_control_lamp_template | 双控照明电路 | 家庭电路 | 4 | 5 | 0 | 0 |
| 4 | double_lamp_single_switch | 单开控制双灯电路 | 家庭电路 | 5 | 7 | 0 | 0 |
| 5 | lamp_fan_parallel_template | 电灯泡与电风扇并联控制电路 | 家庭电路 | 5 | 6 | 0 | 0 |
| 6 | meter_lamp_template | 单相电能表照明电路 | 家庭电路 | 5 | 7 | 0 | 0 |
| 7 | motor_auto_reciprocating_control | 自动往返电动机控制电路 | 工业电路 | 10 | 36 | 0 | 0 |
| 8 | motor_forward_reverse_control | 电动机正反转控制电路 | 工业电路 | 9 | 28 | 0 | 0 |
| 9 | motor_forward_reverse_double_interlock | 按钮和接触器双重联锁正反转控制电路 | 工业电路 | 9 | 34 | 0 | 0 |
| 10 | motor_forward_reverse_interlock | 电气互锁正反转控制电路 | 工业电路 | 9 | 32 | 0 | 0 |
| 11 | motor_jog_continuous | 点动与连续运行混合控制电路 | 工业电路 | 8 | 22 | 0 | 0 |
| 12 | motor_jog_control | 电动机点动控制电路 | 工业电路 | 7 | 17 | 0 | 0 |
| 13 | motor_self_hold_control | 电动机连续运行控制电路 | 工业电路 | 7 | 19 | 0 | 0 |
| 14 | motor_sequential_start_timer | 两电机时间继电器顺序启动控制电路 | 工业电路 | 10 | 33 | 0 | 0 |
| 15 | motor_star_delta_start | 星三角降压启动控制电路 | 工业电路 | 11 | 40 | 0 | 0 |
| 16 | motor_thermal_protection | 热继电器保护电动机控制电路 | 工业电路 | 8 | 23 | 0 | 0 |
| 17 | single_lamp_template | 单开单控照明电路 | 家庭电路 | 3 | 3 | 0 | 0 |
| 18 | two_switch_two_lamp | 双开分别控制双灯电路 | 家庭电路 | 6 | 8 | 0 | 0 |

## 错误

无。

## 警告

- 当前可见器件缺失 Runtime Visual Sprite：Tool_Multimeter

## 推导字段说明

- 端子功能、NO/NC 属性、回路类型、画面位置和测试称呼如无项目原始字段，均明确标记为“根据 terminalId 和元件类型推导”或“根据实例 ID 推导”。
- 本资料不评价模板电气原理，仅检查数据结构和引用完整性。

