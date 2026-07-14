using System.Collections.Generic;
using System.Text;
using ElectricalSim.Core;
using UnityEngine;

namespace ElectricalSim.AI
{
    /// <summary>
    /// 将当前活动 Workspace 的元件、导线、参数和已知显示状态整理为检查助手可消费的文本摘要。
    /// 只读取 Workspace.Components 与 WireManager.Wires，不执行规则判断、不推导电气状态，也不负责 UGUI 渲染；摘要文本不能作为真实电气状态的权威来源。
    /// 当前由非工业解释流程使用。修改后需回归检查助手与 Inspector 报告基线。
    /// </summary>
    public sealed class CircuitSummaryBuilder
    {
        private const int MaxListedItems = 16;
        private readonly WorkspaceController workspace;

        public CircuitSummaryBuilder(WorkspaceController workspace)
        {
            this.workspace = workspace;
        }

        public string BuildDetailedSummary()
        {
            return BuildSummaryInternal(16, 16);
        }

        public string BuildShortSummary()
        {
            return BuildSummaryInternal(3, 3);
        }

        private string BuildSummaryInternal(int maxComponents, int maxWires)
        {
            if (workspace == null)
            {
                return "当前画布为空，请先搭建或加载一个电路。";
            }

            var components = workspace.Components;
            var wires = workspace.WireManager != null ? workspace.WireManager.Wires : null;
            var componentCount = components != null ? components.Count : 0;
            var wireCount = wires != null ? wires.Count : 0;

            if (componentCount == 0)
            {
                return "当前画布为空，请先搭建或加载一个电路。";
            }

            var builder = new StringBuilder();
            builder.AppendLine("当前电路摘要：");
            builder.AppendLine("元件数量：" + componentCount);
            builder.AppendLine("导线数量：" + wireCount);
            builder.AppendLine();
            builder.AppendLine("元件：");

            for (var i = 0; i < componentCount && i < maxComponents; i++)
            {
                var component = components[i];
                if (component == null || component.Definition == null)
                {
                    continue;
                }

                builder.Append(i + 1)
                    .Append(". ")
                    .Append(component.Definition.displayName)
                    .Append(" | definitionName=")
                    .Append(component.Definition.name)
                    .Append(" | instanceId=")
                    .Append(component.InstanceId);

                if (component.Definition.togglable)
                {
                    builder.Append(" | 开关状态=").Append(component.IsClosed ? "闭合" : "断开");
                }

                builder.Append(" | 通电=").Append(component.IsEnergized ? "是" : "否");

                var parameters = FormatParameters(component.GetAllParameters());
                if (!string.IsNullOrEmpty(parameters))
                {
                    builder.Append(" | 参数：").Append(parameters);
                }

                builder.AppendLine();
            }

            if (componentCount > maxComponents)
            {
                builder.AppendLine("... 其余元件已省略。");
            }

            builder.AppendLine();
            builder.AppendLine("连接：");
            if (wireCount == 0)
            {
                builder.AppendLine("暂无导线连接。");
            }
            else
            {
                for (var i = 0; i < wireCount && i < maxWires; i++)
                {
                    var wire = wires[i];
                    if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
                    {
                        continue;
                    }

                    builder.Append(i + 1)
                        .Append(". ")
                        .Append(FormatTerminal(wire.StartTerminal))
                        .Append(" -> ")
                        .Append(FormatTerminal(wire.EndTerminal))
                        .AppendLine();
                }

                if (wireCount > maxWires)
                {
                    builder.AppendLine("... 其余导线已省略。");
                }
            }

            builder.AppendLine();
            builder.AppendLine("仿真状态：" + (workspace.IsSimulationRunning ? "正在仿真" : "未仿真"));
            if (workspace.SelectedComponent != null && workspace.SelectedComponent.Definition != null)
            {
                builder.AppendLine("当前选中元件：" + workspace.SelectedComponent.Definition.displayName + "（" + workspace.SelectedComponent.InstanceId + "）");
            }

            return builder.ToString().TrimEnd();
        }

        private static string FormatTerminal(TerminalView terminal)
        {
            if (terminal == null || terminal.Owner == null)
            {
                return "未知端子";
            }

            return terminal.Owner.InstanceId + "." + terminal.TerminalId;
        }

        private static string FormatParameters(IReadOnlyList<ComponentParameter> parameters)
        {
            if (parameters == null || parameters.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.key))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("，");
                }

                builder.Append(string.IsNullOrWhiteSpace(parameter.displayName) ? parameter.key : parameter.displayName)
                    .Append("=")
                    .Append(FormatNumber(parameter.value));

                if (!string.IsNullOrWhiteSpace(parameter.unit))
                {
                    builder.Append(parameter.unit);
                }
            }

            return builder.ToString();
        }

        private static string FormatNumber(float value)
        {
            return Mathf.Abs(value - Mathf.Round(value)) < 0.001f ? Mathf.RoundToInt(value).ToString() : value.ToString("0.###");
        }
    }
}
