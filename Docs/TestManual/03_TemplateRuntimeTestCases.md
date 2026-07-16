# 18 张模板标准运行操作用例

- Git 分支：`develop/v2.4-stabilization`
- Git 提交：`7e6196fbd0bda90d81fb08d4947b1e8c7f98ac52`
- 证据规则：只有模板 JSON、现有快照或运行代码直接证明的结果写为确定；其余标记“需项目负责人确认”。

## 空开控制灯泡与风扇并联电路（breaker_lamp_fan_parallel）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/breaker_lamp_fan_parallel_template.json`
- 默认元件数/导线数：6 / 8

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| breaker_lamp_fan_parallel-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Fan_220V#1=Running；Lamp_220V#1=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| breaker_lamp_fan_parallel-R-002 | breaker_1 | Breaker_2P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| breaker_lamp_fan_parallel-R-003 | switch_1 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| breaker_lamp_fan_parallel-R-004 | switch_2 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| breaker_lamp_fan_parallel-R-005 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 空气开关控制照明电路（breaker_lamp_template）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/breaker_lamp_template.json`
- 默认元件数/导线数：4 / 5

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| breaker_lamp_template-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Lamp_220V#1=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| breaker_lamp_template-R-002 | breaker_1 | Breaker_2P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| breaker_lamp_template-R-003 | switch_1 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| breaker_lamp_template-R-004 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 双控照明电路（double_control_lamp_template）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/double_control_lamp_template.json`
- 默认元件数/导线数：4 / 5

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| double_control_lamp_template-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Lamp_220V#1=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| double_control_lamp_template-R-002 | switch_a | Two_Way_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| double_control_lamp_template-R-003 | switch_b | Two_Way_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| double_control_lamp_template-R-004 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 单开控制双灯电路（double_lamp_single_switch）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/double_lamp_single_switch_template.json`
- 默认元件数/导线数：5 / 7

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| double_lamp_single_switch-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Lamp_220V#1=Off；Lamp_220V#2=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| double_lamp_single_switch-R-002 | breaker_1 | Breaker_2P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| double_lamp_single_switch-R-003 | switch_1 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| double_lamp_single_switch-R-004 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 电灯泡与电风扇并联控制电路（lamp_fan_parallel_template）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/lamp_fan_parallel_template.json`
- 默认元件数/导线数：5 / 6

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| lamp_fan_parallel_template-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Fan_220V#1=Stopped；Lamp_220V#1=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| lamp_fan_parallel_template-R-002 | switch_lamp | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| lamp_fan_parallel_template-R-003 | switch_fan | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| lamp_fan_parallel_template-R-004 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 单相电能表照明电路（meter_lamp_template）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/meter_lamp_template.json`
- 默认元件数/导线数：5 / 7

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| meter_lamp_template-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Lamp_220V#1=On；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| meter_lamp_template-R-002 | breaker_1 | Breaker_2P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| meter_lamp_template-R-003 | switch_1 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| meter_lamp_template-R-004 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 自动往返电动机控制电路（motor_auto_reciprocating_control）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_auto_reciprocating_control_template.json`
- 默认元件数/导线数：10 / 36

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_auto_reciprocating_control-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#2=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；LimitSwitch_Compound#1=NotTriggered，触发=否；LimitSwitch_Compound#2=NotTriggered，触发=否；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；默认运行态由 Reset 创建时 Position=50、Direction=Stopped。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_auto_reciprocating_control-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_auto_reciprocating_control-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_auto_reciprocating_control-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_auto_reciprocating_control-R-005 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_auto_reciprocating_control-R-006 | sq_left | LimitSwitch_Compound | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_auto_reciprocating_control-R-007 | sq_right | LimitSwitch_Compound | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_auto_reciprocating_control-R-008 | motor_1 / sq_left / sq_right | Motor_ThreePhase_380V / LimitSwitch_Compound | 等待 | 5s | 条件确定：Position 初始 50，范围 0..100，速度 20；正向增加、反向减少，到边界分别触发 sq_left/sq_right 的虚拟限位。实际从哪个按钮进入正反向需项目负责人确认。 |  | RuntimeStateManager.MotionRuntimeState；SimulationEngine.UpdateAutoReciprocatingMotionDirection / IsVirtualLimitSwitchTriggered。 | 是 |
| motor_auto_reciprocating_control-R-009 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 电动机正反转控制电路（motor_forward_reverse_control）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_forward_reverse_control_template.json`
- 默认元件数/导线数：9 / 28

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_forward_reverse_control-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#2=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_forward_reverse_control-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_forward_reverse_control-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_forward_reverse_control-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_control-R-005 | forward_button_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_control-R-006 | reverse_button_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_control-R-007 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 按钮和接触器双重联锁正反转控制电路（motor_forward_reverse_double_interlock）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_forward_reverse_double_interlock_template.json`
- 默认元件数/导线数：9 / 34

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_forward_reverse_double_interlock-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#2=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_forward_reverse_double_interlock-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_forward_reverse_double_interlock-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_forward_reverse_double_interlock-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_double_interlock-R-005 | forward_button_1 | Button_Compound_Green_SB | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_double_interlock-R-006 | reverse_button_1 | Button_Compound_SB | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_double_interlock-R-007 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 电气互锁正反转控制电路（motor_forward_reverse_interlock）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_forward_reverse_interlock_template.json`
- 默认元件数/导线数：9 / 32

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_forward_reverse_interlock-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#2=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_forward_reverse_interlock-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_forward_reverse_interlock-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_forward_reverse_interlock-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_interlock-R-005 | forward_button_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_interlock-R-006 | reverse_button_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_forward_reverse_interlock-R-007 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 点动与连续运行混合控制电路（motor_jog_continuous）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_jog_continuous_template.json`
- 默认元件数/导线数：8 / 22

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_jog_continuous-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_jog_continuous-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_jog_continuous-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_jog_continuous-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_jog_continuous-R-005 | jog_1 | Button_Compound_Green_SB | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_jog_continuous-R-006 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_jog_continuous-R-007 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 电动机点动控制电路（motor_jog_control）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_jog_control_template.json`
- 默认元件数/导线数：7 / 17

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_jog_control-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilEnergized，线圈=是，主触点=是；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Forward，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_jog_control-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_jog_control-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_jog_control-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_jog_control-R-005 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_jog_control-R-006 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 电动机连续运行控制电路（motor_self_hold_control）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_self_hold_control_template.json`
- 默认元件数/导线数：7 / 19

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_self_hold_control-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_self_hold_control-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_self_hold_control-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_self_hold_control-R-004 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_self_hold_control-R-005 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_self_hold_control-R-006 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 两电机时间继电器顺序启动控制电路（motor_sequential_start_timer）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_sequential_start_timer_template.json`
- 默认元件数/导线数：10 / 33

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_sequential_start_timer-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#2=CoilOff，线圈=否，主触点=否；Timer_OnDelay_380V#1=CoilOff，Reset，延时到达=否；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；Motor_ThreePhase_380V#2=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_sequential_start_timer-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_sequential_start_timer-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_sequential_start_timer-R-004 | kt_1 | Timer_OnDelay_380V | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_sequential_start_timer-R-005 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_sequential_start_timer-R-006 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_sequential_start_timer-R-007 | kt_1 | Timer_OnDelay_380V | 等待 | 3s | 条件确定：线圈得电且等待不足 3s 时为 Timing；达到 3s 时为 Elapsed，15/18 闭合、15/16 断开；线圈失电即 Reset。 |  | SimulationEngine.UpdateOnDelayTimerRuntimeState / AddInternalConnections；模板参数 delaySeconds。 | 是 |
| motor_sequential_start_timer-R-008 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 星三角降压启动控制电路（motor_star_delta_start）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_star_delta_start_template.json`
- 默认元件数/导线数：11 / 40

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_star_delta_start-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#2=CoilOff，线圈=否，主触点=否；Contactor_KM_380V#3=CoilOff，线圈=否，主触点=否；Timer_OnDelay_380V#1=CoilOff，Reset，延时到达=否；模板默认状态未包含 SQ。；Motor_StarDelta_380V#1=Stopped，星三角=Unknown；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_star_delta_start-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_star_delta_start-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_star_delta_start-R-004 | kt_timer | Timer_OnDelay_380V | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_star_delta_start-R-005 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_star_delta_start-R-006 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_star_delta_start-R-007 | kt_timer | Timer_OnDelay_380V | 等待 | 3s | 条件确定：线圈得电且等待不足 3s 时为 Timing；达到 3s 时为 Elapsed，15/18 闭合、15/16 断开；线圈失电即 Reset。 |  | SimulationEngine.UpdateOnDelayTimerRuntimeState / AddInternalConnections；模板参数 delaySeconds。 | 是 |
| motor_star_delta_start-R-008 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 热继电器保护电动机控制电路（motor_thermal_protection）

- 分类：工业电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/motor_thermal_protection_template.json`
- 默认元件数/导线数：8 / 23

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| motor_thermal_protection-R-001 | Workspace |  | 点击 | - | Contactor_KM_380V#1=CoilOff，线圈=否，主触点=否；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；Motor_ThreePhase_380V#1=Stopped，星三角=；模板默认状态未包含灯泡或风扇。；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| motor_thermal_protection-R-002 | breaker_3p_1 | Breaker_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_thermal_protection-R-003 | fuse_3p_1 | Fuse_3P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_thermal_protection-R-004 | fr_1 | ThermalRelay_FR_380V | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| motor_thermal_protection-R-005 | stop_1 | Button_Stop_NC | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_thermal_protection-R-006 | start_1 | Button_Start_NO | 按下 / 释放 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerDown/OnPointerUp/SetMomentaryPressed。 | 是 |
| motor_thermal_protection-R-007 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 单开单控照明电路（single_lamp_template）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/single_lamp_template.json`
- 默认元件数/导线数：3 / 3

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| single_lamp_template-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Lamp_220V#1=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| single_lamp_template-R-002 | switch_1 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| single_lamp_template-R-003 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

## 双开分别控制双灯电路（two_switch_two_lamp）

- 分类：家庭电路
- 模板 JSON：`Assets/Resources/Blueprints/Templates/two_switch_two_lamp_template.json`
- 默认元件数/导线数：6 / 8

| 步骤 | 操作对象 | DefinitionName | 操作类型 | 等待 | 预期运行结果 | 检查助手预期 | 证据 | 需确认 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| two_switch_two_lamp-R-001 | Workspace |  | 点击 | - | 模板默认状态未包含 KM。；模板默认状态未包含 KT。；模板默认状态未包含 SQ。；模板默认状态未包含电机。；Lamp_220V#1=Off；Lamp_220V#2=Off；不适用。；Inspector 报告快照包含“参数估算” Section；具体数值未由快照字段结构化保存，需项目负责人确认。 | 结构化 Validation 基线：Error=0，Warning=0；RuleId=。 | TemplateStaticSnapshots.json + InspectorReportSnapshots.json；WorkspaceController.StartSimulation。 | 否 |
| two_switch_two_lamp-R-002 | breaker_1 | Breaker_2P | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| two_switch_two_lamp-R-003 | switch_1 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| two_switch_two_lamp-R-004 | switch_2 | Single_Control_Switch | 切换 | - |  | 局部开闭状态由 CircuitComponent 改变；负载、KM、KT、SQ 与电机的后续状态需项目负责人确认，当前快照只固定默认状态。 | CircuitComponent.OnPointerClick/Toggle。 | 是 |
| two_switch_two_lamp-R-005 | Workspace |  | 复位 | - | TimerRuntimeState 被 ResetAll 清理；不保留上一轮计时。；MotionRuntimeState 被 ResetAll 清理；重新创建时 Position=50、Direction=Stopped。 | 停止不改写模板 JSON；重新加载后回到模板保存的 isClosed 与参数。 | WorkspaceController.StopSimulation；SimulationEngine.ResetRuntimeState；RuntimeStateManager.ResetAll。 | 否 |

