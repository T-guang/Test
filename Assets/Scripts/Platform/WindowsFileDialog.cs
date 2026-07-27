using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ElectricalSim.Platform
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    /// <summary>
    /// Windows common-dialog wrapper. Unity Mono cannot safely host IFileDialog COM interfaces,
    /// so this deliberately uses the stable comdlg32 API instead of modern COM dialog interfaces.
    /// initialDirectory is passed as the native dialog's requested initial directory; Windows may
    /// retain a user-selected folder between invocations, which is platform behavior.
    /// </summary>
    public static class WindowsFileDialog
    {
        private const int OfnExplorer = 0x00080000;
        private const int OfnFileMustExist = 0x00001000;
        private const int OfnPathMustExist = 0x00000800;
        private const int OfnNoChangeDir = 0x00000008;
        private const int OfnOverwritePrompt = 0x00000002;
        private const string UserFacingFailureMessage = "无法打开文件选择窗口，请稍后重试。";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class OpenFileName
        {
            public int structSize;
            public IntPtr dlgOwner = IntPtr.Zero;
            public IntPtr instance = IntPtr.Zero;
            public string filter;
            public string customFilter;
            public int maxCustFilter;
            public int filterIndex;
            public string file;
            public int maxFile;
            public string fileTitle;
            public int maxFileTitle;
            public string initialDir;
            public string title;
            public int flags;
            public short fileOffset;
            public short fileExtension;
            public string defExt;
            public IntPtr custData = IntPtr.Zero;
            public IntPtr hook = IntPtr.Zero;
            public string templateName;
            public IntPtr reservedPtr = IntPtr.Zero;
            public int reservedInt;
            public int flagsEx;
        }

        [DllImport("Comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        [DllImport("Comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSaveFileName([In, Out] OpenFileName ofn);

        [DllImport("Comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        public static string OpenFile(string title, string filter, string extension)
        {
            return OpenFile(title, filter, extension, null, out _);
        }

        public static string OpenFile(string title, string filter, string extension, string initialDirectory)
        {
            return OpenFile(title, filter, extension, initialDirectory, out _);
        }

        public static string OpenFile(string title, string filter, string extension, string initialDirectory, out string error)
        {
            error = null;
            var dialog = CreateDialog(title, filter, extension, initialDirectory, null,
                OfnExplorer | OfnFileMustExist | OfnPathMustExist | OfnNoChangeDir);
            try
            {
                if (!GetOpenFileName(dialog))
                {
                    error = ReadDialogError("GetOpenFileName");
                    return null;
                }

                return TrimNullTerminator(dialog.file);
            }
            catch (Exception exception)
            {
                Debug.LogError("[WindowsFileDialog] GetOpenFileName exception: " + exception);
                error = UserFacingFailureMessage;
                return null;
            }
        }

        public static string SaveFile(string title, string filter, string extension, string initialDirectory, string defaultFileName)
        {
            return SaveFile(title, filter, extension, initialDirectory, defaultFileName, out _);
        }

        public static string SaveFile(string title, string filter, string extension, string initialDirectory, string defaultFileName, out string error)
        {
            error = null;
            var dialog = CreateDialog(title, filter, extension, initialDirectory, defaultFileName,
                OfnExplorer | OfnOverwritePrompt | OfnPathMustExist | OfnNoChangeDir);
            try
            {
                if (!GetSaveFileName(dialog))
                {
                    error = ReadDialogError("GetSaveFileName");
                    return null;
                }

                return StripTrailingDuplicateSpiceJson(TrimNullTerminator(dialog.file));
            }
            catch (Exception exception)
            {
                Debug.LogError("[WindowsFileDialog] GetSaveFileName exception: " + exception);
                error = UserFacingFailureMessage;
                return null;
            }
        }

        private static OpenFileName CreateDialog(string title, string filter, string extension, string initialDirectory, string defaultFileName, int flags)
        {
            var dialog = new OpenFileName
            {
                structSize = Marshal.SizeOf(typeof(OpenFileName)),
                filter = (filter ?? string.Empty).Replace('|', '\0') + "\0",
                file = (defaultFileName ?? string.Empty) + new string('\0', 2048),
                maxFile = 2048 + (defaultFileName ?? string.Empty).Length,
                fileTitle = new string('\0', 256),
                maxFileTitle = 256,
                title = title,
                defExt = extension,
                initialDir = string.IsNullOrEmpty(initialDirectory) ? null : initialDirectory,
                flags = flags,
                dlgOwner = GetActiveWindow()
            };
            return dialog;
        }

        private static string ReadDialogError(string operation)
        {
            var code = CommDlgExtendedError();
            if (code == 0) return null;
            Debug.LogError("[WindowsFileDialog] " + operation + " failed: CommDlgExtendedError=0x" + code.ToString("X8"));
            return UserFacingFailureMessage;
        }

        private static string TrimNullTerminator(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var terminator = value.IndexOf('\0');
            return terminator >= 0 ? value.Substring(0, terminator) : value;
        }

        private static string StripTrailingDuplicateSpiceJson(string path)
        {
            const string extension = ".spicejson";
            if (string.IsNullOrEmpty(path)) return path;
            var duplicate = extension + extension;
            return path.EndsWith(duplicate, StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - extension.Length)
                : path;
        }
    }
#endif
}
