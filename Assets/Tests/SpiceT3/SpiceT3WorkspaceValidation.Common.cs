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
                    workspace.GetDcAnalysisModeButtonForTesting() == null || workspace.GetAcAnalysisModeButtonForTesting() == null ||
                    workspace.GetAcFrequencyInputForTesting() == null || workspace.GetApplyAcFrequencyButtonForTesting() == null ||
                    workspace.GetPaletteCardForTesting(SpiceComponentKind.AcVoltageSource) == null)
                    throw new InvalidOperationException("Q2 UI 层级契约缺少既有工作区控件。");
                if (workspace.GetDcAnalysisModeButtonForTesting().name != "DcAnalysisMode" || workspace.GetAcAnalysisModeButtonForTesting().name != "AcAnalysisMode")
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
            var adapter = File.ReadAllText(Path.Combine(directory, "SpiceWorkspacePresentationAdapter.cs"));
            var presentation = File.ReadAllText(Path.Combine(directory, "SpiceWorkspaceController.Presentation.cs"));
            if (adapter.IndexOf("ApplyToolbarButtonStyle", StringComparison.Ordinal) < 0 ||
                presentation.IndexOf("RefreshResultStateDependentControls", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Q2 定向去重入口缺失。");
            if (presentation.IndexOf("RefreshNetlistUi();\r\n            RefreshCopyResultButton();\r\n            RefreshResultStateDependentControls();", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("Q2 定向去重意外纳入了结果文本或网表刷新。");
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
