using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ElectricalSim.UI.CommonTools;

/// <summary>
/// 仅在 Unity Editor 中审计 CommonToolsPageController 动态页面层级的轻量诊断工具。
/// 菜单 <c>Electrical/Diagnostics/Audit Common Tools Page</c> 查找当前已加载场景中的控制器；若找不到，当前实现会创建
/// 名为 <c>TestCommonTools</c> 的临时 GameObject 并调用 BuildPage，然后遍历其 Transform、Image、Mask、Shadow 与 Outline
/// 组件，将结果写入进程当前工作目录下的 <c>CommonToolsAuditResult.txt</c>。
///
/// 该工具不使用 AssetDatabase、不保存场景或生成正式 Assets，但并非完全只读：缺少控制器时会改变当前内存场景对象并且不在
/// 本工具内销毁。没有 Play Mode 限制；执行前应确认当前场景允许该临时对象存在。报告未发现可疑组件或命名，只代表本脚本
/// 当前遍历范围内未发现对应线索，不代表常用工具页面、交互或 Player 行为已经完整验证。文件写入失败会由调用异常暴露，
/// 完成信息写入 Console。
/// </summary>
public static class CommonToolsAuditor
{
    [MenuItem("Electrical/Diagnostics/Audit Common Tools Page")]
    public static void RunAudit()
    {
        // 为获得动态页面层级，当前实现会直接激活并重建控制器；不要在未确认场景状态时运行。
        var controller = Object.FindObjectOfType<CommonToolsPageController>(true);
        if (controller == null)
        {
            var go = new GameObject("TestCommonTools");
            controller = go.AddComponent<CommonToolsPageController>();
        }

        // Force rebuild to get latest UI elements
        controller.gameObject.SetActive(true);
        controller.BuildPage();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Runtime Component Audit for Common Tools ===");

        int shadowCount = 0;
        int outlineCount = 0;
        int suspectNameCount = 0;

        void Traverse(Transform t, string path)
        {
            var currentPath = path + "/" + t.name;
            
            bool hasShadow = t.GetComponent<Shadow>() != null;
            bool hasOutline = t.GetComponent<Outline>() != null;
            
            bool isSuspectName = t.name.Contains("Shadow") || t.name.Contains("Outline") || t.name.Contains("Border") || t.name.Contains("Glow");

            var img = t.GetComponent<Image>();
            
            var mask = t.GetComponent<Mask>();
            var rectMask = t.GetComponent<RectMask2D>();
            bool parentHasMask = t.parent != null && (t.parent.GetComponent<Mask>() != null || t.parent.GetComponent<RectMask2D>() != null);
            
            if (hasShadow) shadowCount++;
            if (hasOutline) outlineCount++;
            if (isSuspectName) suspectNameCount++;

            if (hasShadow || hasOutline || isSuspectName || img != null)
            {
                sb.AppendLine($"- Path: {currentPath}");
                if (hasShadow) sb.AppendLine("  - Has Shadow: Yes");
                if (hasOutline) sb.AppendLine("  - Has Outline: Yes");
                if (isSuspectName) sb.AppendLine("  - Suspect Name: Yes");
                if (parentHasMask) sb.AppendLine("  - Parent has Mask/RectMask2D: Yes");
                if (mask != null) sb.AppendLine("  - Has Mask: Yes");
                if (rectMask != null) sb.AppendLine("  - Has RectMask2D: Yes");
                
                if (img != null)
                {
                    sb.AppendLine($"  - Image Sprite: {(img.sprite != null ? img.sprite.name : "null")}");
                    sb.AppendLine($"  - Image Type: {img.type}");
                    sb.AppendLine($"  - Image Color: #{ColorUtility.ToHtmlStringRGBA(img.color)}");
                }
                var rt = t.GetComponent<RectTransform>();
                if (rt != null)
                {
                    sb.AppendLine($"  - Rect Size: {rt.rect.width}x{rt.rect.height}");
                }
                sb.AppendLine();
            }

            foreach (Transform child in t)
            {
                Traverse(child, currentPath);
            }
        }

        Traverse(controller.transform, controller.name);

        sb.AppendLine("=== Summary ===");
        sb.AppendLine($"Shadow components: {shadowCount}");
        sb.AppendLine($"Outline components: {outlineCount}");
        sb.AppendLine($"Suspect names (Shadow/Outline/Border/Glow): {suspectNameCount}");

        string path = "CommonToolsAuditResult.txt";
        File.WriteAllText(path, sb.ToString());
        Debug.Log("Audit completed. Result written to " + path);
    }
}
