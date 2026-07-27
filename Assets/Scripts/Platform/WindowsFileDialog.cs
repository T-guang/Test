using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ElectricalSim.Platform
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    /// <summary>
    /// Windows 原生打开/保存文件对话框封装。仅 Windows 平台引用 COM 与 PInvoke；
    /// 非 Windows 平台不引用任何 Windows API，由调用方在编译期被 #if 隔离。
    /// 所有方法在用户取消或调用失败时返回 null，不抛异常；支持中文路径。
    ///
    /// 强制默认目录策略：使用现代 IFileDialog（IFileOpenDialog / IFileSaveDialog），
    /// 调用 SetFolder 设置初始目录（非 SetDefaultFolder），对抗 Windows 目录记忆。
    /// 每次打开都回到指定的 initialDirectory，用户改选其他目录后下次仍回 initialDirectory。
    /// COM 对象与 IShellItem 在 finally 中释放。若 IFileDialog 调用失败返回 null，
    /// 由 Host 显示简洁错误，不偷偷回退到记忆目录。
    /// </summary>
    public static class WindowsFileDialog
    {
        // COM GUID 常量
        private static readonly Guid ClsidFileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid ClsidFileSaveDialog = new Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
        private static readonly Guid IidIFileOpenDialog = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");
        private static readonly Guid IidIFileSaveDialog = new Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");
        private static readonly Guid IidIShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        // FILEOPENDIALOGOPTIONS 标志位
        private const uint FOS_PICKFILES = 0x00000001;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint FOS_FILEMUSTEXIST = 0x00001000;
        private const uint FOS_OVERWRITEPROMPT = 0x00000002;
        private const uint FOS_NOCHANGEDIR = 0x00000008;

        // SIGDN
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        // COM 接口 vtable 顺序（IUnknown 3 方法 + IModalWindow 1 + IFileDialog 11 + 各自扩展）
        // IFileOpenDialog: IUnknown(3) + IModalWindow.Show(1) + IFileDialog(11) + IFileOpenDialog(1) = 16 slots
        // IFileSaveDialog: IUnknown(3) + IModalWindow.Show(1) + IFileDialog(11) + IFileSaveDialog(5) = 20 slots
        // 这里只用到 IFileDialog 公共部分 + IFileSaveDialog.SetSaveAsItem。

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        private interface IShellItem
        {
            // IShellItem vtable：BindToHandler + GetParent + GetDisplayName + GetAttributes + Compare
            void BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName([In] uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes([In] uint sfgaoMask, out uint psfgaoAttribs);
            void Compare([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] uint hint, out int piOrder);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        private interface IFileOpenDialog
        {
            // IUnknown
            void QueryInterface([In] ref Guid riid, out IntPtr ppv);
            uint AddRef();
            uint Release();
            // IModalWindow
            uint Show([In] IntPtr hwndOwner);
            // IFileDialog
            void SetFileTypes([In] uint cFileTypes, [In] IntPtr rgFilterSpec);
            void SetFileTypeIndex([In] uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise([In, MarshalAs(UnmanagedType.Interface)] object pfde, out uint pdwCookie);
            void Unadvise([In] uint dwCookie);
            void SetOptions([In] uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            // IFileOpenDialog 扩展
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB")]
        private interface IFileSaveDialog
        {
            // IUnknown
            void QueryInterface([In] ref Guid riid, out IntPtr ppv);
            uint AddRef();
            uint Release();
            // IModalWindow
            uint Show([In] IntPtr hwndOwner);
            // IFileDialog
            void SetFileTypes([In] uint cFileTypes, [In] IntPtr rgFilterSpec);
            void SetFileTypeIndex([In] uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise([In, MarshalAs(UnmanagedType.Interface)] object pfde, out uint pdwCookie);
            void Unadvise([In] uint dwCookie);
            void SetOptions([In] uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void SetFolder([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([In, MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([In, MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([In, MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            // IFileSaveDialog 扩展
            void SetSaveAsItem([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi);
            void SetProperties([In, MarshalAs(UnmanagedType.Interface)] object pStore);
            void SetCollectedProperties([In, MarshalAs(UnmanagedType.Interface)] object pList, [In] bool fAppendDefault);
            void GetProperties(out IntPtr ppStore);
            void ApplyProperties([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In, MarshalAs(UnmanagedType.Interface)] object pStore, [In] IntPtr hwnd, [In, MarshalAs(UnmanagedType.Interface)] object pSink);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct COMDLG_FILTERSPEC
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pszName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pszSpec;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, [In] ref Guid riid, out IShellItem ppv);

        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        /// <summary>旧版兼容入口：保留原有签名，默认不指定初始目录。</summary>
        public static string OpenFile(string title, string filter, string extension)
        {
            return OpenFile(title, filter, extension, null);
        }

        /// <summary>
        /// 打开文件对话框，可指定初始目录。用户取消或调用失败返回 null。
        /// 使用现代 IFileOpenDialog + SetFolder 强制初始目录；对抗 Windows 目录记忆。
        /// 过滤器格式 "名称|*.ext"，内部转换为 COMDLG_FILTERSPEC。
        /// </summary>
        public static string OpenFile(string title, string filter, string extension, string initialDirectory)
        {
            IFileOpenDialog dialog = null;
            IShellItem folderItem = null;
            IShellItem resultItem = null;
            try
            {
                var clsid = ClsidFileOpenDialog;
                var iid = IidIFileOpenDialog;
                var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var dialogObj);
                if (hr != 0 || dialogObj == null) return null;
                dialog = (IFileOpenDialog)dialogObj;

                // 设置选项
                uint options = FOS_PICKFILES | FOS_PATHMUSTEXIST | FOS_FILEMUSTEXIST | FOS_NOCHANGEDIR;
                dialog.SetOptions(options);

                // 设置过滤器
                SetDialogFilter(dialog, filter);

                // 设置标题
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                // 强制初始目录：SetFolder（非 SetDefaultFolder），对抗目录记忆
                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var iidShell = IidIShellItem;
                    hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iidShell, out folderItem);
                    if (hr == 0 && folderItem != null)
                    {
                        dialog.SetFolder(folderItem);
                    }
                }

                // 设置默认扩展名
                if (!string.IsNullOrEmpty(extension)) dialog.SetFileNameLabel(extension);

                var showHr = dialog.Show(GetActiveWindow());
                if (showHr != 0) return null; // 用户取消或失败

                dialog.GetResult(out resultItem);
                if (resultItem == null) return null;

                resultItem.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                return path;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(resultItem);
                ReleaseComObject(folderItem);
                ReleaseComObject(dialog);
            }
        }

        /// <summary>
        /// 保存文件对话框。用户取消或调用失败返回 null。
        /// 使用现代 IFileSaveDialog + SetFolder 强制初始目录；对抗 Windows 目录记忆。
        /// Windows 自身处理覆盖确认（FOS_OVERWRITEPROMPT）。默认扩展名通过 SetFileNameLabel 设置。
        /// 返回前去除末尾重复的 .spicejson 扩展名，避免 .spicejson.spicejson。
        /// </summary>
        public static string SaveFile(string title, string filter, string extension, string initialDirectory, string defaultFileName)
        {
            IFileSaveDialog dialog = null;
            IShellItem folderItem = null;
            IShellItem resultItem = null;
            try
            {
                var clsid = ClsidFileSaveDialog;
                var iid = IidIFileSaveDialog;
                var hr = CoCreateInstance(ref clsid, IntPtr.Zero, 1, ref iid, out var dialogObj);
                if (hr != 0 || dialogObj == null) return null;
                dialog = (IFileSaveDialog)dialogObj;

                // 设置选项
                uint options = FOS_PICKFILES | FOS_PATHMUSTEXIST | FOS_OVERWRITEPROMPT | FOS_NOCHANGEDIR;
                dialog.SetOptions(options);

                // 设置过滤器
                SetDialogFilter(dialog, filter);

                // 设置默认扩展名（IFileDialog 通过 SetFileNameLabel 不直接设置扩展名，
                // 真正的默认扩展名由用户输入文件名 + defExt 补全。这里用 SetFileName 设置不含扩展名的默认文件名）
                if (!string.IsNullOrEmpty(extension)) dialog.SetFileNameLabel(extension);

                // 设置标题
                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                // 强制初始目录：SetFolder（非 SetDefaultFolder），对抗目录记忆
                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var iidShell = IidIShellItem;
                    hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iidShell, out folderItem);
                    if (hr == 0 && folderItem != null)
                    {
                        dialog.SetFolder(folderItem);
                    }
                }

                // 设置默认文件名（不含扩展名，由对话框默认扩展名机制补全）
                if (!string.IsNullOrEmpty(defaultFileName))
                {
                    // 去除默认文件名中可能预置的扩展名，由对话框统一补全
                    var nameNoExt = StripSpiceJsonExtension(defaultFileName);
                    dialog.SetFileName(nameNoExt);
                }

                var showHr = dialog.Show(GetActiveWindow());
                if (showHr != 0) return null; // 用户取消或失败

                dialog.GetResult(out resultItem);
                if (resultItem == null) return null;

                resultItem.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                // 去除末尾重复的 .spicejson 扩展名，避免 .spicejson.spicejson
                return StripTrailingDuplicateSpiceJson(path);
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(resultItem);
                ReleaseComObject(folderItem);
                ReleaseComObject(dialog);
            }
        }

        /// <summary>安全释放 COM 对象（通过 Marshal.ReleaseComObject），忽略异常。</summary>
        private static void ReleaseComObject(object obj)
        {
            if (obj == null) return;
            try
            {
                while (Marshal.ReleaseComObject(obj) > 0) { }
            }
            catch { }
        }

        /// <summary>设置对话框过滤器。输入格式 "名称|*.ext"，转换为单个 COMDLG_FILTERSPEC。</summary>
        private static void SetDialogFilter(object dialog, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return;
            var parts = filter.Split('|');
            if (parts.Length < 2) return;
            var spec = new COMDLG_FILTERSPEC
            {
                pszName = parts[0],
                pszSpec = parts[1]
            };
            var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<COMDLG_FILTERSPEC>());
            try
            {
                Marshal.StructureToPtr(spec, ptr, false);
                if (dialog is IFileOpenDialog openDialog)
                {
                    openDialog.SetFileTypes(1, ptr);
                    openDialog.SetFileTypeIndex(1);
                }
                else if (dialog is IFileSaveDialog saveDialog)
                {
                    saveDialog.SetFileTypes(1, ptr);
                    saveDialog.SetFileTypeIndex(1);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        /// <summary>去除字符串末尾的 .spicejson 扩展名（大小写不敏感）。用于默认文件名预处理。</summary>
        private static string StripSpiceJsonExtension(string name)
        {
            const string ext = ".spicejson";
            if (string.IsNullOrEmpty(name)) return name;
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                return name.Substring(0, name.Length - ext.Length);
            }
            return name;
        }

        /// <summary>
        /// 去除路径末尾重复的 .spicejson 扩展名。若路径以 .spicejson.spicejson 结尾，
        /// 只保留一个。大小写不敏感。最终仍会经过 C1 的 NormalizeExtension 二次保障。
        /// </summary>
        private static string StripTrailingDuplicateSpiceJson(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            const string ext = ".spicejson";
            var doubleExt = ext + ext;
            if (path.EndsWith(doubleExt, StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(0, path.Length - ext.Length);
            }
            return path;
        }

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance([In] ref Guid rclsid, IntPtr pUnkOuter, int dwClsContext, [In] ref Guid riid, out object ppv);
    }
#endif
}
