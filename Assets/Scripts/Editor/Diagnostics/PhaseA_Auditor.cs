using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ElectricalSim.UI.CommonTools;

/// <summary>
/// 仅在 Unity Editor 中导出 Phase A 常用工具页面视觉组件审计 CSV 的辅助工具。
/// 两个菜单入口 <c>Electrical/Diagnostics/Phase A Before Audit</c> 与 <c>Electrical/Diagnostics/Phase A Final Audit</c>
/// 都会查找当前加载场景中的 CommonToolsPageController；缺失时当前实现创建 <c>TestCommonTools</c>、调用 BuildPage 和布局刷新，
/// 再遍历 Image、Shadow、Outline、Mask 及 RectTransform 信息，写入项目根目录 <c>Reports/UI</c> 下的指定 UTF-8 BOM CSV。
///
/// 它不使用 AssetDatabase、不保存场景、不修改模板或 Build Settings，也不是 Windows Player 功能；但创建测试对象和重建页面会
/// 影响当前内存场景，且该类不负责清理。审计输出是当前 Editor 内存场景中的动态 UI 层级快照，不替代人工回归、Build 或 Player 验证。
/// 新增常用工具模块、组件类型或审计字段时，需同步审查遍历范围和 CSV Schema；未处理的写入或页面构建异常会中断调用并由 Console 定位。
/// </summary>
public static class PhaseA_Auditor
{
    private static void GenerateAudit(string filename)
    {
        // 入口共用此生成路径；它会覆盖同名审计 CSV，运行前需确认报告目录中的既有证据是否应保留。
        var controller = Object.FindObjectOfType<CommonToolsPageController>(true);
        if (controller == null)
        {
            var go = new GameObject("TestCommonTools");
            controller = go.AddComponent<CommonToolsPageController>();
            // 缺少控制器时仅创建最小测试宿主；本工具不额外注入导航控制器，
            // 页面构建能力以当前 CommonToolsPageController.BuildPage 实现为准。
        }

        controller.gameObject.SetActive(true);
        controller.BuildPage();

        // Canvas.ForceUpdateCanvases() and layout rebuild
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(controller.GetComponent<RectTransform>());

        string dir = Path.Combine(Application.dataPath, "../Reports/UI");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        
        string path = Path.Combine(dir, filename);
        
        // UTF-8 BOM
        var utf8BOM = new UTF8Encoding(true);
        using (StreamWriter writer = new StreamWriter(path, false, utf8BOM))
        {
            writer.WriteLine("GameObjectInstanceId,ComponentInstanceId,RootInstanceId,HierarchyPath,GameObject.name,ActiveSelf,ActiveInHierarchy,HasShadow,HasOutline,Shadow.effectColor,Shadow.effectDistance,Shadow.useGraphicAlpha,Image.sprite.name,Image.type,Image.color,RectWidth,RectHeight,anchorMin,anchorMax,offsetMin,offsetMax,localScale,ParentHasMask,ParentHasRectMask2D,NameContainsShadowOutlineBorderGlow");

            var visitedComponents = new HashSet<int>();
            int rootInstanceId = controller.gameObject.GetInstanceID();

            void Traverse(Transform t, string currentPath)
            {
                var fullPath = currentPath + "/" + t.name;
                
                var shadows = t.GetComponents<Shadow>();
                bool hasOutline = false;
                bool hasShadow = false;
                string effectColor = "";
                string effectDistance = "";
                string useAlpha = "";

                Shadow primaryShadow = null;
                foreach (var s in shadows)
                {
                    if (visitedComponents.Contains(s.GetInstanceID())) continue;
                    visitedComponents.Add(s.GetInstanceID());
                    
                    if (s is Outline) hasOutline = true;
                    else hasShadow = true;
                    
                    if (primaryShadow == null) primaryShadow = s;
                }

                if (primaryShadow != null)
                {
                    effectColor = "#" + ColorUtility.ToHtmlStringRGBA(primaryShadow.effectColor);
                    effectDistance = primaryShadow.effectDistance.ToString();
                    useAlpha = primaryShadow.useGraphicAlpha.ToString();
                }

                var img = t.GetComponent<Image>();
                string spriteName = img != null && img.sprite != null ? img.sprite.name : "null";
                string imgType = img != null ? img.type.ToString() : "null";
                string imgColor = img != null ? "#" + ColorUtility.ToHtmlStringRGBA(img.color) : "null";

                var rt = t.GetComponent<RectTransform>();
                string width = rt != null ? rt.rect.width.ToString() : "";
                string height = rt != null ? rt.rect.height.ToString() : "";
                string aMin = rt != null ? rt.anchorMin.ToString() : "";
                string aMax = rt != null ? rt.anchorMax.ToString() : "";
                string oMin = rt != null ? rt.offsetMin.ToString() : "";
                string oMax = rt != null ? rt.offsetMax.ToString() : "";
                string localScale = rt != null ? rt.localScale.ToString() : "";

                bool pMask = t.parent != null && t.parent.GetComponent<Mask>() != null;
                bool pRectMask = t.parent != null && t.parent.GetComponent<RectMask2D>() != null;
                
                string lowerName = t.name.ToLower();
                bool isSuspect = lowerName.Contains("shadow") || lowerName.Contains("glow") || lowerName.Contains("outline") || lowerName.Contains("border");

                if (primaryShadow != null || isSuspect || img != null)
                {
                    int goId = t.gameObject.GetInstanceID();
                    int compId = primaryShadow != null ? primaryShadow.GetInstanceID() : 0;
                    string csvLine = $"{goId},{compId},{rootInstanceId},\"{fullPath}\",\"{t.name}\",{t.gameObject.activeSelf},{t.gameObject.activeInHierarchy},{hasShadow},{hasOutline},\"{effectColor}\",\"{effectDistance}\",{useAlpha},\"{spriteName}\",{imgType},\"{imgColor}\",{width},{height},\"{aMin}\",\"{aMax}\",\"{oMin}\",\"{oMax}\",\"{localScale}\",{pMask},{pRectMask},{isSuspect}";
                    writer.WriteLine(csvLine);
                }

                foreach (Transform child in t)
                {
                    Traverse(child, fullPath);
                }
            }

            Traverse(controller.transform, controller.name);
        }
        Debug.Log($"Generated {filename}");
    }

    [MenuItem("Electrical/Diagnostics/Phase A Before Audit")]
    public static void RunBeforeAudit()
    {
        GenerateAudit("CommonToolsShadowAudit_Before.csv");
    }

    [MenuItem("Electrical/Diagnostics/Phase A Final Audit")]
    public static void RunFinalAudit()
    {
        GenerateAudit("CommonToolsShadowAudit_Final.csv");
    }
}
