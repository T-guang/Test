# Unity 电工数字学生仿真系统 字体体系 + UI 容器尺寸 + 文本溢出风险联合审计报告

> 审计范围：只检查，不修改代码、不修改布局、不修改功能、不修改模板 JSON、不修改检测规则。
>
> 说明：任务中给出的两个脚本路径与项目实际路径略有差异：
> - `LocalInspectorPanel.cs` 实际位于 `Assets/Scripts/AI/LocalInspectorPanel.cs`
> - `CommonToolsPageController.cs` 实际位于 `Assets/Scripts/UI/CommonTools/CommonToolsPageController.cs`

---

## 一、审计结论摘要

### 1. 当前总体结论

当前项目的字体体系不是单一体系，而是三套并存：

1. **品牌/标题字体链**
   - `MainUiTheme.TitleFont`
   - 通过 `Resources.Load<Font>()` 多路径查找猫啃风相关资源
   - 未命中时回退 `LegacyRuntime.ttf`

2. **普通 UI 字体链**
   - `MainUiTheme.UiFont`
   - 通过 `Font.CreateDynamicFontFromOSFont(...)` 动态创建
   - 优先顺序为：`Microsoft YaHei UI / Microsoft YaHei / Source Han Sans SC / Noto Sans CJK SC / Arial`

3. **历史正文/兼容字体链**
   - `MainUiTheme.BodyFont`
   - 当前仍回退为 `LegacyRuntime.ttf`

### 2. 当前真正的问题

本次审计结论不是“有没有字体”，而是：

- 同一项目不同页面字体来源不统一
- 同一页面内标题、按钮、正文也可能来自不同字体链
- 多个按钮和卡片标题尺寸非常紧，当前之所以没炸，部分依赖 `LegacyRuntime.ttf` 较窄的字宽或 `BestFit`
- 一旦统一切到微软雅黑 UI、思源黑体等更宽的清晰字体，部分区域会立即出现：
  - 按钮文字裁切
  - 标题提早换行
  - 卡片高度不足
  - 图标 + 文字挤压
  - 筛选按钮视觉失衡

### 3. 后续统一字体时的高风险区

按风险排序，最容易先出问题的区域是：

1. 模拟电路主界面顶部工具栏按钮
2. 图纸集页面筛选按钮
3. 仿真广场卡片标题与卡片按钮
4. 常用工具页面的色环颜色按钮
5. 个人中心长按钮
6. 左侧控件池的“全部”按钮

### 4. 当前构建状态

- `dotnet build Assembly-CSharp.csproj --no-restore`：已通过
- 本次仅写文档，不涉及代码改动，因此不触发额外功能回归

---

## 二、字体资产清单

### 2.1 项目内字体文件

| 字体文件 | 路径 | 当前用途判断 | 备注 |
|---|---|---|---|
| 猫啃风雅宋 ttf | `Assets/Fonts/MaokenFengyasong/maoken_fengyasong.ttf` | 设计期引入，供标题类字体使用 | 未发现 TMP 资产 |
| Maoken.ttf | `Assets/Resources/UI/Fonts/Maoken.ttf` | 已被场景直接序列化引用 | `Demo.unity` 中命中 guid |
| MaokenFengyaSong.ttf | `Assets/Resources/Fonts/MaokenFengyaSong.ttf` | 资源回退链候选 | 供 `Resources.Load<Font>()` |
| maoken_fengyasong.ttf | `Assets/Resources/Fonts/MaokenFengyasong/maoken_fengyasong.ttf` | 资源回退链候选 | 供 `Resources.Load<Font>()` |

### 2.2 内置/运行时字体来源

| 来源 | 调用方式 | 当前用途 |
|---|---|---|
| `LegacyRuntime.ttf` | `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` | 当前大量正文、按钮、列表、说明文字默认 fallback |
| 系统字体 | `Font.CreateDynamicFontFromOSFont(...)` | `MainUiTheme.UiFont`，主要用于导航和部分更清晰的普通 UI |

### 2.3 TextMeshPro 状态

本次审计未检出以下内容：

- `TMP_Text`
- `TMP_FontAsset`
- `TextMeshProUGUI`
- `enableAutoSizing`
- `overflowMode`

**结论：当前审计范围内 UI 文本体系基本全部基于 Unity Legacy `Text`。**

### 2.4 Inspector / Scene 序列化字体引用

已确认以下情况：

- `Assets/Scenes/Demo.unity`
- `Assets/Scenes/LoginScene.unity`
- 少量 Prefab

存在大量 `LegacyRuntime.ttf` 的序列化引用。

另外确认到一组自定义字体引用：

- `Assets/Scenes/Demo.unity`
- `m_Font guid = 39a0f033c31b2fb4d804eb3c3bfd3f14`
- 对应 `Assets/Resources/UI/Fonts/Maoken.ttf.meta`

**结论：场景中至少已有部分 Text 直接绑定 `Maoken.ttf`。**

---

## 三、字体调用清单

### 3.1 全局字体入口

#### `Assets/Scripts/UI/MainUiTheme.cs`
- `TitleFont`
  - `Resources.Load<Font>(...)`
  - 多路径查找 Maoken 系列
- `UiFont`
  - `Font.CreateDynamicFontFromOSFont(...)`
- `BodyFont`
  - `LegacyRuntime.ttf`
- `ApplyText(...)`
  - 统一设置：
    - `font`
    - `fontSize`
    - `fontStyle`
    - `alignment`
    - `verticalOverflow = Overflow`

### 3.2 主要页面字体设置方式

| 文件 | 字体相关方式 | 结论 |
|---|---|---|
| `TopNavigationController.cs` | 直接使用 `MainUiTheme.UiFont` | 导航相对清晰 |
| `DemoUIController.cs` | 混用 `MainUiTheme.BodyFont / UiFont / ApplyText / BestFit` | 风险最高，策略不一致 |
| `PaletteController.cs` | 标题用 `TitleFont`，其余多处沿用 `ApplyText` 和默认字体 | 中风险 |
| `LocalInspectorPanel.cs` | 按钮文本显式设 `BodyFont`，并开 `BestFit` | 中风险 |
| `BlueprintController.cs` | 基本使用 `MainUiTheme.BodyFont` | 图纸集按钮/标题紧张 |
| `SimulationGalleryPageController.cs` | 整体绑定 `MainUiTheme.BodyFont` | 卡片标题和按钮有换行风险 |
| `EncyclopediaController.cs` | 自己实现 `ResolveTitleFont()/ResolveBodyFont()` | 脱离 `MainUiTheme` 统一策略 |
| `CommonToolsPageController.cs` | 大量直接 `LegacyRuntime.ttf` | 与其他页面风格差异最大 |
| `LocalProfilePageController.cs` | 大量直接 `LegacyRuntime.ttf` | 按钮长文案风险明显 |

### 3.3 关键字体调用模式

#### 已检出
- `text.font = ...`
- `text.fontSize = ...`
- `text.fontStyle = ...`
- `text.lineSpacing = ...`
- `text.alignment = ...`
- `text.horizontalOverflow = ...`
- `text.verticalOverflow = ...`
- `text.resizeTextForBestFit = ...`
- `Resources.Load<Font>(...)`
- `Font.CreateDynamicFontFromOSFont(...)`
- `AddComponent<Text>()`
- `GetComponent<Text>()`

#### 未检出
- TMP / TMP_FontAsset
- TMP AutoSize / overflowMode

---

## 四、UI 文本控件尺寸审计

> 说明：以下聚焦主要动态文本控件和最容易产生风险的容器。对于 LayoutGroup 驱动的控件，部分 `anchoredPosition / sizeDelta` 由布局系统接管，表中以“布局控制”标注。

### 4.1 MainUiTheme / TopNavigation / 全局顶部导航

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `TopNavigationController.CreateBrandGroup` | 全局顶部栏 | 品牌区 | 系统标题 | Label | 电工数字学生仿真系统 | `UiFont` | 18 | Bold | 默认 | MiddleLeft | 默认 | Overflow | 关闭 | 标题偏好 `300x38`，BrandGroup `390x44` | BrandGroup anchored `(24,11)`，Logo `36x36` | 顶部导航栏 | HorizontalLayoutGroup | Logo + 标题间距 | 否优先 | 中 | 中 | `AppTitle` |
| `TopNavigationController.CreateNavTabs` | 全局顶部栏 | 导航区 | 导航项 | Button | 模拟电路 / 图纸集 / 仿真广场 / 元器件百科 / 常用工具 / 个人中心 | `UiFont` | 16 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | 高 `36`，宽度由容器分配 | 布局控制 | 顶部导航栏 | HorizontalLayoutGroup | 左右 padding | 否优先 | 中 | 中 | `NavText` / `NavTextSelected` |

### 4.2 `Assets/Scripts/UI/DemoUIController.cs`（模拟电路顶部工具栏）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `EnsureToolbarLayout` | 模拟电路 | 顶部工具栏 | 主按钮 | Button | 开始仿真 / 结束仿真 | 最终样式为 `UiFont` | 15 | Bold | 默认 | MiddleCenter | Overflow | Overflow | 最终关闭 | `128x40` | 布局控制 | LeftActionGroup | HorizontalLayoutGroup | 图标约 20~24，文本左偏移约 46 | 否 | 中 | 中 | `ToolbarPrimaryButtonText` |
| `EnsureToolbarLayout` | 模拟电路 | 顶部工具栏 | 普通按钮 | Button | 撤销 / 重做 / 锁定 | 最终样式为 `UiFont` | 15 | Bold | 默认 | MiddleCenter | Overflow | Overflow | 最终关闭 | `92x40` | 布局控制 | LeftActionGroup | HorizontalLayoutGroup | 图标占位后文本区明显缩窄 | 否 | 高 | 高 | `ToolbarButtonText` |
| `EnsureToolbarLayout` | 模拟电路 | 顶部工具栏 | 危险按钮 | Button | 删除 / 清线 / 清空 | 最终样式为 `UiFont` | 15 | Bold | 默认 | MiddleCenter | Overflow | Overflow | 最终关闭 | `92x40` | 布局控制 | LeftActionGroup | HorizontalLayoutGroup | 图标 + 文本区紧 | 否 | 高 | 高 | `ToolbarDangerButtonText` |
| `EnsureToolbarLayout` | 模拟电路 | 顶部工具栏 | 图纸按钮 | Button | 加载图纸 / 保存图纸 / 导入图纸 | 最终样式为 `UiFont` | 15 | Bold | 默认 | MiddleCenter | Overflow | Overflow | 最终关闭 | `118x40` | 布局控制 | RightActionGroup | HorizontalLayoutGroup | 图标 + 文本 | 否 | 中 | 中 | `ToolbarButtonText` |
| `CreateWireColorGroup` | 模拟电路 | 顶部工具栏 | 标签 | Label | 导线颜色 | `UiFont` | 14 | Bold | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | `72x28` | anchored 左中 | WireColorGroup | HorizontalLayoutGroup | 与色块并排 | 否 | 低 | 低 | `Caption` |

#### 重点判断
- 顶部工具栏是全项目最高风险区之一。
- 原因：按钮宽度小、图标占位固定、最终关闭 BestFit、统一更换为宽字体后极易超宽。
- `删除 / 清线 / 清空 / 撤销 / 重做 / 锁定` 为本区最敏感控件。

### 4.3 `Assets/Scripts/UI/PaletteController.cs`（左侧控件池 + 操作记录）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `EnsurePaletteHeader` | 模拟电路 | 左侧栏 | 面板标题 | PanelTitle | 电工控件池 | `TitleFont` | 22 | Bold | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | 顶部标题区 | anchored 顶部 | 左侧控件池 | 无 | 左侧蓝色竖条 | 否 | 低 | 低 | `SectionTitle` |
| `CreateFilterButton` | 模拟电路 | 左侧栏分类 | 分类按钮 | Button | 全部 | `UiFont` | 14 | Bold | 默认 | MiddleLeft | Wrap | Truncate | 关闭 | `72x32` | 布局控制 | FilterRow | HorizontalLayoutGroup | 图标 `18x18`，文本区约 40 宽 | 否优先 | 高 | 高 | `NavText` |
| `CreateFilterButton` | 模拟电路 | 左侧栏分类 | 分类按钮 | Button | 家庭电路组件 / 工业电路组件 | `UiFont` | 14 | Bold | 默认 | MiddleLeft | Wrap | Truncate | 关闭 | `136x32` | 布局控制 | FilterRow | HorizontalLayoutGroup | 图标 `18x18` | 否优先 | 中 | 中 | `NavText` |
| `CreatePaletteCard` | 模拟电路 | 元件网格 | 卡片名称 | CardTitle | 交流接触器 / 热继电器 / 双联按钮 | `ApplyText` 默认 body | 13 | Normal | 默认 | MiddleCenter | Wrap | Truncate | 开启，12~13 | 卡片 `96x132`，文本区约 `84x36` | anchored 卡片内部 | Grid 卡片 | GridLayoutGroup | 图标独立区域 | 是 | 中 | 中 | `CardTitle` |
| `EnsureActionLogLayout` | 模拟电路 | 操作记录 | 标题 | PanelTitle | 操作记录 | `ApplyText` | 16 | Bold | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | 标题栏高 `36` | anchored 顶部 | ActionLogPanel | 无 | 左右 padding 充足 | 否 | 低 | 低 | `SectionTitle` |
| `EnsureActionLogLayout` | 模拟电路 | 操作记录 | 正文 | LogText | `[14:09:08] 电路检查完成` | `ApplyText` 默认 body | 13 | Normal | 1.3 | UpperLeft | Wrap | Overflow | 关闭 | 大滚动文本区 | ScrollRect 内文本 | ActionLogViewport | ScrollRect | 内边距 8~10 | 是 | 低 | 低 | `LogBody` |

#### 重点判断
- 左侧栏真正高风险的是“全部”按钮。
- 元件卡片名称虽然空间不大，但开启了 BestFit，风险可控。
- 操作记录正文是可读性优先区，不是当前溢出高危区。

### 4.4 `Assets/Scripts/AI/LocalInspectorPanel.cs`（右侧检查助手）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `BuildUi` | 模拟电路 | 右侧检查助手 | 标题 | PanelTitle | 检查助手 | `ApplyText` 后显式调整 | 17 | Bold | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | 标题面板高约 `48` | VerticalLayoutGroup 控制 | InspectorRoot | VerticalLayoutGroup | 左右内边距 | 否 | 低 | 低 | `InspectorTitle` |
| `CreateActionButton` | 模拟电路 | 右侧检查助手 | 操作按钮 | Button | 当前电路解释 | `BodyFont` | 14 上限 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 开启，10~14 | 高 `36`，宽由面板控制 | 布局控制 | QuickActions | VerticalLayoutGroup | 图标 `18x18`，文本左偏移约 40 | 否优先 | 中 | 中 | `InspectorButton` |
| `CreateActionButton` | 模拟电路 | 右侧检查助手 | 操作按钮 | Button | 检查当前电路 | `BodyFont` | 14 上限 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 开启，10~14 | 同上 | 同上 | QuickActions | VerticalLayoutGroup | 图标 `18x18` | 否优先 | 中 | 中 | `InspectorButton` |
| `CreateActionButton` | 模拟电路 | 右侧检查助手 | 操作按钮 | Button | 清空结果 | `BodyFont` | 14 上限 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 开启，10~14 | 同上 | 同上 | QuickActions | VerticalLayoutGroup | 图标 `18x18` | 否优先 | 低 | 低 | `InspectorButton` |
| `CreateActionButton` | 模拟电路 | 右侧检查助手 | 操作按钮 | Button | 提交练习检测 | `BodyFont` | 14 上限 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 开启，10~14 | 同上 | 同上 | QuickActions | VerticalLayoutGroup | 文本最长，图标占位后更紧 | 否优先 | 高 | 高 | `InspectorButton` |
| `CreateReportCard` | 模拟电路 | 右侧检查助手 | 报告标题 | CardTitle | 最新检查报告 / 教学说明 | `ApplyText` | 16 | Bold | 默认 | UpperLeft | 默认 | 默认 | 关闭 | 卡片宽约 320 内部 | 布局控制 | ReportContent | VerticalLayoutGroup | 内边距 12 | 可一行/两行 | 低 | 低 | `InspectorTitle` |
| `CreateReportCard` | 模拟电路 | 右侧检查助手 | 报告正文 | CardBody | 多段检查报告正文 | `ApplyText` | 14 | Normal | 默认 | UpperLeft | Wrap | Overflow | 关闭 | 可滚动区域 | ScrollRect 内 | ReportContent | VerticalLayoutGroup | 内边距 12 | 是 | 低 | 低 | `InspectorBody` |

#### 重点判断
- 检查助手最大的风险不在报告正文，而在顶部按钮区。
- 报告正文有换行和滚动，安全性较高。
- `提交练习检测` 是这一页最敏感的按钮文案。

### 4.5 `Assets/Scripts/UI/BlueprintController.cs`（图纸集）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `BuildFilters` | 图纸集 | 顶部筛选 | 电路类型按钮 | Button | 家庭电路图纸 / 工业电路图纸 | `BodyFont` | 20 | Bold | 默认 | MiddleCenter | Overflow | Overflow | 关闭 | `175x36` | 布局控制 | FilterPanel | Horizontal/Vertical Layout 组合 | 可能带计数 | 否 | 高 | 高 | `NavText` |
| `BuildFilters` | 图纸集 | 顶部筛选 | 难度按钮 | Button | 全部图纸 / 初级图纸 / 中级图纸 / 高级图纸 | `BodyFont` | 20 | Bold | 默认 | MiddleCenter | Overflow | Overflow | 关闭 | `138x36` | 布局控制 | FilterPanel | 同上 | 计数徽标同排 | 否 | 高 | 高 | `NavText` |
| `BuildFilters` | 图纸集 | 顶部筛选 | 搜索框 | SearchInput | 输入图纸名称 | `BodyFont` | 16 | Normal | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | `236x36` | anchored 右侧 | FilterPanel | 无 | 左图标可能存在 | 否 | 低 | 低 | `DialogBody` |
| `CreateCard` | 图纸集 | 卡片区 | 卡片标题 | CardTitle | 电动机连续运行控制电路 | `BodyFont` | 20 | Bold | 默认 | UpperLeft | 默认 | 默认 | 关闭 | 卡片 `446x336`，标题区较宽 | anchored 卡片内部 | Card | 无 | 与缩略图区分层 | 可 1~2 行 | 中 | 中 | `CardTitle` |
| `CreateCard` | 图纸集 | 卡片区 | 类型/难度标签 | Caption | 工业电路 / 初级 | `BodyFont` | 14 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | 底部标签 | anchored 底部行 | Card | HorizontalLayoutGroup | 左右 padding 小 | 否 | 低 | 低 | `Caption` |
| `CreateCard` | 图纸集 | 卡片区 | 行动按钮 | Button | 进入练习 | `BodyFont` | 20 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | 约 `120x36` 主按钮 | anchored 底部右侧 | Card | 无 | 无图标 | 否 | 中 | 中 | `DialogButton` |
| `CreatePaginationButton` | 图纸集 | 分页区 | 分页按钮 | Button | 上一页 / 下一页 / 1 / 2 | `BodyFont` | 16 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `40/80 x 36` | 布局控制 | PaginationRow | HorizontalLayoutGroup | 无 | 否 | 中 | 中 | `NavText` |

#### 重点判断
- 图纸集的筛选按钮是全项目第二高风险区。
- 主要原因：字号直接 20、按钮高仅 36、中文文本较长、未开启 BestFit。

### 4.6 `Assets/Scripts/UI/SimulationGalleryPageController.cs`（仿真广场）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `BuildFilterBar` | 仿真广场 | 筛选区 | 筛选按钮 | Button | 家庭电路 / 工业电路 / 电机控制 / 正反转 / 星三角 / 自动往返 | `BodyFont` | 16 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | 高 `36`，宽由 padding 和文字决定 | HorizontalLayoutGroup 控制 | FilterBar | HorizontalLayoutGroup | 部分带 `20x20` 图标 | 否优先 | 中 | 中 | `NavText` |
| `BuildSearchSort` | 仿真广场 | 搜索区 | 搜索框 | SearchInput | 搜索案例 | `BodyFont` | 14 | Normal | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | `264x36` | anchored 顶部右侧 | SearchSortBar | 无 | 左图标 | 否 | 低 | 低 | `DialogBody` |
| `BuildSearchSort` | 仿真广场 | 排序区 | 下拉框 | Dropdown | 默认排序 / 名称排序 | `BodyFont` | 14 | Normal | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | `166x36` | anchored 顶部右侧 | SearchSortBar | 无 | 右下拉图标 | 否 | 低 | 低 | `DialogBody` |
| `CreateGalleryCard` | 仿真广场 | 卡片区 | 卡片标题 | CardTitle | 星三角降压启动 / 两电机时间继电器顺序启动 | `BodyFont` | 20 | Bold | 默认 | UpperLeft | Wrap | Truncate | 关闭 | 卡片 `436x318`，标题区仅约 `28` 高 | anchored 卡片内部 | Card | 无 | 与缩略图分层 | 理论允许，但高度很紧 | 高 | 高 | `CardTitle` |
| `CreateGalleryCard` | 仿真广场 | 卡片区 | 信息行 | Caption | 家庭电路 / 内置案例 / 难度等 | `BodyFont` | 16 | Normal | 默认 | MiddleLeft | 默认 | Truncate | 关闭 | 信息行高约 `20` | anchored 卡片内部 | Card | 无 | 左右 padding | 否 | 中 | 中 | `Caption` |
| `CreateGalleryCard` | 仿真广场 | 卡片区 | 标签行 | Caption | 照明 / 电机控制 / 正反转 | `BodyFont` | 16 | Normal | 默认 | MiddleLeft | Wrap | Truncate | 关闭 | 标签区高约 `18` | anchored 卡片内部 | Card | 无 | 标签同行 | 是但空间不足 | 中 | 中 | `Caption` |
| `CreateGalleryCard` | 仿真广场 | 卡片区 | 按钮 | Button | 查看详情 / 加载案例 | `BodyFont` | 16 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `129x31` | anchored 底部 | Card | 无 | 无图标 | 否 | 中 | 中 | `DialogButton` |
| `BuildDetailPage` | 仿真广场详情 | 顶部操作 | 返回/加载按钮 | Button | 返回广场 / 加载到画布 | `BodyFont` | 16 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `128x36` | anchored 顶部 | DetailPage | 无 | 无图标 | 否 | 中 | 中 | `DialogButton` |

#### 重点判断
- 仿真广场最大的风险不是筛选区，而是卡片标题区高度过紧。
- 一旦替换为更宽更厚的 UI 字体，长标题会更早截断，并可能挤压 meta/tags 视觉层级。

### 4.7 `Assets/Scripts/UI/EncyclopediaController.cs`（元器件百科）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `BuildHeader` | 百科 | 顶部标题区 | 页面标题 | PageTitle | 元器件百科 | `ResolveTitleFont()` | 30 | Bold | 默认 | MiddleLeft | 默认 | Overflow | 关闭 | `320x54` | anchored 顶部 | Header | 无 | 无 | 否 | 低 | 低 | `PageTitle` |
| `BuildHeader` | 百科 | 顶部标题区 | 搜索框 | SearchInput | 搜索元件名称 | `ResolveBodyFont()` | 15 | Normal | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | `300x42` | anchored 右上 | Header | 无 | 左图标 | 否 | 低 | 低 | `DialogBody` |
| `BuildSidebar` | 百科 | 左侧分类栏 | 分类项 | Button/Label | 开关按钮 / 控制元件 / 电源仪表 | `ResolveBodyFont()` | 14 | Normal/Selected Bold | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | 侧栏宽 `150`，单项高 `32`，有效宽约 `126` | anchored 左侧 | Sidebar | VerticalLayoutGroup 类布局 | 左右 padding | 否 | 中 | 中 | `NavText` |
| `CreateCard` | 百科 | 卡片区 | 元件名称 | CardTitle | 交流接触器 / 热继电器 / 单相电能表 | `ResolveBodyFont()` | 20 | Bold | 1.1 | UpperLeft | Wrap | Overflow | 关闭 | 卡片 `370x160` | anchored 卡片内部 | Card | 无 | 图像区域独立 | 是 | 中 | 中 | `CardTitle` |
| `CreateCard` | 百科 | 卡片区 | 参数/分类/端子 | CardBody | 分类、额定值、端子信息 | `ResolveBodyFont()` | 15 | Normal | 默认 | UpperLeft | Wrap | Overflow | 关闭 | 多行堆叠 | anchored 卡片内部 | Card | 无 | 左右 padding | 是 | 中 | 中 | `CardBody` |
| `BuildDetailPage` | 百科详情 | 详情区 | 正文 | CardBody | 说明、用途、端子解释 | `ResolveBodyFont()` | 15 | Normal | 默认 | UpperLeft | Wrap | Overflow | 关闭 | 动态高度 | Scroll 区内 | DetailPanel | VerticalLayoutGroup | 内边距充足 | 是 | 低 | 低 | `CardBody` |

#### 重点判断
- 百科页面本身风险不高，但它没有统一接入 `MainUiTheme.UiFont`。
- 如果后续全项目统一字体，这一页最可能出现的是“标题和正文体系与其他页面不一致”。

### 4.8 `Assets/Scripts/UI/CommonTools/CommonToolsPageController.cs`（常用工具）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `CreateText` | 常用工具全页 | 通用文字 | 大量正文/按钮/标签 | 多种 | 全页通用 | `LegacyRuntime.ttf` 直绑 | 按调用而定 | 按调用而定 | 按调用而定 | 多种 | 多种 | 多种 | 多种 | 全页大量使用 | 多种 | 多种 | 多种 | 多种 | 多种 | 高 | 高 | 需拆分角色 |
| `BuildSidebar` | 常用工具 | 左侧分类栏 | 分类项 | Button | 电阻色环识别 / 回路参数估算 / 电路公式 / 基础资料 | `LegacyRuntime` | 14 | Normal/Bold 切换 | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | 侧栏宽 `240`，项高 `36` | VerticalLayout 控制 | Sidebar | VerticalLayoutGroup | 左右 padding | 否 | 中 | 中 | `NavText` |
| `BuildResistorTool` | 常用工具 | 电阻色环识别 | 模式按钮 | Button | 四色环 / 五色环 | `LegacyRuntime` | 15 | Bold | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | 分段容器约 `180x40`，单按钮约 `86` 宽 | HorizontalLayout | ModeGroup | HorizontalLayoutGroup | 无图标 | 否 | 中 | 中 | `InspectorButton` |
| `BuildResistorTool` | 常用工具 | 电阻色环识别 | 色环选择按钮 | Button | 第1环\n数字 | `LegacyRuntime` | 13~15 | Bold 切换 | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `80x40` | Grid/Horizontal | BandGroup | LayoutGroup | 多行文案 | 是 | 中 | 中 | `InspectorButton` |
| `BuildResistorTool` | 常用工具 | 电阻色环识别 | 颜色按钮 | Button | 黑 0 / 棕 1 / 金 ±5% / 银 ±10% | `LegacyRuntime` | 13 | Normal | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `80x35` | Grid 布局 | ColorGroup | GridLayoutGroup | 无图标 | 否优先 | 高 | 高 | `Caption` / `DialogButton` |
| `BuildResistorTool` | 常用工具 | 电阻色环识别 | 示例按钮 | Button | 常用示例文本 | `LegacyRuntime` | 14 | Normal | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `80x30` | HorizontalLayout | ExampleGroup | HorizontalLayoutGroup | 无图标 | 否 | 中 | 中 | `Caption` |
| `BuildFormulaPage` | 常用工具 | 电路公式 | 列表项标题 | CardTitle | 欧姆定律 / 三相功率计算 | `LegacyRuntime` | 14 | Bold | 默认 | MiddleLeft | 默认 | 默认 | 关闭 | 左侧列表宽 `260`，项高 `44` | VerticalLayout | FormulaList | VerticalLayoutGroup | 无图标 | 否 | 低 | 低 | `CardTitle` |
| `BuildFormulaPage` | 常用工具 | 电路公式 | 正文/公式 | CardBody | U = I × R 等 | `LegacyRuntime` | 15~18 | Normal/Bold | 1.3~1.5 | UpperLeft | Wrap | Overflow | 关闭 | 大滚动区 | ScrollRect | FormulaDetail | VerticalLayoutGroup | 内边距较大 | 是 | 低 | 低 | `CardBody` |
| `BuildArticlePage` | 常用工具 | 基础资料 | 正文 | CardBody | 分段教学资料 | `LegacyRuntime` | 15 | Normal | 1.3 | UpperLeft | Wrap | Overflow | 关闭 | 大滚动区 | ScrollRect | ArticleDetail | VerticalLayoutGroup | 内边距较大 | 是 | 低 | 低 | `CardBody` |

#### 重点判断
- 常用工具页面不是某一个控件风险最高，而是全页仍大量直绑 `LegacyRuntime.ttf`。
- 真正高风险控件集中在 `80x35` 的颜色按钮上。

### 4.9 `Assets/Scripts/UI/LocalProfilePageController.cs`（个人中心/本地信息页）

| 文件/方法 | UI 页面 | UI 区域 | 控件位置 | 控件类型 | 当前文本示例 | 当前字体 | 字号 | FontStyle | lineSpacing | alignment | H Overflow | V Overflow | BestFit/AutoSize | Rect 宽高 | anchor / pivot / sizeDelta | 父容器 | Layout 组件 | 图标/内边距 | 是否可换行 | 裁切风险 | 风险等级 | 建议字体角色 |
|---|---|---|---|---|---|---|---:|---|---:|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `CreateInfoCard` | 本地信息页 | 信息卡片 | 卡片标题 | CardTitle | 本地用户信息 / 数据管理 / 软件信息 | `LegacyRuntime` | 18 | Bold | 默认 | UpperLeft | 默认 | 默认 | 关闭 | 卡片 `460x240` | GridLayoutGroup 控制 | InfoGrid | GridLayoutGroup | 无图标 | 否 | 低 | 低 | `CardTitle` |
| `CreateInfoCard` | 本地信息页 | 信息卡片 | 卡片正文 | CardBody | 本地状态说明、多行介绍 | `LegacyRuntime` | 14 | Normal | 默认 | UpperLeft | Wrap | Overflow | 关闭 | 大正文区 | anchored 卡片内部 | Card | 无 | 左右 padding | 是 | 低 | 低 | `CardBody` |
| `CreateActionButton` | 本地信息页 | 信息卡片 | 操作按钮 | Button | 打开图纸文件夹 / 打开数据目录 / 刷新信息 | `LegacyRuntime` | 14 | Normal | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `140x36` | anchored 底部 | Card | 无 | 无图标 | 否 | 中 | 中 | `DialogButton` |
| `CreateActionButton` | 本地信息页 | 信息卡片 | 操作按钮 | Button | 清理缓存(暂未开放) | `LegacyRuntime` | 14 | Normal | 默认 | MiddleCenter | 默认 | 默认 | 关闭 | `140x36` | anchored 底部 | Card | 无 | 无图标 | 否 | 高 | 高 | `DialogButton` |

#### 重点判断
- 个人中心整体风险不高，但 `清理缓存(暂未开放)` 是典型长中文按钮，统一成更宽字体后很容易先炸。

---

## 五、按钮专项审计

### 5.1 按钮风险总表

| 按钮类别 | 代表按钮 | 按钮宽高 | 文本区域宽高 | 文本内容 | 中文字符数估算 | 是否有图标 | 图标占用宽度 | 左右 padding | 当前字号 | 当前字重 | 当前字体 | 换成微软雅黑 UI / 思源黑体后是否可能变宽 | 建议 | 风险等级 |
|---|---|---|---|---|---:|---|---:|---|---:|---|---|---|---|---|
| 顶部导航按钮 | 元器件百科 / 仿真广场 | 高约 36，宽由布局分配 | 布局控制 | 导航页签 | 4~5 | 否 | 0 | 中等 | 16 | Bold | `UiFont` | 会，幅度中 | 先只替换同类 `UiFont`，不动布局 | 中 |
| 顶部工具栏主按钮 | 开始仿真 | `128x40` | 图标后剩余中等 | 开始仿真 | 4 | 是 | 20~24 | 中等 | 15 | Bold | `UiFont` | 会，幅度中 | 可保字号，先看文字是否偏左 | 中 |
| 顶部工具栏普通按钮 | 撤销 / 重做 / 锁定 | `92x40` | 图标后剩余较小 | 2 字 | 2 | 是 | 20~24 | 紧 | 15 | Bold | `UiFont` | 会 | 保字号，必要时微增宽 | 高 |
| 顶部工具栏危险按钮 | 删除 / 清线 / 清空 | `92x40` | 图标后剩余较小 | 2 字 | 2 | 是 | 20~24 | 紧 | 15 | Bold | `UiFont` | 会 | 不先改字号，优先检查图标+文字 offset | 高 |
| 图纸操作按钮 | 加载图纸 / 保存图纸 / 导入图纸 | `118x40` | 中等 | 4 字 | 4 | 是 | 20~24 | 中等 | 15 | Bold | `UiFont` | 会，幅度中 | 建议保持字号但检查 padding | 中 |
| 左侧控件池分类按钮 | 全部 | `72x32` | 约 40 宽 | 全部 | 2 | 是 | 18 | 非常紧 | 14 | Bold | `UiFont` | 会 | 高风险，不建议先换宽字体 | 高 |
| 左侧控件池分类按钮 | 家庭电路组件 | `136x32` | 中等 | 6 字 | 6 | 是 | 18 | 紧 | 14 | Bold | `UiFont` | 会 | 可以先维持原字号 | 中 |
| 左侧元件卡片名称 | 交流接触器等 | 卡片 `96x132` | 约 `84x36` | 2~8 字 | 2~8 | 否 | 0 | 中等 | 13 | Normal + BestFit | 默认 body | 会 | 暂可先只换正文，不动卡片 | 中 |
| 右侧检查助手按钮 | 提交练习检测 | 高 36，宽较宽 | 图标后剩余中等 | 6 字 | 6 | 是 | 18 | 中等 | 14 上限 | Bold | `BodyFont` | 会 | 先保 BestFit，不要贸然关掉 | 高 |
| 右侧检查助手按钮 | 检查当前电路 | 高 36，宽较宽 | 中等 | 6 字 | 6 | 是 | 18 | 中等 | 14 上限 | Bold | `BodyFont` | 会 | 可切清晰字体，但保 BestFit | 中 |
| 图纸集筛选按钮 | 家庭电路图纸 | `175x36` | 中等 | 6 字 | 6 | 可能带计数 | 小 | 中等 | 20 | Bold | `BodyFont` | 明显会 | 优先不改字号，必要时先改字体不改 weight | 高 |
| 图纸集筛选按钮 | 全部图纸 / 初级图纸 | `138x36` | 中等偏小 | 4 字 | 4 | 含数量徽标 | 小 | 中等 | 20 | Bold | `BodyFont` | 明显会 | 高风险 | 高 |
| 图纸集卡片按钮 | 进入练习 | 约 `120x36` | 中等 | 4 字 | 4 | 否 | 0 | 中等 | 20 | Bold | `BodyFont` | 会 | 可接受，但需检查左右留白 | 中 |
| 仿真广场筛选按钮 | 自动往返 | 高 36，宽由内容决定 | 中等 | 4 字 | 4 | 可能有图标 | 20 | 中等 | 16 | Bold | `BodyFont` | 会 | 风险可控 | 中 |
| 仿真广场卡片按钮 | 查看详情 / 加载案例 | `129x31` | 中等 | 4 字 | 4 | 否 | 0 | 中等 | 16 | Bold | `BodyFont` | 会 | 高度偏紧，注意上下裁切 | 中 |
| 百科分类按钮 | 控制元件 | 高 32，宽约 126 | 充足 | 4 字 | 4 | 否 | 0 | 中等 | 14 | Normal | `ResolveBodyFont()` | 会，幅度小 | 可直接换普通 UI 字体 | 低 |
| 常用工具模式按钮 | 四色环 / 五色环 | 约 `86x40` | 中等 | 3 字 | 3 | 否 | 0 | 中等 | 15 | Bold | `LegacyRuntime` | 会 | 风险可控 | 中 |
| 常用工具颜色按钮 | 金 ±5% / 银 ±10% | `80x35` | 小 | 4~6 字 | 4~6 | 否 | 0 | 小 | 13 | Normal | `LegacyRuntime` | 会，明显 | 极易拥挤，先不要改字号 | 高 |
| 系统信息页按钮 | 清理缓存(暂未开放) | `140x36` | 中等偏小 | 9 字 | 9 | 否 | 0 | 中等 | 14 | Normal | `LegacyRuntime` | 会，明显 | 长文案高风险 | 高 |
| 弹窗按钮 | 确认 / 取消 | 常见 `80~128 x 36` | 中等 | 2 字 | 2 | 否 | 0 | 中等 | 15~16 | Bold/Normal | 多为 `LegacyRuntime` | 会，影响小 | 可直接替换 | 低 |

---

## 六、卡片和面板专项审计

| 区域 | 卡片/面板尺寸 | 标题区域尺寸 | 正文区域尺寸 | 按钮区域尺寸 | 当前标题最大字数 | 当前正文最大行数 | 替换字体后是否可能换行 | 替换字体后是否可能压缩按钮区 | 是否建议改字号 | 是否建议改行距 | 是否建议保留当前布局 | 风险等级 |
|---|---|---|---|---|---:|---:|---|---|---|---|---|---|
| 图纸集卡片 | `446x336` | 标题约单独一行 | 中等 | 进入练习按钮位于底部 | 10~16 | 2~4 | 可能，标题中风险 | 可能，但不算首要 | 先不改 | 不优先 | 是 | 中 |
| 仿真广场卡片 | `436x318` | 标题区约 28 高 | meta 和 tags 紧 | 两个 `129x31` 按钮 | 8~16 | 2~3 | **高概率** | 中 | 不建议先增大 | 不优先 | 是 | 高 |
| 元器件百科卡片 | `370x160` | 标题 20 | 多行参数 15 | 无主按钮 | 4~10 | 3~5 | 中等 | 低 | 先不改 | 不优先 | 是 | 中 |
| 常用工具卡片 | 多块白色卡片 | 标题 16~24 | 结果/公式/资料大区 | 80 宽小按钮较多 | 4~12 | 很多 | 小按钮区高，正文区低 | 高 | 不先动 | 正文可统一 1.25~1.35 | 是 | 高 |
| 系统信息页信息卡片 | `460x240` | 标题 18 | 正文 14 | 140 宽按钮 | 4~8 | 4~8 | 正文低 | 长按钮高 | 不先改 | 正文可轻调 | 是 | 中 |
| 检查助手面板 | 宽 320 | 标题安全 | 正文滚动区安全 | 按钮高 36 | 4~8 | 不限 | 按钮低、正文低 | 中 | 不先改 | 正文可调 | 是 | 中 |
| 操作记录面板 | 左栏底部大滚动区 | 标题安全 | 正文滚动区大 | 清空小按钮 | 4 | 很多 | 低 | 低 | 不先改 | 可统一 1.25~1.3 | 是 | 低 |
| 左侧控件池面板 | 宽 380 | 标题安全 | 元件网格固定 | 分类按钮紧 | 2~6 | 卡片名 1~2 行 | 中 | 中 | 不先改 | 不优先 | 是 | 中 |
| 顶部工具栏 | 高 60 | 无大标题 | 无正文 | 小按钮密集 | 2~4 | 1 | **高** | **高** | 不先改 | 不适用 | 是 | 高 |

---

## 七、字体角色映射建议

| 角色 | 字体来源 | 字号 | FontStyle | lineSpacing | 是否允许换行 | horizontalOverflow | verticalOverflow | 适用控件 | 不适用控件 |
|---|---|---:|---|---:|---|---|---|---|---|
| `AppTitle` | 品牌字体（猫啃风） | 18~20 | Bold | 1.0 | 否优先 | Overflow | Overflow | 顶部系统标题 | 小按钮、正文 |
| `PageTitle` | 品牌字体（猫啃风） | 24~32 | Bold | 1.0 | 少量允许 | Wrap | Overflow | 页面标题、一级页头 | 密集导航、按钮 |
| `SectionTitle` | 品牌字体或清晰 UI 字体 | 16~18 | Bold | 1.0 | 否优先 | Overflow | Overflow | 面板标题、卡片分组标题 | 小按钮 |
| `NavText` | 普通 UI 字体 | 14~16 | Bold / Medium | 1.0 | 否优先 | Overflow | Truncate | 顶部导航、侧栏导航、筛选按钮 | 长正文 |
| `NavTextSelected` | 普通 UI 字体 | 14~16 | Bold | 1.0 | 否优先 | Overflow | Truncate | 选中导航/筛选项 | 正文 |
| `ToolbarButtonText` | 普通 UI 字体 | 13~14 | Medium / Bold | 1.0 | 否 | Overflow | Truncate | 顶部普通按钮 | 卡片正文 |
| `ToolbarPrimaryButtonText` | 普通 UI 字体 | 13~14 | Bold | 1.0 | 否 | Overflow | Truncate | 开始仿真 | 正文 |
| `ToolbarDangerButtonText` | 普通 UI 字体 | 13~14 | Bold | 1.0 | 否 | Overflow | Truncate | 删除 / 清线 / 清空 | 正文 |
| `CardTitle` | 普通 UI 字体 | 16~18 | Bold | 1.05~1.15 | 允许 1~2 行 | Wrap | Truncate / Overflow | 卡片名称 | 标签小字 |
| `CardBody` | 普通 UI 字体 | 14~15 | Normal | 1.25~1.35 | 是 | Wrap | Overflow | 百科正文、资料正文、说明正文 | 顶部按钮 |
| `Caption` | 普通 UI 字体 | 12~14 | Medium | 1.1~1.2 | 视情况 | Wrap / Overflow | Truncate | 标签、元数据、提示小字 | 一级标题 |
| `LogBody` | 普通 UI 字体 | 12~13 | Normal | 1.25~1.3 | 是 | Wrap | Overflow | 操作记录 | 小按钮 |
| `InspectorTitle` | 品牌字体或清晰 UI 字体 | 16~17 | Bold | 1.0 | 否 | Overflow | Overflow | 检查助手标题、报告块标题 | 长正文 |
| `InspectorBody` | 普通 UI 字体 | 13~14 | Normal | 1.25~1.35 | 是 | Wrap | Overflow | 检查报告正文 | 工具栏按钮 |
| `InspectorButton` | 普通 UI 字体 | 13~14 | Bold | 1.0 | 否优先 | Overflow | Truncate | 检查助手按钮 | 卡片正文 |
| `DialogTitle` | 品牌字体或清晰 UI 字体 | 18~20 | Bold | 1.0 | 否 | Overflow | Overflow | 弹窗标题 | 小按钮 |
| `DialogBody` | 普通 UI 字体 | 14~15 | Normal | 1.25~1.35 | 是 | Wrap | Overflow | 弹窗正文、搜索输入 | 顶部工具栏 |
| `DialogButton` | 普通 UI 字体 | 13~15 | Medium / Bold | 1.0 | 否 | Overflow | Truncate | 确认/取消/进入练习/加载案例 | 长正文 |

### 7.1 核心原则

- 品牌字体只留给标题层
- 普通 UI 字体覆盖按钮、标签、正文
- 不要把猫啃风放到小字号按钮和卡片小字
- 不要继续让大面积正文长期使用 `LegacyRuntime` 作为统一方案

---

## 八、MainUiTheme.cs 当前能力分析

### 8.1 已具备能力

| 能力 | 当前状态 |
|---|---|
| 品牌字体解析 | 有，`TitleFont` 多路径查找 Maoken |
| 普通 UI 字体解析 | 有，`UiFont` 使用系统字体动态创建 |
| 系统字体 fallback | 有 |
| Resources 内置字体 fallback | 有，回退 `LegacyRuntime.ttf` |
| `ApplyText` 统一入口 | 有，但能力较弱 |
| 基础色彩和尺寸 token | 有一部分 |

### 8.2 当前缺失能力

| 缺失项 | 说明 |
|---|---|
| 字体角色枚举 | 没有 `AppTitle / CardBody / ToolbarButtonText` 这种角色层 |
| 统一正文字体入口 | `BodyFont` 仍固定为 `LegacyRuntime.ttf` |
| 行距角色 | 没有统一 `lineSpacing` 策略 |
| 溢出角色 | 没有统一 `Wrap / Overflow / Truncate` 预设 |
| 标题/正文/按钮统一 Apply API | `ApplyText` 过于基础 |
| 按钮文本风险分级 | 没有针对小按钮/大按钮的不同策略 |
| 卡片/面板文本角色 | 没有 `CardTitleStyle / PanelTitleStyle / LogBodyStyle` |
| 字体 fallback 结果诊断 | 没有直接输出当前实际命中的字体链 |

### 8.3 后续建议

如果后续要真正统一字体体系，`MainUiTheme.cs` 建议补齐：

1. `TextRole` 枚举
2. `ApplyTextRole(Text text, TextRole role)`
3. 统一的 `TitleFont / UiFont / BodyFont` 使用边界
4. 每个 role 的：
   - 字号
   - FontStyle
   - lineSpacing
   - 是否允许换行
   - Overflow 策略

---

## 九、LegacyRuntime 和猫啃风使用风险

### 9.1 当前哪些普通 UI 仍在使用 LegacyRuntime

明显仍在使用 `LegacyRuntime` 或 `BodyFont = LegacyRuntime` 的区域包括：

- `CommonToolsPageController`
- `LocalProfilePageController`
- `EncyclopediaController` 正文体系
- `SimulationGalleryPageController`
- `BlueprintController`
- `LocalInspectorPanel` 按钮字体
- 多个场景 / Prefab 中的序列化 Text

### 9.2 当前哪些正文 / 按钮 / 卡片误用了猫啃风风险最大

从代码看，猫啃风主要仍用于标题方向，没有发现大面积按钮正文统一套用猫啃风的明确代码路径。

但需要注意：

- `Demo.unity` 中已有 `Maoken.ttf` 的场景直绑引用
- 如果后续有人在 Inspector 里继续手动套到普通 Text，会出现：
  - 小字发虚
  - 字重异常
  - 行宽偏大
  - 与系统字体混排不一致

### 9.3 哪些地方可以直接替换为普通 UI 字体

建议优先替换为普通 UI 字体的区域：

- 顶部工具栏按钮
- 图纸集筛选按钮
- 仿真广场卡片标题与按钮
- 检查助手按钮与正文
- 操作记录正文
- 常用工具正文和色环颜色按钮
- 个人中心按钮

### 9.4 哪些地方需要保留猫啃风

建议保留：

- 顶部系统标题
- 页面主标题
- 面板一级标题
- 部分分区大标题

### 9.5 哪些地方不确定，需要人工确认

- `Demo.unity` 中两处 `Maoken.ttf` 场景直绑文本，需在 Unity Inspector 中确认具体对象
- 若有某些 Prefab 的文本是在场景里做了 Override，而不是代码动态创建，也需要手工点检

---

## 十、最小安全修改建议

> 本节只给后续方案，不在本次执行。

### 10.1 建议顺序

#### 第 1 步：只改字体调用，不改 RectTransform
优先把普通 UI 字体统一为：

- `MainUiTheme.UiFont`
- 或将 `MainUiTheme.BodyFont` 改为清晰黑体链

但先**不要改尺寸和布局**。

#### 第 2 步：先替换正文，不先替换高风险小按钮
优先替换：

- 操作记录正文
- 检查助手正文
- 百科正文
- 图纸集详情正文
- 仿真广场详情正文
- 个人中心正文

暂缓：

- 顶部工具栏小按钮
- 图纸集筛选按钮
- 常用工具颜色按钮
- “全部”按钮
- “清理缓存(暂未开放)”按钮

### 10.2 高风险按钮策略

对于高风险按钮：

- 先保持原字号
- 不先加粗
- 不先改按钮宽度
- 先看仅替换字体会不会裁切
- 必要时才微调 padding / 宽度

### 10.3 正文区域策略

- 检查助手：优先保证正文可读
- 操作记录：优先保证日志密度和可读性
- 资料/说明：可以统一 lineSpacing，但不要一刀切

### 10.4 图纸集 / 仿真广场卡片标题策略

建议：

- 不优先放大字号
- 不优先增加字重
- 优先避免更早截断
- 必要时保持原字号，只替换字体
- 仿真广场卡片标题是首要盯防对象

### 10.5 lineSpacing 策略

可统一正文 `lineSpacing`，但要谨慎：

- 说明类正文：`1.25~1.35`
- 日志：`1.25~1.3`
- 按钮/标题：`1.0`

不要一刀切提高所有文本 `lineSpacing`，否则会造成：

- 卡片正文高度不够
- 面板垂直空间被吃掉
- 小卡片更容易挤爆

---

## 十一、字体替换后的人工回归检查清单

### 11.1 顶部与主操作区
- 顶部导航是否裁切
- 工具栏按钮是否裁切
- 开始仿真按钮是否显示完整
- 撤销 / 重做 / 删除 / 清线 / 清空 / 锁定是否完整
- 加载图纸 / 保存图纸 / 导入图纸是否完整
- 导线颜色标签是否变形

### 11.2 左侧栏
- 左侧控件池分类按钮是否裁切
- “全部”按钮是否被图标挤压
- 元件名称是否溢出或被截成难读
- 操作记录标题是否正常
- 操作记录正文是否过密或太松

### 11.3 右侧检查助手
- 当前电路解释按钮是否裁切
- 检查当前电路按钮是否裁切
- 清空结果按钮是否裁切
- 提交练习检测 / 退出练习是否正常
- 报告正文是否拥挤
- 卡片标题与正文层级是否清晰

### 11.4 图纸集
- 家庭电路图纸 / 工业电路图纸按钮是否挤
- 全部图纸 / 初级图纸 / 中级图纸 / 高级图纸是否裁切
- 搜索框 placeholder 是否正常
- 卡片标题是否换行异常
- 进入练习按钮是否完整
- 分页按钮是否正常

### 11.5 仿真广场
- 筛选按钮是否正常
- 搜索框和排序框是否完整
- 卡片标题是否截断过早
- 标签行是否过挤
- 查看详情 / 加载案例按钮是否正常
- 详情页按钮和正文是否正常

### 11.6 元器件百科
- 左侧分类栏是否正常
- 卡片标题是否明显变挤
- 参数 / 端子小字是否仍清楚
- 搜索框是否正常
- 详情页正文是否正常

### 11.7 常用工具
- 左侧分类项是否正常
- 四色环 / 五色环按钮是否正常
- 色环选择按钮是否挤压
- 颜色按钮是否裁切，尤其：
  - 白 9
  - 金 ±5%
  - 银 ±10%
- 公式 / 资料正文是否仍清晰

### 11.8 个人中心 / 系统信息页
- 打开图纸文件夹按钮是否正常
- 打开数据目录按钮是否正常
- 刷新信息按钮是否正常
- 清理缓存(暂未开放)是否裁切

### 11.9 弹窗
- 确认 / 取消按钮是否正常
- 保存图纸弹窗标题是否正常
- 导入图纸相关弹窗是否正常

---

## 十二、禁止事项提醒

后续若进入字体统一阶段，请明确遵守以下边界：

- 不修改模板 JSON
- 不修改检测规则
- 不修改仿真逻辑
- 不修改接线逻辑
- 不修改参数估算
- 不修改 KT 计时
- 不修改自动往返逻辑
- 不修改保存 / 加载 / 导入功能
- 不修改按钮功能绑定
- 不改页面布局
- 不把微软雅黑字体文件直接拷贝进 `Assets` 打包
- 不为了字体好看去替换已稳定的 UI 结构
- 不在没有回归按钮尺寸的情况下批量把 `LegacyRuntime` 一次性替成更宽字体

---

## 十三、最终结论

这套 UI 现在已经不是“缺字体”，而是“**字体来源分裂 + 小按钮空间过紧**”的问题。

如果后续要做统一字体，最安全路线不是先大换，而是：

1. 先统一正文和说明类文本
2. 再单独回归顶部工具栏 / 图纸集筛选 / 仿真广场卡片标题
3. 最后处理常用工具和系统信息页的高风险小按钮

一句话总结：

**当前最危险的不是标题字体，而是那些宽度紧、带图标、BestFit 不一致的小按钮。**
