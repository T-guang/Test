# 测试与发布清单

## 构建门禁

在项目根目录执行：

```powershell
dotnet build Assembly-CSharp.csproj --no-restore
dotnet build Assembly-CSharp-Editor.csproj --no-restore
```

已有且无关的警告应单独记录；本次引入的新警告或错误属于发布阻塞项。

## 基线门禁

在 Unity 中运行：

1. `Tools/Tests/运行 Inspector 报告模型测试`
2. `Tools/Tests/验证架构重构基线`
3. 与变更相关的既有规则/安全测试菜单：通用电源安全、拓扑安全与负向扰动、保护旁路与互锁缺失、热继主回路与时间继电器旁路。

架构基线通过真实模板路径运行 18 张模板，并比较模板结构、规则结果、Inspector UI Block 和模型 Block。验证绝不能写入 expected；失败必须调查源代码，不能通过重新生成基线解决。

## 运行态与场景取证

涉及场景或活动图的修改，需在 Play Mode 采集 V2.3.9.1 五阶段证据：初始默认示例、清空画布、加载家庭模板、加载工业模板、保存并重新导入用户图纸。

记录场景元件/端子/导线、工作区元件/导线、Analyzer/Validation 输入数量、问题数和 Console Error，以区分历史场景对象与真实运行态污染。

## 页面生命周期门禁

修改页面创建或路由后，循环切换六个顶部页面 20 次，记录 Controller、Canvas、EventSystem 和页面根对象数量，并检查 Console Error。该诊断不能通过反射修改 Button listener。

## 人工产品检查

- 开始/停止仿真、接线、撤销/重做、保存、加载模板、导入图纸。
- 家庭与工业模板加载。
- 正常家庭与工业模板的 Inspector 解释和检查报告。
- 受影响时检查 KT、热继、星三角和自动往返。
- 规则/报告变更时检查一个 Warning 和一个 Error 案例。
- PC 构建和发布前 Console Error 复查。

## 发布原则

发布负责人完成最终人工签字。工具只能提供证据，不能在未人工测试时声称业务流程通过。除非明确要求，生成报告和临时导出不得进入正式提交。
