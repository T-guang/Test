using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    public enum SpiceWorkspaceResultState { NeverRun, Running, Current, Stale, Failed }

    /// <summary>
    /// 独立 DC 原型的 UGUI 宿主。其 SpiceWorkspaceModel 是原型电路的唯一事实来源；
    /// 本类不读取或写入正式 WorkspaceController、WireManager、模板或检查助手。
    /// </summary>
    public sealed class SpiceWorkspaceController : MonoBehaviour
    {
        private readonly Dictionary<string, SpiceWorkspaceComponentView> componentViews = new Dictionary<string, SpiceWorkspaceComponentView>(StringComparer.Ordinal);
        private readonly List<SpiceWorkspaceWireView> wireViews = new List<SpiceWorkspaceWireView>();
        private SpiceDcSimulationService simulationService;
        private SpiceWorkspaceComponentView selectedComponent;
        private SpiceWorkspaceWireView selectedWire;
        private SpiceWorkspaceComponentView pendingComponent;
        private string pendingTerminalId;
        private Text statusText;
        private Text resultText;
        private Text parameterTitle;
        private InputField parameterInput;
        private Button unitButton;
        private Text unitLabel;
        private Button runButton;
        private string[] currentUnits = Array.Empty<string>();
        private int unitIndex;
        private bool initialized;

        public SpiceWorkspaceModel Model { get; } = new SpiceWorkspaceModel();
        public SpiceWorkspaceResultState ResultState { get; private set; } = SpiceWorkspaceResultState.NeverRun;
        public RectTransform WorkspaceRect { get; private set; }
        public RectTransform WireLayer { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            simulationService = new SpiceDcSimulationService();
            BuildUi();
            Model.Changed += HandleModelChanged;
            initialized = true;
        }

        private void OnDestroy()
        {
            Model.Changed -= HandleModelChanged;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) CancelPendingWire();
        }

        public SpiceWorkspaceComponentData CreateComponent(SpiceComponentKind kind)
        {
            EnsureInitialized();
            var offset = new Vector2(-120f + componentViews.Count * 32f, 90f - componentViews.Count * 24f);
            return CreateComponent(kind, offset);
        }

        public SpiceWorkspaceComponentData CreateComponent(SpiceComponentKind kind, Vector2 position)
        {
            EnsureInitialized();
            var data = Model.AddComponent(kind, position);
            CreateComponentView(data);
            SelectComponent(componentViews[data.InstanceId]);
            return data;
        }

        public bool Connect(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId)
        {
            EnsureInitialized();
            if (!Model.AddWire(startComponentId, startTerminalId, endComponentId, endTerminalId)) return false;
            var wire = Model.Wires[Model.Wires.Count - 1];
            wireViews.Add(new SpiceWorkspaceWireView(this, wire, componentViews[startComponentId], componentViews[endComponentId]));
            return true;
        }

        public bool TrySetParameter(string instanceId, double displayValue, string unit)
        {
            EnsureInitialized();
            var component = Model.FindComponent(instanceId);
            if (component == null || !SpiceParameterUnits.TryToSi(component.Kind, displayValue, unit, out var siValue)) return false;
            return Model.TrySetParameter(instanceId, siValue);
        }

        public async Task<SpiceSimulationResult> RunCalculationAsync()
        {
            EnsureInitialized();
            if (ResultState == SpiceWorkspaceResultState.Running) return null;
            ResultState = SpiceWorkspaceResultState.Running;
            runButton.interactable = false;
            statusText.text = "计算中...";
            resultText.text = string.Empty;
            try
            {
                var result = await simulationService.SimulateAsync(Model.BuildCircuitModel());
                if (result.Success)
                {
                    ResultState = SpiceWorkspaceResultState.Current;
                    statusText.text = "结果有效";
                    resultText.text = FormatResult(result);
                }
                else
                {
                    ResultState = SpiceWorkspaceResultState.Failed;
                    statusText.text = "计算失败";
                    resultText.text = FormatDiagnostics(result);
                }
                return result;
            }
            catch (Exception exception)
            {
                ResultState = SpiceWorkspaceResultState.Failed;
                statusText.text = "计算失败";
                resultText.text = exception.ToString();
                return null;
            }
            finally
            {
                runButton.interactable = true;
            }
        }

        public void SelectComponent(SpiceWorkspaceComponentView component)
        {
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedWire = null;
            selectedComponent = component;
            if (selectedComponent != null)
            {
                selectedComponent.SetSelected(true);
                RefreshParameterPanel();
            }
        }

        public void SelectWire(SpiceWorkspaceWireView wire)
        {
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = wire;
            wire.SetSelected(true);
            parameterTitle.text = "已选择导线";
            parameterInput.interactable = false;
            unitButton.interactable = false;
        }

        public void HandleTerminalClick(SpiceWorkspaceComponentView component, string terminalId)
        {
            if (pendingComponent == null)
            {
                pendingComponent = component;
                pendingTerminalId = terminalId;
                statusText.text = "请选择第二个端子，Esc 取消";
                return;
            }
            if (pendingComponent == component && pendingTerminalId == terminalId) return;
            Connect(pendingComponent.InstanceId, pendingTerminalId, component.InstanceId, terminalId);
            CancelPendingWire();
        }

        public void MoveComponent(string instanceId, Vector2 position)
        {
            Model.MoveComponent(instanceId, position);
            foreach (var wire in wireViews) wire.Refresh();
        }

        public void DeleteSelection()
        {
            if (selectedComponent != null)
            {
                var id = selectedComponent.InstanceId;
                Destroy(selectedComponent.gameObject);
                componentViews.Remove(id);
                wireViews.Where(wire => wire.Data.StartComponentId == id || wire.Data.EndComponentId == id).ToList().ForEach(RemoveWireView);
                Model.RemoveComponent(id);
                selectedComponent = null;
                ClearParameterPanel();
                return;
            }
            if (selectedWire != null)
            {
                RemoveWireView(selectedWire);
                selectedWire = null;
                ClearParameterPanel();
            }
        }

        public void ClearWorkspace()
        {
            foreach (var wire in wireViews.ToList()) wire.Destroy();
            wireViews.Clear();
            foreach (var view in componentViews.Values) Destroy(view.gameObject);
            componentViews.Clear();
            selectedComponent = null;
            selectedWire = null;
            CancelPendingWire();
            Model.Clear();
            ClearParameterPanel();
        }

        public void CancelPendingWire()
        {
            pendingComponent = null;
            pendingTerminalId = null;
            if (ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
        }

        private void BuildUi()
        {
            var canvas = GetComponent<Canvas>() ?? gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = GetComponent<CanvasScaler>() ?? gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (FindObjectOfType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                eventSystem.transform.SetParent(transform, false);
            }

            var root = SpiceWorkspaceUi.CreateImage(transform, "SpiceT3Root", new Color(0.94f, 0.96f, 0.98f));
            SpiceWorkspaceUi.Stretch(root.rectTransform, Vector2.zero, Vector2.zero);
            var toolbar = SpiceWorkspaceUi.CreateImage(root.transform, "Toolbar", new Color(0.07f, 0.14f, 0.23f));
            SpiceWorkspaceUi.Anchor(toolbar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -64f), Vector2.zero);
            runButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "Run", "运行计算", new Color(0.1f, 0.55f, 0.38f), RunFromButton);
            SpiceWorkspaceUi.Anchor(runButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, -20f), new Vector2(128f, 20f));
            var deleteButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "Delete", "删除", new Color(0.73f, 0.18f, 0.16f), DeleteSelection);
            SpiceWorkspaceUi.Anchor(deleteButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(156f, -20f), new Vector2(252f, 20f));
            var clearButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "Clear", "清空", new Color(0.35f, 0.41f, 0.5f), ClearWorkspace);
            SpiceWorkspaceUi.Anchor(clearButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(266f, -20f), new Vector2(362f, 20f));
            statusText = SpiceWorkspaceUi.CreateText(toolbar.transform, "Status", "未计算", 15, FontStyle.Bold, TextAnchor.MiddleRight, Color.white);
            SpiceWorkspaceUi.Anchor(statusText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-330f, 0f), new Vector2(-20f, 0f));

            var palette = SpiceWorkspaceUi.CreateImage(root.transform, "Palette", Color.white);
            SpiceWorkspaceUi.Anchor(palette.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(242f, -64f));
            var paletteTitle = SpiceWorkspaceUi.CreateText(palette.transform, "Title", "基础元件", 20, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.08f, 0.15f, 0.25f));
            SpiceWorkspaceUi.Anchor(paletteTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
            CreatePaletteButton(palette.transform, SpiceComponentKind.DcVoltageSource, "V  直流电压源\n10 V", -78f);
            CreatePaletteButton(palette.transform, SpiceComponentKind.Resistor, "R  电阻\n1 kOhm", -158f);
            CreatePaletteButton(palette.transform, SpiceComponentKind.Capacitor, "C  电容\n1 uF", -238f);
            CreatePaletteButton(palette.transform, SpiceComponentKind.Inductor, "L  电感\n10 mH", -318f);
            CreatePaletteButton(palette.transform, SpiceComponentKind.Ground, "GND  接地", -398f);

            var workspace = SpiceWorkspaceUi.CreateImage(root.transform, "Workspace", new Color(0.89f, 0.93f, 0.97f));
            SpiceWorkspaceUi.Anchor(workspace.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(254f, 16f), new Vector2(-368f, -80f));
            WorkspaceRect = workspace.rectTransform;
            workspace.gameObject.AddComponent<SpiceWorkspaceBlankClick>().Initialize(this);
            var wireLayer = new GameObject("WireLayer", typeof(RectTransform));
            wireLayer.transform.SetParent(workspace.transform, false);
            WireLayer = wireLayer.GetComponent<RectTransform>();
            SpiceWorkspaceUi.Stretch(WireLayer, Vector2.zero, Vector2.zero);
            WireLayer.SetAsFirstSibling();

            var side = SpiceWorkspaceUi.CreateImage(root.transform, "Inspector", Color.white);
            SpiceWorkspaceUi.Anchor(side.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-354f, 0f), new Vector2(0f, -64f));
            parameterTitle = SpiceWorkspaceUi.CreateText(side.transform, "ParameterTitle", "选择元件", 19, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.08f, 0.15f, 0.25f));
            SpiceWorkspaceUi.Anchor(parameterTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -10f));
            parameterInput = SpiceWorkspaceUi.CreateInput(side.transform, "ParameterInput");
            SpiceWorkspaceUi.Anchor(parameterInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(18f, -106f), new Vector2(-4f, -66f));
            unitButton = SpiceWorkspaceUi.CreateButton(side.transform, "Unit", "V", new Color(0.25f, 0.39f, 0.58f), CycleUnit);
            SpiceWorkspaceUi.Anchor(unitButton.GetComponent<RectTransform>(), new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(2f, -106f), new Vector2(-18f, -66f));
            unitLabel = unitButton.GetComponentInChildren<Text>();
            var apply = SpiceWorkspaceUi.CreateButton(side.transform, "Apply", "应用参数", new Color(0.1f, 0.45f, 0.72f), ApplyParameter);
            SpiceWorkspaceUi.Anchor(apply.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -154f), new Vector2(-18f, -116f));
            resultText = SpiceWorkspaceUi.CreateText(side.transform, "Results", "尚无结果", 14, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.1f, 0.16f, 0.23f));
            resultText.horizontalOverflow = HorizontalWrapMode.Wrap;
            resultText.verticalOverflow = VerticalWrapMode.Overflow;
            SpiceWorkspaceUi.Anchor(resultText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(18f, 18f), new Vector2(-18f, -178f));
            ClearParameterPanel();
        }

        private void CreatePaletteButton(Transform parent, SpiceComponentKind kind, string label, float y)
        {
            var button = SpiceWorkspaceUi.CreateButton(parent, kind.ToString(), label, new Color(0.91f, 0.95f, 0.99f), () => CreateComponent(kind));
            SpiceWorkspaceUi.Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, y - 56f), new Vector2(-16f, y));
            button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
        }

        private void CreateComponentView(SpiceWorkspaceComponentData data)
        {
            var view = new GameObject(data.InstanceId, typeof(RectTransform), typeof(SpiceWorkspaceComponentView)).GetComponent<SpiceWorkspaceComponentView>();
            view.transform.SetParent(WorkspaceRect, false);
            view.Initialize(this, data);
            componentViews.Add(data.InstanceId, view);
        }

        private void RemoveWireView(SpiceWorkspaceWireView wire)
        {
            wire.Destroy();
            wireViews.Remove(wire);
            Model.RemoveWire(wire.Data);
        }

        private void HandleModelChanged(SpiceWorkspaceChange change)
        {
            if (ResultState == SpiceWorkspaceResultState.Current) ResultState = SpiceWorkspaceResultState.Stale;
            if (ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
        }

        private void RefreshParameterPanel()
        {
            if (selectedComponent == null || selectedComponent.Kind == SpiceComponentKind.Ground)
            {
                ClearParameterPanel();
                return;
            }
            currentUnits = SpiceParameterUnits.UnitsFor(selectedComponent.Kind);
            unitIndex = 0;
            parameterTitle.text = selectedComponent.InstanceId + " 参数";
            parameterInput.interactable = true;
            unitButton.interactable = true;
            parameterInput.text = SpiceParameterUnits.FromSi(selectedComponent.Kind, selectedComponent.Data.SiValue, currentUnits[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
            unitLabel.text = currentUnits[unitIndex];
        }

        private void ClearParameterPanel()
        {
            parameterTitle.text = "选择元件";
            parameterInput.text = string.Empty;
            parameterInput.interactable = false;
            unitButton.interactable = false;
            unitLabel.text = "-";
        }

        private void CycleUnit()
        {
            if (selectedComponent == null || currentUnits.Length == 0) return;
            unitIndex = (unitIndex + 1) % currentUnits.Length;
            unitLabel.text = currentUnits[unitIndex];
            parameterInput.text = SpiceParameterUnits.FromSi(selectedComponent.Kind, selectedComponent.Data.SiValue, currentUnits[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
        }

        private void ApplyParameter()
        {
            if (selectedComponent == null || !double.TryParse(parameterInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !TrySetParameter(selectedComponent.InstanceId, value, currentUnits[unitIndex]))
            {
                statusText.text = "参数无效";
                return;
            }
            RefreshParameterPanel();
        }

        private async void RunFromButton()
        {
            await RunCalculationAsync();
        }

        private string StateMessage()
        {
            return ResultState == SpiceWorkspaceResultState.Stale ? "结果已过期" : ResultState == SpiceWorkspaceResultState.Failed ? "计算失败" : ResultState == SpiceWorkspaceResultState.Current ? "结果有效" : "未计算";
        }

        private static string FormatResult(SpiceSimulationResult result)
        {
            return string.Join("\n\n", result.ComponentResults.Values.OrderBy(value => value.ComponentId, StringComparer.Ordinal).Select(value =>
                value.ComponentId + "  " + value.ComponentKind + "\nU=" + value.Voltage.ToString("G6", CultureInfo.InvariantCulture) + " V\nI=" + value.Current.ToString("G6", CultureInfo.InvariantCulture) + " A (positive-to-negative)" +
                (string.IsNullOrEmpty(value.Notes) ? string.Empty : "\n" + value.Notes)));
        }

        private static string FormatDiagnostics(SpiceSimulationResult result)
        {
            return string.Join("\n", result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message + (string.IsNullOrEmpty(diagnostic.ComponentId) ? string.Empty : " [" + diagnostic.ComponentId + "]")));
        }
    }

    public sealed class SpiceWorkspaceBlankClick : MonoBehaviour, IPointerClickHandler
    {
        private SpiceWorkspaceController owner;
        public void Initialize(SpiceWorkspaceController workspace) { owner = workspace; }
        public void OnPointerClick(PointerEventData eventData) { owner.CancelPendingWire(); }
    }

    internal static class SpiceWorkspaceUi
    {
        private static Font font;
        private static Font Font => font ?? (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            return image;
        }
        public static Text CreateText(Transform parent, string name, string value, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false); text.font = Font; text.text = value; text.fontSize = size; text.fontStyle = style; text.alignment = anchor; text.color = color;
            return text;
        }
        public static Button CreateButton(Transform parent, string name, string label, Color color, UnityEngine.Events.UnityAction action)
        {
            var image = CreateImage(parent, name, color);
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            var text = CreateText(image.transform, "Text", label, 14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            return button;
        }
        public static InputField CreateInput(Transform parent, string name)
        {
            var image = CreateImage(parent, name, Color.white);
            var input = image.gameObject.AddComponent<InputField>();
            var text = CreateText(image.transform, "Text", string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.1f, 0.16f, 0.23f));
            Stretch(text.rectTransform, new Vector2(10f, 2f), new Vector2(-10f, -2f));
            input.textComponent = text;
            return input;
        }
        public static void Stretch(RectTransform rect, Vector2 min, Vector2 max) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
        public static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) { rect.anchorMin = anchorMin; rect.anchorMax = anchorMax; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    }
}
