using System;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 可序列化的元件参数项，作为 ComponentDefinition 配置和模板/保存图纸实例参数的共同数据形状。
    /// 当前由 Definition、模板 DTO 和 CircuitComponent 参数集合创建、复制与读取；本类型本身不决定参数估算、UI 编辑或仿真结果。
    /// 公开字段会参与 Unity 序列化，并会随包含它的模板 DTO 进入 JsonUtility 读写；字段改名、类型或默认值变化前需复核资产、模板和用户图纸读取端。
    /// </summary>
    [Serializable]
    public sealed class ComponentParameter
    {
        // 调用方当前按 key 查找参数；显示名称仅用于面向用户的文本。
        public string key;
        public string displayName;
        public float value;
        public string unit;
        public float min;
        public float max;
        public bool editable;

        public ComponentParameter Clone()
        {
            // 复制保持当前所有字段值；不要在此处加入额外规范化或兼容迁移。
            return new ComponentParameter
            {
                key = key,
                displayName = displayName,
                value = value,
                unit = unit,
                min = min,
                max = max,
                editable = editable
            };
        }

        public void ClampValue()
        {
            // 仅当 max 大于 min 时按现有约定限制 value；范围语义由持有者配置。
            if (max > min)
            {
                value = Mathf.Clamp(value, min, max);
            }
        }
    }
}
