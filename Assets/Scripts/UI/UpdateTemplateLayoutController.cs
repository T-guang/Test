using ElectricalSim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    /// <summary>
    /// 系统模板布局更新的 UI 入口与确认协调器。
    /// 负责创建维护按钮、检查当前模板会话、展示确认对话框并反馈更新结果；不自行读取、解析或重写模板 JSON。
    /// 该功能仅服务 Unity Editor 中的系统模板维护，不应作为用户保存图纸的更新入口。修改后需回归确认、取消、失败提示和更新后的模板重载。
    /// </summary>
    public sealed class UpdateTemplateLayoutController : MonoBehaviour
    {
        private WorkspaceController workspace;

        public static void Create(WorkspaceController workspace)
        {
            var go = new GameObject("UpdateTemplateLayoutController", typeof(UpdateTemplateLayoutController));
            var controller = go.GetComponent<UpdateTemplateLayoutController>();
            controller.workspace = workspace;
        }

        private void Start()
        {
            if (workspace == null)
            {
                workspace = FindObjectOfType<WorkspaceController>();
            }

            CreateButtonIfNeeded();
        }

        private void CreateButtonIfNeeded()
        {
            var fileActionGroup = GameObject.Find("FileActionGroup");
            if (fileActionGroup == null)
            {
                return;
            }

            var existing = fileActionGroup.transform.Find("UpdateTemplateLayoutButton");
            if (existing != null)
            {
                Destroy(existing.gameObject);
            }

            var buttonObject = new GameObject("UpdateTemplateLayoutButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(fileActionGroup.transform, false);

            var rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(138f, 48f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.42f, 0.95f, 0.98f);

            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(OnUpdateButtonClicked);

            var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(buttonObject.transform, false);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            label.text = "更新模板布局";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;

            buttonObject.transform.SetAsLastSibling();
        }

        private void OnUpdateButtonClicked()
        {
            if (!TemplateEditSession.HasSystemTemplateLoaded)
            {
                workspace?.SetStatus("当前画布不是系统模板，无法更新模板布局。");
                return;
            }

#if UNITY_EDITOR
            // 具体磁盘写回由 Editor 专用的 SystemTemplateLayoutUpdater 完成，Controller 只保留确认与结果反馈职责。
            UpdateTemplateLayoutConfirmDialog.Show(TemplateEditSession.CurrentTemplateName, ExecuteUpdate);
#else
            workspace?.SetStatus("当前环境不支持更新系统模板（仅限 Unity Editor）。");
#endif
        }

        private void ExecuteUpdate()
        {
#if UNITY_EDITOR
            if (SystemTemplateLayoutUpdater.UpdateTemplateLayout(workspace, out var message))
            {
                workspace?.SetStatus(message);
            }
            else
            {
                workspace?.SetStatus("更新失败：" + message);
            }
#endif
        }
    }
}
