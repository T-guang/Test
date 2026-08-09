using System;
using System.Globalization;
using ElectricalSim.Spice.Core;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// 编辑单个 SPICE 元件数值的可复用展示层。
    /// 对话框负责输入收集、单位/显示转换，以及 AC source 专用字段的前置解析与校验；普通参数仍通过既有 Workspace API 解析并写入模型。
    /// Workspace Controller / Model 负责正式写入、revision 递增与 stale-result 生命周期。
    /// 因此这里的输入框缓存不能替代 SpiceWorkspaceModel 中的稳定参数值，也不能成为仿真读取的第二个来源。
    /// </summary>
    public sealed class SpiceComponentParameterDialog : MonoBehaviour
    {
        private RectTransform popupLayer;
        private SpiceWorkspaceController workspace;
        private Image blocker;
        private Image panel;
        private Text title;
        private Text componentLabel;
        private Text parameterLabel;
        private InputField input;
        private Outline inputOutline;
        private Text phaseLabel;
        private InputField phaseInput;
        private Outline phaseInputOutline;
        private Text frequencyLabel;
        private InputField frequencyInput;
        private Outline frequencyInputOutline;
        private Button unitButton;
        private Text unitLabel;
        private Text errorLabel;
        private Button cancelButton;
        private Button applyButton;
        private Button closeButton;
        private SpiceWorkspaceComponentData currentComponent;
        private string[] units = Array.Empty<string>();
        private int unitIndex;
        private bool initialized;
        private bool applying;
        private bool switchState;
        private bool editingAcVoltageSource;

        private static readonly Vector2 StandardPanelSize = new Vector2(404f, 224f);
        private static readonly Vector2 AcSourcePanelSize = new Vector2(440f, 340f);

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        public void Initialize(RectTransform layer, SpiceWorkspaceController controller)
        {
            if (initialized) throw new InvalidOperationException("SPICE parameter dialog is already initialized.");
            popupLayer = layer ?? throw new ArgumentNullException(nameof(layer));
            workspace = controller ?? throw new ArgumentNullException(nameof(controller));
            BuildUi();
            CloseWithoutApply();
            initialized = true;
        }

        public void Open(SpiceWorkspaceComponentData component)
        {
            if (!initialized || component == null || component.Kind == SpiceComponentKind.Ground)
            {
                return;
            }

            currentComponent = component;
            editingAcVoltageSource = component.Kind == SpiceComponentKind.AcVoltageSource;
            switchState = component.Kind == SpiceComponentKind.IdealSwitch && component.SiValue > 0.5d;
            units = editingAcVoltageSource ? new[] { "V" } : SpiceParameterUnits.UnitsFor(component.Kind);
            if (component.Kind == SpiceComponentKind.IdealSwitch) units = new[] { "状态" };
            if (units.Length == 0) return;

            unitIndex = 0;
            title.text = DialogTitle(component.Kind);
            componentLabel.text = "元件：" + component.InstanceId;
            parameterLabel.text = ParameterName(component.Kind);
            ConfigureAcSourceFields(component);
            unitLabel.text = component.Kind == SpiceComponentKind.IdealSwitch ? (switchState ? "闭合" : "断开") : units[unitIndex];
            input.gameObject.SetActive(component.Kind != SpiceComponentKind.IdealSwitch);
            input.text = component.Kind == SpiceComponentKind.IdealSwitch ? string.Empty : SpiceParameterUnits.FromSi(component.Kind, component.SiValue, units[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
            parameterLabel.text = component.Kind == SpiceComponentKind.IdealSwitch ? "状态（点击右侧切换）" : ParameterName(component.Kind);
            SetError(null);
            blocker.gameObject.SetActive(true);
            panel.gameObject.SetActive(true);
            panel.rectTransform.SetAsLastSibling();
            if (component.Kind != SpiceComponentKind.IdealSwitch)
            {
                input.ActivateInputField();
                input.selectionAnchorPosition = 0;
                input.selectionFocusPosition = input.text.Length;
            }
        }

        public void CloseWithoutApply()
        {
            currentComponent = null;
            applying = false;
            editingAcVoltageSource = false;
            if (panel != null) panel.gameObject.SetActive(false);
            if (blocker != null) blocker.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            CloseWithoutApply();
            // 参数弹窗的遮罩也是运行时创建的 View；统一入口保证 Editor 测试不会退回延迟 Destroy 警告。
            if (blocker != null) SpiceUnityObjectLifetime.Destroy(blocker.gameObject);
            blocker = null;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseWithoutApply();
                return;
            }

            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && string.IsNullOrEmpty(Input.compositionString))
            {
                Apply();
            }
        }

        private void BuildUi()
        {
            blocker = SpiceWorkspaceUi.CreateImage(popupLayer, "SpiceParameterModalBlocker", new Color(0.08f, 0.12f, 0.2f, 0.48f));
            SpiceWorkspaceUi.Stretch(blocker.rectTransform, Vector2.zero, Vector2.zero);
            blocker.raycastTarget = true;

            panel = gameObject.AddComponent<Image>();
            panel.color = Color.white;
            panel.sprite = UiThemeTokens.GetRoundedSprite(8);
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;
            panel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = StandardPanelSize;
            panel.rectTransform.anchoredPosition = Vector2.zero;
            var outline = gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            var shadow = gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.18f);
            shadow.effectDistance = new Vector2(0f, -4f);

            var header = SpiceWorkspaceUi.CreateImage(transform, "Header", MainUiTheme.Hex("F8FBFF"));
            header.sprite = UiThemeTokens.GetRoundedSprite(8);
            header.type = Image.Type.Sliced;
            SpiceWorkspaceUi.Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -48f), Vector2.zero);
            title = SpiceWorkspaceUi.CreateText(header.transform, "Title", string.Empty, 17, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Stretch(title.rectTransform, new Vector2(18f, 0f), new Vector2(-58f, 0f));
            closeButton = SpiceWorkspaceUi.CreateButton(header.transform, "Close", "×", Color.clear, CloseWithoutApply);
            SpiceWorkspaceUi.Anchor(closeButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-42f, -16f), new Vector2(-10f, 16f));
            closeButton.GetComponent<Outline>().enabled = false;
            closeButton.GetComponentInChildren<Text>().fontSize = 20;

            componentLabel = SpiceWorkspaceUi.CreateText(transform, "Component", string.Empty, 14, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(componentLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -82f), new Vector2(-20f, -56f));
            parameterLabel = SpiceWorkspaceUi.CreateText(transform, "Parameter", string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(parameterLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -112f), new Vector2(-20f, -88f));

            input = SpiceWorkspaceUi.CreateInput(transform, "ValueInput");
            SpiceWorkspaceUi.Anchor(input.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -152f), new Vector2(-106f, -120f));
            input.lineType = InputField.LineType.SingleLine;
            inputOutline = input.GetComponent<Outline>();
            unitButton = SpiceWorkspaceUi.CreateButton(transform, "Unit", string.Empty, MainUiTheme.FilterButton, CycleUnit);
            SpiceWorkspaceUi.Anchor(unitButton.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-98f, -152f), new Vector2(-20f, -120f));
            unitLabel = unitButton.GetComponentInChildren<Text>();

            phaseLabel = SpiceWorkspaceUi.CreateText(transform, "PhaseLabel", "相位", 13, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(phaseLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -182f), new Vector2(-20f, -158f));
            phaseInput = SpiceWorkspaceUi.CreateInput(transform, "PhaseInput");
            SpiceWorkspaceUi.Anchor(phaseInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -222f), new Vector2(-106f, -190f));
            phaseInput.lineType = InputField.LineType.SingleLine;
            phaseInputOutline = phaseInput.GetComponent<Outline>();
            var phaseUnit = SpiceWorkspaceUi.CreateText(transform, "PhaseUnit", "°", 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(phaseUnit.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-98f, -222f), new Vector2(-20f, -190f));

            frequencyLabel = SpiceWorkspaceUi.CreateText(transform, "FrequencyLabel", "分析频率", 13, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(frequencyLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -252f), new Vector2(-20f, -228f));
            frequencyInput = SpiceWorkspaceUi.CreateInput(transform, "FrequencyInput");
            SpiceWorkspaceUi.Anchor(frequencyInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -292f), new Vector2(-106f, -260f));
            frequencyInput.lineType = InputField.LineType.SingleLine;
            frequencyInputOutline = frequencyInput.GetComponent<Outline>();
            var frequencyUnit = SpiceWorkspaceUi.CreateText(transform, "FrequencyUnit", "Hz", 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(frequencyUnit.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-98f, -292f), new Vector2(-20f, -260f));

            errorLabel = SpiceWorkspaceUi.CreateText(transform, "Error", string.Empty, 12, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.DangerRed);
            SpiceWorkspaceUi.Anchor(errorLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -178f), new Vector2(-20f, -154f));

            cancelButton = SpiceWorkspaceUi.CreateButton(transform, "Cancel", "取消", Color.white, CloseWithoutApply);
            SpiceWorkspaceUi.Anchor(cancelButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-192f, 16f), new Vector2(-108f, 48f));
            applyButton = SpiceWorkspaceUi.CreateButton(transform, "Apply", "应用", MainUiTheme.PrimaryBlue, Apply, true);
            SpiceWorkspaceUi.Anchor(applyButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-98f, 16f), new Vector2(-20f, 48f));
            ConfigureButtonColors(cancelButton, false);
            ConfigureButtonColors(applyButton, true);
        }

        /// <summary>
        /// 交流源的幅值、相位和单频分析频率属于同一次用户编辑。
        /// 先在弹窗内完整校验三项文本，避免任何一项非法时只提交其中一部分电气状态。
        /// </summary>
        private void ConfigureAcSourceFields(SpiceWorkspaceComponentData component)
        {
            var visible = editingAcVoltageSource;
            panel.rectTransform.sizeDelta = visible ? AcSourcePanelSize : StandardPanelSize;
            phaseLabel.gameObject.SetActive(visible);
            phaseInput.gameObject.SetActive(visible);
            frequencyLabel.gameObject.SetActive(visible);
            frequencyInput.gameObject.SetActive(visible);
            transform.Find("PhaseUnit").gameObject.SetActive(visible);
            transform.Find("FrequencyUnit").gameObject.SetActive(visible);
            unitButton.interactable = !visible;

            if (!visible)
            {
                SpiceWorkspaceUi.Anchor(errorLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(20f, -178f), new Vector2(-20f, -154f));
                return;
            }

            parameterLabel.text = "AC 小信号幅值";
            input.text = component.SiValue.ToString("G6", CultureInfo.InvariantCulture);
            phaseInput.text = component.AcPhaseDegrees.ToString("G6", CultureInfo.InvariantCulture);
            frequencyInput.text = workspace.Model.AcFrequencyHz.ToString("G9", CultureInfo.InvariantCulture);
            SpiceWorkspaceUi.Anchor(errorLabel.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(20f, 16f), new Vector2(-210f, 48f));
        }

        private void CycleUnit()
        {
            if (currentComponent == null || units.Length == 0) return;
            if (editingAcVoltageSource) return;
            if (currentComponent.Kind == SpiceComponentKind.IdealSwitch)
            {
                switchState = !switchState;
                unitLabel.text = switchState ? "闭合" : "断开";
                return;
            }
            unitIndex = (unitIndex + 1) % units.Length;
            unitLabel.text = units[unitIndex];
            input.text = SpiceParameterUnits.FromSi(currentComponent.Kind, currentComponent.SiValue, units[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
            SetError(null);
        }

        private void Apply()
        {
            if (applying || currentComponent == null || units.Length == 0) return;
            applying = true;
            if (currentComponent.Kind == SpiceComponentKind.IdealSwitch)
            {
                if (!workspace.TrySetSwitchState(currentComponent.InstanceId, switchState))
                {
                    applying = false;
                    SetError("开关状态未能更新。");
                    return;
                }
                CloseWithoutApply();
                return;
            }
            if (editingAcVoltageSource)
            {
                if (!TryParseAcSourceFields(out var magnitude, out var phaseDegrees, out var frequencyHz, out var acError))
                {
                    applying = false;
                    SetError(acError);
                    return;
                }

                // Controller 仍是唯一的 Model 写入入口；三项均已验证后才按正式 API 提交。
                if (!workspace.TrySetAcVoltageSourceParameters(currentComponent.InstanceId, magnitude, phaseDegrees) ||
                    !workspace.TrySetAcFrequency(frequencyHz))
                {
                    applying = false;
                    SetError("交流源参数或分析频率未能更新。");
                    return;
                }

                CloseWithoutApply();
                return;
            }
            if (!workspace.TryApplyParameterText(currentComponent.InstanceId, input.text, units[unitIndex], out var error))
            {
                applying = false;
                SetError(error);
                return;
            }

            CloseWithoutApply();
        }

        private void SetError(string value)
        {
            var hasError = !string.IsNullOrWhiteSpace(value);
            errorLabel.text = value ?? string.Empty;
            inputOutline.effectColor = hasError ? MainUiTheme.DangerRed : MainUiTheme.Divider;
            if (phaseInputOutline != null) phaseInputOutline.effectColor = hasError ? MainUiTheme.DangerRed : MainUiTheme.Divider;
            if (frequencyInputOutline != null) frequencyInputOutline.effectColor = hasError ? MainUiTheme.DangerRed : MainUiTheme.Divider;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private bool TryParseAcSourceFields(out double magnitude, out double phaseDegrees, out double frequencyHz, out string error)
        {
            magnitude = 0d;
            phaseDegrees = 0d;
            frequencyHz = 0d;
            error = null;
            if (!double.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out magnitude) || !IsFinite(magnitude) || magnitude <= 0d)
            {
                error = "交流源幅值必须是有限的正数。";
                return false;
            }
            if (!double.TryParse(phaseInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out phaseDegrees) || !IsFinite(phaseDegrees))
            {
                error = "相位必须是有限数值。";
                return false;
            }
            if (!double.TryParse(frequencyInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out frequencyHz) || !SpiceAnalysisLimits.IsValidFrequency(frequencyHz))
            {
                error = "频率必须是 0.001 Hz～10000000 Hz 范围内的有限数值。";
                return false;
            }
            return true;
        }

        internal InputField GetMagnitudeInputForTesting() => input;
        internal InputField GetPhaseInputForTesting() => phaseInput;
        internal InputField GetFrequencyInputForTesting() => frequencyInput;
        internal Button GetApplyButtonForTesting() => applyButton;
        internal bool IsEditingAcVoltageSourceForTesting => editingAcVoltageSource;
        internal string GetTitleForTesting() => title.text;
        internal string GetParameterLabelForTesting() => parameterLabel.text;

        private static void ConfigureButtonColors(Button button, bool primary)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? MainUiTheme.Hex("1D4ED8") : MainUiTheme.Hex("F8FAFC");
            colors.pressedColor = primary ? MainUiTheme.Hex("1E40AF") : MainUiTheme.Hex("EAF2FF");
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
        }

        private static string DialogTitle(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? "编辑直流电压源参数" :
                kind == SpiceComponentKind.DcCurrentSource ? "编辑直流电流源参数" :
                kind == SpiceComponentKind.AcVoltageSource ? "编辑交流电压源参数" :
                kind == SpiceComponentKind.IdealSwitch ? "编辑理想开关状态" :
                kind == SpiceComponentKind.Resistor ? "编辑电阻参数" :
                kind == SpiceComponentKind.Capacitor ? "编辑电容参数" : "编辑电感参数";
        }

        private static string ParameterName(SpiceComponentKind kind)
        {
            return kind == SpiceComponentKind.DcVoltageSource ? "电压" :
                kind == SpiceComponentKind.DcCurrentSource ? "电流" :
                kind == SpiceComponentKind.AcVoltageSource ? "AC 小信号幅值" :
                kind == SpiceComponentKind.IdealSwitch ? "状态" :
                kind == SpiceComponentKind.Resistor ? "阻值" :
                kind == SpiceComponentKind.Capacitor ? "电容量" : "电感量";
        }
    }
}
