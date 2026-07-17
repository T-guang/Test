using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ElectricalSim.Spice.T2
{
    /// <summary>
    /// Player 测试报告，只用于独立 T2 验证包；不是正式保存格式或产品结果模型。
    /// </summary>
    [Serializable]
    public sealed class SpiceT2PlayerValidationReport
    {
        public bool success;
        public double middleVoltage;
        public double r1Current;
        public double r2Current;
        public double sourceCurrent;
        public long durationMilliseconds;
        public string netlist;
        public string failure;
        public string standardOutput;
        public string standardError;
    }

    public sealed class SpiceT2PlayerValidationHarness : MonoBehaviour
    {
        private async void Start()
        {
            var report = await RunValidationAsync();
            try
            {
                var path = GetResultPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                Debug.Log("[SpiceT2] Player validation report: " + path + "\n" + JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                // 结果文件是 Player 自动化的外部交接点；写入失败必须令进程以失败码退出。
                report.success = false;
                report.failure = exception.ToString();
                Debug.LogError("[SpiceT2] " + report.failure);
            }
            Application.Quit(report.success ? 0 : 1);
        }

        private static async Task<SpiceT2PlayerValidationReport> RunValidationAsync()
        {
            try
            {
                var result = await SpiceT2Validation.VerifyDividerForPlayerAsync();
                var middle = result.NodeVoltages.Values.First(value => Math.Abs(value - 10d) > SpiceT2Validation.VoltageTolerance);
                return new SpiceT2PlayerValidationReport
                {
                    success = true,
                    middleVoltage = middle,
                    r1Current = result.ComponentResults["r1"].Current,
                    r2Current = result.ComponentResults["r2"].Current,
                    sourceCurrent = result.ComponentResults["source"].Current,
                    durationMilliseconds = (long)result.Duration.TotalMilliseconds,
                    netlist = result.Netlist,
                    standardOutput = result.RawNgspiceResult.StandardOutput,
                    standardError = result.RawNgspiceResult.StandardError
                };
            }
            catch (Exception exception)
            {
                // Harness 保留完整异常文本给结果文件，避免 Player 验证静默成功。
                return new SpiceT2PlayerValidationReport { success = false, failure = exception.ToString() };
            }
        }

        private static string GetResultPath()
        {
            foreach (var argument in Environment.GetCommandLineArgs())
            {
                const string prefix = "--spice-t2-result=";
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(argument.Substring(prefix.Length).Trim('"'));
            }
            return Path.Combine(Application.persistentDataPath, "SpiceT2", "SpiceT2PlayerValidationResult.json");
        }
    }
}
