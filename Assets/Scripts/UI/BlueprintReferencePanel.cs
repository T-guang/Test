using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ElectricalSim.Templates;

namespace ElectricalSim.UI
{
    public sealed class BlueprintReferencePanel : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        private enum ReferencePanelSizeMode
        {
            Small,
            Medium,
            Large
        }

        [SerializeField] private RectTransform panelRect;
        [SerializeField] private Button zoomInButton;
        [SerializeField] private Button zoomOutButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private Text referenceTitle;
        [SerializeField] private Image referenceImage;
        [SerializeField] private Text referenceRecommendations;

        private RectTransform headerRect;
        private RectTransform imageViewportRect;
        private RectTransform infoScrollViewRect;
        private RectTransform infoViewportRect;
        private RectTransform infoContentRect;
        private ScrollRect infoScrollRect;
        private Button closeButton;
        private Text imageStatusText;
        private Coroutine layoutRefreshCoroutine;
        private Vector2 dragStartPointer;
        private Vector2 dragStartPosition;
        private float currentFitScale = 1f;
        private ReferencePanelSizeMode currentSizeMode = ReferencePanelSizeMode.Medium;

        private void Awake()
        {
            if (panelRect == null)
            {
                panelRect = GetComponent<RectTransform>();
            }

            EnsureContentReferences();
            zoomInButton?.onClick.AddListener(EnlargePanel);
            zoomOutButton?.onClick.AddListener(ShrinkPanel);
            resetButton?.onClick.AddListener(ResetPanelSize);
        }

        /// <summary>
        /// 使用模板目录项刷新练习参考图。图片只从 thumbnailPath 加载，
        /// 不允许使用模板 resourcePath 或上一次预览残留的 Sprite。
        /// </summary>
        public void ShowPracticeReference(CircuitTemplateCatalogItemDto templateItem)
        {
            EnsureContentReferences();
            if (templateItem == null)
            {
                return;
            }

            gameObject.SetActive(true);
            EnsureFixedLayout();
            headerRect?.SetAsLastSibling();

            if (referenceTitle != null)
            {
                referenceTitle.font = MainUiTheme.UiFontBold;
                referenceTitle.fontSize = 20;
                referenceTitle.fontStyle = FontStyle.Normal;
                referenceTitle.color = MainUiTheme.NormalText;
                referenceTitle.resizeTextForBestFit = false;
                referenceTitle.text = "图纸参考：" + templateItem.templateName;
            }

            Sprite sprite = null;
            if (!string.IsNullOrWhiteSpace(templateItem.thumbnailPath))
            {
                sprite = Resources.Load<Sprite>(templateItem.thumbnailPath);
            }

            if (referenceImage != null)
            {
                referenceImage.sprite = sprite;
                referenceImage.preserveAspect = true;
                referenceImage.color = Color.white;
                referenceImage.enabled = sprite != null;
                referenceImage.raycastTarget = false;
            }

            if (sprite == null)
            {
                Debug.LogWarning(
                    "[PracticeReference] Failed to load sprite: " +
                    "templateId=" + templateItem.templateId + ", " +
                    "templateName=" + templateItem.templateName + ", " +
                    "thumbnailPath=" + templateItem.thumbnailPath);
            }

            SetImageStatus(sprite == null, "参考原理图加载失败");
            ApplyRecommendations(templateItem);
            RefreshAfterLayout(true);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (panelRect == null)
            {
                return;
            }

            dragStartPointer = eventData.position;
            dragStartPosition = panelRect.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (panelRect == null)
            {
                return;
            }

            var canvas = panelRect.GetComponentInParent<Canvas>();
            var scale = canvas != null ? canvas.scaleFactor : 1f;
            panelRect.anchoredPosition = dragStartPosition + (eventData.position - dragStartPointer) / Mathf.Max(0.01f, scale);
        }

        private void EnsureFixedLayout()
        {
            if (panelRect == null)
            {
                return;
            }

            EnsureFixedLayoutContainers();
            ApplyPanelSize(currentSizeMode);
        }

        private void EnsureFixedLayoutContainers()
        {
            if (panelRect == null)
            {
                return;
            }

            headerRect = EnsureRectChild("Header", panelRect);
            imageViewportRect = EnsureRectChild("ImageViewport", panelRect);
            infoScrollViewRect = EnsureRectChild("InfoScrollView", panelRect);
        }

        private void ApplyPanelSize(ReferencePanelSizeMode sizeMode)
        {
            if (panelRect == null)
            {
                return;
            }

            currentSizeMode = sizeMode;
            GetSizeProfile(sizeMode, out var panelSize, out var headerHeight, out var infoHeight);
            panelRect.sizeDelta = panelSize;
            ConfigureHeader(headerHeight);
            ConfigureImageViewport(headerHeight, infoHeight);
            ConfigureInfoScrollView(infoHeight);
            ClampPanelToCanvas();
        }

        private static void GetSizeProfile(
            ReferencePanelSizeMode sizeMode,
            out Vector2 panelSize,
            out float headerHeight,
            out float infoHeight)
        {
            switch (sizeMode)
            {
                case ReferencePanelSizeMode.Small:
                    panelSize = new Vector2(420f, 420f);
                    headerHeight = 48f;
                    infoHeight = 126f;
                    break;
                case ReferencePanelSizeMode.Large:
                    panelSize = new Vector2(680f, 700f);
                    headerHeight = 52f;
                    infoHeight = 200f;
                    break;
                default:
                    panelSize = new Vector2(520f, 540f);
                    headerHeight = 50f;
                    infoHeight = 160f;
                    break;
            }
        }

        private void ClampPanelToCanvas()
        {
            if (panelRect == null || !(panelRect.parent is RectTransform parentRect))
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            panelRect.GetWorldCorners(corners);
            var min = parentRect.InverseTransformPoint(corners[0]);
            var max = parentRect.InverseTransformPoint(corners[2]);
            var parentBounds = parentRect.rect;
            var correction = Vector2.zero;

            if (min.x < parentBounds.xMin)
            {
                correction.x += parentBounds.xMin - min.x;
            }
            else if (max.x > parentBounds.xMax)
            {
                correction.x += parentBounds.xMax - max.x;
            }

            if (min.y < parentBounds.yMin)
            {
                correction.y += parentBounds.yMin - min.y;
            }
            else if (max.y > parentBounds.yMax)
            {
                correction.y += parentBounds.yMax - max.y;
            }

            if (correction != Vector2.zero)
            {
                panelRect.anchoredPosition += correction;
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled || referenceImage == null || referenceImage.sprite == null || imageViewportRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            CalculateFitScale();
            FitImageToViewport();
        }

        private void ConfigureHeader(float headerHeight)
        {
            if (headerRect == null)
            {
                return;
            }

            SetStretchRect(headerRect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -headerHeight), new Vector2(-14f, 0f));
            MoveToParent(referenceTitle != null ? referenceTitle.rectTransform : null, headerRect);
            MoveToParent(zoomOutButton != null ? zoomOutButton.transform as RectTransform : null, headerRect);
            MoveToParent(resetButton != null ? resetButton.transform as RectTransform : null, headerRect);
            MoveToParent(zoomInButton != null ? zoomInButton.transform as RectTransform : null, headerRect);
            MoveToParent(closeButton != null ? closeButton.transform as RectTransform : null, headerRect);

            // 标题区严格分离：占用除右侧控制组外的全部宽度。
            // 控制组总宽 = 4 个按钮(32) + 3 个间距(6) + 1 个间距(8 到 X) + X 右边距(10) = 32*4 + 6*3 + 8 + 10 = 158
            // 标题右边缘距 header 右边缘 = 162（含 4px 安全间隔，避免与控制组重叠）
            const float ControlGroupWidth = 162f;
            const float TitleRightOffset = ControlGroupWidth + 4f;

            if (referenceTitle != null)
            {
                SetStretchRect(referenceTitle.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(8f, 0f), new Vector2(-TitleRightOffset, 0f));
                referenceTitle.alignment = TextAnchor.MiddleLeft;
                // 允许长标题换行到第二行，超出两行时截断；避免水平溢出侵入按钮区
                referenceTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
                referenceTitle.verticalOverflow = VerticalWrapMode.Truncate;
            }

            // 控制组从右向左布局：X(32) + 8 间距 + +(32) + 6 间距 + 1:1(40) + 6 间距 + -(32)
            // 距 header 右边缘的偏移（pivot=1,0.5，anchoredPosition.x 为负值）
            const float BtnSize = 32f;
            const float ResetWidth = 40f;
            const float GapSmall = 6f;
            const float GapBeforeClose = 8f;
            const float RightMargin = 10f;

            float closeRight = -RightMargin;
            float zoomInRight = closeRight - BtnSize - GapBeforeClose;
            float resetRight = zoomInRight - BtnSize - GapSmall;
            float zoomOutRight = resetRight - ResetWidth - GapSmall;

            ApplyHeaderButtonStyle(zoomOutButton, zoomOutRight, BtnSize, false);
            ApplyHeaderButtonStyle(resetButton, resetRight, ResetWidth, true);
            ApplyHeaderButtonStyle(zoomInButton, zoomInRight, BtnSize, false);
            ApplyHeaderButtonStyle(closeButton, closeRight, BtnSize, false, true);

            headerRect.SetAsLastSibling();
        }

        private static void ApplyHeaderButtonStyle(Button button, float rightOffset, float width, bool isReset, bool isClose = false)
        {
            if (button == null)
            {
                return;
            }

            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(width, 32f);
            rect.anchoredPosition = new Vector2(rightOffset, 0f);

            // 复用项目现有圆角按钮样式（UiThemeTokens.GetRoundedSprite），与对话框按钮风格一致
            var bg = button.GetComponent<Image>();
            if (bg != null)
            {
                bg.sprite = UiThemeTokens.GetRoundedSprite(6, 64);
                bg.type = Image.Type.Sliced;
                if (isClose)
                {
                    bg.color = UiThemeTokens.TextMuted;
                }
                else if (isReset)
                {
                    bg.color = UiThemeTokens.PrimaryBlue;
                }
                else
                {
                    bg.color = UiThemeTokens.BorderColor;
                }
            }

            // 统一按钮文字样式
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.font = MainUiTheme.UiFont;
                label.fontSize = isReset ? 13 : 16;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = isClose ? Color.white : (isReset ? Color.white : UiThemeTokens.TextDark);
                label.resizeTextForBestFit = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void ConfigureImageViewport(float headerHeight, float infoHeight)
        {
            if (imageViewportRect == null)
            {
                return;
            }

            SetStretchRect(
                imageViewportRect,
                Vector2.zero,
                Vector2.one,
                new Vector2(14f, 26f + infoHeight),
                new Vector2(-14f, -12f - headerHeight));
            EnsureComponent<RectMask2D>(imageViewportRect.gameObject);
            MoveToParent(referenceImage != null ? referenceImage.rectTransform : null, imageViewportRect);

            if (referenceImage != null)
            {
                var imageRect = referenceImage.rectTransform;
                imageRect.anchorMin = new Vector2(0.5f, 0.5f);
                imageRect.anchorMax = new Vector2(0.5f, 0.5f);
                imageRect.pivot = new Vector2(0.5f, 0.5f);
                imageRect.anchoredPosition = Vector2.zero;
            }
        }

        private void ConfigureInfoScrollView(float infoHeight)
        {
            if (infoScrollViewRect == null)
            {
                return;
            }

            SetStretchRect(infoScrollViewRect, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(14f, 14f), new Vector2(-14f, 14f + infoHeight));
            var background = EnsureComponent<Image>(infoScrollViewRect.gameObject);
            background.color = MainUiTheme.Hex("F8FAFC");

            infoViewportRect = EnsureRectChild("Viewport", infoScrollViewRect);
            SetStretchRect(infoViewportRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            EnsureComponent<RectMask2D>(infoViewportRect.gameObject);

            infoContentRect = EnsureRectChild("Content", infoViewportRect);
            infoContentRect.anchorMin = new Vector2(0f, 1f);
            infoContentRect.anchorMax = new Vector2(1f, 1f);
            infoContentRect.pivot = new Vector2(0.5f, 1f);
            infoContentRect.anchoredPosition = Vector2.zero;
            infoContentRect.sizeDelta = new Vector2(0f, 0f);

            var layout = EnsureComponent<VerticalLayoutGroup>(infoContentRect.gameObject);
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 0f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = EnsureComponent<ContentSizeFitter>(infoContentRect.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            MoveToParent(referenceRecommendations != null ? referenceRecommendations.rectTransform : null, infoContentRect);
            if (referenceRecommendations != null)
            {
                referenceRecommendations.name = "PracticeReferenceInfo";
                var layoutElement = EnsureComponent<LayoutElement>(referenceRecommendations.gameObject);
                layoutElement.minWidth = 0f;
                layoutElement.flexibleWidth = 1f;
                layoutElement.minHeight = 0f;
                layoutElement.flexibleHeight = 0f;
                referenceRecommendations.rectTransform.sizeDelta = Vector2.zero;
            }

            infoScrollRect = EnsureComponent<ScrollRect>(infoScrollViewRect.gameObject);
            infoScrollRect.viewport = infoViewportRect;
            infoScrollRect.content = infoContentRect;
            infoScrollRect.horizontal = false;
            infoScrollRect.vertical = true;
            infoScrollRect.movementType = ScrollRect.MovementType.Clamped;
            infoScrollRect.inertia = true;
            infoScrollRect.scrollSensitivity = 20f;
        }

        private void ApplyRecommendations(CircuitTemplateCatalogItemDto templateItem)
        {
            if (referenceRecommendations == null)
            {
                return;
            }

            referenceRecommendations.font = MainUiTheme.DenseUiFont;
            referenceRecommendations.fontSize = 14;
            referenceRecommendations.fontStyle = FontStyle.Normal;
            referenceRecommendations.color = MainUiTheme.Hex("475569");
            referenceRecommendations.alignment = TextAnchor.UpperLeft;
            referenceRecommendations.supportRichText = true;
            referenceRecommendations.horizontalOverflow = HorizontalWrapMode.Wrap;
            referenceRecommendations.verticalOverflow = VerticalWrapMode.Overflow;
            referenceRecommendations.lineSpacing = 1.1f;
            referenceRecommendations.resizeTextForBestFit = false;

            var hasDescription = !string.IsNullOrWhiteSpace(templateItem.description);
            var hasVariantNote = !string.IsNullOrWhiteSpace(templateItem.practiceVariantNote);
            var isBVariant = string.Equals(templateItem.diagramRiskLevel, "B", StringComparison.OrdinalIgnoreCase);
            var builder = new StringBuilder();

            if (isBVariant)
            {
                builder.Append("<color=#B45309><b>提示</b> 原理图与系统练习版可能存在少量画法差异，请以实际端子和练习说明为准。</color>\n\n");
            }

            builder.Append("<b>【练习提示】</b>\n参考原理图选择元件并完成接线。");
            if (!isBVariant)
            {
                builder.Append("\n元件端子与接线规则以系统实际内容为准。");
            }

            if (hasDescription)
            {
                AppendSection(builder, "【电路说明】", templateItem.description);
            }

            if (hasVariantNote)
            {
                AppendSection(builder, "【系统练习版说明】", templateItem.practiceVariantNote);
            }

            referenceRecommendations.text = builder.ToString();
        }

        private void EnlargePanel()
        {
            if (currentSizeMode == ReferencePanelSizeMode.Small)
            {
                SetPanelSizeMode(ReferencePanelSizeMode.Medium);
            }
            else if (currentSizeMode == ReferencePanelSizeMode.Medium)
            {
                SetPanelSizeMode(ReferencePanelSizeMode.Large);
            }
        }

        private void ShrinkPanel()
        {
            if (currentSizeMode == ReferencePanelSizeMode.Large)
            {
                SetPanelSizeMode(ReferencePanelSizeMode.Medium);
            }
            else if (currentSizeMode == ReferencePanelSizeMode.Medium)
            {
                SetPanelSizeMode(ReferencePanelSizeMode.Small);
            }
        }

        private void ResetPanelSize()
        {
            SetPanelSizeMode(ReferencePanelSizeMode.Medium);
        }

        private void SetPanelSizeMode(ReferencePanelSizeMode sizeMode)
        {
            EnsureContentReferences();
            EnsureFixedLayoutContainers();
            ApplyPanelSize(sizeMode);
            RefreshAfterLayout(false);
        }

        private void RefreshAfterLayout(bool resetInfoScrollPosition)
        {
            Canvas.ForceUpdateCanvases();
            RebuildAndFit(resetInfoScrollPosition);

            if (layoutRefreshCoroutine != null)
            {
                StopCoroutine(layoutRefreshCoroutine);
            }

            layoutRefreshCoroutine = StartCoroutine(RefreshAfterLayoutNextFrame(resetInfoScrollPosition));
        }

        private IEnumerator RefreshAfterLayoutNextFrame(bool resetInfoScrollPosition)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            RebuildAndFit(resetInfoScrollPosition);
            layoutRefreshCoroutine = null;
        }

        private void RebuildAndFit(bool resetInfoScrollPosition)
        {
            if (infoContentRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(infoContentRect);
            }

            CalculateFitScale();
            FitImageToViewport();
            if (resetInfoScrollPosition && infoScrollRect != null)
            {
                infoScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void CalculateFitScale()
        {
            if (referenceImage == null || referenceImage.sprite == null || imageViewportRect == null)
            {
                currentFitScale = 1f;
                return;
            }

            var spritePixelsPerUnit = Mathf.Max(0.01f, referenceImage.sprite.pixelsPerUnit);
            var canvas = GetComponentInParent<Canvas>();
            var canvasPixelsPerUnit = canvas != null ? Mathf.Max(0.01f, canvas.referencePixelsPerUnit) : 100f;
            var sourceSize = referenceImage.sprite.rect.size * (canvasPixelsPerUnit / spritePixelsPerUnit);
            var viewportSize = imageViewportRect.rect.size;
            if (sourceSize.x <= 0f || sourceSize.y <= 0f || viewportSize.x <= 0f || viewportSize.y <= 0f)
            {
                currentFitScale = 1f;
                return;
            }

            referenceImage.rectTransform.sizeDelta = sourceSize;
            currentFitScale = Mathf.Min(viewportSize.x / sourceSize.x, viewportSize.y / sourceSize.y);
            currentFitScale = Mathf.Max(0.01f, currentFitScale);
        }

        private void FitImageToViewport()
        {
            if (referenceImage != null)
            {
                referenceImage.rectTransform.localScale = Vector3.one * currentFitScale;
                referenceImage.rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        private void EnsureContentReferences()
        {
            var texts = GetComponentsInChildren<Text>(true);
            var images = GetComponentsInChildren<Image>(true);
            var buttons = GetComponentsInChildren<Button>(true);

            if (referenceTitle == null)
            {
                referenceTitle = FindByName(texts, "ReferenceTitle");
            }

            if (referenceRecommendations == null)
            {
                referenceRecommendations = FindByName(texts, "ReferenceRecommendations") ?? FindByName(texts, "PracticeReferenceInfo");
            }

            if (referenceImage == null)
            {
                referenceImage = FindByName(images, "ReferenceImage");
            }

            if (closeButton == null)
            {
                closeButton = FindByName(buttons, "ReferenceClose");
            }

            if (referenceRecommendations == null)
            {
                referenceRecommendations = CreateReferenceInfoText();
            }

            if (imageStatusText == null && referenceImage != null)
            {
                imageStatusText = referenceImage.transform.Find("PracticeReferenceImageStatus")?.GetComponent<Text>();
            }
        }

        private Text CreateReferenceInfoText()
        {
            if (panelRect == null)
            {
                return null;
            }

            var infoObject = new GameObject("PracticeReferenceInfo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            infoObject.transform.SetParent(panelRect, false);
            var infoText = infoObject.GetComponent<Text>();
            infoText.raycastTarget = false;
            return infoText;
        }

        private void SetImageStatus(bool visible, string message)
        {
            if (imageStatusText == null && referenceImage != null)
            {
                var statusObject = new GameObject("PracticeReferenceImageStatus", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                statusObject.transform.SetParent(referenceImage.transform, false);
                imageStatusText = statusObject.GetComponent<Text>();
                var rect = imageStatusText.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                imageStatusText.font = MainUiTheme.DenseUiFont;
                imageStatusText.fontSize = 15;
                imageStatusText.fontStyle = FontStyle.Normal;
                imageStatusText.color = MainUiTheme.MutedText;
                imageStatusText.alignment = TextAnchor.MiddleCenter;
                imageStatusText.resizeTextForBestFit = false;
                imageStatusText.raycastTarget = false;
            }

            if (imageStatusText != null)
            {
                imageStatusText.text = message;
                imageStatusText.gameObject.SetActive(visible);
            }
        }

        private static void AppendSection(StringBuilder builder, string heading, string content)
        {
            builder.Append("\n\n<b>");
            builder.Append(heading);
            builder.Append("</b>\n");
            builder.Append(content.Replace("|", " / "));
        }

        private static RectTransform EnsureRectChild(string name, Transform parent)
        {
            var existing = parent.Find(name) as RectTransform;
            if (existing != null)
            {
                return existing;
            }

            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }

        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static T FindByName<T>(T[] components, string objectName) where T : Component
        {
            for (var i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null && string.Equals(component.name, objectName, StringComparison.Ordinal))
                {
                    return component;
                }
            }

            return null;
        }

        private static void MoveToParent(RectTransform child, Transform parent)
        {
            if (child != null && child.parent != parent)
            {
                child.SetParent(parent, false);
            }
        }

        private static void SetStretchRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
