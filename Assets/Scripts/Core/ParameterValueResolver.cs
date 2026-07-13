namespace ElectricalSim.Core
{
    /// <summary>
    /// 统一读取元件实例参数，并在实例缺失时回退到 ComponentDefinition 的同义字段。
    /// 它不把 UI 文本当作参数来源，也不写回默认值；保存加载和参数面板应继续维护实例参数，
    /// 需要新增参数别名时必须回归参数面板、模板加载和保存导入。
    /// </summary>
    public static class ParameterValueResolver
    {
        public static bool TryGetFloat(
            CircuitComponent component,
            out float value,
            params string[] keys)
        {
            value = 0f;
            if (component == null || keys == null)
            {
                return false;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var parameter = component.GetParameter(keys[i]);
                if (parameter != null)
                {
                    value = parameter.value;
                    return true;
                }
            }

            return false;
        }

        public static float GetFloatOrFallback(
            CircuitComponent component,
            float fallback,
            params string[] keys)
        {
            if (TryGetFloat(component, out var value, keys))
            {
                return value;
            }

            // 实例参数代表用户当前图纸；定义值只用于缺失实例参数时的兼容回退，
            // 不能反向覆盖实例值，否则会丢失保存图纸中的用户配置。
            var definitionValue = ResolveDefinitionValue(component != null ? component.Definition : null, keys);
            return definitionValue > 0f ? definitionValue : fallback;
        }

        private static float ResolveDefinitionValue(ComponentDefinition definition, string[] keys)
        {
            if (definition == null || keys == null)
            {
                return 0f;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                switch (keys[i])
                {
                    case ParameterKeys.SourceVoltage:
                    case ParameterKeys.Voltage:
                        if (definition.sourceVoltage > 0f)
                        {
                            return definition.sourceVoltage;
                        }
                        break;
                    case ParameterKeys.SourceLineVoltage:
                    case ParameterKeys.LineVoltage:
                        if (definition.sourceLineVoltage > 0f)
                        {
                            return definition.sourceLineVoltage;
                        }
                        break;
                    case ParameterKeys.RatedVoltage:
                        if (definition.ratedVoltage > 0f)
                        {
                            return definition.ratedVoltage;
                        }
                        break;
                    case ParameterKeys.RatedPower:
                    case ParameterKeys.Power:
                        if (definition.ratedPower > 0f)
                        {
                            return definition.ratedPower;
                        }
                        break;
                    case ParameterKeys.RatedCurrent:
                    case ParameterKeys.Current:
                        if (definition.ratedCurrent > 0f)
                        {
                            return definition.ratedCurrent;
                        }
                        break;
                }
            }

            return 0f;
        }
    }
}
