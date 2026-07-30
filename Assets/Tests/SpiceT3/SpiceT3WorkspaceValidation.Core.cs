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
        private static void ValidateElectricalRevisionAndRunningMutationGuards()
        {
            var canvasRoot = new GameObject("SpiceD1RevisionValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var initialRevision = workspace.ElectricalRevisionForTesting;
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 80f);
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 80f);
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 80f);
                var switchData = workspace.CreateComponent(SpiceComponentKind.IdealSwitch, Vector2.up * 80f);
                if (workspace.ElectricalRevisionForTesting <= initialRevision)
                    throw new InvalidOperationException("新增器件应递增电气修订号。");

                var revisionAfterCreate = workspace.ElectricalRevisionForTesting;
                workspace.MoveComponent(resistor.InstanceId, Vector2.right * 100f);
                workspace.RotateSelectedComponent();
                if (workspace.ElectricalRevisionForTesting != revisionAfterCreate)
                    throw new InvalidOperationException("移动或纯视觉旋转不应递增电气修订号。");

                if (!workspace.Connect(source.InstanceId, "positive", resistor.InstanceId, "positive"))
                    throw new InvalidOperationException("D1 验证无法创建导线。");
                if (workspace.ElectricalRevisionForTesting <= revisionAfterCreate)
                    throw new InvalidOperationException("新增导线应递增电气修订号。");

                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);
                var componentCount = workspace.Model.Components.Count;
                var wireCount = workspace.Model.Wires.Count;
                var revisionBeforeGuardedChanges = workspace.ElectricalRevisionForTesting;
                if (workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.zero) != null)
                    throw new InvalidOperationException("运行中不应允许新增器件。");
                if (workspace.Connect(source.InstanceId, "negative", ground.InstanceId, "ground"))
                    throw new InvalidOperationException("运行中不应允许新增导线。");
                if (workspace.TrySetParameter(resistor.InstanceId, 2d, "kOhm"))
                    throw new InvalidOperationException("运行中不应允许修改参数。");
                if (workspace.TrySetSwitchState(switchData.InstanceId, true))
                    throw new InvalidOperationException("运行中不应允许切换开关。");
                workspace.DeleteSelection();
                workspace.ClearWorkspace();
                if (workspace.Model.Components.Count != componentCount || workspace.Model.Wires.Count != wireCount)
                    throw new InvalidOperationException("运行中被拒绝的操作不应修改电气模型。");
                if (workspace.ElectricalRevisionForTesting != revisionBeforeGuardedChanges)
                    throw new InvalidOperationException("运行中被拒绝的操作不应递增电气修订号。");

                workspace.MoveComponent(resistor.InstanceId, Vector2.right * 120f);
                workspace.RotateSelectedComponent();
                if (workspace.ElectricalRevisionForTesting != revisionBeforeGuardedChanges)
                    throw new InvalidOperationException("运行中的移动或纯视觉旋转不应递增电气修订号。");

                if (!workspace.Model.TrySetParameter(resistor.InstanceId, 2000d))
                    throw new InvalidOperationException("D1 验证无法执行受控底层参数变更。");
                if (workspace.ElectricalRevisionForTesting <= revisionBeforeGuardedChanges)
                    throw new InvalidOperationException("绕过 UI 的模型变更仍应递增电气修订号。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }
    }
}
