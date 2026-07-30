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
            const int expectedValidateMethodCount = 104;
            var validationMethods = typeof(SpiceT3WorkspaceValidation)
                .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                .Where(method => method.Name.StartsWith("Validate", StringComparison.Ordinal))
                .ToArray();
            if (validationMethods.Length != expectedValidateMethodCount)
                throw new InvalidOperationException("Q1 测试拆分后的 Validate 方法数量不一致：" + validationMethods.Length);
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
