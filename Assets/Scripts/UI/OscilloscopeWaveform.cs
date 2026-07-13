using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 使用 UGUI 顶点绘制简化的教学示波器网格和参考波形。
    /// 波形由 MeasurementPanel 提供的电压、交直流标记和活动状态生成，不是采样设备数据，
    /// 不参与 CircuitStateAnalyzer 或规则校验。RectTransform 尺寸变化会触发重绘；无有效信号时
    /// 保持零线。修改后需检查示波器入口及不同分辨率下的显示。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class OscilloscopeWaveform : MaskableGraphic
    {
        private float voltage;
        private bool alternating;
        private bool active;

        protected override void Awake()
        {
            if (GetComponent<CanvasRenderer>() == null)
            {
                gameObject.AddComponent<CanvasRenderer>();
            }

            base.Awake();
        }

        public void SetSignal(float signalVoltage, bool isAlternating, bool isActive)
        {
            // 仅缓存下一次 OnPopulateMesh 所需的显示输入；不保存历史采样，也不修改元件运行态。
            voltage = Mathf.Max(0f, signalVoltage);
            alternating = isAlternating;
            active = isActive;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // 网格和曲线都由当前 RectTransform 生成，避免将某一分辨率的 UI 几何缓存为权威坐标。
            vh.Clear();

            var rect = rectTransform.rect;
            DrawGrid(vh, rect);

            var samples = 72;
            var amplitude = active ? Mathf.Lerp(10f, rect.height * 0.38f, Mathf.InverseLerp(0f, 430f, voltage)) : 0f;
            var centerY = rect.center.y;
            var previous = Vector2.zero;

            for (var i = 0; i < samples; i++)
            {
                var t = samples <= 1 ? 0f : i / (samples - 1f);
                var x = Mathf.Lerp(rect.xMin + 10f, rect.xMax - 10f, t);
                var y = centerY;
                if (active)
                {
                    y += alternating ? Mathf.Sin(t * Mathf.PI * 4f) * amplitude : amplitude * 0.35f;
                }

                var current = new Vector2(x, y);
                if (i > 0)
                {
                    DrawSegment(vh, previous, current, 3f, active ? new Color32(30, 120, 255, 255) : new Color32(120, 130, 145, 210));
                }

                previous = current;
            }
        }

        private static void DrawGrid(VertexHelper vh, Rect rect)
        {
            var gridColor = new Color32(210, 222, 238, 120);
            for (var i = 1; i < 4; i++)
            {
                var y = Mathf.Lerp(rect.yMin, rect.yMax, i / 4f);
                DrawSegment(vh, new Vector2(rect.xMin, y), new Vector2(rect.xMax, y), 1f, gridColor);
            }

            for (var i = 1; i < 6; i++)
            {
                var x = Mathf.Lerp(rect.xMin, rect.xMax, i / 6f);
                DrawSegment(vh, new Vector2(x, rect.yMin), new Vector2(x, rect.yMax), 1f, gridColor);
            }
        }

        private static void DrawSegment(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color32 color)
        {
            var delta = end - start;
            if (delta.sqrMagnitude < 0.01f)
            {
                return;
            }

            var normal = new Vector2(-delta.y, delta.x).normalized * (thickness * 0.5f);
            var index = vh.currentVertCount;
            vh.AddVert(start - normal, color, Vector2.zero);
            vh.AddVert(start + normal, color, Vector2.zero);
            vh.AddVert(end + normal, color, Vector2.zero);
            vh.AddVert(end - normal, color, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
            vh.AddTriangle(index, index + 2, index + 3);
        }
    }
}
