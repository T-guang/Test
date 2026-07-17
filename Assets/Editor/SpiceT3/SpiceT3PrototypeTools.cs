using System;
using System.Diagnostics;
using System.IO;
using ElectricalSim.EditorTools.SpiceT2;
using ElectricalSim.Spice.T3;
using ElectricalSim.Spice.Workspace;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private const string BuildRoot = "E:/Builds/ElectricalSimulation2D/SpiceT3-Prototype";

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
            var root = new GameObject(includeHarness ? "SpiceT3PlayerValidation" : "SpiceT3WorkspacePrototype", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            root.AddComponent<SpiceWorkspaceController>();
            if (includeHarness) root.AddComponent<SpiceT3PlayerValidationHarness>();
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Unable to save Spice T3 scene: " + path);
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
