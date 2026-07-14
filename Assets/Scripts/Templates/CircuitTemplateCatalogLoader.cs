using UnityEngine;

namespace ElectricalSim.Templates
{
    /// <summary>
    /// 读取系统模板目录文件并反序列化其中的目录项。
    /// 输入是 Resources 相对路径，输出仅为 Catalog DTO；不读取单张模板 JSON，不校验元件、端子或导线，也不生成画布对象。
    /// templateId 与目录顺序会被模板选择 UI、真实模板基线和测试使用，修改目录路径或目录数据后必须运行 Template Integrity 与 18 模板基线。
    /// </summary>
    public static class CircuitTemplateCatalogLoader
    {
        public static bool TryLoad(string resourcesPath, out CircuitTemplateCatalogDto catalog, out string error)
        {
            catalog = null;
            error = null;

            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                error = "模板目录路径为空。";
                return false;
            }

            // Catalog 必须通过 Resources 相对路径读取，不能依赖开发机绝对文件路径，保证 Editor 与构建后的读取入口一致。
            var asset = Resources.Load<TextAsset>(resourcesPath);
            if (asset == null)
            {
                error = "模板目录读取失败。";
                return false;
            }

            try
            {
                catalog = JsonUtility.FromJson<CircuitTemplateCatalogDto>(asset.text);
            }
            catch (System.Exception exception)
            {
                error = "模板目录解析失败：" + exception.Message;
                return false;
            }

            if (catalog == null || catalog.templates == null)
            {
                error = "模板目录数据为空。";
                return false;
            }

            return true;
        }
    }
}
