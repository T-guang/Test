using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 封装一个绑定到 CircuitComponent 的运行时 Visual Prefab 实例及其外观、端子锚点缓存。
    /// 它通过 VisualPrefabConfig/VisualPrefabRegistry 的配置创建视觉层，不替代 ComponentDefinition、
    /// CircuitComponent 或用户图纸数据；缓存引用仅服务当前运行时，不能进入保存格式。视觉资源或
    /// 端子锚点缺失时返回失败并保守回退。修改后需检查普通元件、KT 与电机 Visual Prefab。
    /// </summary>
    public sealed class VisualPrefabInstance
    {
        private readonly VisualPrefabConfig config;
        private readonly RectTransform root;
        private readonly Image bodyImage;
        private readonly Sprite defaultSprite;
        private readonly Sprite activeSprite;
        private readonly Dictionary<string, RectTransform> terminalAnchors;
        private readonly Dictionary<string, Vector2> terminalPositionOverrides;

        private VisualPrefabInstance(
            VisualPrefabConfig config,
            RectTransform root,
            Image bodyImage,
            Sprite defaultSprite,
            Sprite activeSprite,
            Dictionary<string, RectTransform> terminalAnchors,
            Dictionary<string, Vector2> terminalPositionOverrides)
        {
            this.config = config;
            this.root = root;
            this.bodyImage = bodyImage;
            this.defaultSprite = defaultSprite;
            this.activeSprite = activeSprite;
            this.terminalAnchors = terminalAnchors;
            this.terminalPositionOverrides = terminalPositionOverrides;
        }

        public VisualPrefabConfig Config => config;
        public RectTransform Root => root;
        public bool IsActive => root != null;
        public bool ShowTerminalDebugMarkers => config != null && config.ShowTerminalDebugMarkers;

        public static bool TryCreate(
            VisualPrefabConfig config,
            Transform parent,
            RectTransform ownerRect,
            Image legacyBody,
            Text legacyTitle,
            out VisualPrefabInstance instance)
        {
            // 运行时 Catalog 保存序列化引用，避免 Player 依赖 Editor 的 AssetDatabase。
            // 失败时调用方继续使用旧外观，不应让视觉缺失改变元件端子、规则或运行态。
            instance = null;
            if (config == null || parent == null || string.IsNullOrWhiteSpace(config.PrefabPath))
            {
                return false;
            }

            var catalog = ComponentVisualRuntimeCatalog.Load();
            ComponentVisualRuntimeCatalog.Entry runtimeEntry = null;
            var prefab = catalog != null && catalog.TryGetEntry(config.DefinitionName, out runtimeEntry)
                ? runtimeEntry.visualPrefab
                : null;
            var defaultSprite = runtimeEntry != null ? runtimeEntry.defaultSprite : null;
            var activeSprite = runtimeEntry != null ? runtimeEntry.activeSprite : null;

#if UNITY_EDITOR
            if (prefab == null)
            {
                prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(config.PrefabPath);
                defaultSprite = string.IsNullOrWhiteSpace(config.DefaultSpritePath)
                    ? null
                    : UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(config.DefaultSpritePath);
                activeSprite = string.IsNullOrWhiteSpace(config.ActiveSpritePath)
                    ? null
                    : UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(config.ActiveSpritePath);
            }
#endif

            if (prefab == null)
            {
                Debug.LogWarning("[VisualPrefab] Missing runtime prefab: " + config.DefinitionName);
                return false;
            }

            var visualObject = Object.Instantiate(prefab, parent);
            visualObject.name = prefab.name + "_Visual";
            visualObject.transform.SetAsFirstSibling();

            var visualRoot = visualObject.GetComponent<RectTransform>();
            if (visualRoot != null)
            {
                visualRoot.anchorMin = new Vector2(0.5f, 0.5f);
                visualRoot.anchorMax = new Vector2(0.5f, 0.5f);
                visualRoot.pivot = new Vector2(0.5f, 0.5f);
                visualRoot.anchoredPosition = Vector2.zero;

                if (ownerRect != null && visualRoot.sizeDelta.x > 0f && visualRoot.sizeDelta.y > 0f)
                {
                    ownerRect.sizeDelta = visualRoot.sizeDelta;
                }
            }

            var bodyTransform = visualObject.transform.Find("Body");
            var visualBodyImage = bodyTransform != null ? bodyTransform.GetComponent<Image>() : null;
            if (visualBodyImage != null)
            {
                visualBodyImage.raycastTarget = false;
            }

            if (legacyBody != null)
            {
                legacyBody.enabled = true;
                legacyBody.raycastTarget = true;
                legacyBody.color = Color.clear;
            }

            if (legacyTitle != null && config.HideLegacyTerminalLabel)
            {
                legacyTitle.enabled = false;
            }

            instance = new VisualPrefabInstance(
                config,
                visualRoot,
                visualBodyImage,
                defaultSprite,
                activeSprite,
                CollectTerminalAnchors(visualObject.transform),
                CollectTerminalPositionOverrides(runtimeEntry, config));
            instance.UpdateBodySprite(false);
            return true;
        }

        public bool TryGetTerminalPosition(string terminalId, out Vector2 localPosition)
        {
            // 锚点只决定 TerminalView 的视觉位置；端子 ID 和电气连接仍由元件定义与 WireManager 保持。
            localPosition = Vector2.zero;
            if (!IsActive || string.IsNullOrWhiteSpace(terminalId))
            {
                return false;
            }

            if (terminalPositionOverrides.TryGetValue(terminalId, out localPosition))
            {
                return true;
            }

            if (terminalAnchors.TryGetValue(terminalId, out var anchor) &&
                anchor != null &&
                TryGetAnchoredPositionRelativeToRoot(anchor, root, out localPosition))
            {
                return true;
            }

            Debug.LogWarning("[VisualPrefab] Missing TerminalAnchor: " + config.DefinitionName + " Terminal_" + terminalId);
            return false;
        }

        public void UpdateBodySprite(bool active)
        {
            if (bodyImage == null)
            {
                return;
            }

            var targetSprite = active && activeSprite != null ? activeSprite : defaultSprite;
            if (targetSprite != null && bodyImage.sprite != targetSprite)
            {
                bodyImage.sprite = targetSprite;
            }
        }

        private static Dictionary<string, RectTransform> CollectTerminalAnchors(Transform visualRoot)
        {
            // 缓存 Prefab 中约定命名的 Terminal_* 子节点，避免每次刷新重复遍历层级；
            // 该缓存随 Visual Prefab 实例销毁，不属于用户图纸或模板数据。
            var anchors = new Dictionary<string, RectTransform>(System.StringComparer.OrdinalIgnoreCase);
            if (visualRoot == null)
            {
                return anchors;
            }

            var rects = visualRoot.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var candidate = rects[i];
                if (candidate == null ||
                    string.IsNullOrEmpty(candidate.name) ||
                    !candidate.name.StartsWith("Terminal_", System.StringComparison.Ordinal))
                {
                    continue;
                }

                var terminalId = candidate.name.Substring("Terminal_".Length);
                if (!anchors.ContainsKey(terminalId))
                {
                    anchors.Add(terminalId, candidate);
                }
            }

            return anchors;
        }

        private static Dictionary<string, Vector2> CollectTerminalPositionOverrides(
            ComponentVisualRuntimeCatalog.Entry runtimeEntry,
            VisualPrefabConfig config)
        {
            var overrides = new Dictionary<string, Vector2>(System.StringComparer.OrdinalIgnoreCase);
            var runtimeOverrides = runtimeEntry != null ? runtimeEntry.terminalPositionOverrides : null;
            if (runtimeOverrides != null)
            {
                for (var i = 0; i < runtimeOverrides.Count; i++)
                {
                    var candidate = runtimeOverrides[i];
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.terminalId))
                    {
                        overrides[candidate.terminalId] = candidate.localPosition;
                    }
                }
            }

            if (overrides.Count == 0 && config != null)
            {
                foreach (var candidate in config.TerminalPositionOverrides)
                {
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.terminalId))
                    {
                        overrides[candidate.terminalId] = candidate.localPosition;
                    }
                }
            }

            return overrides;
        }

        private static bool TryGetAnchoredPositionRelativeToRoot(
            RectTransform anchor,
            RectTransform visualRoot,
            out Vector2 localPosition)
        {
            localPosition = Vector2.zero;
            var current = anchor;
            while (current != null && current != visualRoot)
            {
                localPosition += current.anchoredPosition;
                current = current.parent as RectTransform;
            }

            return current == visualRoot;
        }
    }
}
