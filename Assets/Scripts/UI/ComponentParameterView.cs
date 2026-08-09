using System.Collections.Generic;
using System.Globalization;
using ElectricalSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    // 参数面板是当前 CircuitComponent 可编辑参数的 UI 投影：Definition 提供规格与默认值，实例保存用户修改，
    // runtime-derived 状态和分析结果只能展示而不应经此回写成参数。面板负责输入解析和 min/max 范围检查，
    // CircuitComponent.SetParameterValue 写入实例，Workspace 只提供状态提示和 MarkSimulationDirty。
    // 面板可以被隐藏后复用；currentComponent 仅表示当前编辑目标，不是选中状态或电气身份的替代缓存。
    public sealed class ComponentParameterView : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text componentNameText;
        [SerializeField] private ScrollRect parameterScrollRect;
        [SerializeField] private RectTransform rowsViewport;
        [SerializeField] private RectTransform rowsRoot;
        [SerializeField] private Text emptyText;
        [SerializeField] private Button applyButton;

        private readonly Dictionary<string, InputField> editableInputs = new Dictionary<string, InputField>();
        private CircuitComponent currentComponent;
        private WorkspaceController workspace;

        public static ComponentParameterView Create(RectTransform parent)
        {
            var panelObject = new GameObject("ComponentParameterPanel", typeof(RectTransform), typeof(Image), typeof(ComponentParameterView));
            panelObject.transform.SetParent(parent, false);

            var rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            // X = -100 避开右侧操作按钮，Y = -160 避开顶部文件栏
            rect.anchoredPosition = new Vector2(-100f, -160f);
            rect.sizeDelta = new Vector2(300f, 300f);

            var image = panelObject.GetComponent<Image>();
            image.color = new Color(0.98f, 0.98f, 0.98f, 1f);
            image.raycastTarget = true;
            
            var outline = panelObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);

            var vlg = panelObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 8f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childAlignment = TextAnchor.UpperLeft;

            var view = panelObject.GetComponent<ComponentParameterView>();
            view.BuildDefaultContent(rect);
            view.EnsureDragHandle();
            view.Hide();
            return view;
        }

        public void Show(CircuitComponent component)
        {
            Show(component, workspace);
        }

        public void Show(CircuitComponent component, WorkspaceController owner)
        {
            workspace = owner != null ? owner : workspace;
            currentComponent = component;
            EnsureBuilt();
            EnsureDragHandle();

            if (component == null)
            {
                Hide();
                return;
            }

            gameObject.SetActive(true);
            if (componentNameText != null)
            {
                componentNameText.text = "元件：" + (component.Definition != null ? component.Definition.displayName : "未知元件");
            }
            BuildParameterRows(component);
        }

        public void Hide()
        {
            currentComponent = null;
            gameObject.SetActive(false);
        }

        private void ApplyChanges()
        {
            // 当前实现按字段顺序校验并立即写入；若后续字段非法，前面已通过的字段可能已经更新。
            // 未来若需要事务式提交，必须先完成全量校验再进入写入阶段，不能只在此注释旁假定现有行为具备原子性。
            if (currentComponent == null)
            {
                return;
            }

            var changed = new List<string>();
            var parameters = GetDisplayParameters(currentComponent);
            foreach (var parameter in parameters)
            {
                if (parameter == null || !parameter.editable)
                {
                    continue;
                }

                if (!editableInputs.TryGetValue(parameter.key, out var input))
                {
                    continue;
                }

                if (!TryParseFloat(input.text, out var value))
                {
                    workspace?.SetStatus("参数格式错误，请输入数字。");
                    return;
                }

                if (parameter.max > parameter.min && (value < parameter.min || value > parameter.max))
                {
                    workspace?.SetStatus("参数超出范围：" + GetDisplayName(parameter) + " 允许范围 " + FormatValue(parameter.min) + "~" + FormatValue(parameter.max) + (string.IsNullOrWhiteSpace(parameter.unit) ? "" : " " + parameter.unit));
                    return;
                }

                if (!Mathf.Approximately(parameter.value, value))
                {
                    currentComponent.SetParameterValue(parameter.key, value);
                    changed.Add(GetDisplayName(parameter) + " = " + FormatValue(value) + (string.IsNullOrWhiteSpace(parameter.unit) ? "" : " " + parameter.unit));
                }
            }

            if (changed.Count == 0)
            {
                workspace?.SetStatus("参数未改变。");
                return;
            }

            var componentName = currentComponent.Definition != null ? currentComponent.Definition.displayName : "元件";
            workspace?.SetStatus("已修改参数：" + componentName + " " + string.Join("，", changed));
            workspace?.MarkSimulationDirty("元件参数已修改，点击开始仿真刷新结果。");
            Show(currentComponent, workspace);
        }

        private void BuildParameterRows(CircuitComponent component)
        {
            // 行控件每次均从当前实例参数重新投影。不要复用上一元件的 InputField 值，否则显示文本可能被误提交给不同的 parameter key。
            ClearRows();
            editableInputs.Clear();

            var parameters = GetDisplayParameters(component);
            var hasParameters = parameters != null && parameters.Count > 0;
            
            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(!hasParameters);
            }
            if (applyButton != null)
            {
                applyButton.gameObject.SetActive(hasEditableParameters(parameters));
            }

            if (!hasParameters)
            {
                return;
            }

            foreach (var parameter in parameters)
            {
                if (parameter == null)
                {
                    continue;
                }

                CreateParameterRow(parameter);
            }
            
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rowsRoot);
            LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
            if (parameterScrollRect != null)
            {
                parameterScrollRect.verticalNormalizedPosition = 1f;
            }
        }
        
        private bool hasEditableParameters(IReadOnlyList<ComponentParameter> parameters)
        {
            if (parameters == null) return false;
            foreach (var p in parameters)
            {
                if (p != null && p.editable) return true;
            }
            return false;
        }

        private static List<ComponentParameter> GetDisplayParameters(CircuitComponent component)
        {
            var result = new List<ComponentParameter>();
            if (component == null)
            {
                return result;
            }

            var parameters = component.GetAllParameters();
            if (parameters == null)
            {
                return result;
            }

            var indexByCanonicalKey = new Dictionary<string, int>();
            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.key))
                {
                    continue;
                }

                var canonicalKey = ParameterAliases.GetCanonicalKey(parameter.key);
                if (!indexByCanonicalKey.TryGetValue(canonicalKey, out var existingIndex))
                {
                    indexByCanonicalKey[canonicalKey] = result.Count;
                    result.Add(parameter);
                    continue;
                }

                if (ShouldPreferDisplayParameter(parameter, result[existingIndex], canonicalKey))
                {
                    result[existingIndex] = parameter;
                }
            }

            return result;
        }

        private static bool ShouldPreferDisplayParameter(
            ComponentParameter candidate,
            ComponentParameter current,
            string canonicalKey)
        {
            if (candidate == null || current == null || string.IsNullOrWhiteSpace(canonicalKey))
            {
                return false;
            }

            var candidateIsCanonical = string.Equals(candidate.key, canonicalKey, System.StringComparison.OrdinalIgnoreCase);
            var currentIsCanonical = string.Equals(current.key, canonicalKey, System.StringComparison.OrdinalIgnoreCase);
            return candidateIsCanonical && !currentIsCanonical;
        }

        private void CreateParameterRow(ComponentParameter parameter)
        {
            var row = new GameObject("ParameterRow_" + parameter.key, typeof(RectTransform));
            row.transform.SetParent(rowsRoot, false);
            var le = row.AddComponent<LayoutElement>();
            le.minHeight = 32f;
            le.preferredHeight = 32f;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var label = CreateText("LabelText", row.transform, GetDisplayName(parameter), 14, FontStyle.Normal, TextAnchor.MiddleLeft);
            var labelLE = label.gameObject.AddComponent<LayoutElement>();
            labelLE.minWidth = 100f;
            labelLE.preferredWidth = 100f;

            if (parameter.editable)
            {
                var input = CreateInputField(row.transform, FormatValue(parameter.value));
                var inputLE = input.gameObject.AddComponent<LayoutElement>();
                inputLE.flexibleWidth = 1f;
                inputLE.minWidth = 80f;
                editableInputs[parameter.key] = input;
            }
            else
            {
                var value = CreateText("ValueText", row.transform, FormatValue(parameter.value), 14, FontStyle.Normal, TextAnchor.MiddleLeft);
                var valueLE = value.gameObject.AddComponent<LayoutElement>();
                valueLE.flexibleWidth = 1f;
                valueLE.minWidth = 80f;
            }

            var unitTextStr = string.IsNullOrWhiteSpace(parameter.unit) ? string.Empty : parameter.unit;
            var unit = CreateText("UnitText", row.transform, unitTextStr, 14, FontStyle.Normal, TextAnchor.MiddleLeft);
            var unitLE = unit.gameObject.AddComponent<LayoutElement>();
            unitLE.minWidth = 36f;
            unitLE.preferredWidth = 36f;
        }

        private void EnsureBuilt()
        {
            if (titleText != null && componentNameText != null && rowsRoot != null && emptyText != null && applyButton != null)
            {
                EnsureRowsAreScrollable((RectTransform)transform);
                EnsureDragHandle();
                return;
            }

            BuildDefaultContent((RectTransform)transform);
            EnsureDragHandle();
        }

        private void EnsureDragHandle()
        {
            if (titleText == null)
            {
                return;
            }

            titleText.raycastTarget = true;
            var handle = titleText.GetComponent<ParameterPanelDragHandle>();
            if (handle == null)
            {
                handle = titleText.gameObject.AddComponent<ParameterPanelDragHandle>();
            }

            handle.Initialize((RectTransform)transform);
        }

        private void BuildDefaultContent(RectTransform parent)
        {
            if (titleText == null)
            {
                titleText = CreateText("HeaderText", parent, "元件参数", 16, FontStyle.Bold, TextAnchor.MiddleLeft);
                var le = titleText.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 24f;
                le.preferredHeight = 24f;
            }

            if (componentNameText == null)
            {
                componentNameText = CreateText("ComponentNameText", parent, "元件：未选择", 14, FontStyle.Normal, TextAnchor.MiddleLeft);
                componentNameText.color = new Color(0.2f, 0.2f, 0.2f);
                var le = componentNameText.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 24f;
                le.preferredHeight = 24f;
            }

            EnsureRowsAreScrollable(parent);

            if (rowsRoot == null)
            {
                rowsRoot = CreateRowsContent(rowsViewport != null ? rowsViewport : parent);
            }

            if (emptyText == null)
            {
                emptyText = CreateText("EmptyText", rowsRoot, "暂无参数", 14, FontStyle.Normal, TextAnchor.MiddleCenter);
                emptyText.color = new Color(0.5f, 0.5f, 0.5f);
                var le = emptyText.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 32f;
                le.preferredHeight = 32f;
            }

            if (applyButton == null)
            {
                applyButton = CreateButton(parent, "应用修改");
                var le = applyButton.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 36f;
                le.preferredHeight = 36f;
                applyButton.onClick.RemoveListener(ApplyChanges);
                applyButton.onClick.AddListener(ApplyChanges);
            }
        }

        private void EnsureRowsAreScrollable(RectTransform parent)
        {
            if (parameterScrollRect != null && rowsViewport != null)
            {
                return;
            }

            var scrollObject = new GameObject("ParameterScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            var scrollRectTransform = scrollObject.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = Vector2.zero;
            scrollRectTransform.anchorMax = Vector2.one;
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            var scrollImage = scrollObject.GetComponent<Image>();
            scrollImage.color = new Color(1f, 1f, 1f, 0.02f);
            scrollImage.raycastTarget = true;

            var scrollLayout = scrollObject.AddComponent<LayoutElement>();
            scrollLayout.minHeight = 110f;
            scrollLayout.preferredHeight = 150f;
            scrollLayout.flexibleHeight = 1f;

            rowsViewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask)).GetComponent<RectTransform>();
            rowsViewport.SetParent(scrollObject.transform, false);
            rowsViewport.anchorMin = Vector2.zero;
            rowsViewport.anchorMax = Vector2.one;
            rowsViewport.offsetMin = Vector2.zero;
            rowsViewport.offsetMax = Vector2.zero;

            var viewportImage = rowsViewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;
            var mask = rowsViewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            parameterScrollRect = scrollObject.GetComponent<ScrollRect>();
            parameterScrollRect.viewport = rowsViewport;
            parameterScrollRect.horizontal = false;
            parameterScrollRect.vertical = true;
            parameterScrollRect.movementType = ScrollRect.MovementType.Clamped;
            parameterScrollRect.scrollSensitivity = 24f;

            if (rowsRoot != null)
            {
                rowsRoot.SetParent(rowsViewport, false);
                ConfigureRowsContent(rowsRoot);
            }

            parameterScrollRect.content = rowsRoot;
        }

        private RectTransform CreateRowsContent(RectTransform parent)
        {
            var content = new GameObject("ParameterRowsRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(parent, false);
            ConfigureRowsContent(content);
            if (parameterScrollRect != null)
            {
                parameterScrollRect.content = content;
            }
            return content;
        }

        private static void ConfigureRowsContent(RectTransform content)
        {
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            var rowsVlg = content.GetComponent<VerticalLayoutGroup>();
            if (rowsVlg == null)
            {
                rowsVlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            }
            rowsVlg.spacing = 8f;
            rowsVlg.childForceExpandWidth = true;
            rowsVlg.childForceExpandHeight = false;
            rowsVlg.childControlWidth = true;
            rowsVlg.childControlHeight = true;
            rowsVlg.childAlignment = TextAnchor.UpperLeft;

            var fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void ClearRows()
        {
            if (rowsRoot == null)
            {
                return;
            }

            for (var i = rowsRoot.childCount - 1; i >= 0; i--)
            {
                var child = rowsRoot.GetChild(i);
                if (emptyText != null && child == emptyText.transform)
                {
                    continue;
                }
                Destroy(child.gameObject);
            }
        }

        private static Text CreateText(string name, Transform parent, string text, int fontSize, FontStyle style, TextAnchor alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = new Color(0.08f, 0.1f, 0.16f);
            label.raycastTarget = false;
            return label;
        }

        private static InputField CreateInputField(Transform parent, string value)
        {
            var go = new GameObject("InputField", typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.95f, 0.97f, 1f, 1f);

            var text = CreateText("Text", go.transform, value, 14, FontStyle.Normal, TextAnchor.MiddleLeft);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = new Vector2(6f, 0f);
            text.rectTransform.offsetMax = new Vector2(-6f, 0f);

            var input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.text = value;
            input.contentType = InputField.ContentType.DecimalNumber;
            input.targetGraphic = image;
            return input;
        }

        private static Button CreateButton(Transform parent, string text)
        {
            var go = new GameObject("ApplyButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.42f, 0.95f, 1f);

            var label = CreateText("Text", go.transform, text, 15, FontStyle.Bold, TextAnchor.MiddleCenter);
            label.color = Color.white;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = image;
            return btn;
        }

        private static string GetDisplayName(ComponentParameter parameter)
        {
            return string.IsNullOrWhiteSpace(parameter.displayName) ? parameter.key : parameter.displayName;
        }

        private static string FormatValue(float value)
        {
            return Mathf.Approximately(value, Mathf.Round(value)) ? Mathf.RoundToInt(value).ToString() : value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
