using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI.CommonTools
{
    /// <summary>
    /// 常用工具页面的运行时 UI 组装器：首次启用时从 CommonToolsSeedData 读取电阻色环、公式与文章数据，
    /// 动态建立标签页和内容面板。它不参与电路仿真、模板加载、保存导入或元件定义管理。
    /// 当前页面不依赖 Editor API，动态创建节点由 BuildPage/ClearChildren 在同一根节点下维护；
    /// 未来若预制体化，应先确认 built 标记、按钮监听器与运行时创建 Sprite 缓存的释放边界。
    /// </summary>
    public sealed class CommonToolsPageController : MonoBehaviour
    {
        // 常用工具页只组织工具入口、页签和展示容器。具体电阻计算、公式解释与文章内容各由本页的专用子流程处理，
        // 不能把此 controller 扩展成读取或修改活动 Workspace 的“万能工具中心”。
        private enum ToolTab
        {
            Resistor,
            Calculator,
            Formula,
            Article
        }

        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<Text> tabLabels = new List<Text>();
        private readonly List<RectTransform> tabHighlights = new List<RectTransform>();
        private readonly List<Button> bandButtons = new List<Button>();
        private readonly List<Text> bandLabels = new List<Text>();
        private readonly List<Button> colorButtons = new List<Button>();
        
        private RectTransform contentRoot;
        private RectTransform resistorPanel;
        private RectTransform calculatorPanel;
        private RectTransform formulaPanel;
        private RectTransform articlePanel;
        
        private RectTransform resistorPreview;
        private RectTransform resistorBandLayer;
        private Text resistorResultText;
        private RectTransform formulaDetailContent;
        private RectTransform articleDetailContent;

        private List<ResistorColorEntry> resistorColors;
        private List<CommonFormulaEntry> formulas;
        private List<CommonArticleEntry> articles;

        private readonly int[] bandColorIndices = { 1, 2, 0, 10, 10 };
        private bool fiveBandMode;
        private int selectedBandIndex;
        private bool built;

        // UI Colors
        private static readonly Color PageBackground = ColorUtility.TryParseHtmlString("#F7F9FC", out var bg) ? bg : new Color(0.97f, 0.98f, 1f);
        private static readonly Color CardBackground = Color.white;
        private static readonly Color PrimaryBlue = ColorUtility.TryParseHtmlString("#3B82F6", out var pb) ? pb : new Color(0.23f, 0.51f, 0.96f);
        private static readonly Color PrimaryBlueHover = ColorUtility.TryParseHtmlString("#2563EB", out var pbh) ? pbh : new Color(0.15f, 0.39f, 0.92f);
        private static readonly Color PrimaryBlueLight = ColorUtility.TryParseHtmlString("#EFF6FF", out var pbl) ? pbl : new Color(0.94f, 0.96f, 1f);
        private static readonly Color TextDark = ColorUtility.TryParseHtmlString("#1E293B", out var td) ? td : new Color(0.12f, 0.16f, 0.23f);
        private static readonly Color TextMuted = ColorUtility.TryParseHtmlString("#64748B", out var tm) ? tm : new Color(0.39f, 0.45f, 0.55f);
        private static readonly Color BorderColor = ColorUtility.TryParseHtmlString("#E2E8F0", out var bc) ? bc : new Color(0.89f, 0.91f, 0.94f);
        private static readonly Color OrangeWarning = ColorUtility.TryParseHtmlString("#F59E0B", out var ow) ? ow : new Color(0.96f, 0.62f, 0.04f);

        private const string ResistorBaseSpritePath = "CommonTools/Resistor/色环电阻";

        // Sprite Cache
        private readonly Dictionary<string, Sprite> roundedSpriteCache = new Dictionary<string, Sprite>();

        private Sprite GetRoundedSprite(Color color, int radius)
        {
            string key = $"{color.r:F2}_{color.g:F2}_{color.b:F2}_{color.a:F2}_{radius}";
            if (roundedSpriteCache.TryGetValue(key, out var sprite))
                return sprite;

            int size = radius * 4;
            if (size == 0) size = 4; // Prevent 0 size
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            float r2 = radius * radius;
            float center = size / 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x - center + 0.5f) - (center - radius));
                    float dy = Mathf.Max(0, Mathf.Abs(y - center + 0.5f) - (center - radius));
                    float d2 = dx * dx + dy * dy;

                    if (d2 <= r2 || radius == 0) pixels[y * size + x] = color;
                    else pixels[y * size + x] = Color.clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            var newSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            roundedSpriteCache[key] = newSprite;
            return newSprite;
        }

        private void OnEnable()
        {
            // 页面每次显示时可安全确保布局存在，但不能重复堆叠工具卡或监听。可见性刷新不应重置用户正在填写的
            // 子工具输入，除非代码明确重建了对应工具页。
            // 只在首次显示时创建页面，避免页面切换时重复累加工具卡与监听器。
            if (!built)
            {
                BuildPage();
                built = true;
            }
        }

        public void BuildPage()
        {
            // BuildPage 拥有本页动态 UI 的生命周期：先清理本控制器生成的子节点，再按固定层级建立 Header、Sidebar
            // 和 Content。它不负责业务公式，布局重建也不能改变当前电路或仿真状态。
            // 供当前运行时页面初始化调用：清理本页旧节点后以种子数据重建，不修改任何外部页面或项目资源。
            ClearChildren();
            resistorColors = CommonToolsSeedData.GetResistorColors();
            formulas = CommonToolsSeedData.GetFormulas();
            articles = CommonToolsSeedData.GetArticles();

            var background = gameObject.GetComponent<Image>();
            if (background == null) background = gameObject.AddComponent<Image>();
            background.color = PageBackground;
            background.raycastTarget = true;

            BuildHeader();
            BuildSidebar();
            BuildContentRoot();
            
            BuildResistorPanel();
            BuildCalculatorPlaceholderPanel();
            BuildFormulaPanel();
            BuildArticlePanel();
            SelectTool(ToolTab.Resistor);
        }

        private void ClearChildren()
        {
            // 仅销毁本页根下由 BuildPage 生成的对象；不要把通用清理用于外部导航或场景节点，否则会破坏 SerializedField 引用。
            // 仅销毁本控制器根节点下动态创建的 UI；不要把它用于清理场景其他页面。
            foreach (Transform child in transform)
            {
                Destroy(child.gameObject);
            }
            tabButtons.Clear();
            tabLabels.Clear();
            tabHighlights.Clear();
            bandButtons.Clear();
            bandLabels.Clear();
            colorButtons.Clear();
        }

        private void ClearChildren(Transform parent)
        {
            foreach (Transform child in parent)
            {
                Destroy(child.gameObject);
            }
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private RectTransform CreatePanel(string name, Transform parent, Color color, int radius = 0)
        {
            var rect = CreateRect(name, parent);
            var img = rect.gameObject.AddComponent<Image>();
            img.type = Image.Type.Sliced;
            if (radius > 0)
                img.sprite = GetRoundedSprite(color, radius);
            else
                img.color = color;
            return rect;
        }

        private Text CreateText(string name, Transform parent, string text, int fontSize, FontStyle style, Color color)
        {
            var rect = CreateRect(name, parent);
            var textComp = rect.gameObject.AddComponent<Text>();
            textComp.text = text;
            textComp.font = style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
            textComp.fontSize = fontSize;
            textComp.fontStyle = FontStyle.Normal;
            textComp.color = color;
            textComp.supportRichText = true;
            textComp.resizeTextForBestFit = false;
            return textComp;
        }

        private Button CreateButton(string name, Transform parent, string text, Color bgColor, Color textColor, int fontSize, int radius = 6)
        {
            var rect = CreatePanel(name, parent, bgColor, radius);
            var button = rect.gameObject.AddComponent<Button>();
            var label = CreateText("Label", rect, text, fontSize, FontStyle.Normal, textColor);
            label.alignment = TextAnchor.MiddleCenter;
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
        }

        private void SetRect(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        private void StretchTo(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private void CleanupCardDecoration(RectTransform target)
        {
            if (target == null) return;
            
            // Clean up Outlines
            var outlines = target.GetComponentsInChildren<Outline>(true);
            foreach (var o in outlines) { o.enabled = false; Destroy(o); }
            
            // Clean up Shadows (that are not Outlines, though Destroying won't hurt if we already destroyed Outline)
            var shadows = target.GetComponentsInChildren<Shadow>(true);
            foreach (var s in shadows)
            {
                if (s != null) { s.enabled = false; Destroy(s); }
            }

            var namesToDestroy = new HashSet<string> { "Shadow", "SoftShadow", "CardShadow", "BorderShadow", "DropShadow", "Glow", "SoftGlow", "OutlineShadow" };
            var childrenToDestroy = new List<Transform>();

            void CollectSuspects(Transform t)
            {
                if (namesToDestroy.Contains(t.name)) childrenToDestroy.Add(t);
                foreach (Transform child in t) CollectSuspects(child);
            }
            CollectSuspects(target);

            foreach (var child in childrenToDestroy)
            {
                if (child != null)
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }
        }

        private void BuildHeader()
        {
            var title = CreateText("ToolsTitle", transform, "常用工具", 30, FontStyle.Bold, MainUiTheme.Hex("111827"));
            MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.PageTitle);
            title.alignment = TextAnchor.MiddleLeft;
            title.horizontalOverflow = HorizontalWrapMode.Overflow;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -30f), new Vector2(260f, 40f));

            var desc = CreateText("ToolsDescription", transform, "系统内置的辅助计算与基础教学工具库，方便您在接线练习时进行参数推演和规范查询。", 15, FontStyle.Normal, MainUiTheme.Hex("64748B"));
            MainUiTheme.ApplyTextRole(desc, MainUiTheme.UiTextRole.PageSubtitle);
            desc.alignment = TextAnchor.MiddleLeft;
            SetRect(desc.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(160f, -30f), new Vector2(-200f, 40f));
        }

        private void BuildSidebar()
        {
            // Sidebar 只发出工具选择意图。选中样式是当前工具页的表现投影，不能替代工具内部的计算结果或输入校验。
            var sidebar = CreatePanel("ToolSidebar", transform, CardBackground, 12);
            SetRect(sidebar, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(34f, -90f), new Vector2(240f, -120f));
            CleanupCardDecoration(sidebar);

            var title = CreateText("SidebarTitle", sidebar, "工具分类", 18, FontStyle.Bold, TextDark);
            title.alignment = TextAnchor.MiddleLeft;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(20f, -18f), new Vector2(-40f, 30f));

            var categoryList = CreateRect("CategoryList", sidebar);
            StretchTo(categoryList, 0, 60, 0, 0);

            var layout = categoryList.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;
            layout.padding = new RectOffset(14, 14, 10, 10);

            AddToolTab(categoryList, ToolTab.Resistor, "电阻色环识别");
            AddToolTab(categoryList, ToolTab.Calculator, "回路参数估算工具");
            AddToolTab(categoryList, ToolTab.Formula, "电路公式");
            AddToolTab(categoryList, ToolTab.Article, "基础资料");
        }

        private void AddToolTab(Transform parent, ToolTab tab, string label)
        {
            var buttonRect = CreatePanel("ToolTab_" + tab, parent, Color.clear, 8);
            var layoutElement = buttonRect.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = 36f;
            layoutElement.preferredHeight = 36f;

            var highlight = CreatePanel("Highlight", buttonRect, PrimaryBlueLight, 8);
            StretchTo(highlight, 0f, 0f, 0f, 0f);
            highlight.gameObject.SetActive(false);
            tabHighlights.Add(highlight);

            var line = CreatePanel("Line", buttonRect, PrimaryBlue, 2);
            SetRect(line, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(3f, 20f));
            line.gameObject.SetActive(false);

            var button = buttonRect.gameObject.AddComponent<Button>();
            var text = CreateText("Text", buttonRect, label, 15, FontStyle.Normal, MainUiTheme.SecondaryText);
            text.alignment = TextAnchor.MiddleLeft;
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(-14f, 0f));
            
            button.onClick.AddListener(() => SelectTool(tab));
            tabButtons.Add(button);
            tabLabels.Add(text);
        }

        private void SelectTool(ToolTab tab)
        {
            // 切换工具时只替换 Content 容器。每个工具拥有自己的 UI 和数据边界，避免电阻色环、公式文章和计算器
            // 通过共享控件或残留 listener 相互污染。
            // 标签选择只切换本页面板可见性和按钮状态，不保存为全局应用状态。
            resistorPanel.gameObject.SetActive(tab == ToolTab.Resistor);
            calculatorPanel.gameObject.SetActive(tab == ToolTab.Calculator);
            formulaPanel.gameObject.SetActive(tab == ToolTab.Formula);
            articlePanel.gameObject.SetActive(tab == ToolTab.Article);

            for (var i = 0; i < tabButtons.Count; i++)
            {
                var active = (int)tab == i;
                tabHighlights[i].gameObject.SetActive(active);
                tabHighlights[i].parent.Find("Line").gameObject.SetActive(active);
                tabLabels[i].font = active ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
                tabLabels[i].fontSize = 15;
                tabLabels[i].fontStyle = FontStyle.Normal;
                tabLabels[i].color = active ? PrimaryBlueHover : MainUiTheme.SecondaryText;
            }
        }

        private void BuildContentRoot()
        {
            // 各工具共享同一个内容根节点，但仅当前选中的面板显示；切页复用已构建的 UI，避免重复注册按钮和重复持有资源。
            contentRoot = CreateRect("ToolContentRoot", transform);
            StretchTo(contentRoot, 290f, 90f, 34f, 30f);
        }
        
        private void BuildCalculatorPlaceholderPanel()
        {
            calculatorPanel = CreateRect("CalculatorPanel", contentRoot);
            StretchTo(calculatorPanel, 0, 0, 0, 0);

            // Clean up any existing ElectricianCalculatorController on the root object
            var oldCalc = GetComponent<ElectricianCalculatorController>();
            if (oldCalc != null) Destroy(oldCalc);

            // Delegate to ElectricianCalculatorController to build its UI in this panel
            calculatorPanel.gameObject.AddComponent<ElectricianCalculatorController>();
        }

        // ==============================================
        // RESISTOR TOOL
        // ==============================================
        private void BuildResistorPanel()
        {
            // 电阻工具把色环选择转换为教学展示，所有中间状态仅存在于页面。它不创建 ComponentDefinition，
            // 也不应把计算值写回 Palette、模板或活动电路中的电阻实例。
            resistorPanel = CreateRect("ResistorColorPanel", contentRoot);
            StretchTo(resistorPanel, 0f, 0f, 0f, 0f);

            var header = CreateText("ModeTitle", resistorPanel, "电阻色环识别", 24, FontStyle.Bold, TextDark);
            header.alignment = TextAnchor.MiddleLeft;
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -22f), new Vector2(220f, 44f));
            
            var desc = CreateText("ModeDesc", resistorPanel, "选择色环颜色，自动计算电阻阻值与误差。", 15, FontStyle.Normal, TextMuted);
            desc.alignment = TextAnchor.MiddleLeft;
            SetRect(desc.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(210f, -22f), new Vector2(-220f, 44f));

            BuildSegmentedControl();
            BuildResistorPreviewCard();
            BuildBandSelectCard();
            BuildColorPickerCard();
            BuildExampleCard();

            RefreshResistorTool();
        }

        private void BuildSegmentedControl()
        {
            var segmentContainer = CreatePanel("SegmentContainer", resistorPanel, MainUiTheme.Hex("F8FAFC"), 8);
            SetRect(segmentContainer, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-10f, -22f), new Vector2(180f, 40f));

            var fourBtn = CreateButton("Four", segmentContainer, "四色环", Color.white, PrimaryBlue, 15, 6);
            SetRect(fourBtn.GetComponent<RectTransform>(), new Vector2(0,0), new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(4, 4), new Vector2(-4, -8));
            var fiveBtn = CreateButton("Five", segmentContainer, "五色环", Color.clear, TextMuted, 15, 6);
            SetRect(fiveBtn.GetComponent<RectTransform>(), new Vector2(0.5f,0), new Vector2(1f, 1f), new Vector2(0, 0), new Vector2(4, 4), new Vector2(-8, -8));

            fourBtn.onClick.AddListener(() => {
                SetBandMode(false);
                fourBtn.GetComponent<Image>().sprite = GetRoundedSprite(MainUiTheme.Hex("EAF2FF"), 6);
                fourBtn.GetComponentInChildren<Text>().color = PrimaryBlueHover;
                fourBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
                fiveBtn.GetComponent<Image>().sprite = GetRoundedSprite(Color.white, 6);
                fiveBtn.GetComponentInChildren<Text>().color = TextMuted;
                fiveBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Normal;
            });
            fiveBtn.onClick.AddListener(() => {
                SetBandMode(true);
                fiveBtn.GetComponent<Image>().sprite = GetRoundedSprite(MainUiTheme.Hex("EAF2FF"), 6);
                fiveBtn.GetComponentInChildren<Text>().color = PrimaryBlueHover;
                fiveBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
                fourBtn.GetComponent<Image>().sprite = GetRoundedSprite(Color.white, 6);
                fourBtn.GetComponentInChildren<Text>().color = TextMuted;
                fourBtn.GetComponentInChildren<Text>().fontStyle = FontStyle.Normal;
            });
            
            fourBtn.onClick.Invoke(); // Default
        }

        private void BuildResistorPreviewCard()
        {
            var card = CreatePanel("PreviewCard", resistorPanel, CardBackground, 12);
            SetRect(card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(10f, -80f), new Vector2(-20f, 260f));
            CleanupCardDecoration(card);

            resistorPreview = CreateRect("ResistorPreview", card);
            SetRect(resistorPreview, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -15f), new Vector2(600f, 140f));

            var baseSprite = Resources.Load<Sprite>(ResistorBaseSpritePath);
            if (baseSprite != null)
            {
                var baseImageRect = CreatePanel("BaseImage", resistorPreview, Color.white);
                var baseImage = baseImageRect.GetComponent<Image>();
                baseImage.sprite = baseSprite;
                baseImage.preserveAspect = true;
                baseImage.raycastTarget = false;
                SetRect(baseImageRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 140f));
            }
            else
            {
                BuildFallbackResistorBody();
            }

            resistorBandLayer = CreateRect("BandLayer", resistorPreview);
            SetRect(resistorBandLayer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 140f));

            for (var i = 0; i < 5; i++)
            {
                var band = CreatePanel("Band" + (i + 1), resistorBandLayer, Color.black);
                var bandImage = band.GetComponent<Image>();
                var specificBandSprite = Resources.Load<Sprite>("CommonTools/Resistor/resistor_band_" + (i + 1));
                bandImage.sprite = specificBandSprite;
                bandImage.preserveAspect = true;
                bandImage.raycastTarget = false;
                SetRect(band, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(600f, 140f));
            }

            // Result Area
            var resultArea = CreatePanel("ResultArea", card, PrimaryBlueLight, 8);
            SetRect(resultArea, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(20f, 20f), new Vector2(-40f, 80f));

            resistorResultText = CreateText("ResultText", resultArea, string.Empty, 16, FontStyle.Normal, TextDark);
            resistorResultText.alignment = TextAnchor.MiddleLeft;
            SetRect(resistorResultText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(20f, 0f), new Vector2(-40f, 0f));
        }

        private void BuildFallbackResistorBody()
        {
            var leadL = CreatePanel("LeadLeft", resistorPreview, new Color(0.7f, 0.74f, 0.8f));
            SetRect(leadL, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-300, 0), new Vector2(200, 8));
            var leadR = CreatePanel("LeadRight", resistorPreview, new Color(0.7f, 0.74f, 0.8f));
            SetRect(leadR, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(300, 0), new Vector2(200, 8));
            var body = CreatePanel("Body", resistorPreview, new Color(0.86f, 0.72f, 0.48f), 10);
            SetRect(body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400, 80));
        }

        private void BuildBandSelectCard()
        {
            var card = CreatePanel("BandSelectCard", resistorPanel, CardBackground, 12);
            SetRect(card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(10f, -360f), new Vector2(-20f, 100f));
            CleanupCardDecoration(card);

            var title = CreateText("Title", card, "选择要编辑的色环", 16, FontStyle.Bold, TextDark);
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -15), new Vector2(200, 30));

            for (var i = 0; i < 5; i++)
            {
                var captured = i;
                var button = CreateButton("BandSelector_" + i, card, "第" + (i + 1) + "环", Color.clear, TextDark, 14, 8);
                SetRect(button.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20 + i * 90, -50), new Vector2(80, 40));
                AddButtonOutline(button, MainUiTheme.Hex("DCEBFF"));
                button.onClick.AddListener(() => {
                    selectedBandIndex = captured;
                    RefreshResistorTool();
                });
                bandButtons.Add(button);
                bandLabels.Add(button.GetComponentInChildren<Text>());
            }
        }

        private void BuildColorPickerCard()
        {
            var card = CreatePanel("ColorPickerCard", resistorPanel, CardBackground, 12);
            SetRect(card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(10f, -480f), new Vector2(-20f, 160f));
            CleanupCardDecoration(card);

            var title = CreateText("Title", card, "可选颜色", 16, FontStyle.Bold, TextDark);
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -15), new Vector2(200, 30));

            for (var i = 0; i < resistorColors.Count; i++)
            {
                var entry = resistorColors[i];
                var button = CreateButton("Color_" + entry.Name, card, entry.Name, entry.Color, GetReadableTextColor(entry.Color), 14, 6);
                AddButtonOutline(button, IsLightColor(entry.Color) ? MainUiTheme.Hex("CBD5E1") : new Color(1f, 1f, 1f, 0.14f));
                var captured = i;
                button.onClick.AddListener(() => SetSelectedBandColor(captured));
                colorButtons.Add(button);
            }
        }

        private void BuildExampleCard()
        {
            var card = CreatePanel("ExampleCard", resistorPanel, CardBackground, 12);
            SetRect(card, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(10f, -660f), new Vector2(-20f, 80f));
            CleanupCardDecoration(card);

            var title = CreateText("ExampleTitle", card, "常用示例", 16, FontStyle.Bold, TextDark);
            title.alignment = TextAnchor.MiddleLeft;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -15f), new Vector2(100f, 30f));

            AddExampleButton(card, "10 Ω", 0, 1, 0, 0);
            AddExampleButton(card, "100 Ω", 1, 1, 0, 1);
            AddExampleButton(card, "1 kΩ", 2, 1, 0, 2);
            AddExampleButton(card, "4.7 kΩ", 3, 4, 7, 2);
            AddExampleButton(card, "10 kΩ", 4, 1, 0, 3);
            AddExampleButton(card, "1 MΩ", 5, 1, 0, 5);
        }

        private void AddExampleButton(RectTransform parent, string label, int index, int band1, int band2, int multiplier)
        {
            var button = CreateButton("Example_" + index, parent, label, PrimaryBlueLight, PrimaryBlue, 14, 6);
            SetRect(button.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20 + index * 90, -45), new Vector2(80, 30));
            AddButtonOutline(button, BorderColor);
            button.onClick.AddListener(() =>
            {
                // Force switch to 4 bands
                var segContainer = resistorPanel.Find("SegmentContainer");
                segContainer.Find("Four").GetComponent<Button>().onClick.Invoke();
                selectedBandIndex = 0;
                bandColorIndices[0] = band1;
                bandColorIndices[1] = band2;
                bandColorIndices[2] = multiplier;
                bandColorIndices[3] = 10;
                bandColorIndices[4] = 10;
                RefreshResistorTool();
            });
        }

        private void SetBandMode(bool useFiveBands)
        {
            fiveBandMode = useFiveBands;
            if (!fiveBandMode && selectedBandIndex > 3) selectedBandIndex = 3;
            RefreshResistorTool();
        }

        private void SetSelectedBandColor(int colorIndex)
        {
            if (!IsColorAllowedForBand(colorIndex, selectedBandIndex)) return;
            bandColorIndices[selectedBandIndex] = colorIndex;
            RefreshResistorTool();
        }

        private void RefreshResistorTool()
        {
            // 刷新统一重算颜色、预览和文本，避免某个按钮仅更新局部 UI 产生前后不一致的色环含义。
            if (resistorResultText == null) return;
            
            var visibleBands = fiveBandMode ? 5 : 4;
            for (var i = 0; i < 5; i++)
            {
                var band = resistorBandLayer != null ? resistorBandLayer.Find("Band" + (i + 1)) : null;
                if (band != null)
                {
                    bool isVisible = fiveBandMode ? true : (i != 3);
                    band.gameObject.SetActive(isVisible);
                    if (isVisible)
                    {
                        int logicalIndex = i;
                        if (!fiveBandMode && i == 4) logicalIndex = 3;
                        band.GetComponent<Image>().color = resistorColors[bandColorIndices[logicalIndex]].Color;
                    }
                }

                if (i < bandButtons.Count)
                {
                    bandButtons[i].gameObject.SetActive(i < visibleBands);
                    var active = selectedBandIndex == i;
                    var btnImg = bandButtons[i].GetComponent<Image>();
                    btnImg.sprite = GetRoundedSprite(active ? PrimaryBlueHover : MainUiTheme.Hex("F8FAFC"), 8);
                    bandLabels[i].color = active ? Color.white : PrimaryBlueHover;
                    bandLabels[i].fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                    AddButtonOutline(bandButtons[i], active ? PrimaryBlueHover : MainUiTheme.Hex("DCEBFF"));
                    bandLabels[i].text = "第" + (i + 1) + "环\n" + GetBandRoleName(i);
                }
            }

            var visibleColorIndex = 0;
            for (var i = 0; i < colorButtons.Count; i++)
            {
                var allowed = IsColorAllowedForBand(i, selectedBandIndex);
                var button = colorButtons[i];
                button.gameObject.SetActive(allowed);
                button.interactable = allowed;
                if (allowed)
                {
                    var row = visibleColorIndex / 8;
                    var col = visibleColorIndex % 8;
                    SetRect(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f + col * 90f, -45f - row * 45f), new Vector2(80f, 35f));
                    var label = button.GetComponentInChildren<Text>();
                    label.text = GetColorButtonLabel(resistorColors[i], selectedBandIndex);
                    label.fontSize = 13;
                    label.fontStyle = FontStyle.Normal;
                    label.color = GetReadableTextColor(resistorColors[i].Color);
                    visibleColorIndex++;
                }
            }

            resistorResultText.text = CalculateResistanceText();
        }

        private bool IsColorAllowedForBand(int colorIndex, int bandIndex)
        {
            var color = resistorColors[colorIndex];
            var multiplierBand = fiveBandMode ? 3 : 2;
            var toleranceBand = fiveBandMode ? 4 : 3;
            if (bandIndex == toleranceBand) return color.HasTolerance;
            if (bandIndex == multiplierBand) return color.HasMultiplier;
            return color.HasDigit;
        }

        private string CalculateResistanceText()
        {
            // 色环换算是独立的教学计算，不读取也不修改活动 Workspace；输入不完整时返回说明文本，而不是伪造一个电路计算结果。
            double significant;
            double multiplier;
            string tolerance;

            if (fiveBandMode)
            {
                significant = resistorColors[bandColorIndices[0]].Digit.Value * 100 +
                              resistorColors[bandColorIndices[1]].Digit.Value * 10 +
                              resistorColors[bandColorIndices[2]].Digit.Value;
                multiplier = resistorColors[bandColorIndices[3]].Multiplier.Value;
                tolerance = resistorColors[bandColorIndices[4]].Tolerance;
            }
            else
            {
                significant = resistorColors[bandColorIndices[0]].Digit.Value * 10 +
                              resistorColors[bandColorIndices[1]].Digit.Value;
                multiplier = resistorColors[bandColorIndices[2]].Multiplier.Value;
                tolerance = resistorColors[bandColorIndices[3]].Tolerance;
            }

            var ohms = significant * multiplier;
            return "<b><size=22><color=#2563EB>阻值：" + FormatResistance(ohms) + " " + tolerance + "</color></size></b>\n" +
                   "计算过程：" + FormatNumber(significant) + " × " + FormatMultiplier(multiplier) + " = " + FormatRawOhms(ohms) + "\n" +
                   "色环排布：" + GetBandColorNames();
        }

        private static string FormatResistance(double value)
        {
            if (value >= 1000000d) return FormatNumber(value / 1000000d) + " MΩ";
            if (value >= 1000d) return FormatNumber(value / 1000d) + " kΩ";
            return FormatNumber(value) + " Ω";
        }

        private static string FormatRawOhms(double value) => FormatNumber(value) + " Ω";
        private static string FormatMultiplier(double value) => value >= 1d ? FormatNumber(value) : value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string FormatNumber(double value) => value.ToString(value >= 10d || Math.Abs(value - Math.Round(value)) < 0.0001d ? "0.##" : "0.###", CultureInfo.InvariantCulture);

        private string GetBandRoleName(int bandIndex)
        {
            var multiplierBand = fiveBandMode ? 3 : 2;
            var toleranceBand = fiveBandMode ? 4 : 3;
            if (bandIndex == toleranceBand) return "误差";
            if (bandIndex == multiplierBand) return "倍率";
            return "数字";
        }

        private string GetColorButtonLabel(ResistorColorEntry color, int bandIndex)
        {
            var role = GetBandRoleName(bandIndex);
            if (role == "误差") return color.Name + " " + color.Tolerance;
            if (role == "倍率") return color.Name + " ×" + FormatMultiplier(color.Multiplier.Value);
            return color.Name + " " + color.Digit.Value;
        }

        private string GetBandColorNames()
        {
            var visibleBands = fiveBandMode ? 5 : 4;
            var names = new List<string>();
            for (var i = 0; i < visibleBands; i++) names.Add(resistorColors[bandColorIndices[i]].Name);
            return string.Join("、", names.ToArray());
        }

        private void AddButtonOutline(Button button, Color color)
        {
            if (button == null)
            {
                return;
            }

            var outlines = button.GetComponents<Outline>();
            foreach (var o in outlines) { o.enabled = false; Destroy(o); }
            var shadows = button.GetComponents<Shadow>();
            foreach (var s in shadows) { s.enabled = false; Destroy(s); }
        }

        private static bool IsLightColor(Color bg)
        {
            return (bg.r * 299f + bg.g * 587f + bg.b * 114f) / 1000f > 0.68f;
        }

        private Color GetReadableTextColor(Color bg)
        {
            float brightness = (bg.r * 299 + bg.g * 587 + bg.b * 114) / 1000f;
            return brightness > 0.6f ? TextDark : Color.white;
        }

        // ==============================================
        // FORMULA & ARTICLE TOOLS
        // ==============================================
        private void BuildFormulaPanel()
        {
            // 公式浏览是静态参考内容；选择公式只改变右侧详情，不触发求解、保存或任何 Workspace 操作。
            formulaPanel = CreateRect("FormulaPanel", contentRoot);
            StretchTo(formulaPanel, 0f, 0f, 0f, 0f);

            var header = CreateText("Title", formulaPanel, "电路公式", 24, FontStyle.Bold, TextDark);
            header.alignment = TextAnchor.MiddleLeft;
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -22f), new Vector2(220f, 44f));
            
            var desc = CreateText("Desc", formulaPanel, "整理常见电工计算公式、变量说明和适用场景。", 15, FontStyle.Normal, TextMuted);
            desc.alignment = TextAnchor.MiddleLeft;
            SetRect(desc.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(120f, -22f), new Vector2(-130f, 44f));

            var listPanel = BuildBorderlessListPanel(formulaPanel, "FormulaList");
            var detailPanel = BuildDetailScrollPanel(formulaPanel, "FormulaDetail", 280f);
            formulaDetailContent = detailPanel;

            var btns = new List<Button>();
            for (var i = 0; i < formulas.Count; i++)
            {
                var captured = formulas[i];
                var button = CreateNavListButton(listPanel.parent, captured.Title, captured.Category);
                btns.Add(button);
                button.onClick.AddListener(() => {
                    ShowFormula(captured);
                    RefreshToolListSelection(btns, button);
                });
            }

            if (btns.Count > 0) btns[0].onClick.Invoke();
        }

        private void BuildArticlePanel()
        {
            // 文章列表同样是展示数据源。动态创建的条目和 listener 随 Content 清理，不能跨页面持有旧的 RectTransform。
            articlePanel = CreateRect("ArticlePanel", contentRoot);
            StretchTo(articlePanel, 0f, 0f, 0f, 0f);

            var header = CreateText("Title", articlePanel, "基础资料", 24, FontStyle.Bold, TextDark);
            header.alignment = TextAnchor.MiddleLeft;
            SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -22f), new Vector2(220f, 44f));
            
            var desc = CreateText("Desc", articlePanel, "整理常见电工基础知识、端子说明和安全用电规范。", 15, FontStyle.Normal, TextMuted);
            desc.alignment = TextAnchor.MiddleLeft;
            SetRect(desc.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(120f, -22f), new Vector2(-130f, 44f));

            var listPanel = BuildBorderlessListPanel(articlePanel, "ArticleList");
            var detailPanel = BuildDetailScrollPanel(articlePanel, "ArticleDetail", 280f);
            articleDetailContent = detailPanel;

            var btns = new List<Button>();
            for (var i = 0; i < articles.Count; i++)
            {
                var captured = articles[i];
                var button = CreateNavListButton(listPanel.parent, captured.Title, captured.Category);
                btns.Add(button);
                button.onClick.AddListener(() => {
                    ShowArticle(captured);
                    RefreshToolListSelection(btns, button);
                });
            }

            if (btns.Count > 0) btns[0].onClick.Invoke();
        }

        private (RectTransform viewport, RectTransform parent) BuildBorderlessListPanel(RectTransform parent, string name)
        {
            var panel = CreatePanel(name, parent, CardBackground, 12);
            SetRect(panel, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(260f, -60f));
            CleanupCardDecoration(panel);

            var viewport = CreatePanel(name + "Viewport", panel, new Color(1f, 1f, 1f, 0.01f));
            StretchTo(viewport, 2, 2, 2, 2);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateRect(name + "Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(10, 10, 10, 10);

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = panel.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            return (viewport, content);
        }

        private Button CreateNavListButton(RectTransform parent, string title, string category)
        {
            var buttonRect = CreatePanel("NavListItem", parent, Color.clear, 8);
            var layoutElement = buttonRect.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = 44f;
            layoutElement.preferredHeight = 44f;

            var titleText = CreateText("Title", buttonRect, title, 15, FontStyle.Normal, TextDark);
            titleText.alignment = TextAnchor.MiddleLeft;
            SetRect(titleText.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(10, 8), new Vector2(-20, -16));

            var catText = CreateText("Category", buttonRect, category, 13, FontStyle.Normal, TextMuted);
            catText.alignment = TextAnchor.MiddleLeft;
            SetRect(catText.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(10, -14), new Vector2(-20, -28));

            return buttonRect.gameObject.AddComponent<Button>();
        }

        private RectTransform BuildDetailScrollPanel(RectTransform parent, string name, float left)
        {
            // 资料详情使用独立 ScrollRect 容纳可变长度文本；滚动内容是页面表现数据，不应与画布导航或元件池滚轮状态耦合。
            var viewport = CreatePanel(name + "Viewport", parent, new Color(1f, 1f, 1f, 0.01f));
            StretchTo(viewport, left, 60f, 0f, 0f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateRect(name, viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 16f;
            layout.padding = new RectOffset(0, 16, 0, 30); // Need right padding to avoid scrollbar overlap

            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35f;

            return content;
        }

        private void ShowFormula(CommonFormulaEntry entry)
        {
            // 详情显示消费条目文本，不对公式表达式做隐藏求值；这样教学说明与计算器的明确输入公式保持边界。
            if (entry == null) return;
            ClearChildren(formulaDetailContent);

            CreateRichCard(formulaDetailContent, entry.Title, $"<color=#64748B>分类：{entry.Category}</color>\n\n{entry.ShortDescription}", 18, false);
            CreateRichCard(formulaDetailContent, "核心公式", JoinLines(entry.Expressions), 16, true);
            if(entry.Variants != null && entry.Variants.Count > 0) CreateRichCard(formulaDetailContent, "常见变形", JoinLines(entry.Variants), 16, true);
            CreateRichCard(formulaDetailContent, "变量说明", entry.Variables, 16, false);
            CreateRichCard(formulaDetailContent, "单位说明", entry.UnitDescription, 16, false);
            CreateRichCard(formulaDetailContent, "适用场景", entry.UseCase, 16, false);
            CreateRichCard(formulaDetailContent, "计算示例", entry.Example, 16, false);
            if(!string.IsNullOrEmpty(entry.CommonMistakes)) CreateRichCard(formulaDetailContent, "常见错误", entry.CommonMistakes, 16, false, OrangeWarning);
            CreateRichCard(formulaDetailContent, "注意事项", entry.Note, 16, false);
        }

        private void ShowArticle(CommonArticleEntry entry)
        {
            if (entry == null) return;
            ClearChildren(articleDetailContent);

            CreateRichCard(articleDetailContent, entry.Title, $"<color=#64748B>分类：{entry.Category}</color>\n\n<b>学习目标：</b>\n{entry.LearningGoal}", 18, false);
            for (var i = 0; i < entry.Sections.Count; i++)
            {
                CreateRichCard(articleDetailContent, entry.Sections[i].Heading, entry.Sections[i].Body, 16, false);
            }

            CreateRichCard(articleDetailContent, "关键点", entry.KeyPoints, 16, false);
            if(!string.IsNullOrEmpty(entry.CommonMistakes)) CreateRichCard(articleDetailContent, "常见误区", entry.CommonMistakes, 16, false, OrangeWarning);
            CreateRichCard(articleDetailContent, "与本系统的关系", entry.RelationToSystem, 16, false, PrimaryBlue);
        }

        private RectTransform CreateRichCard(RectTransform parent, string title, string body, int titleSize, bool isFormula, Color? titleColorOverride = null)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            var card = CreatePanel("InfoCard_" + title, parent, CardBackground, 10);
            CleanupCardDecoration(card);

            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 10f;
            layout.padding = new RectOffset(20, 20, 16, 20);

            var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var titleColor = titleColorOverride ?? TextDark;
            var titleText = CreateText("Title", card, title, titleSize, FontStyle.Bold, titleColor);
            titleText.alignment = TextAnchor.UpperLeft;
            titleText.verticalOverflow = VerticalWrapMode.Overflow;
            
            int bodySize = isFormula ? 18 : 15;
            FontStyle bodyStyle = isFormula ? FontStyle.Bold : FontStyle.Normal;
            Color bodyColor = isFormula ? PrimaryBlue : MainUiTheme.SecondaryText;

            var bodyText = CreateText("Body", card, body, bodySize, bodyStyle, bodyColor);
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            // Set line spacing for better readability
            bodyText.lineSpacing = isFormula ? 1.5f : 1.3f;

            return card;
        }

        private void RefreshToolListSelection(List<Button> buttons, Button selected)
        {
            // 选择色只表示当前详情来源。不要根据某个按钮的颜色/activeSelf 来反推工具类别或业务计算状态。
            foreach (var button in buttons)
            {
                var active = button == selected;
                button.GetComponent<Image>().sprite = GetRoundedSprite(active ? PrimaryBlueLight : Color.clear, 8);
                var title = button.transform.Find("Title").GetComponent<Text>();
                title.font = active ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
                title.fontSize = 15;
                title.fontStyle = FontStyle.Normal;
                title.color = active ? PrimaryBlue : TextDark;
            }
        }

        private static string JoinLines(List<string> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            return string.Join("\n\n", values.ToArray());
        }
    }
}
