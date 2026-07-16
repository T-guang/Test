#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ElectricalSim.Core;
using ElectricalSim.Templates;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.EditorTools.Testing
{
    /// <summary>
    /// 从当前 ComponentDefinition、Template Catalog、模板 JSON 和 Runtime Visual Catalog 生成测试资料。
    /// 只读取项目资产并写入 Docs/TestManual，不改写模板、Definition、Prefab 或运行时数据。
    /// </summary>
    public static class GenerateTestManualData
    {
        private const string DefinitionFolder = "Assets/Data";
        private const string CatalogPath = "Assets/Resources/Blueprints/Templates/template_catalog.json";
        private const string OutputRoot = "Docs/TestManual";
        private const string TemplatesFolder = "Docs/TestManual/Templates";

        [MenuItem("Tools/ElectricalSim/Testing/Generate Terminal And Wiring Documents")]
        public static void Generate()
        {
            try
            {
                var data = Collect();
                WriteDocuments(data);
                AssetDatabase.Refresh();
                var summary = "[TestManualData] 生成完成：模板=" + data.Templates.Count + "，Definition=" + data.Definitions.Count +
                              "，端子=" + data.TerminalCount + "，导线=" + data.WireCount + "，错误=" + data.Errors.Count + "，警告=" + data.Warnings.Count;
                if (data.Errors.Count > 0) Debug.LogError(summary);
                else if (data.Warnings.Count > 0) Debug.LogWarning(summary);
                else Debug.Log(summary);
            }
            catch (Exception exception)
            {
                Debug.LogError("[TestManualData] 生成失败：" + exception);
            }
        }

        private static DataSet Collect()
        {
            var data = new DataSet
            {
                Branch = Git("branch --show-current"),
                Commit = Git("rev-parse HEAD"),
                VisualCatalog = Resources.Load<ComponentVisualRuntimeCatalog>(ComponentVisualRuntimeCatalog.ResourcePath)
            };
            LoadDefinitions(data);
            var catalog = LoadCatalog(data);
            if (catalog == null) return data;

            var catalogIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var resourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in catalog.templates ?? new List<CircuitTemplateCatalogItemDto>())
            {
                if (item == null)
                {
                    data.Errors.Add("Catalog 包含空条目。");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.templateId)) data.Errors.Add("Catalog 条目缺少 templateId。");
                else if (!catalogIds.Add(item.templateId)) data.Errors.Add("Catalog templateId 重复：" + item.templateId);
                if (string.IsNullOrWhiteSpace(item.resourcePath))
                {
                    data.Errors.Add("Catalog 条目缺少 resourcePath：" + item.templateId);
                    continue;
                }
                if (!resourcePaths.Add(item.resourcePath)) data.Errors.Add("Catalog resourcePath 重复：" + item.resourcePath);

                var jsonPath = "Assets/Resources/" + item.resourcePath + ".json";
                var imagePath = "Assets/Resources/" + item.thumbnailPath + ".png";
                if (!File.Exists(jsonPath))
                {
                    data.Errors.Add("Catalog resourcePath 不存在：" + item.templateId + " -> " + item.resourcePath);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(item.thumbnailPath) || !File.Exists(imagePath))
                    data.Errors.Add("Catalog thumbnailPath 不存在：" + item.templateId + " -> " + item.thumbnailPath);

                try
                {
                    var template = JsonUtility.FromJson<CircuitTemplateDto>(File.ReadAllText(jsonPath, Encoding.UTF8));
                    if (template == null) data.Errors.Add("模板 JSON 为空：" + item.templateId);
                    else data.Templates.Add(AuditTemplate(item, template, jsonPath, data));
                }
                catch (Exception exception)
                {
                    data.Errors.Add("模板 JSON 解析失败：" + item.templateId + "：" + exception.Message);
                }
            }

            if (catalog.templates == null || catalog.templates.Count != 18)
                data.Errors.Add("Template Catalog 数量不是 18：" + (catalog.templates == null ? 0 : catalog.templates.Count));
            data.Templates = data.Templates.OrderBy(x => x.Item.sortOrder).ThenBy(x => x.Item.templateId, StringComparer.Ordinal).ToList();
            data.HouseholdCount = data.Templates.Count(x => x.Item.category == "家庭电路");
            data.IndustrialCount = data.Templates.Count(x => x.Item.category == "工业电路");
            if (data.HouseholdCount != 8 || data.IndustrialCount != 10)
                data.Errors.Add("模板分类数量异常：家庭=" + data.HouseholdCount + "，工业=" + data.IndustrialCount);

            BuildTerminalUsage(data);
            ValidateVisuals(data);
            ValidateRequiredTerminals(data);
            return data;
        }

        private static void LoadDefinitions(DataSet data)
        {
            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { DefinitionFolder });
            Array.Sort(guids, StringComparer.Ordinal);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var definition = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
                if (definition == null || string.IsNullOrWhiteSpace(definition.name)) data.Errors.Add("无法读取 ComponentDefinition：" + path);
                else if (!data.Definitions.TryAdd(definition.name, definition)) data.Errors.Add("ComponentDefinition 名称重复：" + definition.name);
            }
            data.TerminalCount = data.Definitions.Values.Sum(x => x.terminals == null ? 0 : x.terminals.Count);
        }

        private static CircuitTemplateCatalogDto LoadCatalog(DataSet data)
        {
            if (!File.Exists(CatalogPath))
            {
                data.Errors.Add("Template Catalog 不存在：" + CatalogPath);
                return null;
            }
            try
            {
                var result = JsonUtility.FromJson<CircuitTemplateCatalogDto>(File.ReadAllText(CatalogPath, Encoding.UTF8));
                if (result == null || result.templates == null) data.Errors.Add("Template Catalog 解析后为空。");
                return result;
            }
            catch (Exception exception)
            {
                data.Errors.Add("Template Catalog 解析失败：" + exception.Message);
                return null;
            }
        }

        private static TemplateData AuditTemplate(CircuitTemplateCatalogItemDto item, CircuitTemplateDto template, string jsonPath, DataSet data)
        {
            var result = new TemplateData { Item = item, Template = template, JsonPath = jsonPath };
            if (!string.Equals(item.templateId, template.templateId, StringComparison.Ordinal))
                result.Errors.Add("Catalog templateId 与 JSON templateId 不一致：" + item.templateId + " / " + template.templateId);

            foreach (var component in template.components ?? new List<TemplateComponentDto>())
            {
                if (component == null || string.IsNullOrWhiteSpace(component.instanceId))
                {
                    result.Errors.Add("存在空实例或空 instanceId。");
                    continue;
                }
                if (!result.Components.TryAdd(component.instanceId, component))
                {
                    result.Errors.Add("重复实例 ID：" + component.instanceId);
                    continue;
                }
                if (string.IsNullOrWhiteSpace(component.definitionName) || !data.Definitions.TryGetValue(component.definitionName, out var definition))
                {
                    result.Errors.Add("缺失 ComponentDefinition：" + component.instanceId + " -> " + component.definitionName);
                    continue;
                }
                if (!definition.showInPalette && (definition.supportLevel == ComponentSupportLevel.VisualOnly || !definition.canParticipateInRuntime))
                    result.Errors.Add("模板引用隐藏且明确不支持的元件：" + component.instanceId + " -> " + component.definitionName);
            }

            var duplicateWires = new HashSet<string>(StringComparer.Ordinal);
            var wires = template.wires ?? new List<TemplateWireDto>();
            for (var i = 0; i < wires.Count; i++)
            {
                var wireData = new WireData { Index = i + 1, Wire = wires[i] };
                result.Wires.Add(wireData);
                if (wires[i] == null)
                {
                    wireData.Errors.Add("导线记录为空。");
                    result.Errors.Add("W-" + WireNumber(i + 1) + " 导线记录为空。");
                    continue;
                }
                ValidateEndpoint(result, data, wireData, wires[i].startComponentId, wires[i].startTerminalId, "起点");
                ValidateEndpoint(result, data, wireData, wires[i].endComponentId, wires[i].endTerminalId, "终点");
                if (string.Equals(wires[i].startComponentId, wires[i].endComponentId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(wires[i].startTerminalId, wires[i].endTerminalId, StringComparison.OrdinalIgnoreCase))
                {
                    wireData.Warnings.Add("同一实例同一端子自连接。");
                    result.Warnings.Add("W-" + WireNumber(i + 1) + " 为同端子自连接。");
                }
                if (!duplicateWires.Add(DuplicateWireKey(wires[i])))
                {
                    wireData.Errors.Add("与前一条导线完全重复。");
                    result.Errors.Add("重复导线：W-" + WireNumber(i + 1));
                }
            }
            foreach (var component in result.Components.Values.OrderBy(x => x.instanceId, StringComparer.Ordinal))
            {
                if (!result.UsedTerminals.TryGetValue(component.instanceId, out var terminals) || terminals.Count == 0)
                    result.Warnings.Add("实例未接入任何导线：" + component.instanceId);
            }
            data.WireCount += result.Wires.Count;
            return result;
        }

        private static void ValidateEndpoint(TemplateData template, DataSet data, WireData wire, string instanceId, string terminalId, string label)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                AddEndpointError(template, wire, label + " instanceId 为空。");
                return;
            }
            if (string.IsNullOrWhiteSpace(terminalId))
            {
                AddEndpointError(template, wire, label + " terminalId 为空：" + instanceId);
                return;
            }
            if (!template.Components.TryGetValue(instanceId, out var component))
            {
                AddEndpointError(template, wire, label + " 实例不存在：" + instanceId);
                return;
            }
            if (!data.Definitions.TryGetValue(component.definitionName, out var definition)) return;
            if (!TryTerminal(definition, terminalId, out _))
            {
                AddEndpointError(template, wire, label + " terminalId 不存在：" + instanceId + "." + terminalId);
                return;
            }
            if (!template.UsedTerminals.TryGetValue(instanceId, out var used))
            {
                used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                template.UsedTerminals.Add(instanceId, used);
            }
            used.Add(terminalId);
        }

        private static void AddEndpointError(TemplateData template, WireData wire, string error)
        {
            wire.Errors.Add(error);
            template.Errors.Add(error);
        }

        private static void BuildTerminalUsage(DataSet data)
        {
            foreach (var template in data.Templates)
            foreach (var component in template.Components.Values)
            {
                if (!data.Definitions.TryGetValue(component.definitionName, out var definition) || !template.UsedTerminals.TryGetValue(component.instanceId, out var used)) continue;
                foreach (var terminalId in used)
                {
                    var key = definition.name + "\u001f" + terminalId;
                    if (!data.TerminalUsage.TryGetValue(key, out var templates))
                    {
                        templates = new SortedSet<string>(StringComparer.Ordinal);
                        data.TerminalUsage.Add(key, templates);
                    }
                    templates.Add(template.Item.templateId);
                }
            }
        }

        private static void ValidateVisuals(DataSet data)
        {
            if (data.VisualCatalog == null)
            {
                data.Errors.Add("Runtime Visual Catalog 无法从 Resources.Load 加载。");
                return;
            }
            foreach (var definition in data.Definitions.Values.OrderBy(x => x.name, StringComparer.Ordinal))
            {
                ComponentVisualRuntimeCatalog.Entry entry = null;
                var hasEntry = data.VisualCatalog.TryGetEntry(definition.name, out entry);
                if (definition.showInPalette && (!hasEntry || entry.defaultSprite == null))
                {
                    data.MissingSprites.Add(definition.name);
                    data.Warnings.Add("当前可见器件缺失 Runtime Visual Sprite：" + definition.name);
                }
                if (VisualPrefabRegistry.TryGetConfig(definition.name, out var config) && config != null && (!hasEntry || entry.visualPrefab == null))
                {
                    data.MissingPrefabs.Add(definition.name);
                    data.Warnings.Add("需要 Visual Prefab 的器件缺失 Runtime Catalog Prefab：" + definition.name);
                }
            }
        }

        private static void ValidateRequiredTerminals(DataSet data)
        {
            Require(data, "Motor_StarDelta_380V", "U1", "V1", "W1", "U2", "V2", "W2");
            Require(data, "ThermalRelay_FR_380V", "95", "96", "97", "98");
            Require(data, "Timer_OnDelay_220V", "A1", "A2", "15", "16", "18");
            Require(data, "Timer_OnDelay_380V", "A1", "A2", "15", "16", "18");
            Require(data, "LimitSwitch_Compound", "11", "12", "23", "24");
            Require(data, "Contactor_KM_220V", "A1", "A2", "L1", "L2", "L3", "T1", "T2", "T3", "13", "14", "21", "22");
            Require(data, "Contactor_KM_380V", "A1", "A2", "L1", "L2", "L3", "T1", "T2", "T3", "13", "14", "21", "22");
            ValidateKm(data, "Contactor_KM_220V");
            ValidateKm(data, "Contactor_KM_380V");
        }

        private static void Require(DataSet data, string definitionName, params string[] terminalIds)
        {
            if (!data.Definitions.TryGetValue(definitionName, out var definition))
            {
                data.Errors.Add("关键器件 Definition 不存在：" + definitionName);
                return;
            }
            foreach (var terminalId in terminalIds)
            if (!TryTerminal(definition, terminalId, out _)) data.Errors.Add("关键器件缺少端子：" + definitionName + "." + terminalId);
        }

        private static void ValidateKm(DataSet data, string definitionName)
        {
            if (!data.Definitions.TryGetValue(definitionName, out var definition) || data.VisualCatalog == null || !data.VisualCatalog.TryGetEntry(definitionName, out var entry))
            {
                data.Errors.Add("KM 缺少 Definition 或 Runtime Catalog 条目：" + definitionName);
                return;
            }
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in entry.terminalPositionOverrides ?? new List<VisualPrefabTerminalPosition>()) if (value != null && !string.IsNullOrWhiteSpace(value.terminalId)) mapped.Add(value.terminalId);
            if (mapped.Count == 0 && VisualPrefabRegistry.TryGetConfig(definitionName, out var config) && config != null)
                foreach (var value in config.TerminalPositionOverrides) if (value != null && !string.IsNullOrWhiteSpace(value.terminalId)) mapped.Add(value.terminalId);
            foreach (var terminal in definition.terminals ?? new List<TerminalDefinition>())
                if (terminal != null && !mapped.Contains(terminal.id)) data.Errors.Add("KM 端子映射缺失：" + definitionName + "." + terminal.id);
        }

        private static void WriteDocuments(DataSet data)
        {
            Directory.CreateDirectory(FullPath(OutputRoot));
            Directory.CreateDirectory(FullPath(TemplatesFolder));
            Write(OutputRoot + "/00_GenerationReport.md", GenerationReport(data));
            Write(OutputRoot + "/01_ComponentTerminalDictionary.csv", TerminalCsv(data));
            Write(OutputRoot + "/01_ComponentTerminalDictionary.md", TerminalMarkdown(data));
            Write(OutputRoot + "/02_TemplateWiringMatrix.csv", WiringCsv(data));
            Write(OutputRoot + "/02_TemplateWiringMatrix.md", WiringMarkdown(data));
            for (var i = 0; i < data.Templates.Count; i++)
                Write(TemplatesFolder + "/" + (i + 1).ToString("D2", CultureInfo.InvariantCulture) + "_" + SafeName(data.Templates[i].Item.templateId) + ".md", TemplateMarkdown(data.Templates[i], data));
        }

        private static string GenerationReport(DataSet data)
        {
            var b = new StringBuilder("# 测试资料数据生成报告\n\n");
            Bullet(b, "生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            Bullet(b, "Git 提交", Value(data.Commit)); Bullet(b, "Git 分支", Value(data.Branch));
            Bullet(b, "模板总数", data.Templates.Count); Bullet(b, "家庭模板数量", data.HouseholdCount); Bullet(b, "工业模板数量", data.IndustrialCount);
            Bullet(b, "支持器件数量", data.Definitions.Values.Count(x => x.canParticipateInRuntime)); Bullet(b, "当前可见器件数量", data.Definitions.Values.Count(x => x.showInPalette));
            Bullet(b, "端子总数", data.TerminalCount); Bullet(b, "导线总数", data.WireCount);
            Bullet(b, "无效 Definition 数量", data.Templates.Sum(x => x.Errors.Count(y => y.StartsWith("缺失 ComponentDefinition", StringComparison.Ordinal))));
            Bullet(b, "无效 terminalId 数量", data.Templates.Sum(x => x.Errors.Count(y => y.Contains("terminalId"))));
            Bullet(b, "重复实例 ID 数量", data.Templates.Sum(x => x.Errors.Count(y => y.StartsWith("重复实例 ID", StringComparison.Ordinal))));
            Bullet(b, "重复导线数量", data.Templates.Sum(x => x.Errors.Count(y => y.StartsWith("重复导线", StringComparison.Ordinal))));
            Bullet(b, "缺失 Visual Sprite 数量", data.MissingSprites.Count); Bullet(b, "缺失 Visual Prefab 数量", data.MissingPrefabs.Count);
            b.AppendLine("\n## 每张模板统计\n");
            Table(b, new[] { "序号", "模板 ID", "模板名称", "分类", "元件数", "导线数", "错误", "警告" }, data.Templates.Select((x, i) => new[] { (i + 1).ToString(), x.Item.templateId, x.Item.templateName, x.Item.category, x.Components.Count.ToString(), x.Wires.Count.ToString(), x.Errors.Count.ToString(), x.Warnings.Count.ToString() }));
            Issues(b, "错误", data.Errors.Concat(data.Templates.SelectMany(x => x.Errors)));
            Issues(b, "警告", data.Warnings.Concat(data.Templates.SelectMany(x => x.Warnings)));
            b.AppendLine("## 推导字段说明\n\n- 端子功能、NO/NC 属性、回路类型、画面位置和测试称呼如无项目原始字段，均明确标记为“根据 terminalId 和元件类型推导”或“根据实例 ID 推导”。\n- 本资料不评价模板电气原理，仅检查数据结构和引用完整性。\n");
            return b.ToString();
        }

        private static string TerminalCsv(DataSet data)
        {
            var rows = new List<IReadOnlyList<string>> { new[] { "DefinitionName", "元件显示名称", "元件分类", "supportLevel", "showInPalette", "是否参与运行时仿真", "是否参与参数估算", "terminalId", "端子显示标签", "端子类型", "端子功能", "功能来源", "常开常闭或普通", "属性来源", "主控制测量分类", "分类来源", "端子方向或画面位置", "Visual Prefab 是否存在", "Runtime Visual Catalog 是否存在", "被哪些模板使用", "备注" } };
            foreach (var definition in OrderedDefinitions(data)) foreach (var terminal in definition.terminals ?? new List<TerminalDefinition>()) if (terminal != null) rows.Add(TerminalRow(data, definition, terminal));
            return Csv(rows);
        }

        private static string TerminalMarkdown(DataSet data)
        {
            var b = new StringBuilder("# 元件端子与引脚字典\n\n数据来自 ComponentDefinition。带“推导”的内容不是项目原始字段，仅供测试定位时保守参考。\n\n## 当前可见器件\n\n");
            TerminalTable(b, data, x => x.showInPalette);
            b.AppendLine("## 附录：当前隐藏或不属于 InternalTest1 测试范围的器件\n\n以下条目来自真实 Definition，但未显示在元件池或不参与完整运行时流程。\n");
            TerminalTable(b, data, x => !x.showInPalette || !x.canParticipateInRuntime);
            return b.ToString();
        }

        private static void TerminalTable(StringBuilder b, DataSet data, Func<ComponentDefinition, bool> predicate)
        {
            Table(b, new[] { "DefinitionName", "显示名称", "端子", "标签", "类型", "功能", "NO/NC", "回路", "位置", "Visual Prefab", "Runtime Catalog", "使用模板", "备注" },
                OrderedDefinitions(data).Where(predicate).SelectMany(definition => (definition.terminals ?? new List<TerminalDefinition>()).Where(x => x != null).Select(terminal =>
                {
                    var r = TerminalRow(data, definition, terminal);
                    return new[] { r[0], r[1], r[7], r[8], r[9], r[10] + "（" + r[11] + "）", r[12], r[14], r[16], r[17], r[18], r[19], r[20] };
                })));
        }

        private static IReadOnlyList<string> TerminalRow(DataSet data, ComponentDefinition definition, TerminalDefinition terminal)
        {
            var function = Function(definition, terminal); var contact = Contact(terminal.id); var circuit = Circuit(definition, terminal);
            var key = definition.name + "\u001f" + terminal.id;
            var templates = data.TerminalUsage.TryGetValue(key, out var used) ? string.Join("; ", used) : "未被 18 张标准模板使用";
            ComponentVisualRuntimeCatalog.Entry entry = null;
            var entryFound = data.VisualCatalog != null && data.VisualCatalog.TryGetEntry(definition.name, out entry);
            var note = !definition.showInPalette ? "当前隐藏" : string.Empty;
            if (!definition.canParticipateInRuntime) note = string.IsNullOrWhiteSpace(note) ? "不参与运行时仿真" : note + "；不参与运行时仿真";
            return new[] { definition.name, definition.displayName, definition.category.ToString(), definition.supportLevel.ToString(), YesNo(definition.showInPalette), YesNo(definition.canParticipateInRuntime), YesNo(definition.canParticipateInParameterCalculation), terminal.id, terminal.label, terminal.role.ToString(), function.Value, function.Source, contact.Value, contact.Source, circuit.Value, circuit.Source, Position(terminal.normalizedPosition), YesNo(entryFound && entry.visualPrefab != null), YesNo(entryFound), templates, note };
        }

        private static string WiringCsv(DataSet data)
        {
            var rows = new List<IReadOnlyList<string>> { new[] { "模板ID", "模板名称", "模板分类", "导线序号", "起点实例ID", "起点实例显示名称", "起点DefinitionName", "起点terminalId", "起点端子显示标签", "终点实例ID", "终点实例显示名称", "终点DefinitionName", "终点terminalId", "终点端子显示标签", "导线颜色", "导线样式", "是否为手动折线", "路径点数量", "所属回路类型", "接线说明", "数据来源" } };
            foreach (var template in data.Templates) foreach (var wire in template.Wires) rows.Add(WireRow(template, wire, data));
            return Csv(rows);
        }

        private static string WiringMarkdown(DataSet data)
        {
            var b = new StringBuilder("# 18 张标准模板逐线接线总表\n\n导线编号保持模板 JSON 顺序。回路类型只在端子信息足够明确时作推导，否则显示“未自动判定”。\n\n");
            foreach (var template in data.Templates)
            {
                b.AppendLine("## " + template.Item.templateName + "（" + template.Item.templateId + "）\n");
                Table(b, new[] { "编号", "起点", "终点", "颜色", "手动折线", "路径点", "回路类型", "说明" }, template.Wires.Select(wire => { var r = WireRow(template, wire, data); return new[] { r[3], r[4] + "." + r[7], r[9] + "." + r[12], r[14], r[16], r[17], r[18], r[19] }; }));
            }
            return b.ToString();
        }

        private static string TemplateMarkdown(TemplateData template, DataSet data)
        {
            var b = new StringBuilder("# " + template.Item.templateName + "\n\n## 1. 模板基本信息\n\n");
            Bullet(b, "模板 ID", template.Item.templateId); Bullet(b, "分类", template.Item.category); Bullet(b, "难度", template.Item.difficulty);
            Bullet(b, "元件数量", template.Components.Count); Bullet(b, "导线数量", template.Wires.Count); Bullet(b, "使用的器件类型", string.Join("；", template.Components.Values.Select(x => x.definitionName).Distinct().OrderBy(x => x, StringComparer.Ordinal)));
            Bullet(b, "模板 JSON 路径", template.JsonPath);
            b.AppendLine("\n## 2. 元件实例表\n");
            Table(b, new[] { "实例 ID", "测试称呼（推导）", "DefinitionName", "显示名称", "位置", "参数摘要" }, template.Components.Values.OrderBy(x => x.instanceId, StringComparer.Ordinal).Select(component => { data.Definitions.TryGetValue(component.definitionName, out var definition); return new[] { component.instanceId, Alias(component, definition), component.definitionName, definition == null ? "缺失 Definition" : definition.displayName, Number(component.x) + ", " + Number(component.y), Parameters(component.parameters) }; }));
            b.AppendLine("## 3. 逐线接线表\n");
            Table(b, new[] { "编号", "起点实例", "起点端子", "终点实例", "终点端子", "线色", "说明" }, template.Wires.Select(wire => { var r = WireRow(template, wire, data); return new[] { r[3], r[4], r[7] + "（" + r[8] + "）", r[9], r[12] + "（" + r[13] + "）", r[14], r[19] }; }));
            b.AppendLine("## 4. 端子使用汇总\n");
            Table(b, new[] { "实例 ID", "DefinitionName", "实际使用端子", "未使用端子" }, template.Components.Values.OrderBy(x => x.instanceId, StringComparer.Ordinal).Select(component =>
            {
                data.Definitions.TryGetValue(component.definitionName, out var definition); template.UsedTerminals.TryGetValue(component.instanceId, out var used);
                var all = definition == null ? new List<string>() : (definition.terminals ?? new List<TerminalDefinition>()).Where(x => x != null).Select(x => x.id).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var active = used == null ? new List<string>() : used.OrderBy(x => x, StringComparer.Ordinal).ToList();
                var unused = all.Where(x => used == null || !used.Contains(x)).ToList();
                return new[] { component.instanceId, component.definitionName, active.Count == 0 ? "无" : string.Join("、", active), unused.Count == 0 ? "无" : string.Join("、", unused) };
            }));
            b.AppendLine("## 5. 数据检查结果\n"); Issues(b, "错误", template.Errors); Issues(b, "警告", template.Warnings);
            if (template.Errors.Count == 0 && template.Warnings.Count == 0) b.AppendLine("- 未发现无效端子、缺失元件、重复实例 ID、重复导线、自连接、悬空实例或 Catalog 路径问题。\n");
            return b.ToString();
        }

        private static IReadOnlyList<string> WireRow(TemplateData template, WireData data, DataSet set)
        {
            var wire = data.Wire;
            if (wire == null) return new[] { template.Item.templateId, template.Item.templateName, template.Item.category, "W-" + WireNumber(data.Index), "", "", "", "", "", "", "", "", "", "", "", "", "否", "0", "未自动判定", "导线记录为空", "模板 JSON：" + template.Item.resourcePath };
            template.Components.TryGetValue(wire.startComponentId ?? string.Empty, out var start); template.Components.TryGetValue(wire.endComponentId ?? string.Empty, out var end);
            set.Definitions.TryGetValue(start == null ? string.Empty : start.definitionName, out var startDef); set.Definitions.TryGetValue(end == null ? string.Empty : end.definitionName, out var endDef);
            TryTerminal(startDef, wire.startTerminalId, out var startTerminal); TryTerminal(endDef, wire.endTerminalId, out var endTerminal);
            var circuit = WireCircuit(startDef, startTerminal, endDef, endTerminal);
            var manual = wire.manualRoutePoints != null && wire.manualRoutePoints.Count > 0;
            return new[] { template.Item.templateId, template.Item.templateName, template.Item.category, "W-" + WireNumber(data.Index), wire.startComponentId, Alias(start, startDef), start == null ? "缺失实例" : start.definitionName, wire.startTerminalId, startTerminal == null ? "缺失端子" : startTerminal.label, wire.endComponentId, Alias(end, endDef), end == null ? "缺失实例" : end.definitionName, wire.endTerminalId, endTerminal == null ? "缺失端子" : endTerminal.label, wire.color, wire.style, YesNo(manual), (wire.manualRoutePoints == null ? 0 : wire.manualRoutePoints.Count).ToString(CultureInfo.InvariantCulture), circuit.Value, Alias(start, startDef) + "." + (startTerminal == null ? wire.startTerminalId : startTerminal.label) + " → " + Alias(end, endDef) + "." + (endTerminal == null ? wire.endTerminalId : endTerminal.label), "模板 JSON：" + template.Item.resourcePath };
        }

        private static IEnumerable<ComponentDefinition> OrderedDefinitions(DataSet data) => data.Definitions.Values.OrderBy(x => x.category).ThenBy(x => x.displayName, StringComparer.Ordinal);
        private static bool TryTerminal(ComponentDefinition definition, string id, out TerminalDefinition terminal) { terminal = definition == null || definition.terminals == null || string.IsNullOrWhiteSpace(id) ? null : definition.terminals.FirstOrDefault(x => x != null && string.Equals(x.id, id, StringComparison.OrdinalIgnoreCase)); return terminal != null; }
        private static string Alias(TemplateComponentDto component, ComponentDefinition definition) => component == null ? "缺失实例" : (definition == null ? component.definitionName : definition.displayName.Replace("\n", " ")) + " [" + component.instanceId + "]（根据实例 ID 推导）";
        private static string Parameters(IReadOnlyList<ComponentParameter> parameters) => parameters == null || parameters.Count == 0 ? "无模板覆盖参数" : string.Join("；", parameters.Where(x => x != null).Select(x => (string.IsNullOrWhiteSpace(x.displayName) ? x.key : x.displayName) + "=" + Number(x.value) + x.unit));

        private static Description Function(ComponentDefinition definition, TerminalDefinition terminal)
        {
            if (terminal.role != TerminalRole.Generic) return new Description(terminal.role.ToString(), "TerminalRole 原始字段");
            var id = terminal.id ?? string.Empty;
            if (id.StartsWith("L", StringComparison.OrdinalIgnoreCase) || id.StartsWith("T", StringComparison.OrdinalIgnoreCase) || id.StartsWith("U", StringComparison.OrdinalIgnoreCase) || id.StartsWith("V", StringComparison.OrdinalIgnoreCase) || id.StartsWith("W", StringComparison.OrdinalIgnoreCase)) return new Description("主回路端子", "根据 terminalId 和元件类型推导");
            if (id == "A1" || id == "A2") return new Description("线圈控制端子", "根据 terminalId 和元件类型推导");
            if (new[] { "95", "96", "97", "98", "15", "16", "18", "11", "12", "13", "14", "21", "22", "23", "24" }.Contains(id)) return new Description("控制触点端子", "根据 terminalId 和元件类型推导");
            return new Description("未提供明确功能", "项目未提供端子功能字段");
        }

        private static Description Contact(string id)
        {
            if (new[] { "13", "14", "23", "24", "97", "98" }.Contains(id)) return new Description("常开触点组", "根据 terminalId 推导");
            if (new[] { "11", "12", "21", "22", "95", "96" }.Contains(id)) return new Description("常闭触点组", "根据 terminalId 推导");
            if (id == "15") return new Description("转换触点公共端", "根据 terminalId 推导");
            if (id == "16") return new Description("转换触点常闭端", "根据 terminalId 推导");
            if (id == "18") return new Description("转换触点常开端", "根据 terminalId 推导");
            return new Description("普通端子", "项目未提供 NO/NC 字段");
        }

        private static Description Circuit(ComponentDefinition definition, TerminalDefinition terminal)
        {
            if (terminal.role == TerminalRole.CoilA1 || terminal.role == TerminalRole.CoilA2) return new Description("控制回路", "根据 TerminalRole 推导");
            if (terminal.role == TerminalRole.Phase || terminal.role == TerminalRole.Input || terminal.role == TerminalRole.Output) return new Description("主回路或供电端", "根据 TerminalRole 推导");
            if (definition.category == ComponentCategory.Measurement || (terminal.id ?? string.Empty).StartsWith("P", StringComparison.OrdinalIgnoreCase)) return new Description("测量端子", "根据元件分类和 terminalId 推导");
            if (new[] { "A1", "A2", "95", "96", "97", "98", "15", "16", "18", "11", "12", "13", "14", "21", "22", "23", "24" }.Contains(terminal.id)) return new Description("控制回路", "根据 terminalId 推导");
            return new Description("未自动判定", "项目未提供回路分类字段");
        }

        private static Description WireCircuit(ComponentDefinition leftDefinition, TerminalDefinition left, ComponentDefinition rightDefinition, TerminalDefinition right)
        {
            var a = leftDefinition == null || left == null ? new Description("未自动判定", "") : Circuit(leftDefinition, left);
            var b = rightDefinition == null || right == null ? new Description("未自动判定", "") : Circuit(rightDefinition, right);
            if (a.Value == b.Value && a.Value != "未自动判定") return new Description(a.Value + "（推导）", "根据两端端子信息推导");
            if (a.Value == "控制回路" || b.Value == "控制回路") return new Description("控制回路（推导）", "根据端子角色和 terminalId 推导");
            if (a.Value.Contains("主回路") || b.Value.Contains("主回路")) return new Description("主回路（推导）", "根据端子角色和 terminalId 推导");
            if (a.Value == "测量端子" || b.Value == "测量端子") return new Description("测量回路（推导）", "根据元件分类和 terminalId 推导");
            return new Description("未自动判定", "项目未提供导线回路字段");
        }

        private static string Position(Vector2 p)
        {
            var h = p.x <= .25f ? "左侧" : p.x >= .75f ? "右侧" : "中间"; var v = p.y <= .25f ? "下方" : p.y >= .75f ? "上方" : "中部";
            return h + v + "（normalizedPosition=" + Number(p.x) + "," + Number(p.y) + "；原始坐标）";
        }

        private static string DuplicateWireKey(TemplateWireDto wire)
        {
            var a = (wire.startComponentId ?? string.Empty) + "." + (wire.startTerminalId ?? string.Empty); var b = (wire.endComponentId ?? string.Empty) + "." + (wire.endTerminalId ?? string.Empty);
            if (string.Compare(a, b, StringComparison.Ordinal) > 0) { var temp = a; a = b; b = temp; }
            var route = wire.manualRoutePoints == null ? string.Empty : string.Join(";", wire.manualRoutePoints.Select(x => Number(x.x) + "," + Number(x.y)));
            return a + "|" + b + "|" + wire.color + "|" + wire.style + "|" + route + "|" + wire.manualRouteHorizontal + "|" + Number(wire.manualRouteAxis);
        }

        private static string Csv(IEnumerable<IReadOnlyList<string>> rows) { var b = new StringBuilder(); foreach (var row in rows) b.AppendLine(string.Join(",", row.Select(x => "\"" + (x ?? string.Empty).Replace("\"", "\"\"") + "\""))); return b.ToString(); }
        private static void Table(StringBuilder b, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) { b.AppendLine("| " + string.Join(" | ", headers.Select(Markdown)) + " |"); b.AppendLine("| " + string.Join(" | ", headers.Select(x => "---")) + " |"); foreach (var row in rows) b.AppendLine("| " + string.Join(" | ", row.Select(Markdown)) + " |"); b.AppendLine(); }
        private static void Issues(StringBuilder b, string title, IEnumerable<string> values) { b.AppendLine("## " + title + "\n"); var list = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(); if (list.Count == 0) b.AppendLine("无。\n"); else { foreach (var value in list) b.AppendLine("- " + value); b.AppendLine(); } }
        private static void Bullet(StringBuilder b, string name, object value) { b.AppendLine("- " + name + "：" + (value ?? string.Empty)); }
        private static string Markdown(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", string.Empty).Replace("\n", "<br>");
        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string WireNumber(int index) => index.ToString("D3", CultureInfo.InvariantCulture);
        private static string YesNo(bool value) => value ? "是" : "否";
        private static string Value(string value) => string.IsNullOrWhiteSpace(value) ? "无法读取" : value;
        private static string SafeName(string value) { var b = new StringBuilder(value ?? "template"); foreach (var ch in Path.GetInvalidFileNameChars()) b.Replace(ch, '_'); return b.ToString(); }
        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
        private static string FullPath(string relativePath) => Path.Combine(ProjectRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        private static void Write(string relativePath, string content) => File.WriteAllText(FullPath(relativePath), content, new UTF8Encoding(true));
        private static string Git(string arguments)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("git", arguments) { WorkingDirectory = ProjectRoot(), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using (var process = System.Diagnostics.Process.Start(info)) { if (process == null) return string.Empty; var output = process.StandardOutput.ReadToEnd(); process.WaitForExit(3000); return process.ExitCode == 0 ? output.Trim() : string.Empty; }
            }
            catch { return string.Empty; }
        }

        private sealed class DataSet
        {
            public string Branch; public string Commit; public ComponentVisualRuntimeCatalog VisualCatalog;
            public Dictionary<string, ComponentDefinition> Definitions = new Dictionary<string, ComponentDefinition>(StringComparer.OrdinalIgnoreCase);
            public List<TemplateData> Templates = new List<TemplateData>(); public Dictionary<string, SortedSet<string>> TerminalUsage = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            public List<string> Errors = new List<string>(); public List<string> Warnings = new List<string>(); public List<string> MissingSprites = new List<string>(); public List<string> MissingPrefabs = new List<string>();
            public int HouseholdCount; public int IndustrialCount; public int TerminalCount; public int WireCount;
        }
        private sealed class TemplateData
        {
            public CircuitTemplateCatalogItemDto Item; public CircuitTemplateDto Template; public string JsonPath;
            public Dictionary<string, TemplateComponentDto> Components = new Dictionary<string, TemplateComponentDto>(StringComparer.OrdinalIgnoreCase); public Dictionary<string, HashSet<string>> UsedTerminals = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            public List<WireData> Wires = new List<WireData>(); public List<string> Errors = new List<string>(); public List<string> Warnings = new List<string>();
        }
        private sealed class WireData { public int Index; public TemplateWireDto Wire; public List<string> Errors = new List<string>(); public List<string> Warnings = new List<string>(); }
        private sealed class Description { public readonly string Value; public readonly string Source; public Description(string value, string source) { Value = value; Source = source; } }
    }
}
#endif
