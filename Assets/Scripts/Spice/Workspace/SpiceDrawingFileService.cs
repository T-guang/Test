using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ElectricalSim.Spice.Workspace
{
    /// <summary>
    /// SPICE 图纸文件操作核心服务。本类不接触 UI、文件对话框、控制模式 SaveLoadService 或 WindowsFileDialog。
    /// 职责仅限：当前会话路径、默认目录、.spicejson 扩展名规范化、UTF-8 原子写入、UTF-8 严格读取。
    /// 保存只调用 Batch A 的 SpiceDrawingSerializer.ToJson；导入只把原始 JSON 交给调用方的 Batch B 入口。
    /// 本类不解析器件、坐标、端子或 Wire；不在文件层做任何电路级校验。
    /// </summary>
    public sealed class SpiceDrawingFileService
    {
        /// <summary>SPICE 图纸专用扩展名。不可使用 .cir/.spice/.net。</summary>
        public const string Extension = ".spicejson";

        /// <summary>导入文件大小上限：1 MB。超出直接拒绝，避免读取异常大文件。</summary>
        public const long MaxFileBytes = 1024 * 1024;

        private string currentSpiceFilePath;

        /// <summary>当前会话的图纸文件路径。保存或导入成功后更新；清空画布后清除；为空时表示尚未绑定文件。</summary>
        public string CurrentSpiceFilePath => currentSpiceFilePath;

        /// <summary>是否已绑定当前会话文件路径。C2 的“保存”按钮据此决定是否改走“另存为”。</summary>
        public bool HasCurrentSpiceFilePath => !string.IsNullOrEmpty(currentSpiceFilePath);

        /// <summary>
        /// SPICE 专用默认目录：Application.persistentDataPath/SavedSpiceDrawings。
        /// 目录仅在保存、导入等实际文件操作前按需创建，不在构造时创建。
        /// 不写入 Assets、StreamingAssets、项目目录或 SavedBlueprints。
        /// </summary>
        public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, "SavedSpiceDrawings");

        /// <summary>
        /// 确保 SPICE 默认目录存在。仅在 C2 文件对话框打开前调用；
        /// 失败不抛异常，返回 false 由调用方决定是否仍打开对话框（Windows 也会自行处理路径）。
        /// 不写日志：本方法是 UI 前置准备，文件级错误仍由 TrySaveUtf8Atomically/TryReadUtf8File 负责。
        /// </summary>
        public static bool EnsureDefaultDirectoryExists()
        {
            try
            {
                Directory.CreateDirectory(DefaultDirectory);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 规范化文件路径的扩展名：
        /// - 无扩展名时自动追加 .spicejson；
        /// - 已有 .spicejson 或 .SPICEJSON 时不重复追加；
        /// - 非 .spicejson 的现有扩展名保持不改，供后续 UI 对话框处理。
        /// </summary>
        public static string NormalizeExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return path + Extension;
            if (string.Equals(ext, Extension, StringComparison.OrdinalIgnoreCase)) return path;
            return path;
        }

        /// <summary>
        /// 清除当前会话文件路径。仅在画布清空成功后由 Controller 调用；
        /// 删除单个元件/Wire、缩放、平移、模式切换、仿真运行均不清除路径。
        /// </summary>
        public void ClearCurrentSpiceFilePath()
        {
            currentSpiceFilePath = null;
        }

        /// <summary>
        /// 内部设置当前路径。仅供 Controller 在保存或导入成功后调用。
        /// </summary>
        internal void SetCurrentSpiceFilePath(string path)
        {
            currentSpiceFilePath = path;
        }

        /// <summary>
        /// 将 JSON 内容以 UTF-8 原子方式写入指定路径。
        /// 原子写入策略：在目标文件同目录创建唯一临时文件 → 完整写入 → 目标不存在时 Move，目标存在时优先 File.Replace → 失败删除临时文件。
        /// 不得直接截断原文件后写入，失败时不删除原目标冒险覆盖。
        /// </summary>
        public bool TrySaveUtf8Atomically(string path, string content, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "保存路径不能为空。";
                return false;
            }
            if (content == null)
            {
                error = "保存内容不能为空。";
                return false;
            }

            var normalizedPath = NormalizeExtension(path);
            string directory;
            try
            {
                directory = Path.GetDirectoryName(normalizedPath);
                if (string.IsNullOrEmpty(directory))
                {
                    error = "保存路径缺少目录部分：" + normalizedPath;
                    return false;
                }
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception)
            {
                error = "创建保存目录失败：" + exception.Message;
                Debug.LogError("[SpiceDrawingFileService] 创建保存目录异常：" + exception);
                return false;
            }

            var tempPath = Path.Combine(directory, "." + Path.GetFileNameWithoutExtension(normalizedPath) + ".spicejson.tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(tempPath, content, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                error = "写入临时文件失败：" + exception.Message;
                Debug.LogError("[SpiceDrawingFileService] 写入临时文件异常：" + exception);
                TryDeleteTempFile(tempPath);
                return false;
            }

            try
            {
                if (!File.Exists(normalizedPath))
                {
                    File.Move(tempPath, normalizedPath);
                }
                else
                {
                    // 目标存在：优先 File.Replace，保留原子语义并在失败时删除备份。
                    var backupPath = Path.Combine(directory, ".spicejson.backup." + Guid.NewGuid().ToString("N"));
                    File.Replace(tempPath, normalizedPath, backupPath, true);
                    TryDeleteTempFile(backupPath);
                }
            }
            catch (Exception exception)
            {
                error = "替换目标文件失败：" + exception.Message;
                Debug.LogError("[SpiceDrawingFileService] 替换目标文件异常：" + exception);
                TryDeleteTempFile(tempPath);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 从指定路径以 UTF-8 严格读取文件内容。
        /// 先检查文件存在、普通文件、大小限制；无效 UTF-8 返回可读错误。
        /// </summary>
        public bool TryReadUtf8File(string path, out string content, out string error)
        {
            content = null;
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "导入路径不能为空。";
                return false;
            }

            try
            {
                if (!File.Exists(path))
                {
                    error = "文件不存在：" + path;
                    return false;
                }
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                {
                    error = "路径指向目录而非文件：" + path;
                    return false;
                }
                if (info.Length > MaxFileBytes)
                {
                    error = "文件超过 1MB 限制（" + info.Length + " 字节）：" + path;
                    return false;
                }
            }
            catch (Exception exception)
            {
                error = "读取文件信息失败：" + exception.Message;
                Debug.LogError("[SpiceDrawingFileService] 读取文件信息异常：" + exception);
                return false;
            }

            try
            {
                // 使用 UTF-8 严格读取：无效字节序列会抛出 DecoderFallbackException。
                content = File.ReadAllText(path, new UTF8Encoding(false, throwOnInvalidBytes: true));
                return true;
            }
            catch (Exception exception)
            {
                error = "文件不是有效的 UTF-8 文本：" + exception.Message;
                Debug.LogError("[SpiceDrawingFileService] UTF-8 读取异常：" + exception);
                return false;
            }
        }

        private static void TryDeleteTempFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception)
            {
                Debug.LogError("[SpiceDrawingFileService] 清理临时文件失败：" + path + " " + exception);
            }
        }
    }
}
