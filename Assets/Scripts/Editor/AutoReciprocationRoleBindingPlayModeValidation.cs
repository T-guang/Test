using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// 自动往返角色绑定的真实 Play Mode 验证入口。
    /// 它不直接写入运动状态，而是在播放模式中启动 WorkspaceController 的正常仿真刷新，等待生产 Update
    /// 驱动 SimulationEngine 的时间步进，并记录模板与随机 InstanceId 等价图的完整往返阶段。
    /// </summary>
    [InitializeOnLoad]
    public static class AutoReciprocationRoleBindingPlayModeValidation
    {
        private const string TemplateId = "motor_auto_reciprocating_control";
        private const int RequiredSimulationSteps = 18;
        private const string ActiveSessionKey = "ElectricalSim.E5.PlayMode.Active";
        private const string FinishSessionKey = "ElectricalSim.E5.PlayMode.Finish";
        private const string ExitCodeSessionKey = "ElectricalSim.E5.PlayMode.ExitCode";
        private const string OutputSessionKey = "ElectricalSim.E5.PlayMode.Output";
        private static readonly List<string> TemplateRows = new List<string> { "elapsed,position,direction,forwardVisual,reverseVisual,leftVisual,rightVisual" };
        private static readonly List<string> RandomRows = new List<string> { "elapsed,position,direction,forwardVisual,reverseVisual,leftVisual,rightVisual" };

        private static string outputDirectory;
        private static WorkspaceController workspace;
        private static SaveLoadService saveLoad;
        private static CircuitTemplateDto original;
        private static CircuitTemplateDto randomized;
        private static AutoReciprocationRoleResolution roles;
        private static float scenarioStartedAt;
        private static float lastSimulationStepAt;
        private static int simulationStepCount;
        private static int scenarioIndex;
        private static bool forwardSeen;
        private static bool rightSeen;
        private static bool reverseSeen;
        private static bool leftSeen;
        private static string failure;

        static AutoReciprocationRoleBindingPlayModeValidation()
        {
            // 进入/退出 Play Mode 会重载 Editor 程序集；用 SessionState 保留一次性验证意图，
            // 让域重载后的静态构造器重新订阅事件，而不是把“已进入播放模式”误当成测试成功。
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Tests/Run Auto Reciprocation Role Binding Play Mode Validation")]
        public static void Run()
        {
            ResetState();
            outputDirectory = Environment.GetEnvironmentVariable("E5_PLAYMODE_OUTPUT");
            if (string.IsNullOrWhiteSpace(outputDirectory)) outputDirectory = Path.Combine(Path.GetTempPath(), "E5_AutoReciprocationPlayMode");
            Directory.CreateDirectory(outputDirectory);

            SessionState.SetBool(ActiveSessionKey, true);
            SessionState.SetBool(FinishSessionKey, false);
            SessionState.SetInt(ExitCodeSessionKey, 1);
            SessionState.SetString(OutputSessionKey, outputDirectory);
            EditorSceneManager.OpenScene("Assets/Scenes/Demo.unity", OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                if (!SessionState.GetBool(ActiveSessionKey, false)) return;
                ResetState();
                outputDirectory = SessionState.GetString(OutputSessionKey, string.Empty);
                if (string.IsNullOrWhiteSpace(outputDirectory))
                {
                    failure = "Play Mode 验证缺少仓库外输出目录。";
                    Finish();
                    return;
                }
                EditorApplication.update += Tick;
                return;
            }

            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(FinishSessionKey, false)) return;
            EditorApplication.update -= Tick;
            var exitCode = SessionState.GetInt(ExitCodeSessionKey, 1);
            SessionState.EraseBool(ActiveSessionKey);
            SessionState.EraseBool(FinishSessionKey);
            SessionState.EraseInt(ExitCodeSessionKey);
            SessionState.EraseString(OutputSessionKey);
            EditorApplication.Exit(exitCode);
        }

        private static void Tick()
        {
            try
            {
                if (workspace == null)
                {
                    InitializePlayModeScenario();
                    return;
                }

                if (simulationStepCount < RequiredSimulationSteps && Time.realtimeSinceStartup - lastSimulationStepAt >= 0.10f)
                {
                    InvokeWorkspaceSimulationTick(0.5f);
                    lastSimulationStepAt = Time.realtimeSinceStartup;
                    simulationStepCount++;
                    CaptureTick();
                }
                if (simulationStepCount < RequiredSimulationSteps) return;

                ValidateCompletedScenario();
                if (!string.IsNullOrEmpty(failure))
                {
                    Finish();
                    return;
                }

                if (scenarioIndex == 0)
                {
                    scenarioIndex = 1;
                    BeginScenario(randomized);
                    return;
                }

                var templateStages = StageSequence(TemplateRows);
                var randomStages = StageSequence(RandomRows);
                if (!templateStages.SequenceEqual(randomStages, StringComparer.Ordinal))
                {
                    failure = "模板与随机 ID 图的 Play Mode 运行阶段顺序不一致：" + string.Join("/", templateStages) + " != " + string.Join("/", randomStages);
                }
                else
                {
                    Debug.Log("[Electrical][E5] 自动往返 Play Mode 时间序列：通过");
                }

                Finish();
            }
            catch (Exception exception)
            {
                failure = "Play Mode 验证出现未处理异常：" + exception;
                Debug.LogError(failure);
                Finish();
            }
        }

        private static void InitializePlayModeScenario()
        {
            workspace = UnityEngine.Object.FindObjectOfType<WorkspaceController>(true);
            saveLoad = UnityEngine.Object.FindObjectOfType<SaveLoadService>(true);
            if (workspace == null || saveLoad == null)
            {
                failure = "Play Mode 场景缺少 WorkspaceController 或 SaveLoadService。";
                Finish();
                return;
            }

            var catalogAsset = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            var catalog = catalogAsset != null ? JsonUtility.FromJson<CircuitTemplateCatalogDto>(catalogAsset.text) : null;
            var item = catalog != null && catalog.templates != null ? catalog.templates.FirstOrDefault(candidate => candidate.templateId == TemplateId) : null;
            string error = null;
            if (item == null || !CircuitTemplateLoader.TryLoad(item.resourcePath, out original, out error))
            {
                failure = "Play Mode 无法读取自动往返模板：" + error;
                Finish();
                return;
            }

            randomized = CreateRandomizedEquivalentVariant(original);
            BeginScenario(original);
        }

        private static void BeginScenario(CircuitTemplateDto template)
        {
            workspace.StopSimulation();
            workspace.ClearDrawing(false);
            TemplateEditSession.Clear();
            SimulationEngine.ResetRuntimeState();
            if (!CircuitTemplateSpawnService.Spawn(template, workspace, saveLoad.Catalog, out var message))
            {
                failure = "Play Mode 正式模板生成失败：" + message;
                Finish();
                return;
            }

            roles = AutoReciprocationRoleResolver.Resolve(workspace.Components, workspace.WireManager.Wires);
            if (roles == null || !roles.IsResolved)
            {
                failure = "Play Mode 角色解析失败：" + (roles == null ? "null" : roles.Status + "；" + roles.Reason);
                Finish();
                return;
            }

            if (scenarioIndex == 1 && string.Equals(roles.Motor.InstanceId, "motor_1", StringComparison.Ordinal))
            {
                failure = "随机 ID Play Mode 变体仍保留 motor_1。";
                Finish();
                return;
            }

            var start = workspace.Components.FirstOrDefault(component => component != null && component.Definition != null && component.Definition.name.IndexOf("Button_Start", StringComparison.OrdinalIgnoreCase) >= 0);
            if (start == null)
            {
                failure = "Play Mode 自动往返模板缺少启动按钮。";
                Finish();
                return;
            }

            forwardSeen = false;
            rightSeen = false;
            reverseSeen = false;
            leftSeen = false;
            start.SetClosed(true);
            // 不开启 WorkspaceController 的周期刷新，避免无图形 batch 的极小 Time.deltaTime 在采样之间
            // 额外推进限位状态；仍通过其正式 EvaluateSimulation 入口执行每一步生产仿真。
            InvokeWorkspaceSimulationTick(0f);
            scenarioStartedAt = Time.realtimeSinceStartup;
            lastSimulationStepAt = scenarioStartedAt - 0.10f;
            simulationStepCount = 0;
        }

        private static void CaptureTick()
        {
            if (!RuntimeStateManager.Shared.TryGetMotionState(roles.Motor.InstanceId, out var motion) || motion == null) return;
            var direction = motion.Direction;
            var rightVisual = ResolveLimitVisualState(roles.RightLimitSwitch);
            var leftVisual = ResolveLimitVisualState(roles.LeftLimitSwitch);
            forwardSeen |= direction == MotionDirection.Forward;
            rightSeen |= motion.RightLimitTriggered;
            reverseSeen |= direction == MotionDirection.Reverse;
            leftSeen |= motion.LeftLimitTriggered;
            var row = string.Join(",",
                (Time.realtimeSinceStartup - scenarioStartedAt).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                motion.Position.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                direction,
                roles.ForwardContactor.IsEnergized,
                roles.ReverseContactor.IsEnergized,
                leftVisual,
                rightVisual);
            (scenarioIndex == 0 ? TemplateRows : RandomRows).Add(row);
        }

        private static void ValidateCompletedScenario()
        {
            if (!forwardSeen || !rightSeen || !reverseSeen || !leftSeen)
            {
                failure = (scenarioIndex == 0 ? "模板" : "随机 ID") + " Play Mode 未完成正向、右限位、反向、左限位全阶段。";
            }
        }

        private static bool ResolveLimitVisualState(CircuitComponent limitSwitch)
        {
            var method = typeof(CircuitComponent).GetMethod("ResolveLimitSwitchTriggeredVisualState", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("CircuitComponent.ResolveLimitSwitchTriggeredVisualState");
            return (bool)method.Invoke(limitSwitch, null);
        }

        private static void InvokeWorkspaceSimulationTick(float deltaTime)
        {
            // 真实 Play Mode 中仍复用 WorkspaceController 的正式仿真入口；测试只提供时间步长，
            // 不写入接触器、电机或限位状态，所有状态变化继续由 SimulationEngine 推导。
            var method = typeof(WorkspaceController).GetMethod("EvaluateSimulation", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null) throw new MissingMethodException("WorkspaceController.EvaluateSimulation");
            method.Invoke(workspace, new object[] { deltaTime });
        }

        private static IReadOnlyList<string> StageSequence(IEnumerable<string> rows)
        {
            return rows.Skip(1)
                .Select(row => row.Split(',')[2])
                .Where(direction => direction == MotionDirection.Forward.ToString() || direction == MotionDirection.Reverse.ToString())
                .Distinct()
                .ToList();
        }

        private static CircuitTemplateDto CreateRandomizedEquivalentVariant(CircuitTemplateDto source)
        {
            var variant = JsonUtility.FromJson<CircuitTemplateDto>(JsonUtility.ToJson(source));
            var map = new Dictionary<string, string>();
            foreach (var component in variant.components)
            {
                var originalId = component.instanceId;
                var randomizedId = "e5-play-" + Guid.NewGuid().ToString("N");
                map[originalId] = randomizedId;
                component.instanceId = randomizedId;
            }

            foreach (var wire in variant.wires)
            {
                wire.startComponentId = map[wire.startComponentId];
                wire.endComponentId = map[wire.endComponentId];
            }

            variant.components.Reverse();
            variant.wires.Reverse();
            for (var i = 0; i < variant.wires.Count; i += 2)
            {
                var wire = variant.wires[i];
                var component = wire.startComponentId;
                var terminal = wire.startTerminalId;
                wire.startComponentId = wire.endComponentId;
                wire.startTerminalId = wire.endTerminalId;
                wire.endComponentId = component;
                wire.endTerminalId = terminal;
            }
            return variant;
        }

        private static void Finish()
        {
            if (!string.IsNullOrEmpty(failure)) Debug.LogError(failure);
            WriteEvidence();
            SessionState.SetInt(ExitCodeSessionKey, string.IsNullOrEmpty(failure) ? 0 : 1);
            SessionState.SetBool(FinishSessionKey, true);
            SessionState.SetBool(ActiveSessionKey, false);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        private static void WriteEvidence()
        {
            File.WriteAllLines(Path.Combine(outputDirectory, "AUTO_RECIPROCATION_TEMPLATE_PLAYMODE_TIMELINE.csv"), TemplateRows, System.Text.Encoding.UTF8);
            File.WriteAllLines(Path.Combine(outputDirectory, "AUTO_RECIPROCATION_RANDOM_ID_PLAYMODE_TIMELINE.csv"), RandomRows, System.Text.Encoding.UTF8);
            File.WriteAllText(Path.Combine(outputDirectory, "PLAYMODE_RESULT.txt"), string.IsNullOrEmpty(failure) ? "PASS" : failure, System.Text.Encoding.UTF8);
        }

        private static void ResetState()
        {
            TemplateRows.Clear();
            TemplateRows.Add("elapsed,position,direction,forwardVisual,reverseVisual,leftVisual,rightVisual");
            RandomRows.Clear();
            RandomRows.Add("elapsed,position,direction,forwardVisual,reverseVisual,leftVisual,rightVisual");
            workspace = null;
            saveLoad = null;
            original = null;
            randomized = null;
            roles = null;
            scenarioIndex = 0;
            simulationStepCount = 0;
            failure = null;
        }
    }
}
