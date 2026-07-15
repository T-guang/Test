using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ElectricalSim.UI.CommonTools;

public static class PhaseA_Auditor
{
    private static void GenerateAudit(string filename)
    {
        var controller = Object.FindObjectOfType<CommonToolsPageController>(true);
        if (controller == null)
        {
            var go = new GameObject("TestCommonTools");
            controller = go.AddComponent<CommonToolsPageController>();
            // Add a mock TopNavigationController or PageRouter if needed, but CommonToolsPageController works standalone mostly
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
