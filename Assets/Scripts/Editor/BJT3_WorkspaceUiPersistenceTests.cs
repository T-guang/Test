using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// BJT3 — Workspace/UI/持久化接入 Generic NPN/PNP 的正式测试（G 系列）。
    /// 模式遵循 BJT2_AcSupportTests / BJT1C_DcParserResultTests：静态类 + MenuItem + Run/Check 辅助（失败抛异常）。
    /// 无头 UI 构造参照 SpiceT3WorkspaceValidation.Presentation.cs 的 CreateInitializedWorkspaceForCopy：
    /// 正式 SpiceWorkspaceViewBindings + SpiceWorkspaceDemoHost.Initialize + 激活后几何同步。
    /// 本文件只读生产 API；对 private 成员（DesignatorFor / PaletteLabel / IsComponentKindSupportedInCurrentAnalysis）
    /// 使用反射调用，不扩大生产可见性。
    /// </summary>
    public static class BJT3_WorkspaceUiPersistenceTests
    {
        private static int _passed;
        private static int _failed;
        private static readonly StringBuilder Summary = new StringBuilder();

        [MenuItem("Tools/Tests/SPICE/BJT3 Workspace UI Persistence Tests")]
        public static void RunAllTests()
        {
            _passed = 0;
            _failed = 0;
            Summary.Clear();
            Summary.AppendLine("# BJT3 Workspace/UI/Persistence Tests (G1-G13)");

            Run("G1_RecommendedInstanceId", G1_RecommendedInstanceId);
            Run("G2_ToSpiceComponentModel_Mapping", G2_ToSpiceComponentModel_Mapping);
            Run("G3_NoUserParameter", G3_NoUserParameter);
            Run("G4_ThreeTerminalView_HitTest", G4_ThreeTerminalView_HitTest);
            Run("G5_EmitterArrow_Direction", G5_EmitterArrow_Direction);
            Run("G6_Designator_And_FormatParameter", G6_Designator_And_FormatParameter);
            Run("G7_Palette_And_ModeMatrix", G7_Palette_And_ModeMatrix);
            Run("G8_SaveLoad_Roundtrip_V2", G8_SaveLoad_Roundtrip_V2);
            Run("G9_SchemaV1_RejectsBjt", G9_SchemaV1_RejectsBjt);
            Run("G10_OldFiles_StillReadable", G10_OldFiles_StillReadable);
            Run("G11_StaleRegression", G11_StaleRegression);
            Run("G12_SameComponentWiring_ImportConsistency", G12_SameComponentWiring_ImportConsistency);
            Run("G13_FixedModelParameterPanelDoesNotOverlap", G13_FixedModelParameterPanelDoesNotOverlap);

            var total = _passed + _failed;
            Summary.AppendLine();
            Summary.AppendLine($"RESULT: {_passed}/{total} passed, {_failed} failed.");
            var summaryPath = Path.Combine(LogsDirectory(), "BJT3_Tests_Summary.txt");
            File.WriteAllText(summaryPath, Summary.ToString());
            Debug.Log($"[BJT3] Summary saved: {summaryPath}");
            Debug.Log($"BJT3 Workspace UI Persistence Tests: {_passed}/{total} passed.");
            if (_failed > 0)
            {
                throw new InvalidOperationException($"BJT3 Workspace UI Persistence Tests: {_failed} of {total} FAILED.");
            }
        }

        // ---------- G1：推荐 InstanceId ----------
        private static void G1_RecommendedInstanceId()
        {
            var model = new SpiceWorkspaceModel();
            var npn = model.AddComponent(SpiceComponentKind.GenericNpnBjt, Vector2.zero);
            var pnp = model.AddComponent(SpiceComponentKind.GenericPnpBjt, Vector2.right * 100f);
            var npn2 = model.AddComponent(SpiceComponentKind.GenericNpnBjt, Vector2.up * 100f);
            CheckEqual("npn-001", npn.InstanceId, "首个 NPN 的推荐 InstanceId。");
            CheckEqual("pnp-001", pnp.InstanceId, "首个 PNP 的推荐 InstanceId。");
            CheckEqual("npn-002", npn2.InstanceId, "第二个 NPN 的推荐 InstanceId。");
            CheckEqual("npn", SpiceWorkspaceModel.GetExpectedPrefix(SpiceComponentKind.GenericNpnBjt), "NPN 前缀。");
            CheckEqual("pnp", SpiceWorkspaceModel.GetExpectedPrefix(SpiceComponentKind.GenericPnpBjt), "PNP 前缀。");
            CheckTrue(SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.GenericNpnBjt, "npn-001"), "IsValidInstanceId(npn-001) 必须为 true。");
            CheckTrue(SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.GenericPnpBjt, "pnp-001"), "IsValidInstanceId(pnp-001) 必须为 true。");
            CheckFalse(SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.GenericNpnBjt, "q1"), "IsValidInstanceId 必须拒绝 'q1'（Q 编号与 workspace 身份分离）。");
            CheckFalse(SpiceWorkspaceModel.IsValidInstanceId(SpiceComponentKind.GenericNpnBjt, "npn-1"), "IsValidInstanceId 必须拒绝 'npn-1'（后缀不足三位）。");
            Note($"G1: npn-001/pnp-001/npn-002 分配正确；'q1'/'npn-1' 被拒绝。");
        }

        // ---------- G2：ToSpiceComponentModel 映射 ----------
        private static void G2_ToSpiceComponentModel_Mapping()
        {
            var model = new SpiceWorkspaceModel();
            var npnData = model.AddComponent(SpiceComponentKind.GenericNpnBjt, new Vector2(10f, 20f));
            var pnpData = model.AddComponent(SpiceComponentKind.GenericPnpBjt, new Vector2(-30f, 40f));

            var npnMapped = npnData.ToSpiceComponentModel();
            var npnExpected = SpiceComponentModel.GenericNpnBjt("npn-001");
            CheckEqual(npnExpected.Kind, npnMapped.Kind, "NPN 映射 Kind。");
            CheckEqual(npnExpected.InstanceId, npnMapped.InstanceId, "NPN 映射保留 InstanceId。");
            CheckEqual(0, npnMapped.Parameters.Count, "NPN 映射不得携带任何 SiValue 参数。");

            var pnpMapped = pnpData.ToSpiceComponentModel();
            var pnpExpected = SpiceComponentModel.GenericPnpBjt("pnp-001");
            CheckEqual(pnpExpected.Kind, pnpMapped.Kind, "PNP 映射 Kind。");
            CheckEqual(pnpExpected.InstanceId, pnpMapped.InstanceId, "PNP 映射保留 InstanceId。");
            CheckEqual(0, pnpMapped.Parameters.Count, "PNP 映射不得携带任何 SiValue 参数。");

            // 与 Core 工厂等价：端子契约一致。
            foreach (var terminal in new[] { SpiceComponentModel.CollectorTerminalId, SpiceComponentModel.BaseTerminalId, SpiceComponentModel.EmitterTerminalId })
            {
                CheckTrue(npnMapped.HasTerminal(terminal), "NPN SpiceComponentModel 必须拥有端子 " + terminal + "。");
                CheckTrue(pnpMapped.HasTerminal(terminal), "PNP SpiceComponentModel 必须拥有端子 " + terminal + "。");
            }
            Note("G2: Kind/InstanceId 与 Core 工厂等价，Parameters.Count=0（无 SiValue 语义）。");
        }

        // ---------- G3：BJT 无用户参数 ----------
        private static void G3_NoUserParameter()
        {
            CheckFalse(SpiceWorkspaceModel.HasUserParameter(SpiceComponentKind.GenericNpnBjt), "NPN 不得有用户参数。");
            CheckFalse(SpiceWorkspaceModel.HasUserParameter(SpiceComponentKind.GenericPnpBjt), "PNP 不得有用户参数。");
            foreach (var value in new[] { 1.5d, -3d, 100d, 0.001d })
            {
                CheckFalse(SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.GenericNpnBjt, value), $"IsValidParameter(NPN, {value}) 必须为 false。");
                CheckFalse(SpiceWorkspaceModel.IsValidParameter(SpiceComponentKind.GenericPnpBjt, value), $"IsValidParameter(PNP, {value}) 必须为 false。");
            }
            var model = new SpiceWorkspaceModel();
            var npn = model.AddComponent(SpiceComponentKind.GenericNpnBjt, Vector2.zero);
            CheckFalse(model.TrySetParameter(npn.InstanceId, 5d), "Model.TrySetParameter 必须拒绝 BJT 参数写入。");
            Note("G3: HasUserParameter(BJT)=false；IsValidParameter(BJT, 任意非零值)=false；TrySetParameter 拒绝。");
        }

        // ---------- G4：三端端子视图与命中 ----------
        private static void G4_ThreeTerminalView_HitTest()
        {
            var canvasRoot = new GameObject("SpiceBjtG4Validation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, out _);
                foreach (var kind in new[] { SpiceComponentKind.GenericNpnBjt, SpiceComponentKind.GenericPnpBjt })
                {
                    var data = workspace.CreateComponent(kind, Vector2.zero);
                    CheckTrue(data != null, kind + " 创建失败。");
                    var view = workspace.GetComponentViewForTesting(data.InstanceId);
                    CheckTrue(view != null, kind + " 未创建元件视图。");
                    var symbolRoot = view.transform.Find("SymbolRoot");
                    CheckTrue(symbolRoot != null, kind + " 缺少 SymbolRoot。");

                    var terminalRects = new Dictionary<string, RectTransform>();
                    for (var i = 0; i < symbolRoot.childCount; i++)
                    {
                        var child = symbolRoot.GetChild(i);
                        if (child.name.StartsWith("Terminal_", StringComparison.Ordinal))
                            terminalRects[child.name.Substring("Terminal_".Length)] = child as RectTransform;
                    }
                    CheckEqual(3, terminalRects.Count, kind + " 端子视图数量必须恰好为 3。");
                    foreach (var terminal in new[] { SpiceComponentModel.CollectorTerminalId, SpiceComponentModel.BaseTerminalId, SpiceComponentModel.EmitterTerminalId })
                        CheckTrue(terminalRects.ContainsKey(terminal), kind + " 缺少端子视图 " + terminal + "。");

                    // 与实现坐标一致（SpiceWorkspaceComponentView.BuildSymbolRoot BJT 分支）。
                    CheckVector(new Vector2(46f, 40f), terminalRects[SpiceComponentModel.CollectorTerminalId].anchoredPosition, kind + " collector 端子坐标。");
                    CheckVector(new Vector2(-66f, 0f), terminalRects[SpiceComponentModel.BaseTerminalId].anchoredPosition, kind + " base 端子坐标。");
                    CheckVector(new Vector2(46f, -40f), terminalRects[SpiceComponentModel.EmitterTerminalId].anchoredPosition, kind + " emitter 端子坐标。");

                    // 命中区域互不重叠：16×16 端子中心距必须大于 16。
                    var positions = terminalRects.Values.Select(rect => rect.anchoredPosition).ToArray();
                    for (var i = 0; i < positions.Length; i++)
                        for (var j = i + 1; j < positions.Length; j++)
                            CheckTrue(Vector2.Distance(positions[i], positions[j]) > 16f, kind + " 端子命中区域重叠。");

                    // GetTerminalPosition（workspace 局部坐标）与实现坐标一致且互不重叠。
                    var collectorPos = view.GetTerminalPosition(SpiceComponentModel.CollectorTerminalId);
                    var basePos = view.GetTerminalPosition(SpiceComponentModel.BaseTerminalId);
                    var emitterPos = view.GetTerminalPosition(SpiceComponentModel.EmitterTerminalId);
                    CheckVector(new Vector2(112f, 40f), collectorPos - basePos, kind + " collector-base 相对位置。");
                    CheckVector(new Vector2(0f, 80f), collectorPos - emitterPos, kind + " collector-emitter 相对位置。");
                    CheckTrue(Vector2.Distance(collectorPos, basePos) > 16f && Vector2.Distance(collectorPos, emitterPos) > 16f && Vector2.Distance(basePos, emitterPos) > 16f,
                        kind + " GetTerminalPosition 三端位置重叠。");

                    // Data.HasTerminal 契约。
                    foreach (var terminal in new[] { SpiceComponentModel.CollectorTerminalId, SpiceComponentModel.BaseTerminalId, SpiceComponentModel.EmitterTerminalId })
                        CheckTrue(view.Data.HasTerminal(terminal), kind + " Data.HasTerminal(" + terminal + ") 必须为 true。");
                    CheckFalse(view.Data.HasTerminal("gate"), kind + " Data.HasTerminal(gate) 必须为 false。");
                    CheckFalse(view.Data.HasTerminal("drain"), kind + " Data.HasTerminal(drain) 必须为 false。");
                }
                Note("G4: NPN/PNP 均恰有 collector/base/emitter 三个端子视图，坐标 (46,40)/(-66,0)/(46,-40)，互不重叠；HasTerminal 拒绝 gate/drain。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // ---------- G5：emitter 箭头方向 ----------
        // 依据 SpiceWorkspaceComponentView.BuildSchematicSymbol 的实现坐标：
        // emitter 引线 (-14,-12)→(46,-40)；箭头 tip = start + dir*(NPN?40:16)；
        // NPN barbRoot = tip - dir*11（barb 落后 tip，箭头沿引线 outward 指向 emitter 端子）；
        // PNP barbRoot = tip + dir*11（barb 超前 tip，箭头 inward 指向 base）。
        private static void G5_EmitterArrow_Direction()
        {
            var canvasRoot = new GameObject("SpiceBjtG5Validation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, out _);
                foreach (var kind in new[] { SpiceComponentKind.GenericNpnBjt, SpiceComponentKind.GenericPnpBjt })
                {
                    var isNpn = kind == SpiceComponentKind.GenericNpnBjt;
                    var data = workspace.CreateComponent(kind, Vector2.zero);
                    var view = workspace.GetComponentViewForTesting(data.InstanceId);
                    var segments = GetSymbolSegments(view);
                    CheckEqual(6, segments.Count, kind + " 符号线段数量必须为 6（base 线/base 引线/collector 引线/emitter 引线/2 条箭头短线）。");

                    var emitterStart = new Vector2(-14f, -12f);
                    var emitterEnd = new Vector2(46f, -40f);
                    var emitterDirection = (emitterEnd - emitterStart).normalized;
                    var emitterPerp = new Vector2(-emitterDirection.y, emitterDirection.x);
                    var arrowTip = emitterStart + emitterDirection * (isNpn ? 40f : 16f);
                    var barbRoot = isNpn ? arrowTip - emitterDirection * 11f : arrowTip + emitterDirection * 11f;

                    var expected = new[]
                    {
                        (new Vector2(-14f, -24f), new Vector2(-14f, 24f)),
                        (new Vector2(-66f, 0f), new Vector2(-14f, 0f)),
                        (new Vector2(-14f, 12f), new Vector2(46f, 40f)),
                        (emitterStart, emitterEnd),
                        (arrowTip, barbRoot + emitterPerp * 6.5f),
                        (arrowTip, barbRoot - emitterPerp * 6.5f)
                    };
                    foreach (var segment in expected)
                        CheckTrue(ContainsSegment(segments, segment.Item1, segment.Item2), kind + " 符号缺少预期线段 " + segment.Item1 + "->" + segment.Item2 + "。");

                    // 方向断言：
                    // NPN outward —— tip 沿引线方向比 barbRoot 更靠 emitter 端子侧，dot(tip-barbRoot, dir) > 0；
                    // PNP inward  —— barbRoot 比 tip 更靠 emitter 端子侧，dot(tip-barbRoot, dir) < 0（箭头指向 base）。
                    var outward = Vector2.Dot(arrowTip - barbRoot, emitterDirection);
                    if (isNpn)
                    {
                        CheckTrue(outward > 0f, "NPN 箭头必须 outward（指向 emitter 端子），实测 dot=" + outward + "。");
                        CheckTrue(Vector2.Distance(arrowTip, emitterEnd) < Vector2.Distance(barbRoot, emitterEnd), "NPN 箭头 tip 必须比 barbRoot 更接近 emitter 端子。");
                    }
                    else
                    {
                        CheckTrue(outward < 0f, "PNP 箭头必须 inward（指向 base），实测 dot=" + outward + "。");
                        CheckTrue(Vector2.Distance(arrowTip, emitterEnd) > Vector2.Distance(barbRoot, emitterEnd), "PNP 箭头 tip 必须比 barbRoot 更远离 emitter 端子（指向 base）。");
                    }
                }
                Note("G5: 箭头几何按实现坐标重建并全部命中；NPN tip 在引线 40px 处 outward，PNP tip 在 16px 处 inward。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // ---------- G6：DesignatorFor 与 FormatParameter ----------
        private static void G6_Designator_And_FormatParameter()
        {
            var designatorMethod = typeof(SpiceWorkspaceComponentView).GetMethod("DesignatorFor", BindingFlags.Static | BindingFlags.NonPublic);
            CheckTrue(designatorMethod != null, "反射未找到 SpiceWorkspaceComponentView.DesignatorFor。");
            string DesignatorFor(SpiceWorkspaceComponentData component) => (string)designatorMethod.Invoke(null, new object[] { component });

            CheckEqual("Q1", DesignatorFor(new SpiceWorkspaceComponentData("npn-001", SpiceComponentKind.GenericNpnBjt, Vector2.zero, 0d)), "NPN DesignatorFor。");
            CheckEqual("Q7", DesignatorFor(new SpiceWorkspaceComponentData("pnp-007", SpiceComponentKind.GenericPnpBjt, Vector2.zero, 0d)), "PNP DesignatorFor。");

            // 旧器件不回归（抽查）。
            CheckEqual("R3", DesignatorFor(new SpiceWorkspaceComponentData("resistor-003", SpiceComponentKind.Resistor, Vector2.zero, 3000d)), "电阻 DesignatorFor 回归。");
            CheckEqual("V2", DesignatorFor(new SpiceWorkspaceComponentData("source-002", SpiceComponentKind.DcVoltageSource, Vector2.zero, 5d)), "DC 电压源 DesignatorFor 回归。");
            CheckEqual("D1", DesignatorFor(new SpiceWorkspaceComponentData("diode-001", SpiceComponentKind.SiliconDiode, Vector2.zero, 0d)), "二极管 DesignatorFor 回归。");

            CheckEqual(SpiceComponentDefaults.NpnGenericModelName, SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.GenericNpnBjt, 0d), "NPN FormatParameter。");
            CheckEqual(SpiceComponentDefaults.PnpGenericModelName, SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.GenericPnpBjt, 0d), "PNP FormatParameter。");

            // 旧器件 FormatParameter 语义不回归（抽查）。
            CheckEqual("2 kΩ", SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Resistor, 2000d), "电阻 FormatParameter 回归。");
            CheckEqual("D_GENERIC", SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.SiliconDiode, 0d), "二极管 FormatParameter 回归。");
            CheckEqual("GND", SpiceWorkspaceDisplay.FormatParameter(SpiceComponentKind.Ground, 0d), "GND FormatParameter 回归。");
            Note("G6: DesignatorFor NPN/PNP → Q1/Q7（旧件 R3/V2/D1 不变）；FormatParameter → NPN_GENERIC/PNP_GENERIC（旧件语义不变）。");
        }

        // ---------- G7：元件池标签与模式矩阵 ----------
        private static void G7_Palette_And_ModeMatrix()
        {
            var paletteLabelMethod = typeof(SpiceWorkspaceController).GetMethod("PaletteLabel", BindingFlags.Static | BindingFlags.NonPublic);
            CheckTrue(paletteLabelMethod != null, "反射未找到 SpiceWorkspaceController.PaletteLabel。");
            string PaletteLabel(SpiceComponentKind kind) => (string)paletteLabelMethod.Invoke(null, new object[] { kind });
            CheckEqual("通用 NPN 三极管", PaletteLabel(SpiceComponentKind.GenericNpnBjt), "NPN PaletteLabel。");
            CheckEqual("通用 PNP 三极管", PaletteLabel(SpiceComponentKind.GenericPnpBjt), "PNP PaletteLabel。");

            var canvasRoot = new GameObject("SpiceBjtG7Validation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, out _);
                var supportedMethod = typeof(SpiceWorkspaceController).GetMethod("IsComponentKindSupportedInCurrentAnalysis", BindingFlags.Instance | BindingFlags.NonPublic);
                CheckTrue(supportedMethod != null, "反射未找到 IsComponentKindSupportedInCurrentAnalysis。");
                bool Supported(SpiceComponentKind kind) => (bool)supportedMethod.Invoke(workspace, new object[] { kind });

                // DC 模式：BJT 与 DC 源允许，AC 源拒绝。
                CheckEqual(SpiceAnalysisMode.DcOperatingPoint, workspace.Model.AnalysisMode, "初始分析模式应为 DC。");
                CheckTrue(Supported(SpiceComponentKind.GenericNpnBjt), "DC 模式 NPN 必须受支持。");
                CheckTrue(Supported(SpiceComponentKind.GenericPnpBjt), "DC 模式 PNP 必须受支持。");
                CheckTrue(Supported(SpiceComponentKind.DcVoltageSource), "DC 模式 DcVoltageSource 必须受支持。");
                CheckFalse(Supported(SpiceComponentKind.AcVoltageSource), "DC 模式 AcVoltageSource 必须不受支持。");

                // AC 模式：BJT 与 DcVoltageSource（偏置源）允许；DcCurrentSource/SiliconDiode 仍拒绝（不得放宽）。
                CheckTrue(workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency), "切换到 AC 模式失败。");
                CheckTrue(Supported(SpiceComponentKind.GenericNpnBjt), "AC 模式 NPN 必须受支持。");
                CheckTrue(Supported(SpiceComponentKind.GenericPnpBjt), "AC 模式 PNP 必须受支持。");
                CheckTrue(Supported(SpiceComponentKind.DcVoltageSource), "AC 模式 DcVoltageSource 必须受支持。");
                CheckFalse(Supported(SpiceComponentKind.DcCurrentSource), "AC 模式 DcCurrentSource 必须仍被拒绝。");
                CheckFalse(Supported(SpiceComponentKind.SiliconDiode), "AC 模式 SiliconDiode 必须仍被拒绝。");

                // 元件池卡片可用性跟随同一矩阵刷新。
                CheckTrue(workspace.GetPaletteCardForTesting(SpiceComponentKind.GenericNpnBjt).interactable, "AC 模式 NPN 元件池卡片必须可用。");
                CheckTrue(workspace.GetPaletteCardForTesting(SpiceComponentKind.DcVoltageSource).interactable, "AC 模式 DcVoltageSource 元件池卡片必须可用。");
                CheckFalse(workspace.GetPaletteCardForTesting(SpiceComponentKind.DcCurrentSource).interactable, "AC 模式 DcCurrentSource 元件池卡片必须禁用。");
                CheckFalse(workspace.GetPaletteCardForTesting(SpiceComponentKind.SiliconDiode).interactable, "AC 模式 SiliconDiode 元件池卡片必须禁用。");
                Note("G7: PaletteLabel 含两条 BJT 中文标签；BJT 在 DC/AC 均受支持，AC 允许 DcVoltageSource、仍拒 DcCurrentSource/SiliconDiode；卡片可用性同步。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // ---------- G8：V2 保存/导入往返 ----------
        private static void G8_SaveLoad_Roundtrip_V2()
        {
            var canvasRoot = new GameObject("SpiceBjtG8Validation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, out _);
                BuildMixedBjtWorkspace(workspace, out var npnId, out var resistorId);
                // 旋转 NPN 一次，验证旋转参与往返。
                workspace.GetComponentViewForTesting(npnId).RotateClockwise();
                var original = workspace.Model;
                CheckEqual(1, original.FindComponent(npnId).RotationQuarterTurns, "旋转未写入数据。");

                // Serializer 层：schemaVersion==2 且全字段保留。
                var dto = SpiceDrawingSerializer.ToDto(original);
                CheckEqual(SpiceDrawingFormat.CurrentSchemaVersion, dto.schemaVersion, "schemaVersion 必须为 2。");
                var json = SpiceDrawingSerializer.ToJson(original);
                CheckTrue(SpiceDrawingSerializer.TryFromJson(json, out var reloaded, out var parseError), "ToJson→TryFromJson 失败：" + parseError);
                AssertModelEquivalent(original, reloaded, "Serializer 往返");

                var sameComponentWire = reloaded.Wires.FirstOrDefault(wire => wire.StartComponentId == npnId && wire.EndComponentId == npnId);
                CheckTrue(sameComponentWire != null, "同元件导线未保留。");
                CheckEqual(SpiceComponentModel.BaseTerminalId, sameComponentWire.StartTerminalId, "同元件导线规范化后 start 端子。");
                CheckEqual(SpiceComponentModel.CollectorTerminalId, sameComponentWire.EndTerminalId, "同元件导线规范化后 end 端子。");

                // Controller 级：真实写盘到临时目录并读回。
                var directory = Path.Combine(LogsDirectory(), "BJT3_G8_Temp");
                Directory.CreateDirectory(directory);
                var filePath = Path.Combine(directory, "bjt3_g8_roundtrip.spicejson");
                CheckTrue(workspace.TrySaveWorkspaceToPath(filePath, out var saveError), "TrySaveWorkspaceToPath 失败：" + saveError);
                CheckTrue(File.Exists(filePath), "图纸文件未写盘。");
                CheckTrue(workspace.TryImportWorkspaceFromPath(filePath, out var importError), "TryImportWorkspaceFromPath 失败：" + importError);
                AssertModelEquivalent(original, workspace.Model, "Controller 往返");
                CheckEqual(SpiceWorkspaceResultState.NeverRun, workspace.ResultState, "导入后结果状态必须为 NeverRun。");
                CheckEqual(Path.GetFullPath(filePath), Path.GetFullPath(workspace.CurrentSpiceFilePath ?? string.Empty), "导入后当前路径必须绑定。");
                Note("G8: V2 往返（Serializer 与 Controller 写盘读回）Kind/InstanceId/位置/旋转/同元件导线端子全保留；schemaVersion=2；导入后 NeverRun。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // ---------- G9：V1 拒绝 BJT ----------
        private static void G9_SchemaV1_RejectsBjt()
        {
            // V1 保存校验：含 BJT 被拒，文案含 BJT 专属措辞。
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
            model.AddComponent(SpiceComponentKind.GenericNpnBjt, Vector2.right * 100f);
            CheckFalse(SpiceDrawingSerializer.TryValidateSchemaV1SaveCompatibility(model, out var saveError), "含 BJT 的 V1 保存必须被拒。");
            CheckTrue(saveError != null && saveError.Contains("三极管"), "V1 保存拒绝文案必须含 BJT 专属措辞，实际：" + saveError);
            CheckTrue(saveError.Contains("NPN") || saveError.Contains("PNP"), "V1 保存拒绝文案必须指明 NPN/PNP，实际：" + saveError);

            var pnpModel = new SpiceWorkspaceModel();
            pnpModel.AddComponent(SpiceComponentKind.GenericPnpBjt, Vector2.zero);
            CheckFalse(SpiceDrawingSerializer.TryValidateSchemaV1SaveCompatibility(pnpModel, out var pnpSaveError), "含 PNP 的 V1 保存必须被拒。");
            CheckTrue(pnpSaveError != null && pnpSaveError.Contains("三极管"), "PNP V1 保存拒绝文案必须含 BJT 专属措辞，实际：" + pnpSaveError);

            // V1 导入：含 BJT 的 JSON 被拒。
            const string v1WithBjt = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1," +
                "\"components\":[{\"instanceId\":\"npn-001\",\"componentType\":\"GenericNpnBjt\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0}]," +
                "\"wires\":[]}";
            CheckFalse(SpiceDrawingSerializer.TryFromJson(v1WithBjt, out _, out var importError), "V1 导入含 BJT 必须被拒。");
            CheckTrue(importError != null && importError.Contains("V1") && importError.Contains("三极管"), "V1 导入拒绝文案必须含 BJT 专属措辞，实际：" + importError);

            // 不含 BJT 的旧式电路 V1 保存仍成功。
            var legacyModel = new SpiceWorkspaceModel();
            var source = legacyModel.AddComponent(SpiceComponentKind.DcVoltageSource, Vector2.zero);
            var resistor = legacyModel.AddComponent(SpiceComponentKind.Resistor, Vector2.right * 100f);
            var ground = legacyModel.AddComponent(SpiceComponentKind.Ground, Vector2.down * 100f);
            CheckTrue(legacyModel.AddWire(source.InstanceId, "positive", resistor.InstanceId, "positive"), "旧式电路接线失败。");
            CheckTrue(legacyModel.AddWire(resistor.InstanceId, "negative", ground.InstanceId, "ground"), "旧式电路接地接线失败。");
            CheckTrue(SpiceDrawingSerializer.TryValidateSchemaV1SaveCompatibility(legacyModel, out var legacyError), "不含 BJT 的旧式电路 V1 保存必须仍成功：" + legacyError);
            Note("G9: V1 保存/导入均拒绝 BJT（文案含“三极管（NPN/PNP）”专属措辞）；旧式电路 V1 保存不受影响。");
        }

        // ---------- G10：旧文件兼容 ----------
        private static void G10_OldFiles_StillReadable()
        {
            // V1 旧文件（无 analysis，无同元件导线）。
            const string v1Legacy = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":1,\"components\":[" +
                "{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"-120\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"10\"}," +
                "{\"instanceId\":\"resistor-001\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":1,\"siValueText\":\"2200\"}," +
                "{\"instanceId\":\"capacitor-001\",\"componentType\":\"Capacitor\",\"position\":{\"x\":\"80\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1E-06\"}," +
                "{\"instanceId\":\"inductor-001\",\"componentType\":\"Inductor\",\"position\":{\"x\":\"160\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0.01\"}," +
                "{\"instanceId\":\"diode-001\",\"componentType\":\"SiliconDiode\",\"position\":{\"x\":\"0\",\"y\":\"120\"},\"rotationQuarterTurns\":2}," +
                "{\"instanceId\":\"switch-001\",\"componentType\":\"IdealSwitch\",\"position\":{\"x\":\"80\",\"y\":\"120\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1\"}," +
                "{\"instanceId\":\"current-source-001\",\"componentType\":\"DcCurrentSource\",\"position\":{\"x\":\"160\",\"y\":\"120\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0.001\"}," +
                "{\"instanceId\":\"voltage-probe-001\",\"componentType\":\"VoltageProbe\",\"position\":{\"x\":\"-120\",\"y\":\"120\"},\"rotationQuarterTurns\":0}," +
                "{\"instanceId\":\"current-probe-001\",\"componentType\":\"CurrentProbe\",\"position\":{\"x\":\"-120\",\"y\":\"-120\"},\"rotationQuarterTurns\":0}," +
                "{\"instanceId\":\"ground-001\",\"componentType\":\"Ground\",\"position\":{\"x\":\"0\",\"y\":\"-120\"},\"rotationQuarterTurns\":0}" +
                "],\"wires\":[" +
                "{\"startComponentId\":\"source-001\",\"startTerminalId\":\"positive\",\"endComponentId\":\"resistor-001\",\"endTerminalId\":\"positive\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}," +
                "{\"startComponentId\":\"resistor-001\",\"startTerminalId\":\"negative\",\"endComponentId\":\"ground-001\",\"endTerminalId\":\"ground\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}," +
                "{\"startComponentId\":\"source-001\",\"startTerminalId\":\"negative\",\"endComponentId\":\"ground-001\",\"endTerminalId\":\"ground\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}" +
                "]}";
            CheckTrue(SpiceDrawingSerializer.TryFromJson(v1Legacy, out var v1Model, out var v1Error), "V1 旧文件导入失败：" + v1Error);
            CheckEqual(10, v1Model.Components.Count, "V1 旧文件组件数量。");
            CheckEqual(3, v1Model.Wires.Count, "V1 旧文件导线数量。");
            CheckEqual(SpiceAnalysisMode.DcOperatingPoint, v1Model.AnalysisMode, "V1 导入必须回落 DC。");
            CheckRelClose(SpiceAnalysisLimits.DefaultFrequencyHz, v1Model.AcFrequencyHz, 1e-12d, "V1 导入必须回落默认频率。");
            CheckEqual(2200d, v1Model.FindComponent("resistor-001").SiValue, "V1 电阻参数。");
            CheckEqual(1, v1Model.FindComponent("resistor-001").RotationQuarterTurns, "V1 电阻旋转。");
            CheckEqual(2, v1Model.FindComponent("diode-001").RotationQuarterTurns, "V1 二极管旋转。");
            CheckVector(new Vector2(-120f, 0f), v1Model.FindComponent("source-001").Position, "V1 电压源位置。");

            // V2 旧文件（含全部 12 种 BJT 前 Kind；同元件导线仅 V2 —— 运放反馈）。
            const string v2Legacy = "{\"format\":\"ElectricalSimulation2D.SpiceDrawing\",\"schemaVersion\":2," +
                "\"analysis\":{\"mode\":\"DcOperatingPoint\",\"frequencyHz\":\"1000\"},\"components\":[" +
                "{\"instanceId\":\"source-001\",\"componentType\":\"DcVoltageSource\",\"position\":{\"x\":\"-200\",\"y\":\"0\"},\"rotationQuarterTurns\":0,\"siValueText\":\"5\"}," +
                "{\"instanceId\":\"ac-source-001\",\"componentType\":\"AcVoltageSource\",\"position\":{\"x\":\"-200\",\"y\":\"120\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1\",\"phaseDegrees\":\"30\"}," +
                "{\"instanceId\":\"opamp-001\",\"componentType\":\"IdealOperationalAmplifier\",\"position\":{\"x\":\"0\",\"y\":\"0\"},\"rotationQuarterTurns\":0}," +
                "{\"instanceId\":\"resistor-001\",\"componentType\":\"Resistor\",\"position\":{\"x\":\"160\",\"y\":\"0\"},\"rotationQuarterTurns\":3,\"siValueText\":\"1000\"}," +
                "{\"instanceId\":\"capacitor-001\",\"componentType\":\"Capacitor\",\"position\":{\"x\":\"160\",\"y\":\"120\"},\"rotationQuarterTurns\":0,\"siValueText\":\"1E-06\"}," +
                "{\"instanceId\":\"inductor-001\",\"componentType\":\"Inductor\",\"position\":{\"x\":\"160\",\"y\":\"-120\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0.01\"}," +
                "{\"instanceId\":\"diode-001\",\"componentType\":\"SiliconDiode\",\"position\":{\"x\":\"0\",\"y\":\"160\"},\"rotationQuarterTurns\":0}," +
                "{\"instanceId\":\"switch-001\",\"componentType\":\"IdealSwitch\",\"position\":{\"x\":\"80\",\"y\":\"160\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0\"}," +
                "{\"instanceId\":\"current-source-001\",\"componentType\":\"DcCurrentSource\",\"position\":{\"x\":\"160\",\"y\":\"160\"},\"rotationQuarterTurns\":0,\"siValueText\":\"0.002\"}," +
                "{\"instanceId\":\"voltage-probe-001\",\"componentType\":\"VoltageProbe\",\"position\":{\"x\":\"-80\",\"y\":\"160\"},\"rotationQuarterTurns\":0}," +
                "{\"instanceId\":\"current-probe-001\",\"componentType\":\"CurrentProbe\",\"position\":{\"x\":\"-160\",\"y\":\"160\"},\"rotationQuarterTurns\":0}," +
                "{\"instanceId\":\"ground-001\",\"componentType\":\"Ground\",\"position\":{\"x\":\"0\",\"y\":\"-160\"},\"rotationQuarterTurns\":0}" +
                "],\"wires\":[" +
                "{\"startComponentId\":\"opamp-001\",\"startTerminalId\":\"inverting\",\"endComponentId\":\"opamp-001\",\"endTerminalId\":\"output\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}," +
                "{\"startComponentId\":\"opamp-001\",\"startTerminalId\":\"output\",\"endComponentId\":\"resistor-001\",\"endTerminalId\":\"positive\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}," +
                "{\"startComponentId\":\"resistor-001\",\"startTerminalId\":\"negative\",\"endComponentId\":\"ground-001\",\"endTerminalId\":\"ground\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}," +
                "{\"startComponentId\":\"source-001\",\"startTerminalId\":\"negative\",\"endComponentId\":\"ground-001\",\"endTerminalId\":\"ground\",\"routeMode\":\"Auto\",\"manualRoutePoints\":[]}" +
                "]}";
            CheckTrue(SpiceDrawingSerializer.TryFromJson(v2Legacy, out var v2Model, out var v2Error), "V2 旧文件导入失败：" + v2Error);
            CheckEqual(12, v2Model.Components.Count, "V2 旧文件组件数量。");
            CheckEqual(4, v2Model.Wires.Count, "V2 旧文件导线数量。");
            CheckEqual(SpiceAnalysisMode.DcOperatingPoint, v2Model.AnalysisMode, "V2 分析模式保留。");
            CheckRelClose(1d, v2Model.FindComponent("ac-source-001").SiValue, 1e-12d, "V2 交流源幅值保留。");
            CheckRelClose(30d, v2Model.FindComponent("ac-source-001").AcPhaseDegrees, 1e-12d, "V2 交流源相位保留。");
            CheckEqual(3, v2Model.FindComponent("resistor-001").RotationQuarterTurns, "V2 电阻旋转保留。");
            var feedbackWire = v2Model.Wires.FirstOrDefault(wire => wire.StartComponentId == "opamp-001" && wire.EndComponentId == "opamp-001");
            CheckTrue(feedbackWire != null, "V2 同元件导线（运放反馈）必须保留。");
            CheckTrue(SpiceConnectionRules.IsConnectionAllowed(feedbackWire.StartComponentId == null ? SpiceComponentKind.Ground : v2Model.FindComponent(feedbackWire.StartComponentId).Kind,
                feedbackWire.StartComponentId, feedbackWire.StartTerminalId,
                v2Model.FindComponent(feedbackWire.EndComponentId).Kind, feedbackWire.EndComponentId, feedbackWire.EndTerminalId), "V2 同元件导线必须通过连接规则复验。");
            Note("G10: V1（10 种）与 V2（12 种含运放反馈同元件导线）旧文件均导入成功，字段完整。");
        }

        // ---------- G11：Stale 语义回归 ----------
        private static void G11_StaleRegression()
        {
            var canvasRoot = new GameObject("SpiceBjtG11Validation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, out _);
                var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, Vector2.left * 200f);
                var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.zero);
                var ground = workspace.CreateComponent(SpiceComponentKind.Ground, Vector2.down * 200f);
                var extraResistor = workspace.CreateComponent(SpiceComponentKind.Resistor, Vector2.up * 200f);
                CheckTrue(workspace.Connect(source.InstanceId, "positive", resistor.InstanceId, "positive"), "初始接线失败。");
                CheckTrue(workspace.Connect(resistor.InstanceId, "negative", ground.InstanceId, "ground"), "初始接地接线失败。");

                // add → Stale
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                workspace.CreateComponent(SpiceComponentKind.Capacitor, Vector2.right * 300f);
                CheckEqual(SpiceWorkspaceResultState.Stale, workspace.ResultState, "add 后必须 Stale。");

                // delete → Stale
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                workspace.SelectComponent(workspace.GetComponentViewForTesting(extraResistor.InstanceId));
                workspace.DeleteSelection();
                CheckEqual(SpiceWorkspaceResultState.Stale, workspace.ResultState, "delete 后必须 Stale。");

                // wire → Stale
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                CheckTrue(workspace.Connect(source.InstanceId, "negative", ground.InstanceId, "ground"), "wire 用例接线失败。");
                CheckEqual(SpiceWorkspaceResultState.Stale, workspace.ResultState, "wire 后必须 Stale。");

                // 参数改 → Stale
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                CheckTrue(workspace.TrySetParameter(resistor.InstanceId, 2d, "kOhm"), "参数写入失败。");
                CheckEqual(SpiceWorkspaceResultState.Stale, workspace.ResultState, "参数改后必须 Stale。");

                // 模式改 → Stale
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                CheckTrue(workspace.TrySetAnalysisMode(SpiceAnalysisMode.AcSingleFrequency), "模式切换失败。");
                CheckEqual(SpiceWorkspaceResultState.Stale, workspace.ResultState, "模式改后必须 Stale。");
                CheckTrue(workspace.TrySetAnalysisMode(SpiceAnalysisMode.DcOperatingPoint), "模式切回失败。");

                // move → 保持 Current（不劣化）
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                workspace.MoveComponent(resistor.InstanceId, new Vector2(40f, 24f));
                CheckEqual(SpiceWorkspaceResultState.Current, workspace.ResultState, "move 不得使结果过期。");

                // rotate → 不劣化
                workspace.SelectComponent(workspace.GetComponentViewForTesting(resistor.InstanceId));
                workspace.RotateSelection();
                CheckEqual(SpiceWorkspaceResultState.Current, workspace.ResultState, "rotate 不得使结果过期。");

                // save → 不变
                var directory = Path.Combine(LogsDirectory(), "BJT3_G11_Temp");
                Directory.CreateDirectory(directory);
                var filePath = Path.Combine(directory, "bjt3_g11_save.spicejson");
                CheckTrue(workspace.TrySaveWorkspaceToPath(filePath, out var saveError), "save 失败：" + saveError);
                CheckEqual(SpiceWorkspaceResultState.Current, workspace.ResultState, "save 不得改变结果状态。");

                // import → NeverRun
                workspace.SetResultStateForTesting(SpiceWorkspaceResultState.Current);
                var importModel = new SpiceWorkspaceModel();
                importModel.AddComponent(SpiceComponentKind.Resistor, Vector2.zero);
                CheckTrue(workspace.TryImportDrawingJson(SpiceDrawingSerializer.ToJson(importModel), out var importError), "import 失败：" + importError);
                CheckEqual(SpiceWorkspaceResultState.NeverRun, workspace.ResultState, "import 后必须 NeverRun。");
                Note("G11: add/delete/wire/参数/模式→Stale；move/rotate/save→保持 Current；import→NeverRun。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // ---------- G12：同元件接线序列化方向规范与导入一致性 ----------
        private static void G12_SameComponentWiring_ImportConsistency()
        {
            var model = new SpiceWorkspaceModel();
            model.AddComponent(SpiceComponentKind.GenericNpnBjt, Vector2.zero);
            // 故意以“大端子在前”的方向添加三条同元件导线（collector/base/emitter 两两组合）。
            CheckTrue(model.AddWire("npn-001", SpiceComponentModel.CollectorTerminalId, "npn-001", SpiceComponentModel.BaseTerminalId), "C↔B 同元件导线添加失败。");
            CheckTrue(model.AddWire("npn-001", SpiceComponentModel.EmitterTerminalId, "npn-001", SpiceComponentModel.BaseTerminalId), "E↔B 同元件导线添加失败。");
            CheckTrue(model.AddWire("npn-001", SpiceComponentModel.EmitterTerminalId, "npn-001", SpiceComponentModel.CollectorTerminalId), "E↔C 同元件导线添加失败。");

            var dto = SpiceDrawingSerializer.ToDto(model);
            CheckEqual(3, dto.wires.Count, "同元件导线 DTO 数量。");
            foreach (var wire in dto.wires)
            {
                CheckEqual("npn-001", wire.startComponentId, "同元件导线 start 组件。");
                CheckEqual("npn-001", wire.endComponentId, "同元件导线 end 组件。");
                CheckTrue(string.CompareOrdinal(wire.startTerminalId, wire.endTerminalId) < 0,
                    $"同元件导线方向必须规范化为端子字典序 start<end，实际 {wire.startTerminalId} -> {wire.endTerminalId}。");
            }

            var json = SpiceDrawingSerializer.ToJson(model);
            CheckTrue(SpiceDrawingSerializer.TryFromJson(json, out var reimported, out var error), "同元件导线图纸导入失败：" + error);
            CheckEqual(3, reimported.Wires.Count, "重导入导线数量。");
            var expectedPairs = new HashSet<string>(StringComparer.Ordinal)
            {
                PairKey(SpiceComponentModel.BaseTerminalId, SpiceComponentModel.CollectorTerminalId),
                PairKey(SpiceComponentModel.BaseTerminalId, SpiceComponentModel.EmitterTerminalId),
                PairKey(SpiceComponentModel.CollectorTerminalId, SpiceComponentModel.EmitterTerminalId)
            };
            foreach (var wire in reimported.Wires)
            {
                CheckTrue(SpiceConnectionRules.IsConnectionAllowed(
                    SpiceComponentKind.GenericNpnBjt, wire.StartComponentId, wire.StartTerminalId,
                    SpiceComponentKind.GenericNpnBjt, wire.EndComponentId, wire.EndTerminalId),
                    "重导入导线必须通过 SpiceConnectionRules 复验：" + wire.StartTerminalId + "->" + wire.EndTerminalId);
                CheckTrue(expectedPairs.Remove(PairKey(wire.StartTerminalId, wire.EndTerminalId)), "重导入导线端点语义与原始不一致：" + wire.StartTerminalId + "->" + wire.EndTerminalId);
            }
            CheckEqual(0, expectedPairs.Count, "重导入后缺少原始同元件导线语义。");
            CheckEqual(json, SpiceDrawingSerializer.ToJson(reimported), "同元件导线图纸必须满足确定性往返（方向规范化稳定）。");
            Note("G12: C/B/E 同元件导线序列化按端子字典序规范化（base<collector<emitter），重导入通过连接规则复验且 JSON 确定性稳定。");
        }

        // ---------- G13：固定模型说明不与通用参数提示或输入控件重叠 ----------
        private static void G13_FixedModelParameterPanelDoesNotOverlap()
        {
            var canvasRoot = new GameObject("SpiceBjtG13Validation", typeof(RectTransform), typeof(Canvas));
            try
            {
                var workspace = CreateInitializedWorkspace(canvasRoot.transform, out var bindings);
                foreach (var kind in new[]
                {
                    SpiceComponentKind.GenericNpnBjt,
                    SpiceComponentKind.GenericPnpBjt,
                    SpiceComponentKind.IdealOperationalAmplifier
                })
                {
                    var component = workspace.CreateComponent(kind, Vector2.zero);
                    workspace.SelectComponent(workspace.GetComponentViewForTesting(component.InstanceId));
                    Canvas.ForceUpdateCanvases();

                    var root = bindings.ParameterRoot;
                    var header = root.Find("ParameterHeader") as RectTransform;
                    var info = root.Find("OpAmpInfo") as RectTransform;
                    var subtitle = root.Find("ParameterSubtitle");
                    var input = root.Find("ParameterInput");
                    var unit = root.Find("Unit");
                    var apply = root.Find("Apply");

                    CheckTrue(header != null && info != null, kind + " 缺少固定模型参数区结构。");
                    CheckTrue(info.gameObject.activeSelf, kind + " 必须显示固定模型说明。");
                    CheckFalse(subtitle.gameObject.activeSelf, kind + " 不得同时显示通用编辑提示。");
                    CheckFalse(input.gameObject.activeSelf, kind + " 不得显示普通参数输入框。");
                    CheckFalse(unit.gameObject.activeSelf, kind + " 不得显示单位按钮。");
                    CheckFalse(apply.gameObject.activeSelf, kind + " 不得显示应用按钮。");
                    CheckTrue(info.rect.height >= 80f, kind + " 固定模型说明区域必须容纳完整多行文本。");
                    var headerCorners = new Vector3[4];
                    var infoCorners = new Vector3[4];
                    header.GetWorldCorners(headerCorners);
                    info.GetWorldCorners(infoCorners);
                    CheckTrue(infoCorners[1].y <= headerCorners[0].y - 8f,
                        kind + " 固定模型说明必须与参数 Header 保留清晰间隔。");
                }
                Note("G13: NPN/PNP/运放均只显示固定模型说明；通用提示和普通编辑控件已隐藏，说明区与 Header 保持独立。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasRoot);
            }
        }

        // ---------- 无头工作区构造（参照 SpiceT3WorkspaceValidation.Presentation.CreateInitializedWorkspaceForCopy） ----------
        private static SpiceWorkspaceController CreateInitializedWorkspace(Transform parent, out SpiceWorkspaceViewBindings bindings)
        {
            var spiceRoot = new GameObject("SpiceBjtWorkspace", typeof(RectTransform));
            spiceRoot.transform.SetParent(parent, false);
            var spiceRootRect = spiceRoot.GetComponent<RectTransform>();
            spiceRootRect.anchorMin = new Vector2(0.5f, 0.5f);
            spiceRootRect.anchorMax = new Vector2(0.5f, 0.5f);
            spiceRootRect.sizeDelta = new Vector2(1920f, 1080f);
            spiceRoot.SetActive(false);
            bindings = spiceRoot.AddComponent<SpiceWorkspaceViewBindings>();
            var workspace = spiceRoot.AddComponent<SpiceWorkspaceController>();
            var toolbar = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(toolbar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), Vector2.zero);
            var palette = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(palette, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(286f, -64f));
            var viewport = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(viewport, Vector2.zero, Vector2.one, new Vector2(302f, 16f), new Vector2(-384f, -80f));
            var grid = new GameObject("WorkspaceGrid", typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
            grid.SetParent(viewport, false);
            SpiceWorkspaceUi.Stretch(grid, Vector2.zero, Vector2.zero);
            grid.gameObject.AddComponent<WorkspaceGrid>().raycastTarget = false;
            var wires = CreateRect(viewport);
            var components = CreateRect(viewport);
            var overlay = CreateRect(viewport);
            var assistant = CreateRect(spiceRoot.transform);
            SpiceWorkspaceUi.Anchor(assistant, new Vector2(1f, 0f), Vector2.one, new Vector2(-368f, 0f), new Vector2(0f, -64f));
            var parameters = CreateRect(assistant);
            var results = CreateRect(assistant);
            var netlist = CreateRect(assistant);
            var diagnostics = CreateRect(assistant);
            bindings.Bind(palette, viewport, wires, components, overlay, assistant, parameters, results, netlist, diagnostics,
                CreateButton(toolbar), CreateButton(toolbar), CreateButton(toolbar), CreateButton(toolbar));

            var hostRoot = new GameObject("SpiceBjtHost", typeof(RectTransform));
            hostRoot.transform.SetParent(parent, false);
            hostRoot.SetActive(false);
            var host = hostRoot.AddComponent<SpiceWorkspaceDemoHost>();
            host.Configure(bindings, workspace);
            host.Initialize();
            spiceRoot.SetActive(true);
            hostRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            if (!workspace.SynchronizeWorkspaceGeometryForTesting())
                throw new InvalidOperationException("BJT3 测试工作区未能在激活后完成几何同步：" + workspace.GetWorkspaceGeometryDiagnosticsForTesting());
            return workspace;
        }

        private static RectTransform CreateRect(Transform parent)
        {
            var rect = new GameObject("BindingRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(900f, 600f);
            return rect;
        }

        private static Button CreateButton(Transform parent)
        {
            var button = new GameObject("BindingButton", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            button.transform.SetParent(parent, false);
            return button;
        }

        // ---------- G8 fixture：NPN+PNP+电阻+DC 源+GND，含 NPN 同元件 C↔B 导线 ----------
        private static void BuildMixedBjtWorkspace(SpiceWorkspaceController workspace, out string npnId, out string resistorId)
        {
            var source = workspace.CreateComponent(SpiceComponentKind.DcVoltageSource, new Vector2(-300f, 0f));
            var resistor = workspace.CreateComponent(SpiceComponentKind.Resistor, new Vector2(-100f, 0f));
            var npn = workspace.CreateComponent(SpiceComponentKind.GenericNpnBjt, new Vector2(120f, 0f));
            var pnp = workspace.CreateComponent(SpiceComponentKind.GenericPnpBjt, new Vector2(120f, 260f));
            var ground = workspace.CreateComponent(SpiceComponentKind.Ground, new Vector2(-100f, -260f));
            npnId = npn.InstanceId;
            resistorId = resistor.InstanceId;
            CheckTrue(workspace.Connect(source.InstanceId, "positive", resistor.InstanceId, "positive"), "G8 接线 source→resistor 失败。");
            CheckTrue(workspace.Connect(resistor.InstanceId, "negative", npn.InstanceId, SpiceComponentModel.CollectorTerminalId), "G8 接线 resistor→collector 失败。");
            CheckTrue(workspace.Connect(npn.InstanceId, SpiceComponentModel.CollectorTerminalId, npn.InstanceId, SpiceComponentModel.BaseTerminalId), "G8 同元件导线 C↔B 失败。");
            CheckTrue(workspace.Connect(npn.InstanceId, SpiceComponentModel.EmitterTerminalId, ground.InstanceId, "ground"), "G8 接线 emitter→ground 失败。");
            CheckTrue(workspace.Connect(source.InstanceId, "negative", ground.InstanceId, "ground"), "G8 接线 source→ground 失败。");
            CheckTrue(workspace.Connect(pnp.InstanceId, SpiceComponentModel.EmitterTerminalId, source.InstanceId, "positive"), "G8 接线 pnp emitter→source 失败。");
        }

        private static void AssertModelEquivalent(SpiceWorkspaceModel expected, SpiceWorkspaceModel actual, string label)
        {
            CheckEqual(expected.Components.Count, actual.Components.Count, label + "：组件数量。");
            CheckEqual(expected.Wires.Count, actual.Wires.Count, label + "：导线数量。");
            CheckEqual(expected.AnalysisMode, actual.AnalysisMode, label + "：分析模式。");
            CheckRelClose(expected.AcFrequencyHz, actual.AcFrequencyHz, 1e-12d, label + "：AC 频率。");
            foreach (var component in expected.Components)
            {
                var other = actual.FindComponent(component.InstanceId);
                CheckTrue(other != null, label + "：缺少组件 " + component.InstanceId + "。");
                CheckEqual(component.Kind, other.Kind, label + "：" + component.InstanceId + " Kind。");
                CheckVector(component.Position, other.Position, label + "：" + component.InstanceId + " 位置。");
                CheckEqual(component.RotationQuarterTurns, other.RotationQuarterTurns, label + "：" + component.InstanceId + " 旋转。");
                if (SpiceWorkspaceModel.HasUserParameter(component.Kind))
                    CheckRelClose(component.SiValue, other.SiValue, 1e-12d, label + "：" + component.InstanceId + " 参数。");
            }
            // 导线按“无向端点对”语义比较：Serializer 会对导线方向做字典序规范化（G12 单独验证），
            // 因此往返后 start/end 可能与原始方向相反，不能直接按方向比较。
            var expectedWires = expected.Wires.Select(wire => PairKey(wire.StartComponentId + ":" + wire.StartTerminalId, wire.EndComponentId + ":" + wire.EndTerminalId)).OrderBy(key => key, StringComparer.Ordinal).ToList();
            var actualWires = actual.Wires.Select(wire => PairKey(wire.StartComponentId + ":" + wire.StartTerminalId, wire.EndComponentId + ":" + wire.EndTerminalId)).OrderBy(key => key, StringComparer.Ordinal).ToList();
            for (var i = 0; i < expectedWires.Count; i++)
                CheckEqual(expectedWires[i], actualWires[i], label + "：导线端点集合。");
        }

        // ---------- 符号线段几何重建 ----------
        // CreateLine 以中点 anchoredPosition、长度 sizeDelta.x、Atan2 角度 localRotation 放置线段；
        // 据此反推端点，用于断言 BuildSchematicSymbol 的真实图元输出。
        private static List<(Vector2 From, Vector2 To)> GetSymbolSegments(SpiceWorkspaceComponentView view)
        {
            var symbol = view.transform.Find("SymbolRoot/Symbol");
            CheckTrue(symbol != null, "缺少 SymbolRoot/Symbol。");
            var segments = new List<(Vector2, Vector2)>();
            for (var i = 0; i < symbol.childCount; i++)
            {
                var rect = symbol.GetChild(i) as RectTransform;
                if (rect == null) continue;
                var length = rect.sizeDelta.x;
                var angle = rect.localEulerAngles.z * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var midpoint = rect.anchoredPosition;
                segments.Add((midpoint - direction * (length * 0.5f), midpoint + direction * (length * 0.5f)));
            }
            return segments;
        }

        private static bool ContainsSegment(List<(Vector2 From, Vector2 To)> segments, Vector2 from, Vector2 to)
        {
            const float tolerance = 0.01f;
            return segments.Any(segment =>
                (Vector2.Distance(segment.From, from) < tolerance && Vector2.Distance(segment.To, to) < tolerance) ||
                (Vector2.Distance(segment.From, to) < tolerance && Vector2.Distance(segment.To, from) < tolerance));
        }

        private static string PairKey(string first, string second)
        {
            return string.CompareOrdinal(first, second) <= 0 ? first + "|" + second : second + "|" + first;
        }

        private static string LogsDirectory()
        {
            var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        // ---------- 断言辅助（沿用 BJT2/BJT1C 模式，不引入 NUnit） ----------
        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Summary.AppendLine("[PASS] " + name);
                Debug.Log($"[BJT3] PASS: {name}");
            }
            catch (Exception ex)
            {
                _failed++;
                Summary.AppendLine("[FAIL] " + name + " — " + ex.Message);
                Debug.LogError($"[BJT3] FAIL: {name} — {ex.Message}");
            }
        }

        private static void Note(string text)
        {
            Summary.AppendLine("       " + text);
        }

        private static void CheckTrue(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckFalse(bool condition, string message)
        {
            if (condition) throw new InvalidOperationException(message);
        }

        private static void CheckEqual<T>(T expected, T actual, string message)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }

        private static void CheckRelClose(double expected, double actual, double relativeTolerance, string message)
        {
            var tolerance = relativeTolerance * Math.Max(1d, Math.Abs(expected));
            if (Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException($"{message} Expected {expected:0.######e+0} ±{relativeTolerance:0.#e+0} (rel), got {actual:0.######e+0}.");
        }

        private static void CheckVector(Vector2 expected, Vector2 actual, string message)
        {
            if (Vector2.Distance(expected, actual) > 0.01f)
                throw new InvalidOperationException($"{message} Expected {expected}, got {actual}.");
        }
    }
}
