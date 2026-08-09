using UnityEngine;

namespace ElectricalSim.UI
{
    // AppSession 只保存当前本地用户这一会话级 UI 身份，供导航和个人页显示使用；它不是账号认证、权限模型或图纸所有权的来源。
    // 静态访问点会跨页面存在，因此销毁/重建时必须保持单例语义，不能把页面临时显示文本当作会话真值。
    public sealed class AppSession : MonoBehaviour
    {
        private static AppSession instance;

        public static string CurrentUser { get; private set; } = string.Empty;
        public static bool IsLoggedIn => !string.IsNullOrWhiteSpace(CurrentUser);

        public static AppSession Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                var existing = FindObjectOfType<AppSession>();
                if (existing != null)
                {
                    instance = existing;
                    return instance;
                }

                var sessionObject = new GameObject("AppSession");
                instance = sessionObject.AddComponent<AppSession>();
                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            if (string.IsNullOrWhiteSpace(CurrentUser))
            {
                CurrentUser = PlayerPrefs.GetString(LoginController.LastUserNameKey, string.Empty);
            }
        }

        public static void Login(string userName)
        {
            Instance.EnsureAlive();
            CurrentUser = userName;
        }

        public static void Logout()
        {
            CurrentUser = string.Empty;
        }

        private void EnsureAlive()
        {
        }
    }
}
