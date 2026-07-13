using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 表示工作区中的单个元件实例，而不是元件定义资产。
    /// 它持有实例参数、端子、当前开合/得电/测量状态和视觉引用；Definition 提供静态规格，
    /// WorkspaceController 负责实例生命周期。端子会被导线直接引用，不能在已有接线期间随意重建。
    /// 修改后需回归元件拖动、参数保存加载、视觉 Prefab、接线与运行态模板。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class CircuitComponent : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        // KM 视觉试点。关闭后恢复默认矩形外观。
        private const bool useExperimentalKmVisualPrefab = true;
        private const bool showExperimentalKmTerminalDebugMarkers = false;
        private const string experimentalKmVisualDefinitionName = "Contactor_KM_380V";
        private const string experimentalKmVisualAssetPath = "Assets/Prefab/Contactor_KM_380V_Visual.prefab";
        private const string experimentalKmDefaultSpritePath = "Assets/Art/Components/Contactor_KM_380V_Default.png";
        private const string experimentalKmEnergizedSpritePath = "Assets/Art/Components/Contactor_KM_380V_Energized.png";
        // 按钮视觉试点。关闭后恢复默认矩形外观。
        private const bool useExperimentalButtonVisualPrefab = true;
        private const bool showExperimentalButtonTerminalDebugMarkers = false;
        private const string experimentalStartButtonDefinitionName = "Button_Start_NO";
        private const string experimentalStopButtonDefinitionName = "Button_Stop_NC";
        private const string experimentalStartButtonVisualAssetPath = "Assets/Prefab/Button_Start_NO_Visual.prefab";
        private const string experimentalStopButtonVisualAssetPath = "Assets/Prefab/Button_Stop_NC_Visual.prefab";
        private const string experimentalStartButtonDefaultSpritePath = "Assets/Art/Components/Button_Start_NO_Default.png";
        private const string experimentalStartButtonPressedSpritePath = "Assets/Art/Components/Button_Start_NO_Pressed.png";
        private const string experimentalStopButtonDefaultSpritePath = "Assets/Art/Components/Button_Stop_NC_Default.png";
        private const string experimentalStopButtonPressedSpritePath = "Assets/Art/Components/Button_Stop_NC_Pressed.png";
        // 复合按钮视觉试点。关闭后恢复默认矩形外观。
        private const bool useExperimentalCompoundButtonVisualPrefab = true;
        private const bool showExperimentalCompoundButtonTerminalDebugMarkers = false;
        private const string experimentalCompoundRedButtonDefinitionName = "Button_Compound_SB";
        private const string experimentalCompoundGreenButtonDefinitionName = "Button_Compound_Green_SB";
        private const string experimentalCompoundRedButtonVisualAssetPath = "Assets/Prefab/Button_Compound_SB_Visual.prefab";
        private const string experimentalCompoundGreenButtonVisualAssetPath = "Assets/Prefab/Button_Compound_Green_SB_Visual.prefab";
        private const string experimentalCompoundRedButtonDefaultSpritePath = "Assets/Art/Components/Button_Compound_SB_Default.png";
        private const string experimentalCompoundRedButtonPressedSpritePath = "Assets/Art/Components/Button_Compound_SB_Pressed.png";
        private const string experimentalCompoundGreenButtonDefaultSpritePath = "Assets/Art/Components/Button_Compound_Green_SB_Default.png";
        private const string experimentalCompoundGreenButtonPressedSpritePath = "Assets/Art/Components/Button_Compound_Green_SB_Pressed.png";
        // 自锁按钮视觉试点。关闭后恢复默认矩形外观。
        private const bool useExperimentalSelfLockButtonVisualPrefab = true;
        private const bool showExperimentalSelfLockButtonTerminalDebugMarkers = false;
        private const string experimentalSelfLockRedButtonDefinitionName = "Button_SelfLock_SB";
        private const string experimentalSelfLockGreenButtonDefinitionName = "Button_SelfLock_Green_SB";
        private const string experimentalSelfLockRedButtonVisualAssetPath = "Assets/Prefab/Button_SelfLock_SB_Visual.prefab";
        private const string experimentalSelfLockGreenButtonVisualAssetPath = "Assets/Prefab/Button_SelfLock_Green_SB_Visual.prefab";
        private const string experimentalSelfLockRedButtonDefaultSpritePath = "Assets/Art/Components/Button_SelfLock_SB_Default.png";
        private const string experimentalSelfLockRedButtonPressedSpritePath = "Assets/Art/Components/Button_SelfLock_SB_Locked.png";
        private const string experimentalSelfLockGreenButtonDefaultSpritePath = "Assets/Art/Components/Button_SelfLock_Green_SB_Default.png";
        private const string experimentalSelfLockGreenButtonPressedSpritePath = "Assets/Art/Components/Button_SelfLock_Green_SB_Locked.png";
        // 三相电源视觉试点。关闭后恢复默认外观。
        private const bool useExperimentalThreePhasePowerVisualPrefab = true;
        private const bool showExperimentalThreePhasePowerTerminalDebugMarkers = false;
        private const string experimentalThreePhasePowerDefinitionName = "AC_ThreePhase_Power";
        private const string experimentalThreePhasePowerVisualAssetPath = "Assets/Prefab/AC_ThreePhase_Power_Visual.prefab";
        private const string experimentalThreePhasePowerSpritePath = "Assets/Art/Components/AC_ThreePhase_Power_Visual.png";
        // 熔断器视觉试点。关闭后恢复默认外观。
        private const bool useExperimentalFuseVisualPrefab = true;
        private const bool showExperimentalFuseTerminalDebugMarkers = false;
        private const string experimentalFuse1PDefinitionName = "Fuse_1P";
        private const string experimentalFuse3PDefinitionName = "Fuse_3P";
        private const string experimentalFuse1PVisualAssetPath = "Assets/Prefab/Fuse_1P_Visual.prefab";
        private const string experimentalFuse3PVisualAssetPath = "Assets/Prefab/Fuse_3P_Visual.prefab";

        [SerializeField] private Image body;
        [SerializeField] private Text title;
        [SerializeField] private Text stateLabel;
        [SerializeField] private ComponentParameterSet parameterSet = new ComponentParameterSet();

        public string InstanceId { get; private set; }
        public ComponentDefinition Definition { get; private set; }
        public bool IsClosed { get; private set; }
        public bool IsEnergized { get; private set; }
        public float MeasuredVoltage { get; private set; }
        public float MeasuredCurrent { get; private set; }
        public float MeasuredPower { get; private set; }
        public IReadOnlyList<TerminalView> Terminals => terminals;

        private readonly List<TerminalView> terminals = new List<TerminalView>();
        private WorkspaceController workspace;
        private RectTransform rectTransform;
        private Vector2 dragOffset;
        private bool selected;
        private RectTransform experimentalKmVisualRoot;
        private Image experimentalKmBodyImage;
        private Sprite experimentalKmDefaultSprite;
        private Sprite experimentalKmEnergizedSprite;
        private readonly Dictionary<string, RectTransform> experimentalKmTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private RectTransform experimentalButtonVisualRoot;
        private Image experimentalButtonBodyImage;
        private Sprite experimentalButtonDefaultSprite;
        private Sprite experimentalButtonPressedSprite;
        private readonly Dictionary<string, RectTransform> experimentalButtonTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private RectTransform experimentalCompoundButtonVisualRoot;
        private Image experimentalCompoundButtonBodyImage;
        private Sprite experimentalCompoundButtonDefaultSprite;
        private Sprite experimentalCompoundButtonPressedSprite;
        private readonly Dictionary<string, RectTransform> experimentalCompoundButtonTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private RectTransform experimentalSelfLockButtonVisualRoot;
        private Image experimentalSelfLockButtonBodyImage;
        private Sprite experimentalSelfLockButtonDefaultSprite;
        private Sprite experimentalSelfLockButtonPressedSprite;
        private readonly Dictionary<string, RectTransform> experimentalSelfLockButtonTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private RectTransform experimentalThreePhasePowerVisualRoot;
        private Image experimentalThreePhasePowerBodyImage;
        private Sprite experimentalThreePhasePowerSprite;
        private readonly Dictionary<string, RectTransform> experimentalThreePhasePowerTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private RectTransform experimentalFuse1PVisualRoot;
        private Image experimentalFuse1PBodyImage;
        private readonly Dictionary<string, RectTransform> experimentalFuse1PTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private RectTransform experimentalFuse3PVisualRoot;
        private Image experimentalFuse3PBodyImage;
        private readonly Dictionary<string, RectTransform> experimentalFuse3PTerminalAnchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
        private VisualPrefabInstance configuredVisualPrefab;
        private KTTimerVisualController ktTimerVisualController;

        public void Initialize(ComponentDefinition definition, WorkspaceController owner, string instanceId = null)
        {
            // 初始化顺序不可随意调整：先复制定义参数，再挂接视觉与端子，
            // 保证视觉锚点、参数面板和后续导线都绑定同一实例身份。
            InstanceId = string.IsNullOrWhiteSpace(instanceId) ? System.Guid.NewGuid().ToString("N") : instanceId;
            Definition = definition;
            workspace = owner;
            rectTransform = GetComponent<RectTransform>();
            IsClosed = definition.startsClosed;
            parameterSet.SetParameters(definition.parameters);
            EnsureInstanceParametersFromDefinition();

            if (body != null)
            {
                body.color = definition.bodyColor;
            }

            if (title != null)
            {
                title.text = ResolveInstanceDisplayName(definition.displayName, InstanceId);
            }

            TryApplyExperimentalKmVisualPrefab();
            TryApplyConfiguredVisualPrefab();
            BuildTerminals();
            RefreshVisual();
        }

        private static string ResolveInstanceDisplayName(string defaultName, string instanceId)
        {
            if (string.Equals(instanceId, "km_main", System.StringComparison.OrdinalIgnoreCase))
            {
                return "主接触器 KM";
            }

            if (string.Equals(instanceId, "km_star", System.StringComparison.OrdinalIgnoreCase))
            {
                return "星形接触器 KMY";
            }

            if (string.Equals(instanceId, "km_delta", System.StringComparison.OrdinalIgnoreCase))
            {
                return "三角形接触器 KMD";
            }

            return defaultName;
        }

        public TerminalView GetTerminal(string terminalId)
        {
            return terminals.Find(t => t.TerminalId == terminalId);
        }

        public void SetParameters(IEnumerable<ComponentParameter> parameters)
        {
            if (!HasAnyParameter(parameters) &&
                parameterSet.parameters != null &&
                parameterSet.parameters.Count > 0)
            {
                EnsureInstanceParametersFromDefinition();
                return;
            }

            parameterSet.SetParameters(parameters);
            EnsureInstanceParametersFromDefinition();
        }

        public void EnsureInstanceParametersFromDefinition()
        {
            // 仅补齐缺失键，绝不覆盖已经保存或由参数面板修改过的实例参数。
            if (Definition == null || Definition.parameters == null || Definition.parameters.Count == 0)
            {
                return;
            }

            if (parameterSet.parameters == null)
            {
                parameterSet.parameters = new List<ComponentParameter>();
            }

            var existingKeys = new HashSet<string>();
            var existingCanonicalKeys = new HashSet<string>();
            for (var i = 0; i < parameterSet.parameters.Count; i++)
            {
                var parameter = parameterSet.parameters[i];
                if (parameter != null && !string.IsNullOrWhiteSpace(parameter.key))
                {
                    existingKeys.Add(parameter.key);
                    existingCanonicalKeys.Add(ParameterAliases.GetCanonicalKey(parameter.key));
                }
            }

            for (var i = 0; i < Definition.parameters.Count; i++)
            {
                var definitionParameter = Definition.parameters[i];
                var canonicalKey = definitionParameter != null
                    ? ParameterAliases.GetCanonicalKey(definitionParameter.key)
                    : null;
                if (definitionParameter == null ||
                    string.IsNullOrWhiteSpace(definitionParameter.key) ||
                    existingKeys.Contains(definitionParameter.key) ||
                    existingCanonicalKeys.Contains(canonicalKey))
                {
                    continue;
                }

                var clone = definitionParameter.Clone();
                clone.ClampValue();
                parameterSet.parameters.Add(clone);
                existingKeys.Add(clone.key);
                existingCanonicalKeys.Add(ParameterAliases.GetCanonicalKey(clone.key));
            }
        }

        private static bool HasAnyParameter(IEnumerable<ComponentParameter> parameters)
        {
            if (parameters == null)
            {
                return false;
            }

            foreach (var parameter in parameters)
            {
                if (parameter != null && !string.IsNullOrWhiteSpace(parameter.key))
                {
                    return true;
                }
            }

            return false;
        }

        public ComponentParameter GetParameter(string key)
        {
            return parameterSet.GetParameter(key);
        }

        public bool SetParameterValue(string key, float value)
        {
            return parameterSet.SetParameterValue(key, value);
        }

        public IReadOnlyList<ComponentParameter> GetAllParameters()
        {
            return parameterSet.parameters;
        }

        public List<ComponentParameter> CloneParameters()
        {
            return parameterSet.CloneList();
        }

        public void SetEnergized(bool energized)
        {
            IsEnergized = energized;
            RefreshVisual();
        }

        public void SetMeasurement(float voltage, float current, float power)
        {
            MeasuredVoltage = voltage;
            MeasuredCurrent = current;
            MeasuredPower = power;
        }

        public void ClearMeasurement()
        {
            SetMeasurement(0f, 0f, 0f);
        }

        public void SetClosed(bool closed)
        {
            IsClosed = closed;
            RefreshVisual();
        }

        public void SetSelected(bool isSelected)
        {
            selected = isSelected;
            RefreshVisual();
        }

        public void Toggle()
        {
            if (workspace != null && workspace.IsInteractionLocked)
            {
                workspace.SetStatus("画布已锁定，解锁后再切换元件状态。");
                return;
            }

            if (!Definition.togglable)
            {
                return;
            }

            workspace?.RecordHistoryCheckpoint();
            IsClosed = !IsClosed;
            RefreshVisual();
            var statusMessage = IsOnDelayTimerRelay()
                ? "时间继电器兼容状态已切换：该字段仅用于旧图纸兼容；当前延时触点以 KT 运行态计时为准。"
                : "开关状态已改变，点击开始仿真刷新结果。";
            workspace?.MarkSimulationDirty(statusMessage);
        }

        private bool IsHittingOperationArea(PointerEventData eventData)
        {
            if (configuredVisualPrefab != null && configuredVisualPrefab.Config.HasOperationHitArea)
            {
                var hitObj = eventData.pointerCurrentRaycast.gameObject;
                if (hitObj == null)
                {
                    hitObj = eventData.pointerPressRaycast.gameObject;
                }
                return hitObj != null && hitObj.name == "OperationHitArea";
            }
            return true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (workspace != null && workspace.IsInteractionLocked)
            {
                workspace.SetStatus("画布已锁定，当前不能选择元件。");
                return;
            }

            if (eventData.clickCount >= 2 && !IsMomentaryPushButton() && IsHittingOperationArea(eventData))
            {
                Toggle();
            }
            else
            {
                workspace?.SelectComponent(this);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (workspace != null && workspace.IsInteractionLocked) return;
            if (IsMomentaryPushButton() && IsHittingOperationArea(eventData))
            {
                SetMomentaryPressed(true);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (workspace != null && workspace.IsInteractionLocked) return;
            if (IsMomentaryPushButton())
            {
                SetMomentaryPressed(false);
            }
        }

        private void SetMomentaryPressed(bool pressed)
        {
            if (Definition == null)
            {
                return;
            }

            var targetClosed = pressed ? !Definition.startsClosed : Definition.startsClosed;
            if (IsClosed == targetClosed)
            {
                return;
            }

            IsClosed = targetClosed;
            RefreshVisual();
            workspace?.MarkSimulationDirty(pressed ? "瞬时按钮已按下，点击开始仿真刷新结果。" : "瞬时按钮已释放，点击开始仿真刷新结果。");
        }

        private void OnDisable()
        {
            if (Definition == null || !IsMomentaryPushButton() || IsClosed == Definition.startsClosed)
            {
                return;
            }

            IsClosed = Definition.startsClosed;
            RefreshVisual();
        }

        private bool IsStartPushButton()
        {
            return Definition != null && Definition.name.IndexOf("Button_Start", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsStopPushButton()
        {
            return Definition != null && Definition.name.IndexOf("Button_Stop", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsCompoundPushButton()
        {
            return Definition != null && Definition.name.IndexOf("Button_Compound", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsSelfLockingButton()
        {
            return Definition != null && Definition.name.IndexOf("Button_SelfLock", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsMomentaryPushButton()
        {
            if (IsSelfLockingButton())
            {
                return false;
            }

            return IsStartPushButton() || IsStopPushButton() || IsCompoundPushButton();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (workspace != null && workspace.IsInteractionLocked)
            {
                workspace.SetStatus("画布已锁定，解锁后再移动元件。");
                return;
            }

            if (IsMomentaryPushButton())
            {
                SetMomentaryPressed(false);
            }

            workspace?.RecordHistoryCheckpoint();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out dragOffset);
            workspace?.SelectComponent(this);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (workspace == null || workspace.IsInteractionLocked)
            {
                return;
            }

            if (workspace.TryScreenToCanvasLocal(eventData.position, eventData.pressEventCamera, out var localPoint))
            {
                rectTransform.anchoredPosition = workspace.Snap(localPoint - dragOffset);
                workspace.RefreshWiresFor(this);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            workspace?.MarkSimulationDirty("元件位置已调整，点击开始仿真刷新结果。");
        }

        private void BuildTerminals()
        {
            // 端子定义变化或初始化时才调用。此方法会销毁旧端子，因此在已有 WireView 引用时重建
            // 会使活动接线失效；正常运行期间应只刷新视觉，不应调用这里。
            foreach (var terminal in terminals)
            {
                if (terminal != null)
                {
                    Destroy(terminal.gameObject);
                }
            }

            terminals.Clear();

            foreach (var terminalDefinition in Definition.terminals)
            {
                var terminalObject = new GameObject("Terminal_" + terminalDefinition.id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TerminalView));
                terminalObject.transform.SetParent(transform, false);

                var terminalRect = terminalObject.GetComponent<RectTransform>();
                var terminalAnchor = terminalDefinition.normalizedPosition;
                var terminalOffset = Vector2.zero;
                var experimentalPosition = Vector2.zero;
                var showExperimentalDebugMarker = false;
                var usesExperimentalAnchor = TryGetExperimentalVisualTerminalPosition(
                    terminalDefinition.id,
                    out experimentalPosition,
                    out showExperimentalDebugMarker);
                if (usesExperimentalAnchor)
                {
                    terminalAnchor = new Vector2(0.5f, 0.5f);
                    terminalOffset = experimentalPosition;
                }

                terminalRect.anchorMin = terminalAnchor;
                terminalRect.anchorMax = terminalAnchor;
                terminalRect.pivot = new Vector2(0.5f, 0.5f);
                terminalRect.anchoredPosition = terminalOffset;
                terminalRect.localRotation = Quaternion.identity;
                terminalRect.localScale = Vector3.one;
                terminalRect.sizeDelta = usesExperimentalAnchor ? new Vector2(30f, 30f) : new Vector2(18f, 18f);

                var terminalImage = terminalObject.GetComponent<Image>();
                terminalImage.color = usesExperimentalAnchor
                    ? new Color(terminalDefinition.color.r, terminalDefinition.color.g, terminalDefinition.color.b, 0.16f)
                    : terminalDefinition.color;
                var terminalButton = terminalObject.GetComponent<Button>();
                if (terminalButton != null && usesExperimentalAnchor)
                {
                    terminalButton.transition = Selectable.Transition.None;
                    terminalButton.targetGraphic = terminalImage;
                }

                var terminal = terminalObject.GetComponent<TerminalView>();
                terminal.Initialize(this, terminalDefinition, workspace);
                terminal.SetSubtleVisualMode(usesExperimentalAnchor, showExperimentalDebugMarker);
                terminals.Add(terminal);

                if (Definition.sprite != null && !usesExperimentalAnchor)
                {
                    var labelObject = new GameObject("Label_" + terminalDefinition.id, typeof(RectTransform), typeof(Text));
                    labelObject.transform.SetParent(transform, false);
                    var labelRect = labelObject.GetComponent<RectTransform>();
                    labelRect.sizeDelta = new Vector2(44f, 18f);
                    labelRect.anchorMin = terminalAnchor;
                    labelRect.anchorMax = terminalAnchor;
                    labelRect.anchoredPosition = terminalOffset + GetTerminalLabelOffset(terminalDefinition.normalizedPosition, terminalOffset, usesExperimentalAnchor);

                    var label = labelObject.GetComponent<Text>();
                    label.text = terminalDefinition.label;
                    label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    label.fontSize = 15;
                    label.fontStyle = FontStyle.Bold;
                    label.alignment = TextAnchor.MiddleCenter;
                    label.color = new Color(0.04f, 0.06f, 0.1f);
                    label.raycastTarget = false;
                }
            }
        }

        private static Vector2 GetTerminalLabelOffset(Vector2 normalizedPosition)
        {
            return GetTerminalLabelOffset(normalizedPosition, Vector2.zero, false);
        }

        private static Vector2 GetTerminalLabelOffset(Vector2 normalizedPosition, Vector2 anchoredPosition, bool usesExperimentalAnchor)
        {
            if (usesExperimentalAnchor)
            {
                return anchoredPosition.y > 0f ? new Vector2(0f, -24f) : new Vector2(0f, 24f);
            }

            if (normalizedPosition.y > 0.75f)
            {
                return new Vector2(0f, -24f);
            }

            if (normalizedPosition.y < 0.25f)
            {
                return new Vector2(0f, 24f);
            }

            return new Vector2(0f, 28f);
        }

        private void TryApplyExperimentalKmVisualPrefab()
        {
            experimentalKmVisualRoot = null;
            experimentalKmBodyImage = null;
            experimentalKmDefaultSprite = null;
            experimentalKmEnergizedSprite = null;
            experimentalKmTerminalAnchors.Clear();

            if (!useExperimentalKmVisualPrefab ||
                Definition == null ||
                !(string.Equals(Definition.name, experimentalKmVisualDefinitionName, System.StringComparison.Ordinal) || string.Equals(Definition.name, "Contactor_KM_220V", System.StringComparison.Ordinal)))
            {
                return;
            }

#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(experimentalKmVisualAssetPath);
            if (prefab == null)
            {
                return;
            }

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalKmVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalKmVisualRoot != null)
            {
                experimentalKmVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalKmVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalKmVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalKmVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalKmVisualRoot.sizeDelta.x > 0f &&
                    experimentalKmVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalKmVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalKmTerminalAnchors(visualObject.transform);
            ConfigureExperimentalKmBodyImage(visualObject.transform);

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void ConfigureExperimentalKmBodyImage(Transform visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var bodyTransform = visualRoot.Find("Body");
            experimentalKmBodyImage = bodyTransform != null ? bodyTransform.GetComponent<Image>() : null;
            if (experimentalKmBodyImage != null)
            {
                experimentalKmBodyImage.raycastTarget = false;
            }

#if UNITY_EDITOR
            experimentalKmDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalKmDefaultSpritePath);
            experimentalKmEnergizedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalKmEnergizedSpritePath);
#endif
            UpdateExperimentalKmBodySprite();
        }

        private void UpdateExperimentalKmBodySprite()
        {
            if (experimentalKmBodyImage == null)
            {
                return;
            }

            var targetSprite = IsEnergized && experimentalKmEnergizedSprite != null
                ? experimentalKmEnergizedSprite
                : experimentalKmDefaultSprite;

            if (targetSprite != null && experimentalKmBodyImage.sprite != targetSprite)
            {
                experimentalKmBodyImage.sprite = targetSprite;
            }
        }

        private void RegisterExperimentalKmTerminalAnchors(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null ||
                    string.IsNullOrEmpty(candidate.name) ||
                    !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalKmTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalKmTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalKmVisualActive() ||
                rectTransform == null ||
                string.IsNullOrWhiteSpace(terminalId) ||
                !IsExperimentalKmTerminal(terminalId))
            {
                return false;
            }

            if (TryGetExperimentalKmCoordinateTablePosition(terminalId, out localPosition))
            {
                return true;
            }

            return false;
        }

        private bool TryGetAnchoredPositionRelativeToExperimentalRoot(RectTransform anchor, out Vector2 localPosition)
        {
            return TryGetAnchoredPositionRelativeToExperimentalRoot(anchor, experimentalKmVisualRoot, out localPosition);
        }

        private static bool TryGetAnchoredPositionRelativeToExperimentalRoot(
            RectTransform anchor,
            RectTransform visualRoot,
            out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            var current = anchor;
            while (current != null && current != visualRoot)
            {
                localPosition += current.anchoredPosition;
                current = current.parent as RectTransform;
            }

            return current == visualRoot;
        }

        private static bool TryGetExperimentalKmCoordinateTablePosition(string terminalId, out Vector2 localPosition)
        {
            const float prefabWidth = 240f;
            const float prefabHeight = 300f;
            var x = 0f;
            var y = 0f;

            if (string.Equals(terminalId, "L1", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 224f;
                y = 71f;
            }
            else if (string.Equals(terminalId, "L2", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 404f;
                y = 71f;
            }
            else if (string.Equals(terminalId, "L3", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 583f;
                y = 71f;
            }
            else if (string.Equals(terminalId, "T1", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 223f;
                y = 937f;
            }
            else if (string.Equals(terminalId, "T2", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 403f;
                y = 937f;
            }
            else if (string.Equals(terminalId, "T3", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 582f;
                y = 937f;
            }
            else if (string.Equals(terminalId, "A1", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 46f;
                y = 272f;
            }
            else if (string.Equals(terminalId, "A2", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 752f;
                y = 273f;
            }
            else if (string.Equals(terminalId, "13", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 46f;
                y = 496f;
            }
            else if (string.Equals(terminalId, "14", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 752f;
                y = 496f;
            }
            else if (string.Equals(terminalId, "21", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 46f;
                y = 717f;
            }
            else if (string.Equals(terminalId, "22", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 752f;
                y = 717f;
            }
            else
            {
                localPosition = Vector2.zero;
                return false;
            }

            localPosition = new Vector2((x / 800f - 0.5f) * prefabWidth, (0.5f - y / 1000f) * prefabHeight);
            return true;
        }

        private void TryApplyExperimentalButtonVisualPrefab()
        {
            experimentalButtonVisualRoot = null;
            experimentalButtonBodyImage = null;
            experimentalButtonDefaultSprite = null;
            experimentalButtonPressedSprite = null;
            experimentalButtonTerminalAnchors.Clear();

            if (!useExperimentalButtonVisualPrefab ||
                Definition == null ||
                !IsExperimentalButtonDefinition())
            {
                return;
            }

#if UNITY_EDITOR
            var prefabPath = string.Equals(Definition.name, experimentalStartButtonDefinitionName, System.StringComparison.Ordinal)
                ? experimentalStartButtonVisualAssetPath
                : experimentalStopButtonVisualAssetPath;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return;
            }

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalButtonVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalButtonVisualRoot != null)
            {
                experimentalButtonVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalButtonVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalButtonVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalButtonVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalButtonVisualRoot.sizeDelta.x > 0f &&
                    experimentalButtonVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalButtonVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalButtonTerminalAnchors(visualObject.transform);
            ConfigureExperimentalButtonBodyImage(visualObject.transform);

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void ConfigureExperimentalButtonBodyImage(Transform visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var bodyTransform = visualRoot.Find("Body");
            experimentalButtonBodyImage = bodyTransform != null ? bodyTransform.GetComponent<Image>() : null;
            if (experimentalButtonBodyImage != null)
            {
                experimentalButtonBodyImage.raycastTarget = false;
            }

#if UNITY_EDITOR
            if (string.Equals(Definition.name, experimentalStartButtonDefinitionName, System.StringComparison.Ordinal))
            {
                experimentalButtonDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalStartButtonDefaultSpritePath);
                experimentalButtonPressedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalStartButtonPressedSpritePath);
            }
            else
            {
                experimentalButtonDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalStopButtonDefaultSpritePath);
                experimentalButtonPressedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalStopButtonPressedSpritePath);
            }
#endif
            UpdateExperimentalButtonBodySprite();
        }

        private void UpdateExperimentalButtonBodySprite()
        {
            if (experimentalButtonBodyImage == null)
            {
                return;
            }

            var targetSprite = IsExperimentalButtonPressed() && experimentalButtonPressedSprite != null
                ? experimentalButtonPressedSprite
                : experimentalButtonDefaultSprite;

            if (targetSprite != null && experimentalButtonBodyImage.sprite != targetSprite)
            {
                experimentalButtonBodyImage.sprite = targetSprite;
            }
        }

        private void RegisterExperimentalButtonTerminalAnchors(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null ||
                    string.IsNullOrEmpty(candidate.name) ||
                    !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalButtonTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalButtonTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalVisualTerminalPosition(
            string terminalId,
            out Vector2 localPosition,
            out bool showDebugMarker)
        {
            if (IsExperimentalKmVisualActive() &&
                TryGetExperimentalTerminalPosition(terminalId, out localPosition))
            {
                showDebugMarker = showExperimentalKmTerminalDebugMarkers;
                return true;
            }

            if (configuredVisualPrefab != null &&
                configuredVisualPrefab.TryGetTerminalPosition(terminalId, out localPosition))
            {
                showDebugMarker = configuredVisualPrefab.ShowTerminalDebugMarkers;
                return true;
            }

            localPosition = Vector2.zero;
            showDebugMarker = false;
            return false;
        }

        private bool TryGetExperimentalButtonTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalButtonVisualActive() ||
                rectTransform == null ||
                string.IsNullOrWhiteSpace(terminalId) ||
                !experimentalButtonTerminalAnchors.TryGetValue(terminalId, out var anchor) ||
                anchor == null)
            {
                return false;
            }

            return TryGetAnchoredPositionRelativeToExperimentalRoot(
                anchor,
                experimentalButtonVisualRoot,
                out localPosition);
        }

        private void TryApplyExperimentalCompoundButtonVisualPrefab()
        {
            experimentalCompoundButtonVisualRoot = null;
            experimentalCompoundButtonBodyImage = null;
            experimentalCompoundButtonDefaultSprite = null;
            experimentalCompoundButtonPressedSprite = null;
            experimentalCompoundButtonTerminalAnchors.Clear();

            if (!useExperimentalCompoundButtonVisualPrefab ||
                Definition == null ||
                !IsExperimentalCompoundButtonDefinition())
            {
                return;
            }

#if UNITY_EDITOR
            var isGreen = string.Equals(Definition.name, experimentalCompoundGreenButtonDefinitionName, System.StringComparison.Ordinal);
            var prefabPath = isGreen ? experimentalCompoundGreenButtonVisualAssetPath : experimentalCompoundRedButtonVisualAssetPath;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return;
            }

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalCompoundButtonVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalCompoundButtonVisualRoot != null)
            {
                experimentalCompoundButtonVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalCompoundButtonVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalCompoundButtonVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalCompoundButtonVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalCompoundButtonVisualRoot.sizeDelta.x > 0f &&
                    experimentalCompoundButtonVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalCompoundButtonVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalCompoundButtonTerminalAnchors(visualObject.transform);
            ConfigureExperimentalCompoundButtonBodyImage(visualObject.transform);

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void ConfigureExperimentalCompoundButtonBodyImage(Transform visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var bodyTransform = visualRoot.Find("Body");
            experimentalCompoundButtonBodyImage = bodyTransform != null ? bodyTransform.GetComponent<Image>() : null;
            if (experimentalCompoundButtonBodyImage != null)
            {
                experimentalCompoundButtonBodyImage.raycastTarget = false;
            }

#if UNITY_EDITOR
            if (string.Equals(Definition.name, experimentalCompoundGreenButtonDefinitionName, System.StringComparison.Ordinal))
            {
                experimentalCompoundButtonDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalCompoundGreenButtonDefaultSpritePath);
                experimentalCompoundButtonPressedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalCompoundGreenButtonPressedSpritePath);
            }
            else
            {
                experimentalCompoundButtonDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalCompoundRedButtonDefaultSpritePath);
                experimentalCompoundButtonPressedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalCompoundRedButtonPressedSpritePath);
            }
#endif
            UpdateExperimentalCompoundButtonBodySprite();
        }

        private void UpdateExperimentalCompoundButtonBodySprite()
        {
            if (experimentalCompoundButtonBodyImage == null)
            {
                return;
            }

            var targetSprite = IsClosed && experimentalCompoundButtonPressedSprite != null
                ? experimentalCompoundButtonPressedSprite
                : experimentalCompoundButtonDefaultSprite;

            if (targetSprite != null && experimentalCompoundButtonBodyImage.sprite != targetSprite)
            {
                experimentalCompoundButtonBodyImage.sprite = targetSprite;
            }
        }

        private void RegisterExperimentalCompoundButtonTerminalAnchors(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null ||
                    string.IsNullOrEmpty(candidate.name) ||
                    !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalCompoundButtonTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalCompoundButtonTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalCompoundButtonTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalCompoundButtonVisualActive() ||
                rectTransform == null ||
                string.IsNullOrWhiteSpace(terminalId))
            {
                return false;
            }

            if (experimentalCompoundButtonTerminalAnchors.TryGetValue(terminalId, out var anchor) &&
                anchor != null &&
                TryGetAnchoredPositionRelativeToExperimentalRoot(
                    anchor,
                    experimentalCompoundButtonVisualRoot,
                    out localPosition))
            {
                return true;
            }

            return TryGetExperimentalCompoundButtonCoordinateTablePosition(terminalId, out localPosition);
        }

        private bool TryGetExperimentalCompoundButtonCoordinateTablePosition(string terminalId, out Vector2 localPosition)
        {
            const float prefabWidth = 80f;
            const float prefabHeight = 128f;
            var isGreen = string.Equals(Definition.name, experimentalCompoundGreenButtonDefinitionName, System.StringComparison.Ordinal);
            var x = 0f;
            var y = 0f;

            if (string.Equals(terminalId, "11", System.StringComparison.OrdinalIgnoreCase))
            {
                x = isGreen ? 36.16f : 38.16f;
                y = isGreen ? 331.16f : 329.16f;
            }
            else if (string.Equals(terminalId, "12", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 281.16f;
                y = isGreen ? 331.16f : 329.16f;
            }
            else if (string.Equals(terminalId, "23", System.StringComparison.OrdinalIgnoreCase))
            {
                x = isGreen ? 36.16f : 38.16f;
                y = 464.16f;
            }
            else if (string.Equals(terminalId, "24", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 281.16f;
                y = 464.16f;
            }
            else
            {
                localPosition = Vector2.zero;
                return false;
            }

            localPosition = new Vector2((x / 320f - 0.5f) * prefabWidth, (0.5f - y / 512f) * prefabHeight);
            return true;
        }

        private void TryApplyExperimentalSelfLockButtonVisualPrefab()
        {
            experimentalSelfLockButtonVisualRoot = null;
            experimentalSelfLockButtonBodyImage = null;
            experimentalSelfLockButtonDefaultSprite = null;
            experimentalSelfLockButtonPressedSprite = null;
            experimentalSelfLockButtonTerminalAnchors.Clear();

            if (!useExperimentalSelfLockButtonVisualPrefab ||
                Definition == null ||
                !IsExperimentalSelfLockButtonDefinition())
            {
                return;
            }

#if UNITY_EDITOR
            var isGreen = string.Equals(Definition.name, experimentalSelfLockGreenButtonDefinitionName, System.StringComparison.Ordinal);
            var prefabPath = isGreen ? experimentalSelfLockGreenButtonVisualAssetPath : experimentalSelfLockRedButtonVisualAssetPath;
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                return;
            }

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalSelfLockButtonVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalSelfLockButtonVisualRoot != null)
            {
                experimentalSelfLockButtonVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalSelfLockButtonVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalSelfLockButtonVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalSelfLockButtonVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalSelfLockButtonVisualRoot.sizeDelta.x > 0f &&
                    experimentalSelfLockButtonVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalSelfLockButtonVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalSelfLockButtonTerminalAnchors(visualObject.transform);
            ConfigureExperimentalSelfLockButtonBodyImage(visualObject.transform);

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void ConfigureExperimentalSelfLockButtonBodyImage(Transform visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var bodyTransform = visualRoot.Find("Body");
            experimentalSelfLockButtonBodyImage = bodyTransform != null ? bodyTransform.GetComponent<Image>() : null;
            if (experimentalSelfLockButtonBodyImage != null)
            {
                experimentalSelfLockButtonBodyImage.raycastTarget = false;
            }

#if UNITY_EDITOR
            if (string.Equals(Definition.name, experimentalSelfLockGreenButtonDefinitionName, System.StringComparison.Ordinal))
            {
                experimentalSelfLockButtonDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalSelfLockGreenButtonDefaultSpritePath);
                experimentalSelfLockButtonPressedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalSelfLockGreenButtonPressedSpritePath);
            }
            else
            {
                experimentalSelfLockButtonDefaultSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalSelfLockRedButtonDefaultSpritePath);
                experimentalSelfLockButtonPressedSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalSelfLockRedButtonPressedSpritePath);
            }
#endif
            UpdateExperimentalSelfLockButtonBodySprite();
        }

        private void UpdateExperimentalSelfLockButtonBodySprite()
        {
            if (experimentalSelfLockButtonBodyImage == null)
            {
                return;
            }

            var targetSprite = IsClosed && experimentalSelfLockButtonPressedSprite != null
                ? experimentalSelfLockButtonPressedSprite
                : experimentalSelfLockButtonDefaultSprite;

            if (targetSprite != null && experimentalSelfLockButtonBodyImage.sprite != targetSprite)
            {
                experimentalSelfLockButtonBodyImage.sprite = targetSprite;
            }
        }

        private void RegisterExperimentalSelfLockButtonTerminalAnchors(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null ||
                    string.IsNullOrEmpty(candidate.name) ||
                    !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalSelfLockButtonTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalSelfLockButtonTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalSelfLockButtonTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalSelfLockButtonVisualActive() ||
                rectTransform == null ||
                string.IsNullOrWhiteSpace(terminalId))
            {
                return false;
            }

            if (experimentalSelfLockButtonTerminalAnchors.TryGetValue(terminalId, out var anchor) &&
                anchor != null &&
                TryGetAnchoredPositionRelativeToExperimentalRoot(
                    anchor,
                    experimentalSelfLockButtonVisualRoot,
                    out localPosition))
            {
                return true;
            }

            return TryGetExperimentalSelfLockButtonCoordinateTablePosition(terminalId, out localPosition);
        }

        private bool TryGetExperimentalSelfLockButtonCoordinateTablePosition(string terminalId, out Vector2 localPosition)
        {
            const float prefabWidth = 80f;
            const float prefabHeight = 128f;
            var isGreen = string.Equals(Definition.name, experimentalSelfLockGreenButtonDefinitionName, System.StringComparison.Ordinal);
            var x = 0f;
            var y = 0f;

            if (string.Equals(terminalId, "11", System.StringComparison.OrdinalIgnoreCase))
            {
                x = isGreen ? 36.16f : 38.16f;
                y = isGreen ? 331.16f : 329.16f;
            }
            else if (string.Equals(terminalId, "12", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 281.16f;
                y = isGreen ? 331.16f : 329.16f;
            }
            else if (string.Equals(terminalId, "23", System.StringComparison.OrdinalIgnoreCase))
            {
                x = isGreen ? 36.16f : 38.16f;
                y = 464.16f;
            }
            else if (string.Equals(terminalId, "24", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 281.16f;
                y = 464.16f;
            }
            else
            {
                localPosition = Vector2.zero;
                return false;
            }

            localPosition = new Vector2((x / 320f - 0.5f) * prefabWidth, (0.5f - y / 512f) * prefabHeight);
            return true;
        }

        private void TryApplyExperimentalThreePhasePowerVisualPrefab()
        {
            experimentalThreePhasePowerVisualRoot = null;
            experimentalThreePhasePowerBodyImage = null;
            experimentalThreePhasePowerSprite = null;
            experimentalThreePhasePowerTerminalAnchors.Clear();

            if (!useExperimentalThreePhasePowerVisualPrefab ||
                Definition == null ||
                !string.Equals(Definition.name, experimentalThreePhasePowerDefinitionName, System.StringComparison.Ordinal))
            {
                return;
            }

#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(experimentalThreePhasePowerVisualAssetPath);
            if (prefab == null)
            {
                return;
            }

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalThreePhasePowerVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalThreePhasePowerVisualRoot != null)
            {
                experimentalThreePhasePowerVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalThreePhasePowerVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalThreePhasePowerVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalThreePhasePowerVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalThreePhasePowerVisualRoot.sizeDelta.x > 0f &&
                    experimentalThreePhasePowerVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalThreePhasePowerVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalThreePhasePowerTerminalAnchors(visualObject.transform);
            ConfigureExperimentalThreePhasePowerBodyImage(visualObject.transform);

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void ConfigureExperimentalThreePhasePowerBodyImage(Transform visualRoot)
        {
            if (visualRoot == null)
            {
                return;
            }

            var bodyTransform = visualRoot.Find("Body");
            experimentalThreePhasePowerBodyImage = bodyTransform != null ? bodyTransform.GetComponent<Image>() : null;
            if (experimentalThreePhasePowerBodyImage != null)
            {
                experimentalThreePhasePowerBodyImage.raycastTarget = false;
            }

#if UNITY_EDITOR
            experimentalThreePhasePowerSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(experimentalThreePhasePowerSpritePath);
#endif
            UpdateExperimentalThreePhasePowerBodySprite();
        }

        private void UpdateExperimentalThreePhasePowerBodySprite()
        {
            if (experimentalThreePhasePowerBodyImage == null)
            {
                return;
            }

            if (experimentalThreePhasePowerSprite != null &&
                experimentalThreePhasePowerBodyImage.sprite != experimentalThreePhasePowerSprite)
            {
                experimentalThreePhasePowerBodyImage.sprite = experimentalThreePhasePowerSprite;
            }
        }

        private void RegisterExperimentalThreePhasePowerTerminalAnchors(Transform root)
        {
            if (root == null)
            {
                return;
            }

            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null ||
                    string.IsNullOrEmpty(candidate.name) ||
                    !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalThreePhasePowerTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalThreePhasePowerTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalThreePhasePowerTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalThreePhasePowerVisualActive() ||
                rectTransform == null ||
                string.IsNullOrWhiteSpace(terminalId))
            {
                return false;
            }

            if (experimentalThreePhasePowerTerminalAnchors.TryGetValue(terminalId, out var anchor) &&
                anchor != null &&
                TryGetAnchoredPositionRelativeToExperimentalRoot(
                    anchor,
                    experimentalThreePhasePowerVisualRoot,
                    out localPosition))
            {
                return true;
            }

            return TryGetExperimentalThreePhasePowerCoordinateTablePosition(terminalId, out localPosition);
        }

        private static bool TryGetExperimentalThreePhasePowerCoordinateTablePosition(string terminalId, out Vector2 localPosition)
        {
            const float prefabWidth = 200f;
            const float prefabHeight = 72f;
            var x = 0f;
            var y = 177f;

            if (string.Equals(terminalId, "L1", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 103.06f;
            }
            else if (string.Equals(terminalId, "L2", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 301.97f;
            }
            else if (string.Equals(terminalId, "L3", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 500.88f;
            }
            else if (string.Equals(terminalId, "N", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 699.75f;
            }
            else if (string.Equals(terminalId, "PE", System.StringComparison.OrdinalIgnoreCase))
            {
                x = 899.94f;
            }
            else
            {
                localPosition = Vector2.zero;
                return false;
            }

            localPosition = new Vector2((x / 1000f - 0.5f) * prefabWidth, (0.5f - y / 360f) * prefabHeight);
            return true;
        }

        private static bool IsExperimentalKmTerminal(string terminalId)
        {
            return string.Equals(terminalId, "L1", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "L2", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "L3", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "T1", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "T2", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "T3", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "A1", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "A2", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "13", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "14", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "21", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(terminalId, "22", System.StringComparison.OrdinalIgnoreCase);
        }

        private void TryApplyConfiguredVisualPrefab()
        {
            // Visual Prefab 只替换外观和锚点；电气端子身份仍来自 Definition，
            // 因此不能让视觉资源决定规则、连接或保存数据。
            configuredVisualPrefab = null;

            if (Definition == null ||
                !VisualPrefabRegistry.TryGetConfig(Definition.name, out var config))
            {
                return;
            }

            VisualPrefabInstance.TryCreate(
                config,
                transform,
                rectTransform,
                body,
                title,
                out configuredVisualPrefab);

            ConfigureKtTimerVisualController();
        }

        private void ConfigureKtTimerVisualController()
        {
            ktTimerVisualController = null;
            if (configuredVisualPrefab == null ||
                !configuredVisualPrefab.IsActive ||
                !IsOnDelayTimerRelay() ||
                configuredVisualPrefab.Root == null)
            {
                return;
            }

            ktTimerVisualController = configuredVisualPrefab.Root.GetComponent<KTTimerVisualController>();
            if (ktTimerVisualController == null)
            {
                ktTimerVisualController = configuredVisualPrefab.Root.gameObject.AddComponent<KTTimerVisualController>();
            }

            ktTimerVisualController.Initialize(this, workspace);
        }

        private void UpdateConfiguredVisualPrefabBodySprite()
        {
            if (configuredVisualPrefab == null || !configuredVisualPrefab.IsActive)
            {
                return;
            }

            configuredVisualPrefab.UpdateBodySprite(ResolveConfiguredVisualPrefabActiveState(configuredVisualPrefab.Config));
        }

        private bool ResolveConfiguredVisualPrefabActiveState(VisualPrefabConfig config)
        {
            if (config == null)
            {
                return false;
            }

            switch (config.StateMode)
            {
                case VisualPrefabStateMode.IsClosed:
                    return config.ActiveWhenClosed ? IsClosed : !IsClosed;
                case VisualPrefabStateMode.IsEnergized:
                case VisualPrefabStateMode.ContactorEnergized:
                    return IsEnergized;
                case VisualPrefabStateMode.LimitSwitchTriggered:
                    return ResolveLimitSwitchTriggeredVisualState();
                default:
                    return false;
            }
        }

        private bool ResolveLimitSwitchTriggeredVisualState()
        {
            if (IsClosed)
            {
                return true;
            }

            if (string.Equals(InstanceId, "sq_left", System.StringComparison.OrdinalIgnoreCase) &&
                RuntimeStateManager.Shared.TryGetMotionState("motor_1", out var leftMotionState) &&
                leftMotionState != null)
            {
                return leftMotionState.LeftLimitTriggered;
            }

            if (string.Equals(InstanceId, "sq_right", System.StringComparison.OrdinalIgnoreCase) &&
                RuntimeStateManager.Shared.TryGetMotionState("motor_1", out var rightMotionState) &&
                rightMotionState != null)
            {
                return rightMotionState.RightLimitTriggered;
            }

            return false;
        }

        private void RefreshVisual()
        {
            if (body != null && Definition != null)
            {
                if (IsExperimentalVisualActive())
                {
                    body.enabled = true;
                    body.raycastTarget = true;
                    body.color = Color.clear;
                    UpdateExperimentalKmBodySprite();
                    UpdateConfiguredVisualPrefabBodySprite();
                }
                else
                {
                var baseColor = Definition.sprite != null ? Color.white : Definition.bodyColor;
                var color = IsEnergized ? Color.Lerp(baseColor, Definition.accentColor, 0.45f) : baseColor;
                body.color = selected ? Color.Lerp(color, Color.white, 0.35f) : color;
                }
            }

            if (stateLabel != null && Definition != null)
            {
                if (IsExperimentalVisualActive())
                {
                    stateLabel.text = "";
                }
                else if (Definition.kind == ComponentKind.TwoWaySwitch)
                {
                    stateLabel.text = IsClosed ? "L-L1" : "L-L2";
                    stateLabel.color = new Color(0.05f, 0.42f, 0.9f);
                }
                else if (IsOnDelayTimerRelay())
                {
                    ConfigureOnDelayTimerStateLabel(stateLabel);
                    stateLabel.text = GetOnDelayTimerRuntimeText();
                    stateLabel.color = GetOnDelayTimerRuntimeColor();
                }
                else if (Definition.togglable)
                {
                    ConfigureDefaultStateLabel(stateLabel);
                    stateLabel.fontSize = 18;
                    stateLabel.text = IsClosed ? "ON" : "OFF";
                    stateLabel.color = IsClosed ? new Color(0.05f, 0.55f, 0.24f) : new Color(0.65f, 0.1f, 0.1f);
                }
                else if (IsAutoReciprocatingMotionMotor())
                {
                    ConfigureMotionStateLabel(stateLabel);
                    stateLabel.text = GetMotionRuntimeText();
                    stateLabel.color = IsEnergized ? new Color(0.05f, 0.45f, 0.95f) : new Color(0.32f, 0.36f, 0.42f);
                }
                else
                {
                    ConfigureDefaultStateLabel(stateLabel);
                    stateLabel.fontSize = 18;
                    stateLabel.text = IsEnergized ? GetRunStateText() : "";
                    stateLabel.color = new Color(0.05f, 0.45f, 0.95f);
                }
            }

            ktTimerVisualController?.RefreshNow();
        }

        private bool IsExperimentalKmVisualActive()
        {
            return useExperimentalKmVisualPrefab &&
                   experimentalKmVisualRoot != null &&
                   Definition != null &&
                   (string.Equals(Definition.name, experimentalKmVisualDefinitionName, System.StringComparison.Ordinal) || string.Equals(Definition.name, "Contactor_KM_220V", System.StringComparison.Ordinal));
        }

        private bool IsExperimentalButtonVisualActive()
        {
            return useExperimentalButtonVisualPrefab &&
                   experimentalButtonVisualRoot != null &&
                   Definition != null &&
                   IsExperimentalButtonDefinition();
        }

        private bool IsExperimentalVisualActive()
        {
            return IsExperimentalKmVisualActive() ||
                   (configuredVisualPrefab != null && configuredVisualPrefab.IsActive);
        }

        private bool IsExperimentalCompoundButtonVisualActive()
        {
            return useExperimentalCompoundButtonVisualPrefab &&
                   experimentalCompoundButtonVisualRoot != null &&
                   Definition != null &&
                   IsExperimentalCompoundButtonDefinition();
        }

        private bool IsExperimentalThreePhasePowerVisualActive()
        {
            return useExperimentalThreePhasePowerVisualPrefab &&
                   experimentalThreePhasePowerVisualRoot != null &&
                   Definition != null &&
                   string.Equals(Definition.name, experimentalThreePhasePowerDefinitionName, System.StringComparison.Ordinal);
        }

        private bool IsExperimentalFuse1PVisualActive()
        {
            return useExperimentalFuseVisualPrefab &&
                   experimentalFuse1PVisualRoot != null &&
                   Definition != null &&
                   string.Equals(Definition.name, experimentalFuse1PDefinitionName, System.StringComparison.Ordinal);
        }

        private bool IsExperimentalFuse3PVisualActive()
        {
            return useExperimentalFuseVisualPrefab &&
                   experimentalFuse3PVisualRoot != null &&
                   Definition != null &&
                   string.Equals(Definition.name, experimentalFuse3PDefinitionName, System.StringComparison.Ordinal);
        }

        private bool IsExperimentalButtonDefinition()
        {
            return string.Equals(Definition.name, experimentalStartButtonDefinitionName, System.StringComparison.Ordinal) ||
                   string.Equals(Definition.name, experimentalStopButtonDefinitionName, System.StringComparison.Ordinal);
        }

        private bool IsExperimentalCompoundButtonDefinition()
        {
            return string.Equals(Definition.name, experimentalCompoundRedButtonDefinitionName, System.StringComparison.Ordinal) ||
                   string.Equals(Definition.name, experimentalCompoundGreenButtonDefinitionName, System.StringComparison.Ordinal);
        }

        private bool IsExperimentalSelfLockButtonVisualActive()
        {
            return useExperimentalSelfLockButtonVisualPrefab &&
                   experimentalSelfLockButtonVisualRoot != null &&
                   Definition != null &&
                   IsExperimentalSelfLockButtonDefinition();
        }

        private bool IsExperimentalSelfLockButtonDefinition()
        {
            return string.Equals(Definition.name, experimentalSelfLockRedButtonDefinitionName, System.StringComparison.Ordinal) ||
                   string.Equals(Definition.name, experimentalSelfLockGreenButtonDefinitionName, System.StringComparison.Ordinal);
        }

        private bool IsExperimentalButtonPressed()
        {
            if (Definition == null)
            {
                return false;
            }

            if (string.Equals(Definition.name, experimentalStartButtonDefinitionName, System.StringComparison.Ordinal))
            {
                return IsClosed;
            }

            if (string.Equals(Definition.name, experimentalStopButtonDefinitionName, System.StringComparison.Ordinal))
            {
                return !IsClosed;
            }

            return false;
        }

        private static void ConfigureMotionStateLabel(Text label)
        {
            if (label == null)
            {
                return;
            }

            label.fontSize = 11;
            label.lineSpacing = 0.88f;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 11;

            var rect = label.rectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(-0.18f, 0.08f);
                rect.anchorMax = new Vector2(1.18f, 0.92f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }

        private static void ConfigureDefaultStateLabel(Text label)
        {
            if (label == null)
            {
                return;
            }

            label.lineSpacing = 1f;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = false;
        }

        private static void ConfigureOnDelayTimerStateLabel(Text label)
        {
            if (label == null)
            {
                return;
            }

            label.fontSize = 11;
            label.lineSpacing = 0.85f;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 11;

            var rect = label.rectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(-0.15f, 0.2f);
                rect.anchorMax = new Vector2(1.15f, 0.8f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
        }

        private string GetOnDelayTimerRuntimeText()
        {
            if (!RuntimeStateManager.Shared.TryGetTimerState(InstanceId, out var timerState) || timerState == null)
            {
                return "KT: Reset 0.0 / " + ResolveDelaySeconds().ToString("0.0") + "s\n线圈未得电";
            }

            var coilText = timerState.IsCoilEnergized ? "得电" : "未得电";
            return "KT: " + timerState.Phase + " " +
                   timerState.ElapsedSeconds.ToString("0.0") + " / " +
                   timerState.DelaySeconds.ToString("0.0") + "s，线圈" +
                   coilText;
        }

        private float ResolveDelaySeconds()
        {
            var parameter = GetParameter("delaySeconds");
            return parameter != null ? Mathf.Max(0f, parameter.value) : 3f;
        }

        private Color GetOnDelayTimerRuntimeColor()
        {
            if (!RuntimeStateManager.Shared.TryGetTimerState(InstanceId, out var timerState) || timerState == null)
            {
                return IsClosed ? new Color(0.05f, 0.55f, 0.24f) : new Color(0.65f, 0.1f, 0.1f);
            }

            switch (timerState.Phase)
            {
                case TimerRuntimePhase.Elapsed:
                    return new Color(0.05f, 0.55f, 0.24f);
                case TimerRuntimePhase.Timing:
                    return new Color(0.85f, 0.46f, 0.08f);
                default:
                    return new Color(0.65f, 0.1f, 0.1f);
            }
        }

        private bool IsOnDelayTimerRelay()
        {
            return Definition != null &&
                   !string.IsNullOrWhiteSpace(Definition.name) &&
                   Definition.name.IndexOf("Timer_OnDelay", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsAutoReciprocatingMotionMotor()
        {
            return Definition != null &&
                   Definition.kind == ComponentKind.Motor &&
                   string.Equals(InstanceId, "motor_1", System.StringComparison.OrdinalIgnoreCase) &&
                   GetTerminal("U") != null &&
                   GetTerminal("V") != null &&
                   GetTerminal("W") != null &&
                   HasWorkspaceComponent("sq_left") &&
                   HasWorkspaceComponent("sq_right") &&
                   HasWorkspaceComponent("km_forward") &&
                   HasWorkspaceComponent("km_reverse");
        }

        private bool HasWorkspaceComponent(string instanceId)
        {
            if (workspace == null || workspace.Components == null || string.IsNullOrWhiteSpace(instanceId))
            {
                return false;
            }

            for (var i = 0; i < workspace.Components.Count; i++)
            {
                var component = workspace.Components[i];
                if (component != null &&
                    string.Equals(component.InstanceId, instanceId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetMotionRuntimeText()
        {
            var motionState = RuntimeStateManager.Shared.GetOrCreateMotionState(InstanceId);
            var electricalState = IsEnergized ? GetRunStateText() : "停止";
            if (motionState == null)
            {
                return "电气：" + electricalState + "\n虚拟运动：停止\n虚拟位置：50 / 100\n虚拟左限位：未触发\n虚拟右限位：未触发";
            }

            return "电气：" + electricalState +
                   "\n虚拟运动：" + MotionDirectionText(motionState.Direction) +
                   "\n虚拟位置：" + motionState.Position.ToString("0") + " / 100" +
                   "\n虚拟左限位：" + (motionState.LeftLimitTriggered ? "已触发" : "未触发") +
                   "\n虚拟右限位：" + (motionState.RightLimitTriggered ? "已触发" : "未触发") +
                   "\n速度：" + motionState.Speed.ToString("0") + " / s";
        }

        private static string MotionDirectionText(MotionDirection direction)
        {
            switch (direction)
            {
                case MotionDirection.Forward:
                    return "正向";
                case MotionDirection.Reverse:
                    return "反向";
                default:
                    return "停止";
            }
        }


        private void TryApplyExperimentalFuse1PVisualPrefab()
        {
            experimentalFuse1PVisualRoot = null;
            experimentalFuse1PBodyImage = null;
            experimentalFuse1PTerminalAnchors.Clear();

            if (!useExperimentalFuseVisualPrefab ||
                Definition == null ||
                !string.Equals(Definition.name, experimentalFuse1PDefinitionName, System.StringComparison.Ordinal))
            {
                return;
            }

#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(experimentalFuse1PVisualAssetPath);
            if (prefab == null) return;

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalFuse1PVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalFuse1PVisualRoot != null)
            {
                experimentalFuse1PVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalFuse1PVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalFuse1PVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalFuse1PVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalFuse1PVisualRoot.sizeDelta.x > 0f &&
                    experimentalFuse1PVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalFuse1PVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalFuse1PTerminalAnchors(visualObject.transform);

            var bodyTransform = visualObject.transform.Find("Body");
            if (bodyTransform != null)
            {
                experimentalFuse1PBodyImage = bodyTransform.GetComponent<Image>();
                if (experimentalFuse1PBodyImage != null)
                {
                    experimentalFuse1PBodyImage.raycastTarget = false;
                }
            }

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void RegisterExperimentalFuse1PTerminalAnchors(Transform root)
        {
            if (root == null) return;
            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.name) || !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal)) continue;
                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalFuse1PTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalFuse1PTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalFuse1PTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalFuse1PVisualActive() || rectTransform == null || string.IsNullOrWhiteSpace(terminalId)) return false;

            if (experimentalFuse1PTerminalAnchors.TryGetValue(terminalId, out var anchor) && anchor != null && TryGetAnchoredPositionRelativeToExperimentalRoot(anchor, experimentalFuse1PVisualRoot, out localPosition))
            {
                return true;
            }
            return false;
        }

        private void TryApplyExperimentalFuse3PVisualPrefab()
        {
            experimentalFuse3PVisualRoot = null;
            experimentalFuse3PBodyImage = null;
            experimentalFuse3PTerminalAnchors.Clear();

            if (!useExperimentalFuseVisualPrefab ||
                Definition == null ||
                !string.Equals(Definition.name, experimentalFuse3PDefinitionName, System.StringComparison.Ordinal))
            {
                return;
            }

#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(experimentalFuse3PVisualAssetPath);
            if (prefab == null) return;

            var visualObject = Instantiate(prefab, transform);
            visualObject.name = prefab.name + "_Pilot";
            visualObject.transform.SetAsFirstSibling();

            experimentalFuse3PVisualRoot = visualObject.GetComponent<RectTransform>();
            if (experimentalFuse3PVisualRoot != null)
            {
                experimentalFuse3PVisualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                experimentalFuse3PVisualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                experimentalFuse3PVisualRoot.pivot = new Vector2(0.5f, 0.5f);
                experimentalFuse3PVisualRoot.anchoredPosition = Vector2.zero;

                if (rectTransform != null &&
                    experimentalFuse3PVisualRoot.sizeDelta.x > 0f &&
                    experimentalFuse3PVisualRoot.sizeDelta.y > 0f)
                {
                    rectTransform.sizeDelta = experimentalFuse3PVisualRoot.sizeDelta;
                }
            }

            RegisterExperimentalFuse3PTerminalAnchors(visualObject.transform);

            var bodyTransform = visualObject.transform.Find("Body");
            if (bodyTransform != null)
            {
                experimentalFuse3PBodyImage = bodyTransform.GetComponent<Image>();
                if (experimentalFuse3PBodyImage != null)
                {
                    experimentalFuse3PBodyImage.raycastTarget = false;
                }
            }

            if (body != null)
            {
                body.enabled = true;
                body.raycastTarget = true;
                body.color = Color.clear;
            }

            if (title != null)
            {
                title.enabled = false;
            }
#endif
        }

        private void RegisterExperimentalFuse3PTerminalAnchors(Transform root)
        {
            if (root == null) return;
            var rects = root.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null || string.IsNullOrEmpty(candidate.name) || !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal)) continue;
                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!experimentalFuse3PTerminalAnchors.ContainsKey(terminalId))
                {
                    experimentalFuse3PTerminalAnchors.Add(terminalId, candidate);
                }
            }
        }

        private bool TryGetExperimentalFuse3PTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            if (!IsExperimentalFuse3PVisualActive() || rectTransform == null || string.IsNullOrWhiteSpace(terminalId)) return false;

            if (experimentalFuse3PTerminalAnchors.TryGetValue(terminalId, out var anchor) && anchor != null && TryGetAnchoredPositionRelativeToExperimentalRoot(anchor, experimentalFuse3PVisualRoot, out localPosition))
            {
                return true;
            }
            return false;
        }

        private string GetRunStateText()
        {
            if (Definition != null && Definition.kind == ComponentKind.Motor && GetTerminal("U") != null && GetTerminal("V") != null && GetTerminal("W") != null)
            {
                var direction = GetParameter("rotationDirection");
                if (direction != null)
                {
                    if (direction.value > 0.5f)
                    {
                        return "正转";
                    }

                    if (direction.value < -0.5f)
                    {
                        return "反转";
                    }
                }
            }

            return "RUN";
        }
    }
}

