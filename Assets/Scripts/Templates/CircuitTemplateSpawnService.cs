using System.Collections.Generic;
using ElectricalSim.Core;
using UnityEngine;

namespace ElectricalSim.Templates
{
    /// <summary>
    /// 校验模板 DTO，并使用元件目录定义将其生成到当前活动工作区。
    /// 先确认元件定义、instanceId、端子引用、导线颜色和折线路径，再清空并替换工作区；不把场景内样例对象当作模板数据。
    /// 本类不负责模板选择 UI 或用户图纸导入。生成完成后的 Workspace.Components 与 WireManager.Wires 才是活动电路权威输入。
    /// 真实模板基线依赖这条生产生成路径，修改后必须回归 18 张模板与 Template Integrity。
    /// </summary>
    public static class CircuitTemplateSpawnService
    {
        private sealed class TemplateValidationResult
        {
            public readonly Dictionary<string, ComponentDefinition> DefinitionsByInstanceId = new Dictionary<string, ComponentDefinition>();
        }

        public static bool Spawn(
            CircuitTemplateDto template,
            WorkspaceController workspace,
            IReadOnlyList<ComponentDefinition> catalog,
            out string message)
        {
            // 必须先校验再 ClearDrawing，避免格式错误的 JSON 清掉学习者当前电路。
            message = string.Empty;

            if (workspace == null)
            {
                message = "模板加载失败：未绑定工作区。";
                return false;
            }

            if (!ValidateTemplate(template, catalog, out var validation, out message))
            {
                workspace.SetStatus(message);
                return false;
            }

            // 校验通过后才允许记录历史并清空旧画布；后续生成失败会返回错误，但当前实现不宣称对已生成对象执行完整事务回滚。
            workspace.RecordHistoryCheckpoint();
            workspace.ClearDrawing(false);

            var spawned = new Dictionary<string, CircuitComponent>();
            // 先按模板 instanceId 生成元件并恢复静态状态与参数，导线只能在端子实例已存在后连接。
            foreach (var item in template.components)
            {
                var definition = validation.DefinitionsByInstanceId[item.instanceId];
                var component = workspace.SpawnComponent(definition, new Vector2(item.x, item.y), item.instanceId, false);
                if (component == null)
                {
                    message = "模板元件生成失败：" + item.instanceId;
                    workspace.SetStatus(message);
                    return false;
                }

                component.SetClosed(item.isClosed);
                component.SetParameters(item.parameters);
                spawned[item.instanceId] = component;
            }

            // 端子引用已在 ValidateTemplate 中对应到定义端子；这里仅把模板端点映射到本次实际生成的元件实例。
            foreach (var item in template.wires)
            {
                var startComponent = spawned[item.startComponentId];
                var endComponent = spawned[item.endComponentId];
                var start = startComponent.GetTerminal(item.startTerminalId);
                var end = endComponent.GetTerminal(item.endTerminalId);

                if (!TryParseColor(item.color, out var wireColor))
                {
                    message = "模板导线颜色无效：" + item.color;
                    workspace.SetStatus(message);
                    return false;
                }

                var wire = workspace.WireManager.CreateWire(start, end, wireColor, ParseStyle(item.style));
                if (wire == null)
                {
                    message = $"导线连接失败：{item.startComponentId}.{item.startTerminalId} -> {item.endComponentId}.{item.endTerminalId}";
                    workspace.SetStatus(message);
                    return false;
                }

                if (item.manualRoutePoints != null && item.manualRoutePoints.Count >= 2)
                {
                    wire.SetManualRoutePoints(item.manualRoutePoints);
                }
                else if (!Mathf.Approximately(item.manualRouteAxis, 0f))
                {
                    wire.SetManualRoute(item.manualRouteHorizontal, item.manualRouteAxis);
                }
            }

            workspace.WireManager.RefreshAll();
            workspace.MarkTopologyDirty();
            message = "已加载模板：" + template.templateName;
            workspace.SetStatus(message);
            return true;
        }

        private static bool ValidateTemplate(
            CircuitTemplateDto template,
            IReadOnlyList<ComponentDefinition> catalog,
            out TemplateValidationResult result,
            out string error)
        {
            // 该校验只阻止不完整 DTO 进入清空和生成阶段，不负责修复模板，也不替代运行后的 Analyzer 或 Validation 检查。
            result = null;
            error = null;

            if (template == null)
            {
                error = "模板为空。";
                return false;
            }

            if (template.components == null || template.components.Count == 0)
            {
                error = "模板 components 为空。";
                return false;
            }

            if (template.wires == null)
            {
                error = "模板 wires 为空。";
                return false;
            }

            if (catalog == null || catalog.Count == 0)
            {
                error = "模板加载失败：元件库为空。";
                return false;
            }

            result = new TemplateValidationResult();
            var instanceIds = new HashSet<string>();
            foreach (var component in template.components)
            {
                if (component == null)
                {
                    error = "模板 components 中存在空元件。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(component.instanceId))
                {
                    error = "模板中存在空元件 ID。";
                    return false;
                }

                if (!instanceIds.Add(component.instanceId))
                {
                    error = "模板中存在重复元件 ID：" + component.instanceId;
                    return false;
                }

                var definition = FindDefinition(catalog, component.definitionName);
                if (definition == null)
                {
                    error = "找不到元件定义：" + component.definitionName;
                    return false;
                }

                result.DefinitionsByInstanceId[component.instanceId] = definition;
            }

            foreach (var wire in template.wires)
            {
                if (wire == null)
                {
                    error = "模板 wires 中存在空导线。";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(wire.startComponentId) || string.IsNullOrWhiteSpace(wire.endComponentId))
                {
                    error = "模板导线引用的组件 ID 为空。";
                    return false;
                }

                if (!result.DefinitionsByInstanceId.TryGetValue(wire.startComponentId, out var startDefinition))
                {
                    error = "模板导线引用了不存在的起点组件：" + wire.startComponentId;
                    return false;
                }

                if (!result.DefinitionsByInstanceId.TryGetValue(wire.endComponentId, out var endDefinition))
                {
                    error = "模板导线引用了不存在的终点组件：" + wire.endComponentId;
                    return false;
                }

                if (!HasTerminal(startDefinition, wire.startTerminalId))
                {
                    error = $"找不到组件 {wire.startComponentId} 的端子：{wire.startTerminalId}";
                    return false;
                }

                if (!HasTerminal(endDefinition, wire.endTerminalId))
                {
                    error = $"找不到组件 {wire.endComponentId} 的端子：{wire.endTerminalId}";
                    return false;
                }

                if (!TryParseColor(wire.color, out _))
                {
                    error = "模板导线颜色无效：" + wire.color;
                    return false;
                }

                if (!ValidateManualRoutePoints(wire.manualRoutePoints, out var routeError))
                {
                    error = $"导线手动路径无效：{wire.startComponentId}.{wire.startTerminalId} -> {wire.endComponentId}.{wire.endTerminalId}，{routeError}";
                    return false;
                }
            }

            return true;
        }

        private static ComponentDefinition FindDefinition(IReadOnlyList<ComponentDefinition> catalog, string definitionName)
        {
            if (string.IsNullOrWhiteSpace(definitionName))
            {
                return null;
            }

            for (var i = 0; i < catalog.Count; i++)
            {
                var item = catalog[i];
                if (item != null && item.name == definitionName)
                {
                    return item;
                }
            }

            return null;
        }

        private static bool HasTerminal(ComponentDefinition definition, string terminalId)
        {
            if (definition == null || definition.terminals == null || string.IsNullOrWhiteSpace(terminalId))
            {
                return false;
            }

            for (var i = 0; i < definition.terminals.Count; i++)
            {
                if (definition.terminals[i] != null && definition.terminals[i].id == terminalId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidateManualRoutePoints(IReadOnlyList<Vector2> points, out string error)
        {
            error = null;
            if (points == null || points.Count == 0)
            {
                return true;
            }

            if (points.Count < 2)
            {
                error = "manualRoutePoints 至少需要 2 个点。";
                return false;
            }

            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (!IsFinite(point.x) || !IsFinite(point.y))
                {
                    error = "manualRoutePoints 存在非法坐标。";
                    return false;
                }

                if (i > 0)
                {
                    var previous = points[i - 1];
                    if (!Mathf.Approximately(previous.x, point.x) && !Mathf.Approximately(previous.y, point.y))
                    {
                        // Legacy six-point routes persisted terminal-exit positions. Their
                        // first or last segment can be slightly diagonal after terminal
                        // layout changes; WireView rebuilds this legacy format on load.
                        if (points.Count == 6 && (i == 1 || i == points.Count - 1))
                        {
                            continue;
                        }

                        error = "manualRoutePoints 必须为水平/垂直折线。";
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static WireStyle ParseStyle(string style)
        {
            if (!string.IsNullOrWhiteSpace(style) && System.Enum.TryParse(style, out WireStyle parsed))
            {
                return parsed;
            }

            return WireStyle.Orthogonal;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                color = default;
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "blue":
                    color = new Color(0.1f, 0.35f, 0.95f);
                    return true;
                case "green":
                    color = new Color(0.08f, 0.65f, 0.25f);
                    return true;
                case "yellow":
                    color = new Color(0.95f, 0.78f, 0.12f);
                    return true;
                case "red":
                    color = new Color(0.95f, 0.12f, 0.12f);
                    return true;
            }

            return ColorUtility.TryParseHtmlString(value.StartsWith("#") ? value : "#" + value, out color);
        }
    }
}
