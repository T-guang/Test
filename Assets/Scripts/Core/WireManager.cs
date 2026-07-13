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

            if (start.Owner != end.Owner)
            {
                return true;
            }

            var component = start.Owner;
            if (component == null ||
                component.Definition == null ||
                !component.Definition.allowSameComponentJumper ||
                component.Definition.name.IndexOf("Motor_StarDelta", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                rejectionReason = "当前元件不允许同一器件内部端子跳线。";
                return false;
            }

            if (!IsStarDeltaJumperTerminal(start.TerminalId) || !IsStarDeltaJumperTerminal(end.TerminalId))
            {
                rejectionReason = "星三角电机只允许 U1/V1/W1/U2/V2/W2 参与跳线，PE 不参与。";
                return false;
            }

            return true;
        }

        public WireView CreateWire(TerminalView start, TerminalView end, Color color, WireStyle style)
        {
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

        private static bool IsStarDeltaJumperTerminal(string terminalId)
        {
            return terminalId == "U1" ||
                terminalId == "V1" ||
                terminalId == "W1" ||
                terminalId == "U2" ||
                terminalId == "V2" ||
                terminalId == "W2";
        }

        public void DeleteWire(WireView wire)
        {
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

