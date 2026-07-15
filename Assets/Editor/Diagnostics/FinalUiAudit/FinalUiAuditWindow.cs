using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ElectricalSim.UI;
using ElectricalSim.UI.CommonTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Editor.Diagnostics.FinalUiAudit
{
    /// <summary>Read-only final audit. It never modifies production UI, scenes, prefabs, or page state.</summary>
    public sealed class FinalUiAuditWindow : EditorWindow
    {
        private static readonly Vector2Int[] AllowedResolutions = { new Vector2Int(3840, 2160), new Vector2Int(1920, 1080), new Vector2Int(1366, 768) };
        private static readonly string[] PageIds = { "Simulation", "Blueprint", "Gallery", "Encyclopedia", "Tools", "SystemInfo" };
        private static readonly string[] ScreenshotNames =
        {
            "Simulation_Default", "Simulation_InspectorReport", "Simulation_LogFilled", "Blueprint_Industrial_Page1", "Blueprint_Family", "Blueprint_LongTitles", "Blueprint_Page2",
            "Gallery_List", "Gallery_LongTitles", "Gallery_Detail", "Encyclopedia_All", "Encyclopedia_LongTerminal", "Encyclopedia_Search", "Encyclopedia_Detail",
            "Tools_ColorCode", "Tools_Calculator", "Tools_Formula", "Tools_Reference", "SystemInfo"
        };
        private static readonly List<TextRow> Texts = new List<TextRow>();
        private static readonly List<LayoutRow> Layouts = new List<LayoutRow>();
        private static readonly List<PageRow> Pages = new List<PageRow>();
        private static readonly List<EffectRow> Effects = new List<EffectRow>();
        private static readonly List<IssueRow> Issues = new List<IssueRow>();
        private static int playFrames;
        private int pageIndex, screenshotIndex;
        private string status = "进入 Play Mode，等待页面稳定至少 3 帧后采集。";

        [MenuItem("Electrical/Diagnostics/全项目 UI 最终巡检")]
        private static void Open() => GetWindow<FinalUiAuditWindow>("全项目 UI 最终巡检").Show();
        private void OnEnable() => EditorApplication.update += TrackFrames;
        private void OnDisable() => EditorApplication.update -= TrackFrames;
        private void TrackFrames() { playFrames = Application.isPlaying ? playFrames + 1 : 0; }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("全项目 UI 最终巡检（只读）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("手动切换页面/状态后采集。工具不会点击业务按钮、修改场景、保存 Prefab 或自动修复问题。", MessageType.Info);
            var size = GetGameViewSize();
            EditorGUILayout.LabelField("Game View 选择分辨率", size.x + " × " + size.y);
            EditorGUILayout.LabelField("Screen API（诊断）", Screen.width + " × " + Screen.height);
            EditorGUILayout.LabelField("Play Mode 稳定帧", playFrames.ToString());
            pageIndex = EditorGUILayout.Popup("当前页面", pageIndex, PageIds);
            screenshotIndex = EditorGUILayout.Popup("当前截图名称", screenshotIndex, ScreenshotNames);
            using (new EditorGUI.DisabledScope(!CanCapture(size, false)))
            {
                if (GUILayout.Button("采集当前页面")) CapturePage(PageIds[pageIndex], size);
                if (GUILayout.Button("截图当前 Game View")) CaptureScreenshot(size, ScreenshotNames[screenshotIndex]);
            }
            if (GUILayout.Button("导出 CSV 与 Markdown 总报告")) ExportAll();
            if (GUILayout.Button("生成关键功能人工回归清单")) WriteChecklist();
            if (GUILayout.Button("打包审计结果")) PackageResults();
            if (GUILayout.Button("打开审计目录")) { Directory.CreateDirectory(OutputRoot); EditorUtility.RevealInFinder(OutputRoot); }
            EditorGUILayout.HelpBox(status, MessageType.None);
            EditorGUILayout.LabelField("已采集：页面 " + Pages.Count + "，Text " + Texts.Count + "，布局 " + Layouts.Count + "，问题 " + Issues.Count);
        }

        private static bool CanCapture(Vector2Int size, bool report)
        {
            if (!Application.isPlaying) { if (report) SetStatus("巡检只能在 Play Mode 执行。"); return false; }
            if (playFrames < 3) { if (report) SetStatus("请等待页面稳定至少 3 帧后再采集。"); return false; }
            if (!AllowedResolutions.Any(item => item == size)) { if (report) SetStatus("请切换到 3840×2160、1920×1080 或 1366×768。"); return false; }
            return true;
        }

        private static void CapturePage(string pageId, Vector2Int size)
        {
            if (!CanCapture(size, true)) return;
            var root = FindRoot(pageId);
            if (root == null) { SetStatus("未找到 " + pageId + " 当前页面根。请先手动切换到该顶部页面。"); return; }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            Canvas.ForceUpdateCanvases();
            var canvas = root.GetComponentInParent<Canvas>();
            var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
            var title = root.GetComponentsInChildren<Text>(true).FirstOrDefault(item => item != null && item.gameObject.activeInHierarchy && item.fontSize >= 20);
            var page = new PageRow
            {
                resolution = Res(size), pageId = pageId, rootPath = PathOf(root), active = root.gameObject.activeInHierarchy, title = title != null ? Clean(title.text) : string.Empty,
                rootRect = Rect(root.rect), visibleRect = canvas != null ? Rect(canvas.pixelRect) : string.Empty, scrolls = root.GetComponentsInChildren<ScrollRect>(true).Length,
                texts = root.GetComponentsInChildren<Text>(true).Length, buttons = root.GetComponentsInChildren<Button>(true).Length, inputs = root.GetComponentsInChildren<InputField>(true).Length,
                dropdowns = root.GetComponentsInChildren<Dropdown>(true).Length, shadows = root.GetComponentsInChildren<Shadow>(true).Length, outlines = root.GetComponentsInChildren<Outline>(true).Length,
                canvasScale = canvas != null ? canvas.scaleFactor : 0f, scaleMode = scaler != null ? scaler.uiScaleMode.ToString() : "<none>",
                referenceResolution = scaler != null ? scaler.referenceResolution.ToString() : "<none>", screenMatch = scaler != null ? scaler.screenMatchMode.ToString() : "<none>",
                match = scaler != null ? scaler.matchWidthOrHeight : 0f, referencePixelsPerUnit = scaler != null ? scaler.referencePixelsPerUnit : 0f
            };
            Pages.RemoveAll(item => item.resolution == page.resolution && item.pageId == pageId);
            Pages.Add(page);
            foreach (var text in root.GetComponentsInChildren<Text>(true)) CaptureText(pageId, size, canvas, text);
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true)) CaptureLayout(pageId, size, canvas, rect);
            CaptureEffects(pageId, size, root);
            SetStatus("已采集 " + pageId + "：Text " + page.texts + "，Button " + page.buttons + "。");
        }

        private static void CaptureText(string page, Vector2Int size, Canvas canvas, Text text)
        {
            if (text == null) return;
            var rect = text.rectTransform.rect;
            var valid = rect.width > 0f && rect.height > 0f;
            var preferredWidth = 0f; var preferredHeight = 0f; var lines = 0;
            if (valid)
            {
                var generator = new TextGenerator();
                generator.Populate(text.text ?? string.Empty, text.GetGenerationSettings(rect.size));
                preferredWidth = text.preferredWidth; preferredHeight = text.preferredHeight; lines = generator.lineCount;
            }
            var singleLine = text.horizontalOverflow == HorizontalWrapMode.Overflow;
            var clipped = valid && (singleLine ? preferredWidth > rect.width : preferredHeight > rect.height);
            var offscreen = IsOffscreen(canvas, text.rectTransform);
            var issue = EvaluateText(text, valid, clipped, offscreen);
            Texts.Add(new TextRow
            {
                resolution = Res(size), pageId = page, path = PathOf(text.transform), text = Clean(text.text), activeSelf = text.gameObject.activeSelf, active = text.gameObject.activeInHierarchy,
                font = Font(text.font), size = text.fontSize, style = text.fontStyle.ToString(), color = "#" + ColorUtility.ToHtmlStringRGBA(text.color), alignment = text.alignment.ToString(),
                lineSpacing = text.lineSpacing, horizontal = text.horizontalOverflow.ToString(), vertical = text.verticalOverflow.ToString(), bestFit = text.resizeTextForBestFit,
                bestFitMin = text.resizeTextMinSize, bestFitMax = text.resizeTextMaxSize, rectWidth = rect.width, rectHeight = rect.height, preferredWidth = preferredWidth,
                preferredHeight = preferredHeight, widthUsage = valid ? preferredWidth / rect.width : 0f, heightUsage = valid ? preferredHeight / rect.height : 0f, lines = lines,
                localScale = text.rectTransform.localScale.ToString(), worldScale = text.rectTransform.lossyScale.ToString(), button = Parent<Button>(text.transform), scroll = Parent<ScrollRect>(text.transform),
                layout = FindLayout(text.transform), offscreen = offscreen, clipped = clipped, lowContrast = text.color.a < .45f, risk = issue.level, reason = issue.reason
            });
            if (issue.level != "None") AddIssue(size, page, issue.level, PathOf(text.transform), issue.reason, "Inspect font role, text rect, layout, or contrast; do not auto-fix.");
        }

        private static TextIssue EvaluateText(Text text, bool valid, bool clipped, bool offscreen)
        {
            if (!valid) return new TextIssue("P2", "InvalidRect: width or height is zero.");
            if (!text.gameObject.activeInHierarchy || Allowlisted(text)) return new TextIssue("None", string.Empty);
            if (text.font == null) return new TextIssue("P1", "Visible text has no resolved font.");
            if (Font(text.font).Contains("LegacyRuntime")) return new TextIssue("P1", "Visible text uses LegacyRuntime.");
            if (text.fontStyle == FontStyle.Bold && !ReferenceEquals(text.font, MainUiTheme.UiFontBold)) return new TextIssue("P2", "Synthetic FontStyle.Bold is used.");
            if (text.resizeTextForBestFit && text.resizeTextMinSize < 13) return new TextIssue("P1", "BestFit minimum size is below 13.");
            if (text.resizeTextForBestFit && IsFixedButtonText(text)) return new TextIssue("P2", "Fixed interactive text uses BestFit outside the allowlist.");
            if (text.fontSize < 13) return new TextIssue("P1", "Visible text fontSize is below 13.");
            if (text.fontSize < 14 && !SmallTextAllowlisted(text)) return new TextIssue("P2", "Visible text fontSize is below 14.");
            if (clipped) return new TextIssue("P1", "Preferred text size exceeds active text rect.");
            if (offscreen) return new TextIssue("P2", "Active text is outside canvas bounds.");
            if (text.color.a < .45f) return new TextIssue("P2", "Visible text alpha is below 0.45.");
            return new TextIssue("None", string.Empty);
        }

        private static void CaptureLayout(string page, Vector2Int size, Canvas canvas, RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return;
            var row = new LayoutRow { resolution = Res(size), pageId = page, path = PathOf(rect), active = true, rect = Rect(rect.rect), localScale = rect.localScale.ToString(), worldScale = rect.lossyScale.ToString(), offscreen = IsOffscreen(canvas, rect) };
            if (rect.rect.width <= 0 || rect.rect.height <= 0) { row.risk = "P2"; row.reason = "Active RectTransform has non-positive size."; }
            else if (rect.localScale != Vector3.one) { row.risk = "P3"; row.reason = "Local scale differs from one."; }
            else if (row.offscreen) { row.risk = "P2"; row.reason = "Active RectTransform is outside canvas bounds."; }
            else row.risk = "None";
            Layouts.Add(row);
            if (row.risk != "None") AddIssue(size, page, row.risk, row.path, row.reason, "Inspect anchors, parent layout, or viewport clipping.");
        }

        private static void CaptureEffects(string page, Vector2Int size, RectTransform root)
        {
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null) continue;
                var type = component.GetType().Name;
                if (!(component is Shadow) && !(component is Outline) && !EffectName(type) && !EffectName(component.gameObject.name)) continue;
                var row = new EffectRow { resolution = Res(size), pageId = page, path = PathOf(component.transform), component = type, objectName = component.gameObject.name, active = component.gameObject.activeInHierarchy };
                Effects.Add(row);
                if (page == "Tools" && component is Shadow && row.active) AddIssue(size, page, "P1", row.path, "CommonTools contains active Shadow; expected zero.", "Remove only the local CommonTools shadow after approval.");
            }
        }

        private static void CaptureScreenshot(Vector2Int size, string name)
        {
            if (!CanCapture(size, true)) return;
            var directory = ResolutionDirectory(size); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path, 1);
            SetStatus("已请求真实 Game View 截图：" + path + "。请在当前帧结束后确认文件已写入。");
        }

        private static void ExportAll()
        {
            Directory.CreateDirectory(OutputRoot);
            WriteCsv(Path.Combine(OutputRoot, "FinalUiAudit_AllText.csv"), TextHeader(), Texts.Select(TextCsv));
            WriteCsv(Path.Combine(OutputRoot, "FinalUiAudit_VisibleText.csv"), TextHeader(), Texts.Where(item => item.active).Select(TextCsv));
            WriteCsv(Path.Combine(OutputRoot, "FinalUiAudit_Layout.csv"), "Resolution,PageId,HierarchyPath,Active,Rect,LocalScale,WorldScale,IsOffscreen,Risk,Reason", Layouts.Select(LayoutCsv));
            WriteCsv(Path.Combine(OutputRoot, "FinalUiAudit_ShadowOutline.csv"), "Resolution,PageId,HierarchyPath,Component,ObjectName,Active", Effects.Select(EffectCsv));
            WriteCsv(Path.Combine(OutputRoot, "FinalUiAudit_Issues.csv"), "Resolution,PageId,Level,HierarchyPath,Issue,RecommendedScope", Issues.Select(IssueCsv));
            var fontRows = Texts.GroupBy(item => item.font + "|" + item.active).Select(group => Csv(group.Key.Split('|')[0], group.Key.Split('|')[1], group.Count(), string.Join(" | ", group.Select(item => item.pageId).Distinct())));
            WriteCsv(Path.Combine(OutputRoot, "FinalUiAudit_FontSummary.csv"), "FontName,ActiveInHierarchy,TextCount,Pages", fontRows);
            WriteReport(); WriteChecklist();
            SetStatus("已导出 CSV、Markdown 总报告与人工回归清单。问题只记录，不自动修复。");
        }

        private static void WriteReport()
        {
            var report = new StringBuilder("# 全项目 UI 最终巡检报告\n\n## 执行环境\n");
            report.AppendLine("- Unity: " + Application.unityVersion);
            report.AppendLine("- 已采集页面: " + Pages.Count + "；Text: " + Texts.Count + "；问题: " + Issues.Count);
            report.AppendLine("\n## 分辨率与页面状态");
            foreach (var page in Pages.OrderBy(item => item.resolution).ThenBy(item => item.pageId)) report.AppendLine("- " + page.resolution + " / " + page.pageId + ": root=" + page.rootPath + ", Text=" + page.texts + ", Button=" + page.buttons + ", Shadow=" + page.shadows + ", Outline=" + page.outlines);
            report.AppendLine("\n## 字体命中统计");
            foreach (var group in Texts.Where(item => item.active).GroupBy(item => item.font).OrderByDescending(item => item.Count())) report.AppendLine("- " + group.Key + ": " + group.Count());
            report.AppendLine("\n## LegacyRuntime / BodyFont 残留");
            var legacy = Texts.Where(item => item.active && item.font.Contains("LegacyRuntime")).ToList();
            report.AppendLine(legacy.Count == 0 ? "- 未发现 ActiveInHierarchy LegacyRuntime 文本。" : "- LegacyRuntime: " + legacy.Count);
            var bodyName = Font(MainUiTheme.BodyFont);
            report.AppendLine("- 当前 BodyFont 实际名称: " + bodyName + "；命中数: " + Texts.Count(item => item.active && item.font == bodyName));
            report.AppendLine("\n## BestFit、裁切与越界");
            report.AppendLine("- BestFit 启用: " + Texts.Count(item => item.active && item.bestFit));
            report.AppendLine("- 裁切风险: " + Texts.Count(item => item.active && item.clipped));
            report.AppendLine("- 越界风险: " + Layouts.Count(item => item.offscreen));
            report.AppendLine("\n## Shadow / Outline");
            foreach (var group in Effects.GroupBy(item => item.pageId).OrderBy(item => item.Key)) report.AppendLine("- " + group.Key + ": Shadow=" + group.Count(item => item.component == "Shadow") + "，Outline=" + group.Count(item => item.component == "Outline"));
            report.AppendLine("\n## 问题分级");
            foreach (var level in new[] { "P0", "P1", "P2", "P3" })
            {
                report.AppendLine("### " + level);
                var items = Issues.Where(item => item.level == level).ToList();
                if (items.Count == 0) report.AppendLine("- 无");
                foreach (var item in items) report.AppendLine("- [" + item.pageId + "] " + item.path + "：" + item.issue + " 建议范围：" + item.scope);
            }
            report.AppendLine("\n## 结论");
            report.AppendLine(Issues.Any(item => item.level == "P0" || item.level == "P1") ? "存在 P0/P1 项：请按定点清单确认后再修改。" : "当前采集范围内无 P0/P1：UI 可冻结，允许进入图片资源补齐和发布前回归。");
            report.AppendLine("功能回归以 ManualFunctionalChecklist.md 的人工填写结果为准，工具不会自动声明通过。");
            File.WriteAllText(Path.Combine(OutputRoot, "FinalUiAudit_Report.md"), report.ToString(), new UTF8Encoding(true));
        }

        private static void WriteChecklist()
        {
            Directory.CreateDirectory(OutputRoot);
            var sections = new Dictionary<string, string[]>
            {
                { "模拟电路", new[] { "开始 / 停止仿真", "撤销", "重做", "删除", "清线", "清空", "锁定", "更新布局", "加载模板", "保存图纸", "导入图纸", "检查当前电路", "清空结果", "六个顶部导航" } },
                { "图纸集", new[] { "工业 / 家庭", "难度筛选", "搜索", "分页", "进入练习" } },
                { "仿真广场", new[] { "筛选", "搜索", "排序", "查看详情", "返回", "加载案例", "加载到画布" } },
                { "百科", new[] { "分类", "搜索名称", "搜索端子", "搜索用途", "详情", "关闭详情", "滚动" } },
                { "常用工具", new[] { "四色环 / 五色环", "色环位置和颜色", "常用示例", "参数估算全部 Tab", "InputField", "Dropdown", "开始估算", "公式分类和条目", "基础资料", "滚动" } },
                { "系统信息", new[] { "打开图纸文件夹", "刷新信息", "打开数据目录", "清理缓存入口不存在" } }
            };
            var output = new StringBuilder("# UI 最终巡检 - 关键功能人工回归清单\n\n");
            foreach (var section in sections) { output.AppendLine("## " + section.Key + "\n| 项目 | 状态 | Notes |\n| --- | --- | --- |"); foreach (var item in section.Value) output.AppendLine("| " + item + " | Not Tested | |"); output.AppendLine(); }
            File.WriteAllText(Path.Combine(OutputRoot, "ManualFunctionalChecklist.md"), output.ToString(), new UTF8Encoding(true));
        }

        private static void PackageResults()
        {
            if (!Directory.Exists(OutputRoot)) { SetStatus("请先导出审计结果，再打包。"); return; }
            var zipPath = OutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".zip";
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(OutputRoot, zipPath, System.IO.Compression.CompressionLevel.Optimal, false);
            SetStatus("已打包审计结果：" + zipPath);
        }

        private static RectTransform FindRoot(string id)
        {
            Component component = id == "Blueprint" ? FindObjectOfType<BlueprintController>() : id == "Gallery" ? FindObjectOfType<SimulationGalleryPageController>() : id == "Encyclopedia" ? FindObjectOfType<EncyclopediaController>() : id == "Tools" ? FindObjectOfType<CommonToolsPageController>() : id == "SystemInfo" ? FindObjectOfType<LocalProfilePageController>() : FindObjectOfType<DemoUIController>();
            return component != null ? component.transform as RectTransform : null;
        }
        private static bool Allowlisted(Text text) { var path = PathOf(text.transform); return path.Contains("Diagnostics") || path.Contains("__SimulationUiFontComparison") || path.Contains("FinalUiAudit"); }
        private static bool SmallTextAllowlisted(Text text) { var path = PathOf(text.transform); return text.resizeTextForBestFit && text.resizeTextMinSize >= 14 && (path.Contains("Palette") || path.Contains("SubmitPractice")); }
        private static bool IsFixedButtonText(Text text) { var path = PathOf(text.transform); return text.GetComponentInParent<Button>() != null && !(path.Contains("Palette") || path.Contains("SubmitPractice")); }
        private static bool IsOffscreen(Canvas canvas, RectTransform rect) { if (canvas == null || rect == null) return false; var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvas.transform, rect); var visible = (canvas.transform as RectTransform).rect; return bounds.max.x < visible.xMin || bounds.min.x > visible.xMax || bounds.max.y < visible.yMin || bounds.min.y > visible.yMax; }
        private static string Parent<T>(Transform transform) where T : Component { var parent = transform.GetComponentInParent<T>(); return parent != null ? PathOf(parent.transform) : string.Empty; }
        private static string FindLayout(Transform transform) { for (var current = transform.parent; current != null; current = current.parent) { if (current.GetComponent<HorizontalLayoutGroup>() != null) return "Horizontal:" + PathOf(current); if (current.GetComponent<VerticalLayoutGroup>() != null) return "Vertical:" + PathOf(current); if (current.GetComponent<GridLayoutGroup>() != null) return "Grid:" + PathOf(current); } return string.Empty; }
        private static bool EffectName(string value) { var text = (value ?? string.Empty).ToLowerInvariant(); return text.Contains("shadow") || text.Contains("glow") || text.Contains("outline"); }
        private static void AddIssue(Vector2Int size, string page, string level, string path, string issue, string scope) => Issues.Add(new IssueRow { resolution = Res(size), pageId = page, level = level, path = path, issue = issue, scope = scope });
        private static Vector2Int GetGameViewSize()
        {
            try { var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"); var view = type?.GetMethod("GetMainGameView", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null) ?? Resources.FindObjectsOfTypeAll(type).FirstOrDefault(); var gameSize = type?.GetProperty("currentGameViewSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(view, null); var width = Convert.ToInt32(gameSize?.GetType().GetProperty("width")?.GetValue(gameSize, null)); var height = Convert.ToInt32(gameSize?.GetType().GetProperty("height")?.GetValue(gameSize, null)); return width > 0 && height > 0 ? new Vector2Int(width, height) : new Vector2Int(Screen.width, Screen.height); } catch { return new Vector2Int(Screen.width, Screen.height); }
        }
        private static string OutputRoot => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Reports", "UI", "FinalUiAudit");
        private static string ResolutionDirectory(Vector2Int size) => Path.Combine(OutputRoot, Res(size));
        private static string Res(Vector2Int size) => size.x + "x" + size.y;
        private static string PathOf(Transform transform) { if (transform == null) return string.Empty; var parts = new Stack<string>(); for (var current = transform; current != null; current = current.parent) parts.Push(current.name); return string.Join("/", parts.ToArray()); }
        private static string Font(Font font) => font == null ? "<null>" : font.name;
        private static string Rect(Rect rect) => rect.x.ToString("0.##") + "," + rect.y.ToString("0.##") + "," + rect.width.ToString("0.##") + "," + rect.height.ToString("0.##");
        private static string Clean(string text) => (text ?? string.Empty).Replace("\r", "").Replace("\n", "\\n");
        private static string Csv(params object[] values) => string.Join(",", values.Select(value => "\"" + (value ?? string.Empty).ToString().Replace("\"", "\"\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\""));
        private static void WriteCsv(string path, string header, IEnumerable<string> rows) { Directory.CreateDirectory(OutputRoot); File.WriteAllText(path, header + "\r\n" + string.Join("\r\n", rows), new UTF8Encoding(true)); }
        private static string TextHeader() => "Resolution,PageId,HierarchyPath,TextContent,ActiveSelf,ActiveInHierarchy,FontName,FontSize,FontStyle,Color,Alignment,LineSpacing,HorizontalOverflow,VerticalOverflow,ResizeTextForBestFit,ResizeTextMinSize,ResizeTextMaxSize,RectWidth,RectHeight,PreferredWidth,PreferredHeight,WidthUsageRatio,HeightUsageRatio,GeneratedLineCount,LocalScale,WorldScale,ParentButton,ParentScrollRect,ParentLayoutGroup,IsOffscreen,IsClippedRisk,IsLowContrastRisk,AuditRiskLevel,AuditReason";
        private static string TextCsv(TextRow row) => Csv(row.resolution, row.pageId, row.path, row.text, row.activeSelf, row.active, row.font, row.size, row.style, row.color, row.alignment, row.lineSpacing, row.horizontal, row.vertical, row.bestFit, row.bestFitMin, row.bestFitMax, row.rectWidth, row.rectHeight, row.preferredWidth, row.preferredHeight, row.widthUsage, row.heightUsage, row.lines, row.localScale, row.worldScale, row.button, row.scroll, row.layout, row.offscreen, row.clipped, row.lowContrast, row.risk, row.reason);
        private static string LayoutCsv(LayoutRow row) => Csv(row.resolution, row.pageId, row.path, row.active, row.rect, row.localScale, row.worldScale, row.offscreen, row.risk, row.reason);
        private static string EffectCsv(EffectRow row) => Csv(row.resolution, row.pageId, row.path, row.component, row.objectName, row.active);
        private static string IssueCsv(IssueRow row) => Csv(row.resolution, row.pageId, row.level, row.path, row.issue, row.scope);
        private static void SetStatus(string value) { var window = GetWindow<FinalUiAuditWindow>(); window.status = value; window.Repaint(); Debug.Log("[FinalUiAudit] " + value); }

        private sealed class TextIssue { public readonly string level, reason; public TextIssue(string level, string reason) { this.level = level; this.reason = reason; } }
        private sealed class TextRow { public string resolution, pageId, path, text, font, style, color, alignment, horizontal, vertical, localScale, worldScale, button, scroll, layout, risk, reason; public bool activeSelf, active, bestFit, offscreen, clipped, lowContrast; public int size, bestFitMin, bestFitMax, lines; public float lineSpacing, rectWidth, rectHeight, preferredWidth, preferredHeight, widthUsage, heightUsage; }
        private sealed class LayoutRow { public string resolution, pageId, path, rect, localScale, worldScale, risk, reason; public bool active, offscreen; }
        private sealed class PageRow { public string resolution, pageId, rootPath, title, rootRect, visibleRect, scaleMode, referenceResolution, screenMatch; public bool active; public int scrolls, texts, buttons, inputs, dropdowns, shadows, outlines; public float canvasScale, match, referencePixelsPerUnit; }
        private sealed class EffectRow { public string resolution, pageId, path, component, objectName; public bool active; }
        private sealed class IssueRow { public string resolution, pageId, level, path, issue, scope; }
    }
}
