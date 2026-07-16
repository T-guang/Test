using System;
using System.Collections.Generic;

namespace ElectricalSim.Templates
{
    /// <summary>
    /// template_catalog.json 的 JsonUtility 根数据契约，保存系统模板目录项而非单张电路拓扑。
    /// CircuitTemplateCatalogLoader、图纸集与模板选择页面读取 templates；列表为空与读取失败的处理由 Loader/调用方负责，
    /// 本 DTO 自身不校验路径或加载模板。字段改名、类型变化前必须复核 Catalog JSON、排序/筛选和模板入口兼容性。
    /// </summary>
    [Serializable]
    public sealed class CircuitTemplateCatalogDto
    {
        public List<CircuitTemplateCatalogItemDto> templates = new List<CircuitTemplateCatalogItemDto>();
    }

    /// <summary>
    /// 单张系统模板的目录元数据。templateId 用于目录和流程关联，resourcePath 由当前 Resources 加载链路消费，
    /// thumbnailPath 供图纸集等展示入口读取；templateName、description、风险与练习说明字段是展示元数据，不替代模板 JSON 内容。
    /// 当前由 JsonUtility 从目录 JSON 反序列化读取；如后续增加写回流程，需同步复核字段兼容性。
    /// 空值和路径有效性由 Loader、UI 或完整性检查器处理，DTO 本身不强制验证。
    /// </summary>
    [Serializable]
    public sealed class CircuitTemplateCatalogItemDto
    {
        public string templateId;
        public string templateName;
        public string category;
        public string difficulty;
        public string description;
        public string resourcePath;
        public string thumbnailPath;
        public string referenceDiagramNote;
        public string practiceVariantNote;
        public string diagramRiskLevel;
        public int sortOrder;
    }
}

