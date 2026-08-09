using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricalSim.UI
{
    // PageRouter 只协调页面根节点的显示/隐藏和标题投影。activeSelf 是导航表现状态，不等于 Workspace 是否锁定、模板是否加载或仿真是否运行。
    // 页面控制器仍各自拥有数据刷新与 listener 生命周期；路由器不能通过切页重建或清理其业务状态。
    public sealed class PageRouter : MonoBehaviour
    {
        [SerializeField] private GameObject simulationRoot;
        [SerializeField] private GameObject blueprintRoot;
        [SerializeField] private GameObject squareRoot;
        [SerializeField] private GameObject encyclopediaRoot;
        [SerializeField] private GameObject toolsRoot;
        [SerializeField] private GameObject profileRoot;
        [SerializeField] private GameObject emptyPageRoot;
        [SerializeField] private Text emptyPageTitle;
        [SerializeField] private PageId defaultPage = PageId.Simulation;

        public PageId CurrentPage { get; private set; } = PageId.Simulation;

        public void Configure(
            GameObject simulation,
            GameObject blueprint,
            GameObject square,
            GameObject encyclopedia,
            GameObject tools,
            GameObject profile,
            GameObject emptyPage,
            Text emptyTitle)
        {
            simulationRoot = simulation;
            blueprintRoot = blueprint;
            encyclopediaRoot = encyclopedia;
            toolsRoot = tools;
            emptyPageRoot = emptyPage;
            emptyPageTitle = emptyTitle;
            squareRoot = square != null ? square : EnsureSimulationGalleryRoot();
            profileRoot = profile != null ? profile : EnsureLocalProfileRoot();
        }

        private GameObject EnsureLocalProfileRoot()
        {
            if (profileRoot != null)
            {
                return profileRoot;
            }

            var parent = emptyPageRoot != null ? emptyPageRoot.transform.parent : transform.parent;
            if (parent == null)
            {
                return null;
            }

            var existing = parent.Find("LocalProfilePage");
            if (existing != null)
            {
                profileRoot = existing.gameObject;
                if (profileRoot.GetComponent<LocalProfilePageController>() == null)
                {
                    profileRoot.AddComponent<LocalProfilePageController>();
                }

                return profileRoot;
            }

            var go = new GameObject("LocalProfilePage", typeof(RectTransform), typeof(Image), typeof(LocalProfilePageController));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            var sourceRect = emptyPageRoot != null ? emptyPageRoot.GetComponent<RectTransform>() : null;
            if (sourceRect != null)
            {
                rect.anchorMin = sourceRect.anchorMin;
                rect.anchorMax = sourceRect.anchorMax;
                rect.pivot = sourceRect.pivot;
                rect.anchoredPosition = sourceRect.anchoredPosition;
                rect.sizeDelta = sourceRect.sizeDelta;
            }
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            go.SetActive(false);
            profileRoot = go;
            return profileRoot;
        }

        private GameObject EnsureSimulationGalleryRoot()
        {
            if (squareRoot != null)
            {
                return squareRoot;
            }

            var parent = emptyPageRoot != null ? emptyPageRoot.transform.parent : transform.parent;
            if (parent == null)
            {
                return null;
            }

            var existing = parent.Find("SimulationGalleryPage");
            if (existing != null)
            {
                squareRoot = existing.gameObject;
                if (squareRoot.GetComponent<SimulationGalleryPageController>() == null)
                {
                    squareRoot.AddComponent<SimulationGalleryPageController>();
                }

                return squareRoot;
            }

            var go = new GameObject("SimulationGalleryPage", typeof(RectTransform), typeof(Image), typeof(SimulationGalleryPageController));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            var sourceRect = emptyPageRoot != null ? emptyPageRoot.GetComponent<RectTransform>() : null;
            if (sourceRect != null)
            {
                rect.anchorMin = sourceRect.anchorMin;
                rect.anchorMax = sourceRect.anchorMax;
                rect.pivot = sourceRect.pivot;
                rect.anchoredPosition = sourceRect.anchoredPosition;
                rect.sizeDelta = sourceRect.sizeDelta;
            }
            else
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            go.SetActive(false);
            squareRoot = go;
            return squareRoot;
        }

        private void Awake()
        {
            ShowPage(defaultPage);
        }

        public void ShowPage(PageId page)
        {
            // 切页前由调用方完成必要的 guard；本方法只切换 root，不能把“页面已隐藏”当作练习/仿真已安全结束的证明。
            CurrentPage = page;
            var targetRoot = GetRoot(page);

            SetInactiveIfDifferent(simulationRoot, targetRoot);
            SetInactiveIfDifferent(blueprintRoot, targetRoot);
            SetInactiveIfDifferent(squareRoot, targetRoot);
            SetInactiveIfDifferent(encyclopediaRoot, targetRoot);
            SetInactiveIfDifferent(toolsRoot, targetRoot);
            SetInactiveIfDifferent(profileRoot, targetRoot);
            SetInactiveIfDifferent(emptyPageRoot, targetRoot);

            if (targetRoot != null)
            {
                targetRoot.SetActive(true);
            }

            if (emptyPageTitle != null)
            {
                emptyPageTitle.text = GetPageTitle(page);
            }
        }

        private GameObject GetRoot(PageId page)
        {
            switch (page)
            {
                case PageId.Simulation:
                    return simulationRoot;
                case PageId.Blueprint:
                    return blueprintRoot;
                case PageId.Square:
                    return squareRoot != null ? squareRoot : emptyPageRoot;
                case PageId.Encyclopedia:
                    return encyclopediaRoot;
                case PageId.Tools:
                    return toolsRoot;
                case PageId.Profile:
                    return profileRoot != null ? profileRoot : emptyPageRoot;
                default:
                    return simulationRoot;
            }
        }

        private static void SetInactiveIfDifferent(GameObject root, GameObject activeRoot)
        {
            if (root != null && root != activeRoot)
            {
                root.SetActive(false);
            }
        }

        private static string GetPageTitle(PageId page)
        {
            switch (page)
            {
                case PageId.Simulation:
                    return "\u6a21\u62df\u7535\u8def";
                case PageId.Blueprint:
                    return "\u56fe\u7eb8\u96c6";
                case PageId.Square:
                    return "\u4eff\u771f\u5e7f\u573a";
                case PageId.Encyclopedia:
                    return "\u5143\u5668\u4ef6\u767e\u79d1";
                case PageId.Tools:
                    return "\u5e38\u7528\u5de5\u5177";
                case PageId.Profile:
                    return "\u4e2a\u4eba\u4e2d\u5fc3";
                default:
                    return string.Empty;
            }
        }
    }
}
