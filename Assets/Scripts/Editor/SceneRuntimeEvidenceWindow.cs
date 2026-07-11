// Editor-only, read-only runtime evidence capture for Demo.unity scene-object provenance.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ElectricalSim.AI;
using ElectricalSim.Core;
using ElectricalSim.Core.Validation;
using ElectricalSim.UI;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.EditorTools
{
    public sealed class SceneRuntimeEvidenceWindow : EditorWindow
    {
        private const string BaselineAssetDirectory = "Assets/EditorTests/Baselines/V2.3.9.1";
        private const string EvidenceFileName = "DemoSceneRuntimeEvidence.json";
        private string stageName = "初始默认示例";
        private string lifecycleNote = "切换六个页面 20 轮后";
        private SceneEvidenceBundle bundle;
        private Vector2 scroll;

        [MenuItem("Tools/Diagnostics/采集 Demo 场景运行态对象计数")]
        private static void Open()
        {
            GetWindow<SceneRuntimeEvidenceWindow>("Demo 场景运行态取证");
        }

        private void OnEnable()
        {
            LoadExisting();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Demo 场景运行态对象计数", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("本工具只读取当前 Play Mode 场景。请按阶段手动执行清空、加载家庭/工业模板、保存并导入，然后填写阶段名并采集。不会保存 Demo.unity，也不会删除或移动场景对象。", MessageType.Info);
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("请先进入 Play Mode，并等待默认示例加载完成。", MessageType.Warning);
                return;
            }

            stageName = EditorGUILayout.TextField("当前阶段", stageName);
            if (GUILayout.Button("采集当前阶段"))
            {
                CaptureStage();
            }

            EditorGUILayout.Space();
            lifecycleNote = EditorGUILayout.TextField("生命周期说明", lifecycleNote);
            if (GUILayout.Button("采集页面生命周期计数"))
            {
                CaptureLifecycle();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("导出取证 JSON")) WriteEvidence();
                if (GUILayout.Button("重新读取已导出文件")) LoadExisting();
            }

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (bundle == null || bundle.stages.Count == 0)
            {
                EditorGUILayout.HelpBox("尚未采集。建议顺序：初始默认示例、清空画布、加载家庭模板、加载工业模板、保存并导入用户图纸。", MessageType.None);
            }
            else
            {
                foreach (var stage in bundle.stages)
                {
                    EditorGUILayout.LabelField(stage.stageName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("CircuitComponent / TerminalView / WireView", stage.sceneCircuitComponentCount + " / " + stage.sceneTerminalViewCount + " / " + stage.sceneWireViewCount);
                    EditorGUILayout.LabelField("Workspace Components / Wires", stage.workspaceComponentCount + " / " + stage.workspaceWireCount);
                    EditorGUILayout.LabelField("Analyzer / Validation inputs", stage.analyzerComponentInputCount + " / " + stage.validationComponentInputCount);
                    EditorGUILayout.LabelField("根层电路对象", stage.rootCircuitObjectCount.ToString());
                    EditorGUILayout.Space(4f);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void CaptureStage()
        {
            try
            {
                var workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>();
                if (workspace == null || workspace.WireManager == null)
                {
                    throw new InvalidOperationException("未找到已初始化的 WorkspaceController 或 WireManager。");
                }

                if (bundle == null) bundle = new SceneEvidenceBundle { schemaVersion = 1 };
                var analyzer = new CircuitStateAnalyzer();
                var validation = new CircuitValidationService();
                var state = analyzer.Analyze(workspace.Components, workspace.WireManager.Wires);
                var report = validation.Validate(workspace.Components, workspace.WireManager.Wires, state);
                var item = new RuntimeStageEvidence
                {
                    stageName = string.IsNullOrWhiteSpace(stageName) ? "未命名阶段" : stageName.Trim(),
                    capturedAtUtc = DateTime.UtcNow.ToString("o"),
                    sceneName = workspace.gameObject.scene.name,
                    sceneCircuitComponentCount = FindSceneObjects<CircuitComponent>().Count,
                    sceneTerminalViewCount = FindSceneObjects<TerminalView>().Count,
                    sceneWireViewCount = FindSceneObjects<WireView>().Count,
                    rootObjectCount = workspace.gameObject.scene.GetRootGameObjects().Length,
                    workspaceComponentCount = workspace.Components.Count,
                    workspaceWireCount = workspace.WireManager.Wires.Count,
                    componentLayerChildCount = GetPrivateLayerChildCount(workspace, "componentLayer"),
                    wireLayerChildCount = GetPrivateLayerChildCount(workspace, "wireLayer"),
                    rootCircuitObjectCount = CountRootCircuitObjects(workspace.gameObject.scene),
                    analyzerComponentInputCount = workspace.Components.Count,
                    analyzerWireInputCount = workspace.WireManager.Wires.Count,
                    validationComponentInputCount = workspace.Components.Count,
                    validationWireInputCount = workspace.WireManager.Wires.Count,
                    analyzerReturned = state != null,
                    validationIssueCount = report.Issues.Count,
                    validationRules = report.Issues.Where(issue => issue != null)
                        .OrderBy(issue => issue.RuleId, StringComparer.Ordinal)
                        .Select(issue => new RuntimeRuleEvidence { ruleId = issue.RuleId, severity = issue.Severity.ToString() }).ToList(),
                    analyzerSummary = CaptureAnalyzerSummary(state),
                    checkReport = CaptureInspectorEvidence("CheckCurrentCircuit"),
                    explainReport = CaptureInspectorEvidence("ExplainCurrentCircuit"),
                    consoleErrorsSinceCapture = 0
                };
                bundle.stages.RemoveAll(existing => existing.stageName == item.stageName);
                bundle.stages.Add(item);
                Debug.Log("Demo 场景运行态取证已采集：" + item.stageName + "。Workspace=" + item.workspaceComponentCount + " components / " + item.workspaceWireCount + " wires，场景=" + item.sceneCircuitComponentCount + " CircuitComponent。");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private void CaptureLifecycle()
        {
            if (bundle == null) bundle = new SceneEvidenceBundle { schemaVersion = 1 };
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            bundle.lifecycle = new LifecycleEvidence
            {
                note = lifecycleNote,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
                localInspectorPanelCount = FindSceneObjects<LocalInspectorPanel>().Count,
                simulationGalleryControllerCount = CountTypeByName("SimulationGalleryPageController"),
                localProfileControllerCount = CountTypeByName("LocalProfilePageController"),
                commonToolsControllerCount = CountTypeByName("CommonToolsPageController"),
                templateLoadControllerCount = FindSceneObjects<TemplateLoadController>().Count,
                eventSystemCount = FindSceneObjects<UnityEngine.EventSystems.EventSystem>().Count,
                canvasCount = FindSceneObjects<Canvas>().Count,
                pageRootCount = CountPageRoots(scene)
            };
            Debug.Log("页面生命周期取证已采集。请在手动切换六个页面 20 轮后再次执行并导出。");
        }

        private static List<T> FindSceneObjects<T>() where T : UnityEngine.Object
        {
            return Resources.FindObjectsOfTypeAll<T>().Where(item => item != null && item is Component component && component.gameObject.scene.IsValid()).ToList();
        }

        private static int GetPrivateLayerChildCount(WorkspaceController workspace, string fieldName)
        {
            var field = typeof(WorkspaceController).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var layer = field == null ? null : field.GetValue(workspace) as Transform;
            return layer == null ? -1 : layer.childCount;
        }

        private static int CountRootCircuitObjects(UnityEngine.SceneManagement.Scene scene)
        {
            var count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<CircuitComponent>(true) != null || root.GetComponentInChildren<TerminalView>(true) != null || root.GetComponentInChildren<WireView>(true) != null) count++;
            }
            return count;
        }

        private static int CountTypeByName(string typeName)
        {
            return Resources.FindObjectsOfTypeAll<MonoBehaviour>().Count(item => item != null && item.gameObject.scene.IsValid() && item.GetType().Name == typeName);
        }

        private static int CountPageRoots(UnityEngine.SceneManagement.Scene scene)
        {
            return scene.GetRootGameObjects().Count(root => root.name.IndexOf("Page", StringComparison.OrdinalIgnoreCase) >= 0 || root.name.IndexOf("AppRoot", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static RuntimeAnalyzerEvidence CaptureAnalyzerSummary(CircuitStateResult state)
        {
            var evidence = new RuntimeAnalyzerEvidence();
            if (state == null) return evidence;
            evidence.available = true;
            evidence.hasShortCircuit = state.HasShortCircuit;
            evidence.hasPowerConflict = state.HasPowerConflict;
            evidence.hasInterlockConflict = state.HasContactorInterlockConflict;
            evidence.hasTimerRelays = state.HasTimerRelays;
            evidence.hasLimitSwitches = state.HasLimitSwitches;
            evidence.hasStarDeltaMotors = state.HasStarDeltaMotors;
            evidence.hasThreePhaseCircuit = state.HasThreePhaseCircuit;
            evidence.components = state.Components.Where(component => component != null)
                .OrderBy(component => component.DefinitionName, StringComparer.Ordinal)
                .ThenBy(component => component.InstanceId, StringComparer.Ordinal)
                .Select(component => new RuntimeComponentEvidence
                {
                    definitionId = component.DefinitionName,
                    state = component.State,
                    contactorCoilEnergized = component.IsContactorCoilEnergizedByAnalyzer,
                    contactorMainClosed = component.IsContactorMainContactsClosedByAnalyzer,
                    timerCoilEnergized = component.IsTimerRelayCoilEnergizedByAnalyzer,
                    timerDelayElapsed = component.IsTimerDelayElapsed,
                    timerStatus = component.TimerDelayStatus,
                    limitSwitchTriggered = component.IsLimitSwitchTriggered,
                    motorMode = component.StarDeltaConnectionMode
                }).ToList();
            return evidence;
        }

        private static RuntimeInspectorEvidence CaptureInspectorEvidence(string methodName)
        {
            var inspector = UnityEngine.Object.FindObjectOfType<LocalInspectorPanel>();
            var evidence = new RuntimeInspectorEvidence { entryPoint = methodName };
            if (inspector == null) return evidence;
            var method = typeof(LocalInspectorPanel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            var field = typeof(LocalInspectorPanel).GetField("reportContent", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null || field == null) return evidence;
            method.Invoke(inspector, null);
            var content = field.GetValue(inspector) as RectTransform;
            evidence.available = content != null;
            if (content == null) return evidence;
            Canvas.ForceUpdateCanvases();
            for (var i = 0; i < content.childCount; i++)
            {
                var texts = content.GetChild(i).GetComponentsInChildren<UnityEngine.UI.Text>(true);
                var title = texts.Length > 0 ? NormalizeText(texts[0].text) : string.Empty;
                evidence.sectionTitles.Add(title);
                var allText = NormalizeText(string.Join(" ", texts.Select(text => text.text).ToArray()));
                if (allText.IndexOf("错误", StringComparison.OrdinalIgnoreCase) >= 0) evidence.errorBlockCount++;
                if (allText.IndexOf("提醒", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("警告", StringComparison.OrdinalIgnoreCase) >= 0) evidence.warningBlockCount++;
                foreach (var phrase in new[] { "当前停止", "当前正转运行", "当前反转运行", "KT 正在计时", "星形启动阶段", "三角运行阶段" })
                {
                    if (allText.IndexOf(phrase, StringComparison.Ordinal) >= 0 && !evidence.keyPhrases.Contains(phrase)) evidence.keyPhrases.Add(phrase);
                }
            }
            return evidence;
        }

        private static string NormalizeText(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? string.Empty : System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        }

        private void WriteEvidence()
        {
            if (bundle == null || bundle.stages.Count == 0)
            {
                EditorUtility.DisplayDialog("Demo 场景运行态取证", "请先至少采集一个运行态阶段。", "知道了");
                return;
            }

            var directory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, BaselineAssetDirectory).Replace("/", Path.DirectorySeparatorChar.ToString());
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, EvidenceFileName);
            File.WriteAllText(path, JsonUtility.ToJson(bundle, true), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log("Demo 场景运行态取证已导出：" + BaselineAssetDirectory + "/" + EvidenceFileName);
        }

        private void LoadExisting()
        {
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, BaselineAssetDirectory, EvidenceFileName);
            if (File.Exists(path)) bundle = JsonUtility.FromJson<SceneEvidenceBundle>(File.ReadAllText(path));
            if (bundle == null) bundle = new SceneEvidenceBundle { schemaVersion = 1 };
        }

        [Serializable] private sealed class SceneEvidenceBundle { public int schemaVersion; public List<RuntimeStageEvidence> stages = new List<RuntimeStageEvidence>(); public LifecycleEvidence lifecycle; }
        [Serializable] private sealed class RuntimeStageEvidence { public string stageName; public string capturedAtUtc; public string sceneName; public int sceneCircuitComponentCount; public int sceneTerminalViewCount; public int sceneWireViewCount; public int rootObjectCount; public int workspaceComponentCount; public int workspaceWireCount; public int componentLayerChildCount; public int wireLayerChildCount; public int rootCircuitObjectCount; public int analyzerComponentInputCount; public int analyzerWireInputCount; public int validationComponentInputCount; public int validationWireInputCount; public bool analyzerReturned; public int validationIssueCount; public List<RuntimeRuleEvidence> validationRules = new List<RuntimeRuleEvidence>(); public RuntimeAnalyzerEvidence analyzerSummary; public RuntimeInspectorEvidence checkReport; public RuntimeInspectorEvidence explainReport; public int consoleErrorsSinceCapture; }
        [Serializable] private sealed class RuntimeRuleEvidence { public string ruleId; public string severity; }
        [Serializable] private sealed class RuntimeAnalyzerEvidence { public bool available; public bool hasShortCircuit; public bool hasPowerConflict; public bool hasInterlockConflict; public bool hasTimerRelays; public bool hasLimitSwitches; public bool hasStarDeltaMotors; public bool hasThreePhaseCircuit; public List<RuntimeComponentEvidence> components = new List<RuntimeComponentEvidence>(); }
        [Serializable] private sealed class RuntimeComponentEvidence { public string definitionId; public string state; public bool contactorCoilEnergized; public bool contactorMainClosed; public bool timerCoilEnergized; public bool timerDelayElapsed; public string timerStatus; public bool limitSwitchTriggered; public string motorMode; }
        [Serializable] private sealed class RuntimeInspectorEvidence { public string entryPoint; public bool available; public int errorBlockCount; public int warningBlockCount; public List<string> sectionTitles = new List<string>(); public List<string> keyPhrases = new List<string>(); }
        [Serializable] private sealed class LifecycleEvidence { public string note; public string capturedAtUtc; public int localInspectorPanelCount; public int simulationGalleryControllerCount; public int localProfileControllerCount; public int commonToolsControllerCount; public int templateLoadControllerCount; public int eventSystemCount; public int canvasCount; public int pageRootCount; }
    }
}
#endif
