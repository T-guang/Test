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
        private static void ValidateDrawingImportCountLimits()
        {
            var acceptedComponents = CreateDrawingLimitDto();
            for (var i = 1; i <= SpiceDrawingLimits.MaxComponents; i++)
                acceptedComponents.components.Add(CreateLimitComponent("resistor-" + i.ToString("D3")));
            if (!SpiceDrawingSerializer.TryFromDto(acceptedComponents, out var acceptedComponentModel, out var componentError) ||
                acceptedComponentModel.Components.Count != SpiceDrawingLimits.MaxComponents)
                throw new InvalidOperationException("组件数量上限应被接受：" + componentError);

            acceptedComponents.components.Add(CreateLimitComponent("resistor-" + (SpiceDrawingLimits.MaxComponents + 1).ToString("D3")));
            if (SpiceDrawingSerializer.TryFromDto(acceptedComponents, out _, out var componentOverflowError) ||
                string.IsNullOrEmpty(componentOverflowError) || !componentOverflowError.Contains("器件数量"))
                throw new InvalidOperationException("组件数量超过上限时应被拒绝。");

            var acceptedWires = CreateDrawingLimitDtoWithEndpoints();
            for (var i = 0; i < SpiceDrawingLimits.MaxWires; i++)
                acceptedWires.wires.Add(CreateUniqueLimitWire(i));
            if (!SpiceDrawingSerializer.TryFromDto(acceptedWires, out var acceptedWireModel, out var wireError) ||
                acceptedWireModel.Wires.Count != SpiceDrawingLimits.MaxWires)
                throw new InvalidOperationException("导线数量上限应被接受：" + wireError);

            acceptedWires.wires.Add(CreateUniqueLimitWire(0));
            if (SpiceDrawingSerializer.TryFromDto(acceptedWires, out _, out var wireOverflowError) ||
                string.IsNullOrEmpty(wireOverflowError) || !wireOverflowError.Contains("导线数量"))
                throw new InvalidOperationException("导线数量超过上限时应被拒绝。");
        }

        private static void ValidateDrawingImportWaypointLimits()
        {
            var perWireAccepted = CreateDrawingLimitDtoWithEndpoints();
            perWireAccepted.wires.Add(CreateLimitWire(SpiceWireRouteMode.Manual, SpiceDrawingLimits.MaxManualRoutePointsPerWire));
            if (!SpiceDrawingSerializer.TryFromDto(perWireAccepted, out _, out var acceptedError))
                throw new InvalidOperationException("单条导线折点上限应被接受：" + acceptedError);

            var perWireRejected = CreateDrawingLimitDtoWithEndpoints();
            perWireRejected.wires.Add(CreateLimitWire(SpiceWireRouteMode.Manual, SpiceDrawingLimits.MaxManualRoutePointsPerWire + 1));
            if (SpiceDrawingSerializer.TryFromDto(perWireRejected, out _, out var perWireError) ||
                string.IsNullOrEmpty(perWireError) || !perWireError.Contains("过多手工折点"))
                throw new InvalidOperationException("单条导线折点超过上限时应被拒绝。");

            var totalAccepted = CreateDrawingLimitDtoWithEndpoints();
            AddLimitWaypointsAcrossWires(totalAccepted, SpiceDrawingLimits.MaxTotalManualRoutePoints);
            if (!SpiceDrawingSerializer.TryFromDto(totalAccepted, out _, out var totalAcceptedError))
                throw new InvalidOperationException("全局折点总数上限应被接受：" + totalAcceptedError);

            var totalRejected = CreateDrawingLimitDtoWithEndpoints();
            AddLimitWaypointsAcrossWires(totalRejected, SpiceDrawingLimits.MaxTotalManualRoutePoints + 1);
            if (SpiceDrawingSerializer.TryFromDto(totalRejected, out _, out var totalError) ||
                string.IsNullOrEmpty(totalError) || !totalError.Contains("折点总数"))
                throw new InvalidOperationException("全局折点总数超过上限时应被拒绝。");
        }

        private static void ValidateDrawingImportCoordinateLimits()
        {
            var boundary = CreateDrawingLimitDto();
            boundary.components.Add(CreateLimitComponent("resistor-001",
                SpiceDrawingLimits.MaxCoordinateMagnitude.ToString(System.Globalization.CultureInfo.InvariantCulture),
                (-SpiceDrawingLimits.MaxCoordinateMagnitude).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (!SpiceDrawingSerializer.TryFromDto(boundary, out _, out var boundaryError))
                throw new InvalidOperationException("坐标正负边界与零值应被接受：" + boundaryError);

            foreach (var invalidCoordinate in new[]
            {
                (SpiceDrawingLimits.MaxCoordinateMagnitude + 1f).ToString(System.Globalization.CultureInfo.InvariantCulture),
                (-SpiceDrawingLimits.MaxCoordinateMagnitude - 1f).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "NaN",
                "Infinity",
                "3e38"
            })
            {
                var rejected = CreateDrawingLimitDto();
                rejected.components.Add(CreateLimitComponent("resistor-001", invalidCoordinate, "0"));
                if (SpiceDrawingSerializer.TryFromDto(rejected, out _, out _))
                    throw new InvalidOperationException("非法或越界组件坐标应被拒绝：" + invalidCoordinate);
            }

            var waypointBoundary = CreateDrawingLimitDtoWithEndpoints();
            var boundaryWire = CreateLimitWire(SpiceWireRouteMode.Manual, 1);
            boundaryWire.manualRoutePoints[0].x = SpiceDrawingLimits.MaxCoordinateMagnitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
            boundaryWire.manualRoutePoints[0].y = (-SpiceDrawingLimits.MaxCoordinateMagnitude).ToString(System.Globalization.CultureInfo.InvariantCulture);
            waypointBoundary.wires.Add(boundaryWire);
            if (!SpiceDrawingSerializer.TryFromDto(waypointBoundary, out _, out var waypointBoundaryError))
                throw new InvalidOperationException("折点坐标边界应被接受：" + waypointBoundaryError);

            var waypointRejected = CreateDrawingLimitDtoWithEndpoints();
            var rejectedWire = CreateLimitWire(SpiceWireRouteMode.Manual, 1);
            rejectedWire.manualRoutePoints[0].x = "3e38";
            waypointRejected.wires.Add(rejectedWire);
            if (SpiceDrawingSerializer.TryFromDto(waypointRejected, out _, out _))
                throw new InvalidOperationException("极端有限折点坐标应被拒绝。");
        }

        private static void ValidateDrawingImportStringLimits()
        {
            var suffixLength = SpiceDrawingLimits.MaxInstanceIdLength - "resistor-".Length;
            var maximumInstanceId = "resistor-" + new string('0', suffixLength - 1) + "1";
            var maximum = CreateDrawingLimitDto();
            maximum.components.Add(CreateLimitComponent(maximumInstanceId));
            if (!SpiceDrawingSerializer.TryFromDto(maximum, out _, out var maximumError))
                throw new InvalidOperationException("最大长度的规范 InstanceId 应被接受：" + maximumError);

            var oversized = CreateDrawingLimitDto();
            oversized.components.Add(CreateLimitComponent(maximumInstanceId + "0"));
            if (SpiceDrawingSerializer.TryFromDto(oversized, out _, out var oversizedError) ||
                string.IsNullOrEmpty(oversizedError) || !oversizedError.Contains("过长"))
                throw new InvalidOperationException("超长 InstanceId 应被拒绝。");

            var oversizedTerminal = CreateDrawingLimitDtoWithEndpoints();
            var wire = CreateLimitWire(SpiceWireRouteMode.Auto, 0);
            wire.startTerminalId = new string('t', SpiceDrawingLimits.MaxTerminalIdLength + 1);
            oversizedTerminal.wires.Add(wire);
            if (SpiceDrawingSerializer.TryFromDto(oversizedTerminal, out _, out var terminalError) ||
                string.IsNullOrEmpty(terminalError) || !terminalError.Contains("过长"))
                throw new InvalidOperationException("超长 TerminalId 应在端子语义校验前被拒绝。");

            var extremeParameter = CreateDrawingLimitDto();
            var component = CreateLimitComponent("source-001");
            component.componentType = SpiceComponentKind.DcVoltageSource.ToString();
            component.siValueText = "1e30";
            extremeParameter.components.Add(component);
            if (SpiceDrawingSerializer.TryFromDto(extremeParameter, out _, out var parameterError) ||
                string.IsNullOrEmpty(parameterError) || !parameterError.Contains("器件参数"))
                throw new InvalidOperationException("极端有限参数应被拒绝。");
        }

        private static void ValidateDrawingFileImportLimitPreservesWorkspace()
        {
            var tempDir = CreateUniqueTempDir("D2ImportLimit");
            var canvasRoot = new GameObject("SpiceD2PathLimitValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var existing = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(25f, -40f));
                var originalModel = workspace.Model;
                var originalRevision = workspace.ElectricalRevisionForTesting;
                var originalPath = Path.Combine(tempDir, "current.spicejson");
                if (!workspace.TrySaveWorkspaceToPath(originalPath, out var saveError))
                    throw new InvalidOperationException("D2 路径测试无法建立当前文件路径：" + saveError);

                var oversized = CreateDrawingLimitDto();
                for (var i = 1; i <= SpiceDrawingLimits.MaxComponents + 1; i++)
                    oversized.components.Add(CreateLimitComponent("resistor-" + i.ToString("D3")));
                var importPath = Path.Combine(tempDir, "too-many-components.spicejson");
                File.WriteAllText(importPath, JsonUtility.ToJson(oversized, true));

                if (workspace.TryImportWorkspaceFromPath(importPath, out var importError))
                    throw new InvalidOperationException("正式路径级导入不应接受超限图纸。");
                if (string.IsNullOrEmpty(importError) || !importError.Contains("器件数量"))
                    throw new InvalidOperationException("路径级超限导入应返回稳定的用户错误。");
                if (!ReferenceEquals(originalModel, workspace.Model) ||
                    workspace.Model.Components.Count != 1 ||
                    workspace.Model.Components[0].InstanceId != existing.InstanceId ||
                    workspace.Model.Components[0].Position != new Vector2(25f, -40f) ||
                    workspace.CurrentSpiceFilePath != originalPath ||
                    workspace.ElectricalRevisionForTesting != originalRevision)
                    throw new InvalidOperationException("超限导入失败后 Workspace、路径或修订号发生变化。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
                CleanupTempDir(tempDir);
            }
        }

        private static SpiceDrawingFileDto CreateDrawingLimitDto()
        {
            return new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.CurrentSchemaVersion,
                analysis = new SpiceAnalysisDto
                {
                    mode = SpiceAnalysisMode.DcOperatingPoint.ToString(),
                    frequencyHz = SpiceAnalysisLimits.DefaultFrequencyHz.ToString(CultureInfo.InvariantCulture)
                }
            };
        }

        private static SpiceDrawingFileDto CreateDrawingLimitDtoWithEndpoints()
        {
            var dto = CreateDrawingLimitDto();
            // V2 会拒绝重复 Wire；为验证 1000 条上限，提供 500 个两端器件以构造 1000 条不同端点组合。
            for (var i = 1; i <= SpiceDrawingLimits.MaxComponents; i++)
                dto.components.Add(CreateLimitComponent("resistor-" + i.ToString("D3")));
            return dto;
        }

        private static SpiceComponentDto CreateLimitComponent(string instanceId, string x = "0", string y = "0")
        {
            return new SpiceComponentDto
            {
                instanceId = instanceId,
                componentType = SpiceComponentKind.Resistor.ToString(),
                position = new SpiceVector2Dto { x = x, y = y },
                rotationQuarterTurns = 0,
                siValueText = "1000"
            };
        }

        private static SpiceWireDto CreateLimitWire(SpiceWireRouteMode routeMode, int waypointCount)
        {
            var wire = new SpiceWireDto
            {
                startComponentId = "resistor-001",
                startTerminalId = SpiceComponentModel.PositiveTerminalId,
                endComponentId = "resistor-002",
                endTerminalId = SpiceComponentModel.NegativeTerminalId,
                routeMode = routeMode.ToString()
            };
            for (var i = 0; i < waypointCount; i++)
                wire.manualRoutePoints.Add(new SpiceVector2Dto { x = (i % 100).ToString(), y = (i / 100).ToString() });
            return wire;
        }

        private static SpiceWireDto CreateUniqueLimitWire(int index)
        {
            var componentIndex = index % SpiceDrawingLimits.MaxComponents + 1;
            var endIndex = componentIndex % SpiceDrawingLimits.MaxComponents + 1;
            return new SpiceWireDto
            {
                startComponentId = "resistor-" + componentIndex.ToString("D3"),
                startTerminalId = index < SpiceDrawingLimits.MaxComponents ? SpiceComponentModel.PositiveTerminalId : SpiceComponentModel.NegativeTerminalId,
                endComponentId = "resistor-" + endIndex.ToString("D3"),
                endTerminalId = SpiceComponentModel.NegativeTerminalId,
                routeMode = SpiceWireRouteMode.Auto.ToString()
            };
        }

        private static void AddLimitWaypointsAcrossWires(SpiceDrawingFileDto dto, int totalWaypointCount)
        {
            var remaining = totalWaypointCount;
            var wireIndex = 0;
            while (remaining > 0)
            {
                var count = Math.Min(SpiceDrawingLimits.MaxManualRoutePointsPerWire, remaining);
                var wire = CreateUniqueLimitWire(wireIndex++);
                wire.routeMode = SpiceWireRouteMode.Manual.ToString();
                for (var pointIndex = 0; pointIndex < count; pointIndex++)
                    wire.manualRoutePoints.Add(new SpiceVector2Dto { x = (pointIndex % 100).ToString(), y = (pointIndex / 100).ToString() });
                dto.wires.Add(wire);
                remaining -= count;
            }
        }

        // ===== SPICE 图纸数据契约与内存往返验证 =====

        private static void ValidateDrawingDataContractRoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(10f, 20f));
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 100f));
            model.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive");

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("图纸 JSON 往返失败：" + error);

            if (restored.Components.Count != 2) throw new InvalidOperationException("往返后组件数量不匹配。");
            if (restored.Wires.Count != 1) throw new InvalidOperationException("往返后导线数量不匹配。");

            var restoredSource = restored.FindComponent(source.InstanceId);
            if (restoredSource == null) throw new InvalidOperationException("往返后丢失源组件。");
            if (restoredSource.Kind != SpiceComponentKind.DcVoltageSource) throw new InvalidOperationException("往返后器件类型不匹配。");
            if (restoredSource.Position != new Vector2(10f, 20f)) throw new InvalidOperationException("往返后位置不匹配。");
            if (Math.Abs(restoredSource.SiValue - source.SiValue) > 1e-12) throw new InvalidOperationException("往返后参数值不匹配。");

            var restoredWire = restored.Wires[0];
            // 导线方向已规范化：字典序 "resistor-001" < "source-001"（r < s），
            // 因此 start 固定为 resistor，end 固定为 source。
            if (restoredWire.StartComponentId != resistor.InstanceId || restoredWire.StartTerminalId != "positive") throw new InvalidOperationException("往返后导线起点不匹配。");
            if (restoredWire.EndComponentId != source.InstanceId || restoredWire.EndTerminalId != "positive") throw new InvalidOperationException("往返后导线终点不匹配。");
            if (restoredWire.VisualState.RouteMode != SpiceWireRouteMode.Auto) throw new InvalidOperationException("往返后路由模式不匹配。");

            var dto = SpiceDrawingSerializer.ToDto(model);
            if (dto.format != SpiceDrawingFormat.Format) throw new InvalidOperationException("DTO format 不正确。");
            if (dto.schemaVersion != SpiceDrawingFormat.CurrentSchemaVersion) throw new InvalidOperationException("DTO schemaVersion 不正确。");
        }

        private static void ValidateDrawingTenDeviceTypesRoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(0f, 0f));
            model.AddComponent(SpiceComponentKind.DcCurrentSource, new Vector2(100f, 0f));
            model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 0f));
            model.AddComponent(SpiceComponentKind.Capacitor, new Vector2(300f, 0f));
            model.AddComponent(SpiceComponentKind.Inductor, new Vector2(400f, 0f));
            model.AddComponent(SpiceComponentKind.Ground, new Vector2(500f, 0f));
            model.AddComponent(SpiceComponentKind.IdealSwitch, new Vector2(600f, 0f));
            model.AddComponent(SpiceComponentKind.SiliconDiode, new Vector2(700f, 0f));
            model.AddComponent(SpiceComponentKind.VoltageProbe, new Vector2(800f, 0f));
            model.AddComponent(SpiceComponentKind.CurrentProbe, new Vector2(900f, 0f));

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("10 类器件往返失败：" + error);

            if (restored.Components.Count != 10) throw new InvalidOperationException("10 类器件往返后数量不匹配。");
            // 往返后组件按 InstanceId 字典序排序，验证类型集合而非顺序
            var expectedKinds = new[]
            {
                SpiceComponentKind.DcVoltageSource, SpiceComponentKind.DcCurrentSource,
                SpiceComponentKind.Resistor, SpiceComponentKind.Capacitor,
                SpiceComponentKind.Inductor, SpiceComponentKind.Ground,
                SpiceComponentKind.IdealSwitch, SpiceComponentKind.SiliconDiode,
                SpiceComponentKind.VoltageProbe, SpiceComponentKind.CurrentProbe
            };
            var restoredKinds = new System.Collections.Generic.HashSet<SpiceComponentKind>();
            foreach (var component in restored.Components)
            {
                if (!restoredKinds.Add(component.Kind))
                    throw new InvalidOperationException("10 类器件往返后出现重复类型：" + component.Kind);
            }
            foreach (var expected in expectedKinds)
            {
                if (!restoredKinds.Contains(expected))
                    throw new InvalidOperationException("10 类器件往返后缺失类型：" + expected);
            }
        }

        private static void ValidateDrawingRotationAndSwitchState()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", new Vector2(10f, 10f), 12d, 2);
            var sw = model.AddComponentWithIdentity(SpiceComponentKind.IdealSwitch, "switch-001", new Vector2(100f, 100f), 1d, 3);

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("旋转/开关往返失败：" + error);

            var restoredSource = restored.FindComponent("source-001");
            if (restoredSource.RotationQuarterTurns != 2) throw new InvalidOperationException("往返后旋转值不匹配。");
            if (Math.Abs(restoredSource.SiValue - 12d) > 1e-12) throw new InvalidOperationException("往返后电压值不匹配。");

            var restoredSwitch = restored.FindComponent("switch-001");
            if (restoredSwitch.RotationQuarterTurns != 3) throw new InvalidOperationException("往返后开关旋转值不匹配。");
            if (Math.Abs(restoredSwitch.SiValue - 1d) > 1e-12) throw new InvalidOperationException("往返后开关状态不匹配。");
        }

        private static void ValidateDrawingWireRouteModes()
        {
            var model = new SpiceWorkspaceModel();
            var source = model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistor = model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 0f));
            var ground = model.AddComponent(SpiceComponentKind.Ground, new Vector2(0f, -200f));

            model.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive");
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            model.AddWire(source.InstanceId, "negative", ground.InstanceId, "ground", SpiceWireVisualState.Manual(waypoints));

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("Wire 路由模式往返失败：" + error);

            if (restored.Wires.Count != 2) throw new InvalidOperationException("往返后导线数量不匹配。");
            // 往返后 Wire 方向已规范化（字典序较小的端点为 start），按端子查找对应 Wire：
            // - Auto wire: source:positive <-> resistor:positive（两端端子相同，方向不影响查找）
            // - Manual wire: 原始 source:negative -> ground:ground，规范化后 ground:ground -> source:negative
            SpiceWorkspaceWireData autoWire = null, manualWire = null;
            foreach (var wire in restored.Wires)
            {
                if (wire.StartTerminalId == "positive" && wire.EndTerminalId == "positive") autoWire = wire;
                else if (wire.StartTerminalId == "ground" && wire.EndTerminalId == "negative") manualWire = wire;
            }
            if (autoWire == null) throw new InvalidOperationException("往返后丢失自动路由导线。");
            if (autoWire.VisualState.RouteMode != SpiceWireRouteMode.Auto) throw new InvalidOperationException("自动路由模式往返后不匹配。");
            if (manualWire == null) throw new InvalidOperationException("往返后丢失手工路由导线。");
            if (manualWire.VisualState.RouteMode != SpiceWireRouteMode.Manual) throw new InvalidOperationException("手工路由模式往返后不匹配。");
            if (manualWire.VisualState.Waypoints.Count != 3) throw new InvalidOperationException("手工折点数量往返后不匹配。");
            // 端点交换后折点倒序：原始 [40,0],[40,80],[120,80] -> 倒序 [120,80],[40,80],[40,0]
            if (manualWire.VisualState.Waypoints[0] != new Vector2(120f, 80f) ||
                manualWire.VisualState.Waypoints[1] != new Vector2(40f, 80f) ||
                manualWire.VisualState.Waypoints[2] != new Vector2(40f, 0f))
                throw new InvalidOperationException("手工折点坐标往返后不匹配（端点交换后应倒序）。");
        }

        private static void ValidateDrawingEmptyCanvasRoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("空画布往返失败：" + error);
            if (restored.Components.Count != 0 || restored.Wires.Count != 0)
                throw new InvalidOperationException("空画布往返后不应有组件或导线。");
        }

        private static void ValidateDrawingJsonDeterminism()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.DcVoltageSource, new Vector2(10f, 20f));
            model.AddComponent(SpiceComponentKind.Resistor, new Vector2(200f, 100f));
            model.AddWire("source-001", "positive", "resistor-001", "positive");

            var json1 = SpiceDrawingSerializer.ToJson(model);
            var json2 = SpiceDrawingSerializer.ToJson(model);
            if (json1 != json2) throw new InvalidOperationException("相同模型的 JSON 输出不确定。");
        }

        private static void ValidateDrawingInstanceNumberRecovery()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.zero, 1000d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-002", Vector2.right, 2000d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-005", Vector2.up, 5000d, 0);
            model.RestoreInstanceNumbersFromExisting();

            var newResistor = model.AddComponent(SpiceComponentKind.Resistor, Vector2.down);
            if (newResistor.InstanceId != "resistor-006")
                throw new InvalidOperationException("导入 R1/R2/R5 后新建应得到 R6，实际得到 " + newResistor.InstanceId);
        }

        private static void ValidateDrawingImportRejectionCases()
        {
            // 未知格式
            var badFormat = JsonUtility.ToJson(new SpiceDrawingFileDto { format = "Unknown", schemaVersion = 1 }, true);
            if (SpiceDrawingSerializer.TryFromJson(badFormat, out _, out var error1))
                throw new InvalidOperationException("未知格式应被拒绝。");
            if (string.IsNullOrEmpty(error1)) throw new InvalidOperationException("未知格式应返回错误信息。");

            // 未知版本
            var badVersion = JsonUtility.ToJson(new SpiceDrawingFileDto { format = SpiceDrawingFormat.Format, schemaVersion = 99 }, true);
            if (SpiceDrawingSerializer.TryFromJson(badVersion, out _, out _))
                throw new InvalidOperationException("未知版本应被拒绝。");

            // 空内容
            if (SpiceDrawingSerializer.TryFromJson("", out _, out _))
                throw new InvalidOperationException("空内容应被拒绝。");
            if (SpiceDrawingSerializer.TryFromJson("   ", out _, out _))
                throw new InvalidOperationException("空白内容应被拒绝。");

            // 重复 InstanceId
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.zero, 1000d, 0);
            var dto = SpiceDrawingSerializer.ToDto(model);
            dto.components.Add(new SpiceComponentDto
            {
                instanceId = "resistor-001",
                componentType = SpiceComponentKind.Resistor.ToString(),
                position = new SpiceVector2Dto { x = "1", y = "1" },
                rotationQuarterTurns = 0,
                siValueText = "2000"
            });
            if (SpiceDrawingSerializer.TryFromDto(dto, out _, out var error3))
                throw new InvalidOperationException("重复 InstanceId 应被拒绝。");
            if (string.IsNullOrEmpty(error3)) throw new InvalidOperationException("重复 InstanceId 应返回错误信息。");

            // 未知器件类型（字符串无法解析）
            var unknownTypeDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "unknown-001",
                        componentType = "NonexistentType",
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(unknownTypeDto, out _, out var error4))
                throw new InvalidOperationException("未知器件类型应被拒绝。");
            if (string.IsNullOrEmpty(error4)) throw new InvalidOperationException("未知器件类型应返回错误信息。");

            // 非法参数（0 欧姆电阻）：siValueText 显式提供 "0"，但电阻必须大于 0
            var badParamDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-001",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "0"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(badParamDto, out _, out var error5))
                throw new InvalidOperationException("0 欧姆电阻应被拒绝。");
            if (string.IsNullOrEmpty(error5)) throw new InvalidOperationException("非法参数应返回错误信息。");

            // 导线引用不存在的组件
            var danglingWireDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                wires = new System.Collections.Generic.List<SpiceWireDto>
                {
                    new SpiceWireDto
                    {
                        startComponentId = "resistor-001",
                        startTerminalId = "positive",
                        endComponentId = "resistor-002",
                        endTerminalId = "negative",
                        routeMode = SpiceWireRouteMode.Auto.ToString()
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(danglingWireDto, out _, out var error6))
                throw new InvalidOperationException("引用不存在组件的导线应被拒绝。");
            if (string.IsNullOrEmpty(error6)) throw new InvalidOperationException("悬空导线应返回错误信息。");

            // 数值未定义枚举（"999" 能被 TryParse 解析但不在 IsDefined 范围内）
            var undefinedEnumDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-001",
                        componentType = "999",
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(undefinedEnumDto, out _, out var error7))
                throw new InvalidOperationException("数值未定义枚举 \"999\" 应被 IsDefined 拒绝。");
            if (string.IsNullOrEmpty(error7)) throw new InvalidOperationException("未定义枚举应返回错误信息。");

            // 有参数器件缺失 siValueText（null/空字符串都必须拒绝，以区分字段缺失与数值 0）
            var missingSiValueDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0
                        // siValueText 缺省为 null
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(missingSiValueDto, out _, out var error8))
                throw new InvalidOperationException("有参数器件缺失 siValueText 应被拒绝。");
            if (string.IsNullOrEmpty(error8)) throw new InvalidOperationException("缺失 siValueText 应返回错误信息。");

            // 有参数器件 siValueText 为空白字符串同样视为字段缺失
            var blankSiValueDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "   "
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(blankSiValueDto, out _, out var errorBlank))
                throw new InvalidOperationException("siValueText 为空白字符串应被拒绝。");
            if (string.IsNullOrEmpty(errorBlank)) throw new InvalidOperationException("空白 siValueText 应返回错误信息。");

            // siValueText 非法数值字符串（无法 TryParse）
            var nonNumericDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "not-a-number"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nonNumericDto, out _, out var errorNonNumeric))
                throw new InvalidOperationException("非法数值字符串应被拒绝。");
            if (string.IsNullOrEmpty(errorNonNumeric)) throw new InvalidOperationException("非法数值字符串应返回错误信息。");

            // siValueText 为 NaN 字面量
            var nanDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "NaN"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nanDto, out _, out var errorNan))
                throw new InvalidOperationException("NaN 应被拒绝。");
            if (string.IsNullOrEmpty(errorNan)) throw new InvalidOperationException("NaN 应返回错误信息。");

            // siValueText 为 Infinity 字面量
            var infinityDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "Infinity"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(infinityDto, out _, out var errorInfinity))
                throw new InvalidOperationException("Infinity 应被拒绝。");
            if (string.IsNullOrEmpty(errorInfinity)) throw new InvalidOperationException("Infinity 应返回错误信息。");

            // InstanceId 前缀与器件类型不匹配
            var mismatchedPrefixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(mismatchedPrefixDto, out _, out var error9))
                throw new InvalidOperationException("InstanceId 前缀与器件类型不匹配应被拒绝。");
            if (string.IsNullOrEmpty(error9)) throw new InvalidOperationException("前缀不匹配应返回错误信息。");

            // InstanceId 后缀长度不足 3 位（D3 语义是最少三位）
            var badSuffixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-1",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(badSuffixDto, out _, out var error10))
                throw new InvalidOperationException("InstanceId 后缀长度不足 3 位应被拒绝。");
            if (string.IsNullOrEmpty(error10)) throw new InvalidOperationException("后缀格式错误应返回错误信息。");

            // InstanceId 后缀为 000（非正整数）
            var zeroSuffixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor-000",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(zeroSuffixDto, out _, out var error11))
                throw new InvalidOperationException("InstanceId 后缀 000 应被拒绝（非正整数）。");
            if (string.IsNullOrEmpty(error11)) throw new InvalidOperationException("后缀 000 应返回错误信息。");

            // InstanceId 后缀为负数（含负号，非纯数字）
            var negativeSuffixDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "resistor--001",
                        componentType = SpiceComponentKind.Resistor.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "1000"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(negativeSuffixDto, out _, out var errorNegative))
                throw new InvalidOperationException("InstanceId 后缀为负数应被拒绝。");
            if (string.IsNullOrEmpty(errorNegative)) throw new InvalidOperationException("负数后缀应返回错误信息。");
        }

        private static void ValidateDrawingStableSortOrder()
        {
            // 验证同一模型两次序列化结果一致
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            model.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            model.AddWire("source-001", "positive", "resistor-001", "positive");

            var json1 = SpiceDrawingSerializer.ToJson(model);
            var json2 = SpiceDrawingSerializer.ToJson(model);
            if (json1 != json2) throw new InvalidOperationException("同一模型两次序列化结果应一致。");

            // 验证组件顺序不影响 JSON：先加电阻再加电压源，JSON 中应按 InstanceId 排序
            var modelReversed = new SpiceWorkspaceModel();
            modelReversed.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelReversed.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);

            var jsonReversed = SpiceDrawingSerializer.ToJson(modelReversed);
            var sourceIndex = jsonReversed.IndexOf("source-001");
            var resistorIndex = jsonReversed.IndexOf("resistor-001");
            if (sourceIndex < 0 || resistorIndex < 0) throw new InvalidOperationException("JSON 中应包含两个组件。");
            // 字典序 "resistor-001" < "source-001"（r < s），resistor 应排在 source 之前
            if (resistorIndex > sourceIndex) throw new InvalidOperationException("组件应按 InstanceId 字典序排序（resistor 应在 source 之前）。");
        }

        // 验证 Auto 路由 Wire 的双向创建生成字节完全一致的 JSON。
        // 规范化方向：比较两端 (componentId, terminalId) 字典序，较小者固定为 start。
        private static void ValidateDrawingBidirectionalAutoWireJson()
        {
            var modelA = new SpiceWorkspaceModel();
            var sourceA = modelA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorA = modelA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 正向：source -> resistor
            modelA.AddWire(sourceA.InstanceId, "positive", resistorA.InstanceId, "positive");

            var modelB = new SpiceWorkspaceModel();
            var sourceB = modelB.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorB = modelB.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 反向：resistor -> source
            modelB.AddWire(resistorB.InstanceId, "positive", sourceB.InstanceId, "positive");

            var jsonA = SpiceDrawingSerializer.ToJson(modelA);
            var jsonB = SpiceDrawingSerializer.ToJson(modelB);
            // 字典序 "resistor-001" < "source-001"，两端都会被规范化为 resistor -> source
            if (jsonA != jsonB) throw new InvalidOperationException("反向创建的 Auto Wire 应生成字节完全一致的 JSON。");
        }

        // 验证 Manual 路由 Wire 的双向创建生成字节完全一致的 JSON。
        // 端点交换时 manualRoutePoints 必须倒序输出，保持折点序列与端点方向一致。
        // 等价 Wire：反向创建时折点也必须倒序，使两端表示同一物理路径。
        private static void ValidateDrawingBidirectionalManualWireJson()
        {
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            var reversedWaypoints = new[] { new Vector2(120f, 80f), new Vector2(40f, 80f), new Vector2(40f, 0f) };

            var modelA = new SpiceWorkspaceModel();
            var sourceA = modelA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorA = modelA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 正向：source -> resistor，折点从 source 侧到 resistor 侧
            modelA.AddWire(sourceA.InstanceId, "positive", resistorA.InstanceId, "positive",
                SpiceWireVisualState.Manual(waypoints));

            var modelB = new SpiceWorkspaceModel();
            var sourceB = modelB.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorB = modelB.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            // 反向：resistor -> source，折点倒序以表示同一物理路径
            modelB.AddWire(resistorB.InstanceId, "positive", sourceB.InstanceId, "positive",
                SpiceWireVisualState.Manual(reversedWaypoints));

            var jsonA = SpiceDrawingSerializer.ToJson(modelA);
            var jsonB = SpiceDrawingSerializer.ToJson(modelB);
            // modelA：端点交换（source->resistor 变为 resistor->source），折点倒序输出 [120,80],[40,80],[40,0]
            // modelB：端点不交换（已是 resistor->source），折点原序输出 [120,80],[40,80],[40,0]
            // 两者规范化后端点和折点完全一致，JSON 必须字节相同。
            if (jsonA != jsonB) throw new InvalidOperationException("反向创建的等价 Manual Wire 应生成字节完全一致的 JSON。");

            // 验证往返后折点坐标正确（按规范化方向 resistor -> source）
            if (!SpiceDrawingSerializer.TryFromJson(jsonA, out var restored, out var error))
                throw new InvalidOperationException("Manual Wire 往返失败：" + error);
            if (restored.Wires.Count != 1) throw new InvalidOperationException("往返后导线数量不匹配。");
            var restoredWire = restored.Wires[0];
            // 规范化后 start 应为字典序较小的 resistor-001
            if (restoredWire.StartComponentId != "resistor-001" || restoredWire.EndComponentId != "source-001")
                throw new InvalidOperationException("Manual Wire 往返后端点方向未规范化。");
            if (restoredWire.VisualState.Waypoints.Count != 3) throw new InvalidOperationException("往返后折点数量不匹配。");
            // 规范化后折点应为 [120,80], [40,80], [40,0]
            if (restoredWire.VisualState.Waypoints[0] != new Vector2(120f, 80f) ||
                restoredWire.VisualState.Waypoints[1] != new Vector2(40f, 80f) ||
                restoredWire.VisualState.Waypoints[2] != new Vector2(40f, 0f))
                throw new InvalidOperationException("端点交换后折点未正确倒序。");
        }

        // 验证 0 V 电压源与 0 A 电流源必须被允许（siValueText="0" 合法），
        // 而电阻/电容/电感仍必须大于 0。
        private static void ValidateDrawingZeroValueAllowed()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", Vector2.zero, 0d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.DcCurrentSource, "current-source-001", Vector2.right, 0d, 0);

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("0V/0A 往返失败：" + error);

            var restoredSource = restored.FindComponent("source-001");
            if (restoredSource == null) throw new InvalidOperationException("往返后丢失电压源。");
            if (Math.Abs(restoredSource.SiValue - 0d) > 1e-12) throw new InvalidOperationException("往返后电压源 0V 值不匹配。");

            var restoredCurrent = restored.FindComponent("current-source-001");
            if (restoredCurrent == null) throw new InvalidOperationException("往返后丢失电流源。");
            if (Math.Abs(restoredCurrent.SiValue - 0d) > 1e-12) throw new InvalidOperationException("往返后电流源 0A 值不匹配。");

            // 验证 DTO 中 siValueText 确实为 "0"（区分字段缺失与数值 0）
            var dto = SpiceDrawingSerializer.ToDto(model);
            var sourceDto = dto.components.Find(c => c.instanceId == "source-001");
            if (sourceDto == null || sourceDto.siValueText != "0") throw new InvalidOperationException("0V 电压源的 siValueText 应为 \"0\"。");
            var currentDto = dto.components.Find(c => c.instanceId == "current-source-001");
            if (currentDto == null || currentDto.siValueText != "0") throw new InvalidOperationException("0A 电流源的 siValueText 应为 \"0\"。");
        }

        // 验证第 1000 个编号的 JSON 往返及后续编号恢复。
        // D3 语义是最少三位，不是最多三位：resistor-1000 必须被接受。
        private static void ValidateDrawingInstanceNumber1000RoundTrip()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-1000", Vector2.zero, 1000d, 0);
            model.RestoreInstanceNumbersFromExisting();

            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("resistor-1000 往返失败：" + error);

            var restoredResistor = restored.FindComponent("resistor-1000");
            if (restoredResistor == null) throw new InvalidOperationException("往返后丢失 resistor-1000。");
            if (restoredResistor.Kind != SpiceComponentKind.Resistor) throw new InvalidOperationException("往返后类型不匹配。");

            // 验证后续编号恢复：下一个新建电阻应为 resistor-1001
            var newResistor = restored.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            if (newResistor.InstanceId != "resistor-1001")
                throw new InvalidOperationException("resistor-1000 往返后新建应得到 resistor-1001，实际得到 " + newResistor.InstanceId);
        }

        // 验证 InstanceId 后缀规则的边界情况：
        // 接受 resistor-001（最少三位）和 resistor-1000（四位），
        // 拒绝 resistor-1（不足三位）、resistor-000（非正整数）、resistor--001（负数）。
        private static void ValidateDrawingInstanceIdSuffixBoundary()
        {
            // 直接校验 IsValidInstanceId 的接受与拒绝
            if (!SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-001"))
                throw new InvalidOperationException("resistor-001 应被接受。");
            if (!SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-1000"))
                throw new InvalidOperationException("resistor-1000 应被接受。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-1"))
                throw new InvalidOperationException("resistor-1 应被拒绝（后缀不足三位）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-000"))
                throw new InvalidOperationException("resistor-000 应被拒绝（非正整数）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor--001"))
                throw new InvalidOperationException("resistor--001 应被拒绝（负数后缀）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-abc"))
                throw new InvalidOperationException("resistor-abc 应被拒绝（非数字后缀）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.DcVoltageSource, "resistor-001"))
                throw new InvalidOperationException("resistor-001 与 DcVoltageSource 前缀不匹配应被拒绝。");

            // 纯数字严格校验：不接受加号、减号、空白或其他非数字字符
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-+001"))
                throw new InvalidOperationException("resistor-+001 应被拒绝（加号非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor- 001"))
                throw new InvalidOperationException("resistor- 001 应被拒绝（前导空格非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-001 "))
                throw new InvalidOperationException("resistor-001 (末尾空格) 应被拒绝（末尾空格非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor-00a1"))
                throw new InvalidOperationException("resistor-00a1 应被拒绝（含字母非纯数字）。");
            if (SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.Resistor, "resistor--001"))
                throw new InvalidOperationException("resistor--001 应被拒绝（负号非纯数字）。");

            // 验证 resistor-001 与 resistor-1000 均能完整往返
            var model = new SpiceWorkspaceModel();
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.zero, 1000d, 0);
            model.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-1000", Vector2.right, 2000d, 0);
            var json = SpiceDrawingSerializer.ToJson(model);
            if (!SpiceDrawingSerializer.TryFromJson(json, out var restored, out var error))
                throw new InvalidOperationException("resistor-001 + resistor-1000 往返失败：" + error);
            if (restored.Components.Count != 2) throw new InvalidOperationException("往返后组件数量不匹配。");
            if (restored.FindComponent("resistor-001") == null) throw new InvalidOperationException("往返后丢失 resistor-001。");
            if (restored.FindComponent("resistor-1000") == null) throw new InvalidOperationException("往返后丢失 resistor-1000。");
        }

        // 验证同端点不同 Manual 路径的 Wire 排序稳定性。
        // 两条端点相同、waypoint 不同的 Manual Wire，交换添加顺序后 JSON 必须字节完全一致。
        private static void ValidateDrawingSameEndpointDifferentManualPaths()
        {
            var pathA = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            var pathB = new[] { new Vector2(0f, 40f), new Vector2(80f, 40f) };

            // 模型 1：先 A 后 B
            var modelAB = new SpiceWorkspaceModel();
            var sourceAB = modelAB.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorAB = modelAB.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelAB.AddWire(sourceAB.InstanceId, "positive", resistorAB.InstanceId, "positive", SpiceWireVisualState.Manual(pathA));
            modelAB.AddWire(sourceAB.InstanceId, "positive", resistorAB.InstanceId, "positive", SpiceWireVisualState.Manual(pathB));

            // 模型 2：先 B 后 A
            var modelBA = new SpiceWorkspaceModel();
            var sourceBA = modelBA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorBA = modelBA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelBA.AddWire(sourceBA.InstanceId, "positive", resistorBA.InstanceId, "positive", SpiceWireVisualState.Manual(pathB));
            modelBA.AddWire(sourceBA.InstanceId, "positive", resistorBA.InstanceId, "positive", SpiceWireVisualState.Manual(pathA));

            var jsonAB = SpiceDrawingSerializer.ToJson(modelAB);
            var jsonBA = SpiceDrawingSerializer.ToJson(modelBA);
            if (jsonAB != jsonBA) throw new InvalidOperationException("同端点不同 Manual 路径交换添加顺序后 JSON 应字节一致。");
        }

        // 验证同端点 Manual Wire 的反向端点创建与正向等价版本 JSON 字节一致。
        // 反向创建时 waypoint 必须倒序以表示同一物理路径。
        private static void ValidateDrawingSameEndpointReverseManualPath()
        {
            var pathForward = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };
            var pathReversed = new[] { new Vector2(120f, 80f), new Vector2(40f, 80f), new Vector2(40f, 0f) };

            // 模型 1：正向 source -> resistor
            var modelFwd = new SpiceWorkspaceModel();
            var sourceFwd = modelFwd.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorFwd = modelFwd.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelFwd.AddWire(sourceFwd.InstanceId, "positive", resistorFwd.InstanceId, "positive", SpiceWireVisualState.Manual(pathForward));

            // 模型 2：反向 resistor -> source，waypoint 倒序
            var modelRev = new SpiceWorkspaceModel();
            var sourceRev = modelRev.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorRev = modelRev.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelRev.AddWire(resistorRev.InstanceId, "positive", sourceRev.InstanceId, "positive", SpiceWireVisualState.Manual(pathReversed));

            var jsonFwd = SpiceDrawingSerializer.ToJson(modelFwd);
            var jsonRev = SpiceDrawingSerializer.ToJson(modelRev);
            if (jsonFwd != jsonRev) throw new InvalidOperationException("同端点反向 Manual Wire 与正向等价版本 JSON 应字节一致。");
        }

        // 验证同端点 Auto 与 Manual Wire 共存时，交换添加顺序后 JSON 字节一致。
        private static void ValidateDrawingSameEndpointAutoAndManual()
        {
            var waypoints = new[] { new Vector2(40f, 0f), new Vector2(40f, 80f), new Vector2(120f, 80f) };

            // 模型 1：先 Auto 后 Manual
            var modelAM = new SpiceWorkspaceModel();
            var sourceAM = modelAM.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorAM = modelAM.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelAM.AddWire(sourceAM.InstanceId, "positive", resistorAM.InstanceId, "positive");
            modelAM.AddWire(sourceAM.InstanceId, "positive", resistorAM.InstanceId, "positive", SpiceWireVisualState.Manual(waypoints));

            // 模型 2：先 Manual 后 Auto
            var modelMA = new SpiceWorkspaceModel();
            var sourceMA = modelMA.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistorMA = modelMA.AddComponent(SpiceComponentKind.Resistor, Vector2.right);
            modelMA.AddWire(sourceMA.InstanceId, "positive", resistorMA.InstanceId, "positive", SpiceWireVisualState.Manual(waypoints));
            modelMA.AddWire(sourceMA.InstanceId, "positive", resistorMA.InstanceId, "positive");

            var jsonAM = SpiceDrawingSerializer.ToJson(modelAM);
            var jsonMA = SpiceDrawingSerializer.ToJson(modelMA);
            if (jsonAM != jsonMA) throw new InvalidOperationException("同端点 Auto/Manual 共存交换添加顺序后 JSON 应字节一致。");
        }

        // 验证 position 缺失（null）必须被拒绝，且错误信息包含 InstanceId。
        // SpiceVector2Dto 为 class，缺失时为 null，不再静默变为 (0,0)。
        private static void ValidateDrawingPositionNullRejected()
        {
            var nullPositionDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = null,
                        rotationQuarterTurns = 0,
                        siValueText = "10"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nullPositionDto, out _, out var error))
                throw new InvalidOperationException("position=null 应被拒绝。");
            if (string.IsNullOrEmpty(error)) throw new InvalidOperationException("position=null 应返回错误信息。");
            if (error.IndexOf("source-001", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("position=null 错误信息应包含 InstanceId。");
        }

        // 验证显式 position=(0,0) 必须被允许且往返后保持不变。
        private static void ValidateDrawingPositionZeroAllowed()
        {
            var zeroPositionDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = new SpiceVector2Dto { x = "0", y = "0" },
                        rotationQuarterTurns = 0,
                        siValueText = "10"
                    }
                }
            };
            if (!SpiceDrawingSerializer.TryFromDto(zeroPositionDto, out var restored, out var error))
                throw new InvalidOperationException("position=(0,0) 应被允许：" + error);
            var restoredSource = restored.FindComponent("source-001");
            if (restoredSource == null) throw new InvalidOperationException("往返后丢失组件。");
            if (restoredSource.Position != Vector2.zero) throw new InvalidOperationException("往返后 position 应保持 (0,0)。");
        }

        private static void ValidateIdealSwitchVisualStateSynchronization()
        {
            var canvasRoot = new GameObject("SpiceSwitchVisualValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);
                var switchData = workspace.CreateComponent(SpiceComponentKind.IdealSwitch, Vector2.zero);
                var switchView = workspace.GetComponentViewForTesting(switchData.InstanceId);
                if (switchView == null) throw new InvalidOperationException("新建理想开关应创建元件视图。");
                if (switchView.IsIdealSwitchDrawnClosedForTesting())
                    throw new InvalidOperationException("新建理想开关应显示为断开。");

                if (!workspace.TrySetSwitchState(switchData.InstanceId, true))
                    throw new InvalidOperationException("理想开关应能切换到闭合状态。");
                if (!switchView.IsIdealSwitchDrawnClosedForTesting())
                    throw new InvalidOperationException("闭合理想开关应立即重绘为闭合符号。");

                var json = SpiceDrawingSerializer.ToJson(workspace.Model);
                if (!workspace.TryImportDrawingJson(json, out var error))
                    throw new InvalidOperationException("导入闭合理想开关应成功：" + error);

                switchView = workspace.GetComponentViewForTesting(switchData.InstanceId);
                if (switchView == null) throw new InvalidOperationException("导入理想开关应创建元件视图。");
                if (!switchView.IsIdealSwitchDrawnClosedForTesting())
                    throw new InvalidOperationException("导入后的闭合理想开关应显示为闭合符号。");

                if (!workspace.TrySetSwitchState(switchData.InstanceId, false))
                    throw new InvalidOperationException("理想开关应能切换到断开状态。");
                if (switchView.IsIdealSwitchDrawnClosedForTesting())
                    throw new InvalidOperationException("断开理想开关应立即重绘为断开符号。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // 场景 A：成功导入包含十类器件的完整电路，验证所有字段恢复正确。
        private static void ValidateDrawingImportSuccessFullCircuit()
        {
            var canvasRoot = new GameObject("SpiceImportSuccessValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 构建包含十类器件的图纸模型，含旋转和开关状态
                var sourceModel = new SpiceWorkspaceModel();
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", new Vector2(0f, 0f), 10d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.DcCurrentSource, "current-source-001", new Vector2(100f, 0f), 0.001d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", new Vector2(200f, 0f), 2000d, 1);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Capacitor, "capacitor-001", new Vector2(300f, 0f), 1e-6d, 2);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Inductor, "inductor-001", new Vector2(400f, 0f), 0.01d, 3);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.Ground, "ground-001", new Vector2(0f, -100f), 0d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.IdealSwitch, "switch-001", new Vector2(100f, -100f), 1d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.SiliconDiode, "diode-001", new Vector2(200f, -100f), 0d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.VoltageProbe, "voltage-probe-001", new Vector2(300f, -100f), 0d, 0);
                sourceModel.AddComponentWithIdentity(SpiceComponentKind.CurrentProbe, "current-probe-001", new Vector2(400f, -100f), 0d, 0);
                sourceModel.RestoreInstanceNumbersFromExisting();

                // 添加 Auto Wire 和三折点 Manual Wire
                // Auto Wire：source-001 -> resistor-001（"resistor-001" < "source-001"，序列化会规范化方向）
                sourceModel.AddWire("source-001", "positive", "resistor-001", "positive");
                // Manual Wire：使用已规范化方向 ground-001 -> source-001（字典序较小者在 start），避免端点交换导致折点倒序
                var waypoints = new[] { new Vector2(50f, 0f), new Vector2(50f, 50f), new Vector2(150f, 50f) };
                sourceModel.AddWire("ground-001", "ground", "source-001", "negative", SpiceWireVisualState.Manual(waypoints));

                var json = SpiceDrawingSerializer.ToJson(sourceModel);

                if (!workspace.TryImportDrawingJson(json, out var error))
                    throw new InvalidOperationException("导入完整电路应成功：" + error);

                // 验证组件数量
                if (workspace.Model.Components.Count != 10)
                    throw new InvalidOperationException("导入后组件数量应为 10，实际 " + workspace.Model.Components.Count);

                // 验证各组件字段（InstanceId、Kind、Position、Rotation、SiValue）
                var source = workspace.Model.FindComponent("source-001");
                if (source == null || source.Kind != SpiceComponentKind.DcVoltageSource) throw new InvalidOperationException("导入后电压源丢失或类型错误。");
                if (source.Position != new Vector2(0f, 0f)) throw new InvalidOperationException("导入后电压源位置不匹配。");
                if (source.RotationQuarterTurns != 0) throw new InvalidOperationException("导入后电压源旋转不匹配。");
                if (Math.Abs(source.SiValue - 10d) > 1e-12) throw new InvalidOperationException("导入后电压源参数不匹配。");

                var resistor = workspace.Model.FindComponent("resistor-001");
                if (resistor == null || resistor.RotationQuarterTurns != 1) throw new InvalidOperationException("导入后电阻旋转应为 1。");
                if (Math.Abs(resistor.SiValue - 2000d) > 1e-12) throw new InvalidOperationException("导入后电阻参数不匹配。");

                var capacitor = workspace.Model.FindComponent("capacitor-001");
                if (capacitor == null || capacitor.RotationQuarterTurns != 2) throw new InvalidOperationException("导入后电容旋转应为 2。");

                var inductor = workspace.Model.FindComponent("inductor-001");
                if (inductor == null || inductor.RotationQuarterTurns != 3) throw new InvalidOperationException("导入后电感旋转应为 3。");

                // 验证开关状态（Closed = SiValue 1）
                var switchComponent = workspace.Model.FindComponent("switch-001");
                if (switchComponent == null || Math.Abs(switchComponent.SiValue - 1d) > 1e-12)
                    throw new InvalidOperationException("导入后开关状态不匹配（应为 Closed=1）。");

                // 验证 Wire 数量和路由模式
                if (workspace.Model.Wires.Count != 2) throw new InvalidOperationException("导入后导线数量应为 2。");
                SpiceWorkspaceWireData manualWire = null, autoWire = null;
                foreach (var wire in workspace.Model.Wires)
                {
                    if (wire.VisualState.RouteMode == SpiceWireRouteMode.Manual) manualWire = wire;
                    else if (wire.VisualState.RouteMode == SpiceWireRouteMode.Auto) autoWire = wire;
                }
                if (autoWire == null) throw new InvalidOperationException("导入后应存在 Auto Wire。");
                if (manualWire == null) throw new InvalidOperationException("导入后应存在 Manual Wire。");

                // 逐点验证 ManualRoutePoints 坐标与顺序（端点已规范化为 ground-001 -> source-001，无交换）
                if (manualWire.StartComponentId != "ground-001" || manualWire.StartTerminalId != "ground")
                    throw new InvalidOperationException("导入后 Manual Wire 起始端点不匹配：" + manualWire.StartComponentId + "/" + manualWire.StartTerminalId);
                if (manualWire.EndComponentId != "source-001" || manualWire.EndTerminalId != "negative")
                    throw new InvalidOperationException("导入后 Manual Wire 结束端点不匹配：" + manualWire.EndComponentId + "/" + manualWire.EndTerminalId);
                if (manualWire.VisualState.RouteMode != SpiceWireRouteMode.Manual)
                    throw new InvalidOperationException("导入后 Manual Wire RouteMode 应为 Manual。");
                if (manualWire.VisualState.Waypoints.Count != 3)
                    throw new InvalidOperationException("导入后 Manual Wire 折点数量应为 3，实际 " + manualWire.VisualState.Waypoints.Count);
                var wpTolerance = 1e-5f;
                if (Mathf.Abs(manualWire.VisualState.Waypoints[0].x - 50f) > wpTolerance || Mathf.Abs(manualWire.VisualState.Waypoints[0].y - 0f) > wpTolerance)
                    throw new InvalidOperationException("导入后 Manual Wire Waypoints[0] 应为 (50, 0)，实际 " + manualWire.VisualState.Waypoints[0]);
                if (Mathf.Abs(manualWire.VisualState.Waypoints[1].x - 50f) > wpTolerance || Mathf.Abs(manualWire.VisualState.Waypoints[1].y - 50f) > wpTolerance)
                    throw new InvalidOperationException("导入后 Manual Wire Waypoints[1] 应为 (50, 50)，实际 " + manualWire.VisualState.Waypoints[1]);
                if (Mathf.Abs(manualWire.VisualState.Waypoints[2].x - 150f) > wpTolerance || Mathf.Abs(manualWire.VisualState.Waypoints[2].y - 50f) > wpTolerance)
                    throw new InvalidOperationException("导入后 Manual Wire Waypoints[2] 应为 (150, 50)，实际 " + manualWire.VisualState.Waypoints[2]);

                // 验证 Auto Wire 仍为 Auto
                if (autoWire.VisualState.RouteMode != SpiceWireRouteMode.Auto)
                    throw new InvalidOperationException("导入后 Auto Wire RouteMode 应为 Auto。");

                // 验证编号恢复：导入 R1 后新建电阻应为 R2
                var newResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.up * 200f);
                if (newResistor.InstanceId != "resistor-002")
                    throw new InvalidOperationException("导入后新建电阻应为 resistor-002，实际 " + newResistor.InstanceId);

                // 验证结果状态和无 pending wire
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun)
                    throw new InvalidOperationException("导入后结果状态应为 NeverRun。");
                if (workspace.HasPendingWire)
                    throw new InvalidOperationException("导入后不应有 pending wire。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // 场景 B：无效导入完全不破坏当前 Workspace。
        private static void ValidateDrawingImportFailurePreservesWorkspace()
        {
            var canvasRoot = new GameObject("SpiceImportFailureValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 建立非空旧画布
                var oldSource = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                var oldResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 100f);
                workspace.Connect(oldSource.InstanceId, "positive", oldResistor.InstanceId, "positive");
                var oldModel = workspace.Model;
                var oldComponentCount = oldModel.Components.Count;
                var oldWireCount = oldModel.Wires.Count;
                var oldResultState = workspace.ResultState;

                // 各种无效 JSON
                var invalidInputs = new[]
                {
                    "",                                                                                              // 空 JSON
                    "{\"format\":\"Wrong\",\"schemaVersion\":1,\"components\":[],\"wires\":[]}",                          // 错误 format
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":99,\"components\":[],\"wires\":[]}", // 未知 schemaVersion
                    // 缺失完整 position（x/y 为字符串字段，缺失时为 null，TryFromJson 必须拒绝）
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}",
                    // 非法 InstanceId
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"resistor-1\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1000\"}],\"wires\":[]}",
                    // 悬空 Wire 引用
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[],\"wires\":[{\"startComponentId\":\"resistor-001\",\"startTerminalId\":\"positive\",\"endComponentId\":\"resistor-002\",\"endTerminalId\":\"negative\",\"routeMode\":\"Auto\"}]}",
                    // 非法参数（0 欧姆电阻）
                    "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"resistor-001\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0\"}],\"wires\":[]}"
                };

                for (var i = 0; i < invalidInputs.Length; i++)
                {
                    var invalidJson = invalidInputs[i];
                    if (workspace.TryImportDrawingJson(invalidJson, out var error))
                        throw new InvalidOperationException("无效 JSON #" + i + " 应被拒绝：" + invalidJson.Substring(0, Math.Min(80, invalidJson.Length)));
                    if (string.IsNullOrEmpty(error)) throw new InvalidOperationException("无效 JSON #" + i + " 应返回错误信息。");

                    // 旧 Model 引用不变
                    if (!ReferenceEquals(workspace.Model, oldModel))
                        throw new InvalidOperationException("失败导入后 Model 引用不应改变。");

                    // 旧画布数据不变
                    if (workspace.Model.Components.Count != oldComponentCount)
                        throw new InvalidOperationException("失败导入后组件数量不应改变。");
                    if (workspace.Model.Wires.Count != oldWireCount)
                        throw new InvalidOperationException("失败导入后导线数量不应改变。");

                    // 旧组件字段不变
                    var stillSource = workspace.Model.FindComponent(oldSource.InstanceId);
                    if (stillSource == null || stillSource.Position != oldSource.Position || stillSource.SiValue != oldSource.SiValue)
                        throw new InvalidOperationException("失败导入后旧组件字段不应改变。");

                    // 旧结果状态不变
                    if (workspace.ResultState != oldResultState)
                        throw new InvalidOperationException("失败导入后结果状态不应改变。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // 场景 C：连续成功导入，最终只有第二次导入的内容。
        private static void ValidateDrawingImportConsecutiveSuccess()
        {
            var canvasRoot = new GameObject("SpiceImportConsecutiveValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 图纸 A：包含 source-001 和 resistor-001
                var modelA = new SpiceWorkspaceModel();
                modelA.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", Vector2.zero, 10d, 0);
                modelA.AddComponentWithIdentity(SpiceComponentKind.Resistor, "resistor-001", Vector2.right, 1000d, 0);
                modelA.RestoreInstanceNumbersFromExisting();
                modelA.AddWire("source-001", "positive", "resistor-001", "positive");
                var jsonA = SpiceDrawingSerializer.ToJson(modelA);

                // 图纸 B：包含 capacitor-001 和 inductor-001
                var modelB = new SpiceWorkspaceModel();
                modelB.AddComponentWithIdentity(SpiceComponentKind.Capacitor, "capacitor-001", Vector2.left, 1e-6d, 0);
                modelB.AddComponentWithIdentity(SpiceComponentKind.Inductor, "inductor-001", Vector2.right, 0.01d, 0);
                modelB.RestoreInstanceNumbersFromExisting();
                modelB.AddWire("capacitor-001", "positive", "inductor-001", "positive");
                var jsonB = SpiceDrawingSerializer.ToJson(modelB);

                // 先导入 A
                if (!workspace.TryImportDrawingJson(jsonA, out var errorA))
                    throw new InvalidOperationException("导入图纸 A 应成功：" + errorA);
                if (workspace.Model.Components.Count != 2 || workspace.Model.FindComponent("source-001") == null)
                    throw new InvalidOperationException("导入 A 后应包含 source-001。");

                // 再导入 B
                if (!workspace.TryImportDrawingJson(jsonB, out var errorB))
                    throw new InvalidOperationException("导入图纸 B 应成功：" + errorB);

                // 最终只有 B 的内容
                if (workspace.Model.Components.Count != 2)
                    throw new InvalidOperationException("导入 B 后组件数量应为 2。");
                if (workspace.Model.FindComponent("source-001") != null)
                    throw new InvalidOperationException("导入 B 后不应残留 source-001。");
                if (workspace.Model.FindComponent("resistor-001") != null)
                    throw new InvalidOperationException("导入 B 后不应残留 resistor-001。");
                if (workspace.Model.FindComponent("capacitor-001") == null)
                    throw new InvalidOperationException("导入 B 后应包含 capacitor-001。");
                if (workspace.Model.FindComponent("inductor-001") == null)
                    throw new InvalidOperationException("导入 B 后应包含 inductor-001。");
                if (workspace.Model.Wires.Count != 1)
                    throw new InvalidOperationException("导入 B 后导线数量应为 1。");

                // 验证编号恢复：新建电容应为 capacitor-002
                var newCapacitor = workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.up);
                if (newCapacitor.InstanceId != "capacitor-002")
                    throw new InvalidOperationException("导入 B 后新建电容应为 capacitor-002，实际 " + newCapacitor.InstanceId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // 场景 D：空图纸导入，画布清空，编号从 1 开始。
        private static void ValidateDrawingImportEmptyDrawing()
        {
            var canvasRoot = new GameObject("SpiceImportEmptyValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 先建立非空画布
                workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right);

                // 导入空图纸
                var emptyJson = SpiceDrawingSerializer.ToJson(new SpiceWorkspaceModel());
                if (!workspace.TryImportDrawingJson(emptyJson, out var error))
                    throw new InvalidOperationException("导入空图纸应成功：" + error);

                // 画布为空
                if (workspace.Model.Components.Count != 0)
                    throw new InvalidOperationException("导入空图纸后组件数量应为 0。");
                if (workspace.Model.Wires.Count != 0)
                    throw new InvalidOperationException("导入空图纸后导线数量应为 0。");

                // 编号从 1 开始
                var newResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                if (newResistor.InstanceId != "resistor-001")
                    throw new InvalidOperationException("空图纸导入后新建电阻应为 resistor-001，实际 " + newResistor.InstanceId);

                // 结果状态为 NeverRun
                if (workspace.ResultState != SpiceWorkspaceResultState.NeverRun)
                    throw new InvalidOperationException("空图纸导入后结果状态应为 NeverRun。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // 场景 E：仿真计算进行中（Running）禁止导入，保留当前计算和状态。
        private static void ValidateDrawingImportRejectedWhileRunning()
        {
            var canvasRoot = new GameObject("SpiceImportRunningGuardValidation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspaceForCopy(canvasRoot.transform, out _);

                // 建立非空旧画布
                var oldSource = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
                var oldResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.right * 100f);
                workspace.Connect(oldSource.InstanceId, "positive", oldResistor.InstanceId, "positive");
                var oldModel = workspace.Model;
                var oldComponentCount = oldModel.Components.Count;
                var oldWireCount = oldModel.Wires.Count;

                // 构造一个合法的导入 JSON
                var importModel = new SpiceWorkspaceModel();
                importModel.AddComponentWithIdentity(SpiceComponentKind.DcVoltageSource, "source-001", Vector2.left, 5d, 0);
                importModel.RestoreInstanceNumbersFromExisting();
                var validJson = SpiceDrawingSerializer.ToJson(importModel);

                // 设置 Running 状态（不实际启动 ngspice）
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Running);
                if (workspace.ResultState != SpiceWorkspaceResultState.Running)
                    throw new InvalidOperationException("测试前置：无法设置 Running 状态。");

                // 尝试导入合法 JSON，应被拒绝
                if (workspace.TryImportDrawingJson(validJson, out var error))
                    throw new InvalidOperationException("Running 状态下导入应被拒绝。");
                if (string.IsNullOrEmpty(error))
                    throw new InvalidOperationException("Running 状态下应返回错误信息。");
                // 错误信息应包含"计算"或"运行"
                if (error.IndexOf("计算", StringComparison.Ordinal) < 0 && error.IndexOf("运行", StringComparison.Ordinal) < 0)
                    throw new InvalidOperationException("Running 状态错误信息应包含'计算'或'运行'，实际：" + error);

                // Model 引用不变
                if (!ReferenceEquals(workspace.Model, oldModel))
                    throw new InvalidOperationException("Running 拒绝导入后 Model 引用不应改变。");
                // 组件/Wire 数量不变
                if (workspace.Model.Components.Count != oldComponentCount)
                    throw new InvalidOperationException("Running 拒绝导入后组件数量不应改变。");
                if (workspace.Model.Wires.Count != oldWireCount)
                    throw new InvalidOperationException("Running 拒绝导入后导线数量不应改变。");
                // ResultState 仍为 Running
                if (workspace.ResultState != SpiceWorkspaceResultState.Running)
                    throw new InvalidOperationException("Running 拒绝导入后 ResultState 仍应为 Running。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // 场景 F：真实 JSON 缺失 position 必须被 TryFromJson 拒绝。
        // 覆盖：缺失完整 position、缺失 x、缺失 y、显式 (0,0) 成功、DTO null position 继续拒绝。
        private static void ValidateDrawingImportMissingPositionRejected()
        {
            // A. 缺失完整 position
            var missingPositionJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(missingPositionJson, out _, out var errorA))
                throw new InvalidOperationException("缺失完整 position 的 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(errorA)) throw new InvalidOperationException("缺失 position 应返回错误信息。");

            // B. position 存在但缺失 x（JsonUtility 将缺失的 string 字段置为 null）
            var missingXJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(missingXJson, out _, out var errorB))
                throw new InvalidOperationException("缺失 position.x 的 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(errorB)) throw new InvalidOperationException("缺失 position.x 应返回错误信息。");

            // C. position 存在但缺失 y
            var missingYJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (SpiceDrawingSerializer.TryFromJson(missingYJson, out _, out var errorC))
                throw new InvalidOperationException("缺失 position.y 的 JSON 应被拒绝。");
            if (string.IsNullOrEmpty(errorC)) throw new InvalidOperationException("缺失 position.y 应返回错误信息。");

            // D. position x/y 显式为零必须成功
            var zeroPositionJson = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}],\"wires\":[]}";
            if (!SpiceDrawingSerializer.TryFromJson(zeroPositionJson, out var zeroModel, out var errorD))
                throw new InvalidOperationException("显式 (0,0) position 应被允许：" + errorD);
            var zeroSource = zeroModel.FindComponent("source-001");
            if (zeroSource == null || zeroSource.Position != Vector2.zero)
                throw new InvalidOperationException("显式 (0,0) position 导入后位置应为 (0,0)。");

            // E. DTO 级 position = null 继续被拒绝
            var nullPositionDto = new SpiceDrawingFileDto
            {
                format = SpiceDrawingFormat.Format,
                schemaVersion = SpiceDrawingFormat.SchemaVersion,
                components = new System.Collections.Generic.List<SpiceComponentDto>
                {
                    new SpiceComponentDto
                    {
                        instanceId = "source-001",
                        componentType = SpiceComponentKind.DcVoltageSource.ToString(),
                        position = null,
                        rotationQuarterTurns = 0,
                        siValueText = "10"
                    }
                }
            };
            if (SpiceDrawingSerializer.TryFromDto(nullPositionDto, out _, out var errorE))
                throw new InvalidOperationException("DTO 级 position=null 应继续被拒绝。");
            if (string.IsNullOrEmpty(errorE)) throw new InvalidOperationException("position=null 应返回错误信息。");
        }

        // ===== 文件工作流：文件操作核心与路径会话状态 =====

        // 扩展名规范化：无扩展名补 .spicejson；.spicejson/.SPICEJSON 不重复追加；非 .spicejson 保持不改。
    }
}
