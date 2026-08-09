using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 图纸集页面控制器：基于模板 Catalog 构建和刷新卡片，维护筛选、分页、选中项和参考预览状态。
    /// 进入练习时优先委托 PracticeSessionController；兼容回退路径只切换到模拟页面并展示参考面板。
    /// 本类不解析模板 JSON，也不直接生成 Workspace 元件。
    /// 本控制器内的卡片和预览图片来自 Catalog 的 thumbnailPath；练习会话内部资源由 PracticeSessionController 管理。
    /// </summary>
    public sealed class BlueprintController : MonoBehaviour
    {
        // 图纸集管理的是“可被展示和选择的目录项”及其卡片状态。内置模板、用户保存图纸、展示卡片与运行时
        // Workspace 是不同层的对象：本类不能把预览图片或 selectedIndex 当成已加载电路，也不能直接生成元件。
        // 查看、加载、进入练习必须保持各自的副作用边界，避免一个卡片点击同时隐式改变画布、会话和参考面板。
        private const float PageMargin = 28f;
        private const float CardWidth = 446f;
        private const float CardHeight = 336f;
        private const float CardGap = 27f;

        [SerializeField] private TopNavigationController navigation;
        [SerializeField] private List<Button> blueprintButtons = new List<Button>();
        [SerializeField] private List<GameObject> blueprintCards = new List<GameObject>();
        [SerializeField] private List<string> blueprintNames = new List<string>();
        [SerializeField] private List<Sprite> blueprintSprites = new List<Sprite>();
        [SerializeField] private List<string> blueprintRecommendations = new List<string>();
        [SerializeField] private List<int> blueprintCategories = new List<int>();
        [SerializeField] private List<int> blueprintDifficulties = new List<int>();
        [SerializeField] private List<Button> categoryButtons = new List<Button>();
        [SerializeField] private List<Button> difficultyButtons = new List<Button>();
        [SerializeField] private InputField searchInput;
        [SerializeField] private RectTransform cardContent;
        [SerializeField] private GameObject previewModal;
        [SerializeField] private Text previewTitle;
        [SerializeField] private Image previewImage;
        [SerializeField] private Button previewCloseButton;
        [SerializeField] private Button previewCancelButton;
        [SerializeField] private Button configureButton;
        [SerializeField] private GameObject referencePanel;
        [SerializeField] private Text referenceTitle;
        [SerializeField] private Image referenceImage;
        [SerializeField] private Text referenceRecommendations;
        [SerializeField] private Button referenceCloseButton;

        private readonly List<ElectricalSim.Templates.CircuitTemplateCatalogItemDto> dynamicTemplates = new List<ElectricalSim.Templates.CircuitTemplateCatalogItemDto>();
        private readonly List<ElectricalSim.Templates.CircuitTemplateCatalogItemDto> catalogTemplates = new List<ElectricalSim.Templates.CircuitTemplateCatalogItemDto>();

        private int selectedIndex;
        private int activeCategory;
        private int activeDifficulty = -1;
        private int currentPage = 0;
        private GameObject paginationRoot;

        private void ApplyTheme()
        {
            // 主题遍历只修正现有卡片和筛选控件的表现。不要在样式路径改变 catalog、过滤条件或选择索引，
            // 否则窗口重绘会引入与用户操作无关的列表状态变化。
            var bg = GetComponent<Image>();
            if (bg != null) bg.color = MainUiTheme.Hex("F8FBFF");

            ApplyFilterButtonMetrics();

            foreach (var card in blueprintCards)
            {
                if (card == null) continue;

                var rect = card.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(CardWidth, CardHeight);
                }
                
                var images = card.GetComponentsInChildren<Image>(true);
                var cardBg = card.GetComponent<Image>();
                if (cardBg == null && images.Length > 0) cardBg = images[0];

                if (cardBg != null)
                {
                    cardBg.sprite = UiThemeTokens.GetRoundedSprite(16);
                    cardBg.type = Image.Type.Sliced;
                    cardBg.color = MainUiTheme.Hex("FAFCFF");
                }

                var outline = card.GetComponent<Outline>() ?? card.AddComponent<Outline>();
                outline.effectColor = MainUiTheme.Hex("D2D2D2");
                outline.effectDistance = new Vector2(1f, -1f);
                
                var shadow = card.GetComponent<UnityEngine.UI.Shadow>();
                if (shadow != null) Destroy(shadow);

                var btn = card.GetComponentInChildren<Button>(true);
                if (btn != null)
                {
                    var btnBg = btn.GetComponent<Image>();
                    if (btnBg != null && btnBg != cardBg)
                    {
                        btnBg.sprite = UiThemeTokens.GetRoundedSprite(8);
                        btnBg.type = Image.Type.Sliced;
                        btnBg.color = MainUiTheme.PrimaryBlue;
                        
                        var btnText = btn.GetComponentInChildren<Text>();
                        if (btnText != null)
                        {
                            btnText.font = MainUiTheme.UiFontBold;
                            btnText.fontSize = 16;
                            btnText.fontStyle = FontStyle.Normal;
                            btnText.color = Color.white;
                            btnText.resizeTextForBestFit = false;
                        }
                    }
                }

                ApplyBlueprintCardTextStyle(card);
            }

            if (searchInput != null)
            {
                var searchRect = searchInput.GetComponent<RectTransform>();
                if (searchRect != null)
                {
                    searchRect.sizeDelta = new Vector2(236f, 36f);
                }

                var searchBg = searchInput.GetComponent<Image>();
                if (searchBg != null)
                {
                    searchBg.sprite = UiThemeTokens.GetRoundedSprite(16);
                    searchBg.type = Image.Type.Sliced;
                    searchBg.color = Color.white;
                }

                if (searchInput.textComponent != null)
                {
                    searchInput.textComponent.font = MainUiTheme.DenseUiFont;
                    searchInput.textComponent.fontSize = 16;
                    searchInput.textComponent.fontStyle = FontStyle.Normal;
                    searchInput.textComponent.color = MainUiTheme.Hex("334155");
                    searchInput.textComponent.resizeTextForBestFit = false;
                }

                var placeholder = searchInput.placeholder as Text;
                if (placeholder != null)
                {
                    placeholder.font = MainUiTheme.DenseUiFont;
                    placeholder.fontSize = 16;
                    placeholder.fontStyle = FontStyle.Normal;
                    placeholder.color = MainUiTheme.Hex("94A3B8");
                    placeholder.resizeTextForBestFit = false;
                }
            }

            ApplyReferenceDetailLayout();
            StyleModal(previewModal);
            StyleModal(referencePanel);
            StyleCloseButton(previewCloseButton);
            StyleCloseButton(referenceCloseButton);

            if (configureButton != null)
            {
                var cfgBg = configureButton.GetComponent<Image>();
                if (cfgBg != null)
                {
                    cfgBg.sprite = UiThemeTokens.GetRoundedSprite(8);
                    cfgBg.type = Image.Type.Sliced;
                    cfgBg.color = MainUiTheme.PrimaryBlue;
                }
                var txt = configureButton.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.font = MainUiTheme.UiFontBold;
                    txt.fontSize = 15;
                    txt.fontStyle = FontStyle.Normal;
                    txt.color = Color.white;
                    txt.resizeTextForBestFit = false;
                }
            }
        }

        private void ApplyFilterButtonMetrics()
        {
            for (var i = 0; i < categoryButtons.Count; i++)
            {
                var rect = categoryButtons[i] != null ? categoryButtons[i].GetComponent<RectTransform>() : null;
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(175f, 36f);
                }
            }

            for (var i = 0; i < difficultyButtons.Count; i++)
            {
                var rect = difficultyButtons[i] != null ? difficultyButtons[i].GetComponent<RectTransform>() : null;
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(138f, 36f);
                }
            }
        }

        private struct DifficultyStyle
        {
            public Color BackgroundColor;
            public Color TextColor;
            public Color BorderColor;
            public string LabelText;
        }

        private static DifficultyStyle GetDifficultyStyle(string difficulty)
        {
            var style = new DifficultyStyle();
            style.TextColor = Color.white;
            style.LabelText = difficulty;

            if (string.IsNullOrEmpty(difficulty))
            {
                style.BackgroundColor = MainUiTheme.Hex("22C55E");
                style.BorderColor = MainUiTheme.Hex("22C55E");
            }
            else if (difficulty.Contains("\u9ad8") && !difficulty.Contains("\u4e2d"))
            {
                style.BackgroundColor = MainUiTheme.Hex("EF4444");
                style.BorderColor = MainUiTheme.Hex("EF4444");
            }
            else if (difficulty.Contains("\u4e2d\u9ad8") || difficulty.Contains("\u8fdb\u9636"))
            {
                style.BackgroundColor = MainUiTheme.Hex("16A34A");
                style.BorderColor = MainUiTheme.Hex("16A34A");
            }
            else if (difficulty.Contains("\u4e2d"))
            {
                style.BackgroundColor = MainUiTheme.Hex("F59E0B");
                style.BorderColor = MainUiTheme.Hex("F59E0B");
            }
            else if (difficulty.Contains("\u521d") || difficulty.Contains("\u5165\u95e8"))
            {
                style.BackgroundColor = MainUiTheme.Hex("22C55E");
                style.BorderColor = MainUiTheme.Hex("22C55E");
            }
            else
            {
                style.BackgroundColor = MainUiTheme.Hex("64748B");
                style.BorderColor = MainUiTheme.Hex("64748B");
            }
            
            return style;
        }

        private static void ApplyBlueprintCardTextStyle(GameObject card)
        {
            var texts = card.GetComponentsInChildren<Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                var text = texts[i];
                if (text == null) continue;

                text.resizeTextForBestFit = false;

                if (IsActionLabel(text.text))
                {
                    text.font = MainUiTheme.UiFontBold;
                    text.fontSize = 16;
                    text.fontStyle = FontStyle.Normal;
                    text.color = Color.white;
                    StyleActionButton(text, MainUiTheme.PrimaryBlue);
                }
                else if (IsCategoryLabel(text.text) || IsDifficultyLabel(text.text))
                {
                    var isDifficulty = IsDifficultyLabel(text.text);
                    text.font = isDifficulty ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
                    text.fontSize = 14;
                    text.fontStyle = FontStyle.Normal;
                    
                    if (isDifficulty)
                    {
                        var style = GetDifficultyStyle(text.text);
                        text.color = style.TextColor;
                        StyleTagParent(text, style.BackgroundColor, style.BorderColor, 99, false);
                    }
                    else
                    {
                        text.color = MainUiTheme.Hex("2563EB");
                        StyleTagParent(text, MainUiTheme.Hex("DBEAFE"), MainUiTheme.Hex("DBEAFE"), 99, false);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(text.text))
                {
                    text.font = MainUiTheme.UiFontBold;
                    text.fontSize = 19;
                    text.lineSpacing = 1.05f;
                    text.fontStyle = FontStyle.Normal;
                    text.color = MainUiTheme.Hex("1F2937");
                    text.horizontalOverflow = HorizontalWrapMode.Wrap;
                    text.verticalOverflow = VerticalWrapMode.Truncate;
                }
            }
        }

        private static void StyleActionButton(Text text, Color bgColor)
        {
            if (text == null) return;

            var parent = text.transform.parent;
            if (parent == null || parent == text.transform) return;

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == "Backplate")
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            var btn = parent.GetComponent<Button>();
            if (btn != null)
            {
                var colors = btn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.pressedColor = new Color(0.9f, 0.9f, 0.9f);
                colors.selectedColor = Color.white;
                btn.colors = colors;

                var nav = btn.navigation;
                nav.mode = Navigation.Mode.None;
                btn.navigation = nav;
            }

            var img = parent.GetComponent<Image>();
            if (img == null)
            {
                img = parent.gameObject.AddComponent<Image>();
            }

            img.enabled = true;
            img.sprite = UiThemeTokens.GetRoundedSprite(16);
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            var outline = parent.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null)
            {
                UnityEngine.Object.DestroyImmediate(outline);
            }

            var parentRect = parent.GetComponent<RectTransform>();
            if (parentRect != null)
            {
                parentRect.sizeDelta = new Vector2(Mathf.Max(parentRect.sizeDelta.x, 86f), 30f);
            }

            var textRect = text.GetComponent<RectTransform>();
            if (textRect != null)
            {
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(16f, 0f);
                textRect.offsetMax = new Vector2(-16f, 0f);
            }

            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = false;
        }

        private static void StyleTagParent(Text text, Color bgColor, Color borderColor, int radius, bool keepInteractable)
        {
            if (text == null) return;

            var parent = text.transform.parent;
            if (parent == null || parent == text.transform) return;

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == "Backplate")
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            var parentRect = parent.GetComponent<RectTransform>();
            if (parentRect != null)
            {
                var isPill = radius >= 90;
                parentRect.sizeDelta = isPill
                    ? new Vector2(Mathf.Max(parentRect.sizeDelta.x, 64f), 26f)
                    : new Vector2(Mathf.Max(parentRect.sizeDelta.x, 96f), 32f);
            }

            var img = parent.GetComponent<Image>();
            if (img == null)
            {
                img = parent.gameObject.AddComponent<Image>();
            }

            img.enabled = true;
            img.sprite = UiThemeTokens.GetRoundedSprite(radius >= 90 ? 13 : radius);
            img.type = Image.Type.Sliced;
            img.color = bgColor;

            var outline = parent.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null)
            {
                UnityEngine.Object.DestroyImmediate(outline);
            }

            if (bgColor != borderColor)
            {
                outline = parent.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = borderColor;
                outline.effectDistance = new Vector2(1f, -1f);
            }

            var btn = parent.GetComponent<Button>();
            if (btn != null)
            {
                var colors = btn.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.pressedColor = Color.white;
                colors.selectedColor = Color.white;
                btn.colors = colors;

                var nav = btn.navigation;
                nav.mode = Navigation.Mode.None;
                btn.navigation = nav;

                if (!keepInteractable)
                {
                    btn.interactable = false;
                }
            }

            var textRect = text.GetComponent<RectTransform>();
            if (textRect != null)
            {
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(6f, 0f);
                textRect.offsetMax = new Vector2(-6f, 0f);
            }

            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.resizeTextForBestFit = false;
        }

        private void StyleModal(GameObject modal)
        {
            if (modal == null) return;
            var modalImages = modal.GetComponentsInChildren<Image>(true);
            foreach (var img in modalImages)
            {
                if (img.color.r > 0.9f && img.color.g > 0.9f && img.color.b > 0.9f && img.rectTransform.rect.width > 200)
                {
                    img.sprite = UiThemeTokens.GetRoundedSprite(16);
                    img.type = Image.Type.Sliced;
                    img.color = UiThemeTokens.CardBackground;
                    
                    if (img.gameObject.GetComponent<UnityEngine.UI.Shadow>() == null)
                    {
                        var shadow = img.gameObject.AddComponent<UnityEngine.UI.Shadow>();
                        shadow.effectColor = new Color(0, 0, 0, 0.15f);
                        shadow.effectDistance = new Vector2(0, -8);
                    }
                }
            }
        }

        private void StyleCloseButton(Button closeBtn)
        {
            if (closeBtn == null) return;
            
            var outline = closeBtn.GetComponent<UnityEngine.UI.Outline>();
            if (outline != null) Destroy(outline);

            var closeBg = closeBtn.GetComponent<Image>();
            if (closeBg != null)
            {
                closeBg.sprite = UiThemeTokens.GetRoundedSprite(16);
                closeBg.type = Image.Type.Sliced;
                closeBg.color = new Color(0.94f, 0.95f, 0.97f);
            }
            var closeText = closeBtn.GetComponentInChildren<Text>();
            if (closeText != null)
            {
                closeText.color = UiThemeTokens.TextMuted;
                closeText.text = "✕";
                closeText.fontSize = 20;
            }
        }

        private void Awake()
        {
            // Awake 负责连接场景绑定、目录和动态卡片刷新入口。重复进入页面时必须复用/清理既有 listener，
            // 不能让同一张图纸卡因 UI rebuild 触发多次预览、加载或练习请求。
            // 初始化先读取/整理 Catalog 与卡片，再绑定筛选和预览按钮；页面状态只在本控制器内维护。

            // Scene-authored cards are legacy gallery placeholders. Keep one as
            // the clone template, but exclude every static card from filtering
            // and counts so the gallery is driven only by template_catalog.json.
            for (var i = 0; i < blueprintButtons.Count; i++)
            {
                dynamicTemplates.Add(null);
                if (i < blueprintCategories.Count)
                {
                    blueprintCategories[i] = -1;
                }

                if (i < blueprintDifficulties.Count)
                {
                    blueprintDifficulties[i] = -1;
                }

                if (i < blueprintCards.Count && blueprintCards[i] != null)
                {
                    blueprintCards[i].SetActive(false);
                }
            }

            var catalogJson = Resources.Load<TextAsset>("Blueprints/Templates/template_catalog");
            if (catalogJson != null)
            {
                var catalog = JsonUtility.FromJson<ElectricalSim.Templates.CircuitTemplateCatalogDto>(catalogJson.text);
                if (catalog != null && catalog.templates != null)
                {
                    catalogTemplates.Clear();
                    catalogTemplates.AddRange(catalog.templates);
                }

                if (catalog != null && catalog.templates != null && blueprintCards.Count > 0)
                {
                    var templateCard = blueprintCards[0];
                    foreach (var item in catalog.templates)
                    {
                        if (item.category == "\u5bb6\u5ead\u7535\u8def" || item.category == "\u5de5\u4e1a\u7535\u8def")
                        {
                            var newCard = Instantiate(templateCard, cardContent);
                            var newIndex = blueprintButtons.Count;
                            var categoryLabel = item.category == "\u5de5\u4e1a\u7535\u8def" ? "\u5de5\u4e1a\u7535\u8def" : "\u5bb6\u5ead\u7535\u8def";
                            
                            ApplyDynamicCardTexts(newCard, item, categoryLabel);

                            Sprite sprite = null;
                            if (!string.IsNullOrEmpty(item.thumbnailPath))
                            {
                                sprite = Resources.Load<Sprite>(item.thumbnailPath);
                            }

                            if (sprite != null && blueprintSprites.Count > 0)
                            {
                                var images = newCard.GetComponentsInChildren<Image>(true);
                                foreach (var img in images)
                                {
                                    if (img.sprite == blueprintSprites[0])
                                    {
                                        img.sprite = sprite;
                                    }
                                }
                            }

                            var btn = newCard.GetComponentInChildren<Button>(true);
                            btn.onClick.RemoveAllListeners();
                            btn.onClick.AddListener(() => OpenPreview(newIndex));

                            blueprintButtons.Add(btn);
                            blueprintCards.Add(newCard);
                            blueprintNames.Add(item.templateName);
                            blueprintSprites.Add(sprite);
                            blueprintCategories.Add(item.category == "\u5de5\u4e1a\u7535\u8def" ? 0 : 1);

                            int diff = 0;
                            if (!string.IsNullOrEmpty(item.difficulty) && (item.difficulty.Contains("\u4e2d") || item.difficulty.Contains("\u8fdb\u9636"))) diff = 1;
                            if (!string.IsNullOrEmpty(item.difficulty) && item.difficulty.Contains("\u9ad8")) diff = 2;
                            blueprintDifficulties.Add(diff);
                            blueprintRecommendations.Add(item.description);
                            dynamicTemplates.Add(item);
                        }
                    }
                }
            }

            for (var i = 0; i < blueprintButtons.Count; i++)
            {
                var index = i;
                blueprintButtons[i].onClick.AddListener(() => OpenPreview(index));
            }

            previewCloseButton?.onClick.AddListener(ClosePreview);
            previewCancelButton?.onClick.AddListener(ClosePreview);
            configureButton?.onClick.AddListener(EnterConfiguration);
            referenceCloseButton?.onClick.AddListener(HideReference);
            for (var i = 0; i < categoryButtons.Count; i++)
            {
                var category = i;
                categoryButtons[i].onClick.AddListener(() => SetCategory(category));
            }

            for (var i = 0; i < difficultyButtons.Count; i++)
            {
                var difficulty = i - 1;
                difficultyButtons[i].onClick.AddListener(() => SetDifficulty(difficulty));
            }

            if (categoryButtons != null)
            {
                int cat0Count = 0;
                int cat1Count = 0;
                for (int i = 0; i < blueprintCategories.Count; i++)
                {
                    if (blueprintCategories[i] == 0) cat0Count++;
                    if (blueprintCategories[i] == 1) cat1Count++;
                }
                
                if (categoryButtons.Count > 0)
                {
                    SetButtonText(categoryButtons[0], $"\u5de5\u4e1a\u7535\u8def\u56fe\u7eb8({cat0Count})");
                }
                if (categoryButtons.Count > 1)
                {
                    SetButtonText(categoryButtons[1], $"\u5bb6\u5ead\u7535\u8def\u56fe\u7eb8({cat1Count})");
                }
            }

            searchInput?.onValueChanged.AddListener(_ => { currentPage = 0; ApplyFilter(); });
            ClosePreview();
            HideReference();
            ApplyFilter();
            ApplyTheme();
        }

        private void OpenPreview(int index)
        {
            // 预览只读取目录项并展示缩略图与说明；它不加载模板、不清空 Workspace，也不建立练习会话。
            // 预览只消费当前 Catalog 项的展示资源，不加载模板、不改写 Workspace，也不改变练习评分状态。
            selectedIndex = index;
            if (previewModal != null)
            {
                previewModal.SetActive(true);
            }

            ApplyBlueprint(index, previewTitle, previewImage);
        }

        private void ClosePreview()
        {
            // 关闭预览只撤销 modal 可见性和临时选中展示，不回滚用户对当前 Workspace 或 PracticeSession 的正式状态。
            if (previewModal != null)
            {
                previewModal.SetActive(false);
            }
        }

        private void EnterConfiguration()
        {
            // 进入配置的公开入口负责处理当前 UI 状态与确认路径；真正的模板加载或练习会话由内部正式流程委托，
            // 不能通过按钮文本或卡片层级推断模板身份。
            // 此入口保留图纸集到练习/配置的既有调用顺序；不要在这里复制 TemplateLoadController 的生成算法。
            var templateItem = ResolveSelectedTemplateItem();
            if (templateItem != null)
            {
                var practiceController = ElectricalSim.Practice.PracticeSessionController.Instance;
                if (practiceController != null)
                {
                    practiceController.StartPractice(templateItem, ClosePreview);
                    return;
                }
            }

            EnterConfigurationInternal();
        }

        private ElectricalSim.Templates.CircuitTemplateCatalogItemDto ResolveSelectedTemplateItem()
        {
            if (selectedIndex >= 0 && selectedIndex < dynamicTemplates.Count && dynamicTemplates[selectedIndex] != null)
            {
                return dynamicTemplates[selectedIndex];
            }

            var candidates = new List<string>();
            AddTemplateNameCandidate(candidates, selectedIndex >= 0 && selectedIndex < blueprintNames.Count ? blueprintNames[selectedIndex] : string.Empty);

            if (selectedIndex >= 0 && selectedIndex < blueprintCards.Count && blueprintCards[selectedIndex] != null)
            {
                var labels = blueprintCards[selectedIndex].GetComponentsInChildren<Text>(true);
                for (var i = 0; i < labels.Length; i++)
                {
                    AddTemplateNameCandidate(candidates, labels[i] != null ? labels[i].text : string.Empty);
                }
            }

            for (var i = 0; i < catalogTemplates.Count; i++)
            {
                var item = catalogTemplates[i];
                if (item == null || string.IsNullOrWhiteSpace(item.templateName))
                {
                    continue;
                }

                for (var c = 0; c < candidates.Count; c++)
                {
                    var candidate = candidates[c];
                    if (string.Equals(item.templateName, candidate, System.StringComparison.OrdinalIgnoreCase)
                        || item.templateName.Contains(candidate)
                        || candidate.Contains(item.templateName))
                    {
                        return item;
                    }
                }
            }

            return null;
        }

        private static void AddTemplateNameCandidate(List<string> candidates, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var candidate = value.Trim();
            if (candidate.Length < 3)
            {
                return;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                if (string.Equals(candidates[i], candidate, System.StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            candidates.Add(candidate);
        }

        private static void ApplyDynamicCardTexts(GameObject card, ElectricalSim.Templates.CircuitTemplateCatalogItemDto item, string categoryLabel)
        {
            if (card == null || item == null)
            {
                return;
            }

            var texts = card.GetComponentsInChildren<Text>(true);
            var titleAssigned = false;
            var categoryAssigned = false;
            var difficultyAssigned = false;

            foreach (var text in texts)
            {
                if (text == null || string.IsNullOrWhiteSpace(text.text))
                {
                    continue;
                }

                var original = text.text.Trim();
                if (IsActionLabel(original))
                {
                    text.text = "\u8fdb\u5165\u7ec3\u4e60";
                    continue;
                }

                if (!categoryAssigned && IsCategoryLabel(original))
                {
                    text.text = categoryLabel;
                    categoryAssigned = true;
                    continue;
                }

                if (!difficultyAssigned && IsDifficultyLabel(original))
                {
                    text.text = item.difficulty;
                    difficultyAssigned = true;
                    continue;
                }

                if (!titleAssigned)
                {
                    text.text = item.templateName;
                    titleAssigned = true;
                }
            }
        }

        private static bool IsActionLabel(string text)
        {
            return text.Contains("\u7ec3\u4e60") || text.Contains("\u8fdb\u5165");
        }

        private static bool IsCategoryLabel(string text)
        {
            return text == "\u5bb6\u5ead"
                   || text == "\u5de5\u4e1a"
                   || text == "\u5bb6\u5ead\u7535\u8def"
                   || text == "\u5de5\u4e1a\u7535\u8def"
                   || text == "\u5bb6\u5ead\u7535\u8def\u56fe\u7eb8"
                   || text == "\u5de5\u4e1a\u7535\u8def\u56fe\u7eb8";
        }

        private static bool IsDifficultyLabel(string text)
        {
            return text == "\u521d\u7ea7"
                   || text == "\u4e2d\u7ea7"
                   || text == "\u9ad8\u7ea7"
                   || text == "\u4e2d\u9ad8\u7ea7"
                   || text == "\u5165\u95e8"
                   || text == "\u8fdb\u9636"
                   || text == "\u521d\u7ea7\u56fe\u7eb8"
                   || text == "\u4e2d\u7ea7\u56fe\u7eb8"
                   || text == "\u9ad8\u7ea7\u56fe\u7eb8"
                   || text == "\u4e2d\u9ad8\u7ea7\u56fe\u7eb8"
                   || text.Contains("\u4e2d\u9ad8");
        }

        private void EnterConfigurationInternal()
        {
            // 此处根据目录项类型选择“加载到工作区”或“进入练习”的正式入口。它只消费 guard/loader 的结果，
            // 不维护第二套锁定状态，也不能先隐藏 UI 再假定模板加载必定成功。
            // 非 PracticeSessionController 的兼容回退路径；仍按当前 selectedIndex 调用既有页面加载逻辑。
            ClosePreview();
            navigation?.SelectTab(0);

            if (referencePanel != null)
            {
                referencePanel.SetActive(true);
            }

            ApplyBlueprint(selectedIndex, referenceTitle, referenceImage);
            ApplyRecommendation(selectedIndex);
        }

        private void HideReference()
        {
            if (referencePanel != null)
            {
                referencePanel.SetActive(false);
            }
        }

        private void ApplyBlueprint(int index, Text title, Image image)
        {
            // 卡片详情是 catalog 的展示投影。图片和标题缺失可安全 fallback，但 fallback 不能改写 catalog 条目、
            // 资源路径或用户保存图纸的真实内容。
            if (title != null)
            {
                title.text = index >= 0 && index < blueprintNames.Count ? blueprintNames[index] : string.Empty;
            }

            if (image != null)
            {
                image.sprite = index >= 0 && index < blueprintSprites.Count ? blueprintSprites[index] : null;
                image.color = image.sprite != null ? Color.white : new Color(0.96f, 0.98f, 1f);
                image.preserveAspect = true;
            }
        }

        private void ApplyRecommendation(int index)
        {
            // 推荐说明用于解释当前目录项与练习变体的展示差异；它不参与模板匹配、评分或 Workspace 的实际接线恢复。
            if (referenceRecommendations == null)
            {
                return;
            }

            var item = index >= 0 && index < dynamicTemplates.Count ? dynamicTemplates[index] : null;
            var description = index >= 0 && index < blueprintRecommendations.Count ? blueprintRecommendations[index] : string.Empty;
            var noteBuilder = new StringBuilder();
            var isBVariant = item != null && string.Equals(item.diagramRiskLevel, "B", System.StringComparison.OrdinalIgnoreCase);

            if (isBVariant)
            {
                noteBuilder.Append("<color=#B45309><b>提示</b> 该原理图与系统练习版存在少量画法或元件布局差异，请以系统练习版说明为准。</color>");
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                if (noteBuilder.Length > 0)
                {
                    noteBuilder.Append("\n\n");
                }

                noteBuilder.Append("<b>电路说明</b>\n");
                noteBuilder.Append(description.Replace("|", " / "));
            }

            if (item != null && !string.IsNullOrWhiteSpace(item.referenceDiagramNote))
            {
                if (noteBuilder.Length > 0)
                {
                    noteBuilder.Append("\n\n");
                }

                noteBuilder.Append("<b>原理图说明</b>\n");
                noteBuilder.Append(item.referenceDiagramNote);
            }

            if (item != null && !string.IsNullOrWhiteSpace(item.practiceVariantNote))
            {
                if (noteBuilder.Length > 0)
                {
                    noteBuilder.Append("\n\n");
                }

                noteBuilder.Append("<b>系统练习版说明</b>\n");
                noteBuilder.Append(item.practiceVariantNote);
            }

            referenceRecommendations.text = noteBuilder.Length == 0
                ? "\u6682\u65e0\u56fe\u7eb8\u8bf4\u660e"
                : noteBuilder.ToString();
        }

        private void ApplyReferenceDetailLayout()
        {
            // 参考图区域的尺寸约束只解决可读性和缩放边界。图像及说明是只读资料，不能被当作当前图纸的拓扑或元件参数来源。
            if (referenceRecommendations != null)
            {
                referenceRecommendations.font = MainUiTheme.DenseUiFont;
                referenceRecommendations.fontSize = 14;
                referenceRecommendations.color = MainUiTheme.Hex("475569");
                referenceRecommendations.alignment = TextAnchor.UpperLeft;
                referenceRecommendations.supportRichText = true;
                referenceRecommendations.horizontalOverflow = HorizontalWrapMode.Wrap;
                referenceRecommendations.verticalOverflow = VerticalWrapMode.Overflow;
                referenceRecommendations.resizeTextForBestFit = false;

                var rect = referenceRecommendations.rectTransform;
                rect.anchorMin = new Vector2(0.04f, 0.02f);
                rect.anchorMax = new Vector2(0.96f, 0.24f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            if (referenceImage != null)
            {
                var imageRect = referenceImage.rectTransform;
                imageRect.anchorMin = new Vector2(0.04f, 0.26f);
                imageRect.anchorMax = new Vector2(0.96f, 0.86f);
                imageRect.offsetMin = Vector2.zero;
                imageRect.offsetMax = Vector2.zero;
            }
        }

        private void SetCategory(int category)
        {
            // 分类/难度/搜索只决定本页的可见卡片集合；它们不能改变目录权威数据，也不应使已选模板在运行时失去 identity。
            activeCategory = category;
            activeDifficulty = -1;
            currentPage = 0;
            ApplyFilter();
        }

        private void SetDifficulty(int difficulty)
        {
            activeDifficulty = difficulty;
            currentPage = 0;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            // 过滤后重新计算分页和按钮状态，必须以稳定的目录索引为基础。不要把过滤后的局部位置保存为模板 ID，
            // 否则切换关键字或页码后会把操作施加到另一张图纸。
            var matchIndices = new List<int>();
            for (var i = 0; i < blueprintCards.Count; i++)
            {
                var categoryMatches = i < blueprintCategories.Count && blueprintCategories[i] == activeCategory;
                var difficultyMatches = activeDifficulty < 0 || i < blueprintDifficulties.Count && blueprintDifficulties[i] == activeDifficulty;
                var searchMatches = MatchesSearch(i);
                if (categoryMatches && difficultyMatches && searchMatches)
                {
                    matchIndices.Add(i);
                }
                else
                {
                    if (blueprintCards[i] != null) blueprintCards[i].SetActive(false);
                }
            }

            var totalMatches = matchIndices.Count;
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(totalMatches / 8f));
            if (currentPage >= totalPages) currentPage = totalPages - 1;
            if (currentPage < 0) currentPage = 0;

            var startIndex = currentPage * 8;
            var endIndex = Mathf.Min(startIndex + 8, totalMatches);

            var visibleIndex = 0;
            for (var i = 0; i < matchIndices.Count; i++)
            {
                var cardIndex = matchIndices[i];
                var visible = i >= startIndex && i < endIndex;
                if (blueprintCards[cardIndex] != null) blueprintCards[cardIndex].SetActive(visible);

                if (visible)
                {
                    var rect = blueprintCards[cardIndex] != null ? blueprintCards[cardIndex].GetComponent<RectTransform>() : null;
                    if (rect != null)
                    {
                        var availableWidth = cardContent != null && cardContent.rect.width > 1f
                            ? cardContent.rect.width
                            : Mathf.Max(960f, Screen.width - PageMargin * 2f);
                        var columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth - PageMargin + CardGap) / (CardWidth + CardGap)));
                        var row = visibleIndex / columns;
                        var col = visibleIndex % columns;
                        rect.sizeDelta = new Vector2(CardWidth, CardHeight);
                        rect.anchoredPosition = new Vector2(PageMargin + col * (CardWidth + CardGap), -PageMargin - row * (CardHeight + CardGap));
                    }
                    visibleIndex++;
                }
            }

            if (cardContent != null)
            {
                var availableWidth = cardContent.rect.width > 1f ? cardContent.rect.width : Mathf.Max(960f, Screen.width - PageMargin * 2f);
                var columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth - PageMargin + CardGap) / (CardWidth + CardGap)));
                var rows = Mathf.CeilToInt(visibleIndex / (float)columns);
                cardContent.sizeDelta = new Vector2(0f, Mathf.Max(720f, PageMargin + rows * (CardHeight + CardGap) + 120f));
            }

            RefreshButtonStates();
            BuildPaginationUI(totalPages);
        }

        private bool MatchesSearch(int index)
        {
            if (searchInput == null || string.IsNullOrWhiteSpace(searchInput.text))
            {
                return true;
            }

            var name = index >= 0 && index < blueprintNames.Count ? blueprintNames[index] : string.Empty;
            return name.IndexOf(searchInput.text.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void BuildPaginationUI(int totalPages)
        {
            // 分页节点由本控制器拥有并可重复重建；旧节点及其 listener 必须随 paginationRoot 一起清理，
            // 防止目录刷新后存在指向过期索引的翻页按钮。
            if (paginationRoot == null)
            {
                paginationRoot = new GameObject("PaginationRoot", typeof(RectTransform), typeof(HorizontalLayoutGroup));
                var rect = paginationRoot.GetComponent<RectTransform>();
                rect.SetParent(cardContent != null ? cardContent.parent : transform, false);
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 20f);
                rect.sizeDelta = new Vector2(0f, 40f);

                var layout = paginationRoot.GetComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = 8f;
                layout.childControlHeight = false;
                layout.childControlWidth = false;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = false;
            }

            foreach (Transform child in paginationRoot.transform)
            {
                Destroy(child.gameObject);
            }

            if (totalPages <= 1) return;

            var prevBtn = CreatePaginationButton("< 上一页", currentPage > 0);
            prevBtn.onClick.AddListener(() => { currentPage--; ApplyFilter(); });

            for (var i = 0; i < totalPages; i++)
            {
                var pageIndex = i;
                var pageBtn = CreatePaginationButton((i + 1).ToString(), true, i == currentPage);
                if (i != currentPage)
                {
                    pageBtn.onClick.AddListener(() => { currentPage = pageIndex; ApplyFilter(); });
                }
            }

            var nextBtn = CreatePaginationButton("下一页 >", currentPage < totalPages - 1);
            nextBtn.onClick.AddListener(() => { currentPage++; ApplyFilter(); });
        }

        private Button CreatePaginationButton(string text, bool interactable, bool isCurrent = false)
        {
            var go = new GameObject("PageBtn_" + text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(paginationRoot.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(text.Length > 2 ? 80f : 40f, 36f);

            var img = go.GetComponent<Image>();
            img.sprite = UiThemeTokens.GetRoundedSprite(8);
            img.type = Image.Type.Sliced;
            img.color = interactable ? (isCurrent ? MainUiTheme.Hex("2563EB") : Color.white) : new Color(0.96f, 0.96f, 0.96f);

            if (!isCurrent)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = interactable ? MainUiTheme.Hex("D2D2D2") : MainUiTheme.Hex("E5E5E5");
                outline.effectDistance = new Vector2(1f, -1f);
            }

            var btn = go.GetComponent<Button>();
            btn.interactable = interactable;

            var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            txtGo.transform.SetParent(go.transform, false);
            var txtRect = txtGo.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = Vector2.zero;
            txtRect.offsetMax = Vector2.zero;

            var txt = txtGo.GetComponent<Text>();
            txt.text = text;
            txt.font = isCurrent ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Normal;
            txt.resizeTextForBestFit = false;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = interactable ? (isCurrent ? Color.white : MainUiTheme.Hex("464646")) : MainUiTheme.Hex("A0A0A0");

            return btn;
        }

        private void RefreshButtonStates()
        {
            // 按钮高亮仅投影当前筛选/页码/选择状态，不是模板加载、练习进入或保存完成的业务证据。
            RefreshDifficultyLabels();

            for (var i = 0; i < categoryButtons.Count; i++)
            {
                SetButtonActive(categoryButtons[i], i == activeCategory);
            }

            for (var i = 0; i < difficultyButtons.Count; i++)
            {
                SetButtonActive(difficultyButtons[i], i - 1 == activeDifficulty);
            }
        }

        private void RefreshDifficultyLabels()
        {
            // 难度标签从已筛选的目录状态重新投影，避免卡片刷新后把上一页的展示值遗留到另一张图纸。
            if (difficultyButtons.Count < 4)
            {
                return;
            }

            SetButtonText(difficultyButtons[0], $"\u5168\u90e8\u56fe\u7eb8({CountByDifficulty(-1)})");
            SetButtonText(difficultyButtons[1], $"\u521d\u7ea7\u56fe\u7eb8({CountByDifficulty(0)})");
            SetButtonText(difficultyButtons[2], $"\u4e2d\u7ea7\u56fe\u7eb8({CountByDifficulty(1)})");
            SetButtonText(difficultyButtons[3], $"\u9ad8\u7ea7\u56fe\u7eb8({CountByDifficulty(2)})");
        }

        private int CountByDifficulty(int difficulty)
        {
            var count = 0;
            for (var i = 0; i < blueprintCategories.Count; i++)
            {
                if (blueprintCategories[i] != activeCategory)
                {
                    continue;
                }

                if (difficulty < 0 || i < blueprintDifficulties.Count && blueprintDifficulties[i] == difficulty)
                {
                    count++;
                }
            }

            return count;
        }

        private static void SetButtonText(Button button, string text)
        {
            if (button == null)
            {
                return;
            }

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = text;
            }
        }

        private static void SetButtonActive(Button button, bool active)
        {
            if (button == null)
            {
                return;
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = UiThemeTokens.GetRoundedSprite(16);
                image.type = Image.Type.Sliced;
                image.color = ResolveFilterFill(button, active);
            }

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = ResolveFilterBorder(button, active);
            outline.effectDistance = new Vector2(1f, -1f);

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.font = active ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
                label.fontSize = 16;
                label.fontStyle = FontStyle.Normal;
                label.resizeTextForBestFit = false;
                label.color = ResolveFilterTextColor(button, active);
            }
        }

        private static Color ResolveFilterFill(Button button, bool active)
        {
            var text = button != null && button.GetComponentInChildren<Text>() != null ? button.GetComponentInChildren<Text>().text : string.Empty;
            if (text.Contains("\u521d\u7ea7")) return active ? MainUiTheme.Hex("DCFCE7") : MainUiTheme.Hex("F0FDF4");
            if (text.Contains("\u4e2d\u7ea7")) return active ? MainUiTheme.Hex("FFEDD5") : MainUiTheme.Hex("FFF7ED");
            if (text.Contains("\u9ad8\u7ea7")) return active ? MainUiTheme.Hex("FEE2E2") : MainUiTheme.Hex("FEF2F2");
            return active ? MainUiTheme.Hex("DBEAFE") : MainUiTheme.Hex("FFFFFF");
        }

        private static Color ResolveFilterBorder(Button button, bool active)
        {
            var text = button != null && button.GetComponentInChildren<Text>() != null ? button.GetComponentInChildren<Text>().text : string.Empty;
            if (text.Contains("\u521d\u7ea7")) return active ? MainUiTheme.Hex("16A34A") : MainUiTheme.Hex("86EFAC");
            if (text.Contains("\u4e2d\u7ea7")) return active ? MainUiTheme.Hex("F59E0B") : MainUiTheme.Hex("FDBA74");
            if (text.Contains("\u9ad8\u7ea7")) return active ? MainUiTheme.Hex("EF4444") : MainUiTheme.Hex("FCA5A5");
            return active ? MainUiTheme.Hex("2563EB") : MainUiTheme.Hex("C7D2E0");
        }

        private static Color ResolveFilterTextColor(Button button, bool active)
        {
            var text = button != null && button.GetComponentInChildren<Text>() != null ? button.GetComponentInChildren<Text>().text : string.Empty;
            if (text.Contains("\u521d\u7ea7")) return active ? MainUiTheme.Hex("15803D") : MainUiTheme.Hex("16A34A");
            if (text.Contains("\u4e2d\u7ea7")) return active ? MainUiTheme.Hex("C2410C") : MainUiTheme.Hex("EA580C");
            if (text.Contains("\u9ad8\u7ea7")) return active ? MainUiTheme.Hex("DC2626") : MainUiTheme.Hex("EF4444");
            return active ? MainUiTheme.Hex("2563EB") : MainUiTheme.Hex("4B5563");
        }
    }
}


