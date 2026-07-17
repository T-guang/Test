using System;
using System.Diagnostics;
using System.IO;
using ElectricalSim.EditorTools.SpiceT1;
using ElectricalSim.Spice.T2;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElectricalSim.EditorTools.SpiceT2
{
    public static class SpiceT2ValidationTools
    {
        private const string ValidationScenePath = "Assets/Tests/SpiceT2/SpiceT2PlayerValidation.unity";
        private const string BuildRoot = "E:/Builds/ElectricalSimulation2D/SpiceT2-Validation";

        [MenuItem("Tools/Spice/T2/Run Editor Validation")]
        public static void RunEditorValidation()
        {
            SpiceT2Validation.RunPureCoreChecks();
            var results = SpiceT2Validation.RunIntegrationChecksAsync().GetAwaiter().GetResult();
            UnityEngine.Debug.Log("[SpiceT2] Editor validation passed. Fixtures=" + results.Count);
        }

        public static void RunAllFromCommandLine()
        {
            SpiceT1ValidationTools.RunEditorValidationFromCommandLine();
            RunEditorValidation();
        }

        [MenuItem("Tools/Spice/T2/Build and Run Player Validation")]
        public static void BuildAndRunPlayerValidation()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before building the Spice T2 player validation.");
            EnsureValidationScene();
            var runDirectory = Path.Combine(BuildRoot, "run_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(runDirectory);
            var executablePath = Path.Combine(runDirectory, "SpiceT2-Validation.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ValidationScenePath }, locationPathName = executablePath, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Spice T2 player build failed: " + report.summary.result);
            VerifyStreamingAssets(executablePath);
            RunBuiltPlayer(executablePath, runDirectory);
        }

        private static void EnsureValidationScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("SpiceT2PlayerValidationHarness").AddComponent<SpiceT2PlayerValidationHarness>();
            if (!EditorSceneManager.SaveScene(scene, ValidationScenePath)) throw new InvalidOperationException("Unable to save the Spice T2 validation scene.");
            AssetDatabase.Refresh();
        }

        private static void VerifyStreamingAssets(string executablePath)
        {
            var root = Path.Combine(Path.GetDirectoryName(executablePath) ?? string.Empty, "SpiceT2-Validation_Data", "StreamingAssets", "ThirdParty", "ngspice");
            foreach (var required in new[] { Path.Combine(root, "win-x64", "ngspice_con.exe"), Path.Combine(root, "win-x64", "libomp140.x86_64.dll"), Path.Combine(root, "COPYING") })
                if (!File.Exists(required)) throw new FileNotFoundException("Required ngspice StreamingAssets file is missing from the player build.", required);
        }

        private static void RunBuiltPlayer(string executablePath, string runDirectory)
        {
            var resultPath = Path.Combine(runDirectory, "SpiceT2PlayerResult.json");
            var logPath = Path.Combine(runDirectory, "Player.log");
            var info = new ProcessStartInfo { FileName = executablePath, Arguments = "-batchmode -logFile \"" + logPath + "\" --spice-t2-result=\"" + resultPath + "\"", WorkingDirectory = runDirectory, UseShellExecute = false, CreateNoWindow = true };
            using (var player = Process.Start(info))
            {
                if (player == null) throw new InvalidOperationException("Unable to start the Spice T2 player validation executable.");
                if (!player.WaitForExit(60000)) { try { player.Kill(); } catch { } throw new TimeoutException("Spice T2 player validation did not exit within 60 seconds."); }
                if (player.ExitCode != 0) throw new InvalidOperationException("Spice T2 player validation exited with code " + player.ExitCode + ". Log: " + logPath);
            }
            if (!File.Exists(resultPath)) throw new FileNotFoundException("The Spice T2 player did not write its result file.", resultPath);
            var result = JsonUtility.FromJson<SpiceT2PlayerValidationReport>(File.ReadAllText(resultPath));
            if (result == null || !result.success) throw new InvalidOperationException("Spice T2 player reported failure. Result: " + resultPath);
            if (Math.Abs(result.middleVoltage - 5d) > SpiceT2Validation.VoltageTolerance) throw new InvalidOperationException("Spice T2 player middle voltage mismatch.");
            if (File.Exists(logPath))
            {
                var log = File.ReadAllText(logPath);
                if (log.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0 || log.IndexOf("Fatal Error", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("The player log contains a blocking error. Log: " + logPath);
            }
            UnityEngine.Debug.Log("[SpiceT2] Player validation passed. Build: " + executablePath + " Result: " + resultPath + " Log: " + logPath);
        }
    }
}
