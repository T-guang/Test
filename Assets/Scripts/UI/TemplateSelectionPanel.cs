using System.Collections.Generic;
using ElectricalSim.Templates;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 展示系统模板目录项并向调用方返回用户选择的模板。
    /// 面板只消费 Catalog 数据完成分类、选中和卡片展示，不扫描 Resources，不读取模板 JSON，也不生成 CircuitComponent 或 WireView。
    /// 运行时 UI 只在 Create 时构建一次；Show 仅替换目录数据并刷新列表，避免反复打开时重复绑定选择回调。
    /// 修改后必须检查家庭/工业分类、模板数量、重复打开关闭和卡片加载动作。
    /// </summary>
    public sealed class TemplateSelectionPanel : MonoBehaviour
    {
        private const string FamilyCategory = "家庭电路";
        private const string IndustrialCategory = "工业电路";
        private const float DialogWidth = 800f;
        private const float DialogHeight = 640f;
        private const float HeaderHeight = 56f;
        private const float TabBarTop = 64f;
        private const float TabBarHeight = 40f;
        private const float ContentTop = 116f;

        [SerializeField] private RectTransform dialogPanel;
        [SerializeField] private RectTransform content;
        [SerializeField] private Button closeButton;
        [SerializeField] private Text emptyText;
        [SerializeField] private Button familyButton;
        [SerializeField] private Button industrialButton;

        private readonly List<CircuitTemplateCatalogItemDto> allTemplates = new List<CircuitTemplateCatalogItemDto>();
        private System.Action<CircuitTemplateCatalogItemDto> onSelected;
        private string currentCategory = FamilyCategory;

        public static TemplateSelectionPanel Create(RectTransform parent, System.Action<CircuitTemplateCatalogItemDto> selectedCallback)
        {
            // Controller 负责复用这个面板实例；此处只负责首次创建完整的模态展示层和固定回调。
            var rootObject = new GameObject(
                "LoadTemplateModalRoot",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(GraphicRaycaster),
                typeof(CanvasGroup),
                typeof(TemplateSelectionPanel));
            rootObject.transform.SetParent(parent, false);

            var rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var canvas = rootObject.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 5000;

            var canvasGroup = rootObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;

            var panel = rootObject.GetComponent<TemplateSelectionPanel>();
            panel.onSelected = selectedCallback;
            panel.Build(rootRect);
            panel.Hide();
            return panel;
        }

        public void Show(IReadOnlyList<CircuitTemplateCatalogItemDto> templates)
        {
            allTemplates.Clear();
            if (templates != null)
            {
                allTemplates.AddRange(templates);
            }

            currentCategory = FamilyCategory;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshTemplateListByCategory(currentCategory);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        private void Build(RectTransform root)
        {
            CreateModalBlocker(root);
            dialogPanel = CreateDialogPanel(root);
            CreateHeader(dialogPanel);
            CreateTabBar(dialogPanel);
            CreateContentArea(dialogPanel);
        }

        private static void CreateModalBlocker(RectTransform parent)
        {
            var blocker = new GameObject("ModalBlocker", typeof(RectTransform), typeof(Image));
            blocker.transform.SetParent(parent, false);

            var rect = blocker.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = blocker.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.4f);
            image.raycastTarget = true;
        }

        private static RectTransform CreateDialogPanel(RectTransform parent)
        {
            var panel = new GameObject("DialogPanel", typeof(RectTransform), typeof(Image), typeof(Outline));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(DialogWidth, DialogHeight);

            var image = panel.GetComponent<Image>();
            image.color = Color.white;
            image.sprite = UiThemeTokens.GetRoundedSprite(16);
            image.type = Image.Type.Sliced;
            image.raycastTarget = true;

            var outline = panel.GetComponent<Outline>();
            outline.effectColor = new Color(0.886f, 0.91f, 0.941f);
            outline.effectDistance = new Vector2(1, -1);

            return rect;
        }

        private void CreateHeader(RectTransform parent)
        {
            var header = new GameObject("Header", typeof(RectTransform), typeof(Image));
            header.transform.SetParent(parent, false);

            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -HeaderHeight);
            headerRect.offsetMax = Vector2.zero;

            var headerImage = header.GetComponent<Image>();
            headerImage.color = Color.white;
            headerImage.raycastTarget = false;

            var title = CreateText("TitleText", header.transform, "加载标准图纸", 20, FontStyle.Bold, new Color(0.118f, 0.161f, 0.231f));
            title.font = MainUiTheme.TitleFont;
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.anchorMin = Vector2.zero;
            title.rectTransform.anchorMax = Vector2.one;
            title.rectTransform.offsetMin = new Vector2(24f, 0f);
            title.rectTransform.offsetMax = new Vector2(-120f, 0f);

            closeButton = CreateButton(header.transform, "×", new Color(0.973f, 0.98f, 0.988f), new Color(0.392f, 0.455f, 0.545f), 22);
            closeButton.name = "CloseButton";
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-24f, 0f);
            closeRect.sizeDelta = new Vector2(36f, 36f);
            closeButton.onClick.AddListener(Hide);

            var divider = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            divider.transform.SetParent(header.transform, false);
            var dividerRect = divider.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.offsetMin = new Vector2(0f, 0f);
            dividerRect.offsetMax = new Vector2(0f, 1f);
            divider.GetComponent<Image>().color = new Color(0.90f, 0.92f, 0.95f, 1f);
        }

        private void CreateTabBar(RectTransform parent)
        {
            var tabBar = new GameObject("TabBar", typeof(RectTransform));
            tabBar.transform.SetParent(parent, false);
            var tabRect = tabBar.GetComponent<RectTransform>();
            tabRect.anchorMin = new Vector2(0f, 1f);
            tabRect.anchorMax = new Vector2(1f, 1f);
            tabRect.pivot = new Vector2(0.5f, 1f);
            tabRect.offsetMin = new Vector2(24f, -(TabBarTop + TabBarHeight));
            tabRect.offsetMax = new Vector2(-24f, -TabBarTop);

            familyButton = CreateButton(tabBar.transform, FamilyCategory, new Color(0.145f, 0.388f, 0.922f), Color.white, 15);
            familyButton.name = "FamilyTabButton";
            var familyRect = familyButton.GetComponent<RectTransform>();
            familyRect.anchorMin = new Vector2(0f, 0.5f);
            familyRect.anchorMax = new Vector2(0f, 0.5f);
            familyRect.pivot = new Vector2(0f, 0.5f);
            familyRect.anchoredPosition = Vector2.zero;
            familyRect.sizeDelta = new Vector2(110f, 40f);
            familyButton.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            familyButton.onClick.AddListener(ShowFamilyTemplates);

            industrialButton = CreateButton(tabBar.transform, IndustrialCategory, Color.white, new Color(0.392f, 0.455f, 0.545f), 15);
            industrialButton.name = "IndustrialTabButton";
            var indOutline = industrialButton.gameObject.AddComponent<Outline>();
            indOutline.effectColor = new Color(0.824f, 0.824f, 0.824f);
            indOutline.effectDistance = new Vector2(1, -1);
            var industrialRect = industrialButton.GetComponent<RectTransform>();
            industrialRect.anchorMin = new Vector2(0f, 0.5f);
            industrialRect.anchorMax = new Vector2(0f, 0.5f);
            industrialRect.pivot = new Vector2(0f, 0.5f);
            industrialRect.anchoredPosition = new Vector2(120f, 0f);
            industrialRect.sizeDelta = new Vector2(110f, 40f);
            industrialButton.onClick.AddListener(ShowIndustrialTemplates);
        }

        private void CreateContentArea(RectTransform parent)
        {
            var scrollObject = new GameObject("ContentArea", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = new Vector2(24f, 24f);
            scrollRectTransform.offsetMax = new Vector2(-24f, -ContentTop);

            var scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = new Color(0.945f, 0.961f, 0.976f);
            scrollImage.sprite = UiThemeTokens.GetRoundedSprite(8);
            scrollImage.type = Image.Type.Sliced;
            scrollImage.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scrollObject.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(10f, 10f);
            viewportRect.offsetMax = new Vector2(-10f, -10f);
            var viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            emptyText = CreateText("EmptyText", scrollObject.transform, "暂无标准图纸。", 16, FontStyle.Normal, new Color(0.32f, 0.38f, 0.48f));
            emptyText.alignment = TextAnchor.MiddleCenter;
            emptyText.rectTransform.anchorMin = Vector2.zero;
            emptyText.rectTransform.anchorMax = Vector2.one;
            emptyText.rectTransform.offsetMin = new Vector2(24f, 24f);
            emptyText.rectTransform.offsetMax = new Vector2(-24f, -24f);
            emptyText.gameObject.SetActive(false);

            var scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 26f;
        }

        private void ShowFamilyTemplates()
        {
            RefreshTemplateListByCategory(FamilyCategory);
        }

        private void ShowIndustrialTemplates()
        {
            RefreshTemplateListByCategory(IndustrialCategory);
        }

        private void RefreshTemplateListByCategory(string category)
        {
            currentCategory = category;
            UpdateCategoryButtonState();

            var filteredTemplates = new List<CircuitTemplateCatalogItemDto>();
            foreach (var template in allTemplates)
            {
                if (template != null && template.category == category)
                {
                    filteredTemplates.Add(template);
                }
            }

            RebuildList(filteredTemplates);
            if (emptyText != null && filteredTemplates.Count == 0)
            {
                emptyText.text = category == IndustrialCategory ? "暂无工业电路模板。" : "暂无该分类模板。";
            }
        }

        private void UpdateCategoryButtonState()
        {
            ApplyCategoryButtonStyle(familyButton, currentCategory == FamilyCategory);
            ApplyCategoryButtonStyle(industrialButton, currentCategory == IndustrialCategory);
        }

        private static void ApplyCategoryButtonStyle(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected ? new Color(0.15f, 0.39f, 0.92f) : new Color(0.94f, 0.96f, 0.98f);
            }

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = selected ? Color.white : new Color(0.20f, 0.25f, 0.33f);
            }
        }

        private void RebuildList(IReadOnlyList<CircuitTemplateCatalogItemDto> templates)
        {
            if (content == null)
            {
                return;
            }

            // 每次按分类重建的是卡片子项，不重建面板本身；卡片回调统一指向 HandleSelected。
            for (var i = content.childCount - 1; i >= 0; i--)
            {
                Destroy(content.GetChild(i).gameObject);
            }

            var hasTemplates = templates != null && templates.Count > 0;
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(!hasTemplates);
            }

            var y = -2f;
            if (templates != null)
            {
                foreach (var template in templates)
                {
                    var item = TemplateListItem.Create(content);
                    var rect = item.GetComponent<RectTransform>();
                    rect.anchoredPosition = new Vector2(0f, y);
                    item.Initialize(template, HandleSelected);
                    y -= 112f;
                }
            }

            content.sizeDelta = new Vector2(0f, Mathf.Max(0f, -y + 10f));
        }

        private void HandleSelected(CircuitTemplateCatalogItemDto item)
        {
            Hide();
            onSelected?.Invoke(item);
        }

        private static Text CreateText(string name, Transform parent, string text, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = TextAnchor.UpperLeft;
            label.color = color;
            label.raycastTarget = false;
            return label;
        }

        private static Button CreateButton(Transform parent, string text, Color color, Color textColor, int fontSize)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.sprite = UiThemeTokens.GetRoundedSprite(8);
            img.type = Image.Type.Sliced;
            var label = CreateText("Text", go.transform, text, fontSize, FontStyle.Normal, textColor);
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return go.GetComponent<Button>();
        }
    }
}
