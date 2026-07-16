using System.Collections.Generic;
using UnityEngine;

namespace ElectricalSim.Core
{
    /// <summary>
    /// Unity ScriptableObject 形式的元件定义资产，承载元件池、百科、模板生成和运行时元件初始化所需的基础配置。
    /// 当前由 Editor 工具或既有资产创建，运行时由 DemoRuntimeBootstrap、SaveLoadService 等提供给 Workspace、Palette 和模板生成流程读取；
    /// 它不是元件实例状态，也不负责接线、仿真或规则判断。
    ///
    /// Unity 资产名 <c>name</c> 当前被模板 <c>definitionName</c> 和多个视觉/生成查找路径使用，displayName 仅用于展示。
    /// terminals、parameters 及公开字段由 Unity 序列化；字段重命名、类型或默认值变化前需同时复核既有资产、模板 JSON 与读取端兼容性。
    /// supportLevel 与参与开关是配置输入，实际支持边界仍由调用方实现决定；本资产会进入 Windows Player，但编辑器生成成功不等于运行时行为已验证。
    /// </summary>
    [CreateAssetMenu(menuName = "Electrical Simulation/Component Definition")]
    public sealed class ComponentDefinition : ScriptableObject
    {
        [Header("Palette")]
        // 展示名称不等同于模板和视觉查找使用的 Unity 资产名。
        public string displayName = "Component";
        public ComponentCategory category = ComponentCategory.Household;
        public ComponentKind kind = ComponentKind.Switch;
        public Color bodyColor = new Color(0.86f, 0.88f, 0.9f);
        public Color accentColor = new Color(0.2f, 0.45f, 1f);
        public Sprite sprite;
        public Vector2 size = new Vector2(110f, 130f);
        public ComponentSupportLevel supportLevel = ComponentSupportLevel.RuntimeSupported;
        public bool showInPalette = true;
        public string unsupportedReason;
        public bool canParticipateInRuntime = true;
        public bool canParticipateInParameterCalculation = false;

        [Header("Electrical")]
        public bool startsClosed;
        public bool togglable;
        public bool allowSameComponentJumper;
        public string controlledByTag;
        public string outputTag;

        [Header("Simple Teaching Parameters")]
        public float sourceVoltage;
        public float sourceLineVoltage;
        public int sourcePhaseCount;
        public float ratedVoltage;
        public float ratedPower;
        public float ratedCurrent;
        public float maxVoltage;
        public float maxCurrent;
        public bool canBurnOut;
        public bool canTrip;
        public string parameterNote;
        // 模板和元件实例可复制此参数集合；空集合与无可用参数由调用方按当前流程区分。
        public List<ComponentParameter> parameters = new List<ComponentParameter>();

        public List<TerminalDefinition> terminals = new List<TerminalDefinition>();
    }
}

