# 电灯泡与电风扇并联控制电路

## 1. 模板基本信息

- 模板 ID：lamp_fan_parallel_template
- 分类：家庭电路
- 难度：中级
- 元件数量：5
- 导线数量：6
- 使用的器件类型：AC_220V_Power；Fan_220V；Lamp_220V；Single_Control_Switch
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/lamp_fan_parallel_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| fan_1 | 电风扇(220V) [fan_1]（根据实例 ID 推导） | Fan_220V | 电风扇(220V) | 168, 24 | 额定电压=220V；功率=45W |
| lamp_1 | 电灯泡(220V) [lamp_1]（根据实例 ID 推导） | Lamp_220V | 电灯泡(220V) | 192, 288 | 额定电压=220V；功率=40W |
| power_1 | 220V电源 [power_1]（根据实例 ID 推导） | AC_220V_Power | 220V电源 | -552, 96 | 输出电压=220V |
| switch_fan | 单开单控开关 [switch_fan]（根据实例 ID 推导） | Single_Control_Switch | 单开单控开关 | -168, -192 | 开关类型=1 |
| switch_lamp | 单开单控开关 [switch_lamp]（根据实例 ID 推导） | Single_Control_Switch | 单开单控开关 | -144, 288 | 开关类型=1 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1 | L（L） | switch_lamp | L（L） | #F21F1F | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开单控开关 [switch_lamp]（根据实例 ID 推导）.L |
| W-002 | switch_lamp | L1（L1） | lamp_1 | L（L） | #F21F1F | 单开单控开关 [switch_lamp]（根据实例 ID 推导）.L1 → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-003 | power_1 | N（N） | lamp_1 | N（N） | #1A59F2 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |
| W-004 | power_1 | L（L） | switch_fan | L（L） | #F21F1F | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开单控开关 [switch_fan]（根据实例 ID 推导）.L |
| W-005 | switch_fan | L1（L1） | fan_1 | L（L） | #F21F1F | 单开单控开关 [switch_fan]（根据实例 ID 推导）.L1 → 电风扇(220V) [fan_1]（根据实例 ID 推导）.L |
| W-006 | power_1 | N（N） | fan_1 | N（N） | #1A59F2 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电风扇(220V) [fan_1]（根据实例 ID 推导）.N |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| fan_1 | Fan_220V | L、N | 无 |
| lamp_1 | Lamp_220V | L、N | 无 |
| power_1 | AC_220V_Power | L、N | L2、N2 |
| switch_fan | Single_Control_Switch | L、L1 | 无 |
| switch_lamp | Single_Control_Switch | L、L1 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

