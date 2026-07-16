using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// VisualPrefabRegistry 使用的代码内视觉配置枚举。它描述当前视觉层依据何种运行态状态切换显示，
    /// 不参与模板 JSON 或 Unity 资产序列化，也不执行元件状态判断。
    /// 枚举成员调整前需同步复核 Registry 配置和 VisualPrefabInstance 的消费分支。
    /// </summary>
    public enum VisualPrefabStateMode
    {
        Static,
        IsClosed,
        IsEnergized,
        ContactorEnergized,
        LimitSwitchTriggered,
        TimerPhase,
        MotorRunning
    }

    /// <summary>
    /// 某个 definition.name 的代码内视觉配置，由 VisualPrefabRegistry 创建并由 Catalog Builder、VisualPrefabInstance 消费。
    /// Prefab/Sprite 路径当前供 Editor Builder 解析，Windows Player 的实际引用来自生成后的 ComponentVisualRuntimeCatalog；
    /// 本类型不是 ScriptableObject 或 JSON DTO。空路径、空端子覆盖和状态模式的回退行为由调用方实现，配置本身不验证资源存在。
    /// </summary>
    public sealed class VisualPrefabConfig
    {
        public string DefinitionName { get; }
        public string PrefabPath { get; }
        public string DefaultSpritePath { get; }
        public string ActiveSpritePath { get; }
        public VisualPrefabStateMode StateMode { get; }
        public bool ActiveWhenClosed { get; }
        public bool UseTransparentTerminalView { get; }
        public bool HideLegacyTerminalLabel { get; }
        public bool DisableLegacyTerminalOffset { get; }
        public bool HasOperationHitArea { get; }
        public bool ShowTerminalDebugMarkers { get; }
        public IReadOnlyList<VisualPrefabTerminalPosition> TerminalPositionOverrides { get; }

        public VisualPrefabConfig(
            string definitionName,
            string prefabPath,
            string defaultSpritePath = null,
            string activeSpritePath = null,
            VisualPrefabStateMode stateMode = VisualPrefabStateMode.Static,
            bool activeWhenClosed = true,
            bool useTransparentTerminalView = true,
            bool hideLegacyTerminalLabel = true,
            bool disableLegacyTerminalOffset = true,
            bool hasOperationHitArea = false,
            bool showTerminalDebugMarkers = false,
            IReadOnlyList<VisualPrefabTerminalPosition> terminalPositionOverrides = null)
        {
            DefinitionName = definitionName;
            PrefabPath = prefabPath;
            DefaultSpritePath = defaultSpritePath;
            ActiveSpritePath = activeSpritePath;
            StateMode = stateMode;
            ActiveWhenClosed = activeWhenClosed;
            UseTransparentTerminalView = useTransparentTerminalView;
            HideLegacyTerminalLabel = hideLegacyTerminalLabel;
            DisableLegacyTerminalOffset = disableLegacyTerminalOffset;
            HasOperationHitArea = hasOperationHitArea;
            ShowTerminalDebugMarkers = showTerminalDebugMarkers;
            TerminalPositionOverrides = terminalPositionOverrides ?? Array.Empty<VisualPrefabTerminalPosition>();
        }

        public bool TryGetTerminalPositionOverride(string terminalId, out Vector2 localPosition)
        {
            // 当前端子覆盖按忽略大小写的 terminalId 比较；未命中时保持 Vector2.zero 并返回 false。
            localPosition = Vector2.zero;
            if (string.IsNullOrWhiteSpace(terminalId))
            {
                return false;
            }

            for (var i = 0; i < TerminalPositionOverrides.Count; i++)
            {
                var candidate = TerminalPositionOverrides[i];
                if (candidate != null &&
                    string.Equals(candidate.terminalId, terminalId, StringComparison.OrdinalIgnoreCase))
                {
                    localPosition = candidate.localPosition;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// 可序列化的视觉端子位置覆盖，当前由 Catalog Entry 保存和运行时视觉绑定读取。
    /// 调用方约定 terminalId 应与逻辑端子标识一致；本类型自身不验证端子是否存在。
    /// localPosition 是视觉局部坐标，不改变 ComponentDefinition 的端子语义。
    /// </summary>
    [Serializable]
    public sealed class VisualPrefabTerminalPosition
    {
        public string terminalId;
        public Vector2 localPosition;

        public VisualPrefabTerminalPosition(string terminalId, Vector2 localPosition)
        {
            this.terminalId = terminalId;
            this.localPosition = localPosition;
        }
    }
}
