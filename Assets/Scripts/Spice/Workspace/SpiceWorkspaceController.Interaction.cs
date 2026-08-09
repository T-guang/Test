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
    // Interaction partial 只处理用户编辑输入：选择、放置、拖动、接线与删除意图，并委托共享 controller/model 更新电路事实。
    // 它不保存图纸、不格式化仿真结果、不运行求解；任何编辑完成后必须经统一变更通知使旧结果进入 stale 语义。
    public sealed partial class SpiceWorkspaceController
    {
        /// <summary>保留给验证 Harness 的固定位置创建入口；元件池交互改由拖放入口使用。</summary>
        public SpiceWorkspaceComponentData CreateComponent(SpiceComponentKind kind)
        {
            EnsureInitialized();
            var offset = new Vector2(-120f + componentViews.Count * 32f, 90f - componentViews.Count * 24f);
            return CreateComponent(kind, offset);
        }

        public SpiceWorkspaceComponentData CreateComponent(SpiceComponentKind kind, Vector2 position)
        {
            EnsureInitialized();
            // 未激活根节点下的 Content 尚未同步到真实 Viewport。此时拒绝落点，
            // 避免 Clamp 把新元件错误写到零尺寸画布的伪边界。
            if ((!workspaceGeometryReady && !SynchronizeWorkspaceGeometry()) || !CanModifyElectricalModel()) return null;
            var data = Model.AddComponent(kind, ClampToWorkspace(kind, position));
            CreateComponentView(data);
            SelectComponent(componentViews[data.InstanceId]);
            return data;
        }

        public void SelectPaletteKind(SpiceComponentKind kind)
        {
            if (!IsPaletteKindAvailable(kind)) return;
            paletteKind = kind;
            statusText.text = "拖动元件到画布以放置";
        }

        public void BeginPaletteDrag(SpiceComponentKind kind, Vector2 screenPosition, Camera eventCamera)
        {
            EnsureInitialized();
            if ((!workspaceGeometryReady && !SynchronizeWorkspaceGeometry()) || !CanModifyElectricalModel() || !IsPaletteKindAvailable(kind)) return;
            CancelPendingWire();
            CancelPaletteDrag();
            paletteKind = kind;
            paletteDragActive = true;
            palettePreview.gameObject.SetActive(true);
            palettePreview.GetComponentInChildren<Text>().text = PaletteLabel(kind);
            UpdatePaletteDrag(screenPosition, eventCamera);
        }

        public void UpdatePaletteDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!workspaceGeometryReady || !paletteDragActive) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(WorkspaceRect, screenPosition, eventCamera, out var local);
            palettePreview.rectTransform.anchoredPosition = local;
            palettePreview.color = IsPointerInsideViewport(screenPosition, eventCamera)
                ? new Color(0.15f, 0.39f, 0.92f, 0.22f)
                : new Color(0.39f, 0.45f, 0.55f, 0.16f);
        }

        public void EndPaletteDrag(Vector2 screenPosition, Camera eventCamera)
        {
            if (!paletteDragActive) return;
            if (IsPointerInsideViewport(screenPosition, eventCamera) && TryScreenToWorkspace(screenPosition, eventCamera, out var local))
            {
                CreateComponent(paletteKind, local);
            }
            CancelPaletteDrag();
        }

        public bool TryScreenToWorkspace(Vector2 screenPosition, Camera eventCamera, out Vector2 localPosition)
        {
            if (!workspaceGeometryReady)
            {
                localPosition = Vector2.zero;
                return false;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(WorkspaceRect, screenPosition, eventCamera, out localPosition);
        }

        private bool IsPointerInsideViewport(Vector2 screenPosition, Camera eventCamera)
        {
            return viewportRect != null && RectTransformUtility.RectangleContainsScreenPoint(viewportRect, screenPosition, eventCamera);
        }

        public bool IsViewNavigationActive => viewController != null && viewController.IsPanning;

        public bool ConsumeViewNavigationClick(PointerEventData eventData)
        {
            return viewController != null && viewController.ConsumeNavigationClick(eventData);
        }

        public void UpdateZoomControlState(float scale)
        {
            if (zoomOutButton != null) zoomOutButton.interactable = scale > 0.4001f;
            if (zoomInButton != null) zoomInButton.interactable = scale < 1.9999f;
        }

        public bool Connect(string startComponentId, string startTerminalId, string endComponentId, string endTerminalId, SpiceWireVisualState visualState = null)
        {
            EnsureInitialized();
            if (!CanModifyElectricalModel()) return false;
            if (!Model.AddWire(startComponentId, startTerminalId, endComponentId, endTerminalId, visualState)) return false;
            var wire = Model.Wires[Model.Wires.Count - 1];
            wireViews.Add(new SpiceWorkspaceWireView(this, wire, componentViews[startComponentId], componentViews[endComponentId]));
            return true;
        }

        public bool TrySetParameter(string instanceId, double displayValue, string unit)
        {
            EnsureInitialized();
            if (!CanModifyElectricalModel()) return false;
            var component = Model.FindComponent(instanceId);
            if (component == null || !SpiceParameterUnits.TryToSi(component.Kind, displayValue, unit, out var siValue)) return false;
            if (!Model.TrySetParameter(instanceId, siValue)) return false;
            componentViews[instanceId].RefreshAnnotation();
            if (selectedComponent != null && string.Equals(selectedComponent.InstanceId, instanceId, StringComparison.Ordinal))
            {
                RefreshParameterPanel();
            }
            return true;
        }

        public bool TrySetSwitchState(string instanceId, bool closed)
        {
            if (!CanModifyElectricalModel()) return false;
            var component = Model.FindComponent(instanceId);
            if (component == null || component.Kind != SpiceComponentKind.IdealSwitch || !Model.TrySetParameter(instanceId, closed ? 1d : 0d))
            {
                return false;
            }

            componentViews[instanceId].RefreshVisualState();
            if (selectedComponent != null && string.Equals(selectedComponent.InstanceId, instanceId, StringComparison.Ordinal)) RefreshParameterPanel();
            if (statusText != null) statusText.text = "开关状态已更新，请重新运行计算。";
            return true;
        }

        public bool TryApplyParameterText(string instanceId, string rawValue, string unit, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                error = "请输入参数值。";
                return false;
            }

            if (!double.TryParse(rawValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !TrySetParameter(instanceId, value, unit))
            {
                error = "参数无效，请输入当前器件支持范围内的数值。";
                return false;
            }

            if (statusText != null)
            {
                statusText.text = "参数已更新，请重新运行计算。";
            }

            return true;
        }


        public void RotateSelection() => RotateSelectedComponent();
        public void ClearAll() => ClearWorkspace();
        public void SelectComponent(SpiceWorkspaceComponentView component)
        {
            if (pendingComponent != null && pendingComponent != component) CancelPendingWire();
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedWire = null;
            selectedComponent = component;
            if (selectedComponent != null)
            {
                selectedComponent.SetSelected(true);
                RefreshParameterPanel();
            }
            UpdateRotateAvailability();
        }

        public void HandleComponentPointerClick(SpiceWorkspaceComponentView component, PointerEventData eventData)
        {
            if (component == null || eventData == null || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (HasPendingWire)
            {
                if (statusText != null) statusText.text = "请先完成或取消当前接线。";
                return;
            }

            SelectComponent(component);
            if (eventData.clickCount < 2 || eventData.dragging)
            {
                return;
            }

            if (ResultState == SpiceWorkspaceResultState.Running)
            {
                if (statusText != null) statusText.text = "仿真计算进行中，请稍后编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.IdealSwitch)
            {
                TrySetSwitchState(component.InstanceId, component.Data.SiValue <= 0.5d);
                return;
            }

            if (component.Kind == SpiceComponentKind.SiliconDiode)
            {
                if (statusText != null) statusText.text = "通用硅二极管使用固定模型，无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.VoltageProbe)
            {
                if (statusText != null) statusText.text = "电压探针无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.CurrentProbe)
            {
                if (statusText != null) statusText.text = "电流探针无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.Ground)
            {
                if (statusText != null) statusText.text = "该器件无可编辑参数。";
                return;
            }

            if (component.Kind == SpiceComponentKind.GenericNpnBjt || component.Kind == SpiceComponentKind.GenericPnpBjt)
            {
                if (statusText != null) statusText.text = component.Kind == SpiceComponentKind.GenericNpnBjt
                    ? "通用 NPN 三极管使用固定教学模型（NPN_GENERIC），无可编辑参数。"
                    : "通用 PNP 三极管使用固定教学模型（PNP_GENERIC），无可编辑参数。";
                return;
            }

            ParameterDialogRequested?.Invoke(component.Data);
        }

        public void SelectWire(SpiceWorkspaceWireView wire)
        {
            if (pendingComponent != null) CancelPendingWire();
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = wire;
            wire.SetSelected(true);
            parameterTitle.text = "已选择导线";
            parameterInput.interactable = false;
            unitButton.interactable = false;
            UpdateRotateAvailability();
        }

        public void ClearSelection()
        {
            if (selectedComponent != null) selectedComponent.SetSelected(false);
            if (selectedWire != null) selectedWire.SetSelected(false);
            selectedComponent = null;
            selectedWire = null;
            ClearParameterPanel();
            UpdateRotateAvailability();
        }

        public void HandleTerminalClick(SpiceWorkspaceComponentView component, string terminalId)
        {
            if (pendingComponent == null)
            {
                if (ResultState == SpiceWorkspaceResultState.Running) return;
                pendingComponent = component;
                pendingTerminalId = terminalId;
                pendingNextSegmentHorizontal = SpiceWorkspaceOrthogonalRoute.IsHorizontal(component.GetTerminalDirection(terminalId));
                wirePreview = new SpiceWorkspaceWirePreview(this);
                statusText.text = "请选择第二个端子，Esc 或点击空白取消";
                return;
            }
            if (!IsPendingTargetValid(component, terminalId))
            {
                statusText.text = "该端子不能与起始元件直接连接";
                return;
            }
            var route = pendingWaypoints.Count == 0 ? SpiceWireVisualState.Auto() : SpiceWireVisualState.Manual(pendingWaypoints);
            if (!Connect(pendingComponent.InstanceId, pendingTerminalId, component.InstanceId, terminalId, route))
            {
                statusText.text = "无法建立该导线";
                return;
            }
            CancelPendingWire();
        }

        public void HandleTerminalHover(SpiceWorkspaceComponentView component, string terminalId, bool entered)
        {
            if (highlightedComponent != null)
            {
                highlightedComponent.SetTerminalHighlighted(highlightedTerminalId, SpiceTerminalHighlightState.None);
                highlightedComponent = null;
                highlightedTerminalId = null;
            }
            if (!entered || pendingComponent == null) return;
            highlightedComponent = component;
            highlightedTerminalId = terminalId;
            component.SetTerminalHighlighted(terminalId, IsPendingTargetValid(component, terminalId) ? SpiceTerminalHighlightState.Valid : SpiceTerminalHighlightState.Invalid);
        }

        public void HandleWorkspacePointerClick(PointerEventData eventData)
        {
            if (ConsumeViewNavigationClick(eventData)) return;
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                UndoPendingWaypoint();
                return;
            }
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (pendingComponent == null)
            {
                ClearSelection();
                return;
            }
            if (!TryScreenToWorkspace(eventData.position, eventData.pressEventCamera, out var pointer))
            {
                CancelPendingWire();
                return;
            }
            AddPendingWaypoint(pointer);
        }

        public void MoveComponent(string instanceId, Vector2 position)
        {
            Model.MoveComponent(instanceId, position);
            foreach (var wire in wireViews) wire.Refresh();
            RefreshWirePreview();
        }

        public void RotateSelectedComponent()
        {
            if (selectedComponent == null) return;
            selectedComponent.RotateClockwise();
            foreach (var wire in wireViews) wire.Refresh();
            RefreshWirePreview();
        }

        public void DeleteSelection()
        {
            if (!CanModifyElectricalModel()) return;
            if (selectedComponent != null)
            {
                var id = selectedComponent.InstanceId;
                if (pendingComponent == selectedComponent) CancelPendingWire();
                SpiceUnityObjectLifetime.Destroy(selectedComponent.gameObject);
                componentViews.Remove(id);
                wireViews.Where(wire => wire.Data.StartComponentId == id || wire.Data.EndComponentId == id).ToList().ForEach(RemoveWireView);
                Model.RemoveComponent(id);
                selectedComponent = null;
                ClearParameterPanel();
                UpdateRotateAvailability();
                return;
            }
            if (selectedWire != null)
            {
                RemoveWireView(selectedWire);
                selectedWire = null;
                ClearParameterPanel();
                UpdateRotateAvailability();
            }
        }

        public void ClearWorkspace()
        {
            if (!CanModifyElectricalModel()) return;
            CancelPendingWire();
            foreach (var wire in wireViews.ToList()) wire.Destroy();
            wireViews.Clear();
            foreach (var view in componentViews.Values) SpiceUnityObjectLifetime.Destroy(view.gameObject);
            componentViews.Clear();
            selectedComponent = null;
            selectedWire = null;
            Model.Clear();
            Model.ResetInstanceNaming();
            generatedNetlistContent = null;
            generatedNetlistRevision = -1;
            lastOutcomeText = null;
            ResultState = SpiceWorkspaceResultState.NeverRun;
            SetResultText(string.Empty);
            SetDiagnosticText(string.Empty);
            if (statusText != null) statusText.text = StateMessage();
            ClearParameterPanel();
            RefreshAnalysisControls();
            UpdateRotateAvailability();
            RefreshNetlistUi();
            RefreshCopyResultButton();
            // 清空 SPICE 画布后重置为 100% 和初始中心
            if (viewController != null) viewController.ResetView();
            // 清空画布成功后清除当前会话文件路径（仅在 ClearWorkspace 末尾调用一次，
            // 不在清空按钮 UI 回调中复制清除路径逻辑）。
            ClearCurrentSpiceFilePath();
            isDirty = false;
        }

        /// <summary>
        /// 分析设置必须通过 Model 变更，以便 D1 revision 保护使在途计算结果失效。
        /// 工作区 UI 只转发此正式入口，不能直接修改模型字段或重复推进 revision。
        /// </summary>
        public bool TrySetAnalysisMode(SpiceAnalysisMode mode)
        {
            EnsureInitialized();
            return CanModifyElectricalModel() && Model.TrySetAnalysisMode(mode);
        }

        public bool TrySetAcFrequency(double frequencyHz)
        {
            EnsureInitialized();
            return CanModifyElectricalModel() && Model.TrySetAcFrequency(frequencyHz);
        }

        public bool TrySetAcVoltageSourceParameters(string instanceId, double magnitudeVolts, double phaseDegrees)
        {
            EnsureInitialized();
            if (!CanModifyElectricalModel() || !Model.TrySetAcVoltageSourceParameters(instanceId, magnitudeVolts, phaseDegrees)) return false;
            if (componentViews.TryGetValue(instanceId, out var view)) view.RefreshAnnotation();
            if (selectedComponent != null && string.Equals(selectedComponent.InstanceId, instanceId, StringComparison.Ordinal)) RefreshParameterPanel();
            return true;
        }

        internal SpiceWorkspaceComponentView GetComponentViewForTesting(string instanceId)
        {
            componentViews.TryGetValue(instanceId, out var view);
            return view;
        }


        public void CancelPendingWire()
        {
            if (highlightedComponent != null) highlightedComponent.SetTerminalHighlighted(highlightedTerminalId, SpiceTerminalHighlightState.None);
            highlightedComponent = null;
            highlightedTerminalId = null;
            pendingComponent = null;
            pendingTerminalId = null;
            pendingWaypoints.Clear();
            if (wirePreview != null)
            {
                wirePreview.Destroy();
                wirePreview = null;
            }
            if (initialized && ResultState != SpiceWorkspaceResultState.Running) statusText.text = StateMessage();
        }

        private void CancelPaletteDrag()
        {
            paletteDragActive = false;
            if (palettePreview != null) palettePreview.gameObject.SetActive(false);
        }

        private void RefreshWirePreview()
        {
            if (pendingComponent != null && wirePreview != null && TryScreenToWorkspace(Input.mousePosition, null, out var pointer))
            {
                var visualState = pendingWaypoints.Count == 0 ? SpiceWireVisualState.Auto() : SpiceWireVisualState.Manual(pendingWaypoints);
                wirePreview.Refresh(SpiceWorkspaceOrthogonalRoute.Build(
                    pendingComponent.GetTerminalPosition(pendingTerminalId),
                    pointer,
                    pendingComponent.GetTerminalDirection(pendingTerminalId),
                    visualState));
            }
        }

        private bool IsPendingTargetValid(SpiceWorkspaceComponentView component, string terminalId)
        {
            return ResultState != SpiceWorkspaceResultState.Running &&
                pendingComponent != null &&
                component != null &&
                component.Data.HasTerminal(terminalId) &&
                SpiceConnectionRules.IsConnectionAllowed(
                    pendingComponent.Kind,
                    pendingComponent.InstanceId,
                    pendingTerminalId,
                    component.Kind,
                    component.InstanceId,
                    terminalId);
        }

        private void AddPendingWaypoint(Vector2 pointer)
        {
            var anchor = pendingWaypoints.Count == 0 ? pendingComponent.GetTerminalPosition(pendingTerminalId) : pendingWaypoints[pendingWaypoints.Count - 1];
            var waypoint = SpiceWorkspaceOrthogonalRoute.ConstrainToAxis(anchor, pointer, pendingNextSegmentHorizontal);
            if ((waypoint - anchor).sqrMagnitude <= 0.0001f) return;
            pendingWaypoints.Add(waypoint);
            pendingNextSegmentHorizontal = !pendingNextSegmentHorizontal;
            RefreshWirePreview();
        }

        private void UndoPendingWaypoint()
        {
            if (pendingComponent == null) return;
            if (pendingWaypoints.Count == 0)
            {
                CancelPendingWire();
                return;
            }
            pendingWaypoints.RemoveAt(pendingWaypoints.Count - 1);
            pendingNextSegmentHorizontal = SpiceWorkspaceOrthogonalRoute.IsHorizontal(pendingComponent.GetTerminalDirection(pendingTerminalId));
            if (pendingWaypoints.Count % 2 != 0) pendingNextSegmentHorizontal = !pendingNextSegmentHorizontal;
            RefreshWirePreview();
        }

        private void UpdateRotateAvailability()
        {
            if (rotateButton != null) rotateButton.interactable = selectedComponent != null;
        }


        private Vector2 ClampToWorkspace(SpiceComponentKind kind, Vector2 position)
        {
            var size = SpiceWorkspaceComponentView.SizeFor(kind);
            var half = size * 0.5f;
            var bounds = WorkspaceRect.rect;
            return new Vector2(Mathf.Clamp(position.x, bounds.xMin + half.x, bounds.xMax - half.x), Mathf.Clamp(position.y, bounds.yMin + half.y, bounds.yMax - half.y));
        }

        /// <summary>
        /// 计算画布内容的边界（Content 局部坐标），用于"适配全部"。
        /// 边界来源：元件逻辑位置 + 旋转后视觉尺寸、Wire 两端、ManualRoutePoints。
        /// 不包含 pending Wire 预览、鼠标位置、选择装饰或整个 Content Rect。
        /// </summary>
        public Rect? ComputeContentBounds()
        {
            if (componentViews.Count == 0 && wireViews.Count == 0) return null;
            float? minX = null, minY = null, maxX = null, maxY = null;
            foreach (var component in Model.Components)
            {
                var size = SpiceWorkspaceComponentView.SizeFor(component.Kind);
                if (componentViews.TryGetValue(component.InstanceId, out var view))
                {
                    var rot = view.RotationQuarterTurns;
                    var visualSize = rot % 2 != 0 ? new Vector2(size.y, size.x) : size;
                    var half = visualSize * 0.5f;
                    ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, component.Position - half);
                    ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, component.Position + half);
                }
            }
            foreach (var wire in wireViews)
            {
                var startPos = wire.Data.StartComponentId != null && componentViews.TryGetValue(wire.Data.StartComponentId, out var startView)
                    ? startView.GetTerminalPosition(wire.Data.StartTerminalId) : Vector2.zero;
                var endPos = wire.Data.EndComponentId != null && componentViews.TryGetValue(wire.Data.EndComponentId, out var endView)
                    ? endView.GetTerminalPosition(wire.Data.EndTerminalId) : Vector2.zero;
                ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, startPos);
                ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, endPos);
                if (wire.Data.VisualState != null && wire.Data.VisualState.RouteMode == SpiceWireRouteMode.Manual)
                {
                    foreach (var waypoint in wire.Data.VisualState.Waypoints)
                        ExpandBounds(ref minX, ref minY, ref maxX, ref maxY, waypoint);
                }
            }
            if (!minX.HasValue) return null;
            return Rect.MinMaxRect(minX.Value, minY.Value, maxX.Value, maxY.Value);
        }

        private static void ExpandBounds(ref float? minX, ref float? minY, ref float? maxX, ref float? maxY, Vector2 point)
        {
            if (!minX.HasValue || point.x < minX.Value) minX = point.x;
            if (!maxX.HasValue || point.x > maxX.Value) maxX = point.x;
            if (!minY.HasValue || point.y < minY.Value) minY = point.y;
            if (!maxY.HasValue || point.y > maxY.Value) maxY = point.y;
        }

    }
}
