using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace KkjQuicker.Utilities
{
    /// <summary>
    /// 提供常用文件、目录与路径操作的辅助方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类定位为轻量、实用的文件系统辅助工具，适合在桌面程序和工具库中直接使用。
    /// </para>
    /// <para>
    /// 设计原则：
    /// </para>
    /// <list type="bullet">
    /// <item><description>对无效输入尽量宽容处理。</description></item>
    /// <item><description>高频便捷方法优先，避免为假想场景引入复杂度。</description></item>
    /// <item><description>不承担 UI 职责，不直接弹窗。</description></item>
    /// <item><description>对部分“清理型”操作采用静默失败策略，以降低调用负担。</description></item>
    /// </list>
    /// </remarks>
    public static class FileSystemHelper
    {
        private static readonly Encoding Utf8 = Encoding.UTF8;

        #region 文件读写

        /// <summary>
        /// 读取指定文件的全部文本内容。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <returns>
        /// 文件内容；若 <paramref name="path"/> 为空白或文件不存在，则返回空字符串。
        /// </returns>
        /// <remarks>
        /// 读取时会优先根据文件 BOM 自动识别文本编码；若未检测到 BOM，则默认按 UTF-8 处理。
        /// </remarks>
        public static string ReadAllText(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            using (var reader = new StreamReader(path, Utf8, detectEncodingFromByteOrderMarks: true))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// 异步读取指定文件的全部文本内容。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <returns>
        /// 表示异步读取操作的任务；若 <paramref name="path"/> 为空白或文件不存在，则结果为空字符串。
        /// </returns>
        /// <remarks>
        /// 读取时会优先根据文件 BOM 自动识别文本编码；若未检测到 BOM，则默认按 UTF-8 处理。
        /// 使用异步 FileStream，避免阻塞调用线程。
        /// </remarks>
        public static async Task<string> ReadAllTextAsync(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return string.Empty;

            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       bufferSize: 4096,
                       useAsync: true))
            using (var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: true))
                return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 将文本内容写入指定文件（UTF-8），并在必要时自动创建目标目录。
        /// </summary>
        /// <param name="path">目标文件路径。</param>
        /// <param name="content">要写入的文本内容；为 <see langword="null"/> 时按空字符串写入。</param>
        /// <remarks>
        /// 若 <paramref name="path"/> 为空白，则不执行任何操作。
        /// </remarks>
        public static void WriteAllText(string? path, string? content)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            EnsureParentDirectory(path);
            File.WriteAllText(path, content ?? string.Empty, Utf8);
        }

        /// <summary>
        /// 异步将文本内容写入指定文件（UTF-8），并在必要时自动创建目标目录。
        /// </summary>
        /// <param name="path">目标文件路径。</param>
        /// <param name="content">要写入的文本内容；为 <see langword="null"/> 时按空字符串写入。</param>
        /// <returns>表示异步写入操作的任务。</returns>
        /// <remarks>
        /// 若 <paramref name="path"/> 为空白，则直接返回。
        /// 使用异步 FileStream，避免阻塞调用线程。
        /// </remarks>
        public static async Task WriteAllTextAsync(string? path, string? content)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            EnsureParentDirectory(path);

            using (var stream = new FileStream(
                       path,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       useAsync: true))
            using (var writer = new StreamWriter(stream, Utf8))
            {
                await writer.WriteAsync(content ?? string.Empty).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 读取指定文件的 JSON 内容并反序列化为 <typeparamref name="T"/>。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="path">JSON 文件路径。</param>
        /// <param name="defaultValue">
        /// 当路径无效、文件不存在、内容为空或反序列化失败时返回的默认值。
        /// </param>
        /// <param name="settings">
        /// 可选的 <see cref="JsonSerializerSettings"/>；为 <see langword="null"/> 时使用 Newtonsoft.Json 默认设置。
        /// </param>
        /// <returns>反序列化结果；若读取或解析失败则返回 <paramref name="defaultValue"/>。</returns>
        public static T ReadJson<T>(string? path, T defaultValue, JsonSerializerSettings? settings = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return defaultValue;

            try
            {
                string json = ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                    return defaultValue;

                T? value = JsonConvert.DeserializeObject<T>(json, settings);
                return object.Equals(value, null) ? defaultValue : value!;
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// 将对象序列化为 JSON 后写入指定文件，目录不存在时自动创建。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="path">目标 JSON 文件路径。</param>
        /// <param name="value">要序列化并写入的对象。</param>
        /// <param name="indented">是否格式化输出。</param>
        /// <param name="settings">
        /// 可选的 <see cref="JsonSerializerSettings"/>；为 <see langword="null"/> 时使用 Newtonsoft.Json 默认设置。
        /// </param>
        /// <remarks>
        /// 若 <paramref name="path"/> 为空白，则不执行任何操作。
        /// 序列化或写入失败时异常会向上抛出，由调用方处理。
        /// </remarks>
        public static void WriteJson<T>(string? path, T? value, bool indented = true, JsonSerializerSettings? settings = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            Formatting formatting = indented ? Formatting.Indented : Formatting.None;
            string json = JsonConvert.SerializeObject(value, formatting, settings);
            WriteAllText(path, json);
        }

        /// <summary>
        /// 读取指定文件的全部字节内容。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <returns>
        /// 文件字节数组；若 <paramref name="path"/> 为空白或文件不存在，则返回空数组。
        /// </returns>
        public static byte[] ReadAllBytes(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Array.Empty<byte>();

            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// 将字节数组写入指定文件，并在必要时自动创建目标目录。
        /// </summary>
        /// <param name="path">目标文件路径。</param>
        /// <param name="bytes">要写入的字节数组。</param>
        /// <remarks>
        /// 若 <paramref name="path"/> 为空白，或 <paramref name="bytes"/> 为 <see langword="null"/>，则不执行任何操作。
        /// </remarks>
        public static void WriteAllBytes(string? path, byte[]? bytes)
        {
            if (string.IsNullOrWhiteSpace(path) || bytes == null)
                return;

            EnsureParentDirectory(path);
            File.WriteAllBytes(path, bytes);
        }

        #endregion

        #region 文件与目录管理

        /// <summary>
        /// 删除指定文件。若文件不存在，则忽略。
        /// </summary>
        /// <param name="path">要删除的文件路径。</param>
        /// <remarks>
        /// 删除前会尝试将文件属性设置为 <see cref="FileAttributes.Normal"/>。
        /// 若发生 IO、权限、占用等异常，方法会静默忽略。
        /// </remarks>
        public static void DeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            try
            {
                TrySetNormalAttributes(path);
                File.Delete(path);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 删除指定目录及其所有内容。若目录不存在，则忽略。
        /// </summary>
        /// <param name="directory">要删除的目录路径。</param>
        /// <remarks>
        /// 删除前会尝试将目录下所有文件和子目录属性设置为 <see cref="FileAttributes.Normal"/>。
        /// 若过程中发生异常，方法会静默忽略。
        /// 遇到符号链接、目录联接等重分析点时只删除链接本身，不递归进入链接目标。
        /// </remarks>
        public static void DeleteDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            DeleteDirectoryCore(directory);
        }

        /// <summary>
        /// 清空目录内容，但保留目录本身。
        /// </summary>
        /// <param name="directory">要清空的目录路径。</param>
        /// <remarks>
        /// 若目录不存在，则忽略。
        /// 删除过程中发生异常时，会静默忽略并继续处理其他项。
        /// 遇到符号链接、目录联接等重分析点时只删除链接本身，不递归进入链接目标。
        /// </remarks>
        public static void ClearDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            foreach (var file in GetFilesSafe(directory))
            {
                DeleteFile(file);
            }

            foreach (var dir in GetDirectoriesSafe(directory))
            {
                DeleteDirectory(dir);
            }
        }

        /// <summary>
        /// 复制文件，并在必要时自动创建目标目录。
        /// </summary>
        /// <param name="sourceFilePath">源文件路径。</param>
        /// <param name="destinationFilePath">目标文件路径。</param>
        /// <param name="overwrite">是否覆盖已存在文件。</param>
        /// <exception cref="IOException">
        /// <paramref name="overwrite"/> 为 <see langword="false"/> 且目标文件已存在。
        /// </exception>
        /// <remarks>
        /// 若源文件不存在、路径无效，或源路径与目标路径指向同一文件（规范化后按不区分大小写比较），
        /// 则不执行任何操作。
        /// </remarks>
        public static void CopyFile(string? sourceFilePath, string? destinationFilePath, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) ||
                string.IsNullOrWhiteSpace(destinationFilePath) ||
                !File.Exists(sourceFilePath))
                return;

            if (IsSamePath(sourceFilePath, destinationFilePath))
                return;

            EnsureParentDirectory(destinationFilePath);
            File.Copy(sourceFilePath, destinationFilePath, overwrite);
        }

        /// <summary>
        /// 移动文件，并在必要时自动创建目标目录。
        /// </summary>
        /// <param name="sourceFilePath">源文件路径。</param>
        /// <param name="destinationFilePath">目标文件路径。</param>
        /// <param name="overwrite">是否覆盖已存在文件。</param>
        /// <exception cref="IOException">
        /// <paramref name="overwrite"/> 为 <see langword="false"/> 且目标文件已存在；
        /// 或回退为复制后删除源文件时，源文件删除失败。
        /// </exception>
        /// <remarks>
        /// <para>
        /// 若源文件不存在或路径无效，则不执行任何操作。
        /// </para>
        /// <para>
        /// 若源路径与目标路径指向同一文件（规范化后按不区分大小写比较），则视为无操作直接返回，
        /// 避免在覆盖模式下误删源文件。本方法不支持仅修改文件名大小写的“重命名”。
        /// </para>
        /// <para>
        /// 当直接移动失败且允许覆盖时，会尝试回退为“复制后删除源文件”。
        /// 若源文件删除失败，将抛出 <see cref="IOException"/>；此时目标文件已写入，源文件仍保留。
        /// </para>
        /// </remarks>
        public static void MoveFile(string? sourceFilePath, string? destinationFilePath, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) ||
                string.IsNullOrWhiteSpace(destinationFilePath) ||
                !File.Exists(sourceFilePath))
                return;

            if (IsSamePath(sourceFilePath, destinationFilePath))
                return;

            EnsureParentDirectory(destinationFilePath);

            try
            {
                File.Move(sourceFilePath, destinationFilePath);
            }
            catch
            {
                if (!overwrite)
                    throw;

                string tempPath = GetUniqueFilePath(destinationFilePath + ".tmp");
                bool tempCreated = false;

                try
                {
                    File.Copy(sourceFilePath, tempPath, false);
                    tempCreated = true;

                    if (File.Exists(destinationFilePath))
                    {
                        File.Replace(tempPath, destinationFilePath, null);
                        tempCreated = false;
                    }
                    else
                    {
                        File.Move(tempPath, destinationFilePath);
                        tempCreated = false;
                    }
                }
                finally
                {
                    if (tempCreated)
                        DeleteFile(tempPath);
                }

                try
                {
                    TrySetNormalAttributes(sourceFilePath);
                    File.Delete(sourceFilePath);
                }
                catch (Exception ex)
                {
                    throw new IOException(
                        string.Format("文件已复制到目标路径，但源文件删除失败：{0}", sourceFilePath),
                        ex);
                }
            }
        }

        /// <summary>
        /// 重命名文件，并保持原目录不变。
        /// </summary>
        /// <param name="filePath">原文件路径。</param>
        /// <param name="newFileName">新的文件名（不含目录）。</param>
        /// <param name="overwrite">是否覆盖已存在文件。</param>
        /// <returns>重命名后的新路径；若输入无效，则返回原路径或空字符串。</returns>
        /// <remarks>
        /// 若 <paramref name="newFileName"/> 包含目录部分、为绝对路径或不是有效文件名，则视为无效输入。
        /// </remarks>
        public static string RenameFile(string? filePath, string? newFileName, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return filePath ?? string.Empty;

            if (string.IsNullOrWhiteSpace(newFileName))
                return filePath;

            string fileNameOnly;

            try
            {
                fileNameOnly = Path.GetFileName(newFileName);
            }
            catch
            {
                return filePath;
            }

            if (string.IsNullOrWhiteSpace(fileNameOnly))
                return filePath;

            if (!string.Equals(fileNameOnly, newFileName, StringComparison.Ordinal))
                return filePath;

            if (!IsValidFileName(fileNameOnly))
                return filePath;

            string? directory = Path.GetDirectoryName(filePath);
            string destinationPath = Path.Combine(directory ?? string.Empty, fileNameOnly);

            if (IsSamePath(filePath, destinationPath))
                return filePath;

            MoveFile(filePath, destinationPath, overwrite);
            return destinationPath;
        }

        #endregion

        #region 路径与文件名

        /// <summary>
        /// 获取一个不会与现有文件或目录冲突的文件路径。
        /// </summary>
        /// <param name="filePath">原始文件路径。</param>
        /// <returns>
        /// 若原路径不存在，则返回原路径；
        /// 若已存在，则返回自动追加序号后的新路径。
        /// </returns>
        public static string GetUniqueFilePath(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            if (!File.Exists(filePath) && !Directory.Exists(filePath))
                return filePath;

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            string extension = Path.GetExtension(filePath);

            int index = 1;
            string candidate;

            do
            {
                candidate = Path.Combine(
                    directory,
                    string.Format("{0} ({1}){2}", fileNameWithoutExtension, index, extension));
                index++;
            }
            while (File.Exists(candidate) || Directory.Exists(candidate));

            return candidate;
        }

        /// <summary>
        /// 将文件名中的非法字符替换为指定字符。
        /// </summary>
        /// <param name="fileName">原始文件名。</param>
        /// <param name="replacement">替换字符；若该字符本身属于非法字符，则改用 '_'。</param>
        /// <returns>
        /// 替换非法字符后的文件名；若 <paramref name="fileName"/> 为 <see langword="null"/>，则返回空字符串。
        /// 对 Windows 保留设备名会添加前缀 <c>_</c>，末尾空格或点会被移除。
        /// </returns>
        public static string GetSafeFileName(string? fileName, char replacement = '_')
        {
            if (fileName == null)
                return string.Empty;

            fileName = fileName.Trim();

            char[] invalidChars = Path.GetInvalidFileNameChars();

            if (Array.IndexOf(invalidChars, replacement) >= 0)
                replacement = '_';

            string result = fileName;

            for (int i = 0; i < invalidChars.Length; i++)
            {
                result = result.Replace(invalidChars[i], replacement);
            }

            result = result.TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(result))
                return "_";

            return IsReservedFileName(result) ? "_" + result : result;
        }

        #endregion

        #region 文件大小

        /// <summary>
        /// 获取文件大小（字节）。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <returns>
        /// 文件大小（字节）；若路径无效或文件不存在，则返回 0。
        /// </returns>
        public static long GetFileSize(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return 0;

            try
            {
                return new FileInfo(path).Length;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 将字节大小格式化为便于阅读的文本。
        /// </summary>
        /// <param name="sizeInBytes">字节大小。</param>
        /// <returns>形如 1.23 KB、45.67 MB 的可读文本。</returns>
        public static string GetReadableFileSize(long sizeInBytes)
        {
            if (sizeInBytes < 0)
                sizeInBytes = 0;

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = sizeInBytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? string.Format("{0} {1}", (long)size, units[unitIndex])
                : string.Format("{0:0.##} {1}", size, units[unitIndex]);
        }

        #endregion

        #region 目录辅助

        /// <summary>
        /// 确保指定目录存在；若不存在则创建。
        /// </summary>
        /// <param name="directory">目录路径。</param>
        /// <returns>
        /// 目录路径本身；若输入为空白，则返回空字符串。
        /// </returns>
        public static string EnsureDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                return string.Empty;

            Directory.CreateDirectory(directory);
            return directory;
        }

        #endregion

        #region 私有辅助

        private static void EnsureParentDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
        }

        private static void TrySetNormalAttributes(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
            catch
            {
            }
        }

        private static void DeleteDirectoryCore(string directory)
        {
            try
            {
                if (IsReparsePoint(directory))
                {
                    TrySetNormalAttributes(directory);
                    Directory.Delete(directory, false);
                    return;
                }

                foreach (var file in GetFilesSafe(directory))
                {
                    DeleteFile(file);
                }

                foreach (var dir in GetDirectoriesSafe(directory))
                {
                    DeleteDirectoryCore(dir);
                }

                TrySetNormalAttributes(directory);
                Directory.Delete(directory, false);
            }
            catch
            {
            }
        }

        private static string[] GetFilesSafe(string directory)
        {
            try
            {
                return Directory.GetFiles(directory);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string[] GetDirectoriesSafe(string directory)
        {
            try
            {
                return Directory.GetDirectories(directory);
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static bool IsReparsePoint(string path)
        {
            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSamePath(string? left, string? right)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                    return false;

                string fullLeft = Path.GetFullPath(left);
                string fullRight = Path.GetFullPath(right);
                return string.Equals(fullLeft, fullRight, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool IsValidFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            if (fileName == "." || fileName == "..")
                return false;

            if (fileName.EndsWith(" ", StringComparison.Ordinal) ||
                fileName.EndsWith(".", StringComparison.Ordinal))
                return false;

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;

            return !IsReservedFileName(fileName);
        }

        private static bool IsReservedFileName(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName).TrimEnd(' ', '.');

            return string.Equals(name, "CON", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "PRN", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "AUX", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "NUL", StringComparison.OrdinalIgnoreCase) ||
                   IsReservedDeviceName(name, "COM") ||
                   IsReservedDeviceName(name, "LPT");
        }

        private static bool IsReservedDeviceName(string name, string prefix)
        {
            if (name.Length != 4)
                return false;

            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                   name[3] >= '1' &&
                   name[3] <= '9';
        }

        #endregion
    }
}
