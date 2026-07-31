using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace ElectricalSim.Spice.Infrastructure
{
    /// <summary>
    /// Windows 本地 ngspice 进程边界。构造时在 Unity 主线程捕获 StreamingAssets、临时目录和
    /// 应用本地数据目录，实际进程和 stdout/stderr 读取在后台执行；同一 Runner 串行化运行。
    /// </summary>
    public sealed class NgspiceProcessRunner
    {
        private const string BeginMarker = "__SPICE_T1_BEGIN__";
        private const string EndMarker = "__SPICE_T1_END__";
        private const int DefaultTimeoutMilliseconds = 8000;
        private static readonly Regex PrintedValue = new Regex(
            @"^\s*(?<name>[vi])\s*\(\s*(?<id>[^)]+?)\s*\)\s*=\s*(?<value>[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eEdD][+-]?\d+)?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private readonly SemaphoreSlim runGate = new SemaphoreSlim(1, 1);
        private readonly string executablePath;
        private readonly string temporaryCachePath;
        private readonly string persistentDataPath;

        public NgspiceProcessRunner()
        {
            executablePath = Path.GetFullPath(Path.Combine(
                Application.streamingAssetsPath,
                "ThirdParty",
                "ngspice",
                "win-x64",
                "ngspice_con.exe"));
            temporaryCachePath = Application.temporaryCachePath;
            persistentDataPath = Application.persistentDataPath;
        }

        public Task<NgspiceRunResult> RunFixedDcAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            return RunAsync(SpiceT1FixedNetlist.Content, TimeSpan.FromMilliseconds(DefaultTimeoutMilliseconds), cancellationToken);
        }

        public async Task<NgspiceRunResult> RunAsync(string netlist, TimeSpan timeout, CancellationToken cancellationToken = default(CancellationToken))
        {
            return await RunInternalAsync(netlist, timeout, "SpiceT1", "spice_t1_fixed_dc.cir", true, cancellationToken).ConfigureAwait(false);
        }

        public async Task<NgspiceRunResult> RunRawNetlistAsync(string netlist, TimeSpan timeout, string diagnosticScope, CancellationToken cancellationToken = default(CancellationToken))
        {
            var scope = string.IsNullOrWhiteSpace(diagnosticScope) ? "Spice" : diagnosticScope.Trim();
            return await RunInternalAsync(netlist, timeout, scope, "spice_run.cir", false, cancellationToken).ConfigureAwait(false);
        }

        private async Task<NgspiceRunResult> RunInternalAsync(
            string netlist,
            TimeSpan timeout,
            string diagnosticScope,
            string netlistFileName,
            bool parseT1Output,
            CancellationToken cancellationToken)
        {
            var workingDirectory = Path.Combine(
                temporaryCachePath,
                diagnosticScope,
                Guid.NewGuid().ToString("N"));
            var persistentDiagnosticDirectory = Path.Combine(
                persistentDataPath,
                diagnosticScope,
                "last_run");
            // 同一 Runner 不允许重叠启动两个外部求解器，避免诊断目录和进程生命周期交叉。
            await runGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(
                    () => Execute(netlist, timeout, executablePath, workingDirectory, persistentDiagnosticDirectory, netlistFileName, parseT1Output, cancellationToken),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                runGate.Release();
            }
        }

        private static NgspiceRunResult Execute(
            string netlist,
            TimeSpan timeout,
            string executablePath,
            string workingDirectory,
            string persistentDiagnosticDirectory,
            string netlistFileName,
            bool parseT1Output,
            CancellationToken cancellationToken)
        {
            var result = new NgspiceRunResult
            {
                ExecutablePath = executablePath,
                WorkingDirectory = workingDirectory,
                ExitCode = -1,
                FailureCode = NgspiceFailureCode.None,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                if (string.IsNullOrWhiteSpace(netlist))
                {
                    result.FailureCode = NgspiceFailureCode.ParseFailed;
                    result.FailureMessage = "The fixed T1 netlist is empty.";
                    return result;
                }

                if (!File.Exists(executablePath))
                {
                    result.FailureCode = NgspiceFailureCode.ExecutableMissing;
                    result.FailureMessage = "ngspice_con.exe was not found at the StreamingAssets runtime path.";
                    return result;
                }

                Directory.CreateDirectory(workingDirectory);
                var netlistPath = Path.Combine(workingDirectory, netlistFileName);
                File.WriteAllText(netlistPath, netlist, new UTF8Encoding(false));

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-b \"" + netlistPath + "\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                bool wasCancelled = false;
                using (var process = new Process { StartInfo = startInfo })
                {
                    try
                    {
                        if (!process.Start())
                        {
                            result.FailureCode = NgspiceFailureCode.StartFailed;
                            result.FailureMessage = "Process.Start returned false.";
                            return result;
                        }
                    }
                    catch (Exception exception)
                    {
                        result.FailureCode = NgspiceFailureCode.StartFailed;
                        result.FailureMessage = exception.Message;
                        return result;
                    }

                    var stdoutTask = process.StandardOutput.ReadToEndAsync();
                    var stderrTask = process.StandardError.ReadToEndAsync();
                    var timeoutMilliseconds = Math.Max(1, (int)Math.Min(int.MaxValue, timeout.TotalMilliseconds));

                    using (cancellationToken.Register(() => TryTerminateProcess(process, out _)))
                    {
                        if (!process.WaitForExit(timeoutMilliseconds))
                        {
                            TryTerminateProcess(process, out var killDiag);
                            result.TimedOut = true;
                            result.FailureCode = NgspiceFailureCode.TimedOut;
                            result.FailureMessage = "ngspice exceeded the configured timeout of " + timeoutMilliseconds + " ms.";
                            if (!string.IsNullOrEmpty(killDiag)) result.FailureMessage += " " + killDiag;
                        }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        wasCancelled = true;
                    }

                    if (!process.HasExited)
                    {
                        TryTerminateProcess(process, out _);
                    }

                    if (wasCancelled || result.TimedOut)
                    {
                        process.WaitForExit(1000);
                        try { process.StandardOutput.Close(); } catch { }
                        try { process.StandardError.Close(); } catch { }
                    }

                    try
                    {
                        if (Task.WaitAll(new[] { stdoutTask, stderrTask }, 1000))
                        {
                            result.StandardOutput = stdoutTask.Result ?? string.Empty;
                            result.StandardError = stderrTask.Result ?? string.Empty;
                        }
                        else
                        {
                            result.StandardOutput = string.Empty;
                            result.StandardError = string.Empty;
                        }
                    }
                    catch (AggregateException ae)
                    {
                        ae.Handle(ex => 
                        {
                            if (ex is ObjectDisposedException || ex is IOException || ex is TaskCanceledException) return true;
                            return false;
                        });
                        result.StandardOutput = string.Empty;
                        result.StandardError = string.Empty;
                    }

                    try
                    {
                        result.ExitCode = process.HasExited ? process.ExitCode : -1;
                    }
                    catch { result.ExitCode = -1; }
                }

                File.WriteAllText(Path.Combine(workingDirectory, "stdout.txt"), result.StandardOutput, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(workingDirectory, "stderr.txt"), result.StandardError, new UTF8Encoding(false));

                if (wasCancelled)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (result.TimedOut)
                {
                    return result;
                }

                if (result.ExitCode != 0)
                {
                    result.FailureCode = NgspiceFailureCode.NonZeroExitCode;
                    result.FailureMessage = "ngspice exited with code " + result.ExitCode + ".";
                    return result;
                }

                if (parseT1Output)
                {
                    ParseMarkedOutput(result);
                }
                result.Success = result.FailureCode == NgspiceFailureCode.None;
                return result;
            }
            catch (Exception exception)
            {
                // 文件、进程和编码属于外部边界；保留异常详情而非以成功或数值默认值掩盖失败。
                if (result.FailureCode == NgspiceFailureCode.None)
                {
                    result.FailureCode = NgspiceFailureCode.StartFailed;
                    result.FailureMessage = exception.ToString();
                }

                return result;
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
                PersistDiagnostics(netlist, result, persistentDiagnosticDirectory, netlistFileName);
                
                try
                {
                    if (Directory.Exists(workingDirectory))
                    {
                        Directory.Delete(workingDirectory, true);
                    }
                }
                catch { }
            }
        }

        private static void PersistDiagnostics(string netlist, NgspiceRunResult result, string diagnosticDirectory, string netlistFileName)
        {
            try
            {
                Directory.CreateDirectory(diagnosticDirectory);
                File.WriteAllText(Path.Combine(diagnosticDirectory, netlistFileName), netlist ?? string.Empty, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(diagnosticDirectory, "stdout.txt"), result.StandardOutput ?? string.Empty, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(diagnosticDirectory, "stderr.txt"), result.StandardError ?? string.Empty, new UTF8Encoding(false));
                File.WriteAllText(
                    Path.Combine(diagnosticDirectory, "summary.txt"),
                    "success=" + result.Success + Environment.NewLine +
                    "exitCode=" + result.ExitCode + Environment.NewLine +
                    "timedOut=" + result.TimedOut + Environment.NewLine +
                    "failureCode=" + result.FailureCode + Environment.NewLine +
                    "failureMessage=" + (result.FailureMessage ?? string.Empty) + Environment.NewLine +
                    "workingDirectory=" + (result.WorkingDirectory ?? string.Empty) + Environment.NewLine);
            }
            catch
            {
                // 诊断信息仅作补充，不能遮蔽 ngspice 的原始结果。
            }
        }

        private static void ParseMarkedOutput(NgspiceRunResult result)
        {
            var begin = result.StandardOutput.IndexOf(BeginMarker, StringComparison.OrdinalIgnoreCase);
            var end = begin >= 0
                ? result.StandardOutput.IndexOf(EndMarker, begin + BeginMarker.Length, StringComparison.OrdinalIgnoreCase)
                : -1;

            if (begin < 0 || end < 0 || end <= begin)
            {
                result.FailureCode = NgspiceFailureCode.OutputMarkersMissing;
                result.FailureMessage = "The T1 output markers were not found in ngspice stdout.";
                return;
            }

            var markedOutput = result.StandardOutput.Substring(begin + BeginMarker.Length, end - begin - BeginMarker.Length);
            var matches = PrintedValue.Matches(markedOutput);
            foreach (Match match in matches)
            {
                var kind = match.Groups["name"].Value;
                var id = match.Groups["id"].Value.Trim();
                var numericText = match.Groups["value"].Value.Replace('D', 'E').Replace('d', 'e');
                if (!double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    result.FailureCode = NgspiceFailureCode.ParseFailed;
                    result.FailureMessage = "Unable to parse ngspice value '" + match.Groups["value"].Value + "'.";
                    return;
                }

                if (string.Equals(kind, "v", StringComparison.OrdinalIgnoreCase))
                {
                    result.NodeVoltages[id] = value;
                }
                else if (string.Equals(kind, "i", StringComparison.OrdinalIgnoreCase))
                {
                    result.BranchCurrents[id] = value;
                }
            }

            if (!result.NodeVoltages.ContainsKey("input") || !result.BranchCurrents.ContainsKey("v1"))
            {
                result.FailureCode = NgspiceFailureCode.RequiredValueMissing;
                result.FailureMessage = "Marked stdout did not contain both v(input) and i(v1).";
            }
        }

        private static bool TryTerminateProcess(Process process, out string diagnostic)
        {
            diagnostic = string.Empty;
            if (process == null) return true;

            try
            {
                if (process.HasExited) return true;
                process.Kill();
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                diagnostic = "无法终止进程可能权限不足：" + ex.Message;
                return false;
            }
            catch (NotSupportedException ex)
            {
                diagnostic = "不支持终止进程：" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                diagnostic = "终止进程发生未预期异常：" + ex.Message;
                return false;
            }
        }
    }
}
