using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ElectricalSim.Spice.Core;
using ElectricalSim.Spice.Results;
using ElectricalSim.Spice.Topology;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    // UI 构建、参数区与展示刷新；不推进 electrical revision 或直接运行求解器。
    public sealed partial class SpiceWorkspaceController
    {
        private void BuildUi()
        {
            var toolbar = bindings.RunButton.transform.parent;
            CreateAnalysisControls(toolbar as RectTransform);
            runButton = bindings.RunButton;
            rotateButton = bindings.RotateButton;
            runButton.onClick.AddListener(RunFromButton);
            rotateButton.onClick.AddListener(RotateSelectedComponent);
            bindings.DeleteButton.onClick.AddListener(DeleteSelection);
            bindings.ClearButton.onClick.AddListener(ClearWorkspace);
            statusText = SpiceWorkspaceUi.CreateText(toolbar.transform, "Status", "未计算", 15, FontStyle.Normal, TextAnchor.MiddleRight, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(statusText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-350f, 0f), new Vector2(-20f, 0f));

            // 工具栏新增视图命令按钮：[－] [100%] [＋] [适配全部] [重置视图]
            zoomOutButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ZoomOut", "－", MainUiTheme.ToolbarButton, null);
            var zoomOut = zoomOutButton;
            SpiceWorkspaceUi.Anchor(zoomOut.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(464f, -20f), new Vector2(498f, 20f));
            zoomLabel = SpiceWorkspaceUi.CreateText(toolbar.transform, "ZoomLabel", "100%", 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(zoomLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(506f, -14f), new Vector2(556f, 14f));
            zoomInButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ZoomIn", "＋", MainUiTheme.ToolbarButton, null);
            var zoomIn = zoomInButton;
            SpiceWorkspaceUi.Anchor(zoomIn.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(564f, -20f), new Vector2(598f, 20f));
            var fitAll = SpiceWorkspaceUi.CreateButton(toolbar.transform, "FitAll", "适配全部", MainUiTheme.ToolbarButton, null);
            SpiceWorkspaceUi.Anchor(fitAll.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(606f, -20f), new Vector2(676f, 20f));
            var resetView = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ResetView", "重置视图", MainUiTheme.ToolbarButton, null);
            SpiceWorkspaceUi.Anchor(resetView.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(684f, -20f), new Vector2(754f, 20f));

            var palette = bindings.PaletteRoot;
            var paletteTitle = SpiceWorkspaceUi.CreateText(palette.transform, "Title", "基础元件", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(paletteTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -48f), new Vector2(-18f, -8f));
            CreatePaletteCard(palette.transform, SpiceComponentKind.DcVoltageSource, "直流电压源", "10 V", 0, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.DcCurrentSource, "直流电流源", "1 mA", 1, 2);
            CreatePaletteCard(palette.transform, SpiceComponentKind.IdealSwitch, "理想开关", "断开", 0, 3);
            CreatePaletteCard(palette.transform, SpiceComponentKind.SiliconDiode, "通用硅二极管", "D_GENERIC", 1, 3);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Resistor, "电阻", "1 kOhm", 1, 0);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Capacitor, "电容", "1 uF", 0, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Inductor, "电感", "10 mH", 1, 1);
            CreatePaletteCard(palette.transform, SpiceComponentKind.Ground, "接地", "GND", 0, 2);
            CreatePaletteCard(palette.transform, SpiceComponentKind.VoltageProbe, "电压探针", "V+ - V-", 0, 4);
            CreatePaletteCard(palette.transform, SpiceComponentKind.CurrentProbe, "电流探针", "IN → OUT", 1, 4);

            CreatePaletteCard(palette.transform, SpiceComponentKind.AcVoltageSource, "交流电压源", "~  AC", 0, 5);
            CreatePaletteCard(palette.transform, SpiceComponentKind.IdealOperationalAmplifier, "理想运算放大器", "OP  +  −", 1, 5);

            var workspace = bindings.WorkspaceViewport;
            viewportRect = workspace;
            // 为 Viewport 添加 RectMask2D 以裁剪缩放/平移后超出视口的内容
            if (workspace.GetComponent<RectMask2D>() == null) workspace.gameObject.AddComponent<RectMask2D>();
            // 创建统一 Content 容器：缩放/平移只作用于 Content，所有层共享 Content 坐标系
            contentRect = new GameObject("SpiceWorkspaceContent", typeof(RectTransform)).GetComponent<RectTransform>();
            contentRect.SetParent(workspace, false);
            contentRect.anchorMin = new Vector2(0.5f, 0.5f);
            contentRect.anchorMax = new Vector2(0.5f, 0.5f);
            contentRect.pivot = new Vector2(0.5f, 0.5f);
            // 逻辑画布宽高 = Viewport 初始尺寸 × 3
            var viewportSize = workspace.rect.size;
            contentRect.sizeDelta = viewportSize * 3f;
            contentRect.localScale = Vector3.one;
            contentRect.anchoredPosition = Vector2.zero;
            // 将三层重新挂到 Content 下，保持 WireLayer 在最底
            var gridLayer = FindDirectGridLayer(workspace);
            ConfigureWorkspaceLayer(gridLayer, contentRect);
            ConfigureWorkspaceLayer(bindings.WireLayer, contentRect);
            ConfigureWorkspaceLayer(bindings.ComponentLayer, contentRect);
            ConfigureWorkspaceLayer(bindings.OverlayLayer, contentRect);
            if (gridLayer != null) gridLayer.SetSiblingIndex(0);
            bindings.WireLayer.SetSiblingIndex(1);
            bindings.ComponentLayer.SetSiblingIndex(2);
            bindings.OverlayLayer.SetSiblingIndex(3);
            // WorkspaceRect 指向 Content：ComponentView/WireView 的坐标转换无需修改
            WorkspaceRect = contentRect;
            WireLayer = bindings.WireLayer;
            OverlayLayer = bindings.OverlayLayer;
            // BlankClick 仍挂在 Viewport 上，负责空白点击和右键撤点
            workspace.gameObject.AddComponent<SpiceWorkspaceBlankClick>().Initialize(this);
            // ViewController owns view-only zoom and navigation input.
            viewController = workspace.gameObject.AddComponent<SpiceWorkspaceViewController>();

            palettePreview = SpiceWorkspaceUi.CreateImage(OverlayLayer, "PaletteDragPreview", new Color(0.15f, 0.39f, 0.92f, 0.22f));
            palettePreview.rectTransform.sizeDelta = new Vector2(130f, 72f);
            palettePreview.raycastTarget = false;
            var previewLabel = SpiceWorkspaceUi.CreateText(palettePreview.transform, "Label", string.Empty, 13, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.PrimaryBlue);
            SpiceWorkspaceUi.Stretch(previewLabel.rectTransform, Vector2.zero, Vector2.zero);
            palettePreview.gameObject.SetActive(false);

            var side = bindings.AssistantRoot;
            var parameterRoot = bindings.ParameterRoot;
            var resultRoot = bindings.ResultRoot;
            var netlistRoot = bindings.NetlistRoot;
            var diagnosticRoot = bindings.DiagnosticRoot;
            var assistantTitle = SpiceWorkspaceUi.CreateText(side.transform, "AssistantTitle", "仿真助手", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(assistantTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -46f), new Vector2(-18f, -8f));
            ConfigureAssistantPanel(parameterRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -198f), new Vector2(-12f, -54f));
            ConfigureAssistantPanel(resultRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -396f), new Vector2(-12f, -206f));
            ConfigureAssistantPanel(netlistRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -564f), new Vector2(-12f, -404f));
            ConfigureAssistantPanel(diagnosticRoot, Vector2.zero, Vector2.one, new Vector2(12f, 18f), new Vector2(-12f, -572f));

            parameterTitle = SpiceWorkspaceUi.CreateText(parameterRoot, "ParameterTitle", "参数设置", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(parameterTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(14f, -38f), new Vector2(-14f, -8f));
            parameterInput = SpiceWorkspaceUi.CreateInput(parameterRoot, "ParameterInput");
            SpiceWorkspaceUi.Anchor(parameterInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(14f, -82f), new Vector2(-4f, -44f));
            acPhaseLabel = SpiceWorkspaceUi.CreateText(parameterRoot, "AcPhaseLabel", "相位", 13, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(acPhaseLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -120f), new Vector2(70f, -90f));
            acPhaseInput = SpiceWorkspaceUi.CreateInput(parameterRoot, "AcPhaseInput");
            SpiceWorkspaceUi.Anchor(acPhaseInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.62f, 1f), new Vector2(74f, -122f), new Vector2(-4f, -88f));
            var phaseUnit = SpiceWorkspaceUi.CreateText(parameterRoot, "AcPhaseUnit", "°", 14, FontStyle.Normal, TextAnchor.MiddleCenter, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(phaseUnit.rectTransform, new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(2f, -120f), new Vector2(-14f, -90f));
            unitButton = SpiceWorkspaceUi.CreateButton(parameterRoot, "Unit", "V", MainUiTheme.FilterButton, CycleUnit);
            SpiceWorkspaceUi.Anchor(unitButton.GetComponent<RectTransform>(), new Vector2(0.64f, 1f), new Vector2(1f, 1f), new Vector2(2f, -82f), new Vector2(-14f, -44f));
            unitLabel = unitButton.GetComponentInChildren<Text>();
            var apply = SpiceWorkspaceUi.CreateButton(parameterRoot, "Apply", "应用参数", MainUiTheme.PrimaryBlue, ApplyParameter, true);
            SpiceWorkspaceUi.Anchor(apply.GetComponent<RectTransform>(), Vector2.zero, new Vector2(1f, 0f), new Vector2(14f, 10f), new Vector2(-14f, 42f));
            parameterApplyButton = apply;
            opAmpInfoText = SpiceWorkspaceUi.CreateText(parameterRoot, "OpAmpInfo", string.Empty, 13, FontStyle.Normal, TextAnchor.UpperLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(opAmpInfoText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(14f, 12f), new Vector2(-14f, -44f));
            opAmpInfoText.gameObject.SetActive(false);

            CreatePanelHeader(resultRoot, "ResultHeader", "计算结果", 34f);
            var resultHeader = resultRoot.Find("ResultHeader") as RectTransform;
            copyResultButton = SpiceWorkspaceUi.CreateButton(resultHeader, "CopyResult", "复制结果", MainUiTheme.FilterButton, CopyResult);
            SpiceWorkspaceUi.Anchor(copyResultButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-128f, 2f), new Vector2(-14f, -2f));
            resultView = CreateScrollableTextView(resultRoot, "ResultScrollView", "ResultText", 14, MainUiTheme.NormalText, 38f);
            resultText = resultView.Text;

            var netlistHeader = SpiceWorkspaceUi.CreateImage(netlistRoot, "NetlistHeader", new Color(0.96f, 0.98f, 1f));
            netlistHeader.rectTransform.pivot = new Vector2(0.5f, 1f);
            SpiceWorkspaceUi.Anchor(netlistHeader.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -58f), new Vector2(-10f, 0f));
            var netlistTitle = SpiceWorkspaceUi.CreateText(netlistHeader.transform, "Title", "生成网表", 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(netlistTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(14f, 0f), new Vector2(-194f, -4f));
            netlistStatusText = SpiceWorkspaceUi.CreateText(netlistHeader.transform, "Status", "尚未生成网表。", 11, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(netlistStatusText.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(14f, 4f), new Vector2(-14f, 0f));
            netlistToggleButton = SpiceWorkspaceUi.CreateButton(netlistHeader.transform, "Toggle", "展开", MainUiTheme.FilterButton, ToggleNetlist);
            SpiceWorkspaceUi.Anchor(netlistToggleButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-188f, 2f), new Vector2(-104f, -2f));
            copyNetlistButton = SpiceWorkspaceUi.CreateButton(netlistHeader.transform, "Copy", "复制", MainUiTheme.FilterButton, CopyNetlist);
            SpiceWorkspaceUi.Anchor(copyNetlistButton.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(-98f, 2f), new Vector2(-14f, -2f));

            netlistView = CreateScrollableTextView(netlistRoot, "NetlistScrollView", "NetlistText", 12, MainUiTheme.NormalText, 62f);
            netlistText = netlistView.Text;

            CreatePanelHeader(diagnosticRoot, "DiagnosticHeader", "诊断信息", 34f);
            diagnosticView = CreateScrollableTextView(diagnosticRoot, "DiagnosticScrollView", "DiagnosticText", 13, MainUiTheme.DangerRed, 38f);
            diagnosticText = diagnosticView.Text;
            ClearParameterPanel();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            RefreshCopyResultButton();
            RefreshAnalysisControls();
            ValidateAssistantScrollStructure();

            // 视图控制器初始化（Content 已装配，zoomLabel 已创建）
            viewController.Initialize(this, viewportRect, contentRect, zoomLabel);
            // 视图命令按钮绑定
            zoomOut.onClick.AddListener(viewController.ZoomOut);
            zoomIn.onClick.AddListener(viewController.ZoomIn);
            fitAll.onClick.AddListener(viewController.FitAll);
            resetView.onClick.AddListener(viewController.ResetView);

            // 文件工作流 文件操作工具栏按钮：[保存][另存为][导入]
            // 布局紧随视图命令按钮之后，与右侧 Status 文本之间保持留白，避免 1366×768 下重叠。
            // 按钮宽度对齐既有 ToolbarButton 样式（70f），高度同“旋转/删除”按钮（40f）。
            saveFileButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "SaveFile", "保存", MainUiTheme.ToolbarButton, HandleSaveButtonClicked);
            SpiceWorkspaceUi.Anchor(saveFileButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(762f, -20f), new Vector2(832f, 20f));
            saveAsFileButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "SaveAsFile", "另存为", MainUiTheme.ToolbarButton, HandleSaveAsButtonClicked);
            SpiceWorkspaceUi.Anchor(saveAsFileButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(840f, -20f), new Vector2(910f, 20f));
            importFileButton = SpiceWorkspaceUi.CreateButton(toolbar.transform, "ImportFile", "导入", MainUiTheme.ToolbarButton, HandleImportButtonClicked);
            SpiceWorkspaceUi.Anchor(importFileButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(918f, -20f), new Vector2(988f, 20f));
        }

        private static RectTransform FindDirectGridLayer(RectTransform workspace)
        {
            for (var index = 0; index < workspace.childCount; index++)
            {
                var child = workspace.GetChild(index) as RectTransform;
                if (child != null && child.GetComponent<WorkspaceGrid>() != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static void ConfigureWorkspaceLayer(RectTransform layer, RectTransform content)
        {
            if (layer == null)
            {
                return;
            }

            layer.SetParent(content, false);
            layer.anchorMin = Vector2.zero;
            layer.anchorMax = Vector2.one;
            layer.pivot = new Vector2(0.5f, 0.5f);
            layer.anchoredPosition = Vector2.zero;
            layer.sizeDelta = Vector2.zero;
            layer.localScale = Vector3.one;
            layer.localRotation = Quaternion.identity;
        }

        private void CreatePaletteCard(Transform parent, SpiceComponentKind kind, string title, string summary, int column, int row)
        {
            var card = SpiceWorkspaceUi.CreateImage(parent, kind + "Card", MainUiTheme.SelectedBlue);
            var x = 16f + column * 132f;
            var y = -66f - row * 100f;
            SpiceWorkspaceUi.Anchor(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, y - 82f), new Vector2(x + 116f, y));
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            card.gameObject.AddComponent<SpiceWorkspacePaletteDragItem>().Initialize(this, kind);
            paletteCardGroups[kind] = card.gameObject.AddComponent<CanvasGroup>();
            var label = SpiceWorkspaceUi.CreateText(card.transform, "Title", title, 14, FontStyle.Bold, TextAnchor.MiddleCenter, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(label.rectTransform, new Vector2(0f, 0.45f), new Vector2(1f, 1f), new Vector2(6f, 0f), new Vector2(-6f, -8f));
            var meta = SpiceWorkspaceUi.CreateText(card.transform, "Summary", summary, 12, FontStyle.Normal, TextAnchor.MiddleCenter, MainUiTheme.MutedText);
            SpiceWorkspaceUi.Anchor(meta.rectTransform, Vector2.zero, new Vector2(1f, 0.45f), new Vector2(6f, 6f), new Vector2(-6f, -2f));
        }

        private void CreateComponentView(SpiceWorkspaceComponentData data)
        {
            var view = new GameObject(data.InstanceId, typeof(RectTransform), typeof(SpiceWorkspaceComponentView)).GetComponent<SpiceWorkspaceComponentView>();
            view.transform.SetParent(bindings.ComponentLayer, false);
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
            isDirty = true;
            AdvanceElectricalRevision();
            if (ResultState != SpiceWorkspaceResultState.Running && ResultState != SpiceWorkspaceResultState.NeverRun)
            {
                ResultState = SpiceWorkspaceResultState.Stale;
                lastOutcomeText = null;
                SetResultText(string.Empty);
                SetDiagnosticText(string.Empty);
            }
            if (ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
            RefreshNetlistUi();
            RefreshCopyResultButton();
            RefreshAnalysisControls();
            RefreshParameterPanel();
        }

        private void CreateAnalysisControls(RectTransform toolbar)
        {
            var root = toolbar != null ? toolbar.parent as RectTransform : null;
            if (root == null) root = toolbar;
            if (root == null) return;

            if (root.rect.height >= 500f)
            {
                ReserveTopSpace(bindings.PaletteRoot, -102f);
                ReserveTopSpace(bindings.AssistantRoot, -102f);
                ReserveTopSpace(bindings.WorkspaceViewport, -118f);
            }

            var bar = SpiceWorkspaceUi.CreateImage(root, "AnalysisControls", new Color(0.96f, 0.98f, 1f));
            SpiceWorkspaceUi.Anchor(bar.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -102f), new Vector2(0f, -66f));
            bar.raycastTarget = true;

            var title = SpiceWorkspaceUi.CreateText(bar.transform, "AnalysisLabel", "分析", 13, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(18f, 0f), new Vector2(54f, 0f));
            dcAnalysisModeButton = SpiceWorkspaceUi.CreateButton(bar.transform, "DcAnalysisMode", "直流工作点", MainUiTheme.FilterButton, () => SetAnalysisModeFromUi(SpiceAnalysisMode.DcOperatingPoint));
            SpiceWorkspaceUi.Anchor(dcAnalysisModeButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(58f, 4f), new Vector2(146f, -4f));
            acAnalysisModeButton = SpiceWorkspaceUi.CreateButton(bar.transform, "AcAnalysisMode", "单频 AC", MainUiTheme.FilterButton, () => SetAnalysisModeFromUi(SpiceAnalysisMode.AcSingleFrequency));
            SpiceWorkspaceUi.Anchor(acAnalysisModeButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(150f, 4f), new Vector2(224f, -4f));

            var frequencyLabel = SpiceWorkspaceUi.CreateText(bar.transform, "FrequencyLabel", "频率", 13, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(frequencyLabel.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(246f, 0f), new Vector2(286f, 0f));
            acFrequencyInput = SpiceWorkspaceUi.CreateInput(bar.transform, "AcFrequencyInput");
            SpiceWorkspaceUi.Anchor(acFrequencyInput.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(290f, 4f), new Vector2(386f, -4f));
            var frequencyUnit = SpiceWorkspaceUi.CreateText(bar.transform, "FrequencyUnit", "Hz", 13, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Anchor(frequencyUnit.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(390f, 0f), new Vector2(414f, 0f));
            applyAcFrequencyButton = SpiceWorkspaceUi.CreateButton(bar.transform, "ApplyAcFrequency", "应用", MainUiTheme.PrimaryBlue, ApplyAcFrequency);
            SpiceWorkspaceUi.Anchor(applyAcFrequencyButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(420f, 4f), new Vector2(480f, -4f));
        }

        private static void ReserveTopSpace(RectTransform rect, float topOffset)
        {
            if (rect == null) return;
            var offsetMax = rect.offsetMax;
            offsetMax.y = topOffset;
            rect.offsetMax = offsetMax;
        }

        private void SetAnalysisModeFromUi(SpiceAnalysisMode mode)
        {
            if (!TrySetAnalysisMode(mode) && ResultState != SpiceWorkspaceResultState.Running && statusText != null)
                statusText.text = "分析模式未改变。";
            RefreshAnalysisControls();
        }

        private void ApplyAcFrequency()
        {
            if (ResultState == SpiceWorkspaceResultState.Running || Model.AnalysisMode != SpiceAnalysisMode.AcSingleFrequency) return;
            if (!double.TryParse(acFrequencyInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var frequencyHz) ||
                !SpiceAnalysisLimits.IsValidFrequency(frequencyHz))
            {
                acFrequencyInput.text = FormatFrequencyInput(Model.AcFrequencyHz);
                if (statusText != null) statusText.text = "频率必须是 0.001 Hz～10000000 Hz 范围内的有限数值。";
                return;
            }

            if (!TrySetAcFrequency(frequencyHz) && statusText != null)
                statusText.text = "频率未改变。";
            RefreshAnalysisControls();
        }

        private void RefreshAnalysisControls()
        {
            if (dcAnalysisModeButton == null || acAnalysisModeButton == null || acFrequencyInput == null || applyAcFrequencyButton == null) return;
            var isRunning = ResultState == SpiceWorkspaceResultState.Running;
            var isAc = Model.AnalysisMode == SpiceAnalysisMode.AcSingleFrequency;
            dcAnalysisModeButton.interactable = !isRunning && isAc;
            acAnalysisModeButton.interactable = !isRunning && !isAc;
            ApplyAnalysisModeVisual(dcAnalysisModeButton, !isAc);
            ApplyAnalysisModeVisual(acAnalysisModeButton, isAc);
            acFrequencyInput.interactable = !isRunning && isAc;
            applyAcFrequencyButton.interactable = !isRunning && isAc;
            acFrequencyInput.text = FormatFrequencyInput(Model.AcFrequencyHz);
            foreach (var pair in paletteCardGroups)
            {
                var enabled = !isRunning && IsPaletteKindAvailable(pair.Key);
                pair.Value.interactable = enabled;
                pair.Value.blocksRaycasts = enabled;
                pair.Value.alpha = enabled ? 1f : 0.42f;
            }
        }

        /// <summary>
        /// ResultState 变化后的交互控件刷新边界。三类控件都依赖 Running 与当前分析设置，
        /// 因此仅在原本同时刷新三者的位置收口，避免遗漏而不把其他视图刷新纳入万能入口。
        /// </summary>
        private void RefreshResultStateDependentControls()
        {
            RefreshFileOperationButtonsAvailability();
            RefreshAnalysisControls();
            RefreshParameterPanel();
        }

        private static void ApplyAnalysisModeVisual(Button button, bool selected)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image != null) image.color = selected ? MainUiTheme.PrimaryBlue : MainUiTheme.FilterButton;
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.color = selected ? Color.white : MainUiTheme.NormalText;
        }

        private static string FormatFrequencyInput(double frequencyHz) => frequencyHz.ToString("G9", CultureInfo.InvariantCulture);

        private bool IsPaletteKindAvailable(SpiceComponentKind kind)
        {
            if (Model.AnalysisMode == SpiceAnalysisMode.DcOperatingPoint)
                return kind != SpiceComponentKind.AcVoltageSource;
            return kind != SpiceComponentKind.DcVoltageSource &&
                kind != SpiceComponentKind.DcCurrentSource &&
                kind != SpiceComponentKind.SiliconDiode;
        }


        private static void ConfigureAssistantPanel(RectTransform panel, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            SpiceWorkspaceUi.Anchor(panel, anchorMin, anchorMax, offsetMin, offsetMax);
            var image = panel.GetComponent<Image>() ?? panel.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var outline = panel.GetComponent<Outline>() ?? panel.gameObject.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
        }

        private static void CreatePanelHeader(RectTransform panel, string name, string title, float height)
        {
            var header = SpiceWorkspaceUi.CreateImage(panel, name, new Color(0.96f, 0.98f, 1f));
            header.rectTransform.pivot = new Vector2(0.5f, 1f);
            SpiceWorkspaceUi.Anchor(header.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(10f, -height), new Vector2(-10f, 0f));
            var text = SpiceWorkspaceUi.CreateText(header.transform, "Title", title, 16, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.SecondaryText);
            SpiceWorkspaceUi.Stretch(text.rectTransform, new Vector2(14f, 0f), new Vector2(-14f, 0f));
        }

        // Each assistant section owns an independent standard UGUI scroll hierarchy.
        private static SpiceScrollableTextView CreateScrollableTextView(RectTransform panel, string scrollName, string textName, int fontSize, Color color, float topInset)
        {
            var scroll = new GameObject(scrollName, typeof(RectTransform), typeof(Image), typeof(ScrollRect)).GetComponent<ScrollRect>();
            scroll.transform.SetParent(panel, false);
            SpiceWorkspaceUi.Anchor(scroll.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(10f, 8f), new Vector2(-10f, -topInset));
            scroll.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.scrollSensitivity = SpiceScrollableTextLayout.ScrollSensitivity;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(scroll.transform, false);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            SpiceWorkspaceUi.Stretch(viewport, Vector2.zero, Vector2.zero);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 0f);
            SpiceScrollableTextLayout.ConfigureContent(content);
            var text = SpiceWorkspaceUi.CreateText(content, textName, string.Empty, fontSize, FontStyle.Normal, TextAnchor.UpperLeft, color);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.rectTransform.anchorMin = new Vector2(0f, 1f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.pivot = new Vector2(0.5f, 1f);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = Vector2.zero;
            scroll.viewport = viewport;
            scroll.content = content;
            return new SpiceScrollableTextView(scroll, viewport, content, text);
        }

        private static void RefreshScrollableText(SpiceScrollableTextView view, string value, bool resetToTop = true)
        {
            SpiceScrollableTextLayout.Refresh(view.ScrollRect, view.Text, value, resetToTop);
        }

        private void SetResultText(string value) => RefreshScrollableText(resultView, string.IsNullOrEmpty(value) ? "尚无结果" : value);
        private void SetDiagnosticText(string value) => RefreshScrollableText(diagnosticView, value ?? string.Empty);

        /// <summary>由宿主在 Start 或更晚阶段调用，以验证 Canvas 完成布局后的三块固定信息区。</summary>
        public void ValidateAssistantPanelLayout()
        {
            Canvas.ForceUpdateCanvases();
            var resultPanel = bindings.ResultRoot;
            var netlistPanel = bindings.NetlistRoot;
            var diagnosticPanel = bindings.DiagnosticRoot;
            if (resultPanel.rect.height <= 0f || netlistPanel.rect.height <= 0f || diagnosticPanel.rect.height <= 0f)
                throw new InvalidOperationException("Spice assistant panels require positive layout height.");
            if (RectsOverlap(resultPanel, netlistPanel) || RectsOverlap(netlistPanel, diagnosticPanel)) throw new InvalidOperationException("Spice assistant panels overlap.");
        }

        private void ValidateAssistantScrollStructure()
        {
            ValidateScrollableTextView(resultView, bindings.ResultRoot);
            ValidateScrollableTextView(netlistView, bindings.NetlistRoot);
            ValidateScrollableTextView(diagnosticView, bindings.DiagnosticRoot);
            if (resultView.ScrollRect.content == netlistView.ScrollRect.content || netlistView.ScrollRect.content == diagnosticView.ScrollRect.content || resultView.ScrollRect.content == diagnosticView.ScrollRect.content)
                throw new InvalidOperationException("Spice assistant panels must not share scroll content.");
        }

        private static void ValidateScrollableTextView(SpiceScrollableTextView view, RectTransform panel)
        {
            if (view.ScrollRect.viewport != view.Viewport || view.ScrollRect.content != view.Content || view.Viewport.GetComponent<RectMask2D>() == null || !view.Viewport.IsChildOf(panel) || !view.Text.transform.IsChildOf(view.Content))
                throw new InvalidOperationException("Spice assistant scroll view bindings are incomplete.");
            if (!RectContains(panel, view.Viewport)) throw new InvalidOperationException("Spice assistant viewport extends outside its panel.");
        }

        private static bool RectsOverlap(RectTransform left, RectTransform right)
        {
            GetWorldBounds(left, out var leftMin, out var leftMax);
            GetWorldBounds(right, out var rightMin, out var rightMax);
            return leftMin.x < rightMax.x && leftMax.x > rightMin.x && leftMin.y < rightMax.y && leftMax.y > rightMin.y;
        }

        private static bool RectContains(RectTransform outer, RectTransform inner)
        {
            GetWorldBounds(outer, out var outerMin, out var outerMax);
            GetWorldBounds(inner, out var innerMin, out var innerMax);
            return innerMin.x >= outerMin.x && innerMax.x <= outerMax.x && innerMin.y >= outerMin.y && innerMax.y <= outerMax.y;
        }

        private static void GetWorldBounds(RectTransform rect, out Vector2 min, out Vector2 max)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            min = corners[0];
            max = corners[2];
        }


        private void RefreshParameterPanel()
        {
            SetAcPhaseControlsVisible(false);
            SetOpAmpInfoVisible(false);
            SetNormalParameterControlsVisible(true);
            if (parameterApplyButton != null) parameterApplyButton.interactable = false;
            if (selectedComponent == null || selectedComponent.Kind == SpiceComponentKind.Ground)
            {
                ClearParameterPanel();
                return;
            }
            currentUnits = SpiceParameterUnits.UnitsFor(selectedComponent.Kind);
            if (selectedComponent.Kind == SpiceComponentKind.AcVoltageSource)
            {
                var isRunning = ResultState == SpiceWorkspaceResultState.Running;
                parameterTitle.text = selectedComponent.InstanceId + " AC 参数设置";
                parameterInput.text = selectedComponent.Data.SiValue.ToString("G6", CultureInfo.InvariantCulture);
                parameterInput.interactable = !isRunning;
                unitButton.interactable = false;
                unitLabel.text = "V";
                acPhaseInput.text = selectedComponent.Data.AcPhaseDegrees.ToString("G6", CultureInfo.InvariantCulture);
                SetAcPhaseControlsVisible(true);
                acPhaseInput.interactable = !isRunning;
                if (parameterApplyButton != null) parameterApplyButton.interactable = !isRunning;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.IdealOperationalAmplifier)
            {
                // 这个固定 VCVS 没有可编辑参数；视图只呈现 Core 模型边界，任何电气改动仍须由 Controller 编排 Model API。
                parameterTitle.text = selectedComponent.InstanceId + " 理想运算放大器（线性）";
                parameterInput.gameObject.SetActive(false);
                unitButton.gameObject.SetActive(false);
                if (parameterApplyButton != null) parameterApplyButton.gameObject.SetActive(false);
                opAmpInfoText.text = "固定开环增益：1e6\n端子：IN+、IN-、OUT\n线性理想模型，无电源引脚和饱和限制\n无可编辑参数";
                SetOpAmpInfoVisible(true);
                return;
            }
            SetNormalParameterControlsVisible(true);
            if (selectedComponent.Kind == SpiceComponentKind.SiliconDiode)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = "固定通用硅模型";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.VoltageProbe)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = "差分电压测量";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.CurrentProbe)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = "串联电流测量";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            if (selectedComponent.Kind == SpiceComponentKind.IdealSwitch)
            {
                parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
                parameterInput.text = selectedComponent.Data.SiValue > 0.5d ? "闭合（双击器件可切换）" : "断开（双击器件可切换）";
                parameterInput.interactable = false;
                unitButton.interactable = false;
                return;
            }
            parameterInput.interactable = ResultState != SpiceWorkspaceResultState.Running;
            unitButton.interactable = ResultState != SpiceWorkspaceResultState.Running;
            if (parameterApplyButton != null) parameterApplyButton.interactable = ResultState != SpiceWorkspaceResultState.Running;
            unitIndex = 0;
            parameterTitle.text = selectedComponent.InstanceId + " 参数设置";
            parameterInput.interactable = ResultState != SpiceWorkspaceResultState.Running;
            unitButton.interactable = ResultState != SpiceWorkspaceResultState.Running;
            parameterInput.text = SpiceParameterUnits.FromSi(selectedComponent.Kind, selectedComponent.Data.SiValue, currentUnits[unitIndex]).ToString("G6", CultureInfo.InvariantCulture);
            unitLabel.text = currentUnits[unitIndex];
        }

        private void ClearParameterPanel()
        {
            SetNormalParameterControlsVisible(true);
            SetOpAmpInfoVisible(false);
            parameterTitle.text = "参数设置";
            parameterInput.text = string.Empty;
            parameterInput.interactable = false;
            unitButton.interactable = false;
            unitLabel.text = "-";
            SetAcPhaseControlsVisible(false);
            if (parameterApplyButton != null) parameterApplyButton.interactable = false;
        }

        private void SetAcPhaseControlsVisible(bool visible)
        {
            if (acPhaseLabel != null) acPhaseLabel.gameObject.SetActive(visible);
            if (acPhaseInput != null) acPhaseInput.gameObject.SetActive(visible);
            var phaseUnit = acPhaseLabel != null ? acPhaseLabel.transform.parent.Find("AcPhaseUnit") : null;
            if (phaseUnit != null) phaseUnit.gameObject.SetActive(visible);
        }

        private void SetOpAmpInfoVisible(bool visible)
        {
            if (opAmpInfoText != null) opAmpInfoText.gameObject.SetActive(visible);
        }

        private void SetNormalParameterControlsVisible(bool visible)
        {
            if (parameterInput != null) parameterInput.gameObject.SetActive(visible);
            if (unitButton != null) unitButton.gameObject.SetActive(visible);
            if (parameterApplyButton != null) parameterApplyButton.gameObject.SetActive(visible);
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
            if (selectedComponent != null && selectedComponent.Kind == SpiceComponentKind.AcVoltageSource)
            {
                if (!double.TryParse(parameterInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var magnitudeVolts) ||
                    !double.TryParse(acPhaseInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var phaseDegrees) ||
                    !TrySetAcVoltageSourceParameters(selectedComponent.InstanceId, magnitudeVolts, phaseDegrees))
                {
                    statusText.text = "AC 小信号幅值或相位无效。";
                    RefreshParameterPanel();
                    return;
                }
                RefreshParameterPanel();
                return;
            }
            if (selectedComponent == null || currentUnits.Length == 0 || !TryApplyParameterText(selectedComponent.InstanceId, parameterInput.text, currentUnits[unitIndex], out _))
            {
                statusText.text = "参数无效";
                return;
            }
            RefreshParameterPanel();
        }

    }
}
