using System.IO;
using UnityEngine;

namespace ElectricalSim.Templates
{
    /// <summary>
    /// 读取并解析单张系统模板的 DTO 数据。
    /// 本类只处理资源文本与反序列化结果，不直接创建 CircuitComponent 或修改 Workspace；生成与完整端子校验由 CircuitTemplateSpawnService 负责。
    /// 当前在 Editor 优先读取 Resources 下的源 JSON，在其他环境回退到 Resources.Load，因此调用方只能传递稳定的 Resources 相对路径。
    /// 修改后应验证 Template Integrity、18 模板基线以及 Editor 与 Windows 构建环境的资源读取。
    /// </summary>
    public static class CircuitTemplateLoader
    {
        public static bool TryLoad(string resourcesPath, out CircuitTemplateDto template, out string error)
        {
            template = null;
            error = null;

            if (string.IsNullOrWhiteSpace(resourcesPath))
            {
                error = "模板路径为空。";
                return false;
            }

            var json = ReadTemplateJson(resourcesPath, out error);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = string.IsNullOrWhiteSpace(error) ? "模板读取失败：" + resourcesPath : error;
                return false;
            }

            try
            {
                template = JsonUtility.FromJson<CircuitTemplateDto>(json);
            }
            catch (System.Exception exception)
            {
                error = "模板解析失败：" + exception.Message;
                return false;
            }

            if (template == null || string.IsNullOrWhiteSpace(template.templateId))
            {
                error = "模板数据为空或缺少 templateId。";
                return false;
            }

            return true;
        }

        private static string ReadTemplateJson(string resourcesPath, out string error)
        {
            error = null;

#if UNITY_EDITOR
            // Editor 直接读取源文件便于模板维护；正式运行环境没有该磁盘路径，只能走下方的 Resources 资源读取。
            var assetPath = Path.Combine(Application.dataPath, "Resources", resourcesPath + ".json").Replace("\\", "/");
            if (File.Exists(assetPath))
            {
                try
                {
                    return File.ReadAllText(assetPath);
                }
                catch (System.Exception exception)
                {
                    error = "模板磁盘读取失败：" + exception.Message;
                    return null;
                }
            }
#endif

            var asset = Resources.Load<TextAsset>(resourcesPath);
            if (asset == null)
            {
                error = "模板读取失败：" + resourcesPath;
                return null;
            }

            return asset.text;
        }
    }
}
