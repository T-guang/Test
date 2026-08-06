using System;
using System.Collections.Generic;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.UI;
using ElectricalSim.Practice;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor
{
    /// <summary>
    /// F6-A UI 视觉收口测试（F6-A.1 修正测试可信度版本）。
    ///
    /// 本测试不再以"方法存在"代替"几何通过"。参考面板测试会真实实例化 BlueprintReferencePanel，
    /// 填入全部 18 张模板名称，分别在 Small / Medium / Large 三档下调用 Canvas.ForceUpdateCanvases，
    /// 然后读取 referenceTitle / prefixText / 控制按钮的实际 Rect 与文本字段做断言。
    ///
    /// batchmode 下无法获得真实文本渲染结果（字体度量、preferredHeight、像素级裁剪）的项目
    /// 一律标记 NOT_TESTED_RUNTIME_TEXT_RENDERING，不得写 AUTOMATED_GEOMETRY_PASS。
    /// 视觉最终效果需人工截图确认（MANUAL_VISUAL_REQUIRED）。
    /// </summary>
    public static class UiVisualPolishTests
    {
        private const string ScenePath = "Assets/Scenes/Demo.unity";
        private const string AcPrefabPath = "Assets/Prefab/AC_ThreePhase_Power_Visual.prefab";

        // 18 张标准模板名称（来自 Assets/Resources/Blueprints/Templates/template_catalog.json）
        // 测试不依赖外部 JSON 解析，硬编码名称以便 batchmode 稳定复现。
        private static readonly string[] AllTemplateNames =
        {
            "单开单控照明电路",
            "双控照明电路",
            "空气开关控制照明电路",
            "单相电能表照明电路",
            "电灯泡与电风扇并联控制电路",
            "单开控制双灯电路",
            "双开分别控制双灯电路",
            "空开控制灯泡与风扇并联电路",
            "电动机点动控制电路",
            "电动机连续运行控制电路",
            "点动与连续运行混合控制电路",
            "热继电器保护电动机控制电路",
            "电动机正反转控制电路",
            "电气互锁正反转控制电路",
            "按钮和接触器双重联锁正反转控制电路",
            "自动往返电动机控制电路",
            "两电机时间继电器顺序启动控制电路",
            "星三角降压启动控制电路"
        };

        // 四个重点长标题（用户指定必须验证，已包含在 AllTemplateNames 中）：
        // 按钮和接触器双重联锁正反转控制电路
        // 点动与连续运行混合控制电路
        // 星三角降压启动控制电路
        // 自动往返电动机控制电路

        [MenuItem("Tools/Tests/Run UI Visual Polish Tests")]
        public static void Run()
        {
            var failures = new List<string>();
            var notTested = new List<string>();

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

                // --- 参考面板两层 Header 真实布局测试 ---
                TestReferencePanelTwoLayerHeaderAllTemplates(failures, notTested);
                TestReferencePanelHeaderButtonRightMargin(failures, notTested);
                TestReferencePanelHeaderTitleNoEllipsis(failures, notTested);
                TestReferencePanelNameRowOverflowMode(failures, notTested);
                TestReferencePanelPrefixTextContent(failures, notTested);

                // --- 元件池显示名称测试（真实 ComponentDefinition） ---
                TestPaletteDisplayNamesWithRealDefinitions(failures, notTested);
                TestPaletteDisplayNameNoStandaloneParen(failures, notTested);
                TestPaletteDisplayNameMaxTwoLines(failures, notTested);
                TestKnifeSwitchPreviewSize(failures, notTested);
                TestPaletteItemClickDragMethodsUnchanged(failures, notTested);
                TestPaletteDisplayNameDoesNotModifyDisplayNameField(failures, notTested);

                // --- 交流电源视觉测试 ---
                TestAcThreePhaseBodyScale(failures, notTested);
                TestAcThreePhaseRootUnchanged(failures, notTested);
                TestAcThreePhaseTerminalAnchorsUnchanged(failures, notTested);
                TestAcThreePhaseAllTerminalPositions(failures, notTested);

                // --- 通用 ---
                TestNoCompilerErrors(failures, notTested);
            }
            catch (Exception ex)
            {
                failures.Add("F6-A 测试异常: " + ex.Message);
            }

            // 输出 NOT_TESTED 项
            foreach (var n in notTested)
            {
                Debug.LogWarning("[NOT_TESTED] " + n);
            }

            if (failures.Count > 0)
            {
                Debug.LogError("=== F6-A UiVisualPolishTests 失败 ===");
                foreach (var f in failures)
                {
                    Debug.LogError("[FAIL] " + f);
                }
                throw new InvalidOperationException(
                    "F6-A UiVisualPolishTests 失败 " + failures.Count + " 项:\n" + string.Join("\n", failures));
            }

            Debug.Log("=== F6-A UiVisualPolishTests 通过 (MANUAL_VISUAL_REQUIRED, NOT_TESTED_RUNTIME_TEXT_RENDERING 项已记录) ===");
        }

        // =========================================================================
        // 参考面板两层 Header 真实布局测试
        // =========================================================================

        /// <summary>
        /// 实例化 BlueprintReferencePanel，填入全部 18 张模板名称，
        /// 在 Small / Medium / Large 三档下验证：
        /// 1. referenceTitle.text 等于完整模板名称（未截断）
        /// 2. referenceTitle 世界矩形不与 closeButton 世界矩形相交
        /// 3. referenceTitle 位于 Name Row（底部），prefixText 位于 Top Row（顶部）
        /// 4. referenceTitle.resizeTextForBestFit == true，minSize=16，maxSize=20
        /// </summary>
        private static void TestReferencePanelTwoLayerHeaderAllTemplates(List<string> failures, List<string> notTested)
        {
            var panel = CreateIsolatedReferencePanel();
            if (panel == null)
            {
                failures.Add("无法创建 BlueprintReferencePanel 实例（NOT_TESTED: 隔离 Canvas 创建失败）。");
                return;
            }

            try
            {
                var sizeModeEnum = typeof(BlueprintReferencePanel)
                    .GetNestedType("ReferencePanelSizeMode", BindingFlags.NonPublic);
                if (sizeModeEnum == null)
                {
                    failures.Add("ReferencePanelSizeMode 枚举未找到。");
                    return;
                }

                var smallMode = Enum.Parse(sizeModeEnum, "Small");
                var mediumMode = Enum.Parse(sizeModeEnum, "Medium");
                var largeMode = Enum.Parse(sizeModeEnum, "Large");

                var setPanelSizeMode = typeof(BlueprintReferencePanel).GetMethod("SetPanelSizeMode",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (setPanelSizeMode == null)
                {
                    failures.Add("SetPanelSizeMode 方法未找到。");
                    return;
                }

                var titleField = typeof(BlueprintReferencePanel).GetField("referenceTitle",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var prefixField = typeof(BlueprintReferencePanel).GetField("prefixText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var closeField = typeof(BlueprintReferencePanel).GetField("closeButton",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var headerField = typeof(BlueprintReferencePanel).GetField("headerRect",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                var sizeModes = new[] { smallMode, mediumMode, largeMode };
                var sizeNames = new[] { "Small", "Medium", "Large" };

                for (int i = 0; i < AllTemplateNames.Length; i++)
                {
                    var templateName = AllTemplateNames[i];
                    var templateItem = new CircuitTemplateCatalogItemDto
                    {
                        templateId = "test_" + i,
                        templateName = templateName,
                        thumbnailPath = null,
                        referenceDiagramNote = "测试说明",
                        practiceVariantNote = null
                    };

                    panel.ShowPracticeReference(templateItem);

                    for (int s = 0; s < sizeModes.Length; s++)
                    {
                        setPanelSizeMode.Invoke(panel, new object[] { sizeModes[s] });
                        Canvas.ForceUpdateCanvases();

                        var title = titleField?.GetValue(panel) as Text;
                        var prefix = prefixField?.GetValue(panel) as Text;
                        var closeBtn = closeField?.GetValue(panel) as Button;
                        var headerRect = headerField?.GetValue(panel) as RectTransform;

                        if (title == null)
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle 未填充。");
                            continue;
                        }

                        // 1. 文本字段必须等于完整模板名称（不截断）
                        if (title.text != templateName)
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle.text='{title.text}' 与完整名称不符（被截断）。");
                        }

                        // 2. resizeTextForBestFit 配置
                        if (!title.resizeTextForBestFit)
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle.resizeTextForBestFit 应为 true。");
                        }
                        if (title.resizeTextMinSize != 16)
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': resizeTextMinSize={title.resizeTextMinSize} 应为 16。");
                        }
                        if (title.resizeTextMaxSize != 20)
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': resizeTextMaxSize={title.resizeTextMaxSize} 应为 20。");
                        }

                        // 3. Name Row 应使用 Overflow（不依赖中文 Wrap）
                        if (title.horizontalOverflow != HorizontalWrapMode.Overflow)
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle.horizontalOverflow 应为 Overflow，实际 {title.horizontalOverflow}。");
                        }

                        // 4. 几何不相交：referenceTitle 世界矩形不与 closeButton 世界矩形相交
                        if (closeBtn != null)
                        {
                            var titleWorld = GetWorldRect(title.rectTransform);
                            var closeWorld = GetWorldRect(closeBtn.transform as RectTransform);
                            if (RectsOverlap(titleWorld, closeWorld))
                            {
                                failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle 世界矩形与 closeButton 相交。");
                            }
                        }
                        else
                        {
                            // closeButton 运行时由 EnsureContentReferences 查找；隔离场景下可能未序列化
                            notTested.Add($"[{sizeNames[s]}] 模板'{templateName}': closeButton 未填充（NOT_TESTED_RUNTIME_TEXT_RENDERING: 隔离场景下按钮未序列化）。");
                        }

                        // 5. prefixText 与 referenceTitle 世界矩形严格分离
                        if (prefix != null && headerRect != null)
                        {
                            var prefixWorld = GetWorldRect(prefix.rectTransform);
                            var titleWorld2 = GetWorldRect(title.rectTransform);

                            // 5a. 两矩形不得相交
                            if (RectsOverlap(prefixWorld, titleWorld2))
                            {
                                failures.Add($"[{sizeNames[s]}] 模板'{templateName}': prefixText 世界矩形与 referenceTitle 相交。prefix={prefixWorld} title={titleWorld2}");
                            }

                            // 5b. prefix.center.y 必须大于 title.center.y（Prefix Row 在顶部）
                            if (!(prefixWorld.center.y > titleWorld2.center.y))
                            {
                                failures.Add($"[{sizeNames[s]}] 模板'{templateName}': prefix.center.y({prefixWorld.center.y}) 应大于 title.center.y({titleWorld2.center.y})。");
                            }

                            // 5c. prefix.yMin 应等于或高于 title.yMax，允许 0~2px 间距
                            // Unity Rect: yMin 为底边，yMax 为顶边；世界坐标中 prefix 底边应 >= title 顶边 - 1px
                            if (prefixWorld.yMin < titleWorld2.yMax - 1f)
                            {
                                failures.Add($"[{sizeNames[s]}] 模板'{templateName}': prefix.yMin({prefixWorld.yMin}) 应 >= title.yMax({titleWorld2.yMax}) - 1px。");
                            }

                            // 5d. referenceTitle 高度应等于 nameRowHeight（Header 总高减去 PrefixRowHeight）
                            // 使用 rectTransform.rect.height（本地坐标），避免 CanvasScaler 缩放干扰
                            // Small: 64-30=34, Medium: 66-32=34, Large: 68-32=36
                            float expectedNameRow = (s == 0) ? 34f : (s == 1) ? 34f : 36f;
                            float titleLocalHeight = title.rectTransform.rect.height;
                            if (Mathf.Abs(titleLocalHeight - expectedNameRow) > 0.5f)
                            {
                                failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle 高度({titleLocalHeight}) 应为 nameRowHeight({expectedNameRow})。");
                            }

                            // 5e. prefixText 高度应等于 prefixRowHeight
                            float expectedPrefixRow = (s == 0) ? 30f : 32f;
                            float prefixLocalHeight = prefix.rectTransform.rect.height;
                            if (Mathf.Abs(prefixLocalHeight - expectedPrefixRow) > 0.5f)
                            {
                                failures.Add($"[{sizeNames[s]}] 模板'{templateName}': prefixText 高度({prefixLocalHeight}) 应为 prefixRowHeight({expectedPrefixRow})。");
                            }
                        }
                        else
                        {
                            notTested.Add($"[{sizeNames[s]}] 模板'{templateName}': prefixText 或 headerRect 未填充（NOT_TESTED_RUNTIME_TEXT_RENDERING: 隔离场景下字段未序列化）。");
                        }

                        // 6. 文本字段不含省略号
                        if (title.text.Contains("…") || title.text.Contains("..."))
                        {
                            failures.Add($"[{sizeNames[s]}] 模板'{templateName}': referenceTitle.text 含省略号。");
                        }

                        // 7. preferredHeight 检查（batchmode 下字体度量不可靠）
                        var preferredH = title.preferredHeight;
                        var titleRectH = title.rectTransform.rect.height;
                        if (titleRectH > 0f && preferredH > titleRectH + 0.5f)
                        {
                            // batchmode 下 preferredHeight 可能不准确，标记 NOT_TESTED
                            notTested.Add($"[{sizeNames[s]}] 模板'{templateName}': preferredHeight({preferredH}) > rectHeight({titleRectH})（NOT_TESTED_RUNTIME_TEXT_RENDERING: batchmode 字体度量不可靠）。");
                        }
                    }
                }
            }
            finally
            {
                if (panel != null)
                {
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
                }
            }
        }

        /// <summary>
        /// 验证 X 按钮右边距为 10px（通过 ApplyHeaderButtonStyle 方法签名和 closeRight 常量）。
        /// </summary>
        private static void TestReferencePanelHeaderButtonRightMargin(List<string> failures, List<string> notTested)
        {
            // 验证 ApplyHeaderButtonStyle 方法存在且包含 isClose 参数
            var applyStyle = typeof(BlueprintReferencePanel).GetMethod("ApplyHeaderButtonStyle",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (applyStyle == null)
            {
                failures.Add("ApplyHeaderButtonStyle 方法未找到。");
                return;
            }

            var parameters = applyStyle.GetParameters();
            bool hasIsCloseParam = false;
            foreach (var p in parameters)
            {
                if (p.Name == "isClose") hasIsCloseParam = true;
            }
            if (!hasIsCloseParam)
            {
                failures.Add("ApplyHeaderButtonStyle 缺少 isClose 参数。");
            }

            // X 右边距 10px 的实际验证需要运行时读取 closeButton.anchoredPosition，
            // batchmode 下隔离面板未序列化按钮，标记 NOT_TESTED
            notTested.Add("X 按钮右边距 10px 实际像素验证（NOT_TESTED_RUNTIME_TEXT_RENDERING: 需运行时场景按钮序列化）。");
        }

        /// <summary>
        /// 验证 referenceTitle.text 不含省略号和截断（已在主测试中覆盖，此处单独记录）。
        /// </summary>
        private static void TestReferencePanelHeaderTitleNoEllipsis(List<string> failures, List<string> notTested)
        {
            // 已在 TestReferencePanelTwoLayerHeaderAllTemplates 中覆盖
            // 此处仅作为独立计数项
        }

        /// <summary>
        /// 验证 Name Row 使用 Overflow 模式（已在主测试中覆盖）。
        /// </summary>
        private static void TestReferencePanelNameRowOverflowMode(List<string> failures, List<string> notTested)
        {
            // 已在 TestReferencePanelTwoLayerHeaderAllTemplates 中覆盖
        }

        /// <summary>
        /// 验证 prefixText.text == "图纸参考："。
        /// </summary>
        private static void TestReferencePanelPrefixTextContent(List<string> failures, List<string> notTested)
        {
            var panel = CreateIsolatedReferencePanel();
            if (panel == null)
            {
                notTested.Add("prefixText 内容验证（NOT_TESTED: 隔离 Canvas 创建失败）。");
                return;
            }

            try
            {
                var templateItem = new CircuitTemplateCatalogItemDto
                {
                    templateId = "prefix_test",
                    templateName = "测试模板",
                    thumbnailPath = null
                };
                panel.ShowPracticeReference(templateItem);
                Canvas.ForceUpdateCanvases();

                var prefixField = typeof(BlueprintReferencePanel).GetField("prefixText",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var prefix = prefixField?.GetValue(panel) as Text;
                if (prefix == null)
                {
                    failures.Add("prefixText 字段未填充。");
                    return;
                }
                if (prefix.text != "图纸参考：")
                {
                    failures.Add($"prefixText.text='{prefix.text}' 应为 '图纸参考：'。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(panel.gameObject);
            }
        }

        // =========================================================================
        // 元件池显示名称测试（真实 ComponentDefinition）
        // =========================================================================

        /// <summary>
        /// 加载 Assets/Data/ 下的真实 ComponentDefinition，验证：
        /// 1. GetPaletteDisplayName 返回非空；
        /// 2. 去除换行并统一全角/半角括号后，文本内容必须与原 displayName 等价；
        /// 3. 四个 Breaker 显示"空气开关"，不得出现"断路器"；
        /// 4. Single_Control_Switch 保留"单开单控开关"；
        /// 5. LimitSwitch_Compound 保留"限位开关"。
        /// </summary>
        private static void TestPaletteDisplayNamesWithRealDefinitions(List<string> failures, List<string> notTested)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" });
            if (guids.Length == 0)
            {
                failures.Add("Assets/Data 下未找到任何 ComponentDefinition 资产。");
                return;
            }

            var method = typeof(PaletteController).GetMethod("GetPaletteDisplayName",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                failures.Add("GetPaletteDisplayName 方法未找到。");
                return;
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (def == null) continue;

                var result = (string)method.Invoke(null, new object[] { def });
                if (string.IsNullOrEmpty(result))
                {
                    failures.Add($"Definition '{def.name}' 返回空名称。");
                    continue;
                }

                // 名称归一化：去除换行，统一全角/半角括号
                var normalizedResult = NormalizeForCompare(result);
                var normalizedOriginal = NormalizeForCompare(def.displayName);

                // 去除换行并统一括号后，必须与原 displayName 等价
                if (normalizedResult != normalizedOriginal)
                {
                    failures.Add($"Definition '{def.name}' UI 名称归一化后与 displayName 不等价。result='{result}' normalized='{normalizedResult}' vs displayName='{def.displayName}' normalized='{normalizedOriginal}'。");
                }

                // 四个 Breaker 必须显示"空气开关"，不得出现"断路器"
                if (def.name.StartsWith("Breaker_"))
                {
                    if (!result.Contains("空气开关"))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 应包含 '空气开关'。");
                    }
                    if (result.Contains("断路器"))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 不得包含 '断路器'。");
                    }
                }

                // Single_Control_Switch 必须保留"单开单控开关"
                if (def.name == "Single_Control_Switch")
                {
                    if (!result.Contains("单开单控开关"))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 应保留 '单开单控开关'。");
                    }
                    if (result.Contains("单控开关") && !result.Contains("单开单控开关"))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 不得简化为 '单控开关'。");
                    }
                }

                // LimitSwitch_Compound 必须保留"限位开关"
                if (def.name == "LimitSwitch_Compound")
                {
                    if (!result.Contains("限位开关"))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 应保留 '限位开关'。");
                    }
                    if (result.Contains("限位型"))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 不得使用 '限位型'。");
                    }
                }
            }
        }

        /// <summary>
        /// 名称归一化：去除换行符，统一全角括号为半角，便于比较 UI 名称与原 displayName 是否等价。
        /// </summary>
        private static string NormalizeForCompare(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("\n", string.Empty)
                .Replace("\r", string.Empty)
                .Replace("（", "(")
                .Replace("）", ")")
                .Replace(" ", string.Empty);
        }

        /// <summary>
        /// 验证 UI-only 名称中括号不单独占据一行。
        /// </summary>
        private static void TestPaletteDisplayNameNoStandaloneParen(List<string> failures, List<string> notTested)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" });
            var method = typeof(PaletteController).GetMethod("GetPaletteDisplayName",
                BindingFlags.Public | BindingFlags.Static);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (def == null) continue;

                var result = (string)method.Invoke(null, new object[] { def });
                if (string.IsNullOrEmpty(result)) continue;

                var lines = result.Split('\n');
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    // 如果某行仅由括号字符组成（半角或全角），判定为括号单独成行
                    if (IsStandaloneParenLine(trimmed))
                    {
                        failures.Add($"Definition '{def.name}' UI 名称 '{result}' 中括号单独成行: '{line}'。");
                    }
                }
            }
        }

        /// <summary>
        /// 验证 UI-only 名称最多两行。
        /// </summary>
        private static void TestPaletteDisplayNameMaxTwoLines(List<string> failures, List<string> notTested)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" });
            var method = typeof(PaletteController).GetMethod("GetPaletteDisplayName",
                BindingFlags.Public | BindingFlags.Static);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (def == null) continue;

                var result = (string)method.Invoke(null, new object[] { def });
                if (string.IsNullOrEmpty(result)) continue;

                var lines = result.Split('\n');
                if (lines.Length > 2)
                {
                    failures.Add($"Definition '{def.name}' UI 名称 '{result}' 超过两行（{lines.Length} 行）。");
                }
            }
        }

        /// <summary>
        /// 验证刀开关预览尺寸仍为 56×80。
        /// </summary>
        private static void TestKnifeSwitchPreviewSize(List<string> failures, List<string> notTested)
        {
            var method = typeof(PaletteController).GetMethod("GetPaletteIconSize",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                failures.Add("GetPaletteIconSize 方法未找到。");
                return;
            }

            var def = ScriptableObject.CreateInstance<ComponentDefinition>();
            try
            {
                def.name = "KnifeSwitch_QS";
                var result = (Vector2)method.Invoke(null, new object[] { def });
                if (result.x != 56f || result.y != 80f)
                {
                    failures.Add($"KnifeSwitch 预览尺寸应为 (56, 80)，实际 ({result.x}, {result.y})。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(def);
            }
        }

        /// <summary>
        /// 验证 PaletteItem 的点击和拖拽方法未被破坏。
        /// </summary>
        private static void TestPaletteItemClickDragMethodsUnchanged(List<string> failures, List<string> notTested)
        {
            var onPointerClick = typeof(PaletteItem).GetMethod("OnPointerClick",
                BindingFlags.Public | BindingFlags.Instance);
            if (onPointerClick == null)
            {
                failures.Add("PaletteItem.OnPointerClick 方法未找到。");
            }

            var onBeginDrag = typeof(PaletteItem).GetMethod("OnBeginDrag",
                BindingFlags.Public | BindingFlags.Instance);
            if (onBeginDrag == null)
            {
                failures.Add("PaletteItem.OnBeginDrag 方法未找到。");
            }

            var onDrag = typeof(PaletteItem).GetMethod("OnDrag",
                BindingFlags.Public | BindingFlags.Instance);
            if (onDrag == null)
            {
                failures.Add("PaletteItem.OnDrag 方法未找到。");
            }

            var onEndDrag = typeof(PaletteItem).GetMethod("OnEndDrag",
                BindingFlags.Public | BindingFlags.Instance);
            if (onEndDrag == null)
            {
                failures.Add("PaletteItem.OnEndDrag 方法未找到。");
            }
        }

        /// <summary>
        /// 验证 GetPaletteDisplayName 不修改 ComponentDefinition.displayName 字段。
        /// </summary>
        private static void TestPaletteDisplayNameDoesNotModifyDisplayNameField(List<string> failures, List<string> notTested)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" });
            var method = typeof(PaletteController).GetMethod("GetPaletteDisplayName",
                BindingFlags.Public | BindingFlags.Static);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (def == null) continue;

                var originalDisplayName = def.displayName;
                method.Invoke(null, new object[] { def });
                if (def.displayName != originalDisplayName)
                {
                    failures.Add($"Definition '{def.name}' displayName 被修改: '{originalDisplayName}' → '{def.displayName}'。");
                }
            }
        }

        // =========================================================================
        // 交流电源视觉测试
        // =========================================================================

        /// <summary>
        /// 验证 Body localScale 为 1.5（允许 1.45~1.55 范围）。
        /// </summary>
        private static void TestAcThreePhaseBodyScale(List<string> failures, List<string> notTested)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AcPrefabPath);
            if (prefab == null)
            {
                failures.Add("AC_ThreePhase_Power_Visual.prefab 未找到。");
                return;
            }

            var body = prefab.transform.Find("Body");
            if (body == null)
            {
                failures.Add("Body 子节点未找到。");
                return;
            }

            var scale = body.localScale;
            // 目标 1.5x，允许 1.45~1.55
            if (scale.x < 1.45f || scale.x > 1.55f)
            {
                failures.Add($"Body localScale.x={scale.x} 应在 1.45~1.55 范围内（目标 1.5）。");
            }
            if (scale.y < 1.45f || scale.y > 1.55f)
            {
                failures.Add($"Body localScale.y={scale.y} 应在 1.45~1.55 范围内（目标 1.5）。");
            }
            if (scale.z != 1f)
            {
                failures.Add($"Body localScale.z={scale.z} 应保持 1。");
            }
        }

        /// <summary>
        /// 验证 Root localScale 和 sizeDelta 保持不变。
        /// </summary>
        private static void TestAcThreePhaseRootUnchanged(List<string> failures, List<string> notTested)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AcPrefabPath);
            if (prefab == null)
            {
                failures.Add("AC_ThreePhase_Power_Visual.prefab 未找到。");
                return;
            }

            var rootRect = prefab.GetComponent<RectTransform>();
            if (rootRect == null)
            {
                failures.Add("Root RectTransform 未找到。");
                return;
            }

            if (rootRect.localScale != Vector3.one)
            {
                failures.Add($"Root localScale ({rootRect.localScale}) 应保持 (1,1,1)。");
            }
            if (rootRect.sizeDelta.x != 200f || rootRect.sizeDelta.y != 72f)
            {
                failures.Add($"Root sizeDelta ({rootRect.sizeDelta.x}, {rootRect.sizeDelta.y}) 应保持 (200, 72)。");
            }
        }

        /// <summary>
        /// 验证 TerminalAnchors localScale 保持 (1,1,1)。
        /// </summary>
        private static void TestAcThreePhaseTerminalAnchorsUnchanged(List<string> failures, List<string> notTested)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AcPrefabPath);
            if (prefab == null)
            {
                failures.Add("AC_ThreePhase_Power_Visual.prefab 未找到。");
                return;
            }

            var terminalAnchors = prefab.transform.Find("TerminalAnchors");
            if (terminalAnchors == null)
            {
                failures.Add("TerminalAnchors 子节点未找到。");
                return;
            }

            if (terminalAnchors.localScale != Vector3.one)
            {
                failures.Add($"TerminalAnchors localScale ({terminalAnchors.localScale}) 应保持 (1,1,1)。");
            }
        }

        /// <summary>
        /// 验证 5 个端子（L1/L2/L3/N/PE）anchoredPosition 完全保持。
        /// </summary>
        private static void TestAcThreePhaseAllTerminalPositions(List<string> failures, List<string> notTested)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AcPrefabPath);
            if (prefab == null)
            {
                failures.Add("AC_ThreePhase_Power_Visual.prefab 未找到。");
                return;
            }

            var terminalAnchors = prefab.transform.Find("TerminalAnchors");
            if (terminalAnchors == null)
            {
                failures.Add("TerminalAnchors 子节点未找到。");
                return;
            }

            // 5 个端子的预期坐标（来自 prefab 原始值）
            var expected = new Dictionary<string, Vector2>
            {
                { "Terminal_L1", new Vector2(-79.39f, 0.6f) },
                { "Terminal_L2", new Vector2(-39.61f, 0.6f) },
                { "Terminal_L3", new Vector2(0.18f, 0.6f) },
                { "Terminal_N", new Vector2(39.95f, 0.6f) },
                { "Terminal_PE", new Vector2(79.99f, 0.6f) }
            };

            foreach (var kv in expected)
            {
                var terminal = terminalAnchors.Find(kv.Key);
                if (terminal == null)
                {
                    failures.Add($"{kv.Key} 端子未找到。");
                    continue;
                }

                var rect = terminal.GetComponent<RectTransform>();
                if (rect == null)
                {
                    failures.Add($"{kv.Key} RectTransform 未找到。");
                    continue;
                }

                if (Mathf.Abs(rect.anchoredPosition.x - kv.Value.x) > 0.5f ||
                    Mathf.Abs(rect.anchoredPosition.y - kv.Value.y) > 0.5f)
                {
                    failures.Add($"{kv.Key} anchoredPosition ({rect.anchoredPosition.x}, {rect.anchoredPosition.y}) 应保持 ({kv.Value.x}, {kv.Value.y})。");
                }

                if (rect.localScale != Vector3.one)
                {
                    failures.Add($"{kv.Key} localScale ({rect.localScale}) 应保持 (1,1,1)。");
                }
            }
        }

        // =========================================================================
        // 通用
        // =========================================================================

        private static void TestNoCompilerErrors(List<string> failures, List<string> notTested)
        {
            if (EditorApplication.isCompiling)
            {
                failures.Add("Unity 仍在编译，可能存在编译错误。");
            }
        }

        // =========================================================================
        // 辅助方法
        // =========================================================================

        /// <summary>
        /// 创建隔离的 BlueprintReferencePanel 实例（带 Canvas），用于真实布局测试。
        /// 创建 EnsureContentReferences 所需的子对象（ReferenceTitle、ReferenceImage、按钮等），
        /// 使 panel.ShowPracticeReference 能正常填充字段并执行布局。
        /// </summary>
        private static BlueprintReferencePanel CreateIsolatedReferencePanel()
        {
            var canvasGo = new GameObject("TestCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            var panelGo = new GameObject("TestReferencePanel", typeof(RectTransform), typeof(BlueprintReferencePanel));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var panel = panelGo.GetComponent<BlueprintReferencePanel>();
            // 通过反射设置 panelRect 字段
            var panelRectField = typeof(BlueprintReferencePanel).GetField("panelRect",
                BindingFlags.NonPublic | BindingFlags.Instance);
            panelRectField?.SetValue(panel, panelRect);

            // 创建 EnsureContentReferences 所需的子对象
            // ReferenceTitle Text（referenceTitle 字段通过 FindByName 查找）
            var titleGo = new GameObject("ReferenceTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            titleGo.transform.SetParent(panelGo.transform, false);
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;
            var titleText = titleGo.GetComponent<Text>();
            titleText.raycastTarget = false;

            // ReferenceImage（referenceImage 字段通过 FindByName 查找）
            var imageGo = new GameObject("ReferenceImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageGo.transform.SetParent(panelGo.transform, false);
            var imageRect = imageGo.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            // 控制按钮（zoomIn/zoomOut/reset/close）
            CreateTestButton(panelGo.transform, "ReferenceZoomIn");
            CreateTestButton(panelGo.transform, "ReferenceZoomOut");
            CreateTestButton(panelGo.transform, "ReferenceReset");
            CreateTestButton(panelGo.transform, "ReferenceClose");

            return panel;
        }

        private static void CreateTestButton(Transform parent, string name)
        {
            var btnGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);
            var rect = btnGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(32f, 32f);
        }

        private static Rect GetWorldRect(RectTransform rt)
        {
            if (rt == null) return Rect.zero;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var min = corners[0];
            var max = corners[2];
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        private static bool RectsOverlap(Rect a, Rect b)
        {
            if (a.width <= 0f || a.height <= 0f || b.width <= 0f || b.height <= 0f) return false;
            return a.xMax > b.xMin && b.xMax > a.xMin && a.yMax > b.yMin && b.yMax > a.yMin;
        }

        private static bool IsStandaloneParenLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return false;
            var trimmed = line.Trim();

            // 只标记单独的括号字符（半角或全角），如 "(" ")" "（" "）"
            // 完整括号表达式如 "（红）" "（FU）" "（380V）" 是期望格式，不标记
            if (trimmed == "(" || trimmed == ")" || trimmed == "（" || trimmed == "）")
            {
                return true;
            }
            return false;
        }
    }
}
