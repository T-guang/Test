using System;
using System.Collections.Generic;
using ElectricalSim.AI;
using ElectricalSim.Core;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// KM-2B4 专项测试：验证同一 KM 的 NO1/NO2 pair-level permutation 等价识别。
    /// 测试直接构造 CircuitTopologyGraph，覆盖 T01-T10 场景：
    /// ExactMatch、EquivalentMatch（NO pair swap）、NoMatch（半 pair / NO→NC / 跨 KM 等）。
    /// </summary>
    public static class KM2B4_ContactorNoPairEquivalenceTests
    {
        private const string KmDef = "Contactor_KM_220V";
        private const string StartDef = "Button_Start_NO";
        private const string LoadDef = "Lamp";
        private const string Load2Def = "Indicator";

        [MenuItem("Tools/Tests/Run KM2B4 Contactor NO Pair Equivalence Tests")]
        public static void Run()
        {
            var failures = new List<string>();

            T01_OriginalExactMatch(failures);
            T02_SingleKmFullSwap(failures);
            T03_SingleNoUsedSwap(failures);
            T04_HalfPairReject(failures);
            T05_BothBranchesSamePair(failures);
            T06_NoToNcReject(failures);
            T07_NoToMainReject(failures);
            T08_NoToCoilReject(failures);
            T09_CrossKmReject(failures);
            T10_TwoKmIndependentSwap(failures);

            if (failures.Count == 0)
            {
                Debug.Log("KM2B4_ContactorNoPairEquivalenceTests 通过 (10/10)");
            }
            else
            {
                foreach (var f in failures) Debug.LogError("[FAIL] " + f);
                Debug.LogError($"KM2B4_ContactorNoPairEquivalenceTests 失败 ({failures.Count} failures)");
            }
        }

        // === T01: 原始完全一致 → ExactMatch ===
        private static void T01_OriginalExactMatch(List<string> failures)
        {
            const string scenario = "T01-OriginalExactMatch";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            var candidate = BuildSingleKmGraph("13", "14", "33", "34");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "ExactMatch")
                failures.Add($"{scenario}: 期望 ExactMatch, 实际 {result}");
        }

        // === T02: 单 KM 两个 NO pair 完整交换 → EquivalentMatch ===
        private static void T02_SingleKmFullSwap(List<string> failures)
        {
            const string scenario = "T02-SingleKmFullSwap";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            var candidate = BuildSingleKmGraph("33", "34", "13", "14");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "EquivalentMatch")
                failures.Add($"{scenario}: 期望 EquivalentMatch, 实际 {result}");
        }

        // === T03: 只有一个 NO 被使用，swap → EquivalentMatch ===
        private static void T03_SingleNoUsedSwap(List<string> failures)
        {
            const string scenario = "T03-SingleNoUsedSwap";
            var template = BuildSingleNoGraph("13", "14");
            var candidate = BuildSingleNoGraph("33", "34");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "EquivalentMatch")
                failures.Add($"{scenario}: 期望 EquivalentMatch, 实际 {result}");
        }

        // === T04: 半 pair 替换 → NoMatch ===
        private static void T04_HalfPairReject(List<string> failures)
        {
            const string scenario = "T04-HalfPairReject";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            // 只换 13→33, 14 保持 14（半 pair）
            var candidate = BuildSingleKmGraph("33", "14", "13", "34");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T05: 两个支路共用同一个 NO pair → NoMatch ===
        private static void T05_BothBranchesSamePair(List<string> failures)
        {
            const string scenario = "T05-BothBranchesSamePair";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            // self-hold 和 linkage 都挂到 NO1(13/14)，NO2(33/34) 不用
            var candidate = new CircuitTopologyGraph();
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef });
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = LoadDef });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "13", NodeB = 1, TerminalB = "23" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "14", NodeB = 1, TerminalB = "24" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "13", NodeB = 2, TerminalB = "L" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "14", NodeB = 2, TerminalB = "N" });
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T06: NO→NC → NoMatch ===
        private static void T06_NoToNcReject(List<string> failures)
        {
            const string scenario = "T06-NoToNcReject";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            var candidate = BuildSingleKmGraph("21", "22", "33", "34");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T07: NO→主触点 → NoMatch ===
        private static void T07_NoToMainReject(List<string> failures)
        {
            const string scenario = "T07-NoToMainReject";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            var candidate = BuildSingleKmGraph("L1", "T1", "33", "34");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T08: NO→A1/A2 → NoMatch ===
        private static void T08_NoToCoilReject(List<string> failures)
        {
            const string scenario = "T08-NoToCoilReject";
            var template = BuildSingleKmGraph("13", "14", "33", "34");
            var candidate = BuildSingleKmGraph("A1", "A2", "33", "34");
            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T09: 跨 KM 交换 → NoMatch ===
        private static void T09_CrossKmReject(List<string> failures)
        {
            const string scenario = "T09-CrossKmReject";
            // Template: KM1 用 NO1(13/14)→Start, NO2(33/34)→Load; KM2 用 NO1(13/14)→Start
            var template = new CircuitTopologyGraph();
            template.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });     // 0: KM1
            template.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });     // 1: KM2
            template.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef });  // 2: Start
            template.Nodes.Add(new ComponentTopologyNode { DefinitionName = LoadDef });   // 3: Load
            template.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "13", NodeB = 2, TerminalB = "23" });
            template.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "14", NodeB = 2, TerminalB = "24" });
            template.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "33", NodeB = 3, TerminalB = "L" });
            template.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "34", NodeB = 3, TerminalB = "N" });
            template.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = "13", NodeB = 2, TerminalB = "23" });
            template.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = "14", NodeB = 2, TerminalB = "24" });

            // Candidate: KM1 NO2(33/34)→Start (取代 KM2 的 NO1 role), KM2 NO2(33/34)→Load (取代 KM1 的 NO2 role)
            // KM1 signature: 33:1,34:1 (2 terminals) vs template KM1: 13:1,14:1,33:1,34:1 (4 terminals) → 不匹配
            // KM2 signature: 33:1,34:1 (2 terminals) vs template KM2: 13:1,14:1 (2 terminals) → 不匹配
            // per-component permutation 无法跨 KM 修复
            var candidate = new CircuitTopologyGraph();
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });     // 0: KM1
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });     // 1: KM2
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef });  // 2: Start
            candidate.Nodes.Add(new ComponentTopologyNode { DefinitionName = LoadDef });   // 3: Load
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "33", NodeB = 2, TerminalB = "23" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "34", NodeB = 2, TerminalB = "24" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = "33", NodeB = 3, TerminalB = "L" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = "34", NodeB = 3, TerminalB = "N" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "13", NodeB = 1, TerminalB = "13" });
            candidate.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = "14", NodeB = 1, TerminalB = "14" });

            var result = Judge(candidate, template);
            Debug.Log($"[{scenario}] result={result}");
            if (result != "NoMatch")
                failures.Add($"{scenario}: 期望 NoMatch, 实际 {result}");
        }

        // === T10: 两只 KM 各自独立 swap → EquivalentMatch ===
        private static void T10_TwoKmIndependentSwap(List<string> failures)
        {
            const string scenario = "T10-TwoKmIndependentSwap";
            // Template: KM1 NO1→Start, NO2→Load; KM2 NO1→Start, NO2→Load
            var template = BuildTwoKmGraph("13", "14", "33", "34", "13", "14", "33", "34");

            // Candidate: KM1 swap, KM2 identity
            var candidateKm1SwapKm2Identity = BuildTwoKmGraph("33", "34", "13", "14", "13", "14", "33", "34");
            var result1 = Judge(candidateKm1SwapKm2Identity, template);
            Debug.Log($"[{scenario}-KM1swap-KM2identity] result={result1}");
            if (result1 != "EquivalentMatch")
                failures.Add($"{scenario}-KM1swap-KM2identity: 期望 EquivalentMatch, 实际 {result1}");

            // Candidate: KM1 swap, KM2 swap
            var candidateBothSwap = BuildTwoKmGraph("33", "34", "13", "14", "33", "34", "13", "14");
            var result2 = Judge(candidateBothSwap, template);
            Debug.Log($"[{scenario}-both-swap] result={result2}");
            if (result2 != "EquivalentMatch")
                failures.Add($"{scenario}-both-swap: 期望 EquivalentMatch, 实际 {result2}");
        }

        // === 判定函数 ===
        private static string Judge(CircuitTopologyGraph candidate, CircuitTopologyGraph template)
        {
            if (CircuitTopologyMatcher.IsExactMatch(candidate, template, out _))
                return "ExactMatch";
            if (ContactorNoPairPermutation.IsNoPairEquivalentMatch(candidate, template, out _))
                return "EquivalentMatch";
            return "NoMatch";
        }

        // === Graph 构造辅助 ===
        // 单 KM，NO1(startTerm1/startTerm2)→Start, NO2(loadTerm1/loadTerm2)→Load
        private static CircuitTopologyGraph BuildSingleKmGraph(
            string startTerm1, string startTerm2,
            string loadTerm1, string loadTerm2)
        {
            var graph = new CircuitTopologyGraph();
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });    // 0: KM
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef }); // 1: Start
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = LoadDef });  // 2: Load
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = startTerm1, NodeB = 1, TerminalB = "23" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = startTerm2, NodeB = 1, TerminalB = "24" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = loadTerm1, NodeB = 2, TerminalB = "L" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = loadTerm2, NodeB = 2, TerminalB = "N" });
            return graph;
        }

        // 单 KM，只使用一个 NO pair (term1/term2)→Start
        private static CircuitTopologyGraph BuildSingleNoGraph(string term1, string term2)
        {
            var graph = new CircuitTopologyGraph();
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });    // 0: KM
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef }); // 1: Start
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = term1, NodeB = 1, TerminalB = "23" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = term2, NodeB = 1, TerminalB = "24" });
            return graph;
        }

        // 两只 KM，各自 NO1→Start, NO2→Load
        private static CircuitTopologyGraph BuildTwoKmGraph(
            string km1Start1, string km1Start2,
            string km1Load1, string km1Load2,
            string km2Start1, string km2Start2,
            string km2Load1, string km2Load2)
        {
            var graph = new CircuitTopologyGraph();
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });    // 0: KM1
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = KmDef, Kind = ComponentKind.ContactorCoil });    // 1: KM2
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = StartDef }); // 2: Start
            graph.Nodes.Add(new ComponentTopologyNode { DefinitionName = LoadDef });  // 3: Load
            // KM1
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = km1Start1, NodeB = 2, TerminalB = "23" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = km1Start2, NodeB = 2, TerminalB = "24" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = km1Load1, NodeB = 3, TerminalB = "L" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 0, TerminalA = km1Load2, NodeB = 3, TerminalB = "N" });
            // KM2
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = km2Start1, NodeB = 2, TerminalB = "23" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = km2Start2, NodeB = 2, TerminalB = "24" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = km2Load1, NodeB = 3, TerminalB = "L" });
            graph.Edges.Add(new TerminalTopologyEdge { NodeA = 1, TerminalA = km2Load2, NodeB = 3, TerminalB = "N" });
            return graph;
        }
    }
}
