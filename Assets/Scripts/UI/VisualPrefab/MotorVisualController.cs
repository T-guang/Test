using UnityEngine;
using ElectricalSim.Core;

namespace ElectricalSim.UI.VisualPrefab
{
    /// <summary>
    /// 驱动电机 Visual Prefab 中风扇转轴的显示动画。
    /// 视觉状态只读取父级 CircuitComponent 的得电状态和 rotationDirection 参数；旋转不反向影响
    /// 仿真、Analyzer 或 RuntimeStateManager。Prefab 子节点缺失时保守地停止显示更新。
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
                var dir = 1f;
                var param = component.GetParameter("rotationDirection");
                if (param != null)
                {
                    if (param.value < -0.5f) dir = -1f;
                    else if (param.value > -0.5f && param.value < 0.5f) dir = 0f;
                }

                if (dir != 0f)
                {
                    fanPivot.Rotate(0, 0, dir * rotationSpeed * Time.deltaTime);
                }
            }
        }
    }
}
