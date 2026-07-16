using System.Collections.Generic;

namespace ElectricalSim.Core
{
    /// <summary>
    /// CircuitComponent 持有的可序列化参数集合，用于从 ComponentDefinition、模板/保存数据复制实例参数，并供参数面板读取。
    /// 它不是参数估算引擎；当前产品测试政策虽不检查参数估算值，参数模型仍服务于编辑、保存加载和兼容路径。
    /// 参数查找使用 key 的区分大小写相等比较，找不到时返回 null/false。集合本身不验证 key 唯一性或参数业务语义。
    /// </summary>
    [System.Serializable]
    public sealed class ComponentParameterSet
    {
        public List<ComponentParameter> parameters = new List<ComponentParameter>();

        public void SetParameters(IEnumerable<ComponentParameter> source)
        {
            // 原地清空后按输入顺序逐字段 Clone 并 Clamp；null 输入得到空集合，原参数对象不会被直接共享。
            parameters.Clear();
            if (source == null)
            {
                return;
            }

            foreach (var parameter in source)
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.key))
                {
                    continue;
                }

                var clone = parameter.Clone();
                clone.ClampValue();
                parameters.Add(clone);
            }
        }

        public ComponentParameter GetParameter(string key)
        {
            // 空 key 或未命中均返回 null；调用方决定是否使用定义默认值或报告失败。
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return parameters.Find(item => item != null && item.key == key);
        }

        public bool SetParameterValue(string key, float value)
        {
            // 原地修改已找到实例并沿用 ComponentParameter 的现有范围限制；不新增缺失参数。
            var parameter = GetParameter(key);
            if (parameter == null)
            {
                return false;
            }

            parameter.value = value;
            parameter.ClampValue();
            return true;
        }

        public List<ComponentParameter> CloneList()
        {
            // 返回保持当前顺序的逐项 Clone 列表；null 项会被跳过。
            var result = new List<ComponentParameter>();
            foreach (var parameter in parameters)
            {
                if (parameter != null)
                {
                    result.Add(parameter.Clone());
                }
            }

            return result;
        }
    }
}
