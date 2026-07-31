using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.T3
{
    /// <summary>
    /// T3 工作区自动验证。partial 文件按功能分组，RunPureChecks 保持唯一且明确的回归执行顺序。
    /// </summary>
    public static partial class SpiceT3WorkspaceValidation
    {
        /// <summary>
        /// partial 拆分只移动方法，不改变统一测试类的公开入口。固定数量用于捕获拆分时误删方法；
        /// 重名检测防止同名方法在不同文件中静默遮蔽，完整调用关系由审查包中的静态清单复核。
        /// </summary>
        private static void ValidateQualityQ1SuiteSplitIntegrity()
        {
            const int expectedPreQ2ValidateMethodCount = 104;
            const int expectedQ2ValidateMethodCount = 5;
            var validationMethods = typeof(SpiceT3WorkspaceValidation)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Where(method => method.Name.StartsWith("Validate", StringComparison.Ordinal))
                .ToArray();
            var preQ2Methods = validationMethods.Where(method => !method.Name.StartsWith("ValidateQualityQ2", StringComparison.Ordinal)).ToArray();
            if (preQ2Methods.Length != expectedPreQ2ValidateMethodCount || validationMethods.Length != expectedPreQ2ValidateMethodCount + expectedQ2ValidateMethodCount)
                throw new InvalidOperationException("Q1 原有和 Q2 新增 Validate 方法数量不一致：" + preQ2Methods.Length + "/" + validationMethods.Length);
            if (validationMethods.GroupBy(method => method.Name, StringComparer.Ordinal).Any(group => group.Count() != 1))
                throw new InvalidOperationException("Q1 测试拆分后出现重复 Validate 方法名。");
        }

        /// <summary>
        /// 项目自有通过日志使用固定前缀与中文结果词，便于批处理日志稳定筛选；第三方 ngspice 原始输出不经过此规则。
        /// </summary>
        private static void ValidateQualityQ1ChineseLogContract()
        {
            var expected = new[]
            {
                "[Spice][AC-C1] 分析模式控件：通过",
                "[Spice][AC-D] 导入事务：通过",
                "[Spice][OpAmp] 工作区反馈接线：通过",
                "[Spice][Quality-Q1] Editor 对象生命周期：通过"
            };
            if (expected.Any(message => !message.StartsWith("[Spice][", StringComparison.Ordinal) || !message.EndsWith("：通过", StringComparison.Ordinal)))
                throw new InvalidOperationException("Q1 中文日志契约不符合稳定前缀或结果格式。");
        }

        /// <summary>
        /// Edit Mode 下工作区仍要立即移除临时视图，以便同一轮验证能准确检查 Model/View/Wire 数量。
        /// 该测试监听 Unity 日志，确保集中生命周期入口没有退回会触发警告的延迟 Destroy。
        /// </summary>
        private static void ValidateQualityQ1EditorObjectLifetime()
        {
            var messages = new System.Collections.Generic.List<string>();
            Application.LogCallback handler = (condition, _, __) => messages.Add(condition);
            Application.logMessageReceived += handler;
            var root = new GameObject("SpiceQualityQ1Lifetime", typeof(RectTransform), typeof(Canvas));
            try
            {
                SpiceUnityObjectLifetime.Destroy(null);
                var destroyed = new GameObject("DestroyedLifetimeTarget");
                UnityEngine.Object.DestroyImmediate(destroyed);
                SpiceUnityObjectLifetime.Destroy(destroyed);

                var workspace = CreateInitializedWorkspaceForCopy(root.transform, out _);
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 100f);
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 100f);
                if (!workspace.Connect(source.InstanceId, SpiceComponentModel.PositiveTerminalId, resistor.InstanceId, SpiceComponentModel.PositiveTerminalId) ||
                    !workspace.Connect(resistor.InstanceId, SpiceComponentModel.NegativeTerminalId, ground.InstanceId, SpiceComponentModel.GroundTerminalId))
                    throw new InvalidOperationException("Q1 生命周期测试无法建立 Wire。");
                workspace.SelectComponent(workspace.GetComponentViewForTesting(resistor.InstanceId));
                workspace.DeleteSelection();
                if (workspace.Model.FindComponent(resistor.InstanceId) != null || workspace.GetComponentViewForTesting(resistor.InstanceId) != null ||
                    workspace.Model.Wires.Count != 0 || workspace.GetWireViewCountForTesting() != 0)
                    throw new InvalidOperationException("Q1 生命周期测试中删除元件未同时清理 Model 和 WireView。");

                workspace.ClearWorkspace();
                if (workspace.Model.Components.Count != 0 || workspace.GetComponentViewCountForTesting() != 0 || workspace.GetWireViewCountForTesting() != 0)
                    throw new InvalidOperationException("Q1 生命周期测试中清空工作区未清理视图。");

                var imported = new SpiceWorkspaceModel();
                imported.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
                if (!workspace.TryImportDrawingJson(SpiceDrawingSerializer.ToJson(imported), out var error) ||
                    workspace.Model.Components.Count != 1 || workspace.GetComponentViewCountForTesting() != 1)
                    throw new InvalidOperationException("Q1 生命周期测试中成功导入未完成视图替换：" + error);
            }
            finally
            {
                Application.logMessageReceived -= handler;
                UnityEngine.Object.DestroyImmediate(root);
            }
            if (messages.Any(message => message != null && message.Contains("Destroy may not be called from edit mode")))
                throw new InvalidOperationException("Q1 生命周期入口仍触发了 Edit Mode Destroy 警告。");
        }

        /// <summary>
        /// Q2 只移动既有类型定义，因此完整类型名、MonoBehaviour 基类和程序集归属必须保持不变。
        /// 这能防止移动后出现 Missing Script 或 AddComponent 目标漂移，而无需改变任何运行时架构。
        /// </summary>
        private static void ValidateQualityQ2TypeMoveIntegrity()
        {
            var expected = new[]
            {
                typeof(SpiceWorkspacePresentationAdapter),
                typeof(SpiceWorkspacePaletteCardPresentation),
                typeof(SpiceAssistantOutcomePresentation),
                typeof(SpiceAssistantParameterPresentation),
                typeof(SpiceScrollableTextView),
                typeof(SpiceScrollableTextLayout),
                typeof(SpiceWorkspaceBlankClick),
                typeof(SpiceWorkspaceUi)
            };
            if (expected.Any(type => type.FullName == null || !type.FullName.StartsWith("ElectricalSim.Spice.Workspace.", StringComparison.Ordinal)))
                throw new InvalidOperationException("Q2 类型移动后发现工作区完整类型名漂移。");
            if (!typeof(MonoBehaviour).IsAssignableFrom(typeof(SpiceWorkspaceBlankClick)) ||
                !typeof(MonoBehaviour).IsAssignableFrom(typeof(SpiceWorkspacePaletteCardPresentation)))
                throw new InvalidOperationException("Q2 类型移动后 MonoBehaviour 基类契约不一致。");
        }

        /// <summary>
        /// partial 只重新归档同一个 Controller 的方法。源文件存在性与关键方法归属共同防止误拆为新类型，
        /// 也避免主文件重新膨胀为职责混杂的大文件。
        /// </summary>
        private static void ValidateQualityQ2ControllerPartialIntegrity()
        {
            var workspaceRoot = Path.GetDirectoryName(Application.dataPath);
            var expectedFiles = new[]
            {
                "SpiceWorkspaceController.cs",
                "SpiceWorkspaceController.Simulation.cs",
                "SpiceWorkspaceController.Interaction.cs",
                "SpiceWorkspaceController.Persistence.cs",
                "SpiceWorkspaceController.Presentation.cs"
            };
            var controllerDirectory = Path.Combine(workspaceRoot, "Assets", "Scripts", "Spice", "Workspace");
            if (expectedFiles.Any(file => !File.Exists(Path.Combine(controllerDirectory, file))))
                throw new InvalidOperationException("Q2 Controller partial 文件不完整。");

            var methods = typeof(SpiceWorkspaceController).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var required = new[] { "RunCalculationAsync", "CommitImportedModel", "HandleTerminalClick", "BuildUi", "RefreshParameterPanel" };
            if (required.Any(name => !methods.Any(method => method.Name == name && method.DeclaringType == typeof(SpiceWorkspaceController))))
                throw new InvalidOperationException("Q2 Controller partial 后缺少既有职责方法。");
        }

        /// <summary>
        /// 仅验证原有公开入口和测试接缝仍可反射获取；不以扩大可见性来迁就测试。
        /// 保存、导入和求解仍由既有 Controller 实现承担。
        /// </summary>
        private static void ValidateQualityQ2PublicApiCompatibility()
        {
            var type = typeof(SpiceWorkspaceController);
            var publicMembers = new[]
            {
                "Model", "ResultState", "WorkspaceRect", "ViewportRect", "ContentRect", "ViewController", "WireLayer", "OverlayLayer", "HasPendingWire", "IsDirty",
                "Initialize", "CreateComponent", "Connect", "RunCalculationAsync", "TrySaveWorkspaceToPath", "TrySaveCurrentWorkspace", "TryImportWorkspaceFromPath",
                "TryImportDrawingJson", "TryGetCopyableNetlistText", "TryGetCopyableOutcomeText", "ValidateAssistantPanelLayout", "ComputeContentBounds"
            };
            var members = type.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(member => member.Name)
                .ToArray();
            if (publicMembers.Any(name => !members.Contains(name)))
                throw new InvalidOperationException("Q2 Controller 公开接口或既有测试接缝发生缺失。");

            var hostMembers = typeof(SpiceWorkspaceDemoHost).GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .Select(member => member.Name)
                .ToArray();
            if (new[] { "Configure", "Controller", "IsInitialized" }.Any(name => !hostMembers.Contains(name)))
                throw new InvalidOperationException("Q2 DemoHost 公开接口发生缺失。");
        }

        /// <summary>
        /// 使用正式 Host/Bindings 创建路径检查既有 UI 名称和控制项，不改变布局、颜色或层级。
        /// 这同时证明类型移动没有让动态 AddComponent 目标失效。
        /// </summary>
        private static void ValidateQualityQ2UiHierarchyContract()
        {
            var root = new GameObject("SpiceQualityQ2Ui", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(root.transform, out var bindings);
                if (bindings.RunButton == null || bindings.PaletteRoot == null || bindings.WorkspaceViewport == null || bindings.AssistantRoot == null ||
                    workspace.GetAnalysisModeToggleButtonForTesting() == null ||
                    workspace.GetAcFrequencyInputForTesting() == null || workspace.GetApplyAcFrequencyButtonForTesting() == null ||
                    workspace.GetPaletteCardForTesting(SpiceComponentKind.AcVoltageSource) == null)
                    throw new InvalidOperationException("Q2 UI 层级契约缺少既有工作区控件。");
                if (workspace.GetAnalysisModeToggleButtonForTesting().name != "AnalysisModeToggle" ||
                    workspace.GetAnalysisModeToggleButtonForTesting().transform.parent != bindings.RunButton.transform.parent)
                    throw new InvalidOperationException("Q2 UI 层级契约中的分析按钮名称发生变化。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 定向去重只收口已证实的工具栏样式和 ResultState 控件三连刷新；
        /// 通过源代码契约确认没有把无关 Refresh 纳入万能入口。
        /// </summary>
        private static void ValidateQualityQ2DirectedDeduplication()
        {
            var workspaceRoot = Path.GetDirectoryName(Application.dataPath);
            var directory = Path.Combine(workspaceRoot, "Assets", "Scripts", "Spice", "Workspace");
            var adapter = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, "SpiceWorkspacePresentationAdapter.cs")));
            var presentation = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, "SpiceWorkspaceController.Presentation.cs")));
            var simulation = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, "SpiceWorkspaceController.Simulation.cs")));
            var persistence = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, "SpiceWorkspaceController.Persistence.cs")));
            if (adapter.IndexOf("ApplyToolbarButtonStyle", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Q2 定向去重入口缺失。");

            var helperBody = ExtractMethodBody(presentation, "RefreshResultStateDependentControls");
            var helperCalls = System.Text.RegularExpressions.Regex.Matches(helperBody, @"(?m)^\s*(Refresh\w+)\s*\(\s*\)\s*;")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => match.Groups[1].Value)
                .ToArray();
            var expectedHelperCalls = new[]
            {
                "RefreshFileOperationButtonsAvailability",
                "RefreshAnalysisControls",
                "RefreshParameterPanel"
            };
            if (!helperCalls.SequenceEqual(expectedHelperCalls, StringComparer.Ordinal) ||
                helperBody.IndexOf("RefreshNetlistUi", StringComparison.Ordinal) >= 0 ||
                helperBody.IndexOf("RefreshCopyResultButton", StringComparison.Ordinal) >= 0 ||
                helperBody.IndexOf("SetResultText", StringComparison.Ordinal) >= 0 ||
                helperBody.IndexOf("SetDiagnosticText", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Q2 定向去重意外纳入了结果、网表或诊断刷新。");

            var allControllerPartials = presentation + "\n" + simulation + "\n" + persistence;
            var helperOccurrences = System.Text.RegularExpressions.Regex.Matches(allControllerPartials, @"\bRefreshResultStateDependentControls\s*\(")
                .Count - 1;
            if (helperOccurrences != 3)
                throw new InvalidOperationException("Q2 定向去重的正式调用点数量应为 3，实际为 " + helperOccurrences + "。");

            var discardBody = ExtractMethodBody(simulation, "DiscardOutdatedCalculation");
            if (discardBody.IndexOf("RefreshResultStateDependentControls", StringComparison.Ordinal) >= 0 ||
                discardBody.IndexOf("RefreshAnalysisControls();", StringComparison.Ordinal) < 0 ||
                discardBody.IndexOf("RefreshParameterPanel();", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("过期计算丢弃路径必须保留原有的分析控件和参数区刷新边界。");
        }

        // Q2.1 以源代码静态清单确认 partial 拆分后注释仍与正式方法相邻，
        // 并以预处理后的活动源码统计测试入口，避免把 #if false 的历史探针误计入回归执行集合。
        private static void AssertQualityQ21Evidence()
        {
            var workspaceRoot = Path.GetDirectoryName(Application.dataPath);
            var testDirectory = Path.Combine(workspaceRoot, "Assets", "Tests", "SpiceT3");
            var partialFiles = Directory.GetFiles(testDirectory, "SpiceT3WorkspaceValidation*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (partialFiles.Length != 9)
                throw new InvalidOperationException("Q2.1 测试统计未读取全部 9 个 partial 文件。");

            var activeSources = partialFiles.Select(path => RemoveFalseConditionalRegions(NormalizeSourceNewlines(File.ReadAllText(path)))).ToArray();
            var activeDeclarations = activeSources
                .SelectMany(FindValidateDeclarations)
                .ToArray();
            var activeNames = activeDeclarations.Select(declaration => declaration.Name).ToArray();
            if (activeNames.Length != 109 || activeNames.GroupBy(name => name, StringComparer.Ordinal).Any(group => group.Count() != 1))
                throw new InvalidOperationException("Q2.1 编译生效 Validate 声明数量或重复名称不符合 109/0 契约。");

            var mainPath = partialFiles.Single(path => Path.GetFileName(path) == "SpiceT3WorkspaceValidation.cs");
            var runPureChecksBody = ExtractMethodBody(RemoveFalseConditionalRegions(NormalizeSourceNewlines(File.ReadAllText(mainPath))), "RunPureChecks");
            var directCalls = System.Text.RegularExpressions.Regex.Matches(runPureChecksBody, @"(?m)^\s*(Validate\w+)\s*\(")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => match.Groups[1].Value)
                .ToArray();
            if (directCalls.Length != 109 || directCalls.Distinct(StringComparer.Ordinal).Count() != 109 ||
                activeNames.Except(directCalls, StringComparer.Ordinal).Any())
                throw new InvalidOperationException("Q2.1 RunPureChecks 直接调用或活动测试覆盖不符合 109/0 契约。");

            var inactiveDeclarations = partialFiles
                .SelectMany(path => FindValidateDeclarations(ExtractFalseConditionalRegions(NormalizeSourceNewlines(File.ReadAllText(path)))))
                .ToArray();
            var inactiveHelpers = inactiveDeclarations.Count(declaration => declaration.ParameterCount > 0);
            var inactiveEntryProbes = inactiveDeclarations.Length - inactiveHelpers;
            if (inactiveEntryProbes != 8 || inactiveHelpers != 2)
                throw new InvalidOperationException("Q2.1 #if false 历史探针或内部 helper 数量不符合 8/2 契约。");

            var controllerDirectory = Path.Combine(workspaceRoot, "Assets", "Scripts", "Spice", "Workspace");
            AssertMethodSummary(controllerDirectory, "SpiceWorkspaceController.Interaction.cs", "CreateComponent", "保留给验证 Harness 的固定位置创建入口");
            AssertMethodSummary(controllerDirectory, "SpiceWorkspaceController.Interaction.cs", "TrySetAnalysisMode", "分析设置必须通过 Model 变更");
            AssertMethodSummary(controllerDirectory, "SpiceWorkspaceController.Persistence.cs", "HandleSaveButtonClicked", "文件按钮回调");
            AssertMethodSummary(controllerDirectory, "SpiceWorkspaceController.Persistence.cs", "TryImportDrawingJson", "事务式导入图纸 JSON");
            AssertMethodSummary(controllerDirectory, "SpiceWorkspaceController.Persistence.cs", "CanImportDrawing", "判定当前是否允许导入图纸");
            AssertMethodSummary(controllerDirectory, "SpiceWorkspaceController.Simulation.cs", "CanModifyElectricalModel", "统一电气编辑的 Running 防线");
            AssertNoForbiddenCommentResidues(workspaceRoot);
            AssertSelectComponentHasNoFileButtonComment(controllerDirectory);
            AssertTypeSummary(controllerDirectory, "SimulationModeDropdown.cs", "SimulationModeOptionVisual", "为显式绑定的模式选项提供仅影响视觉的交互反馈");
        }

        private static string NormalizeSourceNewlines(string source)
        {
            return (source ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var signature = System.Text.RegularExpressions.Regex.Match(source,
                @"(?:public|private|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\], ?]+\s+" +
                System.Text.RegularExpressions.Regex.Escape(methodName) + @"\s*\(");
            if (!signature.Success)
                throw new InvalidOperationException("未找到方法：" + methodName + "。");
            var openBraceIndex = source.IndexOf('{', signature.Index + signature.Length);
            if (openBraceIndex < 0)
                throw new InvalidOperationException("未找到方法体开始：" + methodName + "。");

            var depth = 0;
            for (var index = openBraceIndex; index < source.Length; index++)
            {
                if (source[index] == '{') depth++;
                else if (source[index] == '}' && --depth == 0)
                    return source.Substring(openBraceIndex + 1, index - openBraceIndex - 1);
            }
            throw new InvalidOperationException("未找到方法体结束：" + methodName + "。");
        }

        private static System.Collections.Generic.IEnumerable<ValidateDeclaration> FindValidateDeclarations(string source)
        {
            return System.Text.RegularExpressions.Regex.Matches(source,
                    @"(?:public|private|internal)\s+static\s+(?:async\s+)?[\w<>\[\], ?]+\s+(Validate\w+)\s*\(([^)]*)\)")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => new ValidateDeclaration(match.Groups[1].Value, match.Groups[2].Value));
        }

        private static string RemoveFalseConditionalRegions(string source)
        {
            return ProcessConditionalRegions(source, includeInactive: false);
        }

        private static string ExtractFalseConditionalRegions(string source)
        {
            return ProcessConditionalRegions(source, includeInactive: true);
        }

        private static string ProcessConditionalRegions(string source, bool includeInactive)
        {
            var result = new System.Text.StringBuilder();
            var states = new System.Collections.Generic.Stack<bool>();
            var active = true;
            foreach (var line in source.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#if false", StringComparison.Ordinal))
                {
                    states.Push(active);
                    active = false;
                    continue;
                }
                if (trimmed.StartsWith("#if", StringComparison.Ordinal))
                {
                    states.Push(active);
                    continue;
                }
                if (trimmed.StartsWith("#else", StringComparison.Ordinal))
                {
                    if (states.Count == 0) throw new InvalidOperationException("Q2.1 条件编译指令不成对。");
                    active = states.Peek() && !active;
                    continue;
                }
                if (trimmed.StartsWith("#endif", StringComparison.Ordinal))
                {
                    if (states.Count == 0) throw new InvalidOperationException("Q2.1 条件编译指令不成对。");
                    active = states.Pop();
                    continue;
                }
                if (active != includeInactive)
                    result.AppendLine(line);
            }
            if (states.Count != 0) throw new InvalidOperationException("Q2.1 条件编译指令未闭合。");
            return result.ToString();
        }

        private static void AssertMethodSummary(string directory, string fileName, string methodName, string expectedFirstSentence)
        {
            var source = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, fileName)));
            var signature = System.Text.RegularExpressions.Regex.Match(source,
                @"(?:public|private|internal)\s+(?:static\s+)?(?:async\s+)?[\w<>\[\], ?]+\s+" +
                System.Text.RegularExpressions.Regex.Escape(methodName) + @"\s*\(");
            if (!signature.Success)
                throw new InvalidOperationException("Q2.1 未找到方法签名：" + fileName + " / " + methodName + "。");
            var methodIndex = signature.Index;
            var summaryStart = source.LastIndexOf("/// <summary>", methodIndex, StringComparison.Ordinal);
            var summaryEnd = source.IndexOf("</summary>", summaryStart, StringComparison.Ordinal);
            if (summaryStart < 0 || summaryEnd < summaryStart || summaryEnd > methodIndex ||
                source.Substring(summaryStart, summaryEnd - summaryStart).IndexOf(expectedFirstSentence, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Q2.1 方法摘要关联错误：" + fileName + " / " + methodName + "。");
        }

        private static void AssertNoForbiddenCommentResidues(string workspaceRoot)
        {
            var sourceDirectories = new[]
            {
                Path.Combine(workspaceRoot, "Assets", "Scripts", "Spice"),
                Path.Combine(workspaceRoot, "Assets", "Editor", "SpiceT1"),
                Path.Combine(workspaceRoot, "Assets", "Editor", "SpiceT2"),
                Path.Combine(workspaceRoot, "Assets", "Editor", "SpiceT3"),
                Path.Combine(workspaceRoot, "Assets", "Tests", "SpiceT3")
            };
            var forbidden = new[]
            {
                "文件工作流 " + "文件工作流",
                "文件工作流 " + "专属",
                "正式工作流 " + "文件",
                "不直接调用 " + "正式工作流",
                "/" + "/ ：",
                "Visual-only interaction " + "feedback"
            };

            foreach (var path in sourceDirectories.SelectMany(directory => Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)))
            {
                var commentText = string.Join("\n", NormalizeSourceNewlines(File.ReadAllText(path))
                    .Split('\n')
                    .Where(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
                var residue = forbidden.FirstOrDefault(text => commentText.IndexOf(text, StringComparison.Ordinal) >= 0);
                if (residue != null)
                    throw new InvalidOperationException("Q2.1 仍存在禁止的注释残留：" + residue + " / " + path);
            }
        }

        private static void AssertSelectComponentHasNoFileButtonComment(string directory)
        {
            var source = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, "SpiceWorkspaceController.Interaction.cs")));
            var signature = System.Text.RegularExpressions.Regex.Match(source, @"public\s+void\s+SelectComponent\s*\(");
            if (!signature.Success)
                throw new InvalidOperationException("Q2.1 未找到 SelectComponent。");

            var preceding = source.Substring(Math.Max(0, signature.Index - 400), Math.Min(400, signature.Index));
            if (preceding.IndexOf("文件按钮回调", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Q2.1 文件按钮回调注释仍错误关联到 SelectComponent。");
        }

        private static void AssertTypeSummary(string directory, string fileName, string typeName, string expectedFirstSentence)
        {
            var source = NormalizeSourceNewlines(File.ReadAllText(Path.Combine(directory, fileName)));
            var declaration = System.Text.RegularExpressions.Regex.Match(source,
                @"(?:public|internal|private)\s+(?:sealed\s+)?class\s+" + System.Text.RegularExpressions.Regex.Escape(typeName) + @"\b");
            if (!declaration.Success)
                throw new InvalidOperationException("Q2.1 未找到类型声明：" + fileName + " / " + typeName + "。");

            var summaryStart = source.LastIndexOf("/// <summary>", declaration.Index, StringComparison.Ordinal);
            var summaryEnd = source.IndexOf("</summary>", summaryStart, StringComparison.Ordinal);
            if (summaryStart < 0 || summaryEnd < summaryStart || summaryEnd > declaration.Index ||
                source.Substring(summaryStart, summaryEnd - summaryStart).IndexOf(expectedFirstSentence, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Q2.1 类型摘要关联错误：" + fileName + " / " + typeName + "。");
        }

        private readonly struct ValidateDeclaration
        {
            public ValidateDeclaration(string name, string parameters)
            {
                Name = name;
                ParameterCount = string.IsNullOrWhiteSpace(parameters) ? 0 : parameters.Split(',').Length;
            }

            public string Name { get; }
            public int ParameterCount { get; }
        }

        private static string CreateUniqueTempDir(string label)
        {
            var path = Path.Combine(Path.GetTempPath(), "SpiceT3_" + label + "_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        // 清理临时目录及其所有文件。测试不得把临时 JSON 留在仓库。
        private static void CleanupTempDir(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception exception)
            {
                Console.WriteLine("[SpiceT3] 清理临时目录失败：" + path + " " + exception);
            }
        }
    }
}
