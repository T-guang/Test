# 负向规则端点搜索结果

搜索方式：每次只在内存模板副本中删除一条既有 W-xxx 或新增一条端子导线，并连续运行 3 次 CircuitStateAnalyzer + CircuitValidationService。未写回模板 JSON。

| RuleId | 结果 | 模板 | 修改 | 删除 W-xxx（原端点） | 新增端点 | 建议线色 | 前置状态 | 实际 RuleIds | 额外 Issue | 适合人工测试 | 证据 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `BREAKER_OR_FUSE_BYPASSED` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。；自动测试证据：Assets/Scripts/Editor/ProtectionBypassValidationTests.cs |
| `MOTOR_CONTACTOR_BYPASSED` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。；自动测试证据：Assets/Scripts/Editor/ProtectionBypassValidationTests.cs |
| `REVERSING_INTERLOCK_MISSING` | 连续 3 次稳定 | motor_auto_reciprocating_control | 删除既有导线 | W-028：km_reverse.22 → km_forward.A1 | - -> - | - | 标准模板静态接线；无需运行态操作 | REVERSING_INTERLOCK_MISSING | 无 | 是 | 内存 Template DTO + CircuitStateAnalyzer + CircuitValidationService |
| `THERMAL_RELAY_MAIN_CIRCUIT_BYPASSED` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。；自动测试证据：Assets/Scripts/Editor/ThermalTimerBypassValidationTests.cs |
| `THERMAL_RELAY_CONTROL_BYPASSED` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。；自动测试证据：Assets/Scripts/Editor/ThermalTimerBypassValidationTests.cs |
| `TIMER_CONTROL_BYPASSED` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。；自动测试证据：Assets/Scripts/Editor/ThermalTimerBypassValidationTests.cs |
| `STOP_BUTTON_BYPASSED` | 连续 3 次稳定 | motor_forward_reverse_interlock | 控制线圈直接旁路 | - | power_3p_1.L1 -> km_forward.A1 | #E74C3C | 标准模板静态接线；无需运行态操作 | STOP_BUTTON_BYPASSED | 无 | 是 | 内存 Template DTO + CircuitStateAnalyzer + CircuitValidationService |
| `SELF_HOLDING_BRANCH_INCOMPLETE` | 连续 3 次稳定 | motor_self_hold_control | 删除既有导线 | W-018：stop_1.12 → km_1.13 | - -> - | - | 标准模板静态接线；无需运行态操作 | SELF_HOLDING_BRANCH_INCOMPLETE | 无 | 是 | 内存 Template DTO + CircuitStateAnalyzer + CircuitValidationService |
| `REVERSING_CONTACTOR_CONFLICT` | NOT SUITABLE FOR MANUAL TEMPLATE TEST | - | - | - | - | - | - | - | 否 | NOT SUITABLE FOR MANUAL TEMPLATE TEST：当前 18 张标准模板的单步内存扰动未在连续 3 次验证中稳定触发该 RuleId；保留现有 Editor 自动测试证据。；自动测试证据：Assets/Scripts/Editor/TopologySafetyTests.cs |
