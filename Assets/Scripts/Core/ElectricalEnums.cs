using System;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 元件的基础运行类型，供 ComponentDefinition 和运行时分支读取；枚举本身不执行仿真。
    /// 成员调整前需复核既有定义资产及依赖 ComponentKind 的消费分支。
    /// </summary>
    public enum ComponentKind
    {
        PowerSource,
        Switch,
        TwoWaySwitch,
        PushButton,
        Fuse,
        Breaker,
        EnergyMeter,
        Lamp,
        Fan,
        Motor,
        ContactorCoil,
        Indicator,
        TerminalBlock,
        Instrument
    }

    /// <summary>
    /// 元件的展示与筛选分类。成员调整前需复核既有 ComponentDefinition 资产和分类 UI。
    /// </summary>
    public enum ComponentCategory
    {
        Household,
        Industrial,
        Measurement
    }

    /// <summary>
    /// 元件支持程度的配置枚举。当前成员包含显式整数值，调整数值或顺序前需复核既有资产和消费逻辑。
    /// </summary>
    public enum ComponentSupportLevel
    {
        VisualOnly = 0,
        ConnectorOnly = 1,
        SimpleSwitchSupported = 2,
        RuntimeSupported = 3,
        DynamicRuntimeSupported = 4,
        ParameterCalculationSupported = 5
    }

    /// <summary>
    /// 端子的语义角色，供定义、展示和运行时分支读取；它不直接决定真实电气连通性。
    /// </summary>
    public enum TerminalRole
    {
        Generic,
        Phase,
        Neutral,
        ProtectiveEarth,
        Input,
        Output,
        CoilA1,
        CoilA2
    }

    /// <summary>
    /// 导线的视觉路径样式。成员调整前需复核模板、用户图纸保存加载和 WireView/WireManager 的读取端。
    /// </summary>
    public enum WireStyle
    {
        Straight,
        Orthogonal
    }

    /// <summary>
    /// ComponentDefinition 内的单个端子配置。id 是模板和导线关联使用的端子标识，label 为显示文本；
    /// normalizedPosition 与 color 仅提供当前视觉配置，端子实际连通关系由运行时对象和 WireManager 管理。
    /// </summary>
    [Serializable]
    public sealed class TerminalDefinition
    {
        public string id = "T1";
        public string label = "T1";
        public TerminalRole role = TerminalRole.Generic;
        public Vector2 normalizedPosition = new Vector2(0.5f, 0f);
        public Color color = Color.yellow;
    }
}

