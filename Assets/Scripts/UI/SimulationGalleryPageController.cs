using System;
using System.Collections.Generic;
using System.Linq;
using ElectricalSim.Templates;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 仿真广场的运行时页面控制器：从模板 Catalog 读取条目，动态创建列表卡片、筛选/搜索/排序控件和详情视图，
    /// 并在用户选择时调用既有模板加载入口。它不解析模板 JSON、不生成电路对象，也不维护图纸集或练习会话状态。
    /// 卡片与详情图片均由 Catalog 的 thumbnailPath 经 Resources.Load 获取，Editor 与 Windows Player 使用相同资源路径。
    /// 页面在 Awake 建立容器、OnEnable 刷新内容；动态节点由本页根节点统一清理，后续改为预制体时需复核卡片监听器生命周期。
    /// </summary>
    public sealed class SimulationGalleryPageController : MonoBehaviour
    {
        // 仿真广场展示并启动本地教学示例：它管理 catalog 条目的卡片、筛选、预览和启动请求，但不解析模板 JSON、
        // 不生成电路对象，也不拥有图纸集或练习会话。缩略图、描述和标签都是 presentation，不等于运行时电路事实。
        // Gallery 与 Blueprint 的职责应保持区分：前者面向示例探索/启动，后者面向图纸浏览、配置与练习入口。
        private const string CatalogPath = "Blueprints/Templates/template_catalog";
        private const float GalleryMargin = 44f;
        private const float DetailMargin = 37f;
        private const float GalleryCardWidth = 436f;
        private const float GalleryCardHeight = 318f;
        private const float GalleryCardGap = 30f;
        private static Color PageBackground => UiThemeTokens.Background;
        private static Color CardBackground => UiThemeTokens.CardBackground;
        private static Color TextPrimary => UiThemeTokens.TextDark;
        private static Color TextSecondary => UiThemeTokens.TextMuted;
        private static Color BorderColor => UiThemeTokens.BorderColor;
        private static Color Blue => UiThemeTokens.PrimaryBlue;
        private static Color PaleBlue => UiThemeTokens.PrimaryLight;

        private readonly List<GalleryEntry> entries = new List<GalleryEntry>();
        private readonly Dictionary<string, Button> filterButtons = new Dictionary<string, Button>();

        private GameObject listRoot;
        private GameObject detailRoot;
        private RectTransform gridContent;
        private ScrollRect gridScrollRect;
        private InputField searchInput;
        private Dropdown sortDropdown;
        private Text emptyHint;
        private Text statusText;
        private string activeFilter = "全部";

        private void Awake()
        {
            // 首次建立页面骨架后再读取 catalog 和创建卡片。动态节点均由本页拥有，不能依赖场景中遗留的旧卡片来恢复
            // 选择或筛选状态，否则 catalog 更新后 listener 与条目索引会失配。
            // 首次创建页面骨架；Catalog 条目在后续 LoadEntries 中读取，避免把模板数据写入场景对象。
            EnsureRootRect();
            BuildPage();
            LoadEntries();
            RefreshCards();
        }

        private void OnEnable()
        {
            // 重新显示页面默认回到列表视图，但不触碰当前 Workspace 或已加载模板。页面可见性不是一次新的示例启动请求。
            // 页面重新显示时刷新筛选后的卡片，不改变当前 Workspace 或已加载模板。
            if (listRoot != null && detailRoot != null)
            {
                listRoot.SetActive(true);
                detailRoot.SetActive(false);
            }
        }

        private void EnsureRootRect()
        {
            var rect = GetComponent<RectTransform>();
            if (rect == null)
            {
                rect = gameObject.AddComponent<RectTransform>();
            }

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = GetComponent<Image>();
            if (image == null)
            {
                image = gameObject.AddComponent<Image>();
            }

            image.color = MainUiTheme.Hex("F8FBFF");
            image.raycastTarget = true;
        }

        private void BuildPage()
        {
            // 重建只清理本页根节点下的表现树，并按固定层级创建 filter、search、grid 和 detail 容器。不要把真实模板
            // Spawn、Workspace 清理或 Practice 状态变更混入 UI rebuild。
            ClearChildren(transform);

            listRoot = CreateObject("GalleryListRoot", transform, typeof(RectTransform));
            Stretch(listRoot.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);

            var header = CreateObject("Header", listRoot.transform, typeof(RectTransform));
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(GalleryMargin, -60f);
            headerRect.offsetMax = new Vector2(-GalleryMargin, 0f);

            var title = CreateText("Title", header.transform, "仿真广场", 24, FontStyle.Normal, MainUiTheme.Hex("111827"));
            MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.PageTitle);
            Stretch(title.rectTransform, 0f, 0f, 0f, 0f);

            var description = CreateText(
                "Description",
                header.transform,
                "精选本地教学案例，点击案例可查看说明并加载到仿真画布。全部案例来自本地模板，断网可用。",
                16,
                FontStyle.Normal,
                MainUiTheme.Hex("B1B7BE"));
            description.alignment = TextAnchor.UpperLeft;
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.gameObject.SetActive(false);

            BuildFilterBar(listRoot.transform);
            BuildSearchAndSort(listRoot.transform);
            BuildGrid(listRoot.transform);
            BuildDetailRoot();
        }

        private void BuildFilterBar(Transform parent)
        {
            // 筛选栏只维护 activeFilter 和按钮视觉。分类选择影响展示集合，不会改变 catalog 的权威条目、模板资源路径或难度数据。
            var bar = CreateObject("FilterBar", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = bar.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(GalleryMargin, -112f);
            rect.offsetMax = new Vector2(-470f, -72f);

            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;

            filterButtons.Clear();
            var filters = new[] { "全部", "推荐", "家庭电路", "工业电路", "电机控制", "正反转", "星三角", "自动往返" };
            foreach (var filter in filters)
            {
                var button = CreatePillButton(bar.transform, filter);
                var captured = filter;
                button.onClick.AddListener(() =>
                {
                    activeFilter = captured;
                    RefreshCards();
                });
                filterButtons[filter] = button;
            }
        }

        private void BuildSearchAndSort(Transform parent)
        {
            // 搜索和排序是本地展示查询；文本输入和下拉顺序不能写回 catalog，更不能作为模板 identity 或加载参数。
            var group = CreateObject("SearchAndSort", parent, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rect = group.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-GalleryMargin, -72f);
            rect.sizeDelta = new Vector2(440f, 40f);

            var layout = group.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleRight;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            searchInput = CreateInput(group.transform, "搜索案例、知识点或元件", 264f, 36f);
            searchInput.onValueChanged.AddListener(_ => RefreshCards());

            sortDropdown = CreateDropdown(group.transform, 166f, 36f);
            sortDropdown.options.Clear();
            sortDropdown.options.Add(new Dropdown.OptionData("默认排序"));
            sortDropdown.options.Add(new Dropdown.OptionData("难度从低到高"));
            sortDropdown.options.Add(new Dropdown.OptionData("难度从高到低"));
            sortDropdown.options.Add(new Dropdown.OptionData("名称排序"));
            sortDropdown.options.Add(new Dropdown.OptionData("推荐优先"));
            sortDropdown.value = 0;
            sortDropdown.RefreshShownValue();
            sortDropdown.onValueChanged.AddListener(_ => RefreshCards());
        }

        private void BuildGrid(Transform parent)
        {
            // Grid 的 ScrollRect/Content 仅解决大量卡片的可达性。卡片位置、列数和响应式尺寸不能影响示例电路内元件坐标。
            var scroll = CreateObject("CaseGridScrollView", parent, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var scrollRect = scroll.GetComponent<RectTransform>();
            Stretch(scrollRect, GalleryMargin, GalleryMargin, 150f, 32f);
            scroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            gridScrollRect = scroll.GetComponent<ScrollRect>();
            gridScrollRect.horizontal = false;
            gridScrollRect.vertical = true;
            gridScrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateObject("Viewport", scroll.transform, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, 0f, 0f, 0f, 0f);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            viewport.GetComponent<Image>().raycastTarget = true;

            var content = CreateObject("CardGridContent", viewport.transform, typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            gridContent = content.GetComponent<RectTransform>();
            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(0.5f, 1f);
            gridContent.anchoredPosition = Vector2.zero;
            gridContent.offsetMin = new Vector2(0f, gridContent.offsetMin.y);
            gridContent.offsetMax = new Vector2(0f, 0f);

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(GalleryCardWidth, GalleryCardHeight);
            grid.spacing = new Vector2(GalleryCardGap, GalleryCardGap);
            grid.padding = new RectOffset(0, 0, 0, 24);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.Flexible;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            gridScrollRect.viewport = viewportRect;
            gridScrollRect.content = gridContent;

            emptyHint = CreateText("EmptyHint", scroll.transform, "未找到匹配案例\n\n请尝试更换关键词，或切换到“全部”分类查看本地案例。", 14, FontStyle.Normal, MainUiTheme.Hex("64748B"));
            MainUiTheme.ApplyTextRole(emptyHint, MainUiTheme.UiTextRole.SearchInputText);
            emptyHint.alignment = TextAnchor.MiddleCenter;
            Stretch(emptyHint.rectTransform, 0f, 0f, 0f, 0f);
            emptyHint.gameObject.SetActive(false);

            statusText = CreateText("StatusText", parent, string.Empty, 15, FontStyle.Normal, MainUiTheme.Hex("64748B"));
            MainUiTheme.ApplyTextRole(statusText, MainUiTheme.UiTextRole.StatusText);
            statusText.alignment = TextAnchor.MiddleLeft;
            var statusRect = statusText.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.offsetMin = new Vector2(GalleryMargin, -140f);
            statusRect.offsetMax = new Vector2(-GalleryMargin, -118f);

            ApplyResponsiveGridLayout();
        }

        private void BuildDetailRoot()
        {
            // 详情根与列表根独立，选择条目时只切换展示树。详情图片/文本可以 fallback，但不应修改条目来源或当前画布。
            detailRoot = CreateObject("GalleryDetailRoot", transform, typeof(RectTransform));
            Stretch(detailRoot.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            detailRoot.SetActive(false);
        }

        private void LoadEntries()
        {
            // entries 从正式 template catalog 投影而来。读取失败时保留可解释的空状态，不从缩略图文件名或已有 UI 反推模板定义。
            // 数据来源限定为 CircuitTemplateCatalogLoader；不要通过扫描 Resources 目录推断卡片集合。
            entries.Clear();
            if (!CircuitTemplateCatalogLoader.TryLoad(CatalogPath, out var catalog, out var error))
            {
                SetStatus(string.IsNullOrWhiteSpace(error) ? "本地案例目录读取失败。" : error);
                return;
            }

            foreach (var item in catalog.templates.OrderBy(i => i.sortOrder))
            {
                if (item == null || string.IsNullOrWhiteSpace(item.resourcePath))
                {
                    continue;
                }

                entries.Add(CreateEntry(item));
            }

            SetStatus("共 " + entries.Count + " 个本地案例");
        }

        private GalleryEntry CreateEntry(CircuitTemplateCatalogItemDto item)
        {
            var title = string.IsNullOrWhiteSpace(item.templateName) ? item.templateId : item.templateName;
            var tags = BuildTags(item, title);
            return new GalleryEntry
            {
                CatalogItem = item,
                Id = item.templateId,
                Title = title,
                Category = string.IsNullOrWhiteSpace(item.category) ? "本地案例" : item.category,
                Difficulty = string.IsNullOrWhiteSpace(item.difficulty) ? "初级" : item.difficulty,
                Description = BuildDescription(item, title, tags),
                LearningGoal = BuildLearningGoal(title, tags),
                MainComponents = BuildMainComponents(title, tags),
                KeyPoints = BuildKeyPoints(title, tags),
                CommonMistakes = BuildCommonMistakes(title, tags),
                SourceLabel = "系统内置案例",
                TemplateId = item.templateId,
                ThumbnailPath = item.thumbnailPath,
                Tags = tags,
                Recommended = IsRecommended(title, tags)
            };
        }

        private void RefreshCards()
        {
            // 每次刷新先按 filter/search/sort 形成临时展示列表，再重建 Grid 卡片。旧 listener 随卡片销毁，避免点击过期条目
            // 后加载了错误模板；筛选本身不删除 entries。
            // 仅销毁并重建本页动态卡片，详情根节点和模板加载流程不在此处重置。
            if (gridContent == null)
            {
                return;
            }

            ClearChildren(gridContent);
            ApplyResponsiveGridLayout();
            RefreshFilterButtons();

            var filtered = entries
                .Where(MatchFilter)
                .Where(MatchSearch)
                .ToList();
            ApplySort(filtered);

            foreach (var entry in filtered)
            {
                CreateCard(entry);
            }

            emptyHint.gameObject.SetActive(filtered.Count == 0);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            if (gridScrollRect != null)
            {
                gridScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void RefreshFilterButtons()
        {
            foreach (var pair in filterButtons)
            {
                var active = pair.Key == activeFilter;
                var image = pair.Value.GetComponent<Image>();
                if (image != null)
                {
                    image.color = active ? MainUiTheme.PrimaryBlue : Color.white;
                    var outline = pair.Value.GetComponent<Outline>();
                    if (outline != null)
                    {
                        outline.effectColor = active ? MainUiTheme.PrimaryBlue : MainUiTheme.Hex("D2D2D2");
                    }
                }

                var label = pair.Value.transform.Find("Text")?.GetComponent<Text>();
                if (label != null)
                {
                    label.color = active ? Color.white : MainUiTheme.Hex("464646");
                    MainUiTheme.ApplyTextRole(label, active
                        ? MainUiTheme.UiTextRole.ContentFilterTextSelected
                        : MainUiTheme.UiTextRole.ContentFilterText);
                }

                var icon = pair.Value.transform.Find("Icon")?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.color = active ? Color.white : MainUiTheme.Hex("6B7280");
                }
            }
        }

        private void ApplyResponsiveGridLayout()
        {
            // 响应式布局仅根据当前 Viewport 计算列数与 Content 高度。几何变化是 UI 事实，不能写回 GalleryEntry 或模板数据。
            if (gridContent == null || gridScrollRect == null || gridScrollRect.viewport == null)
            {
                return;
            }

            var viewportWidth = gridScrollRect.viewport.rect.width;
            if (viewportWidth <= 0f)
            {
                var rootRect = GetComponent<RectTransform>();
                viewportWidth = rootRect != null ? Mathf.Max(0f, rootRect.rect.width - GalleryMargin * 2f) : 1400f;
            }

            var gap = GalleryCardGap;
            int columnCount;

            if (viewportWidth >= 1320f)
            {
                columnCount = 4;
            }
            else if (viewportWidth >= 960f)
            {
                columnCount = 3;
            }
            else if (viewportWidth >= 640f)
            {
                columnCount = 2;
            }
            else
            {
                columnCount = 1;
            }

            var cellWidth = (viewportWidth - gap * (columnCount - 1)) / columnCount;
            cellWidth = Mathf.Clamp(cellWidth, 300f, GalleryCardWidth);

            var grid = gridContent.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columnCount;
                grid.cellSize = new Vector2(cellWidth, GalleryCardHeight);
                grid.spacing = new Vector2(gap, gap);
                grid.childAlignment = TextAnchor.UpperLeft;
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.padding = new RectOffset(0, 0, 0, 24);
            }

            gridContent.anchorMin = new Vector2(0f, 1f);
            gridContent.anchorMax = new Vector2(1f, 1f);
            gridContent.pivot = new Vector2(0f, 1f);
            gridContent.anchoredPosition = Vector2.zero;
            gridContent.offsetMin = new Vector2(0f, gridContent.offsetMin.y);
            gridContent.offsetMax = new Vector2(0f, 0f);
        }

        private void OnRectTransformDimensionsChange()
        {
            // 尺寸变化后只刷新卡片布局；不要在此回调重新加载 catalog 或改变 activeFilter，避免 Canvas layout 循环造成状态抖动。
            if (gridContent != null)
            {
                ApplyResponsiveGridLayout();
                LayoutRebuilder.ForceRebuildLayoutImmediate(gridContent);
            }
        }

        private bool MatchFilter(GalleryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(activeFilter) || activeFilter == "全部")
            {
                return true;
            }

            if (activeFilter == "推荐")
            {
                return entry.Recommended;
            }

            if (entry.Category == activeFilter)
            {
                return true;
            }

            return entry.Tags.Any(tag => tag.IndexOf(activeFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                || entry.Title.IndexOf(activeFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool MatchSearch(GalleryEntry entry)
        {
            var keyword = searchInput != null ? searchInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return true;
            }

            keyword = keyword.Trim();
            return Contains(entry.Title, keyword)
                || Contains(entry.Category, keyword)
                || Contains(entry.Difficulty, keyword)
                || Contains(entry.Description, keyword)
                || Contains(entry.MainComponents, keyword)
                || Contains(entry.KeyPoints, keyword)
                || entry.Tags.Any(tag => Contains(tag, keyword));
        }

        private void ApplySort(List<GalleryEntry> list)
        {
            if (sortDropdown == null || list == null)
            {
                return;
            }

            switch (sortDropdown.value)
            {
                case 1:
                    list.Sort((a, b) => DifficultyRank(a.Difficulty).CompareTo(DifficultyRank(b.Difficulty)));
                    break;
                case 2:
                    list.Sort((a, b) => DifficultyRank(b.Difficulty).CompareTo(DifficultyRank(a.Difficulty)));
                    break;
                case 3:
                    list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCulture));
                    break;
                case 4:
                    list.Sort((a, b) => b.Recommended.CompareTo(a.Recommended));
                    break;
                default:
                    list.Sort((a, b) => a.CatalogItem.sortOrder.CompareTo(b.CatalogItem.sortOrder));
                    break;
            }
        }

        private void CreateCard(GalleryEntry entry)
        {
            // Card 持有条目的展示数据和明确的选择回调。其 thumbnail、tag 与难度 pill 只帮助用户浏览，不是 rule/validation 输入。
            var card = CreateObject("CaseCard_" + entry.Id, gridContent, typeof(RectTransform), typeof(Image), typeof(Button), typeof(UnityEngine.UI.Shadow));
            var image = card.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(15);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;
            var outline = card.GetComponent<Outline>() ?? card.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("D2D2D2");
            outline.effectDistance = new Vector2(1f, -1f);
            
            var shadow = card.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.04f);
            shadow.effectDistance = new Vector2(0, -4);

            var button = card.GetComponent<Button>();
            button.onClick.AddListener(() => ShowDetail(entry));

            var thumbPanel = CreateObject("Thumbnail", card.transform, typeof(RectTransform), typeof(Image));
            var thumbRect = thumbPanel.GetComponent<RectTransform>();
            thumbRect.anchorMin = new Vector2(0f, 1f);
            thumbRect.anchorMax = new Vector2(1f, 1f);
            thumbRect.pivot = new Vector2(0.5f, 1f);
            thumbRect.offsetMin = new Vector2(16f, -164f);
            thumbRect.offsetMax = new Vector2(-16f, -14f);
            var thumbImage = thumbPanel.GetComponent<Image>();
            thumbImage.sprite = UiThemeTokens.GetRoundedSprite(12);
            thumbImage.type = Image.Type.Sliced;
            thumbImage.color = MainUiTheme.Hex("F1F5F9");

            var thumbnailSprite = LoadThumbnail(entry.ThumbnailPath);
            if (thumbnailSprite != null)
            {
                var thumbnail = CreateObject("Image", thumbPanel.transform, typeof(RectTransform), typeof(Image));
                Stretch(thumbnail.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
                var thumbnailImage = thumbnail.GetComponent<Image>();
                thumbnailImage.sprite = thumbnailSprite;
                thumbnailImage.preserveAspect = true;
                thumbnailImage.color = Color.white;
                thumbnailImage.raycastTarget = false;
            }
            else
            {
                var colorBar = CreateObject("ColorBar", thumbPanel.transform, typeof(RectTransform), typeof(Image));
                var colorBarRect = colorBar.GetComponent<RectTransform>();
                colorBarRect.anchorMin = new Vector2(0f, 1f);
                colorBarRect.anchorMax = new Vector2(1f, 1f);
                colorBarRect.pivot = new Vector2(0.5f, 1f);
                colorBarRect.offsetMin = new Vector2(0f, -4f);
                colorBarRect.offsetMax = new Vector2(0f, 0f);
                colorBar.GetComponent<Image>().color = entry.Category == "家庭电路" ? HexColor(0x60A5FA) : (entry.Category == "工业电路" ? HexColor(0xFBBF24) : HexColor(0x94A3B8));

                var placeholder = CreateText("Placeholder", thumbPanel.transform, "案例缩略图\n待补充", 16, FontStyle.Normal, MainUiTheme.Hex("B1B7BE"));
                placeholder.alignment = TextAnchor.MiddleCenter;
                Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);
            }

            var title = CreateText("Title", card.transform, entry.Title, 20, FontStyle.Normal, MainUiTheme.Hex("1F2937"));
            MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.ContentCardTitle);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(16f, -228f);
            title.rectTransform.offsetMax = new Vector2(-16f, -180f);

            var metaRow = CreateObject("MetaRow", card.transform, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            metaRow.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
            metaRow.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            metaRow.GetComponent<RectTransform>().offsetMin = new Vector2(16f, -254f);
            metaRow.GetComponent<RectTransform>().offsetMax = new Vector2(-16f, -230f);
            var metaLayout = metaRow.GetComponent<HorizontalLayoutGroup>();
            metaLayout.spacing = 8f;
            metaLayout.childAlignment = TextAnchor.MiddleLeft;
            metaLayout.childControlWidth = true;
            metaLayout.childControlHeight = true;
            metaLayout.childForceExpandWidth = false;
            metaLayout.childForceExpandHeight = false;

            CreateMetadataPill(metaRow.transform, "Category", entry.Category, MainUiTheme.Hex("DBEAFE"), MainUiTheme.PrimaryBlue, 78f, MainUiTheme.UiTextRole.MetaText);
            CreateMetadataPill(metaRow.transform, "Difficulty", entry.Difficulty, GetDifficultyPillColor(entry.Difficulty), Color.white, 64f, MainUiTheme.UiTextRole.MetaTextBold);
            CreateMetadataSource(metaRow.transform, entry.SourceLabel);

            var detailButton = CreateButton(card.transform, "查看详情", MainUiTheme.Hex("F1F5F9"), MainUiTheme.Hex("464646"), MainUiTheme.UiTextRole.GalleryCardActionButton);
            var detailRect = detailButton.GetComponent<RectTransform>();
            detailRect.anchorMin = new Vector2(0f, 0f);
            detailRect.anchorMax = new Vector2(0f, 0f);
            detailRect.pivot = new Vector2(0f, 0f);
            detailRect.anchoredPosition = new Vector2(16f, 12f);
            detailRect.sizeDelta = new Vector2(129f, 34f);
            detailButton.onClick.AddListener(() => ShowDetail(entry));

            var loadButton = CreateButton(card.transform, "加载案例", MainUiTheme.PrimaryBlue, Color.white, MainUiTheme.UiTextRole.GalleryCardActionButton);
            var loadRect = loadButton.GetComponent<RectTransform>();
            loadRect.anchorMin = new Vector2(1f, 0f);
            loadRect.anchorMax = new Vector2(1f, 0f);
            loadRect.pivot = new Vector2(1f, 0f);
            loadRect.anchoredPosition = new Vector2(-16f, 12f);
            loadRect.sizeDelta = new Vector2(129f, 34f);
            loadButton.onClick.AddListener(() => LoadEntry(entry));
        }

        private void ShowDetail(GalleryEntry entry)
        {
            // 展示详情不等于启动示例。用户仍需通过 LoadEntry 发出正式加载请求，保证预览不会无意清空或替换 Workspace。
            // 详情页是展示层；进入模板的实际动作继续交由 LoadEntry 的既有控制器调用。
            listRoot.SetActive(false);
            detailRoot.SetActive(true);
            ClearChildren(detailRoot.transform);

            var topBar = CreateObject("DetailTopBar", detailRoot.transform, typeof(RectTransform));
            var topRect = topBar.GetComponent<RectTransform>();
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = new Vector2(1f, 1f);
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.offsetMin = new Vector2(DetailMargin, -60f);
            topRect.offsetMax = new Vector2(-DetailMargin, 0f);

            var backButton = CreateButton(topBar.transform, "返回广场", MainUiTheme.Hex("F1F5F9"), MainUiTheme.Hex("464646"));
            MainUiTheme.ApplyTextRole(backButton.GetComponentInChildren<Text>(), MainUiTheme.UiTextRole.CardActionButton);
            var backRect = backButton.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(0f, 0.5f);
            backRect.pivot = new Vector2(0f, 0.5f);
            backRect.anchoredPosition = Vector2.zero;
            backRect.sizeDelta = new Vector2(128f, 36f);
            backButton.onClick.AddListener(() =>
            {
                detailRoot.SetActive(false);
                listRoot.SetActive(true);
            });

            var title = CreateText("Title", topBar.transform, entry.Title, 22, FontStyle.Normal, MainUiTheme.Hex("727272"));
            MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.DetailTitle);
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.offsetMin = new Vector2(140f, 0f);
            title.rectTransform.offsetMax = new Vector2(-180f, 0f);

            var loadButton = CreateButton(topBar.transform, "加载到画布", MainUiTheme.PrimaryBlue, Color.white);
            MainUiTheme.ApplyTextRole(loadButton.GetComponentInChildren<Text>(), MainUiTheme.UiTextRole.CardActionButton);
            var loadRect = loadButton.GetComponent<RectTransform>();
            loadRect.anchorMin = new Vector2(1f, 0.5f);
            loadRect.anchorMax = new Vector2(1f, 0.5f);
            loadRect.pivot = new Vector2(1f, 0.5f);
            loadRect.anchoredPosition = Vector2.zero;
            loadRect.sizeDelta = new Vector2(128f, 36f);
            loadButton.onClick.AddListener(() => LoadEntry(entry));

            var scroll = CreateObject("DetailScrollView", detailRoot.transform, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            Stretch(scroll.GetComponent<RectTransform>(), DetailMargin, DetailMargin, 80f, 28f);
            scroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            var scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var viewport = CreateObject("Viewport", scroll.transform, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            Stretch(viewport.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            var content = CreateObject("DetailContent", viewport.transform, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, contentRect.offsetMin.y);
            contentRect.offsetMax = new Vector2(0f, 0f);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(0, 0, 0, 24);
            layout.spacing = 20f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;

            CreateHeaderCard(content.transform, entry);
            CreateSection(content.transform, "案例说明", entry.Description);
            CreateSection(content.transform, "学习目标", entry.LearningGoal);
            CreateSection(content.transform, "主要元件", entry.MainComponents);
            CreateSection(content.transform, "知识点", entry.KeyPoints);
            CreateSection(content.transform, "常见错误提醒", entry.CommonMistakes);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private void CreateHeaderCard(Transform parent, GalleryEntry entry)
        {
            var card = CreateSectionCard(parent, 230f);
            var layout = card.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 24f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = false;

            var imagePanel = CreateObject("ImagePanel", card.transform, typeof(RectTransform), typeof(Image));
            var imageRect = imagePanel.GetComponent<RectTransform>();
            imageRect.sizeDelta = new Vector2(320f, 190f);
            var imageLayout = imagePanel.AddComponent<LayoutElement>();
            imageLayout.minWidth = 320f;
            imageLayout.preferredWidth = 320f;
            imageLayout.flexibleWidth = 0f;
            imageLayout.minHeight = 190f;
            imageLayout.preferredHeight = 190f;
            imageLayout.flexibleHeight = 0f;
            var headerImage = imagePanel.GetComponent<Image>();
            headerImage.sprite = UiThemeTokens.GetRoundedSprite(12);
            headerImage.type = Image.Type.Sliced;
            headerImage.color = MainUiTheme.Hex("F1F5F9");
            var sprite = LoadThumbnail(entry.ThumbnailPath);
            if (sprite != null)
            {
                var image = CreateObject("Image", imagePanel.transform, typeof(RectTransform), typeof(Image));
                Stretch(image.GetComponent<RectTransform>(), 12f, 12f, 10f, 10f);
                var uiImage = image.GetComponent<Image>();
                uiImage.sprite = sprite;
                uiImage.preserveAspect = true;
                uiImage.color = Color.white;
            }
            else
            {
                var placeholder = CreateText("Placeholder", imagePanel.transform, "案例缩略图\n待补充", 14, FontStyle.Normal, MainUiTheme.Hex("94A3B8"));
                placeholder.alignment = TextAnchor.MiddleCenter;
                Stretch(placeholder.rectTransform, 0f, 0f, 0f, 0f);
            }

            var infoPanel = CreateObject("InfoPanel", card.transform, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var infoElement = infoPanel.GetComponent<LayoutElement>();
            infoElement.minWidth = 0f;
            infoElement.preferredWidth = 0f;
            infoElement.flexibleWidth = 1f;
            var infoLayout = infoPanel.GetComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 7f;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;

            CreateFlowText(infoPanel.transform, entry.Title, MainUiTheme.UiTextRole.DetailHeaderTitle, MainUiTheme.Hex("1F2937"));
            CreateFlowText(infoPanel.transform, "类型：" + entry.Category + "    难度：" + entry.Difficulty + "    来源：" + entry.SourceLabel, MainUiTheme.UiTextRole.DetailMetaText, MainUiTheme.Hex("64748B"));
            CreateFlowText(infoPanel.transform, "标签：" + string.Join("、", entry.Tags.ToArray()), MainUiTheme.UiTextRole.DetailMetaText, MainUiTheme.PrimaryBlue);
            CreateFlowText(infoPanel.transform, GetShortDescription(entry.Description), MainUiTheme.UiTextRole.DetailBody, MainUiTheme.Hex("475569"));
        }

        private void CreateSection(Transform parent, string title, string body)
        {
            var card = CreateObject("SectionCard", parent, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement), typeof(UnityEngine.UI.Shadow));
            var image = card.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(15);
            image.type = Image.Type.Sliced;
            image.color = CardBackground;
            var shadow = card.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.03f);
            shadow.effectDistance = new Vector2(0, -2);
            var vertical = card.GetComponent<VerticalLayoutGroup>();
            vertical.padding = new RectOffset(20, 20, 12, 12);
            vertical.spacing = 8f;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            var fitter = card.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.GetComponent<LayoutElement>().minHeight = 79f;

            CreateFlowText(card.transform, title, MainUiTheme.UiTextRole.DetailSectionTitle, MainUiTheme.Hex("1F2937"));
            CreateFlowText(card.transform, string.IsNullOrWhiteSpace(body) ? "该部分内容待补充。" : body, MainUiTheme.UiTextRole.DetailBody, MainUiTheme.Hex("475569"));
        }

        private GameObject CreateSectionCard(Transform parent, float minHeight = 196f)
        {
            var card = CreateObject("SectionCard", parent, typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement), typeof(UnityEngine.UI.Shadow));
            var image = card.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(15);
            image.type = Image.Type.Sliced;
            image.color = CardBackground;
            var shadow = card.GetComponent<UnityEngine.UI.Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.03f);
            shadow.effectDistance = new Vector2(0, -2);
            var layout = card.GetComponent<HorizontalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            var fitter = card.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            card.GetComponent<LayoutElement>().minHeight = minHeight;
            return card;
        }

        private void CreateMetadataPill(Transform parent, string name, string text, Color background, Color textColor, float width, MainUiTheme.UiTextRole textRole)
        {
            var pill = CreateObject(name, parent, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var image = pill.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = background;

            var element = pill.GetComponent<LayoutElement>();
            element.minWidth = width;
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            element.minHeight = 22f;
            element.preferredHeight = 22f;
            element.flexibleHeight = 0f;

            var label = CreateText("Text", pill.transform, text, 12, FontStyle.Normal, textColor);
            MainUiTheme.ApplyTextRole(label, textRole);
            label.alignment = TextAnchor.MiddleCenter;
            Stretch(label.rectTransform, 6f, 6f, 0f, 0f);
        }

        private void CreateMetadataSource(Transform parent, string sourceLabel)
        {
            var source = CreateObject("Source", parent, typeof(RectTransform), typeof(LayoutElement));
            var element = source.GetComponent<LayoutElement>();
            element.minWidth = 0f;
            element.preferredWidth = 120f;
            element.flexibleWidth = 1f;
            element.minHeight = 22f;
            element.preferredHeight = 22f;
            element.flexibleHeight = 0f;

            var label = CreateText("Text", source.transform, "来源：" + sourceLabel, 12, FontStyle.Normal, MainUiTheme.Hex("64748B"));
            MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.MetaText);
            label.alignment = TextAnchor.MiddleLeft;
            Stretch(label.rectTransform, 0f, 0f, 0f, 0f);
        }

        private void LoadEntry(GalleryEntry entry)
        {
            // Gallery 只发起选中示例的正式模板加载入口；真正的模板校验、组件生成、Wire 恢复和运行态清理由下游服务负责。
            // 加载失败必须保留 status 反馈，不能因列表 UI 已切换而假定当前 Workspace 已成功重建。
            var loader = FindObjectOfType<TemplateLoadController>();
            if (loader == null)
            {
                SetStatus("模板加载控制器未就绪，无法加载案例。");
                return;
            }

            loader.RequestLoadTemplateFromGallery(entry.CatalogItem, () =>
            {
                var navigation = FindObjectOfType<TopNavigationController>();
                if (navigation != null)
                {
                    navigation.SelectTab(0);
                }
            });
        }

        private static string[] BuildTags(CircuitTemplateCatalogItemDto item, string title)
        {
            var tags = new List<string>();
            AddTag(tags, item.category);
            if (Contains(title, "照明")) AddTag(tags, "照明");
            if (Contains(title, "双控")) AddTag(tags, "双控");
            if (Contains(title, "电能表")) AddTag(tags, "电能表");
            if (Contains(title, "风扇")) AddTag(tags, "风扇");
            if (Contains(title, "空开") || Contains(title, "空气开关")) AddTag(tags, "空气开关");
            if (Contains(title, "点动")) AddTag(tags, "点动");
            if (Contains(title, "连续")) AddTag(tags, "连续运行");
            if (Contains(title, "热继")) AddTag(tags, "热继保护");
            if (Contains(title, "正反转")) AddTag(tags, "正反转");
            if (Contains(title, "互锁")) AddTag(tags, "互锁");
            if (Contains(title, "自动往返")) AddTag(tags, "自动往返");
            if (Contains(title, "时间继电器") || Contains(title, "顺序")) AddTag(tags, "时间继电器");
            if (Contains(title, "星三角")) AddTag(tags, "星三角");
            if (item.category == "工业电路") AddTag(tags, "电机控制");
            return tags.Count == 0 ? new[] { "本地案例" } : tags.ToArray();
        }

        private static string BuildDescription(CircuitTemplateCatalogItemDto item, string title, string[] tags)
        {
            if (!string.IsNullOrWhiteSpace(item.description))
            {
                return item.description;
            }

            if (tags.Contains("星三角")) return "通过时间继电器实现电机由星形启动切换到三角运行，适合学习降压启动的基本过程。";
            if (tags.Contains("自动往返")) return "通过行程开关和正反转接触器实现运动机构自动往返，适合学习限位换向与自锁保持。";
            if (tags.Contains("正反转")) return "通过两个接触器切换电机相序，学习正转、反转以及互锁保护。";
            if (tags.Contains("照明")) return "典型家庭照明接线案例，适合练习火线、零线、开关和负载之间的连接关系。";
            return title + " 是系统内置本地教学案例，可用于查看标准接线并进行仿真练习。";
        }

        private static string BuildLearningGoal(string title, string[] tags)
        {
            if (tags.Contains("家庭电路"))
            {
                return "理解家庭电路中电源、开关、保护元件和负载的连接关系，练习按图完成照明或并联负载接线。";
            }

            if (tags.Contains("星三角"))
            {
                return "掌握星形启动、延时切换和三角运行的控制顺序，理解星形接触器与三角接触器不能同时吸合。";
            }

            if (tags.Contains("自动往返"))
            {
                return "掌握行程开关常闭触点切断当前方向、常开触点启动反向回路的自动换向逻辑。";
            }

            if (tags.Contains("正反转"))
            {
                return "掌握三相电机正反转主回路换相方法，理解按钮互锁、接触器互锁和双重联锁的作用。";
            }

            return "掌握该控制电路的主要元件、控制回路路径和运行过程，并能在仿真画布中进行验证。";
        }

        private static string BuildMainComponents(string title, string[] tags)
        {
            if (tags.Contains("星三角")) return "三相电源、空气开关、交流接触器 KM、时间继电器 KT、热继电器 FR、星三角电机。";
            if (tags.Contains("自动往返")) return "三相电源、正反转接触器、行程开关 SQ、启动/停止按钮、三相异步电机。";
            if (tags.Contains("正反转")) return "三相电源、两个交流接触器、按钮、互锁触点、三相异步电机。";
            if (tags.Contains("家庭电路")) return "220V 电源、空气开关、开关、灯泡、电风扇或单相电能表。";
            return "电源、控制元件、执行元件、负载和必要的保护元件。";
        }

        private static string BuildKeyPoints(string title, string[] tags)
        {
            if (tags.Contains("星三角")) return "延时切换、星形接触器和三角接触器互锁、主回路六端子连接。";
            if (tags.Contains("自动往返")) return "SQ 11/12 常闭限位切断当前方向，SQ 23/24 常开触点给出反向启动信号。";
            if (tags.Contains("正反转")) return "换相、互锁、自锁、停止回路以及正反转不能同时吸合。";
            if (tags.Contains("照明")) return "火线进开关、零线进负载、保护元件串入电源入口，负载需要形成完整回路。";
            return "电源路径、控制回路、保护触点和负载状态。";
        }

        private static string BuildCommonMistakes(string title, string[] tags)
        {
            if (tags.Contains("星三角")) return "不要让星形和三角接触器同时吸合；不要忽略时间继电器延时触点；不要混淆电机 U1/V1/W1 与 U2/V2/W2。";
            if (tags.Contains("自动往返")) return "不要把 SQ 的 11/12 当作启动源；不要让两个方向接触器同时吸合；离开限位后应依靠对向接触器自锁保持。";
            if (tags.Contains("正反转")) return "不要取消互锁；不要让两个接触器同时得电；主回路换相和控制回路互锁都需要核对。";
            if (tags.Contains("家庭电路")) return "不要把 PE 当作工作零线；不要让 L 与 N 直接短接；开关通常应控制火线。";
            return "加载案例后建议先查看主回路和控制回路，再运行仿真并使用检查助手核对。";
        }

        private static bool IsRecommended(string title, string[] tags)
        {
            return tags.Contains("照明")
                || tags.Contains("连续运行")
                || tags.Contains("正反转")
                || tags.Contains("自动往返")
                || tags.Contains("星三角")
                || tags.Contains("时间继电器");
        }

        private static void AddTag(List<string> tags, string tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !tags.Contains(tag))
            {
                tags.Add(tag);
            }
        }

        private Sprite LoadThumbnail(string path)
        {
            // 缩略图加载只服务视觉预览。资源缺失可返回 null/fallback，不得影响模板是否可被正式 loader 读取。
            // Catalog 路径按 Resources 规则传入且不带扩展名；Player 不应依赖 AssetDatabase。
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return Resources.Load<Sprite>(path);
        }

        private void SetStatus(string message)
        {
            // 状态文本是页面反馈的表现投影，不保存为示例配置，也不参与 Workspace/Practice 的业务状态判断。
            if (statusText != null)
            {
                statusText.text = message ?? string.Empty;
            }
        }

        private static int DifficultyRank(string difficulty)
        {
            if (string.IsNullOrWhiteSpace(difficulty)) return 0;
            if (difficulty.Contains("中高级")) return 2;
            if (difficulty.Contains("高")) return 3;
            if (difficulty.Contains("中")) return 1;
            return 0;
        }

        private static Color GetDifficultyPillColor(string difficulty)
        {
            if (Contains(difficulty, "中高级")) return MainUiTheme.Hex("15803D");
            if (Contains(difficulty, "高级")) return MainUiTheme.Hex("EF4444");
            if (Contains(difficulty, "中级")) return MainUiTheme.Hex("F59E0B");
            return MainUiTheme.Hex("16A34A");
        }

        private static string GetShortDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                return string.Empty;
            }

            return description
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        }

        private static bool Contains(string source, string keyword)
        {
            return !string.IsNullOrWhiteSpace(source)
                && !string.IsNullOrWhiteSpace(keyword)
                && source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Color HexColor(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }

        private GameObject CreateObject(string name, Transform parent, params Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(parent, false);
            return go;
        }

        private Text CreateText(string name, Transform parent, string text, int size, FontStyle style, Color color)
        {
            var go = CreateObject(name, parent, typeof(RectTransform), typeof(Text));
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.UiFont;
            label.fontSize = size;
            label.fontStyle = FontStyle.Normal;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private Text CreateFlowText(Transform parent, string text, MainUiTheme.UiTextRole role, Color color)
        {
            var label = CreateText("Text", parent, text, 14, FontStyle.Normal, color);
            MainUiTheme.ApplyTextRole(label, role);
            return label;
        }

        private Text CreateFlowText(Transform parent, string text, int size, FontStyle style, Color color)
        {
            var label = CreateText("Text", parent, text, size, style, color);
            return label;
        }

        private Button CreatePillButton(Transform parent, string text)
        {
            var go = CreateObject("FilterButton_" + text, parent, typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter), typeof(Outline));
            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(16);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            image.raycastTarget = true;

            var outline = go.GetComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("D2D2D2");
            outline.effectDistance = new Vector2(1f, -1f);

            var btn = go.GetComponent<Button>();
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 0, 0);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 36f);

            var sprite = GetFilterIconSprite(text);
            if (sprite != null)
            {
                var iconGo = CreateObject("Icon", go.transform, typeof(RectTransform), typeof(Image));
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.sizeDelta = new Vector2(20f, 20f);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.color = MainUiTheme.Hex("6B7280");
                iconImg.sprite = sprite;
                iconImg.type = Image.Type.Simple;
                iconImg.preserveAspect = true;
                iconImg.raycastTarget = false;
            }

            var label = CreateText("Text", go.transform, text, 16, FontStyle.Normal, MainUiTheme.Hex("464646"));
            MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.ContentFilterText);
            
            return btn;
        }

        private Sprite GetFilterIconSprite(string filterName)
        {
            string path = null;
            switch (filterName)
            {
                case "全部": path = "UIAssets/Filter/ui_filter_all_24"; break;
                case "推荐": path = "UIAssets/Filter/ui_filter_recommend_24"; break;
                case "家庭电路": path = "UIAssets/Filter/ui_filter_home_24"; break;
                case "工业电路": path = "UIAssets/Filter/ui_filter_industry_24"; break;
                case "电机控制": path = "UIAssets/Filter/ui_filter_motor_24"; break;
                case "正反转": path = "UIAssets/Filter/ui_filter_reverse_24"; break;
                case "星三角": path = "UIAssets/Filter/ui_filter_star_delta_24"; break;
                case "自动往返": path = "UIAssets/Filter/ui_filter_auto_reciprocating_24"; break;
            }
            return string.IsNullOrEmpty(path) ? null : Resources.Load<Sprite>(path);
        }

        private Button CreateButton(Transform parent, string text, Color background, Color textColor, MainUiTheme.UiTextRole textRole = MainUiTheme.UiTextRole.CardActionButton)
        {
            var go = CreateObject("Button", parent, typeof(RectTransform), typeof(Image), typeof(Button));
            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = background;
            image.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;

            var label = CreateText("Text", go.transform, text, 16, FontStyle.Normal, textColor);
            MainUiTheme.ApplyTextRole(label, textRole);
            Stretch(label.rectTransform, 0f, 0f, 0f, 0f);
            return btn;
        }

        private InputField CreateInput(Transform parent, string placeholder, float width, float height)
        {
            var go = CreateObject("SearchInput", parent, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(15);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var text = CreateText("Text", go.transform, string.Empty, 14, FontStyle.Normal, TextPrimary);
            MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.SearchInputText);

            var hint = CreateText("Placeholder", go.transform, placeholder, 14, FontStyle.Normal, HexColor(0x94A3B8));
            MainUiTheme.ApplyTextRole(hint, MainUiTheme.UiTextRole.SearchInputText);

            var iconSprite = Resources.Load<Sprite>("UIAssets/Common/ui_common_search_24");
            if (iconSprite != null)
            {
                var iconGo = CreateObject("SearchIcon", go.transform, typeof(RectTransform), typeof(Image));
                var iconRect = iconGo.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0f, 0.5f);
                iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(10f, 0f);
                iconRect.sizeDelta = new Vector2(18f, 18f);
                var iconImg = iconGo.GetComponent<Image>();
                iconImg.sprite = iconSprite;
                iconImg.color = MainUiTheme.Hex("94A3B8");
                iconImg.raycastTarget = false;

                Stretch(text.rectTransform, 36f, 10f, 4f, 4f);
                Stretch(hint.rectTransform, 36f, 10f, 4f, 4f);
            }
            else
            {
                Stretch(text.rectTransform, 12f, 10f, 4f, 4f);
                Stretch(hint.rectTransform, 12f, 10f, 4f, 4f);
            }

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = go.GetComponent<Image>();
            return input;
        }

        private Dropdown CreateDropdown(Transform parent, float width, float height)
        {
            var go = CreateObject("SortDropdown", parent, typeof(RectTransform), typeof(Image), typeof(Dropdown));
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var label = CreateText("Label", go.transform, string.Empty, 14, FontStyle.Normal, TextPrimary);
            MainUiTheme.ApplyTextRole(label, MainUiTheme.UiTextRole.SearchInputText);
            Stretch(label.rectTransform, 12f, 28f, 0f, 0f);
            label.alignment = TextAnchor.MiddleLeft;

            var iconSprite = Resources.Load<Sprite>("UIAssets/Common/ui_common_dropdown_24");
            if (iconSprite != null)
            {
                var arrowGo = CreateObject("Arrow", go.transform, typeof(RectTransform), typeof(Image));
                var arrowRect = arrowGo.GetComponent<RectTransform>();
                arrowRect.anchorMin = new Vector2(1f, 0.5f);
                arrowRect.anchorMax = new Vector2(1f, 0.5f);
                arrowRect.pivot = new Vector2(1f, 0.5f);
                arrowRect.anchoredPosition = new Vector2(-10f, 0f);
                arrowRect.sizeDelta = new Vector2(18f, 18f);
                var arrowImg = arrowGo.GetComponent<Image>();
                arrowImg.sprite = iconSprite;
                arrowImg.color = TextSecondary;
                arrowImg.raycastTarget = false;
            }
            else
            {
                var arrow = CreateText("Arrow", go.transform, "▼", 12, FontStyle.Normal, TextSecondary);
                arrow.alignment = TextAnchor.MiddleCenter;
                arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
                arrow.rectTransform.anchorMax = new Vector2(1f, 1f);
                arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
                arrow.rectTransform.offsetMin = new Vector2(-28f, 0f);
                arrow.rectTransform.offsetMax = new Vector2(0f, 0f);
            }

            var dropdown = go.GetComponent<Dropdown>();
            dropdown.captionText = label;
            dropdown.targetGraphic = go.GetComponent<Image>();
            CreateDropdownTemplate(go.transform, dropdown);
            return dropdown;
        }

        private void CreateDropdownTemplate(Transform parent, Dropdown dropdown)
        {
            var template = CreateObject("Template", parent, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -2f);
            templateRect.sizeDelta = new Vector2(0f, 180f);
            template.GetComponent<Image>().color = Color.white;

            var viewport = CreateObject("Viewport", template.transform, typeof(RectTransform), typeof(Image), typeof(Mask));
            Stretch(viewport.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = Color.white;
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var content = CreateObject("Content", viewport.transform, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = new Vector2(0f, contentRect.offsetMin.y);
            contentRect.offsetMax = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var item = CreateObject("Item", content.transform, typeof(RectTransform), typeof(Toggle));
            item.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 32f);
            var toggle = item.GetComponent<Toggle>();

            var itemBackground = CreateObject("Item Background", item.transform, typeof(RectTransform), typeof(Image));
            Stretch(itemBackground.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
            itemBackground.GetComponent<Image>().color = HexColor(0xF8FAFC);

            var checkmark = CreateText("Item Checkmark", item.transform, "✓", 13, FontStyle.Bold, Blue);
            checkmark.alignment = TextAnchor.MiddleCenter;
            checkmark.rectTransform.anchorMin = new Vector2(0f, 0f);
            checkmark.rectTransform.anchorMax = new Vector2(0f, 1f);
            checkmark.rectTransform.pivot = new Vector2(0f, 0.5f);
            checkmark.rectTransform.offsetMin = new Vector2(8f, 0f);
            checkmark.rectTransform.offsetMax = new Vector2(28f, 0f);

            var itemLabel = CreateText("Item Label", item.transform, "Option", 13, FontStyle.Normal, TextPrimary);
            MainUiTheme.ApplyTextRole(itemLabel, MainUiTheme.UiTextRole.SearchInputText);
            Stretch(itemLabel.rectTransform, 32f, 8f, 0f, 0f);

            toggle.targetGraphic = itemBackground.GetComponent<Image>();
            toggle.graphic = checkmark;

            var templateScrollRect = template.GetComponent<ScrollRect>();
            templateScrollRect.horizontal = false;
            templateScrollRect.vertical = true;
            templateScrollRect.viewport = viewport.GetComponent<RectTransform>();
            templateScrollRect.content = contentRect;

            template.SetActive(false);
            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
        }

        private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }

        private sealed class GalleryEntry
        {
            public CircuitTemplateCatalogItemDto CatalogItem;
            public string Id;
            public string Title;
            public string Category;
            public string Difficulty;
            public string Description;
            public string LearningGoal;
            public string MainComponents;
            public string KeyPoints;
            public string CommonMistakes;
            public string SourceLabel;
            public string TemplateId;
            public string ThumbnailPath;
            public string[] Tags;
            public bool Recommended;
        }
    }
}
