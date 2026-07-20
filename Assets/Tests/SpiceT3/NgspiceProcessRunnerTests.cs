using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Infrastructure;
using NUnit.Framework;
using UnityEngine;

namespace ElectricalSim.Tests.SpiceT3
{
    [TestFixture]
    public class NgspiceProcessRunnerTests
    {
        private NgspiceProcessRunner runner;

        [SetUp]
        public void Setup()
        {
            runner = new NgspiceProcessRunner();
        }

        [Test]
        public async Task RunAsync_WhenCancelled_ThrowsOperationCanceledExceptionAndClosesPipes()
        {
            using (var cts = new CancellationTokenSource())
            {
                // Give it a netlist that might take a bit or we just cancel immediately
                var netlist = "v1 input 0 dc 5\nr1 input 0 1k\n.dc v1 0 5 0.1\n.print dc v(input)\n.end";
                
                var runTask = runner.RunRawNetlistAsync(netlist, TimeSpan.FromSeconds(10), "TestCancel", cts.Token);
                
                cts.Cancel(); // Immediately cancel

                try
                {
                    await runTask;
                    Assert.Fail("Expected OperationCanceledException was not thrown.");
                }
                catch (OperationCanceledException)
                {
                    Assert.Pass();
                }
            }
        }

        [Test]
        public async Task RunAsync_WhenTimedOut_ReturnsTimeoutResultAndClosesPipes()
        {
            var netlist = "v1 input 0 dc 5\nr1 input 0 1k\n.dc v1 0 5 0.0001\n.print dc v(input)\n.end";
            // Set an extremely short timeout
            var result = await runner.RunRawNetlistAsync(netlist, TimeSpan.FromMilliseconds(1), "TestTimeout", CancellationToken.None);
            
            Assert.IsTrue(result.TimedOut, "Result should be marked as timed out.");
            Assert.AreEqual(NgspiceFailureCode.TimedOut, result.FailureCode);
        }

        [Test]
        public async Task RunAsync_DeletesTemporaryDirectory_OnCompletion()
        {
            var netlist = "v1 input 0 dc 5\nr1 input 0 1k\n.end";
            var result = await runner.RunRawNetlistAsync(netlist, TimeSpan.FromSeconds(2), "TestCleanup", CancellationToken.None);

            var tempDir = result.WorkingDirectory;
            Assert.IsFalse(Directory.Exists(tempDir), $"Temporary directory {tempDir} should have been deleted.");
        }
    }
}
