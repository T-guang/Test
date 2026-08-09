using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI.CommonTools
{
    public sealed class ElectricianCalculatorController : MonoBehaviour
    {
        // 计算器是独立的教学工具 UI：解析用户输入、调用明确的公式并格式化结果。它不读取或修改活动 Workspace
        // 的元件、Wire、仿真状态或练习答案；结果只对当前表单生命周期有效。
        private RectTransform tabsArea;
        private RectTransform contentArea;
        private Button[] tabs;
        private RectTransform[] pages;
        private int currentTab = 0;

        // Load Current
        private Dropdown loadSupplyType;
        private Dropdown loadType;
        private InputField loadPower;
        private Dropdown loadPowerUnit;
        private InputField loadCosPhi;
        private InputField loadEta;
        private InputField loadCount;
        private Text loadResult;
        private RectTransform loadResultBg;

        // Wire and Breaker
        private InputField wireCurrent;
        private Dropdown wireMaterial;
        private Dropdown wireCondition;
        private Dropdown wireCircuitType;
        private Text wireResult;
        private RectTransform wireResultBg;

        // Motor Startup
        private InputField motorPower;
        private InputField motorVoltage;
        private InputField motorCosPhi;
        private InputField motorEta;
        private Dropdown motorStartupType;
        private Text motorResult;
        private RectTransform motorResultBg;

        // Multi Load
        private Dropdown multiSupplyType;
        private InputField multiLightCount;
        private InputField multiLightPower;
        private InputField multiFanCount;
        private InputField multiFanPower;
        private InputField multiMotorCount;
        private InputField multiMotorPower;
        private InputField multiOtherPower;
        private InputField multiCosPhi;
        private Text multiResult;
        private RectTransform multiResultBg;

        // Modern Color Palette
        private static readonly Color C_Primary = new Color(0.23f, 0.51f, 0.96f); // #3B82F6
        private static readonly Color C_Bg = new Color(0.97f, 0.98f, 0.99f); // #F7F9FC
        private static readonly Color C_Card = Color.white;
        private static readonly Color C_TextMain = new Color(0.07f, 0.09f, 0.15f); // #111827
        private static readonly Color C_TextMuted = new Color(0.42f, 0.45f, 0.5f); // #6B7280
        private static readonly Color C_Border = new Color(0.90f, 0.91f, 0.92f); // #E5E7EB
        private static readonly Color C_InputBg = new Color(0.98f, 0.98f, 0.99f); // #F9FAFB
        
        private static readonly Color C_ResultBg = new Color(0.94f, 0.96f, 1f); // #EFF6FF
        private static readonly Color C_ResultBorder = new Color(0.75f, 0.86f, 1f); // #BFDBFE
        private static readonly Color C_ResultText = new Color(0.12f, 0.25f, 0.6f); // #1E3A8A

        private static readonly Color C_ErrorBg = new Color(1f, 0.95f, 0.95f); // #FEF2F2
        private static readonly Color C_ErrorBorder = new Color(1f, 0.79f, 0.79f); // #FECACA
        private static readonly Color C_ErrorText = new Color(0.6f, 0.1f, 0.1f); // #991B1B

        private static Sprite roundedSprite;
        private static Sprite GetRoundedSprite()
        {
            // 圆角 sprite 为本控制器共享的纯视觉缓存；缓存的生命周期不携带任何表单输入或计算结果，避免跨页状态泄漏。
            if (roundedSprite != null) return roundedSprite;
            int r = 16;
            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x - size / 2f) - (size / 2f - r));
                    float dy = Mathf.Max(0, Mathf.Abs(y - size / 2f) - (size / 2f - r));
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = dist > r ? Color.clear : Color.white;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            roundedSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
            return roundedSprite;
        }

        private void Awake()
        {
            // 面板可能被导航多次激活，Awake 只构建一次静态表单骨架；重复 Build 会造成重复输入框、按钮监听和布局节点。
            BuildUI();
            SelectTab(0);
        }

        private void BuildUI()
        {
            // UI 按页签建立，计算逻辑仍集中在各 Calculate 方法。布局代码不承担公式选择或输入有效性判断，避免
            // 改皮肤时无意改变计算语义。
            ClearChildren(transform);
            
            // Header
            var header = CreateText("Header", transform, "回路参数估算工具", 28, FontStyle.Bold, C_TextMain);
            SetRect(header.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(400, 40));

            var subHeader = CreateText("SubHeader", transform, "用于估算负载电流、线径载流量、空开匹配和电机启动电流。结果仅用于教学参考。", 14, FontStyle.Normal, C_TextMuted);
            SetRect(subHeader.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(-80, 24));

            // Modern Segmented Control for Tabs
            var tabsContainer = CreateRect("TabsContainer", transform);
            tabsContainer.anchorMin = new Vector2(0, 1);
            tabsContainer.anchorMax = new Vector2(1, 1);
            tabsContainer.pivot = new Vector2(0.5f, 1);
            tabsContainer.anchoredPosition = new Vector2(0, -114);
            tabsContainer.sizeDelta = new Vector2(-80, 48);
            
            var bgImg = tabsContainer.gameObject.AddComponent<Image>();
            bgImg.sprite = GetRoundedSprite();
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0.92f, 0.93f, 0.94f); // #EBEBEB

            var hLayout = tabsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
            hLayout.padding = new RectOffset(4, 4, 4, 4);
            hLayout.childControlWidth = true;
            hLayout.childControlHeight = true;
            hLayout.childForceExpandWidth = true;
            hLayout.spacing = 2;

            tabsArea = tabsContainer;

            // Content Area
            contentArea = CreateRect("ContentArea", transform);
            contentArea.anchorMin = new Vector2(0, 0);
            contentArea.anchorMax = new Vector2(1, 1);
            contentArea.pivot = new Vector2(0.5f, 0.5f);
            contentArea.offsetMin = new Vector2(40, 40);
            contentArea.offsetMax = new Vector2(-40, -180);

            tabs = new Button[4];
            pages = new RectTransform[4];

            string[] tabNames = { "负载电流估算", "线径与空开估算", "电机启动估算", "多负载回路估算" };
            for (int i = 0; i < 4; i++)
            {
                var captured = i;
                tabs[i] = CreateTabButton(tabNames[i], () => SelectTab(captured));
                pages[i] = BuildPageRoot("Page_" + i);
            }

            BuildLoadCurrentPage(pages[0]);
            BuildWireBreakerPage(pages[1]);
            BuildMotorStartupPage(pages[2]);
            BuildMultiLoadPage(pages[3]);
        }

        private void SelectTab(int index)
        {
            // 页签切换只改变当前页可见性，不复用另一页的输入或结果作为隐含参数；每个计算类别保持独立的表单状态。
            currentTab = index;
            for (int i = 0; i < 4; i++)
            {
                var img = tabs[i].GetComponent<Image>();
                var txt = tabs[i].GetComponentInChildren<Text>();
                bool active = (i == index);
                
                img.color = active ? C_Card : Color.clear;
                txt.color = active ? C_Primary : C_TextMuted;
                txt.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;

                pages[i].gameObject.SetActive(active);
            }
        }

        private Button CreateTabButton(string label, UnityEngine.Events.UnityAction action)
        {
            // tab 创建时只绑定页面选择动作。计算动作绑定在对应页面按钮，避免 tab 重建或顺序调整导致公式被错误复用。
            var go = new GameObject("Tab", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(tabsArea, false);
            
            var img = go.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;

            var btn = go.GetComponent<Button>();
            btn.onClick.AddListener(action);
            
            var txt = CreateText("Text", go.transform, label, 15, FontStyle.Normal, C_TextMuted);
            txt.alignment = TextAnchor.MiddleCenter;
            SetRect(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return btn;
        }

        private RectTransform BuildPageRoot(string name)
        {
            // 每个页面拥有独立根节点和表单控件，页面切换只显隐根节点；不要将不同计算类别的 InputField 共享为同一状态。
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(contentArea, false);
            SetRect(go.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            
            var img = go.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = C_Card;

            return go.GetComponent<RectTransform>();
        }

        private void BuildLoadCurrentPage(RectTransform page)
        {
            var container = CreateScrollableContent(page);
            CreateSectionTitle(container, "参数输入区");
            
            var formLayout = CreateFormContainer(container);

            loadSupplyType = CreateDropdownRow(formLayout, "供电类型", new[] { "单相 220V", "三相 380V" });
            loadType = CreateDropdownRow(formLayout, "负载类型", new[] { "照明灯", "电风扇", "电热负载", "三相电机", "其他负载" });
            
            var powerRow = CreateRow(formLayout);
            loadPower = CreateInputFieldRaw(powerRow, "额定功率");
            loadPowerUnit = CreateDropdownRaw(powerRow, "单位", new[] { "W", "kW" });
            
            loadCosPhi = CreateInputFieldRow(formLayout, "功率因数 cosφ", "纯电阻1.0，感性约0.8");
            loadCosPhi.text = "1";
            loadEta = CreateInputFieldRow(formLayout, "效率 η", "电机默认0.85");
            loadEta.text = "1";
            loadCount = CreateInputFieldRow(formLayout, "负载数量");
            loadCount.text = "1";
            
            loadType.onValueChanged.AddListener(val => {
                if(val == 0 || val == 2) { loadCosPhi.text = "1"; loadEta.text = "1"; } // 照明/电热
                else if(val == 1) { loadCosPhi.text = "0.8"; loadEta.text = "1"; } // 风扇
                else if(val == 3) { loadCosPhi.text = "0.8"; loadEta.text = "0.85"; loadSupplyType.value = 1; } // 电机
            });

            CreateSpace(container, 10);
            CreateCalcButton(container, CalculateLoadCurrent);
            CreateSpace(container, 10);
            loadResult = CreateResultArea(container, out loadResultBg);
        }

        private double GetValidNumber(InputField field, string fieldName, bool allowZero = false)
        {
            // 输入解析的失败语义统一在此处：调用方只在得到有效数值后继续计算，不能把空文本或区域化显示字符串
            // 静默转换为零而生成看似可信的电工建议。
            if (string.IsNullOrEmpty(field.text)) throw new Exception($"请完整填写【{fieldName}】");
            if (!double.TryParse(field.text, out double val)) throw new Exception($"【{fieldName}】必须是有效数字");
            if (val < 0) throw new Exception($"【{fieldName}】不能为负数");
            if (!allowZero && val == 0) throw new Exception($"【{fieldName}】不能为 0");
            return val;
        }

        private void CalculateLoadCurrent()
        {
            // 负载电流页只使用本页已验证的输入和选定相制；它不尝试从画布推断电压、功率或功率因数。
            try
            {
                double pRaw = GetValidNumber(loadPower, "额定功率");
                double pW = loadPowerUnit.value == 1 ? pRaw * 1000 : pRaw;
                double cosPhi = GetValidNumber(loadCosPhi, "功率因数");
                double eta = GetValidNumber(loadEta, "效率");
                double count = GetValidNumber(loadCount, "负载数量");

                double singleI = 0;
                string formula = "";
                string process = "";
                
                if (loadSupplyType.value == 0) // 220V
                {
                    singleI = pW / (220 * cosPhi * eta);
                    formula = "I = P / (U × cosφ × η)";
                    process = $"I = {pW:0.##} / (220 × {cosPhi:0.##} × {eta:0.##})";
                }
                else // 380V
                {
                    singleI = pW / (1.7320508 * 380 * cosPhi * eta);
                    formula = "I = P / (√3 × U × cosφ × η)";
                    process = $"I = {pW:0.##} / (1.732 × 380 × {cosPhi:0.##} × {eta:0.##})";
                }

                double totalI = singleI * count;

                string msg = $"<size=18><b>估算结果</b></size>\n\n单个电流 I ≈ <b>{singleI:0.###} A</b>\n总电流 I_total ≈ <b><color=#000000>{totalI:0.###} A</color></b>\n\n";
                msg += $"<color=#3B82F6>■</color> <b>使用公式</b>：{formula}\n";
                msg += $"<color=#3B82F6>■</color> <b>代入过程</b>：{process}\n\n";
                msg += "<color=#4B5563><b>教学说明</b>：单相公式 U=220V，三相公式 U=380V。实际工程应用中还需考虑启动电流、线路损耗和设备真实的铭牌参数。</color>";
                ShowSuccess(loadResult, loadResultBg, msg);
            }
            catch (Exception ex)
            {
                ShowError(loadResult, loadResultBg, "参数填写错误。" + ex.Message);
            }
        }

        private void BuildWireBreakerPage(RectTransform page)
        {
            var container = CreateScrollableContent(page);
            CreateSectionTitle(container, "参数输入区");
            
            var formLayout = CreateFormContainer(container);
            wireCurrent = CreateInputFieldRow(formLayout, "负载总电流 (A)");
            wireMaterial = CreateDropdownRow(formLayout, "导线材料", new[] { "铜", "铝" });
            wireCondition = CreateDropdownRow(formLayout, "敷设条件", new[] { "普通/明敷", "穿管敷设", "高温环境", "多根并行" });
            wireCircuitType = CreateDropdownRow(formLayout, "回路类型", new[] { "普通照明/插座", "电机回路" });
            
            CreateSpace(container, 10);
            CreateCalcButton(container, CalculateWireBreaker);
            CreateSpace(container, 10);
            wireResult = CreateResultArea(container, out wireResultBg);
        }

        private void CalculateWireBreaker()
        {
            // 导线/断路器建议是教学估算，不替代工程选型或保护协调。结果文案应保留输入前提，不能被其他页面当作
            // 活动电路的真实额定值。
            try
            {
                double i = GetValidNumber(wireCurrent, "负载总电流");

                bool isCopper = wireMaterial.value == 0;
                double minDensity = isCopper ? 5 : 3;
                double maxDensity = isCopper ? 8 : 5;
                
                string condTip = "";
                if (wireCondition.value == 1) { maxDensity -= 1; condTip = "穿管散热差，采用保守电流密度。"; }
                if (wireCondition.value == 2) { maxDensity -= 1; minDensity -= 0.5; condTip = "高温降额，需增加截面积。"; }
                if (wireCondition.value == 3) { maxDensity -= 1; minDensity -= 0.5; condTip = "多根并行互相加热，需降额。"; }

                double minArea = i / maxDensity;
                double maxArea = i / minDensity;
                string mat = isCopper ? "铜" : "铝";
                double breakerMultiplier = wireCircuitType.value == 1 ? 1.5 : 1.2;
                double recommendedBreaker = i * breakerMultiplier;

                string msg = $"<size=18><b>估算结果</b></size>\n\n推荐线径范围：<b>{minArea:0.##} mm² ～ {maxArea:0.##} mm²</b> ({mat}线)\n";
                msg += $"常见可选线径提示：1.5, 2.5, 4, 6, 10, 16 mm²（建议向上取整）\n";
                msg += $"空开建议范围：约 <b>{recommendedBreaker:0.#} A</b>\n\n";
                
                msg += $"<color=#3B82F6>■</color> <b>代入过程</b>：截面积 S = 电流 I / 安全载流密度\n按 {mat}线 {minDensity:0.#}~{maxDensity:0.#} A/mm² 估算：S = {i} / {maxDensity:0.#} ~ {minDensity:0.#}\n\n";
                msg += $"<color=#4B5563><b>教学说明</b>：{condTip} 如果为电机回路，空开额定电流需适当放大以躲过启动电流。</color>\n\n";
                msg += "<color=#EF4444><b>注意事项</b>：该估算仅供参考。工程中必须查阅《电工手册》并保证空开动作电流小于导线极限载流量。</color>";
                
                ShowSuccess(wireResult, wireResultBg, msg);
            }
            catch (Exception ex)
            {
                ShowError(wireResult, wireResultBg, "参数填写错误。" + ex.Message);
            }
        }

        private void BuildMotorStartupPage(RectTransform page)
        {
            var container = CreateScrollableContent(page);
            CreateSectionTitle(container, "参数输入区");
            
            var formLayout = CreateFormContainer(container);
            motorPower = CreateInputFieldRow(formLayout, "电机功率 (kW)");
            motorVoltage = CreateInputFieldRow(formLayout, "线电压 U (V)");
            motorVoltage.text = "380";
            motorCosPhi = CreateInputFieldRow(formLayout, "功率因数 cosφ");
            motorCosPhi.text = "0.8";
            motorEta = CreateInputFieldRow(formLayout, "效率 η");
            motorEta.text = "0.85";
            motorStartupType = CreateDropdownRow(formLayout, "启动方式", new[] { "直接启动", "星三角启动" });
            
            CreateSpace(container, 10);
            CreateCalcButton(container, CalculateMotorStartup);
            CreateSpace(container, 10);
            motorResult = CreateResultArea(container, out motorResultBg);
        }

        private void CalculateMotorStartup()
        {
            // 电机起动页把额定参数和起动倍数显式组合；缺少安全边界时应显示输入错误，而不是以默认值伪造起动电流。
            try
            {
                double pKw = GetValidNumber(motorPower, "电机功率");
                double u = GetValidNumber(motorVoltage, "线电压");
                double cosPhi = GetValidNumber(motorCosPhi, "功率因数");
                double eta = GetValidNumber(motorEta, "效率");

                double pW = pKw * 1000;
                double iRated = pW / (1.7320508 * u * cosPhi * eta);
                double directMin = iRated * 5;
                double directMax = iRated * 7;

                string msg = $"<size=18><b>估算结果</b></size>\n\n额定运行电流 I_rated ≈ <b>{iRated:0.###} A</b>\n";
                msg += $"直接启动电流估算：<b><color=#EF4444>{directMin:0.###} A ～ {directMax:0.###} A</color></b>\n";

                if (motorStartupType.value == 1)
                {
                    double starMin = directMin / 3.0;
                    double starMax = directMax / 3.0;
                    msg += $"星三角星形阶段电流估算：<b><color=#10B981>{starMin:0.###} A ～ {starMax:0.###} A</color></b>\n";
                }
                
                msg += $"\n<color=#3B82F6>■</color> <b>使用公式</b>：I_rated = P / (√3 × U × cosφ × η)\n";
                msg += $"<color=#3B82F6>■</color> <b>代入过程</b>：I_rated = {pW} / (1.732 × {u} × {cosPhi} × {eta})\n\n";
                msg += "<color=#4B5563><b>教学说明</b>：直接启动电流通常是额定的5~7倍。星三角启动通过降低启动阶段绕组电压来减小启动电流，线电流约为直接启动的 1/3。可结合软件内的“星三角降压启动”模板进行验证。</color>";
                
                ShowSuccess(motorResult, motorResultBg, msg);
            }
            catch (Exception ex)
            {
                ShowError(motorResult, motorResultBg, "参数填写错误。" + ex.Message);
            }
        }

        private void BuildMultiLoadPage(RectTransform page)
        {
            var container = CreateScrollableContent(page);
            CreateSectionTitle(container, "参数输入区");
            
            var formLayout = CreateFormContainer(container);
            multiSupplyType = CreateDropdownRow(formLayout, "供电类型", new[] { "单相 220V", "三相 380V" });
            
            var row1 = CreateRow(formLayout);
            multiLightCount = CreateInputFieldRaw(row1, "照明灯数量", "0");
            multiLightPower = CreateInputFieldRaw(row1, "单灯功率(W)", "0");

            var row2 = CreateRow(formLayout);
            multiFanCount = CreateInputFieldRaw(row2, "风扇数量", "0");
            multiFanPower = CreateInputFieldRaw(row2, "单风扇功率(W)", "0");
            
            var row3 = CreateRow(formLayout);
            multiMotorCount = CreateInputFieldRaw(row3, "电机数量", "0");
            multiMotorPower = CreateInputFieldRaw(row3, "单电机(kW)", "0");

            var row4 = CreateRow(formLayout);
            multiOtherPower = CreateInputFieldRaw(row4, "其他功率(W)", "0");
            multiCosPhi = CreateInputFieldRaw(row4, "平均功率因数", "0.8");
            multiCosPhi.text = "0.8";
            
            CreateSpace(container, 10);
            CreateCalcButton(container, CalculateMultiLoad);
            CreateSpace(container, 10);
            multiResult = CreateResultArea(container, out multiResultBg);
        }

        private void CalculateMultiLoad()
        {
            // 多负载结果只汇总当前表单行，行的显示顺序和 UI 容器不构成电气拓扑，也不会写入任何保存文件。
            try
            {
                double GetValOrZero(InputField f, string name)
                {
                    if (string.IsNullOrEmpty(f.text)) return 0;
                    if (!double.TryParse(f.text, out double val)) throw new Exception($"【{name}】必须是有效数字");
                    if (val < 0) throw new Exception($"【{name}】不能为负数");
                    return val;
                }
                
                double lCount = GetValOrZero(multiLightCount, "照明灯数量"); double lPower = GetValOrZero(multiLightPower, "单灯功率");
                double fCount = GetValOrZero(multiFanCount, "风扇数量"); double fPower = GetValOrZero(multiFanPower, "单风扇功率");
                double mCount = GetValOrZero(multiMotorCount, "电机数量"); double mPower = GetValOrZero(multiMotorPower, "单电机功率");
                double oPower = GetValOrZero(multiOtherPower, "其他功率");
                double cosPhi = GetValOrZero(multiCosPhi, "平均功率因数");
                
                if (cosPhi <= 0) cosPhi = 0.8;
                
                double totalPowerW = (lCount * lPower) + (fCount * fPower) + (mCount * mPower * 1000) + oPower;
                double u = multiSupplyType.value == 0 ? 220 : 380;
                double factor = multiSupplyType.value == 0 ? 1 : 1.7320508;
                
                double totalI = totalPowerW / (factor * u * cosPhi);
                double lI = (lCount * lPower) / (factor * u * 1.0); 
                double fI = (fCount * fPower) / (factor * u * 0.8);
                double mI = (mCount * mPower * 1000) / (factor * u * 0.8 * 0.85);

                string msg = $"<size=18><b>估算结果</b></size>\n\n";
                if(lCount > 0) msg += $"照明电流约 {lI:0.###} A\n";
                if(fCount > 0) msg += $"风扇电流约 {fI:0.###} A\n";
                if(mCount > 0) msg += $"电机电流约 {mI:0.###} A\n";
                msg += $"总估算电流 I_total ≈ <b><color=#000000>{totalI:0.###} A</color></b>\n\n";
                
                msg += $"<color=#3B82F6>■</color> <b>代入过程</b>：总功率 P_total = {totalPowerW} W\n";
                if (multiSupplyType.value == 0) msg += $"总电流 I = {totalPowerW} / (220 × {cosPhi})\n\n";
                else msg += $"总电流 I = {totalPowerW} / (1.732 × 380 × {cosPhi})\n\n";
                
                bool exceed = totalI > 16;
                msg += "<color=#F59E0B><b>判断提示</b></color>：";
                msg += exceed ? "<color=#EF4444>总电流较大（>16A），可能超过普通插座回路常见线径（2.5mm²）的范围！</color>\n\n" : "总电流在普通回路常见范围内。\n\n";
                
                msg += "<color=#4B5563><b>教学说明</b>：多个负载并联接入同一回路时，总电流近似为各负载电流之和或总功率求得的总电流。建议结合“线径与空开估算”进一步判断线路是否安全。</color>";
                
                ShowSuccess(multiResult, multiResultBg, msg);
            }
            catch (Exception ex)
            {
                ShowError(multiResult, multiResultBg, "参数填写错误。" + ex.Message);
            }
        }

        private void ShowError(Text uiText, RectTransform bg, string msg)
        {
            // 错误展示不抛出异常，也不保留上一轮成功结果的颜色。解析失败必须显式覆盖结果区域，提示用户修正输入。
            uiText.color = C_ErrorText;
            uiText.text = "<b>出错了</b>\n" + msg;
            
            var img = bg.GetComponent<Image>();
            img.color = C_ErrorBg;
        }

        private void ShowSuccess(Text uiText, RectTransform bg, string msg)
        {
            // 成功文本只代表当前点击时的表单计算，不是电路仿真结论；页面切换、重新输入或销毁面板后不应被持久化。
            uiText.color = C_ResultText;
            uiText.text = msg;
            
            var img = bg.GetComponent<Image>();
            img.color = C_ResultBg;
        }

        private RectTransform CreateFormContainer(RectTransform parent)
        {
            // 表单容器统一交给 LayoutGroup 排布，字段新增时不要手工累积绝对坐标，否则小窗口下滚动高度会与实际内容脱节。
            var form = new GameObject("FormContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            form.transform.SetParent(parent, false);
            var layout = form.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 16;
            return form.GetComponent<RectTransform>();
        }

        private RectTransform CreateRow(RectTransform parent)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = false;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.spacing = 20;
            var element = row.GetComponent<LayoutElement>();
            element.minHeight = 44f;
            return row.GetComponent<RectTransform>();
        }

        private RectTransform CreateSpace(RectTransform parent, float height)
        {
            var space = new GameObject("Space", typeof(RectTransform), typeof(LayoutElement));
            space.transform.SetParent(parent, false);
            space.GetComponent<LayoutElement>().minHeight = height;
            return space.GetComponent<RectTransform>();
        }

        private RectTransform CreateScrollableContent(RectTransform parent)
        {
            // ScrollRect 仅解决表单可达性；Content 高度由 layout 决定，不能为了视觉调整手工篡改输入数据或计算结果。
            var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scroll.transform.SetParent(parent, false);
            SetRect(scroll.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            scroll.GetComponent<Image>().color = new Color(0,0,0,0.01f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(scroll.transform, false);
            SetRect(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            viewport.GetComponent<Image>().color = new Color(0,0,0,0.01f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var rect = content.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.offsetMin = new Vector2(0, 0);
            rect.offsetMax = new Vector2(0, 0);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 32, 40);
            layout.spacing = 16;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = scroll.GetComponent<ScrollRect>();
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.content = rect;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 30f;

            return rect;
        }

        private Text CreateSectionTitle(RectTransform parent, string title)
        {
            var txt = CreateText("SectionTitle", parent, title, 16, FontStyle.Bold, C_TextMain);
            var layoutElement = txt.gameObject.AddComponent<LayoutElement>();
            layoutElement.minHeight = 24f;
            return txt;
        }

        private InputField CreateInputFieldRow(RectTransform parent, string labelText, string placeholderText = "")
        {
            // 行级工厂只创建输入表现和标签；数值范围、单位换算和业务前提由 Calculate 方法及 GetValidNumber 统一管理。
            var row = CreateRow(parent);

            var label = CreateText("Label", row.transform, labelText, 15, FontStyle.Normal, C_TextMuted);
            var leLabel = label.gameObject.AddComponent<LayoutElement>();
            leLabel.preferredWidth = 140f;
            label.alignment = TextAnchor.MiddleLeft;

            var inputBg = new GameObject("InputBg", typeof(RectTransform), typeof(Image));
            inputBg.transform.SetParent(row.transform, false);
            
            var img = inputBg.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = C_InputBg;
            var leInput = inputBg.AddComponent<LayoutElement>();
            leInput.preferredWidth = 260f; // Fixed beautiful width

            var inputText = CreateText("Text", inputBg.transform, "", 15, FontStyle.Normal, C_TextMain);
            SetRect(inputText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(12, 0), new Vector2(-24, 0));
            inputText.alignment = TextAnchor.MiddleLeft;

            var inputField = inputBg.AddComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.contentType = InputField.ContentType.DecimalNumber;

            if (!string.IsNullOrEmpty(placeholderText))
            {
                var ph = CreateText("Placeholder", inputBg.transform, placeholderText, 14, FontStyle.Normal, new Color(0.6f, 0.65f, 0.7f));
                SetRect(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(12, 0), new Vector2(-24, 0));
                ph.alignment = TextAnchor.MiddleLeft;
                inputField.placeholder = ph;
            }

            return inputField;
        }

        private InputField CreateInputFieldRaw(RectTransform parentRow, string labelText, string placeholderText = "")
        {
            var label = CreateText("Label", parentRow, labelText, 14, FontStyle.Normal, C_TextMuted);
            var leLabel = label.gameObject.AddComponent<LayoutElement>();
            leLabel.preferredWidth = 100f;
            label.alignment = TextAnchor.MiddleLeft;

            var inputBg = new GameObject("InputBg", typeof(RectTransform), typeof(Image));
            inputBg.transform.SetParent(parentRow, false);
            
            var img = inputBg.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = C_InputBg;
            
            var leInput = inputBg.AddComponent<LayoutElement>();
            leInput.preferredWidth = 120f;

            var inputText = CreateText("Text", inputBg.transform, "", 15, FontStyle.Normal, C_TextMain);
            SetRect(inputText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(-20, 0));
            inputText.alignment = TextAnchor.MiddleLeft;

            var inputField = inputBg.AddComponent<InputField>();
            inputField.textComponent = inputText;
            inputField.contentType = InputField.ContentType.DecimalNumber;

            if (!string.IsNullOrEmpty(placeholderText))
            {
                var ph = CreateText("Placeholder", inputBg.transform, placeholderText, 14, FontStyle.Normal, new Color(0.6f, 0.65f, 0.7f));
                SetRect(ph.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(10, 0), new Vector2(-20, 0));
                ph.alignment = TextAnchor.MiddleLeft;
                inputField.placeholder = ph;
            }

            return inputField;
        }

        private Dropdown CreateDropdownRow(RectTransform parent, string labelText, string[] options)
        {
            // 下拉选项是当前计算页的显式输入，而非全局应用设置。更新选项时需同步对应公式分支，不可由显示文本猜测单位。
            var row = CreateRow(parent);

            var label = CreateText("Label", row.transform, labelText, 15, FontStyle.Normal, C_TextMuted);
            var leLabel = label.gameObject.AddComponent<LayoutElement>();
            leLabel.preferredWidth = 140f;
            label.alignment = TextAnchor.MiddleLeft;

            return CreateBuiltinDropdown(row.transform, options, 260f);
        }

        private Dropdown CreateDropdownRaw(RectTransform parentRow, string labelText, string[] options)
        {
            var label = CreateText("Label", parentRow, labelText, 14, FontStyle.Normal, C_TextMuted);
            var leLabel = label.gameObject.AddComponent<LayoutElement>();
            leLabel.preferredWidth = 60f;
            label.alignment = TextAnchor.MiddleLeft;

            return CreateBuiltinDropdown(parentRow, options, 120f);
        }

        private Dropdown CreateBuiltinDropdown(Transform parent, string[] options, float width)
        {
            GameObject go = UnityEngine.UI.DefaultControls.CreateDropdown(new UnityEngine.UI.DefaultControls.Resources());
            go.transform.SetParent(parent, false);
            
            var img = go.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = C_InputBg;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            
            var dropdown = go.GetComponent<Dropdown>();
            dropdown.options.Clear();
            foreach(var opt in options) dropdown.options.Add(new Dropdown.OptionData(opt));
            
            var label = go.transform.Find("Label").GetComponent<Text>();
            label.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 15;
            label.color = C_TextMain;
            
            var template = go.transform.Find("Template");
            var itemLabel = template.Find("Viewport/Content/Item/Item Label").GetComponent<Text>();
            itemLabel.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            itemLabel.fontSize = 15;
            itemLabel.color = C_TextMain;
            
            return dropdown;
        }

        private Button CreateCalcButton(RectTransform parent, UnityEngine.Events.UnityAction action)
        {
            var btnGo = new GameObject("CalcBtn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            btnGo.transform.SetParent(parent, false);
            
            var img = btnGo.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = C_Primary;

            var le = btnGo.GetComponent<LayoutElement>();
            le.minHeight = 48f;
            le.preferredWidth = 420f;
            
            var btn = btnGo.GetComponent<Button>();
            btn.onClick.AddListener(action);

            var txt = CreateText("Text", btnGo.transform, "开始估算", 16, FontStyle.Bold, Color.white);
            txt.alignment = TextAnchor.MiddleCenter;
            SetRect(txt.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return btn;
        }

        private Text CreateResultArea(RectTransform parent, out RectTransform bgRect)
        {
            // 结果区域与输入区分离，便于成功/错误样式整体更新；它不保存中间数值，真实计算状态只存在于本次按钮调用栈中。
            var bg = new GameObject("ResultBg", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            bg.transform.SetParent(parent, false);
            bgRect = bg.GetComponent<RectTransform>();

            var img = bg.GetComponent<Image>();
            img.sprite = GetRoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = C_Bg; // Default to normal bg

            var layout = bg.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);

            var txt = CreateText("ResultText", bg.transform, "<color=#9CA3AF>请在上方输入参数并点击“开始估算”</color>", 15, FontStyle.Normal, C_TextMuted);
            txt.alignment = TextAnchor.UpperLeft;
            txt.supportRichText = true;
            
            var le = txt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 120f;
            return txt;
        }

        private Text CreateText(string name, Transform parent, string value, int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = value;
            text.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.lineSpacing = 1.4f;
            text.supportRichText = true;
            var csf = go.GetComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return text;
        }

        private void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private void ClearChildren(Transform parent)
        {
            // 只清理本控制器生成的页面子节点。调用前需确认 parent 属于计算器，避免通用工具重建时销毁导航或场景对象。
            foreach (Transform child in parent) Destroy(child.gameObject);
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }
    }
}
