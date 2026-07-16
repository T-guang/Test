using System;
using System.Collections.Generic;
using ElectricalSim.Core;
using UnityEngine;

namespace ElectricalSim.Templates
{
    /// <summary>
    /// 单张系统模板 JSON 的根 DTO，承载模板标识、展示信息、元件实例列表和导线列表。
    /// CircuitTemplateLoader 通过 JsonUtility 填充，CircuitTemplateSpawnService、练习标准网表和布局更新路径读取；
    /// 本 DTO 只描述静态模板数据，不创建 Workspace 对象、不验证拓扑，也不保存运行态状态。
    /// 字段改名、类型或默认集合变化前需复核全部模板 JSON、加载/生成顺序与测试基线。
    /// </summary>
    [Serializable]
    public sealed class CircuitTemplateDto
    {
        public string templateId;
        public string templateName;
        public string category;
        public string difficulty;
        public string description;
        public List<TemplateComponentDto> components = new List<TemplateComponentDto>();
        public List<TemplateWireDto> wires = new List<TemplateWireDto>();
    }

    /// <summary>
    /// 模板中的单个元件实例。instanceId 在模板内用于导线端点和生成对象关联，definitionName 用于查找 ComponentDefinition；
    /// x/y 为模板布局坐标，isClosed 和 parameters 是模板提供的初始配置而非持续运行态真值。
    /// 空值、重复实例或缺失 Definition 的拒绝边界由 Loader/SpawnService 检查，本 DTO 本身不强制验证。
    /// </summary>
    [Serializable]
    public sealed class TemplateComponentDto
    {
        public string instanceId;
        public string definitionName;
        public float x;
        public float y;
        public bool isClosed;
        public List<ComponentParameter> parameters = new List<ComponentParameter>();
    }

    /// <summary>
    /// 模板中的单根导线数据。起止 componentId/terminalId 关联 TemplateComponentDto 与其定义端子，color、style 和手动折线路径
    /// 描述现有视觉布局；它们不自行判定电气连通性或接线规则。字段调整前需同步复核模板 JSON、WireManager 生成和布局更新读取端。
    /// </summary>
    [Serializable]
    public sealed class TemplateWireDto
    {
        public string startComponentId;
        public string startTerminalId;
        public string endComponentId;
        public string endTerminalId;
        public string color;
        public string style;
        
        // 可选手动折线路径；null 与空列表的实际呈现由导线创建和布局更新调用方处理。
        public List<Vector2> manualRoutePoints;
        public bool manualRouteHorizontal;
        public float manualRouteAxis;
    }
}
