using System;
using System.Collections.Generic;
using ElectricalSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    public sealed class EncyclopediaController : MonoBehaviour
    {
        private const string CategoryAll = "全部";
        private const float SidebarWidth = 150f;
        private const float CardWidth = 370f;
        private const float CardHeight = 160f;
        private const float CardGapX = 18f;
        private const float CardGapY = 18f;

        [SerializeField] private List<Button> categoryButtons = new List<Button>();
        [SerializeField] private List<Text> categoryLabels = new List<Text>();
        [SerializeField] private List<RectTransform> categorySections = new List<RectTransform>();
        [SerializeField] private RectTransform content;

        private readonly List<ComponentEncyclopediaEntry> entries = new List<ComponentEncyclopediaEntry>();
        private readonly List<RectTransform> cardRects = new List<RectTransform>();
        private readonly string[] categories =
        {
            CategoryAll,
            "开关按钮",
            "控制元件",
            "传感元件",
            "保护设备",
            "电源仪表",
            "用电设备",
            "端子与模块",
            "其他"
        };

        private RectTransform listViewRoot;
        private RectTransform detailViewRoot;
        private RectTransform cardContent;
        private RectTransform detailContent;
        private ScrollRect cardScrollRect;
        private GridLayoutGroup cardGridLayout;
        private ScrollRect detailScrollRect;
        private InputField searchInput;
        private Text emptyText;
        private Text detailHintText;
        private string selectedCategory = CategoryAll;
        private string searchText = string.Empty;
        private Sprite fallbackSprite;

        private void Start()
        {
            BuildPage();
        }

        private void BuildPage()
        {
            ClearExistingChildren();
            entries.Clear();
            entries.AddRange(ComponentEncyclopediaDatabase.Build(LoadDefinitions()));

            CreateHeader();
            CreateListView();
            CreateDetailView();
            ShowListView();
            RefreshCards();
        }

        private List<ComponentDefinition> LoadDefinitions()
        {
            var definitions = new List<ComponentDefinition>();
            var saveLoadService = FindObjectOfType<SaveLoadService>();
            if (saveLoadService != null && saveLoadService.Catalog != null)
            {
                foreach (var definition in saveLoadService.Catalog)
                {
                    if (definition != null && definition.showInPalette)
                    {
                        definitions.Add(definition);
                    }
                }
            }

#if UNITY_EDITOR
            if (definitions.Count == 0)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" });
                for (var i = 0; i < guids.Length; i++)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                    var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                    if (definition != null && definition.showInPalette)
                    {
                        definitions.Add(definition);
                    }
                }
            }
#endif

            definitions.Sort((a, b) => string.Compare(GetDisplayName(a), GetDisplayName(b), StringComparison.Ordinal));
            return definitions;
        }

        private void ClearExistingChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            categoryButtons.Clear();
            categoryLabels.Clear();
            categorySections.Clear();
        }

        private void CreateHeader()
        {
            var title = CreateText("EncyclopediaTitle", transform, "元器件百科", 30, FontStyle.Bold, MainUiTheme.Hex("111827"), true);
            MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.PageTitle);
            title.alignment = TextAnchor.MiddleLeft;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -30f), new Vector2(320f, 54f));

            var subtitle = CreateText("EncyclopediaSubtitle", transform, "按元件类型整理用途、端子、接线方式和仿真规则，点击卡片查看详情。", 15, FontStyle.Normal, MainUiTheme.Hex("64748B"));
            MainUiTheme.ApplyTextRole(subtitle, MainUiTheme.UiTextRole.PageSubtitle);
            subtitle.alignment = TextAnchor.MiddleLeft;
            SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(360f, -34f), new Vector2(-560f, 48f));

            var searchPanel = CreatePanel("SearchBox", transform, Color.white);
            SetRect(searchPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -32f), new Vector2(300f, 42f));
            var searchImage = searchPanel.GetComponent<Image>();
            if (searchImage != null)
            {
                searchImage.sprite = UiThemeTokens.GetRoundedSprite(8);
                searchImage.type = Image.Type.Sliced;
            }
            var outline = searchPanel.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("E2E8F0");
            outline.effectDistance = new Vector2(1f, -1f);

            searchInput = searchPanel.gameObject.AddComponent<InputField>();
            var searchTextLabel = CreateText("Text", searchPanel, string.Empty, 15, FontStyle.Normal, MainUiTheme.Hex("111827"));
            searchTextLabel.alignment = TextAnchor.MiddleLeft;
            SetRect(searchTextLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 0f), new Vector2(-16f, 0f));
            var placeholder = CreateText("Placeholder", searchPanel, "搜索名称、端子或用途", 15, FontStyle.Normal, MainUiTheme.Hex("94A3B8"));
            placeholder.alignment = TextAnchor.MiddleLeft;
            SetRect(placeholder.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 0f), new Vector2(-16f, 0f));
            searchInput.textComponent = searchTextLabel;
            searchInput.placeholder = placeholder;
            searchInput.onValueChanged.AddListener(value =>
            {
                searchText = value ?? string.Empty;
                RefreshCards();
            });
        }

        private void CreateListView()
        {
            listViewRoot = CreateRect("ListViewRoot", transform);
            SetRect(listViewRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var sidebar = CreatePanel("CategorySidebar", listViewRoot, Color.white);
            SetRect(sidebar, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(16f, -40f), new Vector2(SidebarWidth, -128f));
            var sidebarImage = sidebar.GetComponent<Image>();
            if (sidebarImage != null)
            {
                sidebarImage.sprite = UiThemeTokens.GetRoundedSprite(10);
                sidebarImage.type = Image.Type.Sliced;
                sidebarImage.color = Color.white;
            }
            AddSoftOutline(sidebar);

            var sidebarTitle = CreateText("SidebarTitle", sidebar, "分类", 18, FontStyle.Bold, MainUiTheme.Hex("1F2937"));
            sidebarTitle.alignment = TextAnchor.MiddleLeft;
            SetRect(sidebarTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(14f, -16f), new Vector2(-28f, 38f));

            for (var i = 0; i < categories.Length; i++)
            {
                var category = categories[i];
                var buttonRect = CreatePanel("Category_" + category, sidebar, Color.clear);
                SetRect(buttonRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f - i * 36f), new Vector2(-24f, 32f));
                var btnImage = buttonRect.GetComponent<Image>();
                if (btnImage != null)
                {
                    btnImage.sprite = UiThemeTokens.GetRoundedSprite(8);
                    btnImage.type = Image.Type.Sliced;
                }
                var button = buttonRect.gameObject.AddComponent<Button>();
                var label = CreateText("Text", buttonRect, category, 15, FontStyle.Normal, MainUiTheme.Hex("334155"));
                label.alignment = TextAnchor.MiddleLeft;
                SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(12f, 0f), new Vector2(-12f, 0f));
                var captured = category;
                button.onClick.AddListener(() =>
                {
                    selectedCategory = captured;
                    RefreshCards();
                });
                categoryButtons.Add(button);
                categoryLabels.Add(label);
            }

            var scrollRoot = CreatePanel("CardScrollView", listViewRoot, new Color(1f, 1f, 1f, 0.01f));
            StretchTo(scrollRoot, 178f, 104f, 32f, 24f);

            var viewport = CreatePanel("Viewport", scrollRoot, new Color(1f, 1f, 1f, 0.01f));
            StretchTo(viewport, 0f, 0f, 0f, 0f);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            cardContent = CreateRect("CardGridContent", viewport);
            cardContent.anchorMin = new Vector2(0.5f, 1f);
            cardContent.anchorMax = new Vector2(0.5f, 1f);
            cardContent.pivot = new Vector2(0.5f, 1f);
            cardContent.anchoredPosition = Vector2.zero;
            cardContent.sizeDelta = Vector2.zero;

            cardGridLayout = cardContent.gameObject.AddComponent<GridLayoutGroup>();
            cardGridLayout.cellSize = new Vector2(CardWidth, CardHeight);
            cardGridLayout.spacing = new Vector2(CardGapX, CardGapY);
            cardGridLayout.padding = new RectOffset(0, 0, 0, 24);
            cardGridLayout.childAlignment = TextAnchor.UpperLeft;
            cardGridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            cardGridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            cardGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cardGridLayout.constraintCount = 3;

            var fitter = cardContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            cardScrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            cardScrollRect.viewport = viewport;
            cardScrollRect.content = cardContent;
            cardScrollRect.horizontal = false;
            cardScrollRect.vertical = true;
            cardScrollRect.movementType = ScrollRect.MovementType.Clamped;
            cardScrollRect.scrollSensitivity = 28f;

            emptyText = CreateText("EmptyText", listViewRoot, "未找到相关元器件", 18, FontStyle.Normal, new Color(0.40f, 0.46f, 0.55f));
            emptyText.alignment = TextAnchor.MiddleCenter;
            StretchTo(emptyText.rectTransform, 290f, 104f, 32f, 24f);
            emptyText.gameObject.SetActive(false);
        }

        private void CreateDetailView()
        {
            detailViewRoot = CreateRect("DetailViewRoot", transform);
            SetRect(detailViewRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            detailViewRoot.gameObject.SetActive(false);

            var backButton = CreateButton("BackButton", detailViewRoot, "返回百科", MainUiTheme.Hex("EFF6FF"), MainUiTheme.Hex("2563EB"), 16);
            var backBtnImage = backButton.GetComponent<Image>();
            if (backBtnImage != null)
            {
                backBtnImage.sprite = UiThemeTokens.GetRoundedSprite(8);
                backBtnImage.type = Image.Type.Sliced;
            }
            SetRect(backButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -92f), new Vector2(118f, 38f));
            backButton.onClick.AddListener(ShowListView);

            detailHintText = CreateText("DetailHint", detailViewRoot, string.Empty, 14, FontStyle.Normal, new Color(0.79f, 0.39f, 0.08f));
            detailHintText.alignment = TextAnchor.MiddleLeft;
            SetRect(detailHintText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(172f, -92f), new Vector2(-40f, 38f));

            var scrollRoot = CreatePanel("DetailScrollView", detailViewRoot, new Color(1f, 1f, 1f, 0.01f));
            StretchTo(scrollRoot, 34f, 144f, 34f, 38f);

            var viewport = CreatePanel("Viewport", scrollRoot, new Color(1f, 1f, 1f, 0.01f));
            StretchTo(viewport, 0f, 0f, 0f, 0f);
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            detailContent = CreateRect("DetailContent", viewport);
            detailContent.anchorMin = new Vector2(0f, 1f);
            detailContent.anchorMax = new Vector2(1f, 1f);
            detailContent.pivot = new Vector2(0.5f, 1f);
            detailContent.anchoredPosition = Vector2.zero;
            detailContent.sizeDelta = Vector2.zero;

            var layout = detailContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 14f;
            layout.padding = new RectOffset(16, 16, 16, 24);

            var fitter = detailContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            detailScrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            detailScrollRect.viewport = viewport;
            detailScrollRect.content = detailContent;
            detailScrollRect.horizontal = false;
            detailScrollRect.vertical = true;
            detailScrollRect.movementType = ScrollRect.MovementType.Clamped;
            detailScrollRect.scrollSensitivity = 30f;
        }

        private void RefreshCards()
        {
            for (var i = cardContent.childCount - 1; i >= 0; i--)
            {
                Destroy(cardContent.GetChild(i).gameObject);
            }

            cardRects.Clear();
            UpdateCategoryButtonState();

            var filtered = new List<ComponentEncyclopediaEntry>();
            foreach (var entry in entries)
            {
                if (entry == null || !MatchesCategory(entry) || !MatchesSearch(entry))
                {
                    continue;
                }

                filtered.Add(entry);
            }

            var viewportWidth = cardScrollRect != null && cardScrollRect.viewport != null
                ? cardScrollRect.viewport.rect.width
                : 0f;
            if (viewportWidth <= 1f)
            {
                viewportWidth = Mathf.Max(CardWidth, Screen.width - SidebarWidth - 140f);
            }

            var columns = Mathf.Max(1, Mathf.FloorToInt((viewportWidth + CardGapX) / (CardWidth + CardGapX)));
            cardGridLayout.constraintCount = columns;

            for (var i = 0; i < filtered.Count; i++)
            {
                var card = CreateCard(filtered[i]);
                cardRects.Add(card);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(cardContent);
            cardContent.anchoredPosition = Vector2.zero;
            if (cardScrollRect != null)
            {
                cardScrollRect.verticalNormalizedPosition = 1f;
            }

            emptyText.gameObject.SetActive(filtered.Count == 0);
        }

        private RectTransform CreateCard(ComponentEncyclopediaEntry entry)
        {
            var card = CreatePanel("ComponentCard_" + entry.DefinitionName, cardContent, Color.white);
            card.sizeDelta = new Vector2(CardWidth, CardHeight);
            var cardLayout = card.gameObject.AddComponent<LayoutElement>();
            cardLayout.preferredWidth = CardWidth;
            cardLayout.preferredHeight = CardHeight;
            
            var cardImage = card.GetComponent<Image>();
            if (cardImage != null)
            {
                cardImage.sprite = UiThemeTokens.GetRoundedSprite(8);
                cardImage.type = Image.Type.Sliced;
            }

            var button = card.gameObject.AddComponent<Button>();
            button.targetGraphic = cardImage;
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("E2E8F0");
            outline.effectDistance = new Vector2(1f, -1f);
            button.onClick.AddListener(() => ShowDetail(entry));

            var thumbnailArea = CreatePanel("ThumbnailArea", card, MainUiTheme.Hex("F1F5F9"));
            SetRect(thumbnailArea, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(125f, -28f));
            var thumbImage = thumbnailArea.GetComponent<Image>();
            if (thumbImage != null)
            {
                thumbImage.sprite = UiThemeTokens.GetRoundedSprite(8);
                thumbImage.type = Image.Type.Sliced;
            }
            var mask = thumbnailArea.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            var image = CreateRect("ThumbnailImage", thumbnailArea);
            var imageComponent = image.gameObject.AddComponent<Image>();
            var sprite = ResolveIcon(entry.Definition);
            imageComponent.sprite = sprite != null ? sprite : GetFallbackSprite();
            imageComponent.color = sprite != null ? Color.white : Color.clear;
            imageComponent.preserveAspect = true;
            imageComponent.raycastTarget = false;
            
            SetRect(image, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(105f, 125f));

            var infoArea = CreateRect("InfoArea", card);
            SetRect(infoArea, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            infoArea.offsetMin = new Vector2(150f, 16f);
            infoArea.offsetMax = new Vector2(-16f, -18f);

            var infoLayout = infoArea.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.childAlignment = TextAnchor.MiddleLeft;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            infoLayout.spacing = 4f;

            var name = CreateText("NameText", infoArea, entry.DisplayName, 17, FontStyle.Bold, MainUiTheme.Hex("1F2937"));
            name.alignment = TextAnchor.MiddleLeft;
            name.lineSpacing = 1.1f;
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            name.verticalOverflow = VerticalWrapMode.Truncate;
            SetTextLayoutHeight(name, 40f);

            var category = CreateText("CategoryText", infoArea, entry.Category, 14, FontStyle.Normal, MainUiTheme.Hex("64748B"));
            category.alignment = TextAnchor.MiddleLeft;
            category.horizontalOverflow = HorizontalWrapMode.Overflow;
            category.verticalOverflow = VerticalWrapMode.Truncate;
            SetTextLayoutHeight(category, 18f);

            var param = CreateText("ParamText", infoArea, BuildBasicSummary(entry), 14, FontStyle.Normal, MainUiTheme.Hex("2563EB"));
            param.alignment = TextAnchor.MiddleLeft;
            param.horizontalOverflow = HorizontalWrapMode.Overflow;
            param.verticalOverflow = VerticalWrapMode.Truncate;
            SetTextLayoutHeight(param, 18f);

            var terminals = CreateText("TerminalText", infoArea, BuildCardTerminalSummary(entry.Definition, entry.Terminals), 14, FontStyle.Normal, MainUiTheme.Hex("475569"));
            terminals.alignment = TextAnchor.MiddleLeft;
            terminals.lineSpacing = 1.2f;
            terminals.horizontalOverflow = HorizontalWrapMode.Wrap;
            terminals.verticalOverflow = VerticalWrapMode.Truncate;
            SetTextLayoutHeight(terminals, 34f);

            return card;
        }

        private void ShowDetail(ComponentEncyclopediaEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            listViewRoot.gameObject.SetActive(false);
            detailViewRoot.gameObject.SetActive(true);
            detailHintText.text = entry.ContentCompleted ? string.Empty : "该元件百科内容仍在完善中。";

            for (var i = detailContent.childCount - 1; i >= 0; i--)
            {
                Destroy(detailContent.GetChild(i).gameObject);
            }

            CreateDetailHeader(entry);
            CreateSection("元件用途", entry.Purpose);
            CreateSection("端子说明", entry.TerminalDescription);
            CreateSection("工作状态", entry.WorkingState);
            CreateSection("常见接线方式", entry.WiringUsage);
            CreateSection("常见错误", entry.CommonMistakes);
            CreateSection("本系统中的仿真规则", entry.SimulationRule);
            CreateSection("安全提示", entry.SafetyTips);

            LayoutRebuilder.ForceRebuildLayoutImmediate(detailContent);
            detailContent.anchoredPosition = Vector2.zero;
            if (detailScrollRect != null)
            {
                detailScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void ShowListView()
        {
            detailViewRoot.gameObject.SetActive(false);
            listViewRoot.gameObject.SetActive(true);
            RefreshCards();
        }

        private void CreateDetailHeader(ComponentEncyclopediaEntry entry)
        {
            var header = CreatePanel("HeaderCard", detailContent, Color.white);
            var headerImage = header.GetComponent<Image>();
            if (headerImage != null)
            {
                headerImage.sprite = UiThemeTokens.GetRoundedSprite(12);
                headerImage.type = Image.Type.Sliced;
            }
            AddSoftOutline(header);
            var headerLayoutElement = header.gameObject.AddComponent<LayoutElement>();
            headerLayoutElement.preferredHeight = 258f;
            headerLayoutElement.minHeight = 238f;

            var headerLayout = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(24, 24, 20, 20);
            headerLayout.spacing = 28f;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;

            var imageArea = CreatePanel("ImagePanel", header, MainUiTheme.Hex("F1F5F9"));
            var imageLayout = imageArea.gameObject.AddComponent<LayoutElement>();
            imageLayout.preferredWidth = 210f;
            imageLayout.preferredHeight = 210f;
            imageLayout.minWidth = 180f;
            imageLayout.minHeight = 180f;
            var image = imageArea.GetComponent<Image>();
            var sprite = ResolveIcon(entry.Definition);
            image.sprite = sprite != null ? sprite : GetFallbackSprite();
            image.color = sprite != null ? Color.white : Color.clear;
            image.preserveAspect = true;
            image.raycastTarget = false;

            var infoPanel = CreateRect("BasicInfoPanel", header);
            var infoLayoutElement = infoPanel.gameObject.AddComponent<LayoutElement>();
            infoLayoutElement.flexibleWidth = 1f;
            infoLayoutElement.preferredHeight = 210f;
            var infoLayout = infoPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            infoLayout.childAlignment = TextAnchor.UpperLeft;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            infoLayout.spacing = 6f;

            var title = CreateText("NameText", infoPanel, entry.DisplayName, 20, FontStyle.Bold, MainUiTheme.Hex("1F2937"));
            title.alignment = TextAnchor.MiddleLeft;
            title.verticalOverflow = VerticalWrapMode.Overflow;
            title.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

                        AddDetailLine(infoPanel, "分类：" + entry.Category);
            AddDetailLine(infoPanel, "适用电路：" + entry.CircuitType);
            AddDetailLine(infoPanel, "核心参数：" + BuildBasicSummary(entry));
            AddDetailLine(infoPanel, "端子：" + BuildTerminalSummary(entry.Definition, entry.Terminals), 52f);
        }

        private void AddDetailLine(Transform parent, string text, float preferredHeight = 24f)
        {
            var line = CreateText("InfoLine", parent, text, 15, FontStyle.Normal, MainUiTheme.Hex("334155"));
            line.alignment = TextAnchor.UpperLeft;
            line.verticalOverflow = VerticalWrapMode.Overflow;
            line.horizontalOverflow = HorizontalWrapMode.Wrap;
            line.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
        }

        private void CreateSection(string title, string body)
        {
            body = string.IsNullOrWhiteSpace(body) ? "该部分内容待补充。" : body;
            var card = CreatePanel("Section_" + title, detailContent, Color.white);
            var cardImage = card.GetComponent<Image>();
            if (cardImage != null)
            {
                cardImage.sprite = UiThemeTokens.GetRoundedSprite(12);
                cardImage.type = Image.Type.Sliced;
            }
            AddSoftOutline(card);

            var cardLayoutElement = card.gameObject.AddComponent<LayoutElement>();
            cardLayoutElement.minHeight = 112f;
            cardLayoutElement.preferredHeight = Mathf.Clamp(96f + Mathf.CeilToInt(body.Length / 34f) * 22f, 128f, 260f);

            var layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 14, 16);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleText = CreateText("Title", card, title, 16, FontStyle.Bold, MainUiTheme.Hex("1F2937"));
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

            var bodyText = CreateText("Body", card, body, 15, FontStyle.Normal, MainUiTheme.Hex("334155"));
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.gameObject.AddComponent<LayoutElement>().preferredHeight = Mathf.Clamp(44f + Mathf.CeilToInt(body.Length / 34f) * 22f, 56f, 190f);
        }

        private bool MatchesCategory(ComponentEncyclopediaEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedCategory) ||
                string.Equals(selectedCategory, CategoryAll, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(selectedCategory, "All", StringComparison.OrdinalIgnoreCase) ||
                selectedCategory == "全部")
            {
                return true;
            }

            return string.Equals(entry.Category, selectedCategory, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesSearch(ComponentEncyclopediaEntry entry)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return true;
            }

            var query = searchText.Trim();
            
            var defName = entry.DefinitionName ?? "";
            var aliases = "";
            if (defName.Contains("Contactor")) aliases = "接触器 KM Contactor";
            else if (defName.Contains("Timer_")) aliases = "KT 时间继电器 延时继电器";
            else if (defName.Contains("ThermalRelay")) aliases = "FR 热继 过载保护";
            else if (defName.Contains("LimitSwitch")) aliases = "SQ 限位开关 行程开关";
            else if (defName.Contains("EmergencyStop")) aliases = "急停 急停开关 Emergency Stop";
            else if (defName.Contains("Motor_")) aliases = "电机 三相电动机 Motor";
            else if (defName.Contains("TerminalBlock")) aliases = "端子 接线端子 Terminal Block";
            else if (defName.Contains("Indicator_")) aliases = "信号灯 运行灯 报警灯";
            else if (defName.Contains("SwitchPower")) aliases = "开关电源 稳压电源";
            else if (defName.Contains("PLC_")) aliases = "PLC 可编程控制器";
            else if (defName.Contains("SolenoidValve")) aliases = "电磁阀 气动";
            else if (defName.Contains("Stepper")) aliases = "步进 驱动器";
            else if (defName.Contains("Tool_Oscilloscope")) aliases = "示波器 测量";
            
            return Contains(entry.DisplayName, query) ||
                   Contains(entry.Category, query) ||
                   Contains(entry.Purpose, query) ||
                   Contains(entry.TerminalDescription, query) ||
                   Contains(aliases, query) ||
                   Contains(BuildTerminalSummary(entry.Definition, entry.Terminals), query);
        }

        private void UpdateCategoryButtonState()
        {
            for (var i = 0; i < categoryButtons.Count; i++)
            {
                var active = i < categories.Length && categories[i] == selectedCategory;
                var image = categoryButtons[i].GetComponent<Image>();
                if (image != null)
                {
                    image.color = active ? MainUiTheme.Hex("EAF2FF") : Color.clear;
                }

                if (i < categoryLabels.Count && categoryLabels[i] != null)
                {
                    categoryLabels[i].font = active ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
                    categoryLabels[i].fontSize = 15;
                    categoryLabels[i].fontStyle = FontStyle.Normal;
                    categoryLabels[i].color = active ? MainUiTheme.Hex("2563EB") : MainUiTheme.Hex("334155");
                }
            }
        }

        private static string BuildBasicSummary(ComponentEncyclopediaEntry entry)
        {
            var defName = entry.DefinitionName ?? "";
            if (defName.Contains("AC_220V_Power")) return "电压 220V";
            if (defName.Contains("AC_ThreePhase_Power")) return "电压 380V";
            if (defName.Contains("Button_Start_NO")) return "电压 220V / 电流 5A";
            if (defName.Contains("Button_Stop_NC")) return "电压 220V / 电流 5A";
            if (defName.Contains("Button_Compound")) return "电压 220V / 电流 5A";
            if (defName.Contains("EmergencyStop")) return "电压 220V / 常闭触点";
            if (defName.Contains("Single_Control_Switch")) return "电压 220V / 电流 5A";
            if (defName.Contains("Two_Way_Switch")) return "电压 220V / 电流 5A";
            if (defName.Contains("Contactor_KM_220V")) return "线圈 220V";
            if (defName.Contains("Contactor_KM_380V")) return "线圈 380V";
            if (defName.Contains("ThermalRelay")) return "过载保护 / 95-96 NC";
            if (defName.Contains("Timer_OnDelay")) return "电压 220V / 延时触点";
            if (defName.Contains("LimitSwitch")) return "电压 220V / NO+NC";
            if (defName.Contains("KnifeSwitch")) return "电压 380V / 电流 32A";
            if (defName.Contains("Breaker")) return "按极数配置 / 保护开关";
            if (defName.Contains("Fuse")) return "过流保护 / 熔断断开";
            if (defName.Contains("Motor_ThreePhase")) return "电压 380V";
            if (defName.Contains("Motor_StarDelta")) return "电压 380V / 六端子";
            if (defName.Contains("Lamp")) return "电压 220V";
            if (defName.Contains("Fan")) return "电压 220V";
            if (defName.Contains("Single_Phase_Meter")) return "电压 220V";
            if (defName.Contains("Indicator_")) return "按电压和颜色区分";
            if (defName.Contains("TerminalBlock")) return "按位导通";
            if (defName.Contains("Tool_Multimeter")) return "测量工具 / 图片待补充";
            if (defName.Contains("SwitchPower")) return "直流供电 / 待完善";
            if (defName.Contains("PLC_")) return "逻辑控制 / 待完善";
            if (defName.Contains("SolenoidValve")) return "气液控制 / 待完善";
            if (defName.Contains("StepperDriver")) return "脉冲驱动 / 待完善";
            if (defName.Contains("StepperMotor")) return "精确步进 / 待完善";
            if (defName.Contains("Tool_Oscilloscope")) return "波形分析 / 图片待补充";
            if (defName.Contains("Button_SelfLock")) return "电压 220V / 电流 5A";
            if (defName.Contains("Timer_OffDelay")) return "待支持 / 延时触点";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.RatedVoltage)) parts.Add("电压 " + entry.RatedVoltage);
            if (!string.IsNullOrWhiteSpace(entry.RatedCurrent)) parts.Add("电流 " + entry.RatedCurrent);
            return parts.Count > 0 ? string.Join(" / ", parts) : "参数：待补充";
        }

                private static string BuildDetailBasicInfo(ComponentEncyclopediaEntry entry)
        {
            return "名称：" + entry.DisplayName +
                   "\n分类：" + entry.Category +
                   "\n适用电路：" + entry.CircuitType +
                   "\n核心参数：" + BuildBasicSummary(entry) +
                   "\n引脚端子：" + BuildTerminalSummary(entry.Definition, entry.Terminals);
        }

        private static string BuildTerminalSummary(ComponentDefinition definition, string[] fallbackTerminals)
        {
            var parts = new List<string>();
            if (definition != null && definition.terminals != null)
            {
                for (var i = 0; i < definition.terminals.Count; i++)
                {
                    var terminal = definition.terminals[i];
                    if (terminal != null)
                    {
                        parts.Add(string.IsNullOrWhiteSpace(terminal.label) ? terminal.id : terminal.label);
                    }
                }
            }

            if (parts.Count == 0 && fallbackTerminals != null)
            {
                parts.AddRange(fallbackTerminals);
            }

            if (parts.Count == 0)
                return "端子：暂无端子";

            if (parts.Count <= 6)
                return "端子：" + string.Join("、", parts);

            var firstSix = new List<string>();
            for (var i = 0; i < 6; i++) firstSix.Add(parts[i]);
            
            return "端子：共 " + parts.Count + " 个，" + string.Join("、", firstSix) + " 等";
        }

        private static string BuildCardTerminalSummary(ComponentDefinition definition, string[] fallbackTerminals)
        {
            var parts = new List<string>();
            if (definition != null && definition.terminals != null)
            {
                for (var i = 0; i < definition.terminals.Count; i++)
                {
                    var terminal = definition.terminals[i];
                    if (terminal != null)
                    {
                        parts.Add(string.IsNullOrWhiteSpace(terminal.label) ? terminal.id : terminal.label);
                    }
                }
            }

            if (parts.Count == 0 && fallbackTerminals != null)
            {
                parts.AddRange(fallbackTerminals);
            }

            if (parts.Count == 0)
            {
                return "端子：暂无端子";
            }

            var visibleCount = Mathf.Min(4, parts.Count);
            var visible = parts.GetRange(0, visibleCount);
            return parts.Count > visibleCount
                ? "端子：" + string.Join("、", visible) + " 等"
                : "端子：" + string.Join("、", visible);
        }

        private static string Fallback(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "待补充" : value;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetDisplayName(ComponentDefinition definition)
        {
            if (definition == null) return string.Empty;
            var name = definition.name;
            if (name.Contains("Contactor_KM_220V")) return "交流接触器（220V）";
            if (name.Contains("Contactor_KM_380V")) return "交流接触器（380V）";
            if (name.Contains("Timer_OnDelay_220V")) return "通电延时时间继电器（220V）";
            if (name.Contains("Timer_OnDelay_380V")) return "通电延时时间继电器（380V）";
            if (name.Contains("Timer_OffDelay")) return "断电延时时间继电器（待支持）";
            if (name.Contains("Indicator_Red_220V")) return "红色指示灯（220V）";
            if (name.Contains("Indicator_Green_220V")) return "绿色指示灯（220V）";
            if (name.Contains("Indicator_Yellow_220V")) return "黄色指示灯（220V）";
            if (name.Contains("Indicator_Red_380V")) return "红色指示灯（380V）";
            if (name.Contains("Indicator_Green_380V")) return "绿色指示灯（380V）";
            if (name.Contains("Indicator_Yellow_380V")) return "黄色指示灯（380V）";
            if (name.Contains("TerminalBlock_2H")) return "2位端子横排";
            if (name.Contains("TerminalBlock_2V")) return "2位端子竖排";
            if (name.Contains("TerminalBlock_3H")) return "3位端子横排";
            if (name.Contains("TerminalBlock_3V")) return "3位端子竖排";
            if (name.Contains("TerminalBlock_6H")) return "6位端子横排";
            if (name.Contains("TerminalBlock_6V")) return "6位端子竖排";
            if (name.Contains("AC_220V_Power")) return "220V交流电源";
            if (name.Contains("AC_ThreePhase_Power")) return "三相交流电源";
            if (name.Contains("Button_Start_NO")) return "启动按钮（NO）";
            if (name.Contains("Button_Stop_NC")) return "停止按钮（NC）";
            if (name.Contains("Button_Compound")) return "复合按钮（SB）";
            if (name.Contains("EmergencyStop")) return "急停按钮";
            if (name.Contains("Button_SelfLock")) return "自锁按钮";
            if (name.Contains("Tool_Multimeter")) return "万用表（无图）";
            if (name.Contains("SwitchPower_220V")) return "220V开关电源（待完善）";
            if (name.Contains("PLC_Output_24V")) return "PLC可编程控制器（待完善）";
            if (name.Contains("SolenoidValve_24V")) return "电磁阀（待完善）";
            if (name.Contains("StepperDriver_24V")) return "步进电机驱动器（待完善）";
            if (name.Contains("StepperMotor_24V")) return "步进电动机（待完善）";
            if (name.Contains("Tool_Oscilloscope")) return "示波器（无图）";

            return string.IsNullOrWhiteSpace(definition.displayName)
                ? definition.name
                : definition.displayName.Replace("\n", " ").Replace(" (", "（").Replace(")", "）");
        }

        private Sprite ResolveIcon(ComponentDefinition definition)
        {
            if (definition == null)
            {
                return null;
            }

#if UNITY_EDITOR
            if (definition.name.IndexOf("Contactor_KM", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var kmSprite = LoadSpriteAtPath("Assets/Art/Components/Contactor_KM_380V_Default.png");
                if (kmSprite != null)
                {
                    return kmSprite;
                }
            }

            if (VisualPrefabRegistry.TryGetConfig(definition.name, out var config) && config != null)
            {
                var sprite = LoadSpriteAtPath(config.DefaultSpritePath);
                if (sprite != null)
                {
                    return sprite;
                }

                sprite = LoadSpriteFromVisualPrefab(config.PrefabPath);
                if (sprite != null)
                {
                    return sprite;
                }
            }
#endif

            return definition.sprite;
        }

#if UNITY_EDITOR
        private static Sprite LoadSpriteAtPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Sprite LoadSpriteFromVisualPrefab(string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return null;
            }

            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return null;
            }

            var body = prefab.transform.Find("Body");
            var image = body != null ? body.GetComponent<Image>() : prefab.GetComponentInChildren<Image>(true);
            return image != null ? image.sprite : null;
        }
#endif

        private Sprite GetFallbackSprite()
        {
            if (fallbackSprite != null)
            {
                return fallbackSprite;
            }

            var texture = new Texture2D(96, 96, TextureFormat.RGBA32, false);
            var clear = new Color(0f, 0f, 0f, 0f);
            var fill = new Color(0.82f, 0.88f, 0.96f, 1f);
            var border = new Color(0.54f, 0.62f, 0.74f, 1f);
            for (var y = 0; y < 96; y++)
            {
                for (var x = 0; x < 96; x++)
                {
                    var inside = x > 12 && x < 84 && y > 12 && y < 84;
                    var edge = x > 8 && x < 88 && y > 8 && y < 88;
                    texture.SetPixel(x, y, inside ? fill : edge ? border : clear);
                }
            }

            texture.Apply();
            fallbackSprite = Sprite.Create(texture, new Rect(0f, 0f, 96f, 96f), new Vector2(0.5f, 0.5f), 96f);
            return fallbackSprite;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = rect.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return rect;
        }

        private static Button CreateButton(string name, Transform parent, string text, Color color, Color textColor, int fontSize)
        {
            var rect = CreatePanel(name, parent, color);
            var button = rect.gameObject.AddComponent<Button>();
            var label = CreateText("Text", rect, text, fontSize, FontStyle.Normal, textColor);
            label.alignment = TextAnchor.MiddleCenter;
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
        }

        private static Font cachedTitleFont;
        private static Font cachedBodyFont;

        private static Font ResolveTitleFont()
        {
            if (MainUiTheme.TitleFont != null) return MainUiTheme.TitleFont;
            if (cachedTitleFont != null) return cachedTitleFont;

            cachedTitleFont =
                Resources.Load<Font>("Fonts/maoken_fengyasong") ??
                Resources.Load<Font>("Fonts/MaokenFengyasong") ??
                Resources.Load<Font>("Fonts/maoken") ??
                Resources.Load<Font>("Fonts/Maoken") ??
                Resources.Load<Font>("maoken_fengyasong") ??
                Resources.Load<Font>("MaokenFengyasong") ??
                Resources.Load<Font>("maoken") ??
                Resources.Load<Font>("Maoken");

            if (cachedTitleFont == null)
            {
                Debug.LogWarning("未找到猫啃风字体资源，标题已回退到内置字体。");
                cachedTitleFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            return cachedTitleFont;
        }

        private static Font ResolveBodyFont()
        {
            if (MainUiTheme.UiFont != null) return MainUiTheme.UiFont;
            if (cachedBodyFont != null) return cachedBodyFont;

            cachedBodyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return cachedBodyFont;
        }

        private static Text CreateText(string name, Transform parent, string value, int size, FontStyle style, Color color, bool useTitleFont = false)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.text = value;
            text.font = useTitleFont ? ResolveTitleFont() : style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.DenseUiFont;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;
            return text;
        }

        private static void SetTextLayoutHeight(Text text, float height)
        {
            var layout = text.gameObject.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private static void AddSoftOutline(RectTransform rect)
        {
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("E2E8F0");
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void StretchTo(RectTransform rect, float left, float top, float right, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private sealed class ComponentEncyclopediaEntry
        {
            public ComponentDefinition Definition;
            public string DefinitionName;
            public string DisplayName;
            public string Category;
            public string CircuitType;
            public string RatedVoltage;
            public string RatedCurrent;
            public string[] Terminals;
            public string Purpose;
            public string TerminalDescription;
            public string WorkingState;
            public string WiringUsage;
            public string CommonMistakes;
            public string SimulationRule;
            public string SafetyTips;
            public bool ContentCompleted;
        }

        private static class ComponentEncyclopediaDatabase
        {
            public static List<ComponentEncyclopediaEntry> Build(List<ComponentDefinition> definitions)
            {
                var overrides = BuildOverrides();
                var result = new List<ComponentEncyclopediaEntry>();
                foreach (var definition in definitions)
                {
                    if (definition == null)
                    {
                        continue;
                    }

                    var entry = CreateDefaultEntry(definition);
                    foreach (var pair in overrides)
                    {
                        if (definition.name.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            ApplyOverride(entry, pair.Value);
                            break;
                        }
                    }

                    result.Add(entry);
                }

                result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.Ordinal));
                return result;
            }

            private static ComponentEncyclopediaEntry CreateDefaultEntry(ComponentDefinition definition)
            {
                var category = ResolveCategory(definition);
                return new ComponentEncyclopediaEntry
                {
                    Definition = definition,
                    DefinitionName = definition.name,
                    DisplayName = GetDisplayName(definition),
                    Category = category,
                    CircuitType = ResolveCircuitType(definition.category),
                    RatedVoltage = ResolveRatedVoltage(definition),
                    RatedCurrent = definition.ratedCurrent > 0f ? definition.ratedCurrent.ToString("0.##") + "A" : string.Empty,
                    Terminals = null,
                    Purpose = "该部分内容待补充。",
                    TerminalDescription = "该元件端子以当前 Definition 配置为准，端子列表见基本信息。",
                    WorkingState = "该部分内容待补充。",
                    WiringUsage = "该部分内容待补充。",
                    CommonMistakes = "该部分内容待补充。",
                    SimulationRule = definition.canParticipateInRuntime ? "本系统中该元件可参与仿真运行，具体行为以当前电路连接和元件状态为准。" : "该元件当前主要用于展示或辅助操作。",
                    SafetyTips = "实际接线前应确认元件额定电压、电流和端子功能。",
                    ContentCompleted = false
                };
            }

            private static Dictionary<string, ComponentEncyclopediaEntry> BuildOverrides()
        {
            return new Dictionary<string, ComponentEncyclopediaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Contactor",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "交流接触器",
                        Category = "控制元件",
                        CircuitType = "工业电路",
                        Purpose = "交流接触器用于通过控制回路控制主回路通断，常用于电动机启停、连续运行、正反转和星三角启动等工业控制电路。",
                        TerminalDescription = "A1、A2 是线圈端子；1/L1、3/L2、5/L3 是主触点进线端；2/T1、4/T2、6/T3 是主触点出线端；13NO、14NO 是常开辅助触点；21NC、22NC 是常闭辅助触点。",
                        WorkingState = "线圈未得电时，主触点断开，13-14 断开，21-22 导通。线圈得电后，主触点闭合，13-14 闭合，21-22 断开。",
                        WiringUsage = "连续运行控制中，13-14 常用于自锁；正反转控制中，21-22 常用于互锁；主触点用于控制电动机主回路。",
                        CommonMistakes = "不要把 A1/A2 当作主回路端子；不要把 13-14 和 21-22 混用；正反转电路中不能让正转和反转接触器同时吸合。",
                        SimulationRule = "当 A1/A2 线圈形成有效控制回路时，接触器吸合，主触点和辅助触点会随线圈状态自动切换。",
                        SafetyTips = "实际接线时应区分主回路和控制回路，并确认线圈电压与电源电压一致。",
                        ContentCompleted = true
                    }
                },
                {
                    "ThermalRelay",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "热继电器 (FR)",
                        Category = "保护设备",
                        CircuitType = "工业电路",
                        Purpose = "热继电器主要用于电动机的过载保护和断相保护，防止电动机因长期过载发热而烧毁。",
                        TerminalDescription = "1/L1、3/L2、5/L3 是主回路进线端；2/T1、4/T2、6/T3 是主回路出线端（常连接电动机）；95、96 是常闭辅助触点；97、98 是常开辅助触点。",
                        WorkingState = "正常工作时，主触点和 95-96 保持导通，97-98 断开。当主回路电流长时间过载导致双金属片受热弯曲后，继电器动作，95-96 断开，97-98 闭合。",
                        WiringUsage = "主触点串联在电动机主回路中；95-96 常闭触点串联在控制回路（如接触器线圈回路上），用于过载时切断控制电路；97-98 常用于报警指示灯。",
                        CommonMistakes = "错误地将 97-98 常开触点串联在控制回路中导致无法启动；忘记将热继电器主触点与接触器出线端连接。",
                        SimulationRule = "本系统中，当流经主触点的电流超过设定电流并持续一定时间后，热继电器会触发过载动作，切断 95-96 触点。可点击复位按钮恢复。",
                        SafetyTips = "发生过载跳闸后，应先查明过载原因，排除故障并等热元件冷却后，再进行手动复位。",
                        ContentCompleted = true
                    }
                },
                {
                    "Timer_OnDelay",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "通电延时时间继电器 (KT)",
                        Category = "控制元件",
                        CircuitType = "工业电路",
                        Purpose = "用于在电路中实现时间控制，通电后经过设定的延时时间才改变触点状态，常用于星三角降压启动、顺序控制等。",
                        TerminalDescription = "2、7 是线圈端子；8、5 是延时断开常闭触点；8、6 是延时闭合常开触点；1、3、4 根据具体型号可能带有瞬时触点。",
                        WorkingState = "线圈未得电时，8-5 导通，8-6 断开。线圈得电并开始计时，计时期间状态不变；计时到达设定值后，8-5 断开，8-6 导通。断电后立即恢复初始状态。",
                        WiringUsage = "在星三角启动中，常开触点 8-6 用于触发角接接触器，常闭触点 8-5 用于切断星接接触器。",
                        CommonMistakes = "将线圈端子错接为其他触点；混淆延时触点与瞬时触点；忘记设定延时时间导致动作过快或不动作。",
                        SimulationRule = "当 2、7 线圈通电后启动内部计时器。计时到达设定的延迟时间时，触发触点状态切换。断电则瞬间重置计时与触点。",
                        SafetyTips = "在星三角启动接线中，应确保时间继电器的延时设定合理，避免电动机长时间处于星接状态或过早切换。",
                        ContentCompleted = true
                    }
                },
                {
                    "LimitSwitch",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "行程开关 (SQ)",
                        Category = "传感元件",
                        CircuitType = "工业电路",
                        Purpose = "用于检测机械运动部件的位置，并将机械位移信号转换为电信号，实现行程控制或限位保护。",
                        TerminalDescription = "通常包含一对常开触点（NO）和一对常闭触点（NC）。具体端子如 11-12 为常闭，13-14 为常开。",
                        WorkingState = "未受外力触碰时，常闭导通、常开断开。当机械部件压下开关连杆时，常闭断开、常开导通。外力消失后自动复位。",
                        WiringUsage = "常闭触点串联在控制回路中作为限位停止信号（如行车终点停止）；常开触点可作为到达某位置后的启动信号。",
                        CommonMistakes = "作为限位保护时错接了常开触点，导致到达极限位置时无法自动切断电路。",
                        SimulationRule = "在画布中点击行程开关的触碰区域（OperationHitArea）模拟机械压下，松开鼠标即可模拟复位。",
                        SafetyTips = "在关键的安全限位应用中（如起重机上限位），必须强制使用行程开关的常闭触点串联在主控回路中，以保证线路断开时也能安全停车。",
                        ContentCompleted = true
                    }
                },
                {
                    "EmergencyStop",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "急停按钮",
                        Category = "开关按钮",
                        CircuitType = "工业/家庭电路",
                        Purpose = "在遇到紧急情况时，操作人员可以通过迅速按下该按钮切断设备的控制总电源，实现紧急停车保护设备和人员安全。",
                        TerminalDescription = "通常提供 11、12 或 1、2 等常闭（NC）触点端子。",
                        WorkingState = "正常状态下（未按下），常闭触点保持导通。按下后，触点断开并机械锁定。必须旋转或拉出按钮才能复位重新导通。",
                        WiringUsage = "急停按钮的常闭触点必须串联在控制回路的总进线端，确保按下时能切断所有控制线圈的电源。",
                        CommonMistakes = "将急停按钮错接为常开触点；或者未串联在控制总线上，导致按下后部分设备仍在运行。",
                        SimulationRule = "点击急停按钮即可按下并锁定，再次点击可模拟旋转复位释放。状态变化立即切断或连通常闭触点。",
                        SafetyTips = "急停按钮是关键的安全装置，严禁短接。必须确保其安装在操作人员极易触及的醒目位置。",
                        ContentCompleted = true
                    }
                },
                {
                    "Button_Start_NO",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "启动按钮 (NO)",
                        Category = "开关按钮",
                        CircuitType = "工业电路",
                        Purpose = "用于发送启动控制信号，通常为绿色按钮，按下时接通电路，松手后自动复位断开。",
                        TerminalDescription = "提供 13、14 或 3、4 等常开（NO）触点端子。",
                        WorkingState = "未操作时触点断开（不导通）。按下按钮期间，触点闭合导通。松开手后弹簧使按钮复位，触点再次断开。",
                        WiringUsage = "常用于与接触器的辅助常开触点并联，实现启动信号的输入与自锁逻辑。",
                        CommonMistakes = "接线时未使用接触器自锁，导致松开启动按钮后电动机立刻停止运行。",
                        SimulationRule = "鼠标按下时触点导通，松开鼠标时触点断开。适合用来测试瞬时导通逻辑。",
                        SafetyTips = "操作时应明确按钮控制的设备对象，启动大型设备前需确认周围环境安全。",
                        ContentCompleted = true
                    }
                },
                {
                    "Button_Stop_NC",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "停止按钮 (NC)",
                        Category = "开关按钮",
                        CircuitType = "工业电路",
                        Purpose = "用于发送停止控制信号，通常为红色按钮，按下时切断控制电路，松手后自动复位导通。",
                        TerminalDescription = "提供 11、12 或 1、2 等常闭（NC）触点端子。",
                        WorkingState = "未操作时触点导通。按下按钮期间，触点断开。松开手后弹簧使按钮复位，触点再次导通。",
                        WiringUsage = "常串联在接触器线圈的控制回路中，按下时破坏自锁状态，使接触器断电释放。",
                        CommonMistakes = "误将其与启动按钮并联，导致按下停止按钮时造成控制线路短路或误动作。",
                        SimulationRule = "鼠标按下时触点断开，松开鼠标时触点恢复导通，用于破坏回路自锁状态。",
                        SafetyTips = "停止按钮的可靠性至关重要，接线时确保连接牢固，避免因接触不良导致设备意外停机。",
                        ContentCompleted = true
                    }
                },
                {
                    "Button_Compound",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "复合按钮 (SB)",
                        Category = "开关按钮",
                        CircuitType = "工业电路",
                        Purpose = "内部机械联动的双触点按钮，同时包含常开和常闭触点，按下时两个触点会联动翻转，常用于正反转控制的按钮互锁。",
                        TerminalDescription = "通常包含两组端子：11-12 为常闭触点（NC），13-14 为常开触点（NO）。",
                        WorkingState = "未按下时，11-12 导通，13-14 断开。按下瞬间，11-12 先断开，随后 13-14 闭合；松手复位时，13-14 先断开，11-12 再闭合。",
                        WiringUsage = "在正反转控制中，将常闭触点串联在反方向控制回路中，常开触点串联在正方向启动回路中，实现按下启动的同时切断另一方向的作用。",
                        CommonMistakes = "错误理解联动顺序，导致按下瞬间产生短路；或者只接了一组触点，没有发挥复合按钮的互锁优势。",
                        SimulationRule = "点击操作时，系统会自动处理内部的触点联动顺序，保证常闭先断开、常开后闭合的物理规律。",
                        SafetyTips = "使用复合按钮互锁虽然方便，但在容易产生电弧的高压大电流接触器控制中，仍需辅以接触器辅助触点互锁实现双重保护。",
                        ContentCompleted = true
                    }
                },
                {
                    "Button_SelfLock",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "自锁开关/按钮",
                        Category = "开关按钮",
                        CircuitType = "家庭/工业电路",
                        Purpose = "按下后机械锁定保持导通，再按一次解锁断开，不需要通过接触器就能维持导通状态，常用于低功率设备的直接控制。",
                        TerminalDescription = "常见有 L、L1（单控），或带有 11-12、23-24 等多组同步开闭的端子。",
                        WorkingState = "操作一次，触点状态翻转并保持（如闭合）。再次操作，触点状态再次翻转并保持（如断开）。",
                        WiringUsage = "用于直接控制照明灯泡、小型风扇等设备的电源通断，不需要复杂的控制回路。",
                        CommonMistakes = "在需要频繁启停的大功率电动机控制中错误使用了自锁开关直接控制主回路，导致触点烧毁。",
                        SimulationRule = "点击即可切换并保持锁定状态，无需一直按住鼠标。再次点击释放。",
                        SafetyTips = "自锁开关触点容量有限，严禁超载使用；在断电后若未手动复位，来电时设备会自动启动，存在一定安全隐患。",
                        ContentCompleted = true
                    }
                },
                {
                    "KnifeSwitch",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "刀开关 (QS)",
                        Category = "保护设备",
                        CircuitType = "工业电路",
                        Purpose = "用作电气设备隔离或不频繁接通和分断容量较小的低压电路，确保检修时有明显的断开点。",
                        TerminalDescription = "包含三组或多组对称的进线和出线端子，如 L1、L2、L3 和对应的 T1、T2、T3。",
                        WorkingState = "合闸时，动触刀与静触座咬合，电路接通；分闸时，动触刀拔出，电路切断，形成可见的安全断口。",
                        WiringUsage = "通常作为工业控制柜的总电源隔离关开，安装在电源进线的最前端。",
                        CommonMistakes = "带负荷分断或合闸大功率设备，导致产生强烈电弧烧毁触点甚至伤人。",
                        SimulationRule = "点击刀柄实现合闸与分闸操作，三相触点同步动作。",
                        SafetyTips = "严禁带大负荷拉合刀开关！断电维修时必须先分断刀开关并确认有明显断开点。",
                        ContentCompleted = true
                    }
                },
                {
                    "AC_ThreePhase_Power",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "三相交流电源",
                        Category = "电源仪表",
                        CircuitType = "工业电路",
                        Purpose = "为工业控制系统提供三相 380V 的交流动力电源，是电动机和各种工业负载的能量来源。",
                        TerminalDescription = "L1 (U)、L2 (V)、L3 (W) 为三相火线，N 为零线，PE 为保护接地线。",
                        WorkingState = "提供相位依次相差 120 度的交流电势。L1、L2、L3 任意两根火线间的线电压为 380V，任意一根火线与零线 N 之间的相电压为 220V。",
                        WiringUsage = "三根火线接入主回路控制电动机；控制回路可取 L1 与 L2 获得 380V 控制电源，或取 L1 与 N 获得 220V 控制电源。",
                        CommonMistakes = "将 L1/L2 接入 220V 额定电压的接触器线圈导致线圈烧毁；漏接保护接地线 (PE)。",
                        SimulationRule = "系统始终认为电源接通，为整个拓扑网络提供基准电势，支持跨相短路检测。",
                        SafetyTips = "380V 具有极高危险性，仿真中若发生相间短路（如 L1 直通 L2），系统将立即判定违规并切断仿真。",
                        ContentCompleted = true
                    }
                },
                {
                    "AC_220V_Power",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "220V 交流电源",
                        Category = "电源仪表",
                        CircuitType = "家庭电路",
                        Purpose = "为普通家庭照明、插座及小型单相用电设备提供标准单相 220V 交流电源。",
                        TerminalDescription = "L 为火线 (Live)，N 为零线 (Neutral)。有些带有 L2、N2 扩展端子用于多路并联。",
                        WorkingState = "L 极持续提供与 N 极之间具有 220V 交流电势差的电能。",
                        WiringUsage = "所有开关通常只控制火线 L，零线 N 直接接入用电设备的另一端。",
                        CommonMistakes = "接线时将火线和零线直接短接；或者开关错接在零线上，导致电器断开开关后依然带电。",
                        SimulationRule = "系统会从 L 出发，顺着导线寻找回到 N 的回路。如果 L 直接无阻抗回到 N，将触发短路报错。",
                        SafetyTips = "必须遵循“火线进开关，零线进灯头”的接线规范，保证检修电器时的绝对安全。",
                        ContentCompleted = true
                    }
                },
                {
                    "Motor_ThreePhase",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "三相异步电动机",
                        Category = "用电设备",
                        CircuitType = "工业电路",
                        Purpose = "将三相交流电能转化为机械能的旋转电机，是工业拖动系统的绝对核心动力设备。",
                        TerminalDescription = "U1、V1、W1 和 U2、V2、W2 是定子绕组接线端，PE 为外壳接地端。通过端子的不同短接方式可实现星形 (Y) 或三角形 (△) 运行。",
                        WorkingState = "三相绕组通入三相交流电后产生旋转磁场，带动转子旋转。相序改变（如 L1、L2 对调）会使旋转磁场反向，从而实现电动机反转。",
                        WiringUsage = "主回路通过接触器将 L1、L2、L3 接入电机的三个端子。Y接法将 U2/V2/W2 短接；△接法将 U1-W2, V1-U2, W1-V2 连接。",
                        CommonMistakes = "缺相运行导致电机烧毁；电机外壳未接地；星三角接线中绕组头尾接错导致内部短路。",
                        SimulationRule = "系统会实时检测加在电机端子上的三相电压。只有检测到完整的三相电势差且相序合法时，电机才会旋转，并显示转向动画。",
                        SafetyTips = "必须确保电机外壳牢固连接 PE 地线。实际操作中，大功率电机启动电流极大，需要降压启动。",
                        ContentCompleted = true
                    }
                },
                {
                    "Motor_StarDelta",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "星三角电动机",
                        Category = "用电设备",
                        CircuitType = "工业电路",
                        Purpose = "专用于星三角降压启动电路的电机，通过启动时星形连接、运行后三角连接来大幅降低启动电流。",
                        TerminalDescription = "U1、V1、W1 为绕组首端，U2、V2、W2 为绕组尾端。六个端子全部引出。",
                        WorkingState = "星形阶段通常将 U2、V2、W2 短接；三角阶段按 U1-W2、V1-U2、W1-V2 形成三角连接运行。",
                        WiringUsage = "星三角启动电路中，主接触器、星形接触器和三角接触器配合时间继电器自动完成连接切换。",
                        CommonMistakes = "不要同时形成星形和三角连接。不要接错 U1/V1/W1 与 U2/V2/W2 的对应关系，否则导致内部短路。",
                        SimulationRule = "本系统会识别星形、三角和星三角冲突状态，冲突或缺相时电机不会输出正常运行估算。",
                        SafetyTips = "实际星三角接线必须确认电机铭牌支持三角运行电压（如 380V-△），否则容易烧毁绕组。",
                        ContentCompleted = true
                    }
                },
                {
                    "Indicator_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "指示灯",
                        Category = "传感元件",
                        CircuitType = "工业电路",
                        Purpose = "用于指示设备的工作状态（如运行、停止、故障），方便操作人员远距离监控系统运行情况。",
                        TerminalDescription = "通常提供 X1、X2 两个接线端子。",
                        WorkingState = "当 X1 和 X2 两端存在符合额定要求的电压差时，指示灯亮起；否则熄灭。",
                        WiringUsage = "绿灯常与接触器的常开触点串联指示“运行”；红灯常与常闭触点串联指示“停止”；黄灯常与热继电器 97-98 串联指示“故障过载”。",
                        CommonMistakes = "将 220V 的指示灯错误接入 380V 的相间电压，导致灯泡瞬间烧毁。",
                        SimulationRule = "只要 X1、X2 之间形成有效闭合回路且电压差符合元件额定设定，指示灯就会发光。",
                        SafetyTips = "更换指示灯时应注意电压等级。指示灯仅起提示作用，设备是否真正断电必须以电压测量为准。",
                        ContentCompleted = true
                    }
                },
                {
                    "Breaker_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "空气开关/断路器 (QF)",
                        Category = "保护设备",
                        CircuitType = "家庭/工业电路",
                        Purpose = "作为总电源开关，并在电路发生严重过载或短路时自动跳闸切断电源，保护线路和设备安全。",
                        TerminalDescription = "1P提供一对进出线；3P提供三对进出线（如 1、3、5 进线，2、4、6 出线）。",
                        WorkingState = "手动合闸后内部触点连通。当检测到电流超过瞬时脱扣或过载脱扣阈值时，机械结构自动解扣，瞬间断开所有极。",
                        WiringUsage = "安装在电源引入侧，所有用电和控制回路均从空气开关的出线端取电。",
                        CommonMistakes = "上下级断路器额定电流配置倒置，导致越级跳闸；进出线接反。",
                        SimulationRule = "合闸后导通。若仿真中发生硬短路，系统判定短路的同时可模拟断路器跳闸保护动作。",
                        SafetyTips = "跳闸后绝不能强行立刻合闸，必须彻底排查短路点或烧毁元件后才能恢复送电。",
                        ContentCompleted = true
                    }
                },
                {
                    "Fuse_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "熔断器 (FU)",
                        Category = "保护设备",
                        CircuitType = "工业电路",
                        Purpose = "用于短路保护。当电路中发生严重短路产生巨大电流时，内部熔体自身发热熔断，切断电路。",
                        TerminalDescription = "通常提供对称的进出线端子。单极熔断器有一对端子。",
                        WorkingState = "正常情况下作为导线导通。短路电流经过时，熔体在极短时间内烧毁断裂，物理切断回路。",
                        WiringUsage = "主回路熔断器（FU1）通常选额定电流较大者，控制回路熔断器（FU2）选额定电流较小者，串联在对应回路最前端。",
                        CommonMistakes = "随意用铜丝铁丝代替熔体，导致短路时无法熔断，引发火灾。",
                        SimulationRule = "作为常规阻抗极小的导通元件。若发生严重短路，熔断器可作为切断点之一。",
                        SafetyTips = "更换熔断器熔体时必须先切断前端电源！绝不能带电插拔或更换。",
                        ContentCompleted = true
                    }
                },
                {
                    "Tool_Multimeter",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "万用表",
                        Category = "电源仪表",
                        CircuitType = "维修测量",
                        Purpose = "用于测量电路中的电压、电流、电阻及导通状态等电气参数，是电工最基础的排故工具。",
                        TerminalDescription = "通常包含红表笔端子和黑表笔端子。",
                        WorkingState = "选择不同的测量档位（如交流电压档）后，表笔接触测量点，屏幕显示测量数值。",
                        WiringUsage = "电压测量并联在被测元件两端；电流测量串联在回路中。",
                        CommonMistakes = "用电流档或电阻档去测量带电电压，导致万用表内部烧毁；不明确被测电压大小选错量程。",
                        SimulationRule = "本系统中主要作为教学展示和接线体验，不具备完整的动态测量功能。",
                        SafetyTips = "测量未知高压时，应选择最高量程；操作时手不可触碰表笔金属部分。",
                        ContentCompleted = true
                    }
                },
                {
                    "Single_Phase_Meter",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "单相电能表",
                        Category = "电源仪表",
                        CircuitType = "家庭电路",
                        Purpose = "用于计量单相用电回路所消耗的电能（度数），广泛用于家庭用电计量。",
                        TerminalDescription = "一般有四个接线端子：1进火线、2出火线、3进零线、4出零线。",
                        WorkingState = "正确接入电源和负载后，表盘或液晶屏记录累计耗电量。",
                        WiringUsage = "通常安装在家庭总电源进线处，所有家用电器的耗电均经过此表。",
                        CommonMistakes = "进出线接反；火线零线位置接错导致短路或不走字。",
                        SimulationRule = "本系统中主要用于模拟家庭电路的标准接线实训，暂不支持电量数值累加计算。",
                        SafetyTips = "电能表接线端子带电，安装或检修必须拉开上级电源开关。",
                        ContentCompleted = true
                    }
                },
                {
                    "TerminalBlock_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "端子排",
                        Category = "端子与模块",
                        CircuitType = "通用",
                        Purpose = "用于整理和转接线路，方便多个导线之间的规范连接，使控制柜内接线清晰整齐。",
                        TerminalDescription = "端子排按位导通，每一位上下或左右对应端子互通，不同位之间默认不互通。",
                        WorkingState = "端子排本身不产生电源，也不改变电压，只提供纯物理导线连接和转接功能。",
                        WiringUsage = "常用于把电源、按钮、接触器线圈和外部设备接线整理到统一位置过渡连接。",
                        CommonMistakes = "不要误以为端子排所有端子都互相导通。不同位端子不能随意当作同一节点使用。",
                        SimulationRule = "本系统中，端子排按对应位进行电气连通，例如 1A 与 1B 导通，2A 与 2B 导通，但 1A 与 2A 不默认导通。",
                        SafetyTips = "实际接线中端子排应压接牢固，并保持端子编号清晰以防错接。",
                        ContentCompleted = true
                    }
                },
                {
                    "Lamp_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "灯泡",
                        Category = "用电设备",
                        CircuitType = "家庭电路",
                        Purpose = "用于将电能转换为光能，是家庭照明和简单电路中最直观的负载器件。",
                        TerminalDescription = "通常提供 L（火线进线）和 N（零线出线）两个螺纹或卡口端子。",
                        WorkingState = "接入火线和零线形成完整闭合回路后点亮，发光发热。",
                        WiringUsage = "火线通常应经过单控开关或双控开关后接入灯泡的中心触点，零线直接接入螺纹套管。",
                        CommonMistakes = "将零线接开关而火线直通灯泡，导致开关断开后灯泡仍带有高压电，极度危险。",
                        SimulationRule = "本系统中只要两端存在符合额定要求的电压差，灯泡即刻发光显示高亮状态。",
                        SafetyTips = "更换灯泡时应首先确认开关处于断开状态，切忌用湿手接触灯泡及灯座金属部分。",
                        ContentCompleted = true
                    }
                },
                {
                    "Fan_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "电风扇",
                        Category = "用电设备",
                        CircuitType = "家庭电路",
                        Purpose = "将电能转换为机械风能的单相用电设备，用于模拟家庭电路中的感性负载。",
                        TerminalDescription = "提供 L（火线端）和 N（零线端），有时带 PE 端子。",
                        WorkingState = "接入有效 220V 单相电源回路后内部单相电机运转驱动扇叶旋转。",
                        WiringUsage = "常见接线为火线经墙壁开关控制后接入电风扇 L 端，N 端直接接零线。",
                        CommonMistakes = "接线时短路火线与零线；遗漏 PE 保护接地线的连接。",
                        SimulationRule = "接入标准 220V 电源并形成闭合回路后，系统渲染扇叶旋转动画。",
                        SafetyTips = "电风扇带有旋转部件，接线测试时应保持安全距离；金属外壳风扇必须接地。",
                        ContentCompleted = true
                    }
                },
                {
                    "Single_Control_Switch",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "单开单控开关",
                        Category = "开关按钮",
                        CircuitType = "家庭电路",
                        Purpose = "用于控制一盏灯或一路单相负载的简单开关，按下即保持状态，最为常见。",
                        TerminalDescription = "通常提供 L（进线端）和 L1（出线端）两个端子。",
                        WorkingState = "闭合时 L-L1 导通，负载得电；断开时 L-L1 不导通，负载失电。",
                        WiringUsage = "必须串联在火线 (L) 回路中，不允许接在零线上。",
                        CommonMistakes = "把火线和零线分别接在开关的两个端子上，一按开关直接引起爆燃短路。",
                        SimulationRule = "点击开关面板切换并保持导通/断开状态，状态改变立即重算拓扑回路。",
                        SafetyTips = "火线必须先进开关，保证断开开关后负载端不带电，保障人员检修安全。",
                        ContentCompleted = true
                    }
                },
                {
                    "Two_Way_Switch",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "单开双控开关",
                        Category = "开关按钮",
                        CircuitType = "家庭电路",
                        Purpose = "用于在两个不同位置（如楼上和楼下、床头和门口）独立控制同一盏灯的亮灭。",
                        TerminalDescription = "提供 L（公共端）、L1（第一常开/常闭端）和 L2（第二常开/常闭端）三个端子。",
                        WorkingState = "拨动开关时，公共端 L 会在与 L1 导通和与 L2 导通两种状态之间切换。",
                        WiringUsage = "两个双控开关的 L1 和 L2 分别通过两根控制线直连；电源火线进第一个开关的 L，第二个开关的 L 接负载灯泡。",
                        CommonMistakes = "错把火线接到了 L1 或 L2 端；或者双控线与火线零线混接，导致只能单控甚至短路。",
                        SimulationRule = "点击面板切换 L 与 L1 或 L2 的连接关系，支持完整的异地双控逻辑解算。",
                        SafetyTips = "双控接线线路较多，布线前务必明确公共端和控制线，切忌混淆零火线。",
                        ContentCompleted = true
                    }
                },
                {
                    "Timer_OffDelay",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "断电延时时间继电器", Category = "控制元件", CircuitType = "工业电路",
                        Purpose = "在电路中实现断电延时控制，即线圈失电后，经过设定的延时时间，触点才复位。",
                        TerminalDescription = "通常包含线圈端子（如 2、7）和带有延时闭合/延时断开功能的触点（如 15、16、18）。",
                        WorkingState = "线圈得电时，触点瞬间动作；线圈失电时，触点保持当前状态并开始计时，计时到达设定值后触点复位。",
                        WiringUsage = "常用于需要设备在主电源切断后继续运行一段时间的场合（如冷却风扇延时停止）。",
                        CommonMistakes = "与通电延时时间继电器混淆；误将控制电源切断导致无法计时。",
                        SimulationRule = "当前版本该元件主要用于百科展示，运行仿真能力仍在完善中。",
                        SafetyTips = "实际使用时应注意断电延时继电器的储能机制（如内置电容），切断总电源后触点可能依然会动作。",
                        ContentCompleted = true
                    }
                },
                {
                    "SwitchPower",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "开关电源", Category = "电源仪表", CircuitType = "工业电路",
                        Purpose = "将 220V 交流电转换为低压直流电（如 24V），为 PLC、传感器和继电器等低压控制设备提供工作电源。",
                        TerminalDescription = "包含交流进线端（L、N、PE）和直流出线端（V+、V-）。",
                        WorkingState = "接入有效 220V 交流电源后，直流端持续输出稳定的 24V 直流电势差。",
                        WiringUsage = "通常安装在控制柜内，L/N 端子接市电，V+/V- 端子作为控制回路的 DC24V 电源。",
                        CommonMistakes = "将交直流进出线接反，导致设备瞬间烧毁；过载短接 V+ 与 V-。",
                        SimulationRule = "当前版本该元件主要用于百科展示，运行仿真能力仍在完善中。",
                        SafetyTips = "开关电源外壳通常为金属并带有接线端子排，通电后严禁触摸裸露端子和内部电路板。",
                        ContentCompleted = true
                    }
                },
                {
                    "PLC_",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "PLC可编程控制器", Category = "控制元件", CircuitType = "工业电路",
                        Purpose = "工业自动化系统的核心大脑，通过内部程序逻辑控制输出端，实现复杂的时序和逻辑控制。",
                        TerminalDescription = "通常包含电源端子、输入端子（X）和输出端子（Y），以及公共端（COM）。",
                        WorkingState = "根据输入端口的状态和内部梯形图程序，驱动输出端口导通或闭合。",
                        WiringUsage = "输入端接按钮、传感器；输出端接继电器、接触器线圈或电磁阀。",
                        CommonMistakes = "输入输出的 NPN/PNP 类型接错；公共端 COM 接错电压导致烧毁端口。",
                        SimulationRule = "当前版本该元件主要用于百科展示，梯形图编程与运行仿真能力仍在完善中。",
                        SafetyTips = "PLC 属于精密电子设备，接线时必须严格区分交直流电源和信号线，避免高压串入信号端。",
                        ContentCompleted = true
                    }
                },
                {
                    "SolenoidValve",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "电磁阀", Category = "用电设备", CircuitType = "工业电路",
                        Purpose = "利用电磁铁的吸力控制流体（如压缩空气或液压油）的流动方向，是气动/液压系统的核心执行元件。",
                        TerminalDescription = "通常包含电磁线圈的两个接线端（如正负极或 L/N 端）。",
                        WorkingState = "线圈得电时产生磁场吸动阀芯，改变气路或液路状态；失电时依靠弹簧复位。",
                        WiringUsage = "常由 PLC 输出端口或中间继电器控制线圈得失电，进而控制气缸伸缩。",
                        CommonMistakes = "线圈电压等级接错（如 24V 误接 220V）；安装时流体进出口方向装反。",
                        SimulationRule = "当前版本该元件主要用于百科展示，运行仿真能力仍在完善中。",
                        SafetyTips = "气动系统带有高压能量，维修电磁阀前必须先切断气源并排空残余压力。",
                        ContentCompleted = true
                    }
                },
                {
                    "StepperDriver",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "步进电机驱动器", Category = "控制元件", CircuitType = "工业电路",
                        Purpose = "接收来自 PLC 或控制器的脉冲和方向信号，将其转换为控制步进电机绕组的电流，驱动电机按指定步距角转动。",
                        TerminalDescription = "包含电源输入（V+、V-）、控制信号输入（PUL、DIR、ENA）以及电机绕组输出（A+、A-、B+、B-）。",
                        WorkingState = "得电且使能后，每接收到一个脉冲信号，输出对应的电流时序，驱动电机转动一步。",
                        WiringUsage = "控制信号端接 PLC 输出口；电机绕组输出端对应接步进电机的两相线圈。",
                        CommonMistakes = "脉冲和方向信号接反；绕组 A 相与 B 相线序接错导致电机震动不转。",
                        SimulationRule = "当前版本该元件主要用于百科展示，脉冲时序仿真能力仍在完善中。",
                        SafetyTips = "驱动器工作时会产生较高热量，需保证良好散热；电机运行期间严禁插拔电机接线。",
                        ContentCompleted = true
                    }
                },
                {
                    "StepperMotor",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "步进电动机", Category = "用电设备", CircuitType = "工业电路",
                        Purpose = "将电脉冲信号转换为角位移或线位移，用于需要精确控制位置和速度的自动化机构。",
                        TerminalDescription = "通常为四线或六线引出，代表内部的两组定子绕组（如 A 相和 B 相）。",
                        WorkingState = "在驱动器给定的时序电流下，转子按固定的步距角（如 1.8 度）逐点转动。",
                        WiringUsage = "与专用的步进电机驱动器配合使用，四根引出线分别接驱动器的 A+、A-、B+、B-。",
                        CommonMistakes = "未使用驱动器而直接接入直流电源；将不同相的导线短接。",
                        SimulationRule = "当前版本该元件主要用于百科展示，运行仿真能力仍在完善中。",
                        SafetyTips = "步进电机在高速运转时可能产生较大反电动势，断电后电机轴可能仍有惯性旋转，请注意安全。",
                        ContentCompleted = true
                    }
                },
                {
                    "Tool_Oscilloscope",
                    new ComponentEncyclopediaEntry
                    {
                        DisplayName = "示波器", Category = "电源仪表", CircuitType = "维修测量",
                        Purpose = "用于观察和测量电路中电压信号随时间变化的波形，是分析高频信号和瞬态过程的高级测试仪器。",
                        TerminalDescription = "包含多个测量通道探头接口（CH1、CH2）及接地夹。",
                        WorkingState = "探头接入被测电路后，屏幕实时显示电压幅度与时间的二维函数波形。",
                        WiringUsage = "探头针接触被测信号点，接地夹连接电路的地线参考点。",
                        CommonMistakes = "测量高压市电时未使用隔离探头导致示波器炸机；接地夹接错位置造成短路。",
                        SimulationRule = "当前版本该元件主要用于百科展示，完整信号波形捕获能力仍在完善中。",
                        SafetyTips = "示波器探头接地夹通常与外壳及大地相连，测量非隔离电源时极易引发对地短路危险。",
                        ContentCompleted = true
                    }
                }
            };
        }

            private static void ApplyOverride(ComponentEncyclopediaEntry target, ComponentEncyclopediaEntry source)
            {
                // Do NOT overwrite DisplayName, let GetDisplayName handle it
                target.Category = source.Category;
                target.CircuitType = source.CircuitType;
                target.RatedVoltage = source.RatedVoltage;
                target.RatedCurrent = source.RatedCurrent;
                target.Terminals = source.Terminals;
                target.Purpose = source.Purpose;
                target.TerminalDescription = source.TerminalDescription;
                target.WorkingState = source.WorkingState;
                target.WiringUsage = source.WiringUsage;
                target.CommonMistakes = source.CommonMistakes;
                target.SimulationRule = source.SimulationRule;
                target.SafetyTips = source.SafetyTips;
                target.ContentCompleted = source.ContentCompleted;
            }

            private static string ResolveCategory(ComponentDefinition definition)
            {
                var name = definition.name ?? string.Empty;
                if (name.IndexOf("EmergencyStop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    definition.kind == ComponentKind.Fuse ||
                    definition.kind == ComponentKind.Breaker)
                {
                    return "保护设备";
                }

                if (name.IndexOf("LimitSwitch", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "传感元件";
                }

                switch (definition.kind)
                {
                    case ComponentKind.Switch:
                    case ComponentKind.TwoWaySwitch:
                    case ComponentKind.PushButton:
                        return "开关按钮";
                    case ComponentKind.PowerSource:
                    case ComponentKind.EnergyMeter:
                    case ComponentKind.Instrument:
                        return "电源仪表";
                    case ComponentKind.Lamp:
                    case ComponentKind.Fan:
                    case ComponentKind.Motor:
                    case ComponentKind.Indicator:
                        return "用电设备";
                    case ComponentKind.ContactorCoil:
                        return "控制元件";
                    case ComponentKind.TerminalBlock:
                        return "端子与模块";
                    default:
                        return "其他";
                }
            }

            private static string ResolveCircuitType(ComponentCategory category)
            {
                switch (category)
                {
                    case ComponentCategory.Household:
                        return "家庭电路";
                    case ComponentCategory.Industrial:
                        return "工业电路";
                    case ComponentCategory.Measurement:
                        return "通用 / 测量";
                    default:
                        return "通用";
                }
            }

            private static string ResolveRatedVoltage(ComponentDefinition definition)
            {
                if (definition.sourcePhaseCount >= 3 && definition.sourceLineVoltage > 0f)
                {
                    return definition.sourceLineVoltage.ToString("0") + "V";
                }

                if (definition.sourceVoltage > 0f)
                {
                    return definition.sourceVoltage.ToString("0") + "V";
                }

                if (definition.ratedVoltage > 0f)
                {
                    return definition.ratedVoltage.ToString("0") + "V";
                }

                return string.Empty;
            }
        }
    }
}
