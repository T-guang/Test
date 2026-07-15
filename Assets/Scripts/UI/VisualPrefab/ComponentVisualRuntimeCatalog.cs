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
        public const string ResourcePath = "ComponentVisualRuntimeCatalog";

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
            entries = sourceEntries ?? new List<Entry>();
            cacheBuilt = false;
            entriesByDefinitionName.Clear();
        }

        private void EnsureCache()
        {
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
