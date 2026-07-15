using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim.Core
{
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
