#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.EditorTools
{
    /// <summary>
    /// 仅在 Editor 菜单执行的系统模板结构完整性检查器。
    /// 它读取模板 Catalog、Resources 下模板 JSON 与 ComponentDefinition，核对 Catalog 路径、实例、端子和导线引用，
    /// 并把检查事实、警告和错误写入 <c>Assets/Reports/template_integrity_report.md</c>；不会改写模板 JSON、Definition、
    /// 场景或 Prefab。报告导入由现有 AssetDatabase.ImportAsset 调用完成。
    ///
    /// 本工具检查数据引用完整性，不评价电气原理，也不能替代 Play Mode 的 18 模板行为基线。
    /// 报告属于生成输出，提交前必须审查内容差异而非仅接受时间或元数据变化。已捕获的 Catalog 与模板解析问题会记录在报告结果中，
    /// 校验摘要会输出到 Console；未被局部处理的文件系统或扫描异常可能中断本次检查，需结合 Console 定位。
    /// </summary>
    public static class TemplateIntegrityChecker
    {
        private const string TemplateFolder = "Assets/Resources/Blueprints/Templates";
        private const string CatalogPath = TemplateFolder + "/template_catalog.json";
        private const string ReportFolder = "Assets/Reports";
        private const string ReportPath = ReportFolder + "/template_integrity_report.md";

        /// <summary>
        /// 单个 Catalog 或模板检查的内存结果，累计错误和警告后由 WriteReport 写入 template_integrity_report.md。
        /// Passed 只表示本脚本当前检查项未记录错误或警告，不替代运行态、规则或 Player 回归；该类型自身不解析模板或写文件。
        /// </summary>
        private sealed class CheckResult
        {
            public string Name;
            public string Path;
            public readonly List<string> Notes = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Errors = new List<string>();
            public bool Passed => Errors.Count == 0 && Warnings.Count == 0;
        }

        [MenuItem("Tools/电工仿真/校验图纸模板完整性")]
        public static void CheckTemplates()
        {
            // 扫描与报告写入是显式菜单动作；不要将其接入模板加载或 Player 初始化流程。
            var definitions = LoadDefinitions();
            var catalogResult = new CheckResult { Name = "template_catalog.json", Path = CatalogPath };
            var catalog = LoadCatalog(catalogResult);
            var catalogItemsByResourcePath = BuildCatalogIndex(catalog, catalogResult);
            var templateResults = new List<CheckResult>();
            var templateResourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.GetFiles(TemplateFolder, "*.json", SearchOption.TopDirectoryOnly)
                         .Select(NormalizePath)
                         .Where(path => !string.Equals(path, CatalogPath, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                templateResourcePaths.Add(ResourcePathFromAssetPath(path));
                var result = CheckTemplate(path, definitions, catalogItemsByResourcePath);
                templateResults.Add(result);
            }

            ValidateCatalogResources(catalog, catalogResult);
            ValidateUncataloguedTemplates(templateResourcePaths, catalogItemsByResourcePath, catalogResult);
            catalogResult.Notes.Add("Catalog 条目数：" + catalogItemsByResourcePath.Count + "。");
            catalogResult.Notes.Add("扫描到模板 JSON：" + templateResults.Count + "。");
            catalogResult.Notes.Add("可用元件定义：" + definitions.Count + "。");
            WriteReport(catalogResult, templateResults);
            AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceUpdate);

            var warningCount = catalogResult.Warnings.Count + templateResults.Sum(result => result.Warnings.Count);
            var errorCount = catalogResult.Errors.Count + templateResults.Sum(result => result.Errors.Count);
            var passedCount = templateResults.Count(result => result.Passed);
            var summary =
                "[TemplateIntegrityChecker] 校验完成\n" +
                "模板数量：" + templateResults.Count + "\n" +
                "通过：" + passedCount + "\n" +
                "Warning：" + warningCount + "\n" +
                "Error：" + errorCount + "\n" +
                "报告路径：" + ReportPath;

            if (errorCount > 0)
            {
                Debug.LogError(summary);
            }
            else if (warningCount > 0)
            {
                Debug.LogWarning(summary);
            }
            else
            {
                Debug.Log(summary);
            }
        }

        private static Dictionary<string, ComponentDefinition> LoadDefinitions()
        {
            var definitions = new Dictionary<string, ComponentDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in AssetDatabase.FindAssets("t:ComponentDefinition", new[] { "Assets/Data" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (definition != null && !string.IsNullOrWhiteSpace(definition.name))
                {
                    definitions[definition.name] = definition;
                }
            }

            return definitions;
        }

        private static CircuitTemplateCatalogDto LoadCatalog(CheckResult result)
        {
            if (!File.Exists(CatalogPath))
            {
                result.Errors.Add("catalog 文件不存在：" + CatalogPath);
                return null;
            }

            try
            {
                var catalog = JsonUtility.FromJson<CircuitTemplateCatalogDto>(File.ReadAllText(CatalogPath));
                if (catalog == null || catalog.templates == null)
                {
                    result.Errors.Add("catalog JSON 解析后为空。");
                    return null;
                }

                return catalog;
            }
            catch (Exception exception)
            {
                result.Errors.Add("catalog JSON 解析失败：" + exception.Message);
                return null;
            }
        }

        private static Dictionary<string, CircuitTemplateCatalogItemDto> BuildCatalogIndex(
            CircuitTemplateCatalogDto catalog,
            CheckResult result)
        {
            var index = new Dictionary<string, CircuitTemplateCatalogItemDto>(StringComparer.OrdinalIgnoreCase);
            if (catalog == null || catalog.templates == null)
            {
                return index;
            }

            var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in catalog.templates)
            {
                if (item == null)
                {
                    result.Errors.Add("catalog 包含空条目。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.templateId))
                {
                    result.Errors.Add("catalog 条目缺少 templateId。");
                }
                else if (!templateIds.Add(item.templateId))
                {
                    result.Errors.Add("catalog templateId 重复：" + item.templateId);
                }

                if (string.IsNullOrWhiteSpace(item.resourcePath))
                {
                    result.Errors.Add("catalog 条目缺少 resourcePath：" + item.templateId);
                    continue;
                }

                if (!index.TryAdd(item.resourcePath, item))
                {
                    result.Errors.Add("catalog resourcePath 重复：" + item.resourcePath);
                }
            }

            return index;
        }

        private static void ValidateCatalogResources(CircuitTemplateCatalogDto catalog, CheckResult result)
        {
            if (catalog == null || catalog.templates == null)
            {
                return;
            }

            foreach (var item in catalog.templates)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.resourcePath))
                {
                    continue;
                }

                var path = NormalizePath("Assets/Resources/" + item.resourcePath + ".json");
                if (!File.Exists(path))
                {
                    result.Errors.Add(
                        "catalog 条目 resourcePath 找不到对应模板文件：" + item.templateId + " -> " + item.resourcePath);
                }
            }
        }

        private static void ValidateUncataloguedTemplates(
            IEnumerable<string> templateResourcePaths,
            IReadOnlyDictionary<string, CircuitTemplateCatalogItemDto> catalogItemsByResourcePath,
            CheckResult result)
        {
            foreach (var resourcePath in templateResourcePaths)
            {
                if (!catalogItemsByResourcePath.ContainsKey(resourcePath))
                {
                    result.Warnings.Add("模板 JSON 未登记到 catalog：" + resourcePath);
                }
            }
        }

        private static CheckResult CheckTemplate(
            string path,
            IReadOnlyDictionary<string, ComponentDefinition> definitions,
            IReadOnlyDictionary<string, CircuitTemplateCatalogItemDto> catalogItemsByResourcePath)
        {
            var result = new CheckResult { Name = Path.GetFileNameWithoutExtension(path), Path = path };
            CircuitTemplateDto template;
            try
            {
                template = JsonUtility.FromJson<CircuitTemplateDto>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                result.Errors.Add("模板 JSON 解析失败：" + exception.Message);
                return result;
            }

            if (template == null || string.IsNullOrWhiteSpace(template.templateId))
            {
                result.Errors.Add("模板 JSON 解析失败或缺少 templateId。");
                return result;
            }

            result.Name = string.IsNullOrWhiteSpace(template.templateName) ? template.templateId : template.templateName;
            var components = template.components ?? new List<TemplateComponentDto>();
            var wires = template.wires ?? new List<TemplateWireDto>();
            var componentsById = new Dictionary<string, TemplateComponentDto>(StringComparer.OrdinalIgnoreCase);
            var connectedTerminals = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var component in components)
            {
                if (component == null || string.IsNullOrWhiteSpace(component.instanceId))
                {
                    result.Errors.Add("模板包含缺少 instanceId 的元件。");
                    continue;
                }

                if (!componentsById.TryAdd(component.instanceId, component))
                {
                    result.Errors.Add("元件实例 ID 重复：" + component.instanceId);
                }

                connectedTerminals.TryAdd(component.instanceId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(component.definitionName) ||
                    !definitions.ContainsKey(component.definitionName))
                {
                    result.Errors.Add("元件 typeId 不存在：" + component.definitionName + "（" + component.instanceId + "）");
                }
            }

            foreach (var wire in wires)
            {
                ValidateWireEndpoint(
                    wire?.startComponentId,
                    wire?.startTerminalId,
                    componentsById,
                    definitions,
                    connectedTerminals,
                    result);
                ValidateWireEndpoint(
                    wire?.endComponentId,
                    wire?.endTerminalId,
                    componentsById,
                    definitions,
                    connectedTerminals,
                    result);
            }

            foreach (var component in componentsById.Values)
            {
                if (connectedTerminals.TryGetValue(component.instanceId, out var terminals) && terminals.Count == 0)
                {
                    result.Warnings.Add(
                        "元件未接入任何线路，可能只是被放置在画布上：" + ComponentLabel(component));
                }
            }

            var resourcePath = ResourcePathFromAssetPath(path);
            catalogItemsByResourcePath.TryGetValue(resourcePath, out var catalogItem);
            if (catalogItem != null && !string.IsNullOrWhiteSpace(catalogItem.templateName))
            {
                result.Name = catalogItem.templateName;
            }

            var category = catalogItem != null ? catalogItem.category : template.category;
            if (string.Equals(category, "工业电路", StringComparison.OrdinalIgnoreCase))
            {
                result.Notes.Add("工业模板专项检查已执行。");
                CheckIndustrialTemplate(template, componentsById, connectedTerminals, definitions, result);
            }

            return result;
        }

        private static void ValidateWireEndpoint(
            string componentId,
            string terminalId,
            IReadOnlyDictionary<string, TemplateComponentDto> componentsById,
            IReadOnlyDictionary<string, ComponentDefinition> definitions,
            IDictionary<string, HashSet<string>> connectedTerminals,
            CheckResult result)
        {
            if (string.IsNullOrWhiteSpace(componentId) || !componentsById.TryGetValue(componentId, out var component))
            {
                result.Errors.Add("连接引用了不存在的元件：" + componentId);
                return;
            }

            if (string.IsNullOrWhiteSpace(terminalId))
            {
                result.Errors.Add("连接端点缺少 terminalId：component=" + componentId);
                return;
            }

            connectedTerminals[componentId].Add(terminalId);
            if (!definitions.TryGetValue(component.definitionName, out var definition))
            {
                return;
            }

            if (!HasDefinitionTerminal(definition, terminalId))
            {
                result.Errors.Add("连接引用了不存在的端子：component=" + componentId + ", terminal=" + terminalId);
            }
        }

        private static void CheckIndustrialTemplate(
            CircuitTemplateDto template,
            IReadOnlyDictionary<string, TemplateComponentDto> componentsById,
            IReadOnlyDictionary<string, HashSet<string>> connectedTerminals,
            IReadOnlyDictionary<string, ComponentDefinition> definitions,
            CheckResult result)
        {
            var contactors = componentsById.Values.Where(component => IsContactor(component.definitionName)).ToList();
            var motors = componentsById.Values.Where(component => IsThreePhaseMotor(component.definitionName)).ToList();
            var timers = componentsById.Values.Where(component => IsOnDelayTimer(component.definitionName)).ToList();
            var limitSwitches = componentsById.Values.Where(component => IsLimitSwitch(component.definitionName)).ToList();

            foreach (var contactor in contactors)
            {
                RequireDefinitionTerminals(contactor, definitions, result, "接触器", "A1", "A2", "L1", "L2", "L3", "T1", "T2", "T3");
                WarnIfNoneConnected(contactor, connectedTerminals, result, "接触器线圈 A1/A2 未接线。", "A1", "A2");
                WarnIfNoneConnected(contactor, connectedTerminals, result, "接触器主触点 L/T 未接线。", "L1", "L2", "L3", "T1", "T2", "T3");
            }

            foreach (var motor in motors)
            {
                RequireDefinitionTerminals(motor, definitions, result, "三相电机", "U", "V", "W");
                WarnIfNoneConnected(motor, connectedTerminals, result, "三相电机 U/V/W 均未接线。", "U", "V", "W");
            }

            foreach (var timer in timers)
            {
                RequireDefinitionTerminals(timer, definitions, result, "OnDelay KT", "A1", "A2", "15", "16", "18");
                WarnIfNoneConnected(timer, connectedTerminals, result, "时间继电器 KT 线圈 A1/A2 未接入控制回路。", "A1", "A2");
                WarnIfNoneConnected(timer, connectedTerminals, result, "时间继电器 KT 延时常开触点 15/18 未接入任何回路。", "15", "18");
            }
            if (timers.Count > 0)
            {
                result.Notes.Add("OnDelay KT 专项检查：数量=" + timers.Count + "，已检查 A1/A2/15/16/18 定义及线圈、15/18 接线。");
            }

            var identity = (template.templateId + " " + template.templateName).ToLowerInvariant();
            if (ContainsAny(identity, "顺序启动", "sequential", "timer"))
            {
                CheckSequentialTemplate(template, contactors, motors, timers, result);
            }

            if (ContainsAny(identity, "自动往返", "reciprocating", "sq", "limit"))
            {
                CheckAutoReciprocatingTemplate(contactors, motors, limitSwitches, connectedTerminals, definitions, result);
            }
        }

        private static void CheckSequentialTemplate(
            CircuitTemplateDto template,
            IReadOnlyCollection<TemplateComponentDto> contactors,
            IReadOnlyCollection<TemplateComponentDto> motors,
            IReadOnlyCollection<TemplateComponentDto> timers,
            CheckResult result)
        {
            if (contactors.Count < 2) result.Errors.Add("顺序启动模板至少需要两个接触器。");
            if (motors.Count < 2) result.Errors.Add("顺序启动模板至少需要两个三相电机。");
            if (timers.Count < 1) result.Errors.Add("顺序启动模板至少需要一个 Timer_OnDelay。");

            var hasDirectTimerToKm2 = (template.wires ?? new List<TemplateWireDto>()).Any(wire =>
                wire != null &&
                (IsEndpoint(wire.startComponentId, wire.startTerminalId, timers, "18") &&
                 IsEndpoint(wire.endComponentId, wire.endTerminalId, contactors, "A1") ||
                 IsEndpoint(wire.endComponentId, wire.endTerminalId, timers, "18") &&
                 IsEndpoint(wire.startComponentId, wire.startTerminalId, contactors, "A1")));
            if (!hasDirectTimerToKm2)
            {
                result.Warnings.Add("顺序启动模板中未发现 KT.18 到接触器 A1 的直接连接，请确认后级接触器是否受 KT 延时触点控制。");
            }

            result.Notes.Add(
                "顺序启动专项检查：接触器=" + contactors.Count +
                "，三相电机=" + motors.Count +
                "，OnDelay KT=" + timers.Count +
                "，KT.18 到后级接触器 A1 直接连接=" + (hasDirectTimerToKm2 ? "已发现" : "未发现") + "。");
        }

        private static void CheckAutoReciprocatingTemplate(
            IReadOnlyCollection<TemplateComponentDto> contactors,
            IReadOnlyCollection<TemplateComponentDto> motors,
            IReadOnlyCollection<TemplateComponentDto> limitSwitches,
            IReadOnlyDictionary<string, HashSet<string>> connectedTerminals,
            IReadOnlyDictionary<string, ComponentDefinition> definitions,
            CheckResult result)
        {
            if (limitSwitches.Count < 2) result.Errors.Add("自动往返模板至少需要两个 SQ / LimitSwitch。");
            if (contactors.Count < 2) result.Errors.Add("自动往返模板至少需要两个接触器。");
            if (motors.Count < 1) result.Errors.Add("自动往返模板至少需要一个三相电机。");

            foreach (var limitSwitch in limitSwitches)
            {
                RequireDefinitionTerminals(limitSwitch, definitions, result, "SQ 行程开关", "11", "12", "23", "24");
                if (!connectedTerminals.TryGetValue(limitSwitch.instanceId, out var terminals) || terminals.Count == 0)
                {
                    result.Errors.Add("自动往返模板中的 SQ 完全孤立：" + ComponentLabel(limitSwitch));
                }
            }

            result.Notes.Add(
                "自动往返专项检查：接触器=" + contactors.Count +
                "，三相电机=" + motors.Count +
                "，SQ / LimitSwitch=" + limitSwitches.Count +
                "，已检查 SQ 11/12/23/24 定义及接线。");
        }

        private static void RequireDefinitionTerminals(
            TemplateComponentDto component,
            IReadOnlyDictionary<string, ComponentDefinition> definitions,
            CheckResult result,
            string componentType,
            params string[] terminalIds)
        {
            if (!definitions.TryGetValue(component.definitionName, out var definition))
            {
                return;
            }

            var missing = terminalIds.Where(id => !HasDefinitionTerminal(definition, id)).ToList();
            if (missing.Count > 0)
            {
                result.Errors.Add(componentType + " 缺少关键端子：" + ComponentLabel(component) + " -> " + string.Join(", ", missing));
            }
        }

        private static void WarnIfNoneConnected(
            TemplateComponentDto component,
            IReadOnlyDictionary<string, HashSet<string>> connectedTerminals,
            CheckResult result,
            string warning,
            params string[] terminalIds)
        {
            if (!connectedTerminals.TryGetValue(component.instanceId, out var connected) ||
                !terminalIds.Any(connected.Contains))
            {
                result.Warnings.Add(warning + " component=" + component.instanceId);
            }
        }

        private static bool HasDefinitionTerminal(ComponentDefinition definition, string terminalId)
        {
            return definition != null &&
                definition.terminals != null &&
                definition.terminals.Any(terminal =>
                    terminal != null &&
                    string.Equals(terminal.id, terminalId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsEndpoint(
            string componentId,
            string terminalId,
            IReadOnlyCollection<TemplateComponentDto> candidates,
            string expectedTerminal)
        {
            return string.Equals(terminalId, expectedTerminal, StringComparison.OrdinalIgnoreCase) &&
                candidates.Any(component =>
                    string.Equals(component.instanceId, componentId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsContactor(string definitionName)
        {
            return ContainsAny(definitionName, "Contactor", "KM");
        }

        private static bool IsThreePhaseMotor(string definitionName)
        {
            return ContainsAny(definitionName, "Motor_ThreePhase", "ThreePhaseMotor");
        }

        private static bool IsOnDelayTimer(string definitionName)
        {
            return ContainsAny(definitionName, "Timer_OnDelay");
        }

        private static bool IsLimitSwitch(string definitionName)
        {
            return ContainsAny(definitionName, "LimitSwitch", "TravelSwitch", "PositionSwitch", "Switch_Limit");
        }

        private static bool ContainsAny(string value, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return candidates.Any(candidate =>
                value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string ComponentLabel(TemplateComponentDto component)
        {
            return component.instanceId + " (" + component.definitionName + ")";
        }

        private static string ResourcePathFromAssetPath(string path)
        {
            const string prefix = "Assets/Resources/";
            var normalized = NormalizePath(path);
            return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(prefix.Length, normalized.Length - prefix.Length - Path.GetExtension(normalized).Length)
                : string.Empty;
        }

        private static string NormalizePath(string path)
        {
            return path.Replace("\\", "/");
        }

        private static void WriteReport(CheckResult catalogResult, IReadOnlyList<CheckResult> templateResults)
        {
            Directory.CreateDirectory(ReportFolder);
            var warningCount = catalogResult.Warnings.Count + templateResults.Sum(result => result.Warnings.Count);
            var errorCount = catalogResult.Errors.Count + templateResults.Sum(result => result.Errors.Count);
            var passedCount = templateResults.Count(result => result.Passed);
            var builder = new StringBuilder();

            builder.AppendLine("# 图纸模板完整性校验报告");
            builder.AppendLine();
            builder.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine("## 总览");
            builder.AppendLine();
            builder.AppendLine("- 模板总数：" + templateResults.Count);
            builder.AppendLine("- 通过数量：" + passedCount);
            builder.AppendLine("- Warning：" + warningCount);
            builder.AppendLine("- Error：" + errorCount);
            builder.AppendLine();
            AppendResult(builder, "Catalog 校验", catalogResult);

            builder.AppendLine("## 模板详情");
            builder.AppendLine();
            foreach (var result in templateResults)
            {
                AppendResult(builder, result.Name, result, 3);
            }

            File.WriteAllText(ReportPath, builder.ToString(), new UTF8Encoding(false));
        }

        private static void AppendResult(StringBuilder builder, string title, CheckResult result, int headingLevel = 2)
        {
            builder.AppendLine(new string('#', headingLevel) + " " + title);
            builder.AppendLine();
            builder.AppendLine("- 路径：" + result.Path);
            builder.AppendLine("- 结果：" + (result.Errors.Count > 0 ? "Error" : result.Warnings.Count > 0 ? "Warning" : "OK"));
            builder.AppendLine();
            AppendMessages(builder, "校验记录", result.Notes);
            AppendMessages(builder, "Errors", result.Errors);
            AppendMessages(builder, "Warnings", result.Warnings);
        }

        private static void AppendMessages(StringBuilder builder, string title, IReadOnlyCollection<string> messages)
        {
            builder.AppendLine("**" + title + "**");
            builder.AppendLine();
            if (messages.Count == 0)
            {
                builder.AppendLine("- 无");
            }
            else
            {
                foreach (var message in messages)
                {
                    builder.AppendLine("- " + message);
                }
            }

            builder.AppendLine();
        }
    }
}
#endif
