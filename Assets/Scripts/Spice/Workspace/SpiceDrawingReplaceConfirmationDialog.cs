using System;
using ElectricalSim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// C2 非空画布导入前的替换确认模态。挂在 SimulationModeController.PopupLayer 下，
    /// 复用既有 SpiceWorkspaceUi 样式。半透明全屏 Blocker 接收 Raycast，阻止背景交互。
    /// 关闭方式仅：取消 / 继续导入 / Esc / Host 销毁。点击弹窗外部不关闭。
    /// 不新建 Canvas/EventSystem；不使用 GameObject.Find/FindObjectOfType；不新增全局 Update 轮询。
    /// </summary>
    public sealed class SpiceDrawingReplaceConfirmationDialog : MonoBehaviour
    {
        private Image blocker;
        private Image panel;
        private Text body;
        private Button cancelButton;
        private Button confirmButton;
        private bool initialized;

        public bool IsOpen => panel != null && panel.gameObject.activeSelf;

        /// <summary>用户点击“继续导入”时触发；Host 订阅后调用 TryImportWorkspaceFromPath。</summary>
        public event Action ConfirmRequested;

        /// <summary>用户点击“取消”或按 Esc 时触发；Host 订阅后清理 pendingImportPath。</summary>
        public event Action Cancelled;

        public void Initialize(RectTransform popupLayer)
        {
            if (initialized) throw new InvalidOperationException("SPICE replace confirmation dialog is already initialized.");
            if (popupLayer == null) throw new ArgumentNullException(nameof(popupLayer));
            BuildUi(popupLayer);
            CloseWithoutApply();
            initialized = true;
        }

        public void Open()
        {
            if (!initialized || panel == null) return;
            blocker.gameObject.SetActive(true);
            panel.gameObject.SetActive(true);
            // 先把 Blocker 提到 PopupLayer 最上层（覆盖既有 PopupLayer 内容与全局 UI），
            // 再把 Panel 提到 Blocker 之上。结果：Blocker < Panel，Panel 位于最顶层。
            blocker.rectTransform.SetAsLastSibling();
            panel.rectTransform.SetAsLastSibling();
        }

        /// <summary>
        /// 用户取消：关闭弹窗并触发 Cancelled 事件，Host 据此清理 pendingImportPath。
        /// 与 CloseWithoutApply 的区别：CloseWithoutApply 是纯 UI 操作，不通知 Host；
        /// Cancel 是用户主动取消，需要 Host 清理待导入候选路径。
        /// </summary>
        public void Cancel()
        {
            if (!IsOpen) return;
            CloseWithoutApply();
            Cancelled?.Invoke();
        }

        /// <summary>
        /// 用户确认：关闭弹窗并触发 ConfirmRequested 事件，Host 据此执行导入。
        /// </summary>
        public void Confirm()
        {
            HandleConfirmClicked();
        }

        public void CloseWithoutApply()
        {
            if (blocker != null) blocker.gameObject.SetActive(false);
            if (panel != null) panel.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (blocker != null)
            {
                DestroyObject(blocker.gameObject);
                blocker = null;
            }
            if (panel != null)
            {
                DestroyObject(panel.gameObject);
                panel = null;
            }
            ConfirmRequested = null;
            Cancelled = null;
            body = null;
            cancelButton = null;
            confirmButton = null;
            // 销毁弹窗根对象（挂载本组件的 GameObject），确保 PopupLayer 下无残留。
            // 在 Host.OnDestroy 中调用安全：Host 调用后不再访问本引用。
            if (gameObject != null) DestroyObject(gameObject);
        }

        // 运行时用 Destroy（延迟到帧末，安全），编辑模式用 DestroyImmediate（立即销毁，测试可验证）。
        private static void DestroyObject(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
            }
        }

        private void BuildUi(RectTransform popupLayer)
        {
            // 全屏半透明 Blocker：接收 Raycast，阻止背景所有交互。
            blocker = SpiceWorkspaceUi.CreateImage(popupLayer, "SpiceReplaceConfirmBlocker", new Color(0.08f, 0.12f, 0.2f, 0.48f));
            SpiceWorkspaceUi.Stretch(blocker.rectTransform, Vector2.zero, Vector2.zero);
            blocker.raycastTarget = true;

            // 弹窗 Panel：居中，固定尺寸，挂在 Blocker 同层（Blocker 之上）。
            var panelGo = new GameObject("SpiceReplaceConfirmPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(popupLayer, false);
            panel = panelGo.GetComponent<Image>();
            panel.color = Color.white;
            panel.sprite = UiThemeTokens.GetRoundedSprite(8);
            panel.type = Image.Type.Sliced;
            panel.raycastTarget = true;
            panel.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            panel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            panel.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            panel.rectTransform.sizeDelta = new Vector2(520f, 260f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
            var outline = panelGo.AddComponent<Outline>();
            outline.effectColor = MainUiTheme.Divider;
            outline.effectDistance = new Vector2(1f, -1f);
            var shadow = panelGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.18f);
            shadow.effectDistance = new Vector2(0f, -4f);

            // 标题（顶部，20pt，四周边距 28）
            var title = SpiceWorkspaceUi.CreateText(panel.transform, "Title", "导入图纸", 20, FontStyle.Bold, TextAnchor.MiddleLeft, MainUiTheme.DeepText);
            SpiceWorkspaceUi.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -56f), new Vector2(-28f, -12f));

            // 正文（标题下方，16pt，四周边距 28）
            body = SpiceWorkspaceUi.CreateText(panel.transform, "Body", "导入图纸将替换当前 SPICE 画布，是否继续？", 16, FontStyle.Normal, TextAnchor.MiddleLeft, MainUiTheme.NormalText);
            SpiceWorkspaceUi.Anchor(body.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -122f), new Vector2(-28f, -66f));

            // 取消 / 继续导入 按钮（底部右侧，按钮 110×40，间距 14，右边距 24）
            // Panel 宽 520，半宽 260；两按钮总宽 110+14+110=234，右边距 24，左边界距右 24+234=258 < 260 ✓
            // Confirm 右边界距右 24：offsetMax.x=-24；左边界距右 24+110=134：offsetMin.x=-134
            // Cancel 右边界距右 24+110+14=148：offsetMax.x=-148；左边界距右 148+110=258：offsetMin.x=-258
            cancelButton = SpiceWorkspaceUi.CreateButton(panel.transform, "Cancel", "取消", Color.white, Cancel);
            SpiceWorkspaceUi.Anchor(cancelButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-258f, 28f), new Vector2(-148f, 68f));
            confirmButton = SpiceWorkspaceUi.CreateButton(panel.transform, "Confirm", "继续导入", MainUiTheme.PrimaryBlue, HandleConfirmClicked, true);
            SpiceWorkspaceUi.Anchor(confirmButton.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-134f, 28f), new Vector2(-24f, 68f));
            ConfigureButtonColors(cancelButton, false);
            ConfigureButtonColors(confirmButton, true);
            // 显式设置两个按钮的圆角 sprite 与 Sliced 类型
            ApplyRoundedButtonSprite(cancelButton);
            ApplyRoundedButtonSprite(confirmButton);
        }

        private void HandleConfirmClicked()
        {
            // 先关闭弹窗，再触发回调，避免回调中再次打开对话框时被本弹窗 Blocker 拦截。
            CloseWithoutApply();
            ConfirmRequested?.Invoke();
        }

        private static void ConfigureButtonColors(Button button, bool primary)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = primary ? MainUiTheme.Hex("1D4ED8") : MainUiTheme.Hex("F8FAFC");
            colors.pressedColor = primary ? MainUiTheme.Hex("1E40AF") : MainUiTheme.Hex("EAF2FF");
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
        }

        /// <summary>
        /// 显式设置按钮 Image 的圆角 sprite 与 Sliced 类型，确保按钮呈圆角外观。
        /// 对 Cancel 与 Confirm 均调用，不依赖 CreateButton 的默认样式。
        /// </summary>
        private static void ApplyRoundedButtonSprite(Button button)
        {
            if (button == null) return;
            var image = button.GetComponent<Image>();
            if (image == null) return;
            image.sprite = UiThemeTokens.GetRoundedSprite(8);
            image.type = Image.Type.Sliced;
        }
    }
}
