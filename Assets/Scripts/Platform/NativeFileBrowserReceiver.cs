using System;
using UnityEngine;

namespace ElectricalSim.Platform
{
    // 这是浏览器/原生文件选择插件回调与应用服务之间的最薄桥接层：只把异步文本或错误转交给一次性回调，
    // 不解析图纸内容、不保存文件，也不应跨一次选择操作保留旧回调，避免后到的浏览器消息污染下一次导入。
    public sealed class NativeFileBrowserReceiver : MonoBehaviour
    {
        private static NativeFileBrowserReceiver instance;
        private Action<string> onJsonReceived;
        private Action<string> onError;

        public static NativeFileBrowserReceiver Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("NativeFileBrowserReceiver");
                    instance = go.AddComponent<NativeFileBrowserReceiver>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        public void Prepare(Action<string> onJsonReceived, Action<string> onError)
        {
            this.onJsonReceived = onJsonReceived;
            this.onError = onError;
        }

        public void OnWebGLFileLoaded(string json)
        {
            onJsonReceived?.Invoke(json);
            ClearCallbacks();
        }

        public void OnWebGLFileError(string error)
        {
            onError?.Invoke(error);
            ClearCallbacks();
        }

        private void ClearCallbacks()
        {
            onJsonReceived = null;
            onError = null;
        }
    }
}
