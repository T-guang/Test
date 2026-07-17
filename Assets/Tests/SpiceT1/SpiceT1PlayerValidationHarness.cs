using System;
using System.IO;
using System.Threading.Tasks;
using ElectricalSim.Spice.Infrastructure;
using UnityEngine;

namespace ElectricalSim.Spice.T1
{
    [Serializable]
    public sealed class SpiceT1PlayerValidationReport
    {
        public bool success;
        public int exitCode;
        public bool timedOut;
        public double inputVoltage;
        public double v1Current;
        public double voltageError;
        public double currentError;
        public long durationMilliseconds;
        public string failureCode;
        public string failureMessage;
        public string executablePath;
        public string workingDirectory;
        public string standardOutput;
        public string standardError;
    }

    public sealed class SpiceT1PlayerValidationHarness : MonoBehaviour
    {
        private async void Start()
        {
            var report = await RunValidationAsync();
            var resultPath = GetResultPath();
            try
            {
                var directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(resultPath, JsonUtility.ToJson(report, true));
                Debug.Log("[SpiceT1] Player validation report: " + resultPath + "\n" + JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                report.success = false;
                report.failureCode = "ResultWriteFailed";
                report.failureMessage = exception.ToString();
                Debug.LogError("[SpiceT1] " + report.failureMessage);
            }

            Application.Quit(report.success ? 0 : 1);
        }

        private static async Task<SpiceT1PlayerValidationReport> RunValidationAsync()
        {
            var runner = new NgspiceProcessRunner();
            var result = await runner.RunFixedDcAsync();
            var passed = SpiceT1FixedNetlist.TryValidate(result, out var failure);
            result.NodeVoltages.TryGetValue("input", out var inputVoltage);
            result.BranchCurrents.TryGetValue("V1", out var v1Current);

            return new SpiceT1PlayerValidationReport
            {
                success = passed,
                exitCode = result.ExitCode,
                timedOut = result.TimedOut,
                inputVoltage = inputVoltage,
                v1Current = v1Current,
                voltageError = Math.Abs(inputVoltage - SpiceT1FixedNetlist.ExpectedInputVoltage),
                currentError = Math.Abs(v1Current - SpiceT1FixedNetlist.ExpectedV1Current),
                durationMilliseconds = (long)result.Duration.TotalMilliseconds,
                failureCode = result.FailureCode.ToString(),
                failureMessage = passed ? string.Empty : failure,
                executablePath = result.ExecutablePath,
                workingDirectory = result.WorkingDirectory,
                standardOutput = result.StandardOutput,
                standardError = result.StandardError
            };
        }

        private static string GetResultPath()
        {
            var args = Environment.GetCommandLineArgs();
            const string prefix = "--spice-t1-result=";
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suppliedPath = args[i].Substring(prefix.Length).Trim('"');
                    if (!string.IsNullOrWhiteSpace(suppliedPath))
                    {
                        return Path.GetFullPath(suppliedPath);
                    }
                }
            }

            return Path.Combine(Application.persistentDataPath, "SpiceT1", "SpiceT1PlayerValidationResult.json");
        }
    }
}
