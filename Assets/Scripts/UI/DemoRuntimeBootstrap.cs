using System.Collections.Generic;
using ElectricalSim.Core;
using UnityEngine;

namespace ElectricalSim.UI
{
    // Runtime Bootstrap 只在已构建的 Demo 场景中装配可选的家庭照明示例，用于首次展示；它不同于 Editor 的 DemoSceneBuilder，
    // 不创建场景结构、Definition 资产或 UI 绑定。示例构建完成后清除构建过程的 undo/redo 历史并标记拓扑需要重新仿真，
    // 使当前 demo 成为新的初始编辑基线。
    public sealed class DemoRuntimeBootstrap : MonoBehaviour
    {
        [SerializeField] private WorkspaceController workspace;
        [SerializeField] private List<ComponentDefinition> catalog = new List<ComponentDefinition>();
        [SerializeField] private bool buildStarterCircuit = true;

        private void Start()
        {
            if (!buildStarterCircuit || workspace == null || catalog.Count == 0)
            {
                return;
            }

            BuildHomeLightingCircuit();
        }

        private void BuildHomeLightingCircuit()
        {
            // 该方法按固定实例 id、端子和 Wire 顺序重建一套演示拓扑。这里的坐标和颜色只服务演示可读性；连接有效性仍由 Workspace/WireManager 审核。
            workspace.ClearDrawing();

            var power = Find("AC_220V_Power");
            var meter = Find("Single_Phase_Meter");
            var breaker = Find("Breaker_2P");
            var switchOne = Find("Single_Control_Switch");
            var lamp = Find("Lamp_220V");
            var fan = Find("Fan_220V");

            if (power == null || meter == null || breaker == null || switchOne == null || lamp == null || fan == null)
            {
                workspace.SetStatus("演示元件数据缺失，请重新执行 Tools/Electrical Demo/Build Demo Scene。");
                return;
            }

            var powerView = workspace.SpawnComponent(power, new Vector2(-520f, 180f), "demo_power");
            var meterView = workspace.SpawnComponent(meter, new Vector2(-290f, 180f), "demo_meter");
            var breakerView = workspace.SpawnComponent(breaker, new Vector2(-40f, 180f), "demo_breaker_2p");
            var switchView = workspace.SpawnComponent(switchOne, new Vector2(240f, 180f), "demo_single_switch");
            var lampView = workspace.SpawnComponent(lamp, new Vector2(520f, 180f), "demo_lamp");
            var fanView = workspace.SpawnComponent(fan, new Vector2(520f, -80f), "demo_fan");

            breakerView.SetClosed(true);
            switchView.SetClosed(true);

            CreateWire(powerView, "L", meterView, "L_IN", new Color(0.95f, 0.1f, 0.1f));
            CreateWire(powerView, "N", meterView, "N_IN", new Color(0.1f, 0.35f, 0.95f));
            CreateWire(meterView, "L_OUT", breakerView, "P1_IN", new Color(0.95f, 0.1f, 0.1f));
            CreateWire(meterView, "N_OUT", breakerView, "P2_IN", new Color(0.1f, 0.35f, 0.95f));
            CreateWire(breakerView, "P1_OUT", switchView, "L", new Color(0.95f, 0.1f, 0.1f));
            CreateWire(switchView, "L1", lampView, "L", new Color(0.95f, 0.1f, 0.1f));
            CreateWire(switchView, "L1", fanView, "L", new Color(0.08f, 0.65f, 0.25f));
            CreateWire(lampView, "N", breakerView, "P2_OUT", new Color(0.1f, 0.35f, 0.95f));
            CreateWire(fanView, "N", breakerView, "P2_OUT", new Color(0.1f, 0.35f, 0.95f));

            workspace.WireManager.RefreshAll();
            workspace.MarkTopologyDirty();
            workspace.ClearHistory();
            workspace.SetView(0.85f, Vector2.zero);
            workspace.SetStatus("默认家庭照明示例：电能表经过 2P 空开和单开开关控制灯泡/风扇。双击开关或空开可切换通断。");
        }

        private ComponentDefinition Find(string assetName)
        {
            return catalog.Find(item => item != null && item.name == assetName);
        }

        private void CreateWire(CircuitComponent startComponent, string startTerminal, CircuitComponent endComponent, string endTerminal, Color color)
        {
            workspace.WireManager.CreateWire(
                startComponent.GetTerminal(startTerminal),
                endComponent.GetTerminal(endTerminal),
                color,
                WireStyle.Orthogonal);
        }
    }
}
