using System.Collections.Generic;
using ElectricalSim.UI.CommonTools;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 顶部导航的场景绑定控制器：在 Awake 配置 PageRouter、导航页根节点与标签点击事件，
    /// 在 Start 补齐全局退出入口。它只切换页面和维护导航视觉，不负责各页面的内容构建、
    /// 模板加载或退出前保存；这些职责分别留在页面 Controller、TemplateLoadController 与 ExitApplicationDialog。
    /// 当前实现依赖 Demo 场景中序列化的页根节点和按钮列表，运行时仅补建缺失的 PageRouter/退出按钮。
    /// Editor 与 Windows Player 共用此路径；后续若改为预制体化导航，需重新确认重复监听器和单实例退出按钮约束。
    /// </summary>
    public sealed class TopNavigationController : MonoBehaviour
    {
        [SerializeField] private PageRouter pageRouter;
        [SerializeField] private List<Button> tabButtons = new List<Button>();
        [SerializeField] private List<Text> tabLabels = new List<Text>();
        [SerializeField] private GameObject simulationRoot;
        [SerializeField] private GameObject blueprintRoot;
        [SerializeField] private GameObject encyclopediaRoot;
        [SerializeField] private GameObject toolsRoot;
        [SerializeField] private GameObject emptyPageRoot;
        [SerializeField] private Text emptyPageTitle;

        private Button exitButton;

        private void Awake()
        {
            // 路由器必须在任何标签点击前完成配置；这里的空引用兜底仅服务当前场景兼容，不应扩展为页面创建入口。
            if (pageRouter == null)
            {
                pageRouter = FindObjectOfType<PageRouter>();
            }

            if (pageRouter == null)
            {
                pageRouter = gameObject.AddComponent<PageRouter>();
            }

            pageRouter.Configure(
                simulationRoot,
                blueprintRoot,
                null,
                encyclopediaRoot,
                toolsRoot,
                null,
                emptyPageRoot,
                emptyPageTitle);

            if (toolsRoot != null && toolsRoot.GetComponent<CommonToolsPageController>() == null)
            {
                toolsRoot.AddComponent<CommonToolsPageController>();
            }

            if (simulationRoot != null)
            {
                var bg = simulationRoot.GetComponent<Image>();
                if (bg != null) bg.color = UiThemeTokens.Background;
            }

            for (var i = 0; i < tabButtons.Count; i++)
            {
                var index = i;
                tabButtons[i].onClick.AddListener(() => SelectTab(index));
            }

            ApplyThemeToNavBar();
            SelectTab(0);
        }

        private void Start()
        {
            // 在全部 Awake 完成后统一补建或复用全局退出按钮，避免重复创建导航按钮。
            EnsureExitButton();
        }

        private void ApplyThemeToNavBar()
        {
            var navRect = GetComponent<RectTransform>();
            if (navRect != null)
            {
                navRect.sizeDelta = new Vector2(navRect.sizeDelta.x, MainUiTheme.NavBarHeight);
                var bg = navRect.GetComponent<Image>();
                if (bg != null) bg.color = Color.white;
                
                if (navRect.Find("BottomBorder") == null)
                {
                    var border = new GameObject("BottomBorder", typeof(RectTransform), typeof(Image));
                    border.transform.SetParent(navRect, false);
                    var borderRt = border.GetComponent<RectTransform>();
                    borderRt.anchorMin = new Vector2(0, 0);
                    borderRt.anchorMax = new Vector2(1, 0);
                    borderRt.pivot = new Vector2(0.5f, 0f);
                    borderRt.sizeDelta = new Vector2(0, 1f);
                    borderRt.anchoredPosition = Vector2.zero;
                    border.GetComponent<Image>().color = UiThemeTokens.BorderColor;
                }
            }

            ApplyBrandGroupLayout();

            for (int i = 0; i < tabButtons.Count; i++)
            {
                var btnRect = tabButtons[i].GetComponent<RectTransform>();
                if (btnRect != null)
                {
                    btnRect.sizeDelta = new Vector2(btnRect.sizeDelta.x, 40f);
                    var img = tabButtons[i].GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = UiThemeTokens.GetRoundedSprite(8);
                        img.type = Image.Type.Sliced;
                    }
                }
                if (i < tabLabels.Count && tabLabels[i] != null)
                {
                    if (i == 5)
                    {
                        tabLabels[i].text = "系统信息";
                    }
                    MainUiTheme.ApplyTextRole(tabLabels[i], MainUiTheme.UiTextRole.NavText);
                }
            }
        }

        private void ApplyBrandGroupLayout()
        {
            var titleTransform = transform.Find("Logo") ?? transform.Find("Title");
            var brandRect = titleTransform as RectTransform;
            if (brandRect == null)
            {
                return;
            }

            brandRect.anchorMin = new Vector2(0f, 0.5f);
            brandRect.anchorMax = new Vector2(0f, 0.5f);
            brandRect.pivot = new Vector2(0f, 0.5f);
            brandRect.anchoredPosition = new Vector2(24f, 0f);
            brandRect.sizeDelta = new Vector2(404f, 48f);

            var layout = titleTransform.GetComponent<HorizontalLayoutGroup>() ?? titleTransform.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(0, 0, 0, 0);
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = false;
            layout.childControlHeight = false;
            layout.childControlWidth = false;

            var iconWrapper = titleTransform.Find("IconWrapper") as RectTransform;
            if (iconWrapper == null)
            {
                var wrapperObj = new GameObject("IconWrapper", typeof(RectTransform), typeof(LayoutElement));
                wrapperObj.transform.SetParent(titleTransform, false);
                wrapperObj.transform.SetAsFirstSibling();
                iconWrapper = wrapperObj.GetComponent<RectTransform>();
            }

            iconWrapper.sizeDelta = new Vector2(40f, 40f);
            var wrapperLayout = iconWrapper.GetComponent<LayoutElement>() ?? iconWrapper.gameObject.AddComponent<LayoutElement>();
            wrapperLayout.preferredWidth = 40f;
            wrapperLayout.preferredHeight = 40f;
            wrapperLayout.flexibleWidth = 0f;
            wrapperLayout.flexibleHeight = 0f;

            var icon = iconWrapper.Find("Icon") as RectTransform;
            if (icon == null)
            {
                var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                iconObj.transform.SetParent(iconWrapper, false);
                icon = iconObj.GetComponent<RectTransform>();
            }

            icon.anchorMin = new Vector2(0.5f, 0.5f);
            icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.pivot = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = Vector2.zero;
            icon.sizeDelta = new Vector2(40f, 40f);

            var iconImage = icon.GetComponent<Image>() ?? icon.gameObject.AddComponent<Image>();
            iconImage.sprite = UiIconLibrary.Load("Logo/ui_logo_main_320");
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;
            iconImage.raycastTarget = false;

            var titleText = titleTransform.Find("Text")?.GetComponent<Text>();
            if (titleText == null)
            {
                titleText = titleTransform.GetComponentInChildren<Text>(true);
            }

            if (titleText != null)
            {
                titleText.transform.SetAsLastSibling();
                titleText.color = MainUiTheme.Hex("111827");
                MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.BrandTitle);

                var textLayout = titleText.GetComponent<LayoutElement>() ?? titleText.gameObject.AddComponent<LayoutElement>();
                textLayout.preferredWidth = 300f;
                textLayout.preferredHeight = 40f;
            }
        }

        private void EnsureExitButton()
        {
            // 退出按钮由导航栏统一持有；重复进入页面时复用已有实例，避免生成多个确认弹窗入口。
            if (exitButton == null)
            {
                var navBar = transform.Find("MainAppRoot/NavBar") as RectTransform;
                if (navBar == null)
                {
                    return;
                }

                var existingButton = navBar.Find("LogoutButton");
                exitButton = existingButton != null ? existingButton.GetComponent<Button>() : null;
                if (exitButton == null)
                {
                    exitButton = CreateExitButton(navBar);
                }
            }

            exitButton.gameObject.SetActive(true);
            ConfigureExitButton(exitButton);
            exitButton.onClick.RemoveListener(ShowExitDialog);
            exitButton.onClick.AddListener(ShowExitDialog);
        }

        private static Button CreateExitButton(RectTransform navBar)
        {
            var buttonObject = new GameObject("ExitApplicationButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(navBar, false);

            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            return buttonObject.GetComponent<Button>();
        }

        private static void ConfigureExitButton(Button button)
        {
            var buttonRect = button.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.anchoredPosition = new Vector2(-24f, 0f);
            buttonRect.sizeDelta = new Vector2(82f, 36f);

            var image = button.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var outline = button.GetComponent<Outline>() ?? button.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.DangerBorder;
            outline.effectDistance = new Vector2(1f, -1f);

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
            colors.pressedColor = new Color(0.86f, 0.92f, 1f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.78431374f, 0.78431374f, 0.78431374f, 0.5019608f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            button.targetGraphic = image;

            var label = button.GetComponentInChildren<Text>(true);
            if (label == null)
            {
                var labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(button.transform, false);
                label = labelObject.GetComponent<Text>();
            }

            label.text = "退出";
            label.font = MainUiTheme.UiFont;
            label.fontSize = 14;
            label.fontStyle = FontStyle.Normal;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = MainUiTheme.DangerRed;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(28f, 0f);
            label.rectTransform.offsetMax = new Vector2(-8f, 0f);

            UiIconLibrary.EnsureButtonIcon(
                button,
                "ui_exit_practice_20",
                new Vector2(16f, 16f),
                new Vector2(16f, 0f),
                MainUiTheme.DangerRed);
        }

        private void ShowExitDialog()
        {
            ExitApplicationDialog.Show(transform);
        }

        public void SelectTab(int index)
        {
            // 由导航按钮监听器调用。PageRouter 是页面可见性的唯一协调者，标签索引与 PageId 的映射不可随意改序。
            var page = ToPageId(index);
            if (pageRouter != null)
            {
                pageRouter.ShowPage(page);
            }

            RefreshTabStates(index);
        }

        private void RefreshTabStates(int activeIndex)
        {
            for (var i = 0; i < tabButtons.Count; i++)
            {
                var active = i == activeIndex;
                var image = tabButtons[i].GetComponent<Image>();
                if (image != null)
                {
                    image.color = active ? MainUiTheme.SelectedBlue : Color.white;
                }

                if (i < tabLabels.Count && tabLabels[i] != null)
                {
                    tabLabels[i].color = active ? MainUiTheme.Hex("2563EB") : MainUiTheme.Hex("334155");
                    MainUiTheme.ApplyTextRole(
                        tabLabels[i],
                        active ? MainUiTheme.UiTextRole.NavTextSelected : MainUiTheme.UiTextRole.NavText);
                }
            }
        }

        private static PageId ToPageId(int index)
        {
            switch (index)
            {
                case 0:
                    return PageId.Simulation;
                case 1:
                    return PageId.Blueprint;
                case 2:
                    return PageId.Square;
                case 3:
                    return PageId.Encyclopedia;
                case 4:
                    return PageId.Tools;
                case 5:
                    return PageId.Profile;
                default:
                    return PageId.Simulation;
            }
        }
    }

    /// <summary>
    /// 全局 UI 视觉主题规范 (V1.6 升级版)
    /// </summary>
    public static class UiThemeTokens
    {
        public static readonly Color Background = ParseColor("#F7F9FC", new Color(0.97f, 0.98f, 1f));
        public static readonly Color CardBackground = Color.white;
        
        public static readonly Color PrimaryBlue = ParseColor("#3B82F6", new Color(0.23f, 0.51f, 0.96f));
        public static readonly Color PrimaryHover = ParseColor("#2563EB", new Color(0.15f, 0.39f, 0.92f));
        public static readonly Color PrimaryLight = ParseColor("#EFF6FF", new Color(0.94f, 0.96f, 1f));
        
        public static readonly Color TextDark = ParseColor("#1E293B", new Color(0.12f, 0.16f, 0.23f));
        public static readonly Color TextMuted = ParseColor("#64748B", new Color(0.39f, 0.45f, 0.55f));
        
        public static readonly Color BorderColor = ParseColor("#E2E8F0", new Color(0.89f, 0.91f, 0.94f));
        public static readonly Color ErrorColor = ParseColor("#EF4444", new Color(0.94f, 0.27f, 0.27f));

        private static Color ParseColor(string hex, Color fallback)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback;
        }

        private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

        /// <summary>
        /// 获取或生成带有边缘抗锯齿的圆角 Sprite，带缓存机制以防内存泄漏
        /// </summary>
        public static Sprite GetRoundedSprite(int radius = 12, int size = 64)
        {
            string key = $"{radius}_{size}";
            if (spriteCache.TryGetValue(key, out var cachedSprite) && cachedSprite != null)
            {
                return cachedSprite;
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x - size / 2f) - (size / 2f - radius));
                    float dy = Mathf.Max(0, Mathf.Abs(y - size / 2f) - (size / 2f - radius));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float alpha = Mathf.Clamp01(radius - dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();

            int border = radius + 2;
            Sprite sprite = Sprite.Create(
                tex, 
                new Rect(0, 0, size, size), 
                new Vector2(0.5f, 0.5f), 
                100, 
                0, 
                SpriteMeshType.FullRect, 
                new Vector4(border, border, border, border)
            );

            spriteCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// 获取标准圆角按钮的 Sprite
        /// </summary>
        public static Sprite GetButtonSprite()
        {
            return GetRoundedSprite(8, 64);
        }
    }
}
