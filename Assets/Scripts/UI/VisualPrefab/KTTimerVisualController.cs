using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 负责通电延时时间继电器 Visual Prefab 的显示屏和四个局部操作热区。
    /// 延时设定读取 CircuitComponent 参数，计时/复位阶段读取 RuntimeStateManager；显示文本只是结果，
    /// 不能替代 KT 的真实运行态或保存数据。该控制器由元件视觉初始化绑定，停止、失电和重置后
    /// 应显示运行态管理器给出的结果。修改后需回归 KT 设置、倒计时、停止复位和保存导入。
    /// </summary>
    public sealed class KTTimerVisualController : MonoBehaviour
    {
        private const string DelayParameterKey = "delaySeconds";
        private const float MinDelaySeconds = 0f;
        private const float MaxDelaySeconds = 10f;

        [SerializeField] private Text displayText;
        [SerializeField] private RectTransform hitAreaDisplay;
        [SerializeField] private RectTransform hitAreaSet;
        [SerializeField] private RectTransform hitAreaPlus;
        [SerializeField] private RectTransform hitAreaMinus;

        private CircuitComponent component;
        private WorkspaceController workspace;
        private float lastDisplayedSeconds = -1f;
        private TimerRuntimePhase lastPhase = (TimerRuntimePhase)(-1);
        private bool lastCoilEnergized;

        public void Initialize(CircuitComponent owner, WorkspaceController ownerWorkspace)
        {
            // 初始化只绑定当前元件及已有 Prefab 子节点；热区回调仍通过参数入口和工作区脏标记生效。
            component = owner;
            workspace = ownerWorkspace;
            ResolveReferences();
            ConfigureDisplayText();
            ConfigureHitArea(hitAreaDisplay, OpenDelayDialog);
            ConfigureHitArea(hitAreaSet, OpenDelayDialog);
            ConfigureHitArea(hitAreaPlus, IncreaseDelay);
            ConfigureHitArea(hitAreaMinus, DecreaseDelay);
            RefreshNow();
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureDisplayText();
        }

        private void Update()
        {
            RefreshNow();
        }

        public void RefreshNow()
        {
            // 以“显示秒数 + 阶段 + 线圈得电”去重，避免 Update 每帧重写同一段文本。
            if (displayText == null || component == null)
            {
                return;
            }

            var displaySeconds = ResolveDisplaySeconds(out var phase, out var coilEnergized);
            if (Mathf.Approximately(displaySeconds, lastDisplayedSeconds) &&
                phase == lastPhase &&
                coilEnergized == lastCoilEnergized)
            {
                return;
            }

            lastDisplayedSeconds = displaySeconds;
            lastPhase = phase;
            lastCoilEnergized = coilEnergized;
            displayText.text = FormatSeconds(Mathf.CeilToInt(displaySeconds));
        }

        private void ResolveReferences()
        {
            if (displayText == null)
            {
                var display = transform.Find("DisplayText");
                displayText = display != null ? display.GetComponent<Text>() : null;
            }

            var areas = transform.Find("InteractionAreas");
            if (areas == null)
            {
                return;
            }

            hitAreaDisplay = hitAreaDisplay != null ? hitAreaDisplay : FindRect(areas, "HitArea_Display");
            hitAreaSet = hitAreaSet != null ? hitAreaSet : FindRect(areas, "HitArea_Set");
            hitAreaPlus = hitAreaPlus != null ? hitAreaPlus : FindRect(areas, "HitArea_Plus");
            hitAreaMinus = hitAreaMinus != null ? hitAreaMinus : FindRect(areas, "HitArea_Minus");
        }

        private static RectTransform FindRect(Transform parent, string childName)
        {
            var child = parent.Find(childName);
            return child != null ? child as RectTransform : null;
        }

        private void ConfigureDisplayText()
        {
            if (displayText == null)
            {
                return;
            }

            displayText.raycastTarget = false;
            displayText.color = new Color(1f, 0.08f, 0.05f, 1f);
            displayText.alignment = TextAnchor.MiddleCenter;
            displayText.fontStyle = FontStyle.Bold;
            displayText.horizontalOverflow = HorizontalWrapMode.Overflow;
            displayText.verticalOverflow = VerticalWrapMode.Overflow;
            displayText.resizeTextForBestFit = true;
            displayText.resizeTextMinSize = 16;
            displayText.resizeTextMaxSize = 32;
        }

        private static void ConfigureHitArea(RectTransform area, System.Action action)
        {
            if (area == null)
            {
                return;
            }

            var image = area.GetComponent<Image>();
            if (image == null)
            {
                image = area.gameObject.AddComponent<Image>();
            }

            image.color = Color.clear;
            image.raycastTarget = true;

            var hitArea = area.GetComponent<KTTimerHitArea>();
            if (hitArea == null)
            {
                hitArea = area.gameObject.AddComponent<KTTimerHitArea>();
            }

            hitArea.Configure(action);
        }

        private void IncreaseDelay()
        {
            SetDelaySeconds(ResolveDelaySeconds() + 1f);
        }

        private void DecreaseDelay()
        {
            SetDelaySeconds(ResolveDelaySeconds() - 1f);
        }

        private void OpenDelayDialog()
        {
            KTDelaySettingDialog.Show(component, workspace, SetDelaySeconds);
        }

        private void SetDelaySeconds(float value)
        {
            // 参数写入元件实例，随后只刷新显示和参数面板；真实计时仍由仿真循环维护，
            // 不能在此处自行推进或重置 RuntimeStateManager。
            if (component == null)
            {
                return;
            }

            var clamped = Mathf.Clamp(value, MinDelaySeconds, MaxDelaySeconds);
            if (!component.SetParameterValue(DelayParameterKey, clamped))
            {
                return;
            }

            RefreshNow();
            workspace?.RefreshParameterPanelFor(component);
            workspace?.MarkSimulationDirty("时间继电器延时时间已修改，点击开始仿真刷新结果。");
        }

        private float ResolveDisplaySeconds(out TimerRuntimePhase phase, out bool coilEnergized)
        {
            // 运行时计时优先于静态设定值：KT 通电计时显示剩余时间，失电或无状态时显示设定时间。
            var delaySeconds = ResolveDelaySeconds();
            phase = TimerRuntimePhase.Reset;
            coilEnergized = false;

            if (component != null &&
                RuntimeStateManager.Shared.TryGetTimerState(component.InstanceId, out var timerState) &&
                timerState != null)
            {
                phase = timerState.Phase;
                coilEnergized = timerState.IsCoilEnergized;
                if (timerState.IsCoilEnergized)
                {
                    if (timerState.Phase == TimerRuntimePhase.Elapsed)
                    {
                        return 0f;
                    }

                    if (timerState.Phase == TimerRuntimePhase.Timing)
                    {
                        var runtimeDelay = Mathf.Clamp(timerState.DelaySeconds > 0f ? timerState.DelaySeconds : delaySeconds, MinDelaySeconds, MaxDelaySeconds);
                        return Mathf.Max(0f, runtimeDelay - Mathf.Max(0f, timerState.ElapsedSeconds));
                    }
                }
            }

            return delaySeconds;
        }

        private float ResolveDelaySeconds()
        {
            var parameter = component != null ? component.GetParameter(DelayParameterKey) : null;
            return Mathf.Clamp(parameter != null ? parameter.value : 3f, MinDelaySeconds, MaxDelaySeconds);
        }

        internal static string FormatSeconds(int seconds)
        {
            seconds = Mathf.Clamp(seconds, 0, Mathf.RoundToInt(MaxDelaySeconds));
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        internal static bool TryParseDelaySeconds(string text, out float seconds)
        {
            seconds = 0f;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.Trim().Replace("秒", string.Empty);
            var parts = normalized.Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var minutes) &&
                int.TryParse(parts[1], out var sec))
            {
                seconds = Mathf.Clamp(minutes * 60 + sec, MinDelaySeconds, MaxDelaySeconds);
                return true;
            }

            if (float.TryParse(normalized, out var value))
            {
                seconds = Mathf.Clamp(value, MinDelaySeconds, MaxDelaySeconds);
                return true;
            }

            return false;
        }
    }
}
