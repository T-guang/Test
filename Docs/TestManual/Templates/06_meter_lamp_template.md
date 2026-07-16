# 单相电能表照明电路

## 1. 模板基本信息

- 模板 ID：meter_lamp_template
- 分类：家庭电路
- 难度：中级
- 元件数量：5
- 导线数量：7
- 使用的器件类型：AC_220V_Power；Breaker_2P；Lamp_220V；Single_Control_Switch；Single_Phase_Meter
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/meter_lamp_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| breaker_1 | 空气开关2P [breaker_1]（根据实例 ID 推导） | Breaker_2P | 空气开关2P | -168, 168 | 额定电压=220V；额定电流=16A |
| lamp_1 | 电灯泡(220V) [lamp_1]（根据实例 ID 推导） | Lamp_220V | 电灯泡(220V) | 312, 168 | 额定电压=220V；功率=40W |
| meter_1 | 单相电能表(220V) [meter_1]（根据实例 ID 推导） | Single_Phase_Meter | 单相电能表(220V) | -432, 168 | 额定电压=220V；额定电流=10A |
| power_1 | 220V电源 [power_1]（根据实例 ID 推导） | AC_220V_Power | 220V电源 | -624, 168 | 输出电压=220V |
| switch_1 | 单开单控开关 [switch_1]（根据实例 ID 推导） | Single_Control_Switch | 单开单控开关 | 48, 168 | 开关类型=1 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1 | L（L） | meter_1 | L_IN（L进） | #F21F1F | 220V电源 [power_1]（根据实例 ID 推导）.L → 单相电能表(220V) [meter_1]（根据实例 ID 推导）.L进 |
| W-002 | power_1 | N（N） | meter_1 | N_IN（N进） | #1A59F2 | 220V电源 [power_1]（根据实例 ID 推导）.N → 单相电能表(220V) [meter_1]（根据实例 ID 推导）.N进 |
| W-003 | meter_1 | L_OUT（L出） | breaker_1 | P1_IN（进1） | #F21F1F | 单相电能表(220V) [meter_1]（根据实例 ID 推导）.L出 → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-004 | meter_1 | N_OUT（N出） | breaker_1 | P2_IN（进2） | #1A59F2 | 单相电能表(220V) [meter_1]（根据实例 ID 推导）.N出 → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-005 | breaker_1 | P1_OUT（出1） | switch_1 | L（L） | #F21F1F | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-006 | switch_1 | L1（L1） | lamp_1 | L（L） | #F21F1F | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-007 | breaker_1 | P2_OUT（出2） | lamp_1 | N（N） | #1A59F2 | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| breaker_1 | Breaker_2P | P1_IN、P1_OUT、P2_IN、P2_OUT | 无 |
| lamp_1 | Lamp_220V | L、N | 无 |
| meter_1 | Single_Phase_Meter | L_IN、L_OUT、N_IN、N_OUT | 无 |
| power_1 | AC_220V_Power | L、N | L2、N2 |
| switch_1 | Single_Control_Switch | L、L1 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

