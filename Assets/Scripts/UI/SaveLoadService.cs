using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using ElectricalSim.Core;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 当前用户图纸保存、读取与恢复服务：保存目录固定在 Application.persistentDataPath/SavedBlueprints，不负责系统模板 Catalog、Resources 模板写回或弹窗视觉状态。
    /// 用户图纸保存活动 Workspace 的实例身份、布局、参数和导线端点；与系统模板维护生命周期不完全等价。修改后必须回归保存、读取、删除、外部导入和重新启动读取。
    /// 通过活动 WorkspaceController 和元件目录序列化、恢复用户图纸。
    /// 它不是标准模板加载器，必须保持图纸兼容性、实例 ID 和用户数据路径；
    /// 修改保存加载逻辑后必须覆盖模板与用户图纸回归。
    /// </summary>
    public sealed class SaveLoadService : MonoBehaviour
    {
        [SerializeField] private WorkspaceController workspace;
        [SerializeField] private List<ComponentDefinition> catalog = new List<ComponentDefinition>();

        public IReadOnlyList<ComponentDefinition> Catalog => catalog;

        public string SavedBlueprintDirectory => Path.Combine(Application.persistentDataPath, "SavedBlueprints");

        private string LegacySavePath => Path.Combine(Application.persistentDataPath, "electrical_demo_drawing.json");

        public void Initialize(WorkspaceController targetWorkspace, List<ComponentDefinition> definitions)
        {
            workspace = targetWorkspace;
            catalog = definitions;
        }

        public void Save()
        {
            SaveAs("未命名图纸");
        }

        public bool SaveAs(string documentName)
        {
            return SaveAs(documentName, false, out _, out _, out _);
        }

        public bool SaveAs(string documentName, bool overwrite, out SavedBlueprintInfo savedInfo, out bool exists, out string error)
        {
            savedInfo = null;
            error = null;
            exists = false;
            if (workspace == null)
            {
                error = "保存失败：工作区未初始化。";
                return false;
            }

            var safeName = SanitizeDocumentName(documentName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                error = "图纸名称不能为空。";
                return false;
            }

            try
            {
                // 仅创建 persistentDataPath 下的用户图纸目录；保存来源是 Workspace 的活动集合，不能扫描 Demo 场景中的历史对象。
                EnsureSaveDirectory();
                var path = CreateUniqueSavePath(safeName);
                
                if (!overwrite && File.Exists(path))
                {
                    exists = true;
                    error = "已存在同名文件";
                    return false;
                }

                // DTO 保存实例身份、布局、参数和导线端点，供恢复时按 instanceId 重建端子连接。
                var drawing = CreateDrawingDto();
                drawing.documentId = Guid.NewGuid().ToString("N");
                drawing.documentName = safeName;
                drawing.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                File.WriteAllText(path, JsonUtility.ToJson(drawing, true));
                savedInfo = CreateInfo(path, drawing);
                workspace.SetStatus(overwrite ? "已覆盖保存图纸：" + safeName : "已保存图纸：" + safeName);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = "保存失败：" + exception.Message;
                workspace.SetStatus(error);
                return false;
            }
        }

        public void Load()
        {
            if (!File.Exists(LegacySavePath))
            {
                workspace.SetStatus("未找到旧版保存图纸。请使用导入图纸选择已保存文件。");
                return;
            }

            LoadFromFile(LegacySavePath, out _);
        }

        public bool LoadFromFile(string filePath)
        {
            return LoadFromFile(filePath, out _);
        }

        public bool LoadFromFile(string filePath, out string error)
        {
            error = null;
            if (workspace == null)
            {
                error = "导入失败：工作区未初始化。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                error = "导入失败：图纸文件不存在。";
                workspace.SetStatus(error);
                return false;
            }

            try
            {
                var jsonContent = File.ReadAllText(filePath);
                return LoadFromJsonString(jsonContent, out error, filePath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = "导入失败：" + exception.Message;
                workspace.SetStatus(error);
                return false;
            }
        }

        public bool LoadFromJsonString(string jsonContent, out string error, string sourceFilePath = null)
        {
            error = null;
            if (workspace == null)
            {
                error = "导入失败：工作区未初始化。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                error = "导入失败：图纸内容为空。";
                workspace.SetStatus(error);
                return false;
            }

            DrawingDto drawing = null;
            try
            {
                drawing = JsonUtility.FromJson<DrawingDto>(jsonContent);
            }
            catch (Exception)
            {
                error = "导入失败：JSON格式错误。";
                workspace.SetStatus(error);
                return false;
            }

            if (drawing == null || drawing.components == null || drawing.wires == null)
            {
                error = "导入失败：不是本系统支持的图纸格式。";
                workspace.SetStatus(error);
                return false;
            }

            // 在清空当前画布前先检查所有元件定义与导线端子引用，避免明显无效的外部 JSON 覆盖学习者当前电路。
            foreach (var item in drawing.components)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.instanceId) || string.IsNullOrWhiteSpace(item.definitionName))
                {
                    error = "导入失败：图纸中存在无效元件。";
                    workspace.SetStatus(error);
                    return false;
                }

                var definition = catalog.Find(d => d.name == item.definitionName);
                if (definition == null)
                {
                    error = $"导入失败：找不到元件类型 '{item.definitionName}'。";
                    workspace.SetStatus(error);
                    return false;
                }
            }

            foreach (var item in drawing.wires)
            {
                if (item == null ||
                    string.IsNullOrWhiteSpace(item.startComponentId) ||
                    string.IsNullOrWhiteSpace(item.endComponentId) ||
                    string.IsNullOrWhiteSpace(item.startTerminalId) ||
                    string.IsNullOrWhiteSpace(item.endTerminalId))
                {
                    error = "导入失败：图纸中存在无效导线。";
                    workspace.SetStatus(error);
                    return false;
                }

                var startComp = drawing.components.Find(c => c.instanceId == item.startComponentId);
                var endComp = drawing.components.Find(c => c.instanceId == item.endComponentId);
                if (startComp == null || endComp == null)
                {
                    error = "导入失败：导线引用了不存在的元件。";
                    workspace.SetStatus(error);
                    return false;
                }

                var startDef = catalog.Find(d => d.name == startComp.definitionName);
                var endDef = catalog.Find(d => d.name == endComp.definitionName);

                if (startDef != null && startDef.terminals.Find(t => t.id == item.startTerminalId) == null)
                {
                    error = $"导入失败：元件 '{startDef.name}' 缺少端子 '{item.startTerminalId}'。";
                    workspace.SetStatus(error);
                    return false;
                }
                if (endDef != null && endDef.terminals.Find(t => t.id == item.endTerminalId) == null)
                {
                    error = $"导入失败：元件 '{endDef.name}' 缺少端子 '{item.endTerminalId}'。";
                    workspace.SetStatus(error);
                    return false;
                }
            }

            try
            {
                // 验证通过后才进入恢复流程；当前实现会报告生成异常，但不宣称对已清空画布执行完整事务回滚。
                ApplyDrawingDto(drawing);
                var docName = ResolveDocumentName(drawing, string.IsNullOrWhiteSpace(sourceFilePath) ? "外部导入图纸.json" : sourceFilePath);
                
                workspace.SetStatus($"外部图纸导入成功，可点击检查当前电路进行校验。\n已从外部 JSON 导入图纸：{docName}");
                
                // If it was a local file, we can optionally update status with the full path, but generic message is fine.
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = "导入失败：" + exception.Message;
                workspace.SetStatus(error);
                return false;
            }
        }

        public List<SavedBlueprintInfo> ListSavedBlueprints()
        {
            // 只枚举用户专用 SavedBlueprints 目录中的 JSON；单个文件读取失败只记录警告，不阻断其余用户图纸列表。
            EnsureSaveDirectory();
            var result = new List<SavedBlueprintInfo>();
            foreach (var filePath in Directory.GetFiles(SavedBlueprintDirectory, "*.json"))
            {
                try
                {
                    result.Add(CreateInfoFast(filePath));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"读取保存图纸失败：{filePath}\n{exception.Message}");
                }
            }

            return result.OrderByDescending(item => item.lastWriteTime).ToList();
        }

        public bool DeleteSavedBlueprint(string filePath)
        {
            return DeleteSavedBlueprint(filePath, out _);
        }

        public bool DeleteSavedBlueprint(string filePath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                error = "删除失败：图纸文件路径为空。";
                workspace?.SetStatus(error);
                return false;
            }

            try
            {
                EnsureSaveDirectory();
                // 删除前规范化路径并限制在用户图纸目录内，避免 UI 传入任意外部路径。
                var saveDirectory = Path.GetFullPath(SavedBlueprintDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var targetPath = Path.GetFullPath(filePath);

                if (!targetPath.StartsWith(saveDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    error = "删除失败：只能删除用户保存的图纸。";
                    workspace?.SetStatus(error);
                    return false;
                }

                if (!string.Equals(Path.GetExtension(targetPath), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    error = "删除失败：只能删除 json 图纸文件。";
                    workspace?.SetStatus(error);
                    return false;
                }

                if (!File.Exists(targetPath))
                {
                    error = "删除失败：图纸文件不存在。";
                    workspace?.SetStatus(error);
                    return false;
                }

                DrawingDto drawing = null;
                try
                {
                    drawing = JsonUtility.FromJson<DrawingDto>(File.ReadAllText(targetPath));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"读取待删除图纸信息失败：{targetPath}\n{exception.Message}");
                }

                var documentName = ResolveDocumentName(drawing, targetPath);
                File.Delete(targetPath);
                workspace?.SetStatus("已删除图纸：" + documentName);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = "删除失败：" + exception.Message;
                workspace?.SetStatus(error);
                return false;
            }
        }

        private DrawingDto CreateDrawingDto()
        {
            var drawing = new DrawingDto();

            // 只读取 Workspace.Components 与 WireManager.Wires 这两个活动画布权威集合。
            foreach (var component in workspace.Components)
            {
                drawing.components.Add(new ComponentDto
                {
                    instanceId = component.InstanceId,
                    definitionName = component.Definition.name,
                    x = ((RectTransform)component.transform).anchoredPosition.x,
                    y = ((RectTransform)component.transform).anchoredPosition.y,
                    isClosed = component.IsClosed,
                    parameters = component.CloneParameters()
                });
            }

            foreach (var wire in workspace.WireManager.Wires)
            {
                drawing.wires.Add(new WireDto
                {
                    startComponentId = wire.StartTerminal.Owner.InstanceId,
                    startTerminalId = wire.StartTerminal.TerminalId,
                    endComponentId = wire.EndTerminal.Owner.InstanceId,
                    endTerminalId = wire.EndTerminal.TerminalId,
                    color = ColorUtility.ToHtmlStringRGBA(wire.WireColor),
                    style = wire.Style.ToString(),
                    hasManualRoute = wire.HasManualRoute,
                    manualRouteHorizontal = wire.ManualRouteHorizontal,
                    manualRouteAxis = wire.ManualRouteAxis,
                    manualRoutePoints = new List<Vector2>(wire.ManualRoutePoints)
                });
            }

            return drawing;
        }

        private void ApplyDrawingDto(DrawingDto drawing)
        {
            if (drawing == null)
            {
                return;
            }

            // 元件必须先恢复，导线再按保存的 instanceId 与端子 ID 连接；该顺序不能调整。
            workspace.ClearDrawing();

            foreach (var item in drawing.components ?? new List<ComponentDto>())
            {
                var definition = catalog.Find(d => d.name == item.definitionName);
                if (definition == null)
                {
                    continue;
                }

                var component = workspace.SpawnComponent(definition, new Vector2(item.x, item.y), item.instanceId, false);
                component.SetClosed(item.isClosed);
                component.SetParameters(item.parameters);
            }

            foreach (var item in drawing.wires ?? new List<WireDto>())
            {
                var start = workspace.FindComponent(item.startComponentId)?.GetTerminal(item.startTerminalId);
                var end = workspace.FindComponent(item.endComponentId)?.GetTerminal(item.endTerminalId);
                var color = workspace != null ? workspace.ResolveAutoWireColor(start, end) : new Color(0.95f, 0.15f, 0.12f);
                TryParseWireColor(item.color, out color, color);
                var style = WireStyle.Orthogonal;
                Enum.TryParse(item.style, out style);
                var wire = workspace.WireManager.CreateWire(start, end, color, style);
                // 历史图纸缺少手动路由字段时保持 DTO 默认值并使用自动路径；不会在读取阶段改写原文件。
                if (wire != null && item.hasManualRoute)
                {
                    if (item.manualRoutePoints != null && item.manualRoutePoints.Count >= 2)
                    {
                        wire.SetManualRoutePoints(item.manualRoutePoints);
                    }
                    else
                    {
                        wire.SetManualRoute(item.manualRouteHorizontal, item.manualRouteAxis);
                    }
                }
            }

            // 恢复结束后刷新导线并标记拓扑变化，随后清空历史，避免把导入前画布混入新的撤销记录。
            workspace.WireManager.RefreshAll();
            workspace.MarkTopologyDirty();
            workspace.ClearHistory();
        }

        private static bool TryParseWireColor(string rawColor, out Color color, Color fallback)
        {
            color = fallback;
            if (string.IsNullOrWhiteSpace(rawColor))
            {
                return false;
            }

            var value = rawColor.Trim().Trim('"');
            if (ColorUtility.TryParseHtmlString(value, out color))
            {
                color.a = 1f;
                return true;
            }

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
            }

            if (!value.StartsWith("#", StringComparison.Ordinal) && IsHexColor(value))
            {
                if (ColorUtility.TryParseHtmlString("#" + value, out color))
                {
                    color.a = 1f;
                    return true;
                }
            }

            if (TryParseNamedWireColor(value, out color))
            {
                return true;
            }

            if (TryParseNumericWireColor(value, out color))
            {
                color.a = 1f;
                return true;
            }

            color = fallback;
            return false;
        }

        private static bool IsHexColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(2);
            }

            if (value.Length != 6 && value.Length != 8)
            {
                return false;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                var isHex = c >= '0' && c <= '9' ||
                    c >= 'a' && c <= 'f' ||
                    c >= 'A' && c <= 'F';
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryParseNamedWireColor(string value, out Color color)
        {
            color = Color.white;
            switch (value.Trim().ToLowerInvariant())
            {
                case "red":
                case "r":
                case "phase":
                case "火线":
                case "红":
                case "红色":
                    color = new Color(0.95f, 0.15f, 0.12f);
                    return true;
                case "blue":
                case "b":
                case "neutral":
                case "零线":
                case "蓝":
                case "蓝色":
                    color = new Color(0.10f, 0.45f, 0.95f);
                    return true;
                case "green":
                case "g":
                case "pe":
                case "earth":
                case "ground":
                case "地线":
                case "绿":
                case "绿色":
                    color = new Color(0.08f, 0.65f, 0.25f);
                    return true;
                case "yellow":
                case "y":
                case "黄":
                case "黄色":
                    color = new Color(0.95f, 0.78f, 0.12f);
                    return true;
                case "white":
                case "白":
                case "白色":
                    color = Color.white;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseNumericWireColor(string value, out Color color)
        {
            color = Color.white;
            value = value.Replace("rgba", string.Empty)
                .Replace("rgb", string.Empty)
                .Replace("Color", string.Empty)
                .Replace("RGBA", string.Empty)
                .Replace("RGB", string.Empty)
                .Replace("(", string.Empty)
                .Replace(")", string.Empty);

            var parts = value.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return false;
            }

            var values = new float[Mathf.Min(4, parts.Length)];
            for (var i = 0; i < values.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    return false;
                }
            }

            var scale = values[0] > 1f || values[1] > 1f || values[2] > 1f ? 255f : 1f;
            color = new Color(
                Mathf.Clamp01(values[0] / scale),
                Mathf.Clamp01(values[1] / scale),
                Mathf.Clamp01(values[2] / scale),
                values.Length >= 4 ? Mathf.Clamp01(values[3] / (values[3] > 1f ? 255f : 1f)) : 1f);
            return true;
        }

        private void EnsureSaveDirectory()
        {
            // 当前实现仅确保用户保存目录存在，不迁移旧文件，也不执行云备份或缓存清理。
            if (!Directory.Exists(SavedBlueprintDirectory))
            {
                Directory.CreateDirectory(SavedBlueprintDirectory);
            }
        }

        private string CreateUniqueSavePath(string safeName)
        {
            return Path.Combine(SavedBlueprintDirectory, safeName + ".json");
        }

        private static string SanitizeDocumentName(string documentName)
        {
            if (string.IsNullOrWhiteSpace(documentName))
            {
                return string.Empty;
            }

            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }));
            var chars = documentName.Trim().Where(c => !invalid.Contains(c)).ToArray();
            return new string(chars).Trim();
        }

        private static SavedBlueprintInfo CreateInfo(string filePath, DrawingDto drawing)
        {
            return new SavedBlueprintInfo
            {
                documentId = drawing != null ? drawing.documentId : string.Empty,
                documentName = ResolveDocumentName(drawing, filePath),
                savedAt = drawing != null && !string.IsNullOrWhiteSpace(drawing.savedAt) ? drawing.savedAt : "未知时间",
                fileName = Path.GetFileName(filePath),
                filePath = filePath,
                lastWriteTime = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.MinValue
            };
        }

        private static SavedBlueprintInfo CreateInfoFast(string filePath)
        {
            var writeTime = File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.MinValue;
            return new SavedBlueprintInfo
            {
                documentId = string.Empty,
                documentName = Path.GetFileNameWithoutExtension(filePath),
                savedAt = writeTime.ToString("yyyy-MM-dd HH:mm:ss"),
                fileName = Path.GetFileName(filePath),
                filePath = filePath,
                lastWriteTime = writeTime
            };
        }

        private static string ResolveDocumentName(DrawingDto drawing, string filePath)
        {
            if (drawing != null && !string.IsNullOrWhiteSpace(drawing.documentName))
            {
                return drawing.documentName;
            }

            return Path.GetFileNameWithoutExtension(filePath);
        }

        [Serializable]
        private sealed class DrawingDto
        {
            public string documentId;
            public string documentName;
            public string savedAt;
            public List<ComponentDto> components = new List<ComponentDto>();
            public List<WireDto> wires = new List<WireDto>();
        }

        [Serializable]
        private sealed class ComponentDto
        {
            public string instanceId;
            public string definitionName;
            public float x;
            public float y;
            public bool isClosed;
            public List<ComponentParameter> parameters = new List<ComponentParameter>();
        }

        [Serializable]
        private sealed class WireDto
        {
            public string startComponentId;
            public string startTerminalId;
            public string endComponentId;
            public string endTerminalId;
            public string color;
            public string style;
            public bool hasManualRoute;
            public bool manualRouteHorizontal;
            public float manualRouteAxis;
            public List<Vector2> manualRoutePoints = new List<Vector2>();
        }
    }
}

