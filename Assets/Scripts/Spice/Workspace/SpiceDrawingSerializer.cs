using System;
using System.Collections.Generic;
using System.Globalization;
using ElectricalSim.Spice.Core;
using UnityEngine;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 图纸文件格式标识与版本。
    /// </summary>
    public static class SpiceDrawingFormat
    {
        public const string Format = "ElectricalSimulation2D.SpiceDrawing";
        public const int SchemaVersion = 1;
    }

    /// <summary>
    /// SPICE 图纸文件根 DTO。JsonUtility 序列化时不支持字典和多态，
    /// 因此组件类型和路由模式以字符串保存，转换层负责与枚举互转。
    /// </summary>
    [Serializable]
    public sealed class SpiceDrawingFileDto
    {
        public string format;
        public int schemaVersion;
        public List<SpiceComponentDto> components = new List<SpiceComponentDto>();
        public List<SpiceWireDto> wires = new List<SpiceWireDto>();
    }

    /// <summary>
    /// 单个 SPICE 组件记录。instanceId 在文件内唯一；componentType 对应 SpiceComponentKind 枚举名。
    /// siValueText 以 invariant-culture 字符串保存数值，用于区分"字段缺失"（null/空）和数值 0。
    /// </summary>
    [Serializable]
    public sealed class SpiceComponentDto
    {
        public string instanceId;
        public string componentType;
        public SpiceVector2Dto position;
        public int rotationQuarterTurns;
        public string siValueText;
    }

    /// <summary>
    /// 单条 SPICE 导线记录。端点通过 InstanceId + TerminalId 引用组件。
    /// </summary>
    [Serializable]
    public sealed class SpiceWireDto
    {
        public string startComponentId;
        public string startTerminalId;
        public string endComponentId;
        public string endTerminalId;
        public string routeMode;
        public List<SpiceVector2Dto> manualRoutePoints = new List<SpiceVector2Dto>();
    }

    /// <summary>
    /// JsonUtility 不支持 Vector2 的列表序列化，因此使用独立的可序列化结构。
    /// </summary>
    [Serializable]
    public struct SpiceVector2Dto
    {
        public float x;
        public float y;
    }

    /// <summary>
    /// SPICE 图纸序列化、反序列化和临时模型构建的纯数据转换器。
    /// 本类不接触 UI、文件系统或当前工作区；所有方法均为纯函数，
    /// 导入失败时返回 false 且不产生任何副作用。
    /// </summary>
    public static class SpiceDrawingSerializer
    {
        /// <summary>
        /// 将工作区模型转换为 DTO。不包含结果、网表、诊断、选择或视图状态。
        /// 组件和导线按稳定顺序（InstanceId / 端点字典序）排序，确保 Wire 添加方向或列表顺序变化不影响输出。
        /// </summary>
        public static SpiceDrawingFileDto ToDto(SpiceWorkspaceModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var dto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion
            };

            var sortedComponents = new List<SpiceWorkspaceComponentData>(model.Components);
            sortedComponents.Sort((a, b) => string.Compare(a.InstanceId, b.InstanceId, StringComparison.Ordinal));

            foreach (var component in sortedComponents)
            {
                var componentDto = new SpiceComponentDto
                {
                    instanceId = component.InstanceId,
                    componentType = component.Kind.ToString(),
                    position = new SpiceVector2Dto { x = component.Position.x, y = component.Position.y },
                    rotationQuarterTurns = component.RotationQuarterTurns
                };
                // 只有有用户参数的器件才保存参数；无参数器件不保存 siValueText。
                if (SpiceWorkspaceModel.HasUserParameter(component.Kind))
                {
                    componentDto.siValueText = component.SiValue.ToString("R", CultureInfo.InvariantCulture);
                }
                dto.components.Add(componentDto);
            }

            var sortedWires = new List<SpiceWorkspaceWireData>(model.Wires);
            sortedWires.Sort(CompareWiresForStableOrder);

            foreach (var wire in sortedWires)
            {
                // 规范化方向：字典序较小的端点固定为 start，确保 Wire 方向不影响 JSON。
                var swapped = NormalizeWireDirection(wire, out var startComponentId, out var startTerminalId, out var endComponentId, out var endTerminalId);

                var wireDto = new SpiceWireDto
                {
                    startComponentId = startComponentId,
                    startTerminalId = startTerminalId,
                    endComponentId = endComponentId,
                    endTerminalId = endTerminalId,
                    routeMode = wire.VisualState.RouteMode.ToString()
                };
                if (wire.VisualState.RouteMode == SpiceWireRouteMode.Manual)
                {
                    var waypoints = wire.VisualState.Waypoints;
                    // 如果端点被交换，折点必须倒序输出，保持折点序列与端点方向一致。
                    for (var i = 0; i < waypoints.Count; i++)
                    {
                        var point = swapped ? waypoints[waypoints.Count - 1 - i] : waypoints[i];
                        wireDto.manualRoutePoints.Add(new SpiceVector2Dto { x = point.x, y = point.y });
                    }
                }
                dto.wires.Add(wireDto);
            }

            return dto;
        }

        /// <summary>
        /// 规范化导线方向：比较两端 (componentId, terminalId) 字典序，较小者作为 start。
        /// 返回 true 表示发生了端点交换。
        /// </summary>
        private static bool NormalizeWireDirection(SpiceWorkspaceWireData wire,
            out string startComponentId, out string startTerminalId,
            out string endComponentId, out string endTerminalId)
        {
            var cmp = string.Compare(wire.StartComponentId, wire.EndComponentId, StringComparison.Ordinal);
            if (cmp == 0) cmp = string.Compare(wire.StartTerminalId, wire.EndTerminalId, StringComparison.Ordinal);
            if (cmp <= 0)
            {
                startComponentId = wire.StartComponentId;
                startTerminalId = wire.StartTerminalId;
                endComponentId = wire.EndComponentId;
                endTerminalId = wire.EndTerminalId;
                return false;
            }
            startComponentId = wire.EndComponentId;
            startTerminalId = wire.EndTerminalId;
            endComponentId = wire.StartComponentId;
            endTerminalId = wire.StartTerminalId;
            return true;
        }

        /// <summary>
        /// 导线稳定排序：按规范化后的 (startComponentId, startTerminalId, endComponentId, endTerminalId) 字典序。
        /// </summary>
        private static int CompareWiresForStableOrder(SpiceWorkspaceWireData a, SpiceWorkspaceWireData b)
        {
            NormalizeWireDirection(a, out var aStartComp, out var aStartTerm, out var aEndComp, out var aEndTerm);
            NormalizeWireDirection(b, out var bStartComp, out var bStartTerm, out var bEndComp, out var bEndTerm);

            var cmp = string.Compare(aStartComp, bStartComp, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            cmp = string.Compare(aStartTerm, bStartTerm, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            cmp = string.Compare(aEndComp, bEndComp, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return string.Compare(aEndTerm, bEndTerm, StringComparison.Ordinal);
        }

        /// <summary>
        /// 将工作区模型序列化为 JSON 字符串。输出是确定性的：组件按 InstanceId 排序，导线按端点字典序排序。
        /// </summary>
        public static string ToJson(SpiceWorkspaceModel model)
        {
            var dto = ToDto(model);
            return JsonUtility.ToJson(dto, true);
        }

        /// <summary>
        /// 从 JSON 字符串解析并构建临时工作区模型。任何解析或校验失败时返回 false 且不产生部分模型。
        /// 成功时，临时模型的编号分配器已通过 RestoreInstanceNumbersFromExisting 恢复。
        /// </summary>
        public static bool TryFromJson(string json, out SpiceWorkspaceModel model, out string error)
        {
            model = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "图纸内容为空。";
                return false;
            }

            SpiceDrawingFileDto dto;
            try
            {
                dto = JsonUtility.FromJson<SpiceDrawingFileDto>(json);
            }
            catch (Exception exception)
            {
                error = "JSON 解析失败：" + exception.Message;
                return false;
            }

            return TryFromDto(dto, out model, out error);
        }

        /// <summary>
        /// 从 DTO 构建临时工作区模型并执行完整校验。校验通过后才返回已构建的模型。
        /// 不修改任何外部工作区；调用方负责在验证成功后原子替换当前工作区。
        /// </summary>
        public static bool TryFromDto(SpiceDrawingFileDto dto, out SpiceWorkspaceModel model, out string error)
        {
            model = null;
            error = null;

            if (dto == null)
            {
                error = "图纸 DTO 为空。";
                return false;
            }

            if (!string.Equals(dto.format, SpiceDrawingFormat.Format, StringComparison.Ordinal))
            {
                error = "未知文件格式：" + (dto.format ?? "(null)");
                return false;
            }

            if (dto.schemaVersion != SpiceDrawingFormat.SchemaVersion)
            {
                error = "不支持的 schemaVersion：" + dto.schemaVersion + "，当前支持 " + SpiceDrawingFormat.SchemaVersion;
                return false;
            }

            if (dto.components == null) dto.components = new List<SpiceComponentDto>();
            if (dto.wires == null) dto.wires = new List<SpiceWireDto>();

            var tempModel = new SpiceWorkspaceModel();
            var instanceIdSet = new HashSet<string>(StringComparer.Ordinal);

            // 第一阶段：组件校验和构建
            foreach (var componentDto in dto.components)
            {
                if (componentDto == null || string.IsNullOrWhiteSpace(componentDto.instanceId))
                {
                    error = "图纸中存在无效组件（instanceId 为空）。";
                    return false;
                }

                if (!instanceIdSet.Add(componentDto.instanceId))
                {
                    error = "重复组件 InstanceId：" + componentDto.instanceId;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(componentDto.componentType) ||
                    !Enum.TryParse<SpiceComponentKind>(componentDto.componentType, out var kind) ||
                    !Enum.IsDefined(typeof(SpiceComponentKind), kind))
                {
                    error = "未知器件类型：" + (componentDto.componentType ?? "(null)");
                    return false;
                }

                if (!SpiceWorkspaceModel.IsValidInstanceId(kind, componentDto.instanceId))
                {
                    error = "InstanceId 不符合器件类型规范：" + componentDto.instanceId + "（期望前缀 " + SpiceWorkspaceModel.GetExpectedPrefix(kind) + "-NNN）";
                    return false;
                }

                // 有参数器件必须提供 siValueText；无参数器件不保存参数。
                double siValue = 0d;
                if (SpiceWorkspaceModel.HasUserParameter(kind))
                {
                    if (string.IsNullOrWhiteSpace(componentDto.siValueText))
                    {
                        error = "有参数器件缺少 siValueText：" + componentDto.instanceId;
                        return false;
                    }
                    if (!double.TryParse(componentDto.siValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out siValue))
                    {
                        error = "参数值文本无法解析为数值：" + componentDto.instanceId + " = " + componentDto.siValueText;
                        return false;
                    }
                    if (double.IsNaN(siValue) || double.IsInfinity(siValue))
                    {
                        error = "参数值非法（NaN 或 Infinity）：" + componentDto.instanceId;
                        return false;
                    }
                    if (!SpiceWorkspaceModel.IsValidParameter(kind, siValue))
                    {
                        error = "参数值不在合法范围：" + componentDto.instanceId + " = " + siValue.ToString(CultureInfo.InvariantCulture);
                        return false;
                    }
                }

                if (float.IsNaN(componentDto.position.x) || float.IsInfinity(componentDto.position.x) ||
                    float.IsNaN(componentDto.position.y) || float.IsInfinity(componentDto.position.y))
                {
                    error = "位置坐标非法（NaN 或 Infinity）：" + componentDto.instanceId;
                    return false;
                }

                if (componentDto.rotationQuarterTurns < 0 || componentDto.rotationQuarterTurns > 3)
                {
                    error = "旋转值超出范围（0-3）：" + componentDto.instanceId + " = " + componentDto.rotationQuarterTurns;
                    return false;
                }

                try
                {
                    tempModel.AddComponentWithIdentity(
                        kind,
                        componentDto.instanceId,
                        new Vector2(componentDto.position.x, componentDto.position.y),
                        siValue,
                        componentDto.rotationQuarterTurns);
                }
                catch (InvalidOperationException exception)
                {
                    error = exception.Message;
                    return false;
                }
            }

            // 第二阶段：导线校验和构建
            foreach (var wireDto in dto.wires)
            {
                if (wireDto == null ||
                    string.IsNullOrWhiteSpace(wireDto.startComponentId) ||
                    string.IsNullOrWhiteSpace(wireDto.endComponentId) ||
                    string.IsNullOrWhiteSpace(wireDto.startTerminalId) ||
                    string.IsNullOrWhiteSpace(wireDto.endTerminalId))
                {
                    error = "图纸中存在无效导线（端点引用为空）。";
                    return false;
                }

                var startComponent = tempModel.FindComponent(wireDto.startComponentId);
                if (startComponent == null)
                {
                    error = "导线引用了不存在的起始组件：" + wireDto.startComponentId;
                    return false;
                }

                var endComponent = tempModel.FindComponent(wireDto.endComponentId);
                if (endComponent == null)
                {
                    error = "导线引用了不存在的结束组件：" + wireDto.endComponentId;
                    return false;
                }

                if (!startComponent.HasTerminal(wireDto.startTerminalId))
                {
                    error = "起始组件 " + wireDto.startComponentId + " 缺少端子 " + wireDto.startTerminalId;
                    return false;
                }

                if (!endComponent.HasTerminal(wireDto.endTerminalId))
                {
                    error = "结束组件 " + wireDto.endComponentId + " 缺少端子 " + wireDto.endTerminalId;
                    return false;
                }

                if (string.Equals(wireDto.startComponentId, wireDto.endComponentId, StringComparison.Ordinal))
                {
                    error = "导线两端不能指向同一组件：" + wireDto.startComponentId;
                    return false;
                }

                SpiceWireVisualState visualState;
                if (string.IsNullOrWhiteSpace(wireDto.routeMode))
                {
                    error = "导线路由模式为空。";
                    return false;
                }

                if (string.Equals(wireDto.routeMode, SpiceWireRouteMode.Auto.ToString(), StringComparison.Ordinal))
                {
                    visualState = SpiceWireVisualState.Auto();
                }
                else if (string.Equals(wireDto.routeMode, SpiceWireRouteMode.Manual.ToString(), StringComparison.Ordinal))
                {
                    var waypoints = new List<Vector2>();
                    if (wireDto.manualRoutePoints != null)
                    {
                        foreach (var point in wireDto.manualRoutePoints)
                        {
                            if (float.IsNaN(point.x) || float.IsInfinity(point.x) ||
                                float.IsNaN(point.y) || float.IsInfinity(point.y))
                            {
                                error = "导线折点坐标非法（NaN 或 Infinity）。";
                                return false;
                            }
                            waypoints.Add(new Vector2(point.x, point.y));
                        }
                    }
                    if (waypoints.Count == 0)
                    {
                        error = "手工路由导线缺少折点。";
                        return false;
                    }
                    visualState = SpiceWireVisualState.Manual(waypoints);
                }
                else
                {
                    error = "未知路由模式：" + wireDto.routeMode;
                    return false;
                }

                if (!tempModel.AddWire(wireDto.startComponentId, wireDto.startTerminalId,
                    wireDto.endComponentId, wireDto.endTerminalId, visualState))
                {
                    error = "无法建立导线：" + wireDto.startComponentId + ":" + wireDto.startTerminalId +
                            " -> " + wireDto.endComponentId + ":" + wireDto.endTerminalId;
                    return false;
                }
            }

            // 第三阶段：恢复编号分配器
            tempModel.RestoreInstanceNumbersFromExisting();

            model = tempModel;
            return true;
        }
    }
}
