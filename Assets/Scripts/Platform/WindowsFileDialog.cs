using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ElectricalSim.Platform
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    /// <summary>
    /// Windows 原生打开/保存文件对话框封装。仅 Windows 平台引用 COM 与 PInvoke；
    /// 非 Windows 平台不引用任何 Windows API，由调用方在编译期被 #if 隔离。支持中文路径。
    ///
    /// C2.4 契约：
    /// 1. 使用现代 IFileDialog（IFileOpenDialog / IFileSaveDialog + IShellItem）。
    /// 2. IFileDialog 基础接口 vtable 顺序严格按 shobjidl_core.h：
    ///    IUnknown(3) + IModalWindow.Show(1) + IFileDialog(19) + 派生扩展。
    ///    GetResult 后、派生扩展前，必须按顺序补全 AddPlace / SetDefaultExtension /
    ///    Close / SetClientGuid / ClearClientData / SetFilter 六个方法，否则 vtable 槽位错位。
    /// 3. OpenFile 不设置默认扩展名；SaveFile 调用 SetDefaultExtension("spicejson")。
    ///    不得用 SetFileNameLabel 设置扩展名。
    /// 4. FOS_FORCEFILESYSTEM = 0x40 确保 GetDisplayName(SIGDN_FILESYSPATH) 总能取得真实路径。
    /// 5. 失败与取消严格区分：用户取消（ERROR_CANCELLED）返回 null 且 error=null；
    ///    COM 创建、SetFolder、Show 非取消失败、GetResult、GetDisplayName 失败返回 null
    ///    且 error="无法打开文件选择窗口，请稍后重试。"。技术详情写 Debug.LogError，
    ///    不向用户展示 HRESULT、路径或堆栈。不得回退到旧 GetOpenFileName/GetSaveFileName。
    /// 6. COM 对象与 IShellItem 在 finally 中通过 Marshal.ReleaseComObject 释放。
    ///
    /// C2.5 契约（取消 HRESULT 判定修正）：
    /// 7. IModalWindow.Show 必须标注 [PreserveSig] 并返回 int（HRESULT）。
    ///    缺失 [PreserveSig] 时 .NET COM interop 会把失败 HRESULT 转成 COMException 抛出，
    ///    导致 ERROR_CANCELLED 走 catch 分支被误判为 failure，error 被错误设为非空。
    /// 8. 取消判定通过纯函数 ClassifyShowHResult(int hr) 分类：
    ///    hr==0 → Success；hr==ERROR_CANCELLED_HRESULT → Cancelled（error=null）；
    ///    其余 → Failure（error=UserFacingFailureMessage）。
    ///    该 helper 是 internal 纯函数，可被 T3 反射测试覆盖三类分支。
    /// </summary>
    public static class WindowsFileDialog
    {
        // COM GUID 常量
        private static readonly Guid ClsidFileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid ClsidFileSaveDialog = new Guid("C0B4E2F3-BA21-4773-8DBA-335EC946EB8B");
        private static readonly Guid IidIFileOpenDialog = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");
        private static readonly Guid IidIFileSaveDialog = new Guid("84BCCD23-5FDE-4CDB-AEA4-AF64B83D78AB");
        private static readonly Guid IidIShellItem = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        // FILEOPENDIALOGOPTIONS 标志位（shobjidl_core.h FILEOPENDIALOGOPTIONS）
        // C2.4：删除语义不明确的 FOS_PICKFILES = 0x1；新增 FOS_FORCEFILESYSTEM = 0x40。
        private const uint FOS_OVERWRITEPROMPT = 0x00000002;
        private const uint FOS_NOCHANGEDIR = 0x00000008;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint FOS_FILEMUSTEXIST = 0x00001000;

        // SIGDN
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        // 用户取消的 HRESULT（ERROR_CANCELLED 包装为 HRESULT）
        private const int ERROR_CANCELLED_HRESULT = unchecked((int)0x800704C7);

        // 用户可见错误消息（不暴露技术详情）
        private const string UserFacingFailureMessage = "无法打开文件选择窗口，请稍后重试。";

        // C2.5：IModalWindow.Show 的 HRESULT 分类。供 OpenFile/SaveFile 与 T3 测试共用。
        // 提取为纯函数是为了让 T3 能在不真实打开 COM 对话框的前提下覆盖三类分支。
        internal enum DialogShowResult { Success, Cancelled, Failure }

        /// <summary>
        /// C2.5：对 IModalWindow.Show 返回的 HRESULT 进行分类。
        ///   hr == 0                      → Success（继续 GetResult）
        ///   hr == ERROR_CANCELLED_HRESULT → Cancelled（用户取消，error=null，不写日志）
        ///   其余非零                      → Failure（error=用户可见消息，技术详情写 Debug.LogError）
        /// 该方法是纯函数，无副作用，可被 T3 反射直接覆盖三类分支。
        /// </summary>
        internal static DialogShowResult ClassifyShowHResult(int hr)
        {
            if (hr == 0) return DialogShowResult.Success;
            if (hr == ERROR_CANCELLED_HRESULT) return DialogShowResult.Cancelled;
            return DialogShowResult.Failure;
        }

        // COM 接口 vtable 顺序说明：
        // IUnknown(3) + IModalWindow.Show(1) + IFileDialog(19) + 派生扩展。
        // IFileDialog 19 个方法按顺序：
        //   SetFileTypes, SetFileTypeIndex, GetFileTypeIndex, Advise, Unadvise,
        //   SetOptions, GetOptions, SetDefaultFolder, SetFolder, GetFolder,
        //   GetCurrentSelection, SetFileName, GetFileName, SetTitle, SetOkButtonLabel,
        //   SetFileNameLabel, GetResult, AddPlace, SetDefaultExtension,
        //   Close, SetClientGuid, ClearClientData, SetFilter
        // 之前误把派生方法（GetResults/SetSaveAsItem）紧接 GetResult 后放置，导致
        // vtable 槽位错位；C2.4 在 GetResult 后、派生扩展前补全 6 个基础方法。

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
            // C2.5：[PreserveSig] 保留 HRESULT 返回值，避免 interop 把 ERROR_CANCELLED 转成异常。
            // 返回 int（HRESULT 是有符号 32 位）。无此属性时取消会被 catch 块误判为 failure。
            [PreserveSig]
            int Show([In] IntPtr hwndOwner);
            // IFileDialog（19 个方法，顺序与 IFileSaveDialog 完全一致）
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
            // IFileDialog 续：C2.4 补全的 6 个基础方法（之前缺失导致 vtable 错位）
            void AddPlace([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] int fdcp);
            void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszSpec);
            void Close([In] int hr);
            void SetClientGuid([In] ref Guid guid);
            void ClearClientData();
            void SetFilter([In, MarshalAs(UnmanagedType.Interface)] object pFilter);
            // IFileOpenDialog 派生扩展
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
            // C2.5：[PreserveSig] 保留 HRESULT 返回值，避免 interop 把 ERROR_CANCELLED 转成异常。
            // 返回 int（HRESULT 是有符号 32 位）。无此属性时取消会被 catch 块误判为 failure。
            [PreserveSig]
            int Show([In] IntPtr hwndOwner);
            // IFileDialog（19 个方法，顺序与 IFileOpenDialog 完全一致）
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
            // IFileDialog 续：C2.4 补全的 6 个基础方法（之前缺失导致 vtable 错位）
            void AddPlace([In, MarshalAs(UnmanagedType.Interface)] IShellItem psi, [In] int fdcp);
            void SetDefaultExtension([In, MarshalAs(UnmanagedType.LPWStr)] string pszSpec);
            void Close([In] int hr);
            void SetClientGuid([In] ref Guid guid);
            void ClearClientData();
            void SetFilter([In, MarshalAs(UnmanagedType.Interface)] object pFilter);
            // IFileSaveDialog 派生扩展
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

        // C2.6：删除 `out object` 的单一 CoCreateInstance P/Invoke，改为两个强类型 P/Invoke。
        // 原因：Unity Mono 的 COM interop 在 `out object` 时编组器会以 IUnknown 语义创建
        // 临时 RCW，再强制转换到目标接口可能丢失 vtable 偏移或触发额外 QueryInterface，
        // 在某些 Unity/Mono 版本下导致调用 GetResults/SetSaveAsItem 时崩溃或返回错误 HRESULT。
        // 强类型 `out IFileOpenDialog` / `out IFileSaveDialog` 让编组器直接以目标接口指针
        // 返回，跳过中间 object RCW。两个 P/Invoke 都映射到 ole32.dll 的 CoCreateInstance，
        // CLSCTX = 1 (CLSCTX_INPROC_SERVER)，与原调用一致。
        [DllImport("ole32.dll", EntryPoint = "CoCreateInstance")]
        private static extern int CoCreateFileOpenDialog([In] ref Guid rclsid, IntPtr pUnkOuter, int dwClsContext, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IFileOpenDialog ppv);

        [DllImport("ole32.dll", EntryPoint = "CoCreateInstance")]
        private static extern int CoCreateFileSaveDialog([In] ref Guid rclsid, IntPtr pUnkOuter, int dwClsContext, [In] ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IFileSaveDialog ppv);

        // ==================== OpenFile 重载 ====================

        /// <summary>旧版兼容入口：保留原有 3 参签名，默认不指定初始目录。失败与取消都返回 null（不区分）。</summary>
        public static string OpenFile(string title, string filter, string extension)
        {
            return OpenFile(title, filter, extension, null, out _);
        }

        /// <summary>4 参兼容入口：不区分失败与取消。新调用方应使用 5 参带 out error 重载。</summary>
        public static string OpenFile(string title, string filter, string extension, string initialDirectory)
        {
            return OpenFile(title, filter, extension, initialDirectory, out _);
        }

        /// <summary>
        /// 打开文件对话框（主入口）。用户取消返回 null 且 error=null；
        /// COM 创建、SetFolder、Show 非取消失败、GetResult、GetDisplayName 失败返回 null 且 error=用户可见消息。
        /// OpenFile 不设置默认扩展名。强制初始目录通过 SetFolder 实现，对抗 Windows 目录记忆。
        /// 过滤器格式 "名称|*.ext"，内部转换为单个 COMDLG_FILTERSPEC。
        /// </summary>
        public static string OpenFile(string title, string filter, string extension, string initialDirectory, out string error)
        {
            error = null;
            IFileOpenDialog dialog = null;
            IShellItem folderItem = null;
            IShellItem resultItem = null;
            try
            {
                var clsid = ClsidFileOpenDialog;
                var iid = IidIFileOpenDialog;
                // C2.6：调用强类型 P/Invoke，直接得到 IFileOpenDialog，无需 object 中转与强制转换。
                var hr = CoCreateFileOpenDialog(ref clsid, IntPtr.Zero, 1, ref iid, out dialog);
                if (hr != 0 || dialog == null)
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] CoCreateInstance(IFileOpenDialog) failed: hr=0x" + hr.ToString("X8"));
                    return null;
                }

                // Open 选项：C2.4 删除 FOS_PICKFILES，新增 FOS_FORCEFILESYSTEM。
                uint options = FOS_PATHMUSTEXIST | FOS_FILEMUSTEXIST | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR;
                dialog.SetOptions(options);

                SetDialogFilter(dialog, filter);

                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                // 强制初始目录：SetFolder（非 SetDefaultFolder），对抗目录记忆
                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var iidShell = IidIShellItem;
                    hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iidShell, out folderItem);
                    if (hr != 0 || folderItem == null)
                    {
                        error = UserFacingFailureMessage;
                        Debug.LogError("[WindowsFileDialog] SHCreateItemFromParsingName failed: hr=0x" + hr.ToString("X8") + " dir=" + initialDirectory);
                        return null;
                    }
                    dialog.SetFolder(folderItem);
                }

                // OpenFile 不设置默认扩展名（C2.4 契约）

                var showHr = dialog.Show(GetActiveWindow());
                // C2.5：用纯函数 ClassifyShowHResult 分类，确保取消与失败严格区分。
                var showClassification = ClassifyShowHResult(showHr);
                if (showClassification == DialogShowResult.Cancelled)
                {
                    // 用户取消：error 保持 null，不写日志
                    return null;
                }
                if (showClassification == DialogShowResult.Failure)
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] IFileOpenDialog.Show failed: hr=0x" + showHr.ToString("X8"));
                    return null;
                }
                // Success：继续 GetResult

                dialog.GetResult(out resultItem);
                if (resultItem == null)
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] IFileOpenDialog.GetResult returned null.");
                    return null;
                }

                resultItem.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                if (string.IsNullOrEmpty(path))
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] IFileOpenDialog.GetDisplayName returned empty path.");
                    return null;
                }
                return path;
            }
            catch (Exception ex)
            {
                error = UserFacingFailureMessage;
                Debug.LogError("[WindowsFileDialog] OpenFile exception: " + ex);
                return null;
            }
            finally
            {
                ReleaseComObject(resultItem);
                ReleaseComObject(folderItem);
                ReleaseComObject(dialog);
            }
        }

        // ==================== SaveFile 重载 ====================

        /// <summary>5 参兼容入口：不区分失败与取消。新调用方应使用 6 参带 out error 重载。</summary>
        public static string SaveFile(string title, string filter, string extension, string initialDirectory, string defaultFileName)
        {
            return SaveFile(title, filter, extension, initialDirectory, defaultFileName, out _);
        }

        /// <summary>
        /// 保存文件对话框（主入口）。用户取消返回 null 且 error=null；
        /// COM 创建、SetFolder、Show 非取消失败、GetResult、GetDisplayName 失败返回 null 且 error=用户可见消息。
        /// 使用 SetDefaultExtension 设置默认扩展名（C2.4 修复：不再误用 SetFileNameLabel）。
        /// 默认文件名不含扩展名，由对话框补全；返回前 StripTrailingDuplicateSpiceJson 去重，
        /// C1 的 NormalizeExtension 仍会做二次保障，确保不产生 .spicejson.spicejson。
        /// </summary>
        public static string SaveFile(string title, string filter, string extension, string initialDirectory, string defaultFileName, out string error)
        {
            error = null;
            IFileSaveDialog dialog = null;
            IShellItem folderItem = null;
            IShellItem resultItem = null;
            try
            {
                var clsid = ClsidFileSaveDialog;
                var iid = IidIFileSaveDialog;
                // C2.6：调用强类型 P/Invoke，直接得到 IFileSaveDialog，无需 object 中转与强制转换。
                var hr = CoCreateFileSaveDialog(ref clsid, IntPtr.Zero, 1, ref iid, out dialog);
                if (hr != 0 || dialog == null)
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] CoCreateInstance(IFileSaveDialog) failed: hr=0x" + hr.ToString("X8"));
                    return null;
                }

                // Save 选项：C2.4 删除 FOS_PICKFILES，新增 FOS_FORCEFILESYSTEM。
                uint options = FOS_PATHMUSTEXIST | FOS_OVERWRITEPROMPT | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR;
                dialog.SetOptions(options);

                SetDialogFilter(dialog, filter);

                // C2.4 修复：用 SetDefaultExtension 设置默认扩展名（不再误用 SetFileNameLabel）。
                // SetDefaultExtension 位于 IFileDialog vtable 第 18 槽位，之前因 vtable 错位而无法调用。
                if (!string.IsNullOrEmpty(extension))
                {
                    dialog.SetDefaultExtension(extension);
                }

                if (!string.IsNullOrEmpty(title)) dialog.SetTitle(title);

                // 强制初始目录：SetFolder（非 SetDefaultFolder），对抗目录记忆
                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    var iidShell = IidIShellItem;
                    hr = SHCreateItemFromParsingName(initialDirectory, IntPtr.Zero, ref iidShell, out folderItem);
                    if (hr != 0 || folderItem == null)
                    {
                        error = UserFacingFailureMessage;
                        Debug.LogError("[WindowsFileDialog] SHCreateItemFromParsingName failed: hr=0x" + hr.ToString("X8") + " dir=" + initialDirectory);
                        return null;
                    }
                    dialog.SetFolder(folderItem);
                }

                // 默认文件名（不含扩展名，由对话框默认扩展名机制补全）
                if (!string.IsNullOrEmpty(defaultFileName))
                {
                    var nameNoExt = StripSpiceJsonExtension(defaultFileName);
                    dialog.SetFileName(nameNoExt);
                }

                var showHr = dialog.Show(GetActiveWindow());
                // C2.5：用纯函数 ClassifyShowHResult 分类，确保取消与失败严格区分。
                var showClassification = ClassifyShowHResult(showHr);
                if (showClassification == DialogShowResult.Cancelled)
                {
                    // 用户取消：error 保持 null，不写日志
                    return null;
                }
                if (showClassification == DialogShowResult.Failure)
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] IFileSaveDialog.Show failed: hr=0x" + showHr.ToString("X8"));
                    return null;
                }
                // Success：继续 GetResult

                dialog.GetResult(out resultItem);
                if (resultItem == null)
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] IFileSaveDialog.GetResult returned null.");
                    return null;
                }

                resultItem.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                if (string.IsNullOrEmpty(path))
                {
                    error = UserFacingFailureMessage;
                    Debug.LogError("[WindowsFileDialog] IFileSaveDialog.GetDisplayName returned empty path.");
                    return null;
                }
                // 去除末尾重复的 .spicejson 扩展名，避免 .spicejson.spicejson。
                // C1 的 NormalizeExtension 仍会做二次保障。
                return StripTrailingDuplicateSpiceJson(path);
            }
            catch (Exception ex)
            {
                error = UserFacingFailureMessage;
                Debug.LogError("[WindowsFileDialog] SaveFile exception: " + ex);
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
    }
#endif
}
