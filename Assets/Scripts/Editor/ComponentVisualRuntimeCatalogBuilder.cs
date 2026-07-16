using System;
using System.Collections.Generic;
using ElectricalSim.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.EditorTools
{
    /// <summary>
    /// 仅在 Unity Editor 中构建 Windows Player 可读取的 ComponentVisualRuntimeCatalog 资产。
    /// 菜单 <c>Tools/ElectricalSim/Rebuild Runtime Visual Catalog</c> 扫描 <c>Assets/Data</c> 内的 ComponentDefinition；
    /// 默认 Sprite 优先来自 <c>ComponentDefinition.sprite</c>，VisualPrefabRegistry 提供 Visual Prefab、激活 Sprite、
    /// 默认 Sprite 兜底路径和端子位置覆盖。Catalog 条目当前以 <c>definition.name</c> 为键，写入或覆盖
    /// <c>Assets/Resources/ComponentVisualRuntimeCatalog.asset</c> 后调用 SaveAssets 与 Refresh。
    ///
    /// Catalog 是 Palette、百科和画布视觉在 Player 中的资源引用目录；Builder 本身不进入 Player。重复 definitionName、
    /// 缺失 Sprite/Prefab 或不完整端子绑定会写入 Console 警告，但这些情况与 ComponentDefinition 缺失并非同一问题。
    /// 生成成功不等于视觉已经过运行态验证；修改扫描范围、资源路径、DefinitionName 键或 Registry 映射都可能影响 Player，
    /// 应退出 Play Mode 后执行，并在执行完成后审查 Catalog 资产差异。该工具不修改当前场景、模板或 Prefab。
    /// </summary>
    public static class ComponentVisualRuntimeCatalogBuilder
    {
        private const string CatalogAssetPath = "Assets/Resources/ComponentVisualRuntimeCatalog.asset";
        private const string DefinitionSearchFolder = "Assets/Data";

        [MenuItem("Tools/ElectricalSim/Rebuild Runtime Visual Catalog")]
        public static void Rebuild()
        {
            // ReplaceEntries 会整体替换 Catalog 条目；不要把本菜单当作增量、只读检查使用。
            var catalog = LoadOrCreateCatalog();
            var entries = new List<ComponentVisualRuntimeCatalog.Entry>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var missingSprites = new List<string>();
            var missingPrefabs = new List<string>();
            var duplicateNames = new List<string>();
            var incompleteTerminalBindings = new List<string>();

            var guids = AssetDatabase.FindAssets("t:ComponentDefinition", new[] { DefinitionSearchFolder });
            Array.Sort(guids, StringComparer.Ordinal);
            for (var i = 0; i < guids.Length; i++)
            {
                var definitionPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                var definition = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(definitionPath);
                if (definition == null || string.IsNullOrWhiteSpace(definition.name))
                {
                    continue;
                }

                if (!names.Add(definition.name))
                {
                    duplicateNames.Add(definition.name);
                    continue;
                }

                VisualPrefabRegistry.TryGetConfig(definition.name, out var config);
                var prefab = config == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(config.PrefabPath);
                var defaultSprite = definition.sprite;
                var activeSprite = config == null || string.IsNullOrWhiteSpace(config.ActiveSpritePath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Sprite>(config.ActiveSpritePath);

                if (defaultSprite == null && config != null && !string.IsNullOrWhiteSpace(config.DefaultSpritePath))
                {
                    defaultSprite = AssetDatabase.LoadAssetAtPath<Sprite>(config.DefaultSpritePath);
                }

                if (defaultSprite == null && prefab != null)
                {
                    defaultSprite = ResolveBodySprite(prefab);
                }

                if (defaultSprite == null)
                {
                    missingSprites.Add(definition.name);
                }

                if (config != null && prefab == null)
                {
                    missingPrefabs.Add(definition.name + " -> " + config.PrefabPath);
                }

                var terminalPositionOverrides = CloneTerminalPositionOverrides(config);
                var missingTerminalBindings = CollectMissingTerminalBindings(definition, prefab, terminalPositionOverrides);
                if (missingTerminalBindings.Count > 0)
                {
                    incompleteTerminalBindings.Add(definition.name + " -> " + string.Join(", ", missingTerminalBindings));
                }

                entries.Add(new ComponentVisualRuntimeCatalog.Entry
                {
                    definitionName = definition.name,
                    defaultSprite = defaultSprite,
                    activeSprite = activeSprite,
                    visualPrefab = prefab,
                    terminalPositionOverrides = terminalPositionOverrides,
                    terminalBindingsValidated = prefab != null,
                    terminalBindingsComplete = prefab == null || missingTerminalBindings.Count == 0
                });
            }

            catalog.ReplaceEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var runtimeCatalog = Resources.Load<ComponentVisualRuntimeCatalog>(ComponentVisualRuntimeCatalog.ResourcePath);
            if (runtimeCatalog == null)
            {
                Debug.LogError("[RuntimeVisualCatalog] Resources.Load failed: " + ComponentVisualRuntimeCatalog.ResourcePath);
                return;
            }

            LogWarnings("重复 definitionName", duplicateNames);
            LogWarnings("缺少默认 Sprite", missingSprites);
            LogWarnings("缺少配置的 Visual Prefab", missingPrefabs);
            LogWarnings("Incomplete terminal bindings", incompleteTerminalBindings);
            Debug.Log("[RuntimeVisualCatalog] Rebuilt entries=" + runtimeCatalog.Entries.Count +
                      ", sprites=" + (entries.Count - missingSprites.Count) +
                      ", encyclopediaSprites=" + (entries.Count - missingSprites.Count) +
                      ", visualPrefabs=" + CountPrefabs(entries) +
                      ", terminalMappings=" + CountTerminalMappings(entries) +
                      ", duplicateKeys=" + duplicateNames.Count +
                      ", missingResources=" + (missingSprites.Count + missingPrefabs.Count) +
                      ", incompleteTerminalBindings=" + incompleteTerminalBindings.Count);
        }

        private static ComponentVisualRuntimeCatalog LoadOrCreateCatalog()
        {
            // 资产不存在时才创建；既有资产会在 Rebuild 中被新扫描结果覆盖。
            var catalog = AssetDatabase.LoadAssetAtPath<ComponentVisualRuntimeCatalog>(CatalogAssetPath);
            if (catalog != null)
            {
                return catalog;
            }

            catalog = ScriptableObject.CreateInstance<ComponentVisualRuntimeCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
            return catalog;
        }

        private static Sprite ResolveBodySprite(GameObject prefab)
        {
            var body = prefab.transform.Find("Body");
            var image = body != null ? body.GetComponent<Image>() : prefab.GetComponentInChildren<Image>(true);
            return image != null ? image.sprite : null;
        }

        private static int CountPrefabs(IReadOnlyList<ComponentVisualRuntimeCatalog.Entry> entries)
        {
            var count = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].visualPrefab != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountTerminalMappings(IReadOnlyList<ComponentVisualRuntimeCatalog.Entry> entries)
        {
            var count = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].terminalPositionOverrides != null &&
                    entries[i].terminalPositionOverrides.Count > 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<VisualPrefabTerminalPosition> CloneTerminalPositionOverrides(VisualPrefabConfig config)
        {
            var result = new List<VisualPrefabTerminalPosition>();
            if (config == null)
            {
                return result;
            }

            foreach (var source in config.TerminalPositionOverrides)
            {
                if (source != null && !string.IsNullOrWhiteSpace(source.terminalId))
                {
                    result.Add(new VisualPrefabTerminalPosition(source.terminalId, source.localPosition));
                }
            }

            return result;
        }

        private static List<string> CollectMissingTerminalBindings(
            ComponentDefinition definition,
            GameObject prefab,
            IReadOnlyList<VisualPrefabTerminalPosition> terminalPositionOverrides)
        {
            var missing = new List<string>();
            if (definition == null || prefab == null)
            {
                return missing;
            }

            var mappedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anchors = prefab.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < anchors.Length; i++)
            {
                var anchor = anchors[i];
                if (anchor != null && anchor.name.StartsWith("Terminal_", StringComparison.Ordinal))
                {
                    mappedIds.Add(anchor.name.Substring("Terminal_".Length));
                }
            }

            for (var i = 0; i < terminalPositionOverrides.Count; i++)
            {
                var position = terminalPositionOverrides[i];
                if (position != null && !string.IsNullOrWhiteSpace(position.terminalId))
                {
                    mappedIds.Add(position.terminalId);
                }
            }

            foreach (var terminal in definition.terminals)
            {
                if (terminal != null && !mappedIds.Contains(terminal.id))
                {
                    missing.Add(terminal.id);
                }
            }

            return missing;
        }

        private static void LogWarnings(string label, List<string> values)
        {
            if (values.Count > 0)
            {
                Debug.LogWarning("[RuntimeVisualCatalog] " + label + " (" + values.Count + "): " + string.Join(", ", values));
            }
        }
    }
}
