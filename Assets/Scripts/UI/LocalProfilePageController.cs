using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// “系统信息”页的本地数据展示控制器：运行时创建只读信息卡片，显示 persistentDataPath/SavedBlueprints 的统计，
    /// 并提供打开明确本地目录的入口。它不读取或修改图纸 JSON 内容、不承担保存导入逻辑，也不管理用户身份认证。
    /// Awake 建立布局，OnEnable 与尺寸变化时刷新统计和卡片网格；页面不依赖 Editor API，Windows Player 使用同一持久化路径边界。
    /// 后续若替换为预制体 UI，需保持目录操作只作用于 Application.persistentDataPath 对应的应用本地数据目录。
    /// </summary>
    public sealed class LocalProfilePageController : MonoBehaviour
    {
        private const string VersionText = "V1.0 本地版";
        private RectTransform contentRoot;
        private RectTransform contentViewport;
        private GridLayoutGroup cardGrid;
        private Text userInfoText;
        private Text drawingInfoText;
        private Text dataInfoText;
        private float lastContentWidth = -1f;

        private string SavedBlueprintDirectory => Path.Combine(Application.persistentDataPath, "SavedBlueprints");

        private void Awake()
        {
            // 布局只创建一次；数据统计在 Awake 与 OnEnable 分别刷新，适应从其他页面返回后的保存文件变化。
            BuildLayout();
            RefreshInfo();
            RefreshCardGrid();
        }

        private void OnEnable()
        {
            RefreshInfo();
            RefreshCardGrid();
        }

        private void OnRectTransformDimensionsChange()
        {
            RefreshCardGrid();
        }

        private void BuildLayout()
        {
            // 本页所有卡片均运行时创建并挂在自身 ScrollView 下，不改写 Demo 场景其他对象。
            var root = transform as RectTransform;
            if (root == null)
            {
                return;
            }

            var bg = gameObject.GetComponent<Image>();
            if (bg == null)
            {
                bg = gameObject.AddComponent<Image>();
            }

            bg.color = UiThemeTokens.Background;

            var title = CreateText("ProfileTitle", root, "系统信息", 24, FontStyle.Normal, MainUiTheme.Hex("111827"), TextAnchor.MiddleLeft);
            MainUiTheme.ApplyTextRole(title, MainUiTheme.UiTextRole.PageTitle);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(44f, -34f), new Vector2(-88f, 52f));

            var subtitle = CreateText("ProfileSubtitle", root, "当前为单机本地模式，数据保存在本机。", 15, FontStyle.Normal, MainUiTheme.Hex("64748B"), TextAnchor.MiddleLeft);
            MainUiTheme.ApplyTextRole(subtitle, MainUiTheme.UiTextRole.PageSubtitle);
            SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(44f, -82f), new Vector2(-88f, 36f));

            var scrollGo = new GameObject("ProfileScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(root, false);
            var scrollRect = scrollGo.GetComponent<RectTransform>();
            SetRect(scrollRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -84f), new Vector2(-80f, -150f));
            scrollGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.02f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scrollGo.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            contentViewport = viewportRect;
            SetRect(viewportRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            contentRoot = content.GetComponent<RectTransform>();
            contentRoot.anchorMin = new Vector2(0.5f, 1f);
            contentRoot.anchorMax = new Vector2(0.5f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.anchoredPosition = Vector2.zero;
            contentRoot.sizeDelta = new Vector2(1140f, 0f);

            cardGrid = content.GetComponent<GridLayoutGroup>();
            cardGrid.padding = new RectOffset(22, 22, 24, 24);
            cardGrid.spacing = new Vector2(22f, 22f);
            cardGrid.cellSize = new Vector2(537f, 196f);
            cardGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            cardGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
            cardGrid.childAlignment = TextAnchor.UpperCenter;
            cardGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cardGrid.constraintCount = 2;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRoot;
            scroll.horizontal = false;
            scroll.vertical = true;

            AddCard("软件信息", "软件名称：电工数字学生仿真系统\n软件版本：" + VersionText + "\n运行模式：PC 单机版\n网络状态：无需联网");
            drawingInfoText = AddCard("本地图纸", string.Empty, CreateDrawingButtons);
            dataInfoText = AddCard("数据管理", string.Empty, CreateDataButtons);
            AddCard("项目说明", "本系统用于电工电气教学仿真，支持本地模板、自由接线、运行仿真、检查助手、元器件百科和常用工具。当前版本不需要联网，主要功能均可在本机离线使用。");
        }

        private Text AddCard(string title, string body, Action<RectTransform> extraBuilder = null)
        {
            var card = new GameObject(title + "Card", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(Outline));
            card.transform.SetParent(contentRoot, false);
            var image = card.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(12);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var outline = card.GetComponent<Outline>();
            outline.effectColor = MainUiTheme.Hex("E2E8F0");
            outline.effectDistance = new Vector2(1f, -1f);

            var layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var titleText = CreateText("Title", card.transform, title, 18, FontStyle.Normal, MainUiTheme.Hex("111827"), TextAnchor.MiddleLeft);
            MainUiTheme.ApplyTextRole(titleText, MainUiTheme.UiTextRole.InfoCardTitle);
            titleText.rectTransform.sizeDelta = new Vector2(0f, 28f);

            var bodyText = CreateText("Body", card.transform, body, 15, FontStyle.Normal, MainUiTheme.Hex("475569"), TextAnchor.UpperLeft);
            MainUiTheme.ApplyTextRole(bodyText, MainUiTheme.UiTextRole.InfoCardBody);

            var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
            spacer.transform.SetParent(card.transform, false);
            spacer.GetComponent<LayoutElement>().flexibleHeight = 1f;

            extraBuilder?.Invoke(card.GetComponent<RectTransform>());
            return bodyText;
        }

        private void RefreshCardGrid()
        {
            if (contentRoot == null || contentViewport == null || cardGrid == null)
            {
                return;
            }

            var viewportWidth = contentViewport.rect.width;
            if (viewportWidth <= 0f)
            {
                return;
            }

            var contentWidth = Mathf.Min(1140f, Mathf.Max(0f, viewportWidth - 32f));
            if (contentWidth <= 0f || Mathf.Abs(contentWidth - lastContentWidth) < 0.5f)
            {
                return;
            }

            lastContentWidth = contentWidth;
            contentRoot.sizeDelta = new Vector2(contentWidth, contentRoot.sizeDelta.y);
            var horizontalPadding = cardGrid.padding.left + cardGrid.padding.right;
            var cellWidth = (contentWidth - horizontalPadding - cardGrid.spacing.x) * 0.5f;
            cardGrid.cellSize = new Vector2(Mathf.Max(0f, cellWidth), 196f);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        }

        private void CreateDrawingButtons(RectTransform parent)
        {
            var row = CreateButtonRow(parent);
            CreateButton(row, "打开图纸文件夹", OpenSavedBlueprintFolder, true);
            CreateButton(row, "刷新信息", RefreshInfo, false);
        }

        private void CreateDataButtons(RectTransform parent)
        {
            var row = CreateButtonRow(parent);
            CreateButton(row, "打开数据目录", OpenPersistentDataFolder, true);
        }

        private RectTransform CreateButtonRow(RectTransform parent)
        {
            var row = new GameObject("ButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 12;
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 36f;
            return row.GetComponent<RectTransform>();
        }

        private void CreateButton(RectTransform parent, string label, UnityEngine.Events.UnityAction action, bool primary)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
            image.color = primary ? UiThemeTokens.PrimaryBlue : new Color(0.94f, 0.96f, 0.98f);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            var element = go.GetComponent<LayoutElement>();
            element.preferredWidth = 140f;
            element.preferredHeight = 36f;
            var textColor = primary ? Color.white : UiThemeTokens.TextDark;
            var text = CreateText("Text", go.transform, label, 14, FontStyle.Normal, textColor, TextAnchor.MiddleCenter);
            MainUiTheme.ApplyTextRole(text, MainUiTheme.UiTextRole.CardActionButton);
        }

        private void RefreshInfo()
        {
            // 只统计明确的 SavedBlueprints 目录；不扫描模板 Resources 或任意用户磁盘路径。
            if (drawingInfoText != null)
            {
                var count = CountSavedBlueprints();
                if (count == 0)
                {
                    drawingInfoText.text = "当前暂无本地图纸。\n你可以在模拟电路页面点击“保存图纸”创建本地图纸。";
                }
                else
                {
                    drawingInfoText.text = "本地图纸数量：" + count + "\n图纸保存位置：SavedBlueprints";
                }
            }

            if (dataInfoText != null)
            {
                dataInfoText.text = "本地数据目录：ElectricalSimulation2D\n说明：图纸、配置和本地缓存均保存在本机。";
            }
        }

        private int CountSavedBlueprints()
        {
            try
            {
                if (!Directory.Exists(SavedBlueprintDirectory))
                {
                    return 0;
                }

                return Directory.GetFiles(SavedBlueprintDirectory, "*.json").Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private void OpenSavedBlueprintFolder()
        {
            EnsureDirectory(SavedBlueprintDirectory);
            OpenFolder(SavedBlueprintDirectory);
        }

        private void OpenPersistentDataFolder()
        {
            EnsureDirectory(Application.persistentDataPath);
            OpenFolder(Application.persistentDataPath);
        }



        private static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void OpenFolder(string path)
        {
            // 平台相关打开目录入口保持现有保护逻辑；失败不应影响当前页面或保存数据。
            if (string.IsNullOrWhiteSpace(path))
            {
                Debug.LogWarning("目录路径为空，无法打开。");
                return;
            }

            path = Path.GetFullPath(path);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\"");
#else
            Application.OpenURL("file:///" + path.Replace("\\", "/"));
#endif
        }

        private static Text CreateText(string name, Transform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = value;
            text.font = style == FontStyle.Bold ? MainUiTheme.UiFontBold : MainUiTheme.UiFont;
            text.fontSize = size;
            text.fontStyle = FontStyle.Normal;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }
    }
}
