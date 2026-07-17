using System;
using System.Diagnostics;
using System.IO;
using ElectricalSim.Spice.Infrastructure;
using ElectricalSim.Spice.T1;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ElectricalSim.EditorTools.SpiceT1
{
    public static class SpiceT1ValidationTools
    {
        private const string ValidationScenePath = "Assets/Tests/SpiceT1/SpiceT1PlayerValidation.unity";
        private const string BuildRoot = "E:/Builds/ElectricalSimulation2D/SpiceT1-Validation";

        [MenuItem("Tools/Spice/T1/Run Editor Validation")]
        private static async void RunEditorValidation()
        {
            var result = await new NgspiceProcessRunner().RunFixedDcAsync();
            LogEditorValidation(result);
        }

        public static void RunEditorValidationFromCommandLine()
        {
            var result = new NgspiceProcessRunner().RunFixedDcAsync().GetAwaiter().GetResult();
            if (!SpiceT1FixedNetlist.TryValidate(result, out var failure))
            {
                throw new InvalidOperationException(FormatResult(result, failure));
            }

            UnityEngine.Debug.Log(FormatResult(result, null));
        }

        [MenuItem("Tools/Spice/T1/Build and Run Player Validation")]
        public static void BuildAndRunPlayerValidation()
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("Exit Play Mode before building the Spice T1 player validation.");
            }

            EnsureValidationScene();
            var runDirectory = Path.Combine(BuildRoot, "run_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(runDirectory);
            var executablePath = Path.Combine(runDirectory, "SpiceT1-Validation.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ValidationScenePath },
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException("Spice T1 player build failed: " + report.summary.result);
            }

            VerifyStreamingAssets(executablePath);
            RunBuiltPlayer(executablePath, runDirectory);
        }

        private static void LogEditorValidation(NgspiceRunResult result)
        {
            if (SpiceT1FixedNetlist.TryValidate(result, out var failure))
            {
                UnityEngine.Debug.Log(FormatResult(result, null));
                return;
            }

            UnityEngine.Debug.LogError(FormatResult(result, failure));
        }

        private static void EnsureValidationScene()
        {
            var directory = Path.GetDirectoryName(ValidationScenePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var harness = new GameObject("SpiceT1PlayerValidationHarness");
            harness.AddComponent<SpiceT1PlayerValidationHarness>();
            if (!EditorSceneManager.SaveScene(scene, ValidationScenePath))
            {
                throw new InvalidOperationException("Unable to save the Spice T1 validation scene.");
            }

            AssetDatabase.Refresh();
        }

        private static void VerifyStreamingAssets(string executablePath)
        {
            var streamingRoot = Path.Combine(
                Path.GetDirectoryName(executablePath) ?? string.Empty,
                "SpiceT1-Validation_Data",
                "StreamingAssets",
                "ThirdParty",
                "ngspice");
            var requiredFiles = new[]
            {
                Path.Combine(streamingRoot, "win-x64", "ngspice_con.exe"),
                Path.Combine(streamingRoot, "win-x64", "libomp140.x86_64.dll"),
                Path.Combine(streamingRoot, "COPYING")
            };

            for (var i = 0; i < requiredFiles.Length; i++)
            {
                if (!File.Exists(requiredFiles[i]))
                {
                    throw new FileNotFoundException("Required ngspice StreamingAssets file is missing from the player build.", requiredFiles[i]);
                }
            }
        }

        private static void RunBuiltPlayer(string executablePath, string runDirectory)
        {
            var resultPath = Path.Combine(runDirectory, "SpiceT1PlayerResult.json");
            var logPath = Path.Combine(runDirectory, "Player.log");
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = "-batchmode -logFile \"" + logPath + "\" --spice-t1-result=\"" + resultPath + "\"",
                WorkingDirectory = runDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var player = Process.Start(startInfo))
            {
                if (player == null)
                {
                    throw new InvalidOperationException("Unable to start the Spice T1 player validation executable.");
                }

                if (!player.WaitForExit(60000))
                {
                    try
                    {
                        player.Kill();
                    }
                    catch
                    {
                        // The timeout failure remains the primary diagnostic.
                    }

                    throw new TimeoutException("Spice T1 player validation did not exit within 60 seconds.");
                }

                if (player.ExitCode != 0)
                {
                    throw new InvalidOperationException("Spice T1 player validation exited with code " + player.ExitCode + ". Log: " + logPath);
                }
            }

            if (!File.Exists(resultPath))
            {
                throw new FileNotFoundException("The Spice T1 player did not write its result file.", resultPath);
            }

            var playerResult = JsonUtility.FromJson<SpiceT1PlayerValidationReport>(File.ReadAllText(resultPath));
            if (playerResult == null || !playerResult.success)
            {
                throw new InvalidOperationException("Spice T1 player reported failure. Result: " + resultPath);
            }

            if (File.Exists(logPath))
            {
                var log = File.ReadAllText(logPath);
                if (log.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    log.IndexOf("Fatal Error", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new InvalidOperationException("The player log contains a blocking error. Log: " + logPath);
                }
            }

            UnityEngine.Debug.Log("[SpiceT1] Player validation passed. Build: " + executablePath + " Result: " + resultPath + " Log: " + logPath);
        }

        private static string FormatResult(NgspiceRunResult result, string failure)
        {
            result.NodeVoltages.TryGetValue("input", out var inputVoltage);
            result.BranchCurrents.TryGetValue("V1", out var v1Current);
            return "[SpiceT1] success=" + result.Success +
                   " exitCode=" + result.ExitCode +
                   " timedOut=" + result.TimedOut +
                   " v(input)=" + inputVoltage.ToString("R") +
                   " i(v1)=" + v1Current.ToString("R") +
                   " durationMs=" + result.Duration.TotalMilliseconds.ToString("F0") +
                   " failure=" + (failure ?? string.Empty) +
                   "\nstdout:\n" + result.StandardOutput +
                   "\nstderr:\n" + result.StandardError;
        }
    }
}
