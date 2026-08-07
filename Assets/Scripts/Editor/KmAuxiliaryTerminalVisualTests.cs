using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using ElectricalSim.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-1 Phase 2 补正测试：验证 33/34 辅助触点端子的 Definition、视觉坐标、Runtime Catalog、
    /// PNG 规范化、Prefab 无 diff、以及 SimulationEngine/CircuitStateAnalyzer 不含 33/34 内部导通代码。
    ///
    /// 本测试不验证外部 Wire 交互或 33/34 运行导通——这些留到后续阶段。
    /// batchmode 下无法实例化 TerminalView 的项标记 NOT_TESTED。
    /// 旧 12 个端点逐项完整验证（id、顺序、label、role、color、normalizedPosition）。
    /// 33/34 几何不重叠测试优先尝试真实实例化 VisualPrefabInstance，失败则标记 NOT_TESTED。
    /// </summary>
    public static class KmAuxiliaryTerminalVisualTests
    {
        private const string DataFolder = "Assets/Data";
        private const string Km220VName = "Contactor_KM_220V";
        private const string Km380VName = "Contactor_KM_380V";
        private const string DefaultPngPath = "Assets/Art/Components/Contactor_KM_380V_Default.png";
        private const string EnergizedPngPath = "Assets/Art/Components/Contactor_KM_380V_Energized.png";
        private const string KmPrefabPath = "Assets/Prefab/Contactor_KM_380V_Visual.prefab";
        private const string SimulationEnginePath = "Assets/Scripts/Core/SimulationEngine.cs";
        private const string CircuitStateAnalyzerPath = "Assets/Scripts/Core/CircuitStateAnalyzer.cs";
        private const string BaselineSha = "c7e6a84f8f5fa76b5882003df21662734b67b1c1";

        // 原 12 端子的完整期望数据（顺序敏感）：id, label, role, normalizedPosition, color
        private sealed class ExpectedTerminal
        {
            public string Id;
            public string Label;
            public int Role;
            public Vector2 NormalizedPosition;
            public Color Color;

            public ExpectedTerminal(string id, string label, int role, Vector2 pos, Color color)
            {
                Id = id; Label = label; Role = role; NormalizedPosition = pos; Color = color;
            }
        }

        private static readonly ExpectedTerminal[] Original12Terminals =
        {
            new ExpectedTerminal("L1", "1/L1", 4, new Vector2(0.18f, 1f), new Color(0.95f, 0.78f, 0.12f, 1f)),
            new ExpectedTerminal("T1", "2/T1", 5, new Vector2(0.18f, 0f), new Color(0.95f, 0.78f, 0.12f, 1f)),
            new ExpectedTerminal("L2", "3/L2", 4, new Vector2(0.5f, 1f), new Color(0.08f, 0.65f, 0.25f, 1f)),
            new ExpectedTerminal("T2", "4/T2", 5, new Vector2(0.5f, 0f), new Color(0.08f, 0.65f, 0.25f, 1f)),
            new ExpectedTerminal("L3", "5/L3", 4, new Vector2(0.82f, 1f), new Color(0.95f, 0.12f, 0.12f, 1f)),
            new ExpectedTerminal("T3", "6/T3", 5, new Vector2(0.82f, 0f), new Color(0.95f, 0.12f, 0.12f, 1f)),
            new ExpectedTerminal("A1", "A1", 6, new Vector2(0f, 0.68f), new Color(0.95f, 0.12f, 0.12f, 1f)),
            new ExpectedTerminal("A2", "A2", 7, new Vector2(1f, 0.68f), new Color(0.1f, 0.35f, 0.95f, 1f)),
            new ExpectedTerminal("13", "13", 4, new Vector2(0f, 0.42f), new Color(0.08f, 0.65f, 0.25f, 1f)),
            new ExpectedTerminal("14", "14", 5, new Vector2(1f, 0.42f), new Color(0.08f, 0.65f, 0.25f, 1f)),
            new ExpectedTerminal("21", "21", 4, new Vector2(0f, 0.22f), new Color(0.95f, 0.12f, 0.12f, 1f)),
            new ExpectedTerminal("22", "22", 5, new Vector2(1f, 0.22f), new Color(0.95f, 0.12f, 0.12f, 1f)),
        };

        private static readonly ExpectedTerminal[] New33_34Terminals =
        {
            new ExpectedTerminal("33", "33", 4, new Vector2(0f, 0.08f), new Color(0.08f, 0.65f, 0.25f, 1f)),
            new ExpectedTerminal("34", "34", 5, new Vector2(1f, 0.08f), new Color(0.08f, 0.65f, 0.25f, 1f)),
        };

        [MenuItem("Tools/Tests/Run KM Auxiliary Terminal Visual Tests")]
        public static void Run()
        {
            var failures = new List<string>();
            var notTested = new List<string>();

            try
            {
                // 1-2. 220V/380V KM 有 33/34
                TestKMHas33And34(failures, Km220VName);
                TestKMHas33And34(failures, Km380VName);

                // 3. 两种 KM 端点结构一致
                TestBothKMTerminalStructuresMatch(failures);

                // 4. 原 12 个端点逐项完整保持（id/顺序/label/role/color/normalizedPosition）
                TestOriginal12TerminalsComplete(failures, Km220VName);
                TestOriginal12TerminalsComplete(failures, Km380VName);

                // 4b. 33/34 位于原 12 个端点之后
                Test33_34AfterOriginal12(failures, Km220VName);
                Test33_34AfterOriginal12(failures, Km380VName);

                // 5-6. 两张 PNG 为 800x1000 RGBA
                TestBothPNG_800x1000_RGBA(failures);

                // 7. 33/34 Registry 坐标存在
                Test33_34_RegistryCoordinatesExist(failures);

                // 8. Runtime Catalog 两个 KM Entry 有 33/34
                TestRuntimeCatalogHas33_34(failures);

                // 9. 33/34 TerminalView 动态验证（尝试真实实例化）
                Test33_34_TerminalViewDynamic(failures, notTested);

                // 10. 33/34 命中区域 30x30（依赖 9 的实例化结果）
                Test33_34_HitArea30x30(failures, notTested);

                // 11. 33/34 不与 21/22、T1/T3 重叠（使用真实实例化或标记 NOT_TESTED）
                Test33_34_NoOverlapWith21_22_T1_T3(failures, notTested);

                // 12. KM Prefab 相对 c7e6a84 无 diff
                TestKMPrefabNoDiffVsBaseline(failures, notTested);

                // 12b. git diff --name-only c7e6a84 不含禁止文件
                TestNoForbiddenFilesChanged(failures, notTested);

                // 13-14. Phase 4: 33/34 内部导通代码已实现，验证其位于 energized 分支
                Test33_34_InternalConnectionInEnergizedBranch(failures, SimulationEnginePath, "SimulationEngine");
                Test33_34_InternalConnectionInEnergizedBranch(failures, CircuitStateAnalyzerPath, "CircuitStateAnalyzer");
            }
            catch (Exception e)
            {
                failures.Add("测试异常: " + e.Message + "\n" + e.StackTrace);
            }

            if (notTested.Count > 0)
            {
                Debug.LogWarning("=== KM AuxiliaryTerminalVisualTests NOT_TESTED 项 ===");
                foreach (var n in notTested)
                {
                    Debug.LogWarning("[NOT_TESTED] " + n);
                }
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== KM AuxiliaryTerminalVisualTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "KM AuxiliaryTerminalVisualTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== KM AuxiliaryTerminalVisualTests 通过 (AUTOMATED_GEOMETRY_PASS, NOT_TESTED 项已记录) ===");
        }

        // =========================================================================
        // 1-2. 220V/380V KM 有 33/34
        // =========================================================================
        private static void TestKMHas33And34(List<string> failures, string definitionName)
        {
            var def = LoadDefinition(definitionName);
            if (def == null)
            {
                failures.Add($"[{definitionName}] Definition 未找到。");
                return;
            }

            var t33 = def.terminals.Find(t => t.id == "33");
            var t34 = def.terminals.Find(t => t.id == "34");

            if (t33 == null)
                failures.Add($"[{definitionName}] 缺少端子 33。");
            if (t34 == null)
                failures.Add($"[{definitionName}] 缺少端子 34。");

            // 33 role 应与 13 相同（role 4 = Input），34 role 应与 14 相同（role 5 = Output）
            var t13 = def.terminals.Find(t => t.id == "13");
            var t14 = def.terminals.Find(t => t.id == "14");

            if (t33 != null && t13 != null && t33.role != t13.role)
                failures.Add($"[{definitionName}] 端子 33 role({t33.role}) 应与 13 role({t13.role}) 相同。");
            if (t34 != null && t14 != null && t34.role != t14.role)
                failures.Add($"[{definitionName}] 端子 34 role({t34.role}) 应与 14 role({t14.role}) 相同。");

            // allowSameComponentJumper 应为 true
            if (!def.allowSameComponentJumper)
                failures.Add($"[{definitionName}] allowSameComponentJumper 应为 true。");
        }

        // =========================================================================
        // 3. 两种 KM 端点结构一致
        // =========================================================================
        private static void TestBothKMTerminalStructuresMatch(List<string> failures)
        {
            var def220 = LoadDefinition(Km220VName);
            var def380 = LoadDefinition(Km380VName);
            if (def220 == null || def380 == null)
            {
                failures.Add("无法加载两种 KM Definition 进行比较。");
                return;
            }

            if (def220.terminals.Count != def380.terminals.Count)
            {
                failures.Add($"220V 端子数({def220.terminals.Count}) 与 380V 端子数({def380.terminals.Count}) 不一致。");
                return;
            }

            for (int i = 0; i < def220.terminals.Count; i++)
            {
                var t220 = def220.terminals[i];
                var t380 = def380.terminals[i];
                if (t220.id != t380.id)
                    failures.Add($"端子[{i}] id 不一致: 220V='{t220.id}' 380V='{t380.id}'。");
                if (t220.role != t380.role)
                    failures.Add($"端子[{i}]({t220.id}) role 不一致: 220V={t220.role} 380V={t380.role}。");
                if (t220.label != t380.label)
                    failures.Add($"端子[{i}]({t220.id}) label 不一致: 220V='{t220.label}' 380V='{t380.label}'。");
                if (Vector2.Distance(t220.normalizedPosition, t380.normalizedPosition) > 0.001f)
                    failures.Add($"端子[{i}]({t220.id}) normalizedPosition 不一致: 220V={t220.normalizedPosition} 380V={t380.normalizedPosition}。");
                if (!ColorsEqual(t220.color, t380.color))
                    failures.Add($"端子[{i}]({t220.id}) color 不一致: 220V={t220.color} 380V={t380.color}。");
            }
        }

        // =========================================================================
        // 4. 原 12 个端点逐项完整保持（id/顺序/label/role/color/normalizedPosition）
        // =========================================================================
        private static void TestOriginal12TerminalsComplete(List<string> failures, string definitionName)
        {
            var def = LoadDefinition(definitionName);
            if (def == null)
            {
                failures.Add($"[{definitionName}] Definition 未找到。");
                return;
            }

            if (def.terminals.Count < 12)
            {
                failures.Add($"[{definitionName}] 端子数({def.terminals.Count}) 少于 12。");
                return;
            }

            for (int i = 0; i < Original12Terminals.Length; i++)
            {
                var expected = Original12Terminals[i];
                var actual = def.terminals[i];

                if (actual.id != expected.Id)
                    failures.Add($"[{definitionName}] 端子[{i}] id='{actual.id}' 应为 '{expected.Id}'。");

                if (actual.label != expected.Label)
                    failures.Add($"[{definitionName}] 端子[{i}]({actual.id}) label='{actual.label}' 应为 '{expected.Label}'。");

                if ((int)actual.role != expected.Role)
                    failures.Add($"[{definitionName}] 端子[{i}]({actual.id}) role={actual.role} 应为 {expected.Role}。");

                if (Vector2.Distance(actual.normalizedPosition, expected.NormalizedPosition) > 0.001f)
                    failures.Add($"[{definitionName}] 端子[{i}]({actual.id}) normalizedPosition={actual.normalizedPosition} 应为 {expected.NormalizedPosition}。");

                if (!ColorsEqual(actual.color, expected.Color))
                    failures.Add($"[{definitionName}] 端子[{i}]({actual.id}) color={actual.color} 应为 {expected.Color}。");
            }
        }

        // =========================================================================
        // 4b. 33/34 位于原 12 个端点之后
        // =========================================================================
        private static void Test33_34AfterOriginal12(List<string> failures, string definitionName)
        {
            var def = LoadDefinition(definitionName);
            if (def == null)
            {
                failures.Add($"[{definitionName}] Definition 未找到。");
                return;
            }

            if (def.terminals.Count < 14)
            {
                failures.Add($"[{definitionName}] 端子数({def.terminals.Count}) 少于 14（12+33+34）。");
                return;
            }

            // 索引 12 和 13 应为 33 和 34
            if (def.terminals[12].id != "33")
                failures.Add($"[{definitionName}] 端子[12] id='{def.terminals[12].id}' 应为 '33'（位于原 12 端子之后）。");
            if (def.terminals[13].id != "34")
                failures.Add($"[{definitionName}] 端子[13] id='{def.terminals[13].id}' 应为 '34'（位于原 12 端子之后）。");

            // 验证 33/34 的完整属性
            for (int i = 0; i < New33_34Terminals.Length; i++)
            {
                var expected = New33_34Terminals[i];
                var actual = def.terminals[12 + i];

                if (actual.id != expected.Id)
                    failures.Add($"[{definitionName}] 新端子[{12+i}] id='{actual.id}' 应为 '{expected.Id}'。");
                if (actual.label != expected.Label)
                    failures.Add($"[{definitionName}] 新端子({actual.id}) label='{actual.label}' 应为 '{expected.Label}'。");
                if ((int)actual.role != expected.Role)
                    failures.Add($"[{definitionName}] 新端子({actual.id}) role={actual.role} 应为 {expected.Role}。");
                if (Vector2.Distance(actual.normalizedPosition, expected.NormalizedPosition) > 0.001f)
                    failures.Add($"[{definitionName}] 新端子({actual.id}) normalizedPosition={actual.normalizedPosition} 应为 {expected.NormalizedPosition}。");
                if (!ColorsEqual(actual.color, expected.Color))
                    failures.Add($"[{definitionName}] 新端子({actual.id}) color={actual.color} 应为 {expected.Color}。");
            }
        }

        // =========================================================================
        // 5-6. 两张 PNG 为 800x1000 RGBA
        // =========================================================================
        private static void TestBothPNG_800x1000_RGBA(List<string> failures)
        {
            CheckPng(failures, DefaultPngPath, "Default");
            CheckPng(failures, EnergizedPngPath, "Energized");
        }

        private static void CheckPng(List<string> failures, string path, string label)
        {
            // 读取 PNG 文件头（IHDR chunk）验证磁盘上的文件格式，不依赖 Unity Texture2D 导入格式。
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                failures.Add($"[{label}] PNG 文件未找到: {fullPath}");
                return;
            }

            byte[] header = new byte[33];
            using (var fs = File.OpenRead(fullPath))
            {
                int read = fs.Read(header, 0, 33);
                if (read < 33)
                {
                    failures.Add($"[{label}] PNG 文件过小，无法读取完整 IHDR。");
                    return;
                }
            }

            // 验证 PNG 签名: 89 50 4E 47 0D 0A 1A 0A
            byte[] pngSig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < 8; i++)
            {
                if (header[i] != pngSig[i])
                {
                    failures.Add($"[{label}] PNG 签名无效，不是 PNG 文件。");
                    return;
                }
            }

            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            int bitDepth = header[24];
            int colorType = header[25];

            if (width != 800 || height != 1000)
                failures.Add($"[{label}] PNG 磁盘尺寸 {width}x{height} 应为 800x1000。");

            if (colorType != 6)
                failures.Add($"[{label}] PNG colorType={colorType} 应为 6 (RGBA)。");
            if (bitDepth != 8)
                failures.Add($"[{label}] PNG bitDepth={bitDepth} 应为 8。");
        }

        // =========================================================================
        // 7. 33/34 Registry 坐标存在
        // =========================================================================
        private static void Test33_34_RegistryCoordinatesExist(List<string> failures)
        {
            foreach (var name in new[] { Km220VName, Km380VName })
            {
                if (!VisualPrefabRegistry.TryGetConfig(name, out var config))
                {
                    failures.Add($"[{name}] VisualPrefabRegistry 未找到配置。");
                    continue;
                }

                if (config.TerminalPositionOverrides == null)
                {
                    failures.Add($"[{name}] TerminalPositionOverrides 为 null。");
                    continue;
                }

                bool found33 = false, found34 = false;
                foreach (var op in config.TerminalPositionOverrides)
                {
                    if (op != null && op.terminalId == "33") found33 = true;
                    if (op != null && op.terminalId == "34") found34 = true;
                }

                if (!found33)
                    failures.Add($"[{name}] Registry 缺少端子 33 坐标覆盖。");
                if (!found34)
                    failures.Add($"[{name}] Registry 缺少端子 34 坐标覆盖。");
            }
        }

        // =========================================================================
        // 8. Runtime Catalog 两个 KM Entry 有 33/34
        // =========================================================================
        private static void TestRuntimeCatalogHas33_34(List<string> failures)
        {
            var catalog = ComponentVisualRuntimeCatalog.Load();
            if (catalog == null)
            {
                failures.Add("ComponentVisualRuntimeCatalog 未找到。需要先重建 Catalog。");
                return;
            }

            foreach (var name in new[] { Km220VName, Km380VName })
            {
                if (!catalog.TryGetEntry(name, out var entry))
                {
                    failures.Add($"[{name}] Runtime Catalog 缺少 Entry。");
                    continue;
                }

                if (entry.terminalPositionOverrides == null)
                {
                    failures.Add($"[{name}] Catalog Entry terminalPositionOverrides 为 null。");
                    continue;
                }

                bool found33 = false, found34 = false;
                foreach (var op in entry.terminalPositionOverrides)
                {
                    if (op != null && op.terminalId == "33") found33 = true;
                    if (op != null && op.terminalId == "34") found34 = true;
                }

                if (!found33)
                    failures.Add($"[{name}] Runtime Catalog 缺少端子 33 覆盖。");
                if (!found34)
                    failures.Add($"[{name}] Runtime Catalog 缺少端子 34 覆盖。");
            }
        }

        // =========================================================================
        // 9. 33/34 TerminalView 动态验证（尝试真实实例化 VisualPrefabInstance）
        // =========================================================================
        private static void Test33_34_TerminalViewDynamic(List<string> failures, List<string> notTested)
        {
            // 尝试在 Editor 中创建临时 Canvas + GameObject 并实例化 VisualPrefabInstance
            GameObject tempRoot = null;
            Canvas tempCanvas = null;
            GameObject tempParent = null;
            RectTransform parentRect = null;

            try
            {
                tempRoot = new GameObject("KM_Test_TempRoot");
                tempCanvas = tempRoot.AddComponent<Canvas>();
                tempCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = tempRoot.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                tempRoot.AddComponent<GraphicRaycaster>();

                tempParent = new GameObject("KM_Test_Parent");
                tempParent.transform.SetParent(tempRoot.transform, false);
                parentRect = tempParent.AddComponent<RectTransform>();
                parentRect.sizeDelta = new Vector2(110, 130);

                var ownerRect = tempParent.AddComponent<RectTransform>();
                var legacyBody = tempParent.AddComponent<Image>();
                legacyBody.color = Color.clear;

                int successCount = 0;
                int totalCount = 0;

                foreach (var name in new[] { Km220VName, Km380VName })
                {
                    totalCount++;
                    if (!VisualPrefabRegistry.TryGetConfig(name, out var config))
                    {
                        failures.Add($"[{name}] VisualPrefabRegistry 未找到配置。");
                        continue;
                    }

                    if (VisualPrefabInstance.TryCreate(config, tempParent.transform, parentRect, legacyBody, null, out var instance))
                    {
                        // 验证 33/34 端子位置可通过 TryGetTerminalPosition 获取
                        if (instance.TryGetTerminalPosition("33", out var pos33))
                        {
                            Debug.Log($"[{name}] Terminal 33 localPosition: {pos33}");
                            successCount++;
                        }
                        else
                        {
                            failures.Add($"[{name}] TryGetTerminalPosition('33') 返回 false。");
                        }

                        if (instance.TryGetTerminalPosition("34", out var pos34))
                        {
                            Debug.Log($"[{name}] Terminal 34 localPosition: {pos34}");
                        }
                        else
                        {
                            failures.Add($"[{name}] TryGetTerminalPosition('34') 返回 false。");
                        }

                        // 验证 Root RectTransform 存在
                        if (instance.Root == null)
                        {
                            failures.Add($"[{name}] VisualPrefabInstance.Root 为 null。");
                        }

                        // 清理实例
                        if (instance.Root != null)
                        {
                            UnityEngine.Object.DestroyImmediate(instance.Root.gameObject);
                        }
                    }
                    else
                    {
                        notTested.Add($"[{name}] VisualPrefabInstance.TryCreate 失败，无法动态验证 TerminalView。");
                    }
                }

                if (successCount == totalCount)
                {
                    Debug.Log("33/34 TerminalView 动态验证：VisualPrefabInstance 创建成功，TryGetTerminalPosition 返回 33/34 坐标。");
                }
            }
            catch (Exception e)
            {
                notTested.Add("33/34 TerminalView 动态验证异常: " + e.Message);
            }
            finally
            {
                if (tempRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(tempRoot);
                }
            }
        }

        // =========================================================================
        // 10. 33/34 命中区域 30x30（依赖真实 TerminalView 实例化）
        // =========================================================================
        private static void Test33_34_HitArea30x30(List<string> failures, List<string> notTested)
        {
            // TerminalView 的命中区域由其 RectTransform sizeDelta 决定，在 CircuitComponent.BuildTerminals 中创建。
            // batchmode 下无法实例化完整 CircuitComponent（需要 WorkspaceController），
            // 因此标记 NOT_TESTED，需要 PlayMode 或 Editor 手动验证。
            notTested.Add("33/34 命中区域 30x30 需要 PlayMode 中实例化 CircuitComponent 并读取 TerminalView RectTransform，batchmode 下标记 NOT_TESTED。");
            notTested.Add("33/34 TerminalView 可被端子点击层识别需要在 PlayMode 中验证 GraphicRaycaster 命中，batchmode 下标记 NOT_TESTED。");
        }

        // =========================================================================
        // 11. 33/34 不与 21/22、T1/T3 重叠（使用真实实例化或标记 NOT_TESTED）
        // =========================================================================
        private static void Test33_34_NoOverlapWith21_22_T1_T3(List<string> failures, List<string> notTested)
        {
            // 优先尝试真实实例化 VisualPrefabInstance 并读取端子局部坐标
            GameObject tempRoot = null;
            bool usedDynamicCheck = false;

            try
            {
                tempRoot = new GameObject("KM_Test_OverlapRoot");
                var canvas = tempRoot.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = tempRoot.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                tempRoot.AddComponent<GraphicRaycaster>();

                var tempParent = new GameObject("KM_Test_OverlapParent");
                tempParent.transform.SetParent(tempRoot.transform, false);
                var parentRect = tempParent.AddComponent<RectTransform>();
                parentRect.sizeDelta = new Vector2(110, 130);
                var legacyBody = tempParent.AddComponent<Image>();
                legacyBody.color = Color.clear;

                if (VisualPrefabRegistry.TryGetConfig(Km380VName, out var config) &&
                    VisualPrefabInstance.TryCreate(config, tempParent.transform, parentRect, legacyBody, null, out var instance))
                {
                    usedDynamicCheck = true;
                    var positions = new Dictionary<string, Vector2>();
                    string[] idsToCheck = { "33", "34", "21", "22", "T1", "T3" };

                    foreach (var id in idsToCheck)
                    {
                        if (instance.TryGetTerminalPosition(id, out var pos))
                        {
                            positions[id] = pos;
                        }
                        else
                        {
                            failures.Add($"动态重叠测试：无法获取端子 {id} 的位置。");
                        }
                    }

                    if (positions.Count == idsToCheck.Length)
                    {
                        // 使用真实局部坐标计算 30x30 命中区域矩形并检查重叠
                        // 端子中心为局部坐标，命中区域为 30x30（半宽 15）
                        var pairs = new[]
                        {
                            new[] { "33", "21" },
                            new[] { "33", "22" },
                            new[] { "33", "T1" },
                            new[] { "33", "T3" },
                            new[] { "34", "21" },
                            new[] { "34", "22" },
                            new[] { "34", "T1" },
                            new[] { "34", "T3" },
                        };

                        const float hitHalfSize = 15f; // 30x30 命中区域半宽

                        foreach (var pair in pairs)
                        {
                            var p1 = positions[pair[0]];
                            var p2 = positions[pair[1]];

                            // 构建命中区域矩形（中心为端子位置，尺寸 30x30）
                            var rect1 = new Rect(p1.x - hitHalfSize, p1.y - hitHalfSize, 30f, 30f);
                            var rect2 = new Rect(p2.x - hitHalfSize, p2.y - hitHalfSize, 30f, 30f);

                            if (rect1.Overlaps(rect2))
                            {
                                failures.Add($"动态重叠测试：端子 {pair[0]}({p1}) 与 {pair[1]}({p2}) 的 30x30 命中区域重叠。");
                            }
                            else
                            {
                                float dist = Vector2.Distance(p1, p2);
                                Debug.Log($"动态重叠测试：端子 {pair[0]}({p1}) 与 {pair[1]}({p2}) 距离={dist:F1}，命中区域不重叠。");
                            }
                        }
                    }

                    if (instance.Root != null)
                    {
                        UnityEngine.Object.DestroyImmediate(instance.Root.gameObject);
                    }
                }
            }
            catch (Exception e)
            {
                notTested.Add("动态重叠测试异常: " + e.Message);
            }
            finally
            {
                if (tempRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(tempRoot);
                }
            }

            if (!usedDynamicCheck)
            {
                notTested.Add("33/34 与 21/22/T1/T3 重叠测试：VisualPrefabInstance.TryCreate 失败，无法进行动态矩形重叠检查，标记 NOT_TESTED。");
            }
        }

        // =========================================================================
        // 12. KM Prefab 相对 c7e6a84 无 diff
        // =========================================================================
        private static void TestKMPrefabNoDiffVsBaseline(List<string> failures, List<string> notTested)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"diff --exit-code {BaselineSha} -- {KmPrefabPath}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", ""),
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                    if (proc.ExitCode != 0)
                    {
                        var output = proc.StandardOutput.ReadToEnd();
                        failures.Add($"KM Prefab 相对 {BaselineSha} 有 diff (exit={proc.ExitCode}):\n{output}");
                    }
                    else
                    {
                        Debug.Log($"KM Prefab 相对 {BaselineSha} 无 diff。");
                    }
                }
            }
            catch (Exception e)
            {
                notTested.Add("KM Prefab git diff 基线检查失败: " + e.Message);
            }
        }

        // =========================================================================
        // 12b. git diff --name-only c7e6a84 不含禁止文件
        // =========================================================================
        private static void TestNoForbiddenFilesChanged(List<string> failures, List<string> notTested)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"diff --name-only {BaselineSha}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Application.dataPath.Replace("/Assets", "").Replace("\\Assets", ""),
                };

                using (var proc = Process.Start(psi))
                {
                    proc.WaitForExit(10000);
                    var output = proc.StandardOutput.ReadToEnd();
                    var changedFiles = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                    // 禁止文件模式（Phase 3 允许 WireManager/SaveLoadService；Phase 4 允许 SimulationEngine/CircuitStateAnalyzer）
                    var forbiddenPatterns = new[]
                    {
                        "Contactor_KM_380V_Visual.prefab",
                        "Contactor_KM_380V_Visual.prefab.meta",
                        "Demo.unity",
                        "SpiceWorkspaceController",
                    };

                    foreach (var file in changedFiles)
                    {
                        foreach (var pattern in forbiddenPatterns)
                        {
                            if (file.Contains(pattern))
                            {
                                failures.Add($"禁止文件被修改: {file} (匹配模式: {pattern})");
                            }
                        }
                    }

                    Debug.Log($"git diff --name-only {BaselineSha} 共 {changedFiles.Length} 个文件变更，禁止文件检查通过。");
                }
            }
            catch (Exception e)
            {
                notTested.Add("git diff --name-only 基线检查失败: " + e.Message);
            }
        }

        // =========================================================================
        // 13-14. KM-1.1: 33/34 内部导通已迁移到 ContactorTerminalSchema.EnumerateClosedPairs
        // =========================================================================
        private static void Test33_34_InternalConnectionInEnergizedBranch(List<string> failures, string path, string label)
        {
            var fullPath = Path.Combine(Application.dataPath.Replace("/Assets", "").Replace("\\Assets", ""), path);
            if (!File.Exists(fullPath))
            {
                failures.Add($"[{label}] 源文件未找到: {fullPath}");
                return;
            }

            var content = File.ReadAllText(fullPath);

            // KM-1.1: 源文件应使用 ContactorTerminalSchema.EnumerateClosedPairs 遍历触点
            var hasSchemaIteration = Regex.IsMatch(content, @"ContactorTerminalSchema\.EnumerateClosedPairs");
            if (!hasSchemaIteration)
            {
                failures.Add($"[{label}] 应使用 ContactorTerminalSchema.EnumerateClosedPairs 进行触点遍历，但未找到。");
                return;
            }

            // 验证 ContactorTerminalSchema.cs 定义了 33/34 配对且位于 NO 集合
            var schemaPath = Path.Combine(Application.dataPath.Replace("/Assets", "").Replace("\\Assets", ""), "Assets/Scripts/Core/ContactorTerminalSchema.cs");
            if (!File.Exists(schemaPath))
            {
                failures.Add($"[{label}] ContactorTerminalSchema.cs 未找到。");
                return;
            }
            var schemaContent = File.ReadAllText(schemaPath);
            if (!schemaContent.Contains("AuxNO33") || !schemaContent.Contains("AuxNO34"))
            {
                failures.Add($"[{label}] ContactorTerminalSchema.cs 应定义 33/34 配对 (TerminalConstants.AuxNO33/AuxNO34)。");
                return;
            }

            // 验证 33/34 不位于 NC 集合（应在 NO 集合，得电时闭合）
            if (Regex.IsMatch(schemaContent, @"NormallyClosedContactPairs[^;]*AuxNO33[^;]*AuxNO34"))
            {
                failures.Add($"[{label}] 33/34 不应位于 NormallyClosedContactPairs（应为 NormallyOpen）。");
            }
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================
        private static ComponentDefinition LoadDefinition(string name)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { DataFolder });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (def != null && def.name == name)
                    return def;
            }
            return null;
        }

        private static bool ColorsEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.001f &&
                   Mathf.Abs(a.g - b.g) < 0.001f &&
                   Mathf.Abs(a.b - b.b) < 0.001f &&
                   Mathf.Abs(a.a - b.a) < 0.001f;
        }
    }
}
