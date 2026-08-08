using System;
using System.Collections.Generic;
using ElectricalSim.Core;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 回归三相交流电源放大视觉与 TerminalView 命中层的局部坐标一致性。
    /// 可见端子圆心来自已放大 Body（1.5x）的设计锚点，TerminalView 必须落在同一位置。
    /// </summary>
    public static class AcSourceTerminalAlignmentTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const float Tolerance = 0.01f;

        private static readonly IReadOnlyDictionary<string, Vector2> ExpectedTerminalCenters =
            new Dictionary<string, Vector2>(StringComparer.Ordinal)
            {
                { "L1", new Vector2(-119.085f, 0.900f) },
                { "L2", new Vector2(-59.415f, 0.900f) },
                { "L3", new Vector2(0.270f, 0.900f) },
                { "N", new Vector2(59.925f, 0.900f) },
                { "PE", new Vector2(119.985f, 0.900f) }
            };

        [MenuItem("Tools/Tests/Run AC Source Terminal Alignment Tests")]
        public static void Run()
        {
            var failures = new List<string>();
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
                var saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
                var canvas = UnityEngine.Object.FindObjectOfType<Canvas>(true);
                if (workspace == null || saveLoad == null || canvas == null)
                {
                    throw new InvalidOperationException("AC 端子对齐测试依赖缺失：WorkspaceController、SaveLoadService 或 Canvas 未找到。");
                }

                if (workspace.IsInteractionLocked)
                {
                    workspace.ToggleInteractionLock();
                }
                workspace.ClearDrawing(false);

                var definition = FindDefinition(saveLoad, "AC_ThreePhase_Power");
                if (definition == null)
                {
                    throw new InvalidOperationException("Catalog 中未找到 AC_ThreePhase_Power。");
                }

                var component = workspace.SpawnComponent(definition, Vector2.zero, "ac-terminal-alignment", false);
                if (component == null)
                {
                    throw new InvalidOperationException("无法生成 AC_ThreePhase_Power。");
                }

                ValidateTerminalGeometry(component, failures);
                ValidateVisibleCentersAreInsideHitRects(component, canvas, failures);

                var singlePhaseDefinition = FindDefinition(saveLoad, "AC_220V_Power");
                if (singlePhaseDefinition == null)
                {
                    failures.Add("Catalog 中未找到 AC_220V_Power，无法完成回归检查。");
                }
                else
                {
                    var singlePhase = workspace.SpawnComponent(singlePhaseDefinition, new Vector2(500f, 0f), "ac220-terminal-regression", false);
                    ValidateTerminalViews(singlePhase, new[] { "L", "N", "L2", "N2" }, "AC_220V_Power", failures);
                }
            }
            catch (Exception exception)
            {
                failures.Add("测试执行出现未处理异常：" + exception);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("AC 交流电源端子视觉/命中对齐测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][AC-Terminal] 三相交流电源 L1/L2/L3/N/PE 视觉与命中坐标对齐：通过");
        }

        private static ComponentDefinition FindDefinition(SaveLoadService saveLoad, string definitionName)
        {
            if (saveLoad == null || saveLoad.Catalog == null)
            {
                return null;
            }

            foreach (var definition in saveLoad.Catalog)
            {
                if (definition != null && string.Equals(definition.name, definitionName, StringComparison.Ordinal))
                {
                    return definition;
                }
            }

            return null;
        }

        private static void ValidateTerminalGeometry(CircuitComponent component, ICollection<string> failures)
        {
            foreach (var expected in ExpectedTerminalCenters)
            {
                var terminal = component.GetTerminal(expected.Key);
                if (terminal == null)
                {
                    failures.Add("缺少端子 " + expected.Key + "。");
                    continue;
                }

                var rect = terminal.GetComponent<RectTransform>();
                if (rect == null)
                {
                    failures.Add(expected.Key + " 缺少 RectTransform。");
                    continue;
                }

                if (Vector2.Distance(rect.anchoredPosition, expected.Value) > Tolerance)
                {
                    failures.Add(string.Format(
                        "{0} 命中中心错误：实际 {1}，期望可见端子圆心 {2}。",
                        expected.Key,
                        rect.anchoredPosition,
                        expected.Value));
                }

                if (rect.sizeDelta.x < 30f || rect.sizeDelta.y < 30f)
                {
                    failures.Add(expected.Key + " 命中区域小于 30x30。");
                }
            }
        }

        private static void ValidateVisibleCentersAreInsideHitRects(
            CircuitComponent component,
            Canvas canvas,
            ICollection<string> failures)
        {
            Canvas.ForceUpdateCanvases();
            var eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            foreach (var expected in ExpectedTerminalCenters)
            {
                var terminal = component.GetTerminal(expected.Key);
                if (terminal == null)
                {
                    continue;
                }

                var rect = terminal.GetComponent<RectTransform>();
                if (rect == null)
                {
                    failures.Add(expected.Key + " 缺少 RectTransform，无法验证命中矩形。");
                    continue;
                }

                var screenPoint = RectTransformUtility.WorldToScreenPoint(
                    eventCamera,
                    component.transform.TransformPoint(expected.Value));
                if (!RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera))
                {
                    failures.Add(expected.Key + " 的可见端子圆心不在对应 TerminalView 命中矩形内。");
                }

                var image = terminal.GetComponent<Image>();
                if (image == null || !image.raycastTarget)
                {
                    failures.Add(expected.Key + " 的 TerminalView 未启用 Image raycastTarget。");
                }
            }
        }

        private static void ValidateTerminalViews(
            CircuitComponent component,
            IEnumerable<string> terminalIds,
            string definitionName,
            ICollection<string> failures)
        {
            if (component == null)
            {
                failures.Add(definitionName + " 无法生成，无法执行端子回归检查。");
                return;
            }

            foreach (var terminalId in terminalIds)
            {
                var terminal = component.GetTerminal(terminalId);
                var rect = terminal != null ? terminal.GetComponent<RectTransform>() : null;
                var image = terminal != null ? terminal.GetComponent<Image>() : null;
                if (terminal == null || rect == null || image == null || !image.raycastTarget ||
                    rect.sizeDelta.x <= 0f || rect.sizeDelta.y <= 0f)
                {
                    failures.Add(definitionName + "." + terminalId + " 的 TerminalView 或可点击命中层无效。");
                }
            }
        }
    }
}
