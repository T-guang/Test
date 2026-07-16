using System;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 已保存用户图纸的列表与操作元数据，当前由 SaveLoadService 枚举/创建，再由导入列表项和 ImportBlueprintPanel 消费。
    /// 它不是图纸 JSON 的根数据契约，也不包含元件、参数或导线；documentId、fileName 与 filePath 的有效性由创建和读取流程约定。
    /// 该类型当前用于运行时列表传递，字段变更前需复核保存列表、删除和导入入口；DateTime 仅保留文件系统读取的时间信息，
    /// 本类型自身不做路径合法性、文件存在性或用户身份验证。
    /// </summary>
    [Serializable]
    public sealed class SavedBlueprintInfo
    {
        public string documentId;
        public string documentName;
        public string savedAt;
        public string fileName;
        public string filePath;
        public DateTime lastWriteTime;
    }
}
