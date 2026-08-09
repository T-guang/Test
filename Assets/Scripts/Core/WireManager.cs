using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 管理一个 WorkspaceController 所属的导线视图，并执行本地导线创建约束。
    /// 不负责元件生命周期或电气分析；其 Wires 集合是仿真、分析、校验和保存加载的
    /// 活动工作区导线唯一权威输入。
    /// </summary>
    public sealed class WireManager : MonoBehaviour
    {
        // 每条 Wire 的端点对象与端子身份定义外部接线事实；元件内部触点的当前导通由仿真/分析单独建图，
        // 绝不能伪造为 Wire 加入此集合。创建、删除和清空必须先保持该集合一致，供撤销重做与工作区生命周期读取。
        [SerializeField] private RectTransform wireLayer;

        public IReadOnlyList<WireView> Wires => wires;

        private readonly List<WireView> wires = new List<WireView>();
        private WorkspaceController workspace;

        public void Initialize(RectTransform layer, WorkspaceController owner)
        {
            wireLayer = layer;
            workspace = owner;
        }

        public WorkspaceController Workspace => workspace;

        public bool CanCreateWire(TerminalView start, TerminalView end, out string rejectionReason)
        {
            rejectionReason = null;
            if (start == null || end == null)
            {
                rejectionReason = "导线端点无效。";
                return false;
            }

            if (start == end)
            {
                rejectionReason = "不能将端子连接到自身。";
                return false;
            }

            // 不同元件接线保持原逻辑：直接允许。
            // duplicate wire、画布锁定等由 CreateWire 和上层 CircuitComponent/WorkspaceController 负责。
            if (start.Owner != end.Owner)
            {
                return true;
            }

            // 同一元件：委托集中策略 SameComponentWirePolicy，不再使用本地星三角白名单或字符串特判。
            var component = start.Owner;
            var definition = component != null ? component.Definition : null;
            if (!SameComponentWirePolicy.CanConnect(definition, start.TerminalId, end.TerminalId, out var policyReason))
            {
                rejectionReason = policyReason;
                return false;
            }

            return true;
        }

        public WireView CreateWire(TerminalView start, TerminalView end, Color color, WireStyle style)
        {
            // 创建先验证端点与同元件策略，再查询并维护权威集合的无向端点去重；返回既有线保证一个端点对
            // 只对应一条外部 Wire，避免拓扑、保存快照和撤销记录出现重复边。
            if (!CanCreateWire(start, end, out _))
            {
                return null;
            }

            var existing = wires.Find(w => (w.StartTerminal == start && w.EndTerminal == end) || (w.StartTerminal == end && w.EndTerminal == start));
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("Wire", typeof(RectTransform), typeof(WireView));
            go.transform.SetParent(wireLayer, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var wire = go.GetComponent<WireView>();
            wire.Initialize(start, end, color, style, workspace);
            wires.Add(wire);
            ReflowOffsets();
            return wire;
        }

        public void DeleteWire(WireView wire)
        {
            // 先从权威集合撤销电气事实，再延迟销毁显示对象；同一帧的分析、保存和撤销只能读取移除后的集合。
            if (wire == null)
            {
                return;
            }

            wires.Remove(wire);
            Destroy(wire.gameObject);
            ReflowOffsets();
        }

        public void DeleteWiresFor(CircuitComponent component)
        {
            for (var i = wires.Count - 1; i >= 0; i--)
            {
                if (wires[i] != null && wires[i].Uses(component))
                {
                    DeleteWire(wires[i]);
                }
            }
        }

        public void Clear()
        {
            // 清空只移除用户外部导线及其视图；不会重置元件运行态，生命周期调用方需分别处理 RuntimeStateManager。
            foreach (var wire in wires)
            {
                if (wire != null)
                {
                    Destroy(wire.gameObject);
                }
            }

            wires.Clear();
        }

        public void RefreshAll()
        {
            ReflowOffsets();
            foreach (var wire in wires)
            {
                wire.Refresh();
            }
        }

        public void RefreshFor(CircuitComponent component)
        {
            ReflowOffsets();
            foreach (var wire in wires)
            {
                if (wire.Uses(component))
                {
                    wire.Refresh();
                }
            }
        }

        private void ReflowOffsets()
        {
            // 偏移仅改善同层导线的可读性，不参与电气连接或保存语义。
            var lanes = new[] { 0f, 22f, -22f, 44f, -44f, 66f, -66f, 88f, -88f };

            for (var i = 0; i < wires.Count; i++)
            {
                var lane = i % lanes.Length;
                var band = i / lanes.Length;
                var offset = lanes[lane];
                if (band > 0)
                {
                    var direction = Mathf.Abs(offset) < 0.1f || offset > 0f ? 1f : -1f;
                    offset += direction * band * 16f;
                }

                wires[i].SetRouteOffset(offset);
            }
        }
    }
}

