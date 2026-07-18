using System;
using System.Diagnostics;
using System.IO;
using ElectricalSim.EditorTools.SpiceT2;
using ElectricalSim.Spice.T3;
using ElectricalSim.Spice.Workspace;
using ElectricalSim.UI;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricalSim.EditorTools.SpiceT3
{
    /// <summary>
    /// T3 独立原型和验证包的 Editor 工具。BuildPipeline 始终显式指定测试场景，
    /// 不读取或修改正式 Build Settings，也不接触 Demo.unity。
    /// </summary>
    public static class SpiceT3PrototypeTools
    {
        private const string PrototypeScenePath = "Assets/Tests/SpiceT3/SpiceT3WorkspacePrototype.unity";
        private const string ValidationScenePath = "Assets/Tests/SpiceT3/SpiceT3PlayerValidation.unity";
        private const string BuildRoot = "E:/Builds/ElectricalSimulation2D/SpiceT31-EmbeddedHost";

        [MenuItem("Tools/Spice/T3/Open Workspace Prototype")]
        public static void OpenPrototype()
        {
            EnsurePrototypeScene();
            EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Tools/Spice/T3/Run Editor Validation")]
        public static void RunEditorValidation()
        {
            SpiceT3WorkspaceValidation.RunPureChecks();
            // The Player harness owns asynchronous UI lifecycle coverage; blocking an Editor menu
            // on a Unity-context continuation would deadlock the same UI path it is meant to test.
            UnityEngine.Debug.Log("[SpiceT3] Editor workspace mapping and invalidation validation passed.");
        }

        public static void RunAllFromCommandLine()
        {
            SpiceT2ValidationTools.RunAllFromCommandLine();
            RunEditorValidation();
        }

        [MenuItem("Tools/Spice/T3/Build and Run Player Validation")]
        public static void BuildAndRunPlayerValidation()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the Spice T3 player validation.");
            EnsureValidationScene();
            var runDirectory = Path.Combine(BuildRoot, "run_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(runDirectory);
            var executable = Path.Combine(runDirectory, "SpiceT3-Prototype.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ValidationScenePath }, locationPathName = executable, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Spice T3 Player build failed: " + report.summary.result);
            VerifyStreamingAssets(executable);
            RunPlayer(executable, runDirectory);
        }

        private static void EnsurePrototypeScene()
        {
            CreateScene(PrototypeScenePath, false);
        }

        private static void EnsureValidationScene()
        {
            CreateScene(ValidationScenePath, true);
        }

        private static void CreateScene(string path, bool includeHarness)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("SpiceT3PrototypeCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = MainUiTheme.PageBackground;
            camera.orthographic = true;
            camera.cullingMask = 0;
            camera.depth = -100f;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            var eventSystemObject = new GameObject("SpiceT3PrototypeEventSystem");
            var eventSystem = eventSystemObject.AddComponent<EventSystem>();
            var inputModule = eventSystemObject.AddComponent<StandaloneInputModule>();

            var root = new GameObject(includeHarness ? "SpiceT3PlayerValidation" : "SpiceT3WorkspacePrototype", typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            var raycaster = root.AddComponent<GraphicRaycaster>();
            var bindings = root.AddComponent<SpiceWorkspaceViewBindings>();
            var controller = root.AddComponent<SpiceWorkspaceController>();
            var bootstrap = root.AddComponent<SpiceWorkspacePrototypeBootstrap>();
            bootstrap.ConfigurePrototypeInfrastructure(canvas, scaler, raycaster, eventSystem, inputModule, camera, bindings, controller);
            if (includeHarness) root.AddComponent<SpiceT3PlayerValidationHarness>();
            EditorUtility.SetDirty(bootstrap);
            EditorUtility.SetDirty(bindings);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(canvas);
            EditorUtility.SetDirty(scaler);
            EditorUtility.SetDirty(raycaster);
            EditorUtility.SetDirty(eventSystem);
            EditorUtility.SetDirty(inputModule);
            EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Unable to save Spice T3 scene: " + path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void VerifyStreamingAssets(string executable)
        {
            var root = Path.Combine(Path.GetDirectoryName(executable) ?? string.Empty, "SpiceT3-Prototype_Data", "StreamingAssets", "ThirdParty", "ngspice");
            foreach (var required in new[] { Path.Combine(root, "win-x64", "ngspice_con.exe"), Path.Combine(root, "win-x64", "libomp140.x86_64.dll"), Path.Combine(root, "COPYING") })
            {
                if (!File.Exists(required)) throw new FileNotFoundException("Required ngspice StreamingAssets file is missing from the Player build.", required);
            }
        }

        private static void RunPlayer(string executable, string runDirectory)
        {
            var resultPath = Path.Combine(runDirectory, "SpiceT3PlayerResult.json");
            var logPath = Path.Combine(runDirectory, "Player.log");
            var info = new ProcessStartInfo { FileName = executable, Arguments = "-batchmode -logFile \"" + logPath + "\" --spice-t3-result=\"" + resultPath + "\"", WorkingDirectory = runDirectory, UseShellExecute = false, CreateNoWindow = true };
            using (var player = Process.Start(info))
            {
                if (player == null) throw new InvalidOperationException("Unable to start the Spice T3 Player validation executable.");
                if (!player.WaitForExit(90000)) throw new TimeoutException("Spice T3 Player validation did not exit within 90 seconds.");
                if (player.ExitCode != 0) throw new InvalidOperationException("Spice T3 Player validation exited with code " + player.ExitCode + ". Log: " + logPath);
            }
            if (!File.Exists(resultPath)) throw new FileNotFoundException("The Spice T3 Player did not write its result file.", resultPath);
            var result = JsonUtility.FromJson<SpiceT3PlayerValidationReport>(File.ReadAllText(resultPath));
            if (result == null || !result.success || Math.Abs(result.firstCurrent - 0.01d) > 1e-8d || Math.Abs(result.updatedCurrent - 0.005d) > 1e-8d) throw new InvalidOperationException("Spice T3 Player result did not match the single-resistor and parameter-update expectations.");
            UnityEngine.Debug.Log("[SpiceT3] Player validation passed. Build: " + executable + " Result: " + resultPath + " Log: " + logPath);
        }
    }
}
