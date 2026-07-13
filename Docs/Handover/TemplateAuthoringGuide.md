# 模板编写指南

## 数据来源

标准模板位于 `Assets/Resources/Blueprints/Templates/`，入口目录为 `template_catalog.json`。每张模板 JSON 都是由生产 `CircuitTemplateSpawnService` 消费的数据 DTO，不是场景快照。

当前基线覆盖 18 张模板：家庭 8 张、工业 10 张。V2.3.9.1 基线工具会明确校验这一数量。新增或下线标准模板时，应在评审后同时更新目录、基线数量约束与交接文档。

## JSON 基本内容

一张模板通常包含：

- 稳定模板 ID 和展示元数据；
- 元件实例：稳定 `instanceId`、`definitionId`、位置和受支持参数；
- 端子到端子的导线连接；
- 分类、难度等目录元数据。

必须使用真实元件目录中的 `DefinitionId`，不能以显示文字充当业务标识。端子 ID 必须与定义完全一致，不能为了写 JSON 方便而自造缩写。

## 编写步骤

1. 查询既有目录定义和端子 ID。
2. 使用稳定实例 ID 和明确导线端点编写 JSON。
3. 添加目录项和 Resources 路径。
4. 经由正常模板 UI 或生产 Spawn 服务加载。
5. 验证元件/导线数量、Analyzer、校验 RuleId/Severity、Inspector 段落和预期运行态。
6. 上述通过后才将其加入真实模板基线范围。

## 不得用场景对象代替模板

不得从 `Demo.unity` 复制对象来制作标准模板。Demo 含有历史/静态对象，不是规范电路图。模板必须走学习者实际使用的 JSON -> 目录 -> `CircuitTemplateSpawnService` 路径验证。

## 更新模板数量约束

若有意调整支持模板集合：

1. 记录产品范围决策；
2. 更新目录和测试；
3. 仅在审阅新行为后生成候选快照；
4. 审查全部变化的 RuleId、Severity、报告 Block 和运行态证据；
5. 将 JSON 和基线变更作为明确命名、经过审阅的一次提交。

已有模板意外失败时，不得直接重新生成 expected。
