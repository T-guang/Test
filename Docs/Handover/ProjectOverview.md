# 项目概览

## 产品定位

ElectricalSimulation2D 是面向电工教学的本地 2D 接线仿真应用。学习者可以搭建电路、加载标准图纸、运行受支持的仿真、检查接线风险，并阅读教学化报告。它服务于课堂演示与引导练习，不是生产级电气设计套件。

## 当前支持范围

- 家庭电路 8 张标准模板：单开、双控、空开、电能表、灯泡/风扇组合等。
- 工业电路 10 张标准模板：点动、自保持、热继保护、正反转、自动往返、顺序启动、星三角等。
- 元件放置、端子接线、撤销/重做、图纸保存/加载/导入、参数显示和模板练习。
- 已支持的运行态：接触器、热继电器、通电延时继电器 KT、电机方向、自动往返、星三角阶段。
- 具有稳定 RuleId/Severity 契约的规则校验和结构化检查助手报告。

## 当前稳定状态

V2.3.9.x 基线通过生产模板生成路径覆盖 18 张真实模板。架构基线比较 Inspector 报告的 UI Block 与结构化模型 Block。检查流程现由 UGUI 宿主 `LocalInspectorPanel` 和无 UGUI 依赖的 `InspectionWorkflowService` 协作完成。

## 明确不支持

- 本项目不是工程级 SPICE 求解器、PLC IDE、CAD/CAE 套件，也不是电气合规认证工具。
- 不覆盖全部电气元件、任意模拟瞬态、无界拓扑、云端账户或多人协作。
- 对不支持的拓扑必须明确提示，不能静默判定为安全。

## 主要入口

- 场景：`Assets/Scenes/Demo.unity`
- 标准模板：`Assets/Resources/Blueprints/Templates/`
- 模板目录：`Assets/Resources/Blueprints/Templates/template_catalog.json`
- 回归基线：`Assets/EditorTests/Baselines/V2.3.9.1/`
- 高风险边界：[KnownRisksAndDoNotTouch.md](KnownRisksAndDoNotTouch.md)
