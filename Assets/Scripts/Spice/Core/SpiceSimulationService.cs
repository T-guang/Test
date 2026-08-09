using System;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;

namespace ElectricalSim.Spice.Core
{
    // 统一求解入口只按 AnalysisSettings 分派 DC/AC 服务，并保留可注入委托供受控测试使用。
    // 它不构建 Unity 视图、不管理 Workspace 结果生命周期，也不应把 UI 模式文本当作分析模式的权威来源。
    public sealed class SpiceSimulationService
    {
        private readonly SpiceDcSimulationService dcService;
        private readonly SpiceAcSimulationService acService;

        public SpiceSimulationService(SpiceDcSimulationService dcService = null, SpiceAcSimulationService acService = null)
        {
            this.dcService = dcService ?? new SpiceDcSimulationService();
            this.acService = acService ?? new SpiceAcSimulationService();
        }

        internal SpiceSimulationService(
            Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> dcSimulation,
            Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> acSimulation)
        {
            dcService = null;
            acService = null;
            this.dcSimulation = dcSimulation ?? throw new ArgumentNullException(nameof(dcSimulation));
            this.acSimulation = acSimulation ?? throw new ArgumentNullException(nameof(acSimulation));
        }

        private Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> dcSimulation;
        private Func<SpiceCircuitModel, CancellationToken, Task<SpiceSimulationResult>> acSimulation;

        public Task<SpiceSimulationResult> SimulateAsync(SpiceCircuitModel circuit, CancellationToken cancellationToken = default(CancellationToken))
        {
            // 分派依据是模型中的稳定 AnalysisSettings；新增分析模式时必须同时提供明确服务和失败语义，不能悄悄落入 DC 默认分支。
            if (circuit == null) throw new ArgumentNullException(nameof(circuit));
            switch (circuit.AnalysisSettings.Mode)
            {
                case SpiceAnalysisMode.DcOperatingPoint:
                    return dcSimulation != null ? dcSimulation(circuit, cancellationToken) : dcService.SimulateAsync(circuit, cancellationToken);
                case SpiceAnalysisMode.AcSingleFrequency:
                    return acSimulation != null ? acSimulation(circuit, cancellationToken) : acService.SimulateAsync(circuit, cancellationToken);
                default:
                    var result = new SpiceSimulationResult { AnalysisSettings = circuit.AnalysisSettings.Copy() };
                    result.Diagnostics.Add(new SpiceDiagnostic("SPICE_ANALYSIS_MODE_UNSUPPORTED", SpiceDiagnosticSeverity.Error,
                        "The selected SPICE analysis mode is not supported."));
                    return Task.FromResult(result);
            }
        }
    }
}
