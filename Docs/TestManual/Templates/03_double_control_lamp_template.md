# 双控照明电路

## 1. 模板基本信息

- 模板 ID：double_control_lamp_template
- 分类：家庭电路
- 难度：中级
- 元件数量：4
- 导线数量：5
- 使用的器件类型：AC_220V_Power；Lamp_220V；Two_Way_Switch
- 模板 JSON 路径：Assets/Resources/Blueprints/Templates/double_control_lamp_template.json

## 2. 元件实例表

| 实例 ID | 测试称呼（推导） | DefinitionName | 显示名称 | 位置 | 参数摘要 |
| --- | --- | --- | --- | --- | --- |
| lamp_1 | 电灯泡(220V) [lamp_1]（根据实例 ID 推导） | Lamp_220V | 电灯泡(220V) | 432, 120 | 额定电压=220V；功率=60W |
| power_1 | 220V电源 [power_1]（根据实例 ID 推导） | AC_220V_Power | 220V电源 | -528, 120 | 输出电压=220V |
| switch_a | 单开双控开关 [switch_a]（根据实例 ID 推导） | Two_Way_Switch | 单开双控开关 | -192, 120 | 开关类型=2 |
| switch_b | 单开双控开关 [switch_b]（根据实例 ID 推导） | Two_Way_Switch | 单开双控开关 | 120, 120 | 开关类型=2 |

## 3. 逐线接线表

| 编号 | 起点实例 | 起点端子 | 终点实例 | 终点端子 | 线色 | 说明 |
| --- | --- | --- | --- | --- | --- | --- |
| W-001 | power_1 | L（L） | switch_a | L（L） | #F21F1F | 220V电源 [power_1]（根据实例 ID 推导）.L → 单开双控开关 [switch_a]（根据实例 ID 推导）.L |
| W-002 | switch_a | L1（L1） | switch_b | L1（L1） | #F21F1F | 单开双控开关 [switch_a]（根据实例 ID 推导）.L1 → 单开双控开关 [switch_b]（根据实例 ID 推导）.L1 |
| W-003 | switch_a | L2（L2） | switch_b | L2（L2） | #F21F1F | 单开双控开关 [switch_a]（根据实例 ID 推导）.L2 → 单开双控开关 [switch_b]（根据实例 ID 推导）.L2 |
| W-004 | switch_b | L（L） | lamp_1 | L（L） | #F21F1F | 单开双控开关 [switch_b]（根据实例 ID 推导）.L → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.L |
| W-005 | power_1 | N（N） | lamp_1 | N（N） | #1A59F2 | 220V电源 [power_1]（根据实例 ID 推导）.N → 电灯泡(220V) [lamp_1]（根据实例 ID 推导）.N |

## 4. 端子使用汇总

| 实例 ID | DefinitionName | 实际使用端子 | 未使用端子 |
| --- | --- | --- | --- |
| lamp_1 | Lamp_220V | L、N | 无 |
| power_1 | AC_220V_Power | L、N | L2、N2 |
| switch_a | Two_Way_Switch | L、L1、L2 | 无 |
| switch_b | Two_Way_Switch | L、L1、L2 | 无 |

## 5. 数据检查结果

## 错误

无。

## 警告

无。

- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。

