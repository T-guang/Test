#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.UI
{
    /// <summary>
    /// Unity Editor 专用的系统模板布局回写工具。
    /// 在确认当前画布仍与已加载系统模板的元件和导线结构一致后，仅把位置、开关显示状态、导线路由和导线颜色写回源 JSON，并先创建备份。
    /// 不面向用户保存图纸，不应修改元件类型、端子关系、参数语义、规则定义或运行态判断；正式构建环境不包含本类。
    /// 修改后必须回归布局更新确认流程、重新加载模板和 18 张真实模板基线。
    /// </summary>
    public static class SystemTemplateLayoutUpdater
    {
        private const string LogPrefix = "[SystemTemplateLayoutUpdater] ";

        public static bool UpdateTemplateLayout(WorkspaceController workspace, out string message)
        {
            // 只有 TemplateEditSession 标记的系统模板才能写回 Resources 源文件，避免把学习者当前画布误写为正式模板。
            if (!TemplateEditSession.HasSystemTemplateLoaded)
            {
                message = "当前画布不是系统模板，无法更新模板布局。";
                Debug.LogWarning(LogPrefix + message);
                return false;
            }

            if (workspace == null)
            {
                message = "WorkspaceController 为空。";
                Debug.LogWarning(LogPrefix + message);
                return false;
            }

            var resourcePath = TemplateEditSession.CurrentResourcePath;
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                message = "未找到原模板路径。";
                Debug.LogWarning(LogPrefix + message);
                return false;
            }

            var absolutePath = GetTemplateAbsolutePath(resourcePath);
            var relativeAssetPath = GetTemplateRelativeAssetPath(resourcePath);

            if (!File.Exists(absolutePath))
            {
                message = "找不到原模板物理文件：" + absolutePath;
                Debug.LogWarning(LogPrefix + message);
                return false;
            }

            var currentComponents = workspace.Components;
            var currentWires = workspace.WireManager != null ? workspace.WireManager.Wires : null;

            string json;
            try
            {
                json = File.ReadAllText(absolutePath);
            }
            catch (Exception exception)
            {
                message = "读取原模板失败：" + exception.Message;
                Debug.LogException(exception);
                return false;
            }

            CircuitTemplateDto originalDto;
            try
            {
                originalDto = JsonUtility.FromJson<CircuitTemplateDto>(json);
            }
            catch (Exception exception)
            {
                message = "解析原模板失败：" + exception.Message;
                Debug.LogException(exception);
                return false;
            }

            if (originalDto == null)
            {
                message = "解析原模板为空。";
                Debug.LogWarning(LogPrefix + message);
                return false;
            }

            // 先比较结构再备份和写回；布局维护不能借机新增、删除或重接元件与导线。
            if (!ValidateStructure(originalDto, currentComponents, currentWires, out var validationError))
            {
                message = "当前画布结构已改变，无法更新系统模板布局。\n" + validationError;
                Debug.LogWarning(LogPrefix + "ValidateStructure failed: " + validationError);
                return false;
            }

            var backupPath = CreateBackup(absolutePath, out var backupError);
            if (string.IsNullOrWhiteSpace(backupPath))
            {
                message = backupError;
                Debug.LogWarning(LogPrefix + message);
                return false;
            }

            // 备份完成后才更新允许回写的布局字段，随后重新导入该 Assets 资源。
            UpdateLayoutFields(originalDto, currentComponents, currentWires);

            var newJson = JsonUtility.ToJson(originalDto, true);
            try
            {
                File.WriteAllText(absolutePath, newJson);
            }
            catch (Exception exception)
            {
                message = "覆盖原模板失败：" + exception.Message;
                Debug.LogException(exception);
                return false;
            }

            string verifyJson;
            try
            {
                verifyJson = File.ReadAllText(absolutePath);
            }
            catch (Exception exception)
            {
                message = "写回后重新读取模板失败：" + exception.Message;
                Debug.LogException(exception);
                return false;
            }

            var verifyDto = JsonUtility.FromJson<CircuitTemplateDto>(verifyJson);

            AssetDatabase.ImportAsset(relativeAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            message = $"更新模板布局成功。\nJSON 路径：{absolutePath}\n备份：{backupPath}";
            return true;
        }

        private static string GetTemplateAbsolutePath(string resourcePath)
        {
            return Path.Combine(Application.dataPath, "Resources", resourcePath + ".json").Replace("\\", "/");
        }

        private static string GetTemplateRelativeAssetPath(string resourcePath)
        {
            return ("Assets/Resources/" + resourcePath + ".json").Replace("\\", "/");
        }

        private static string CreateBackup(string absolutePath, out string error)
        {
            error = null;
            var backupDir = Path.Combine(Path.GetDirectoryName(absolutePath) ?? Application.dataPath, "Backups");

            try
            {
                Directory.CreateDirectory(backupDir);
            }
            catch (Exception exception)
            {
                error = "创建备份目录失败：" + exception.Message;
                return null;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = Path.GetFileNameWithoutExtension(absolutePath);
            var backupPath = Path.Combine(backupDir, $"{fileName}_backup_{timestamp}.json").Replace("\\", "/");

            try
            {
                File.Copy(absolutePath, backupPath, false);
                return backupPath;
            }
            catch (Exception exception)
            {
                error = "备份原模板失败，已终止写回：" + exception.Message;
                return null;
            }
        }

        private static bool ValidateStructure(
            CircuitTemplateDto originalDto,
            IReadOnlyList<CircuitComponent> currentComponents,
            IReadOnlyList<WireView> currentWires,
            out string reason)
        {
            reason = null;

            if (originalDto.components == null)
            {
                reason = "原模板 components 为空。";
                return false;
            }

            if (originalDto.wires == null)
            {
                reason = "原模板 wires 为空。";
                return false;
            }

            if (currentComponents == null)
            {
                reason = "当前画布元件列表为空。";
                return false;
            }

            if (currentWires == null)
            {
                reason = "当前画布导线列表为空。";
                return false;
            }

            if (originalDto.components.Count != currentComponents.Count)
            {
                reason = $"元件数量不一致：原模板 {originalDto.components.Count}，当前画布 {currentComponents.Count}";
                return false;
            }

            foreach (var component in currentComponents)
            {
                var match = originalDto.components.Find(item => item.instanceId == component.InstanceId);
                if (match == null)
                {
                    reason = "找不到 instanceId：" + component.InstanceId;
                    return false;
                }

                var currentDefinitionName = component.Definition != null ? component.Definition.name : string.Empty;
                if (match.definitionName != currentDefinitionName)
                {
                    reason = $"definitionName 不一致：instanceId={component.InstanceId}，原 {match.definitionName}，当前 {currentDefinitionName}";
                    return false;
                }
            }

            if (originalDto.wires.Count != currentWires.Count)
            {
                reason = $"导线数量不一致：原模板 {originalDto.wires.Count}，当前画布 {currentWires.Count}";
                return false;
            }

            foreach (var wire in currentWires)
            {
                if (!FindMatchingWire(originalDto.wires, wire))
                {
                    reason = "找不到导线连接：" + GetWireSignature(wire);
                    return false;
                }
            }

            return true;
        }

        private static bool FindMatchingWire(List<TemplateWireDto> originalWires, WireView wire)
        {
            var startComp = wire.StartTerminal.Owner.InstanceId;
            var startTerm = wire.StartTerminal.TerminalId;
            var endComp = wire.EndTerminal.Owner.InstanceId;
            var endTerm = wire.EndTerminal.TerminalId;

            foreach (var originalWire in originalWires)
            {
                var forwardMatch =
                    originalWire.startComponentId == startComp &&
                    originalWire.startTerminalId == startTerm &&
                    originalWire.endComponentId == endComp &&
                    originalWire.endTerminalId == endTerm;
                var reverseMatch =
                    originalWire.startComponentId == endComp &&
                    originalWire.startTerminalId == endTerm &&
                    originalWire.endComponentId == startComp &&
                    originalWire.endTerminalId == startTerm;

                if (forwardMatch || reverseMatch)
                {
                    return true;
                }
            }

            return false;
        }

        private static void UpdateLayoutFields(
            CircuitTemplateDto originalDto,
            IReadOnlyList<CircuitComponent> currentComponents,
            IReadOnlyList<WireView> currentWires)
        {
            // ValidateStructure 已保证 instanceId、definitionName 和导线端点匹配；这里故意不改模板的结构性字段或元件参数。
            foreach (var component in currentComponents)
            {
                var match = originalDto.components.Find(item => item.instanceId == component.InstanceId);
                if (match == null)
                {
                    continue;
                }

                var position = component.GetComponent<RectTransform>().anchoredPosition;
                match.x = position.x;
                match.y = position.y;
                match.isClosed = component.IsClosed;
            }

            foreach (var wire in currentWires)
            {
                var match = FindWireDto(originalDto.wires, wire, out var isReverse);
                if (match == null)
                {
                    continue;
                }

                var points = wire.ManualRoutePoints;
                if (points != null && points.Count > 0)
                {
                    var copiedPoints = new List<Vector2>(points);
                    if (isReverse)
                    {
                        copiedPoints.Reverse();
                    }

                    match.manualRoutePoints = copiedPoints;
                }
                else
                {
                    match.manualRoutePoints = new List<Vector2>();
                }

                match.manualRouteHorizontal = wire.ManualRouteHorizontal;
                match.manualRouteAxis = wire.ManualRouteAxis;
                match.color = "#" + ColorUtility.ToHtmlStringRGB(wire.WireColor);
            }
        }

        private static TemplateWireDto FindWireDto(List<TemplateWireDto> wires, WireView wire, out bool isReverse)
        {
            isReverse = false;
            var startComp = wire.StartTerminal.Owner.InstanceId;
            var startTerm = wire.StartTerminal.TerminalId;
            var endComp = wire.EndTerminal.Owner.InstanceId;
            var endTerm = wire.EndTerminal.TerminalId;

            foreach (var item in wires)
            {
                var forwardMatch =
                    item.startComponentId == startComp &&
                    item.startTerminalId == startTerm &&
                    item.endComponentId == endComp &&
                    item.endTerminalId == endTerm;
                if (forwardMatch)
                {
                    return item;
                }

                var reverseMatch =
                    item.startComponentId == endComp &&
                    item.startTerminalId == endTerm &&
                    item.endComponentId == startComp &&
                    item.endTerminalId == startTerm;
                if (reverseMatch)
                {
                    isReverse = true;
                    return item;
                }
            }

            return null;
        }

        private static string GetWireSignature(WireView wire)
        {
            if (wire == null || wire.StartTerminal == null || wire.EndTerminal == null)
            {
                return "未知导线";
            }

            return $"{wire.StartTerminal.Owner.InstanceId}.{wire.StartTerminal.TerminalId} -> {wire.EndTerminal.Owner.InstanceId}.{wire.EndTerminal.TerminalId}";
        }
    }
}
#endif
