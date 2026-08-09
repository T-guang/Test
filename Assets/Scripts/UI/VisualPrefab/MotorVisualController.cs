using UnityEngine;
using ElectricalSim.Core;

namespace ElectricalSim.UI.VisualPrefab
{
    /// <summary>
    /// 驱动电机 Visual Prefab 中风扇转轴的显示动画。
    /// 普通三相电机优先读取 RuntimeStateManager 的相序方向；旧 rotationDirection 仅作兼容回退。
    /// 本控制器只消费 CircuitComponent / RuntimeStateManager 提供的运行态用于视觉动画，不负责修改仿真、
    /// Analyzer 或 RuntimeStateManager；Prefab 子节点缺失时保守停止视觉更新。
    /// 修改后需回归普通电机、正反转、自动往返和星三角的启动/停止显示。
    /// </summary>
    public class MotorVisualController : MonoBehaviour
    {
        [SerializeField] private RectTransform fanPivot;
        [SerializeField] private float rotationSpeed = -720f;

        private CircuitComponent component;

        private void Awake()
        {
            // 该控制器由 Visual Prefab 实例承载，运行时从父级元件读取状态；不自行创建电机或运行态。
            component = GetComponentInParent<CircuitComponent>();
            if (fanPivot == null)
            {
                var pivotTransform = transform.Find("FanPivot");
                if (pivotTransform != null)
                {
                    fanPivot = pivotTransform as RectTransform;
                }
            }
        }

        private void Update()
        {
            // 只在仿真已经写入得电和方向结果后刷新画面。动画速度与方向参数仅是视觉表现，
            // 不能作为电机正反转判断或模板保存的权威来源。
            if (component == null || fanPivot == null)
            {
                return;
            }

            if (component.IsEnergized)
            {
                if (RuntimeStateManager.Shared.TryGetMotorState(component.InstanceId, out var motorState) && motorState != null)
                {
                    if (!motorState.IsRunning)
                    {
                        return;
                    }

                    var runtimeDirection = motorState.Direction == MotorDirectionState.Forward ? 1f :
                        motorState.Direction == MotorDirectionState.Reverse ? -1f : 0f;
                    if (runtimeDirection != 0f)
                    {
                        fanPivot.Rotate(0, 0, runtimeDirection * rotationSpeed * Time.deltaTime);
                    }

                    return;
                }

                // 对尚未产生运行态记录的旧普通电机实例保留参数回退，确保视觉不会反向要求 SimulationEngine 预先创建状态。
                var parameter = component.GetParameter("rotationDirection");
                if (parameter != null)
                {
                    var parameterDirection = parameter.value > 0.5f ? 1f : parameter.value < -0.5f ? -1f : 0f;
                    if (parameterDirection != 0f)
                    {
                        fanPivot.Rotate(0, 0, parameterDirection * rotationSpeed * Time.deltaTime);
                    }

                    return;
                }

                // 星三角等多端子电机的方向和转速依赖专门的运行态语义，不能把普通三相 U/V/W 的视觉回退规则套用到它们。
                var isOrdinaryThreePhaseMotor = component.GetTerminal("U") != null &&
                    component.GetTerminal("V") != null && component.GetTerminal("W") != null;
                if (!isOrdinaryThreePhaseMotor)
                {
                    fanPivot.Rotate(0, 0, rotationSpeed * Time.deltaTime);
                }
            }
        }
    }
}
