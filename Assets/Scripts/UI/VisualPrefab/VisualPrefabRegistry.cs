using System;
using System.Collections.Generic;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 代码内的元件视觉配置映射表，以 ComponentDefinition.name 作为精确键保存 VisualPrefabConfig；
    /// 它不是 ScriptableObject Catalog 资产，也不直接写入 Resources。ComponentVisualRuntimeCatalogBuilder 会读取本表并生成或更新
    /// ComponentVisualRuntimeCatalog.asset；Windows Player 的主要视觉引用来自生成后的 Runtime Catalog；
    /// 部分 Editor 运行态兼容路径当前仍会直接查询 Registry。
    /// 配置中的 PrefabPath、DefaultSpritePath 与 ActiveSpritePath 是 Editor 资产路径而非 Resources 相对路径；
    /// 当前 Builder 优先采用 ComponentDefinition.sprite，DefaultSpritePath 只提供默认视觉的兜底候选；
    /// ActiveSpritePath 由 Editor Builder 解析为 Catalog 中的 activeSprite 引用，再由运行时视觉状态切换逻辑消费生成资产中的引用。
    /// TerminalPositionOverrides 仅覆盖视觉端子局部坐标，不创建 TerminalDefinition 或改变 terminalId 的电气语义。
    /// 修改映射、大小写或路径后需重建 Runtime Catalog，并复核 Player 中的视觉、端子位置与状态切换；Builder 成功不等于已完成运行态验证。
    /// </summary>
    public static class VisualPrefabRegistry
    {
        // 当前 KM 配置通过此覆盖表提供端子视觉局部坐标；
        // Runtime Catalog Builder 会把相同覆盖值写入 Catalog，
        // 使 Editor 兼容路径与 Player Catalog 路径共享同一组配置数据。
        private static readonly VisualPrefabTerminalPosition[] KmTerminalPositionOverrides =
        {
            new VisualPrefabTerminalPosition("L1", new UnityEngine.Vector2(-52.8f, 128.7f)),
            new VisualPrefabTerminalPosition("L2", new UnityEngine.Vector2(1.2f, 128.7f)),
            new VisualPrefabTerminalPosition("L3", new UnityEngine.Vector2(54.9f, 128.7f)),
            new VisualPrefabTerminalPosition("T1", new UnityEngine.Vector2(-53.1f, -131.1f)),
            new VisualPrefabTerminalPosition("T2", new UnityEngine.Vector2(0.9f, -131.1f)),
            new VisualPrefabTerminalPosition("T3", new UnityEngine.Vector2(54.6f, -131.1f)),
            new VisualPrefabTerminalPosition("A1", new UnityEngine.Vector2(-106.2f, 68.4f)),
            new VisualPrefabTerminalPosition("A2", new UnityEngine.Vector2(105.6f, 68.1f)),
            new VisualPrefabTerminalPosition("13", new UnityEngine.Vector2(-106.2f, 1.2f)),
            new VisualPrefabTerminalPosition("14", new UnityEngine.Vector2(105.6f, 1.2f)),
            new VisualPrefabTerminalPosition("21", new UnityEngine.Vector2(-106.2f, -65.1f)),
            new VisualPrefabTerminalPosition("22", new UnityEngine.Vector2(105.6f, -65.1f)),
            new VisualPrefabTerminalPosition("33", new UnityEngine.Vector2(-106.05f, -106.50f)),
            new VisualPrefabTerminalPosition("34", new UnityEngine.Vector2(105.90f, -107.10f))
        };

        // 配置表使用 StringComparer.Ordinal，definitionName 大小写敏感。重复键会在类型初始化时失败而非自动合并；
        // 表项顺序不参与 TryGetConfig 查找。删除表项不等于删除 ComponentDefinition，Catalog 缺项也不等于元件定义不存在。
        private static readonly Dictionary<string, VisualPrefabConfig> Configs = new Dictionary<string, VisualPrefabConfig>(StringComparer.Ordinal)
        {
            {
                "Button_Start_NO",
                new VisualPrefabConfig(
                    "Button_Start_NO",
                    "Assets/Prefab/Button_Start_NO_Visual.prefab",
                    "Assets/Art/Components/Button_Start_NO_Default.png",
                    "Assets/Art/Components/Button_Start_NO_Pressed.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    hasOperationHitArea: true)
            },
            {
                "Contactor_KM_220V",
                new VisualPrefabConfig(
                    "Contactor_KM_220V",
                    "Assets/Prefab/Contactor_KM_380V_Visual.prefab",
                    "Assets/Art/Components/Contactor_KM_380V_Default.png",
                    "Assets/Art/Components/Contactor_KM_380V_Energized.png",
                    VisualPrefabStateMode.ContactorEnergized,
                    terminalPositionOverrides: KmTerminalPositionOverrides)
            },
            {
                "Contactor_KM_380V",
                new VisualPrefabConfig(
                    "Contactor_KM_380V",
                    "Assets/Prefab/Contactor_KM_380V_Visual.prefab",
                    "Assets/Art/Components/Contactor_KM_380V_Default.png",
                    "Assets/Art/Components/Contactor_KM_380V_Energized.png",
                    VisualPrefabStateMode.ContactorEnergized,
                    terminalPositionOverrides: KmTerminalPositionOverrides)
            },
            {
                "EmergencyStop_NC",
                new VisualPrefabConfig(
                    "EmergencyStop_NC",
                    "Assets/Prefab/EmergencyStopButton_Visual.prefab",
                    "Assets/Art/Components/EmergencyStop/EmergencyStopButton_Default.png",
                    "Assets/Art/Components/EmergencyStop/EmergencyStopButton_Pressed.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: false,
                    hasOperationHitArea: true)
            },
            {
                "Button_Stop_NC",
                new VisualPrefabConfig(
                    "Button_Stop_NC",
                    "Assets/Prefab/Button_Stop_NC_Visual.prefab",
                    "Assets/Art/Components/Button_Stop_NC_Default.png",
                    "Assets/Art/Components/Button_Stop_NC_Pressed.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: false,
                    hasOperationHitArea: true)
            },
            {
                "Button_Compound_SB",
                new VisualPrefabConfig(
                    "Button_Compound_SB",
                    "Assets/Prefab/Button_Compound_SB_Visual.prefab",
                    "Assets/Art/Components/Button_Compound_SB_Default.png",
                    "Assets/Art/Components/Button_Compound_SB_Pressed.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    hasOperationHitArea: true)
            },
            {
                "Button_Compound_Green_SB",
                new VisualPrefabConfig(
                    "Button_Compound_Green_SB",
                    "Assets/Prefab/Button_Compound_Green_SB_Visual.prefab",
                    "Assets/Art/Components/Button_Compound_Green_SB_Default.png",
                    "Assets/Art/Components/Button_Compound_Green_SB_Pressed.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    hasOperationHitArea: true)
            },
            {
                "Button_SelfLock_SB",
                new VisualPrefabConfig(
                    "Button_SelfLock_SB",
                    "Assets/Prefab/Button_SelfLock_SB_Visual.prefab",
                    "Assets/Art/Components/Button_SelfLock_SB_Default.png",
                    "Assets/Art/Components/Button_SelfLock_SB_Locked.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    hasOperationHitArea: true)
            },
            {
                "Button_SelfLock_Green_SB",
                new VisualPrefabConfig(
                    "Button_SelfLock_Green_SB",
                    "Assets/Prefab/Button_SelfLock_Green_SB_Visual.prefab",
                    "Assets/Art/Components/Button_SelfLock_Green_SB_Default.png",
                    "Assets/Art/Components/Button_SelfLock_Green_SB_Locked.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    hasOperationHitArea: true)
            },
            {
                "AC_ThreePhase_Power",
                new VisualPrefabConfig(
                    "AC_ThreePhase_Power",
                    "Assets/Prefab/AC_ThreePhase_Power_Visual.prefab",
                    "Assets/Art/Components/AC_ThreePhase_Power_Visual.png")
            },
            {
                "Breaker_1P",
                new VisualPrefabConfig(
                    "Breaker_1P",
                    "Assets/Prefab/Breaker_1P_Visual.prefab",
                    "Assets/Art/Components/Breaker_1P_Off.png",
                    "Assets/Art/Components/Breaker_1P_On.png",
                    VisualPrefabStateMode.IsClosed)
            },
            {
                "Breaker_2P",
                new VisualPrefabConfig(
                    "Breaker_2P",
                    "Assets/Prefab/Breaker_2P_Visual.prefab",
                    "Assets/Art/Components/Breaker_2P_Off.png",
                    "Assets/Art/Components/Breaker_2P_On.png",
                    VisualPrefabStateMode.IsClosed)
            },
            {
                "Breaker_3P",
                new VisualPrefabConfig(
                    "Breaker_3P",
                    "Assets/Prefab/Breaker_3P_Visual.prefab",
                    "Assets/Art/Components/Breaker_3P_Off.png",
                    "Assets/Art/Components/Breaker_3P_On.png",
                    VisualPrefabStateMode.IsClosed)
            },
            {
                "Breaker_4P",
                new VisualPrefabConfig(
                    "Breaker_4P",
                    "Assets/Prefab/Breaker_4P_Visual.prefab",
                    "Assets/Art/Components/Breaker_4P_Off.png",
                    "Assets/Art/Components/Breaker_4P_On.png",
                    VisualPrefabStateMode.IsClosed)
            },
            {
                "Fuse_1P",
                new VisualPrefabConfig(
                    "Fuse_1P",
                    "Assets/Prefab/Fuse_1P_Visual.prefab")
            },
            {
                "Fuse_3P",
                new VisualPrefabConfig(
                    "Fuse_3P",
                    "Assets/Prefab/Fuse_3P_Visual.prefab")
            },
            {
                "Indicator_Green_220V",
                new VisualPrefabConfig(
                    "Indicator_Green_220V",
                    "Assets/Prefab/Indicator_Green_Visual.prefab",
                    "Assets/Art/Components/Indicator_Green_Off.png",
                    "Assets/Art/Components/Indicator_Green_On.png",
                    VisualPrefabStateMode.IsEnergized)
            },
            {
                "Indicator_Red_220V",
                new VisualPrefabConfig(
                    "Indicator_Red_220V",
                    "Assets/Prefab/Indicator_Red_Visual.prefab",
                    "Assets/Art/Components/Indicator_Red_Off.png",
                    "Assets/Art/Components/Indicator_Red_On.png",
                    VisualPrefabStateMode.IsEnergized)
            },
            {
                "Indicator_Yellow_220V",
                new VisualPrefabConfig(
                    "Indicator_Yellow_220V",
                    "Assets/Prefab/Indicator_Yellow_Visual.prefab",
                    "Assets/Art/Components/Indicator_Yellow_Off.png",
                    "Assets/Art/Components/Indicator_Yellow_On.png",
                    VisualPrefabStateMode.IsEnergized)
            },
            {
                "Indicator_Green_380V",
                new VisualPrefabConfig(
                    "Indicator_Green_380V",
                    "Assets/Prefab/Indicator_Green_Visual.prefab",
                    "Assets/Art/Components/Indicator_Green_Off.png",
                    "Assets/Art/Components/Indicator_Green_On.png",
                    VisualPrefabStateMode.IsEnergized)
            },
            {
                "Indicator_Red_380V",
                new VisualPrefabConfig(
                    "Indicator_Red_380V",
                    "Assets/Prefab/Indicator_Red_Visual.prefab",
                    "Assets/Art/Components/Indicator_Red_Off.png",
                    "Assets/Art/Components/Indicator_Red_On.png",
                    VisualPrefabStateMode.IsEnergized)
            },
            {
                "Indicator_Yellow_380V",
                new VisualPrefabConfig(
                    "Indicator_Yellow_380V",
                    "Assets/Prefab/Indicator_Yellow_Visual.prefab",
                    "Assets/Art/Components/Indicator_Yellow_Off.png",
                    "Assets/Art/Components/Indicator_Yellow_On.png",
                    VisualPrefabStateMode.IsEnergized)
            },
            {
                "Timer_OnDelay_220V",
                new VisualPrefabConfig(
                    "Timer_OnDelay_220V",
                    "Assets/Prefab/KT_Timer_Visual.prefab",
                    "Assets/Art/Components/KT_Timer_Normal.png")
            },
            {
                "Timer_OnDelay_380V",
                new VisualPrefabConfig(
                    "Timer_OnDelay_380V",
                    "Assets/Prefab/KT_Timer_Visual.prefab",
                    "Assets/Art/Components/KT_Timer_Normal.png")
            },
            {
                "ThermalRelay_FR_380V",
                new VisualPrefabConfig(
                    "ThermalRelay_FR_380V",
                    "Assets/Prefab/ThermalRelay_FR_Visual.prefab",
                    "Assets/Art/Components/ThermalRelay_FR_Normal.png",
                    hasOperationHitArea: true)
            },
            {
                "Motor_ThreePhase_380V",
                new VisualPrefabConfig(
                    "Motor_ThreePhase_380V",
                    "Assets/Prefab/Motor_ThreePhase_380V_Visual.prefab",
                    null,
                    null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "Motor_StarDelta_380V",
                new VisualPrefabConfig(
                    "Motor_StarDelta_380V",
                    "Assets/Prefab/Motor_StarDelta_380V_Visual.prefab",
                    null,
                    null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "Single_Phase_Meter",
                new VisualPrefabConfig(
                    "Single_Phase_Meter",
                    "Assets/Prefab/SinglePhase_EnergyMeter_Visual.prefab",
                    "Assets/Art/Components/Single_Phase_Meter/单相电能表.png",
                    null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "Single_Control_Switch",
                new VisualPrefabConfig(
                    "Single_Control_Switch",
                    "Assets/Prefab/Switch_SingleControl_Visual.prefab",
                    "Assets/Art/Components/Switches/SingleControl/单开单控开关_关.png",
                    "Assets/Art/Components/Switches/SingleControl/单开单控开关_开.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: true)
            },
            {
                "Two_Way_Switch",
                new VisualPrefabConfig(
                    "Two_Way_Switch",
                    "Assets/Prefab/Switch_TwoWay_Visual.prefab",
                    "Assets/Art/Components/Switches/TwoWay/单开双控开关_关.png",
                    "Assets/Art/Components/Switches/TwoWay/单开双控开关_开.png",
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: true)
            },
            {
                "LimitSwitch_SelfLock",
                new VisualPrefabConfig(
                    "LimitSwitch_SelfLock",
                    "Assets/Prefab/LimitSwitch_SelfLock_Visual.prefab",
                    "Assets/Art/Components/LimitSwitch/限位开关_关.png",
                    "Assets/Art/Components/LimitSwitch/限位开关_开.png",
                    VisualPrefabStateMode.LimitSwitchTriggered,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: true)
            },
            {
                "LimitSwitch_Compound",
                new VisualPrefabConfig(
                    "LimitSwitch_Compound",
                    "Assets/Prefab/LimitSwitch_Compound_Visual.prefab",
                    "Assets/Art/Components/LimitSwitch/限位开关_关.png",
                    "Assets/Art/Components/LimitSwitch/限位开关_开.png",
                    VisualPrefabStateMode.LimitSwitchTriggered,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: true)
            },
            {
                "TerminalBlock_2H",
                new VisualPrefabConfig(
                    "TerminalBlock_2H",
                    "Assets/Prefab/TerminalBlock_2H_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "TerminalBlock_3H",
                new VisualPrefabConfig(
                    "TerminalBlock_3H",
                    "Assets/Prefab/TerminalBlock_3H_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "TerminalBlock_6H",
                new VisualPrefabConfig(
                    "TerminalBlock_6H",
                    "Assets/Prefab/TerminalBlock_6H_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "TerminalBlock_2V",
                new VisualPrefabConfig(
                    "TerminalBlock_2V",
                    "Assets/Prefab/TerminalBlock_2V_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "TerminalBlock_3V",
                new VisualPrefabConfig(
                    "TerminalBlock_3V",
                    "Assets/Prefab/TerminalBlock_3V_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "TerminalBlock_6V",
                new VisualPrefabConfig(
                    "TerminalBlock_6V",
                    "Assets/Prefab/TerminalBlock_6V_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "AC_220V_Power",
                new VisualPrefabConfig(
                    "AC_220V_Power",
                    "Assets/Prefab/AC_220V_Power_Visual.prefab",
                    null, null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "KnifeSwitch_QS",
                new VisualPrefabConfig(
                    "KnifeSwitch_QS",
                    "Assets/Prefab/KnifeSwitch_QS_Visual.prefab",
                    "Assets/Art/Components/KnifeSwitch/刀开关_开.png", // default = 断开
                    "Assets/Art/Components/KnifeSwitch/刀开关_关.png", // active = 合上
                    VisualPrefabStateMode.IsClosed,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: true)
            },
            {
                "Lamp_220V",
                new VisualPrefabConfig(
                    "Lamp_220V",
                    "Assets/Prefab/Lamp_220V_Visual.prefab",
                    "Assets/Art/Components/Lamp/未发光.png",
                    "Assets/Art/Components/Lamp/发光.png",
                    VisualPrefabStateMode.IsEnergized,
                    activeWhenClosed: true,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            },
            {
                "Fan_220V",
                new VisualPrefabConfig(
                    "Fan_220V",
                    "Assets/Prefab/Fan_220V_Visual.prefab",
                    null,
                    null,
                    VisualPrefabStateMode.Static,
                    activeWhenClosed: false,
                    useTransparentTerminalView: true,
                    hideLegacyTerminalLabel: true,
                    disableLegacyTerminalOffset: true,
                    hasOperationHitArea: false)
            }
        };

        public static bool TryGetConfig(string definitionName, out VisualPrefabConfig config)
        {
            // 空键或未命中均返回 false 并保持 config 为 null；本查找不验证 Prefab、Sprite 路径或端子覆盖能否在实际资源中解析。
            if (string.IsNullOrWhiteSpace(definitionName))
            {
                config = null;
                return false;
            }

            return Configs.TryGetValue(definitionName, out config);
        }
    }
}
