using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.Practice;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 验证锁定画布从图纸集、仿真广场或练习入口加载时，只出现一个可关闭的提示弹窗，
    /// 且不会改变工作区、页面导航、模板身份、参考图纸或练习会话。
    /// 所有断言失败都会抛出异常，使 batchmode 以非零状态退出。
    /// </summary>
    public static class LockedCanvasLoadDialogTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string TemplateId = "single_lamp_template";

        [MenuItem("Tools/Tests/Run Locked Canvas Load Dialog Tests")]
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
                    throw new InvalidOperationException("E3.2 测试依赖缺失：WorkspaceController、SaveLoadService 或 Canvas 未找到。");
                }

                var item = LoadTemplateItem(TemplateId);
                var loader = EnsureTemplateLoader(workspace, saveLoad);
                var practice = PracticeSessionController.Instance;
                InvokePrivate(practice, "EnsureReferences");

                TestLockedTemplateAndGallery(workspace, loader, item, failures);
                TestLockedPracticeViaBlueprint(workspace, practice, item, failures);
                TestLockedEmptyCanvas(workspace, loader, item, failures);
                TestDialogReentryAndClose(workspace, loader, item, failures);
                TestUnlockedTemplateControl(workspace, loader, item, failures);
                TestUnlockedPracticeControl(workspace, practice, item, failures);
            }
            catch (Exception exception)
            {
                failures.Add("测试执行异常：" + exception);
            }
            finally
            {
                try { EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single); } catch { }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("E3.2 锁定画布加载弹窗测试失败：\n- " + string.Join("\n- ", failures));
            }

            Debug.Log("[Electrical][E3.2] 锁定画布加载提示：通过");
        }

        private static void TestLockedTemplateAndGallery(WorkspaceController workspace, TemplateLoadController loader, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            ResetWorkspace(workspace);
            SpawnSentinelCircuit(workspace, failures);
            var before = CaptureWorkspace(workspace);
            workspace.ToggleInteractionLock();
            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(item, () => callbacks++);
            AssertLockedInvariant("图纸集加载", workspace, before, callbacks, failures);
            CloseLockedDialog();

            var gallery = UnityEngine.Object.FindObjectOfType<SimulationGalleryPageController>(true);
            var temporaryGallery = false;
            if (gallery == null)
            {
                // Demo 场景不常驻仿真广场页面；临时挂载真实页面 Controller，仍通过其 LoadEntry 入口验证。
                gallery = new GameObject("E32SimulationGalleryEntry", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).AddComponent<SimulationGalleryPageController>();
                temporaryGallery = true;
            }

            InvokeGalleryLoadEntry(gallery, item);
            AssertLockedInvariant("仿真广场加载", workspace, before, 0, failures);
            CloseLockedDialog();
            if (temporaryGallery) UnityEngine.Object.DestroyImmediate(gallery.gameObject);
        }

        private static void TestLockedPracticeViaBlueprint(WorkspaceController workspace, PracticeSessionController practice, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            ResetWorkspace(workspace);
            SpawnSentinelCircuit(workspace, failures);
            var before = CaptureWorkspace(workspace);
            workspace.ToggleInteractionLock();

            var blueprintObject = new GameObject("E32BlueprintEntry");
            var blueprint = blueprintObject.AddComponent<BlueprintController>();
            SetPrivateField(blueprint, "dynamicTemplates", new List<CircuitTemplateCatalogItemDto> { item });
            SetPrivateField(blueprint, "selectedIndex", 0);
            InvokePrivate(blueprint, "EnterConfiguration");

            AssertLockedInvariant("图纸集进入练习", workspace, before, 0, failures);
            if (practice.IsPracticeActive || practice.CurrentTemplateItem != null || practice.CurrentTemplateData != null)
            {
                failures.Add("图纸集进入练习：锁定后不应建立或替换练习会话。");
            }
            CloseLockedDialog();
            UnityEngine.Object.DestroyImmediate(blueprintObject);
        }

        private static void TestLockedEmptyCanvas(WorkspaceController workspace, TemplateLoadController loader, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            ResetWorkspace(workspace);
            var before = CaptureWorkspace(workspace);
            workspace.ToggleInteractionLock();
            loader.RequestLoadTemplateFromGallery(item);
            AssertLockedInvariant("锁定空画布", workspace, before, 0, failures);
            CloseLockedDialog();
        }

        private static void TestDialogReentryAndClose(WorkspaceController workspace, TemplateLoadController loader, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            ResetWorkspace(workspace);
            workspace.ToggleInteractionLock();
            for (var i = 0; i < 5; i++) loader.RequestLoadTemplateFromGallery(item);

            var instances = Resources.FindObjectsOfTypeAll<LockedCanvasLoadDialog>().Count(d => d != null && d.gameObject.scene.IsValid());
            if (instances != 1) failures.Add("防重入：连续点击后应只有一个锁定提示弹窗，实际=" + instances);
            AssertDialogContent(failures);
            CloseLockedDialog();
            if (GameObject.Find("LockedCanvasLoadDialog") != null) failures.Add("关闭提示后弹窗应隐藏。");

            loader.RequestLoadTemplateFromGallery(item);
            AssertDialogContent(failures);
            CloseLockedDialog();
        }

        private static void TestUnlockedTemplateControl(WorkspaceController workspace, TemplateLoadController loader, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            ResetWorkspace(workspace);
            var callbacks = 0;
            loader.RequestLoadTemplateFromGallery(item, () => callbacks++);
            if (callbacks != 1) failures.Add("未锁定图纸集加载：成功回调应执行一次，实际=" + callbacks);
            if (workspace.Components.Count == 0 || TemplateEditSession.CurrentTemplateId != item.templateId)
            {
                failures.Add("未锁定图纸集加载：模板未按原逻辑成功生成或记录身份。");
            }
            if (GameObject.Find("LockedCanvasLoadDialog") != null) failures.Add("未锁定图纸集加载：不应显示锁定提示。");
        }

        private static void TestUnlockedPracticeControl(WorkspaceController workspace, PracticeSessionController practice, CircuitTemplateCatalogItemDto item, List<string> failures)
        {
            ResetWorkspace(workspace);
            var callbacks = 0;
            practice.StartPractice(item, () => callbacks++);
            if (!practice.IsPracticeActive || practice.CurrentTemplateItem != item || practice.CurrentTemplateData == null || callbacks != 1)
            {
                failures.Add("未锁定练习入口：练习会话、模板数据或成功回调未保持原有行为。");
            }
            if (GameObject.Find("LockedCanvasLoadDialog") != null) failures.Add("未锁定练习入口：不应显示锁定提示。");
        }

        private static void AssertLockedInvariant(string scenario, WorkspaceController workspace, WorkspaceSnapshot before, int callbacks, List<string> failures)
        {
            if (!workspace.IsInteractionLocked) failures.Add(scenario + "：画布锁定状态被改变。");
            if (callbacks != 0) failures.Add(scenario + "：不应调用加载成功回调。");
            var after = CaptureWorkspace(workspace);
            if (!before.Equals(after)) failures.Add(scenario + "：锁定拒绝后工作区组件或 Wire 发生变化。");
            AssertDialogContent(failures, scenario);
        }

        private static void AssertDialogContent(List<string> failures, string scenario = "锁定提示")
        {
            var dialog = GameObject.Find("LockedCanvasLoadDialog");
            if (dialog == null) { failures.Add(scenario + "：未显示锁定提示弹窗。"); return; }
            var title = dialog.transform.Find("DialogPanel/Title")?.GetComponent<Text>();
            var message = dialog.transform.Find("DialogPanel/Message")?.GetComponent<Text>();
            var acknowledge = dialog.transform.Find("DialogPanel/AcknowledgeButton")?.GetComponent<Button>();
            if (title == null || title.text != LockedCanvasLoadDialog.Title) failures.Add(scenario + "：弹窗标题不正确。");
            if (message == null || message.text != LockedCanvasLoadDialog.Message) failures.Add(scenario + "：弹窗正文不正确。");
            var label = acknowledge != null ? acknowledge.GetComponentInChildren<Text>() : null;
            if (acknowledge == null || label == null || label.text != LockedCanvasLoadDialog.AcknowledgeLabel) failures.Add(scenario + "：未找到“我知道了”按钮。");
        }

        private static void CloseLockedDialog()
        {
            var dialog = GameObject.Find("LockedCanvasLoadDialog");
            if (dialog == null) return;
            var button = dialog.transform.Find("DialogPanel/AcknowledgeButton")?.GetComponent<Button>();
            button?.onClick.Invoke();
        }

        private static TemplateLoadController EnsureTemplateLoader(WorkspaceController workspace, SaveLoadService saveLoad)
        {
            var loader = UnityEngine.Object.FindObjectOfType<TemplateLoadController>(true);
            if (loader == null) loader = new GameObject("E32TemplateLoadController").AddComponent<TemplateLoadController>();
            SetPrivateField(loader, "workspace", workspace);
            SetPrivateField(loader, "saveLoadService", saveLoad);
            return loader;
        }

        private static CircuitTemplateCatalogItemDto LoadTemplateItem(string templateId)
        {
            var asset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            var catalog = asset != null ? JsonUtility.FromJson<CircuitTemplateCatalogDto>(asset.text) : null;
            var item = catalog != null && catalog.templates != null ? catalog.templates.FirstOrDefault(x => x.templateId == templateId) : null;
            if (item == null) throw new InvalidOperationException("测试模板缺失：" + templateId);
            return item;
        }

        private static void InvokeGalleryLoadEntry(SimulationGalleryPageController gallery, CircuitTemplateCatalogItemDto item)
        {
            var entryType = typeof(SimulationGalleryPageController).GetNestedType("GalleryEntry", BindingFlags.NonPublic);
            var entry = Activator.CreateInstance(entryType, true);
            entryType.GetField("CatalogItem", BindingFlags.Public | BindingFlags.Instance).SetValue(entry, item);
            typeof(SimulationGalleryPageController).GetMethod("LoadEntry", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gallery, new[] { entry });
        }

        private static void SpawnSentinelCircuit(WorkspaceController workspace, List<string> failures)
        {
            var definition = ScriptableObject.CreateInstance<ComponentDefinition>();
            definition.kind = ComponentKind.Switch;
            definition.displayName = "E3.2 Sentinel";
            definition.size = new Vector2(100f, 80f);
            definition.terminals = new List<TerminalDefinition>
            {
                new TerminalDefinition { id = "T1", label = "T1", normalizedPosition = new Vector2(0f, 0.5f) },
                new TerminalDefinition { id = "T2", label = "T2", normalizedPosition = new Vector2(1f, 0.5f) }
            };
            var first = workspace.SpawnComponent(definition, new Vector2(-50f, 0f), "e32-a", false);
            var second = workspace.SpawnComponent(definition, new Vector2(50f, 0f), "e32-b", false);
            if (first == null || second == null || workspace.WireManager.CreateWire(first.GetTerminal("T1"), second.GetTerminal("T1"), Color.yellow, WireStyle.Straight) == null)
            {
                failures.Add("无法建立锁定不变量测试所需的哨兵画布。");
            }
        }

        private static WorkspaceSnapshot CaptureWorkspace(WorkspaceController workspace)
        {
            var componentIds = workspace.Components.Select(component => component.InstanceId).OrderBy(id => id).ToArray();
            var wires = workspace.WireManager.Wires.Select(wire =>
            {
                var start = wire.StartTerminal.GetComponentInParent<CircuitComponent>()?.InstanceId + ":" + wire.StartTerminal.TerminalId;
                var end = wire.EndTerminal.GetComponentInParent<CircuitComponent>()?.InstanceId + ":" + wire.EndTerminal.TerminalId;
                return string.CompareOrdinal(start, end) < 0 ? start + "|" + end : end + "|" + start;
            }).OrderBy(value => value).ToArray();
            var practice = PracticeSessionController.Instance;
            var referencePanel = UnityEngine.Object.FindObjectOfType<BlueprintReferencePanel>(true);
            var pageRouter = UnityEngine.Object.FindObjectOfType<PageRouter>(true);
            return new WorkspaceSnapshot(
                componentIds,
                wires,
                TemplateEditSession.CurrentTemplateId,
                practice.IsPracticeActive,
                practice.CurrentTemplateItem != null ? practice.CurrentTemplateItem.templateId : null,
                referencePanel != null && referencePanel.gameObject.activeSelf,
                pageRouter != null ? pageRouter.CurrentPage.ToString() : null);
        }

        private static void ResetWorkspace(WorkspaceController workspace)
        {
            if (workspace.IsInteractionLocked) workspace.ToggleInteractionLock();
            workspace.ClearDrawing(true);
            PracticeSessionController.Instance.ClearPracticeState();
            CloseLockedDialog();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string methodName)
        {
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(target, null);
        }

        private readonly struct WorkspaceSnapshot : IEquatable<WorkspaceSnapshot>
        {
            private readonly string components;
            private readonly string wires;
            private readonly string templateId;
            private readonly bool practiceActive;
            private readonly string practiceTemplateId;
            private readonly bool referenceVisible;
            private readonly string currentPage;
            public WorkspaceSnapshot(
                IEnumerable<string> componentIds,
                IEnumerable<string> wireEndpoints,
                string templateId,
                bool practiceActive,
                string practiceTemplateId,
                bool referenceVisible,
                string currentPage)
            {
                components = string.Join(",", componentIds);
                wires = string.Join(",", wireEndpoints);
                this.templateId = templateId ?? string.Empty;
                this.practiceActive = practiceActive;
                this.practiceTemplateId = practiceTemplateId ?? string.Empty;
                this.referenceVisible = referenceVisible;
                this.currentPage = currentPage ?? string.Empty;
            }
            public bool Equals(WorkspaceSnapshot other) =>
                components == other.components && wires == other.wires && templateId == other.templateId &&
                practiceActive == other.practiceActive && practiceTemplateId == other.practiceTemplateId &&
                referenceVisible == other.referenceVisible && currentPage == other.currentPage;
        }
    }
}
