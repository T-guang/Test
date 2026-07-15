# 源码清理候选记录

本文件只记录候选和证据，不代表可删除。任何清理都必须单独立项，并先确认场景/Prefab、Resources、反射、菜单和运行时创建引用。

| 候选 | 证据 | 风险 | 当前建议 |
|---|---|---|---|
| `Assets/Scripts/Editor/Diagnostics/PhaseA_Auditor.cs` | 当前为未跟踪 Editor 诊断脚本；名称只表达阶段，不表达领域。 | Low | 先确认是否仍有菜单入口和报告消费者；保留，后续改名或归档需单独提交。 |
| `Assets/Editor/Diagnostics/UiTypographyRuntimeAuditWindow.cs` 与 `Assets/Scripts/Diagnostics/UiTypographyRuntimeAuditRunner.cs` | 一者为 Editor 菜单，一者为 Play Mode Runner；属于成对诊断工具。 | Medium | 不是重复代码，需在诊断文档中明确生命周期；不得把 Runner 接入正式启动流程。 |
| `Assets/Editor/Diagnostics/EditRuntimeUiComparisonWindow.cs`、`SimulationUiFontComparisonWindow.cs`、`FinalUiAuditWindow.cs` | 多个 UI 审计窗口，输出目录和使用阶段不同。 | Medium | 暂不合并；先在 EditorDiagnosticsGuide 中梳理职责与输出，再评估重复。 |
| `Assets/Scripts/Platform/NativeFileBrowser.cs`、`NativeFileBrowserReceiver.cs`、`WindowsFileDialog.cs` | 平台文件对话框包装层存在多个名称相近入口。 | Medium | 可能服务不同回调/平台路径；先做调用链和 Windows 实测，禁止删除。 |
| `Assets/Scripts/Practice/PracticeConnectionChecker.cs` 与 `Assets/Scripts/Practice/Netlist/PracticeConnectionChecker.cs` | 同名类位于不同命名空间，分别可能负责流程层和网表层。 | High | 高度容易误读，但不能据此判重复；需单独做练习模式调用图。 |
| `Assets/Scripts/Rules/CircuitRuleChecker.cs` 与 `Core/Validation/CircuitValidationService.cs` | 两套规则入口并存：教学/旧规则检查与生产 Validation。 | High | 这是架构边界，而非可删重复；先以 RuleSystemGuide 扩充为目标，禁止合并。 |
| `Assets/Scripts/UI/LoginController.cs`、`CurrentUserView.cs` | 当前产品定位已转为系统信息页，但仍保留登录/当前用户脚本。 | Medium | 可能由旧场景或兼容流程引用；先检查 Demo 和 Build Settings，不删除。 |
| `Assets/Scripts/Editor/DemoSceneBuilder.cs` | 2043 行 Editor 场景构建器，和已序列化 Demo 场景并存。 | High | 可能是场景重建来源；改动或清理前必须先复查场景运行态取证。 |
| 根目录 `gpt/`、`gpt.zip`、`temp_demo*.cs`、`Reports/` | 工作区导出、副本、临时测试或报告，不在 Assets 维护范围。 | Low | 不纳入正式提交；是否删除由用户决定，本轮不处理。 |

## TODO / FIXME / HACK / TEMP 结果

生产脚本未发现活动的 `TODO`、`FIXME`、`HACK`、`TEMP`、`以后删除` 或 `待处理` 标记。`TopologySafetyTests.cs:142` 的“临时调低阈值”是 Editor-only 负向测试设置，仍有效；不得复制到生产逻辑。
