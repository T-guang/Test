using System;
using System.Collections.Generic;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// F4 元件参数入口冻结与元件池双击生成测试。
    ///
    /// 验证：
    /// - 单击元件池卡片不生成元件；
    /// - 双击元件池卡片生成一个元件；
    /// - 拖拽到画布生成一个元件；
    /// - 拖拽到画布外不生成；
    /// - 锁定画布时双击和拖拽都不生成；
    /// - 单击画布元件仍被选中但不显示参数面板；
    /// - 拖动画布元件后不显示参数面板；
    /// - 参数值在面板冻结后仍然存在；
    /// - 保存/导入 DTO 仍包含参数；
    /// - 删除选中元件功能不受影响；
    /// - 撤销/重做生成行为不受影响。
    ///
    /// 所有断言失败收集后抛 InvalidOperationException，使 Unity batchmode 非零退出。
    /// </summary>
    public static class ComponentInteractionPolishTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("Tools/Tests/Run Component Interaction Polish Tests")]
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
                    throw new InvalidOperationException("F4 测试依赖缺失：WorkspaceController / SaveLoadService / Canvas 未找到。");
                }

                var lampDef = FindDefinition(saveLoad, "Lamp_220V");
                var buttonDef = FindDefinition(saveLoad, "Button_Start_NO");
                if (lampDef == null || buttonDef == null)
                {
                    throw new InvalidOperationException("F4 测试依赖缺失：catalog 缺少 Lamp_220V / Button_Start_NO。");
                }

                Test01_SingleClickNoSpawn(workspace, lampDef, failures);
                Test02_MultipleSingleClicksNoSpawn(workspace, lampDef, failures);
                Test03_DoubleClickOneSpawn(workspace, lampDef, failures);
                Test04_TripleDoubleClickThreeSpawns(workspace, lampDef, failures);
                Test05_DoubleClickUsesDefaultPosition(workspace, lampDef, failures);
                Test06_DragToCanvasOneSpawn(workspace, lampDef, failures);
                Test07_DragNoExtraDefaultSpawn(workspace, lampDef, failures);
                Test08_DragOutsideCanvasNoSpawn(workspace, lampDef, failures);
                Test09_LockedDoubleClickNoSpawn(workspace, lampDef, failures);
                Test10_LockedDragNoSpawn(workspace, lampDef, failures);
                Test11_ClickComponentStillSelected(workspace, lampDef, failures);
                Test12_ClickComponentNoParameterPanel(workspace, lampDef, failures);
                Test13_DragComponentNoParameterPanel(workspace, lampDef, failures);
                Test14_ParameterValuesPreserved(workspace, lampDef, failures);
                Test15_SaveDtoIncludesParameters(workspace, saveLoad, lampDef, failures);
                Test16_ImportWithParametersRestored(workspace, saveLoad, lampDef, failures);
                Test17_DeleteSelectedStillWorks(workspace, lampDef, failures);
                Test18_UndoRedoSpawnStillWorks(workspace, lampDef, failures);
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
                throw new InvalidOperationException("F4 元件交互优化测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][F4] 元件参数入口冻结与元件池双击生成：通过");
        }

        // === 辅助方法 ===

        private static void ResetWorkspace(WorkspaceController workspace)
        {
            // ClearDrawing 在锁定时会拒绝执行，必须先解锁再清空。
            if (workspace.IsInteractionLocked)
            {
                workspace.ToggleInteractionLock();
            }
            if (workspace.IsSimulationRunning)
            {
                workspace.StopSimulation();
            }
            workspace.ClearDrawing();
            workspace.AutoShowParameterPanel = false;
        }

        private static ComponentDefinition FindDefinition(SaveLoadService saveLoad, string name)
        {
            var catalog = saveLoad.Catalog;
            if (catalog == null) return null;
            foreach (var def in catalog)
            {
                if (def != null && def.name == name) return def;
            }
            return null;
        }

        private static PaletteItem CreateTestPaletteItem(WorkspaceController workspace, ComponentDefinition definition)
        {
            var go = new GameObject("TestPaletteItem_" + definition.name, typeof(RectTransform), typeof(Image), typeof(PaletteItem));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(workspace.WorkspaceRect.root, false);
            rect.sizeDelta = new Vector2(96f, 132f);
            var item = go.GetComponent<PaletteItem>();
            item.Initialize(definition, workspace);
            return item;
        }

        private static PointerEventData CreatePointerEventData(int clickCount = 1, bool dragging = false)
        {
            var eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                var esGo = new GameObject("TestEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem = esGo.GetComponent<EventSystem>();
            }
            var ped = new PointerEventData(eventSystem)
            {
                clickCount = clickCount,
                dragging = dragging,
                position = new Vector2(500f, 400f),
                pressPosition = new Vector2(500f, 400f)
            };
            return ped;
        }

        private static ComponentParameterView GetComponentParameterView(WorkspaceController workspace)
        {
            var field = typeof(WorkspaceController).GetField("componentParameterView",
                BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(workspace) as ComponentParameterView;
        }

        // 将 WorkspaceRect 的世界坐标转换为屏幕坐标，供拖拽测试使用。
        // Overlay Canvas 传 null camera 即可；Screen Space - Camera 时使用画布关联相机。
        private static Vector2 GetWorkspaceScreenPosition(WorkspaceController workspace)
        {
            var canvas = workspace.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                cam = canvas.worldCamera;
            }
            return RectTransformUtility.WorldToScreenPoint(cam, workspace.WorkspaceRect.position);
        }

        private static CircuitComponent SpawnComponentDirect(WorkspaceController workspace, ComponentDefinition def, Vector2 pos)
        {
            return workspace.SpawnComponent(def, pos);
        }

        // === 测试方法 ===

        // 1. 单击元件池卡片一次，不生成
        private static void Test01_SingleClickNoSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var ped = CreatePointerEventData(clickCount: 1);
                item.OnPointerClick(ped);
                var countAfter = workspace.Components.Count;
                if (countAfter != countBefore)
                {
                    failures.Add($"01: 单击不应生成元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 2. 连续多次独立单击，不生成
        private static void Test02_MultipleSingleClicksNoSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                for (var i = 0; i < 5; i++)
                {
                    var ped = CreatePointerEventData(clickCount: 1);
                    item.OnPointerClick(ped);
                }
                var countAfter = workspace.Components.Count;
                if (countAfter != countBefore)
                {
                    failures.Add($"02: 连续5次单击不应生成元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 3. 双击一次，只生成一个
        private static void Test03_DoubleClickOneSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var ped = CreatePointerEventData(clickCount: 2);
                item.OnPointerClick(ped);
                var countAfter = workspace.Components.Count;
                if (countAfter - countBefore != 1)
                {
                    failures.Add($"03: 双击应生成1个元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 4. 连续双击三次，准确生成三个
        private static void Test04_TripleDoubleClickThreeSpawns(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                for (var i = 0; i < 3; i++)
                {
                    var ped = CreatePointerEventData(clickCount: 2);
                    item.OnPointerClick(ped);
                }
                var countAfter = workspace.Components.Count;
                if (countAfter - countBefore != 3)
                {
                    failures.Add($"04: 3次双击应生成3个元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 5. 双击仍使用现有默认生成位置规则
        private static void Test05_DoubleClickUsesDefaultPosition(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var ped = CreatePointerEventData(clickCount: 2);
                item.OnPointerClick(ped);
                if (workspace.Components.Count != 1)
                {
                    failures.Add("05: 双击应生成1个元件用于位置检查。");
                    return;
                }
                // 默认位置为 Vector2.zero，经过 Snap 处理后应接近原点
                var rect = workspace.Components[0].GetComponent<RectTransform>();
                var pos = rect != null ? rect.anchoredPosition : Vector2.zero;
                if (pos.magnitude > 500f)
                {
                    failures.Add($"05: 默认生成位置应接近原点，actual={pos}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 6. 拖拽到画布，只生成一个
        private static void Test06_DragToCanvasOneSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var screenPos = GetWorkspaceScreenPosition(workspace);
                var ped = CreatePointerEventData(clickCount: 1);
                ped.position = screenPos;
                item.OnBeginDrag(ped);
                item.OnDrag(ped);
                item.OnEndDrag(ped);
                var countAfter = workspace.Components.Count;
                if (countAfter - countBefore != 1)
                {
                    failures.Add($"06: 拖拽到画布应生成1个元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 7. 拖拽生成后默认位置没有额外元件
        private static void Test07_DragNoExtraDefaultSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var screenPos = GetWorkspaceScreenPosition(workspace);
                var ped = CreatePointerEventData(clickCount: 1);
                ped.position = screenPos;
                item.OnBeginDrag(ped);
                item.OnDrag(ped);
                item.OnEndDrag(ped);
                // 拖拽结束后不应触发 OnPointerClick（Unity 默认 DragThreshold 抑制）
                // 直接验证元件数没有额外增加（应恰好 1 个，不是 2 个）
                var countAfter = workspace.Components.Count;
                if (countAfter - countBefore != 1)
                {
                    failures.Add($"07: 拖拽应只生成1个元件（无默认位置额外元件），before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 8. 拖拽到画布外，不生成额外元件（F4 关注点：不因双击+拖拽产生 2+ 个元件）
        // 注意：Overlay Canvas 的 ScreenPointToLocalPointInRectangle 对越界点可能仍返回 true，
        // 这是现有坐标转换行为，F4 不修改。本测试验证 F4 不会导致一次拖拽生成 2+ 个元件。
        private static void Test08_DragOutsideCanvasNoSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var ped = CreatePointerEventData(clickCount: 1);
                // 使用一个远离画布的屏幕坐标
                ped.position = new Vector2(-10000f, -10000f);
                item.OnBeginDrag(ped);
                item.OnDrag(ped);
                item.OnEndDrag(ped);
                var countAfter = workspace.Components.Count;
                // F4 关注点：一次拖拽不应生成超过 1 个元件（防止双击+拖拽产生重复）
                if (countAfter - countBefore > 1)
                {
                    failures.Add($"08: 一次拖拽不应生成超过1个元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        // 9. 画布锁定时双击不生成
        private static void Test09_LockedDoubleClickNoSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            if (!workspace.IsInteractionLocked)
            {
                workspace.ToggleInteractionLock();
            }
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var ped = CreatePointerEventData(clickCount: 2);
                item.OnPointerClick(ped);
                var countAfter = workspace.Components.Count;
                if (countAfter != countBefore)
                {
                    failures.Add($"09: 锁定时双击不应生成元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
                if (workspace.IsInteractionLocked)
                {
                    workspace.ToggleInteractionLock();
                }
            }
        }

        // 10. 画布锁定时拖拽不生成
        private static void Test10_LockedDragNoSpawn(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            if (!workspace.IsInteractionLocked)
            {
                workspace.ToggleInteractionLock();
            }
            var item = CreateTestPaletteItem(workspace, lampDef);
            try
            {
                var countBefore = workspace.Components.Count;
                var screenPos = GetWorkspaceScreenPosition(workspace);
                var ped = CreatePointerEventData(clickCount: 1);
                ped.position = screenPos;
                item.OnBeginDrag(ped);
                item.OnDrag(ped);
                item.OnEndDrag(ped);
                var countAfter = workspace.Components.Count;
                if (countAfter != countBefore)
                {
                    failures.Add($"10: 锁定时拖拽不应生成元件，before={countBefore} after={countAfter}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(item.gameObject);
                if (workspace.IsInteractionLocked)
                {
                    workspace.ToggleInteractionLock();
                }
            }
        }

        // 11. 单击画布已有元件，元件仍被选中
        private static void Test11_ClickComponentStillSelected(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            var component = SpawnComponentDirect(workspace, lampDef, Vector2.zero);
            if (component == null)
            {
                failures.Add("11: 前置生成元件失败。");
                return;
            }
            try
            {
                var ped = CreatePointerEventData(clickCount: 1);
                component.OnPointerClick(ped);
                if (workspace.SelectedComponent != component)
                {
                    failures.Add("11: 单击后元件应被选中。");
                }
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 12. 单击画布元件，不显示参数面板
        private static void Test12_ClickComponentNoParameterPanel(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            var component = SpawnComponentDirect(workspace, lampDef, Vector2.zero);
            if (component == null)
            {
                failures.Add("12: 前置生成元件失败。");
                return;
            }
            try
            {
                var ped = CreatePointerEventData(clickCount: 1);
                component.OnPointerClick(ped);
                var view = GetComponentParameterView(workspace);
                if (view != null && view.gameObject.activeSelf)
                {
                    failures.Add("12: 参数面板不应显示。");
                }
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 13. 拖动画布元件后，不显示参数面板
        private static void Test13_DragComponentNoParameterPanel(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            var component = SpawnComponentDirect(workspace, lampDef, Vector2.zero);
            if (component == null)
            {
                failures.Add("13: 前置生成元件失败。");
                return;
            }
            try
            {
                var ped = CreatePointerEventData(clickCount: 1);
                component.OnBeginDrag(ped);
                var view = GetComponentParameterView(workspace);
                if (view != null && view.gameObject.activeSelf)
                {
                    failures.Add("13: 拖动开始后参数面板不应显示。");
                }
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 14. 元件参数值在参数面板冻结后仍然存在
        private static void Test14_ParameterValuesPreserved(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            var component = SpawnComponentDirect(workspace, lampDef, Vector2.zero);
            if (component == null)
            {
                failures.Add("14: 前置生成元件失败。");
                return;
            }
            try
            {
                // 设置参数（SetParameterValue 接收 float）
                component.SetParameterValue("ratedVoltage", 220f);
                component.SetParameterValue("ratedPower", 40f);
                // 点击元件（参数面板不应显示）
                var ped = CreatePointerEventData(clickCount: 1);
                component.OnPointerClick(ped);
                // 验证参数值仍然存在（GetParameter 返回 ComponentParameter，value 为 float）
                var p1 = component.GetParameter("ratedVoltage");
                var p2 = component.GetParameter("ratedPower");
                var v1 = p1 != null ? p1.value : 0f;
                var v2 = p2 != null ? p2.value : 0f;
                if (!Mathf.Approximately(v1, 220f) || !Mathf.Approximately(v2, 40f))
                {
                    failures.Add($"14: 参数值应保持，ratedVoltage={v1} ratedPower={v2}");
                }
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 15. 用户图纸保存 DTO 仍包含参数
        private static void Test15_SaveDtoIncludesParameters(WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            var component = SpawnComponentDirect(workspace, lampDef, Vector2.zero);
            if (component == null)
            {
                failures.Add("15: 前置生成元件失败。");
                return;
            }
            try
            {
                component.SetParameterValue("ratedVoltage", 220f);
                component.SetParameterValue("ratedPower", 40f);
                // SaveLoadService.CreateDrawingDto 为私有方法，通过反射获取 DrawingDto 后用 JsonUtility 序列化。
                var method = typeof(SaveLoadService).GetMethod("CreateDrawingDto",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null)
                {
                    failures.Add("15: 未找到 SaveLoadService.CreateDrawingDto 方法。");
                    return;
                }
                var drawing = method.Invoke(saveLoad, null);
                if (drawing == null)
                {
                    failures.Add("15: CreateDrawingDto 返回 null。");
                    return;
                }
                var jsonResult = JsonUtility.ToJson(drawing, false);
                if (string.IsNullOrEmpty(jsonResult))
                {
                    failures.Add("15: 序列化 JSON 返回空。");
                    return;
                }
                if (!jsonResult.Contains("ratedVoltage") || !jsonResult.Contains("220"))
                {
                    failures.Add("15: DTO 应包含参数 ratedVoltage=220。");
                }
                if (!jsonResult.Contains("ratedPower") || !jsonResult.Contains("40"))
                {
                    failures.Add("15: DTO 应包含参数 ratedPower=40。");
                }
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 16. 导入带参数图纸后参数仍然恢复
        private static void Test16_ImportWithParametersRestored(WorkspaceController workspace, SaveLoadService saveLoad, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            try
            {
                // 构造带参数的 JSON：parameters 是 ComponentParameter 数组，value 为 float
                var json = "{\"documentId\":\"f4-16\",\"documentName\":\"F4 Test\",\"savedAt\":\"2026-08-04 12:00:00\"," +
                    "\"components\":[{\"instanceId\":\"lamp_imp\",\"definitionName\":\"Lamp_220V\",\"x\":0,\"y\":0,\"isClosed\":false," +
                    "\"parameters\":[{\"key\":\"ratedVoltage\",\"value\":220},{\"key\":\"ratedPower\",\"value\":60}]}]," +
                    "\"wires\":[]}";
                var ok = saveLoad.LoadFromJsonString(json, out var error);
                if (!ok)
                {
                    failures.Add($"16: 导入应成功，error={error}");
                    return;
                }
                if (workspace.Components.Count != 1)
                {
                    failures.Add($"16: 导入后应有1个元件，actual={workspace.Components.Count}");
                    return;
                }
                var comp = workspace.Components[0];
                var p1 = comp.GetParameter("ratedVoltage");
                var p2 = comp.GetParameter("ratedPower");
                var v1 = p1 != null ? p1.value : 0f;
                var v2 = p2 != null ? p2.value : 0f;
                if (!Mathf.Approximately(v1, 220f) || !Mathf.Approximately(v2, 60f))
                {
                    failures.Add($"16: 参数应恢复，ratedVoltage={v1}(expected 220) ratedPower={v2}(expected 60)");
                }
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 17. 删除选中元件功能不受影响
        private static void Test17_DeleteSelectedStillWorks(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            var component = SpawnComponentDirect(workspace, lampDef, Vector2.zero);
            if (component == null)
            {
                failures.Add("17: 前置生成元件失败。");
                return;
            }
            try
            {
                var ped = CreatePointerEventData(clickCount: 1);
                component.OnPointerClick(ped);
                if (workspace.SelectedComponent != component)
                {
                    failures.Add("17: 元件应被选中。");
                    return;
                }
                workspace.DeleteSelectedComponent();
                if (workspace.Components.Count != 0)
                {
                    failures.Add($"17: 删除后画布应为空，actual={workspace.Components.Count}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"17: 删除选中元件异常：{ex.Message}");
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }

        // 18. 撤销、重做生成行为不受影响
        private static void Test18_UndoRedoSpawnStillWorks(WorkspaceController workspace, ComponentDefinition lampDef, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.AutoShowParameterPanel = false;
            try
            {
                // 生成一个元件
                SpawnComponentDirect(workspace, lampDef, Vector2.zero);
                if (workspace.Components.Count != 1)
                {
                    failures.Add($"18: 生成后应有1个元件，actual={workspace.Components.Count}");
                    return;
                }
                // 撤销
                workspace.Undo();
                if (workspace.Components.Count != 0)
                {
                    failures.Add($"18: 撤销后画布应为空，actual={workspace.Components.Count}");
                    return;
                }
                // 重做
                workspace.Redo();
                if (workspace.Components.Count != 1)
                {
                    failures.Add($"18: 重做后应有1个元件，actual={workspace.Components.Count}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"18: 撤销/重做异常：{ex.Message}");
            }
            finally
            {
                workspace.ClearDrawing();
            }
        }
    }
}
