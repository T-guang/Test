using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 保存元件名称到运行时 Sprite 与 Visual Prefab 的序列化引用。
    /// 该资源位于 Resources 根目录，使 Editor 与 Player 使用相同的视觉来源，
    /// 而不依赖 AssetDatabase 或 Assets 路径字符串。
    /// </summary>
    public sealed class ComponentVisualRuntimeCatalog : ScriptableObject
    {
        // 当前 Catalog 是 Editor 运行态与 Windows Player 的首选公共视觉来源；Player 不依赖 AssetDatabase 或 Assets 路径字符串。
        // 个别 Editor 调用方仍可能保留 AssetDatabase 兼容回退，不能将两端描述为在所有情况下只使用同一来源。
        // Resources.Load 使用的固定运行时路径；更改前需同步复核 Builder、Palette、百科和 VisualPrefabInstance。
        public const string ResourcePath = "ComponentVisualRuntimeCatalog";

        /// <summary>
        /// 单个 definition.name 对应的运行时视觉引用。Builder 在 Editor 中填充这些序列化字段，Player 仅通过 Catalog 读取；
        /// 端子位置覆盖与绑定状态是视觉绑定的配置/验证结果，不定义逻辑端子或接线规则。
        /// </summary>
        [Serializable]
        public sealed class Entry
        {
            public string definitionName;
            public Sprite defaultSprite;
            public Sprite activeSprite;
            public GameObject visualPrefab;
            public List<VisualPrefabTerminalPosition> terminalPositionOverrides = new List<VisualPrefabTerminalPosition>();
            public bool terminalBindingsValidated;
            public bool terminalBindingsComplete;
        }

        // Unity 序列化的资产内容；运行时字典仅是查找缓存，不是第二份持久化数据。
        [SerializeField] private List<Entry> entries = new List<Entry>();

        private static ComponentVisualRuntimeCatalog cachedCatalog;
        private readonly Dictionary<string, Entry> entriesByDefinitionName =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool cacheBuilt;

        public IReadOnlyList<Entry> Entries => entries;

        public static ComponentVisualRuntimeCatalog Load()
        {
            if (cachedCatalog == null)
            {
                cachedCatalog = Resources.Load<ComponentVisualRuntimeCatalog>(ResourcePath);
            }

            return cachedCatalog;
        }

        public bool TryGetEntry(string definitionName, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(definitionName))
            {
                return false;
            }

            EnsureCache();
            return entriesByDefinitionName.TryGetValue(definitionName, out entry) && entry != null;
        }

        public bool TryGetDefaultSprite(string definitionName, out Sprite sprite)
        {
            sprite = null;
            return TryGetEntry(definitionName, out var entry) &&
                   (sprite = entry.defaultSprite) != null;
        }

        public bool TryGetVisualPrefab(string definitionName, out GameObject visualPrefab)
        {
            visualPrefab = null;
            return TryGetEntry(definitionName, out var entry) &&
                   (visualPrefab = entry.visualPrefab) != null;
        }

        public void ReplaceEntries(List<Entry> sourceEntries)
        {
            // Builder 使用此方法整体替换资产条目；调用方负责在 Editor 中保存资产。
            entries = sourceEntries ?? new List<Entry>();
            cacheBuilt = false;
            entriesByDefinitionName.Clear();
        }

        private void EnsureCache()
        {
            // 字典使用 StringComparer.Ordinal，definitionName 查找区分大小写；重复键保留列表中的首条，诊断主要由 Editor Builder 输出。
            if (cacheBuilt)
            {
                return;
            }

            entriesByDefinitionName.Clear();
            if (entries != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.definitionName))
                    {
                        continue;
                    }

                    if (!entriesByDefinitionName.ContainsKey(entry.definitionName))
                    {
                        entriesByDefinitionName.Add(entry.definitionName, entry);
                    }
                }
            }

            cacheBuilt = true;
        }
    }
}
