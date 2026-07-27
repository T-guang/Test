using System;
using System.Runtime.InteropServices;

namespace ElectricalSim.Platform
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    /// <summary>
    /// Windows 原生打开/保存文件对话框封装。仅 Windows 平台引用 Comdlg32；
    /// 非 Windows 平台不引用任何 Windows API，由调用方在编译期被 #if 隔离。
    /// 所有方法在用户取消时返回 null，不抛异常；支持中文路径（CharSet.Auto）。
    /// </summary>
    public static class WindowsFileDialog
    {
        // OFN 标志位常量
        private const int OFN_EXPLORER = 0x00080000;
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_OVERWRITEPROMPT = 0x00000002;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public class OpenFileName
        {
            public int structSize = 0;
            public IntPtr dlgOwner = IntPtr.Zero;
            public IntPtr instance = IntPtr.Zero;
            public string filter = null;
            public string customFilter = null;
            public int maxCustFilter = 0;
            public int filterIndex = 0;
            public string file = null;
            public int maxFile = 0;
            public string fileTitle = null;
            public int maxFileTitle = 0;
            public string initialDir = null;
            public string title = null;
            public int flags = 0;
            public short fileOffset = 0;
            public short fileExtension = 0;
            public string defExt = null;
            public IntPtr custData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName = null;
            public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt = 0;
            public int flagsEx = 0;
        }

        [DllImport("Comdlg32.dll", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("Comdlg32.dll", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Auto)]
        private static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        /// <summary>旧版兼容入口：保留原有签名，默认不指定初始目录。</summary>
        public static string OpenFile(string title, string filter, string extension)
        {
            return OpenFile(title, filter, extension, null);
        }

        /// <summary>
        /// 打开文件对话框，可指定初始目录。用户取消返回 null。
        /// 使用 OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR。
        /// </summary>
        public static string OpenFile(string title, string filter, string extension, string initialDirectory)
        {
            var ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = filter.Replace('|', '\0') + "\0";
            ofn.file = new string('\0', 2048);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string('\0', 256);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.title = title;
            ofn.defExt = extension;
            ofn.initialDir = string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory;
            ofn.flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
            ofn.dlgOwner = GetActiveWindow();

            if (GetOpenFileName(ofn))
            {
                return ofn.file;
            }
            return null;
        }

        /// <summary>
        /// 保存文件对话框。用户取消返回 null。
        /// 使用 OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR。
        /// Windows 自身处理覆盖确认，不依赖应用层弹窗。
        /// </summary>
        public static string SaveFile(string title, string filter, string extension, string initialDirectory, string defaultFileName)
        {
            var ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = filter.Replace('|', '\0') + "\0";
            // 默认文件名填入 file 缓冲区；用户可在对话框中修改。
            ofn.file = (defaultFileName ?? string.Empty) + new string('\0', 2048);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string('\0', 256);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.title = title;
            ofn.defExt = extension;
            ofn.initialDir = string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory;
            ofn.flags = OFN_EXPLORER | OFN_OVERWRITEPROMPT | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
            ofn.dlgOwner = GetActiveWindow();

            if (GetSaveFileName(ofn))
            {
                return ofn.file;
            }
            return null;
        }
    }
#endif
}
