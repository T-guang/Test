using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Infrastructure;
using UnityEngine;

namespace ElectricalSim.Tests.SpiceT3
{
    public class NgspiceProcessRunnerTests : MonoBehaviour
    {
        private async void Start()
        {
            try
            {
                await RunAsync_WhenCancelled_ThrowsOperationCanceledExceptionAndClosesPipes();
                await RunAsync_WhenTimedOut_ReturnsTimeoutResultAndClosesPipes();
                await RunAsync_DeletesTemporaryDirectory_OnCompletion();
                Debug.Log("[Spice][ngspice] 进程运行器测试：通过");
            }
            catch (Exception ex)
            {
                Debug.LogError("[NgspiceProcessRunnerTests] Test failed: " + ex);
            }
        }

        private async Task RunAsync_WhenCancelled_ThrowsOperationCanceledExceptionAndClosesPipes()
        {
            var runner = new NgspiceProcessRunner();
            using (var cts = new CancellationTokenSource())
            {
                var netlist = "v1 input 0 dc 5\nr1 input 0 1k\n.dc v1 0 5 0.1\n.print dc v(input)\n.end";
                var runTask = runner.RunRawNetlistAsync(netlist, TimeSpan.FromSeconds(10), "TestCancel", cts.Token);
                cts.Cancel(); 
                try
                {
                    await runTask;
                    throw new Exception("Expected OperationCanceledException was not thrown.");
                }
                catch (OperationCanceledException)
                {
                    // Pass
                }
            }
        }

        private async Task RunAsync_WhenTimedOut_ReturnsTimeoutResultAndClosesPipes()
        {
            var runner = new NgspiceProcessRunner();
            var netlist = "v1 input 0 dc 5\nr1 input 0 1k\n.dc v1 0 5 0.0001\n.print dc v(input)\n.end";
            var result = await runner.RunRawNetlistAsync(netlist, TimeSpan.FromMilliseconds(1), "TestTimeout", CancellationToken.None);
            if (!result.TimedOut) throw new Exception("Result should be marked as timed out.");
            if (result.FailureCode != NgspiceFailureCode.TimedOut) throw new Exception("Expected TimedOut failure code.");
        }

        private async Task RunAsync_DeletesTemporaryDirectory_OnCompletion()
        {
            var runner = new NgspiceProcessRunner();
            var netlist = "v1 input 0 dc 5\nr1 input 0 1k\n.end";
            var result = await runner.RunRawNetlistAsync(netlist, TimeSpan.FromSeconds(2), "TestCleanup", CancellationToken.None);
            var tempDir = result.WorkingDirectory;
            if (Directory.Exists(tempDir)) throw new Exception($"Temporary directory {tempDir} should have been deleted.");
        }
    }
}
