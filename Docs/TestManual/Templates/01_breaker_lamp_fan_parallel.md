# 空开控制灯泡与风扇并联电路

## 1. 模板基本信息

- 模板 ID：breaker_lamp_fan_parallel
- 分类：家庭电路
- 难度：中级
- 元件数量：6
- 导线数量：8
- 使用的器件类型：AC_220V_Power；Breaker_2P；Fan_220V；Lamp_220V；Single_Control_Switch
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/breaker_lamp_fan_parallel_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| breaker_1 | 空气开关2P [breaker_1]（根据实例 ID 推导） | Breaker_2P | 空气开关2P | -360, 144 | 无模板覆盖参数 |
| fan_1 | 电风扇(220V) [fan_1]（根据实例 ID 推导） | Fan_220V | 电风扇(220V) | 216, 48 | 额定电压=220V；功率=60W |
| lamp_1 | 电灯泡(220V) [lamp_1]（根据实例 ID 推导） | Lamp_220V | 电灯泡(220V) | 264, 360 | 额定电压=220V；功率=40W |
| power_1 | 220V电源 [power_1]（根据实例 ID 推导） | AC_220V_Power | 220V电源 | -576, 120 | 输出电压=220V |
| switch_1 | 单开单控开关 [switch_1]（根据实例 ID 推导） | Single_Control_Switch | 单开单控开关 | -120, 264 | 开关类型=1 |
| switch_2 | 单开单控开关 [switch_2]（根据实例 ID 推导） | Single_Control_Switch | 单开单控开关 | -96, -120 | 开关类型=1 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1 | L（L） | breaker_1 | P1_IN（进1） | #F21F1F | 220V电源 [power_1]（根据实例 ID 推导）.L → 空气开关2P [breaker_1]（根据实例 ID 推导）.进1 |
| W-002 | power_1 | N（N） | breaker_1 | P2_IN（进2） | #1A59F2 | 220V电源 [power_1]（根据实例 ID 推导）.N → 空气开关2P [breaker_1]（根据实例 ID 推导）.进2 |
| W-003 | breaker_1 | P1_OUT（出1） | switch_1 | L（L） | #F21F1F | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_1]（根据实例 ID 推导）.L |
| W-004 | breaker_1 | P1_OUT（出1） | switch_2 | L（L） | #F21F1F | 空气开关2P [breaker_1]（根据实例 ID 推导）.出1 → 单开单控开关 [switch_2]（根据实例 ID 推导）.L |
| W-005 | switch_1 | L1（L1） | lamp_1 | L（L） | #F21F1F | 单开单控开关 [switch_1]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-006 | switch_2 | L1（L1） | fan_1 | L（L） | #F21F1F | 单开单控开关 [switch_2]（根据实例 ID 推导）.L1 → 电风扇(220V) [fan_1]（根据实例 ID 推导）.L |
| W-007 | breaker_1 | P2_OUT（出2） | lamp_1 | N（N） | #1A59F2 | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |
| W-008 | breaker_1 | P2_OUT（出2） | fan_1 | N（N） | #1A59F2 | 空气开关2P [breaker_1]（根据实例 ID 推导）.出2 → 电风扇(220V) [fan_1]（根据实例 ID 推导）.N |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| breaker_1 | Breaker_2P | P1_IN、P1_OUT、P2_IN、P2_OUT | 无 |
| fan_1 | Fan_220V | L、N | 无 |
| lamp_1 | Lamp_220V | L、N | 无 |
| power_1 | AC_220V_Power | L、N | L2、N2 |
| switch_1 | Single_Control_Switch | L、L1 | 无 |
| switch_2 | Single_Control_Switch | L、L1 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

