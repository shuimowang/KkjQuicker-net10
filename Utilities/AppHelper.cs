using KkjQuicker.Utilities.Win32;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace KkjQuicker.Utilities
{
    public static class AppHelper
    {

        /// <summary>
        /// 查找当前 <see cref="Application"/> 中第一个指定类型的根窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>找到则返回对应窗口；未找到则返回 <see langword="null"/>。</returns>
        public static T? FindRootWindow<T>() where T : Window
        {
            return Application.Current == null
                ? null
                : Application.Current.Windows.OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// 查找当前 <see cref="Application"/> 中所有指定类型的根窗口。
        /// </summary>
        /// <typeparam name="T">窗口类型。</typeparam>
        /// <returns>匹配到的窗口序列；若当前应用不存在则返回空序列。</returns>
        public static IEnumerable<T> FindRootWindows<T>() where T : Window
        {
            return Application.Current == null
                ? Enumerable.Empty<T>()
                : Application.Current.Windows.OfType<T>();
        }

        public static void RunOnUiThread(
            Action? action,
            DispatcherPriority priority = DispatcherPriority.Normal,
            bool waitForCompletion = false)
        {
            if (action == null)
                return;

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            if (waitForCompletion)
                dispatcher.Invoke(action, priority);
            else
                dispatcher.BeginInvoke(action, priority);
        }

        public static bool SetForegroundWindow(nint hWnd)
        {
            return NativeMethods.SetForegroundWindow((IntPtr)hWnd);
        }

        /// <summary>
        /// 显示文件选择对话框。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="filter"/> 为空白字符串。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="filterIndex"/> 小于 1。</exception>
        public static Tuple<bool, string> ShowSelectFileDialog(
            string filter,
            string defaultExt,
            string defaultFileName,
            string initialDir,
            string? title = null,
            int filterIndex = 1,
            bool topmost = false,
            Window? parentWindow = null)
        {
            if (string.IsNullOrWhiteSpace(filter))
                throw new ArgumentException("文件筛选器不能为空。", nameof(filter));
            if (filterIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(filterIndex), "筛选器索引从 1 开始。");

            var dialog = new OpenFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = defaultFileName,
                FilterIndex = filterIndex
            };

            if (!string.IsNullOrWhiteSpace(title))
                dialog.Title = title;

            if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                dialog.InitialDirectory = initialDir;

            bool? result = ShowCommonDialog(dialog, topmost, parentWindow);
            return result == true
                ? Tuple.Create(true, dialog.FileName)
                : Tuple.Create(false, string.Empty);
        }

        /// <summary>
        /// 显示文件保存对话框。
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="filter"/> 为空白字符串。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="filterIndex"/> 小于 1。</exception>
        public static Tuple<bool, string> ShowSaveFileDialog(
            string filter,
            string defaultExt,
            string defaultFileName,
            string initialDir,
            string? title = null,
            bool overwritePrompt = true,
            bool addExtension = true,
            int filterIndex = 1,
            bool topmost = false,
            Window? parentWindow = null)
        {
            if (string.IsNullOrWhiteSpace(filter))
                throw new ArgumentException("文件筛选器不能为空。", nameof(filter));
            if (filterIndex < 1)
                throw new ArgumentOutOfRangeException(nameof(filterIndex), "筛选器索引从 1 开始。");

            var dialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = defaultExt,
                FileName = defaultFileName,
                FilterIndex = filterIndex,
                OverwritePrompt = overwritePrompt,
                AddExtension = addExtension
            };

            if (!string.IsNullOrWhiteSpace(title))
                dialog.Title = title;

            if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                dialog.InitialDirectory = initialDir;

            bool? result = ShowCommonDialog(dialog, topmost, parentWindow);
            return result == true
                ? Tuple.Create(true, dialog.FileName)
                : Tuple.Create(false, string.Empty);
        }

        /// <summary>
        /// 显示文件夹选择对话框。
        /// </summary>
        public static Tuple<bool, string> ShowSelectFolderDialog(
            string initialDir,
            string? title = null,
            bool showNewFolderButton = true,
            bool topmost = false,
            Window? parentWindow = null)
        {
            using (var dialog = new Forms.FolderBrowserDialog())
            {
                dialog.ShowNewFolderButton = showNewFolderButton;

                if (!string.IsNullOrWhiteSpace(title))
                    dialog.Description = title;

                if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                    dialog.SelectedPath = initialDir;

                Forms.DialogResult result;

                if (parentWindow != null)
                {
                    bool originalTopmost = parentWindow.Topmost;
                    try
                    {
                        if (topmost && !originalTopmost)
                            parentWindow.Topmost = true;

                        IntPtr ownerHandle = parentWindow.GetHandle();
                        if (ownerHandle == IntPtr.Zero)
                        {
                            result = dialog.ShowDialog();
                        }
                        else
                        {
                            var ownerWindow = new Forms.NativeWindow();
                            try
                            {
                                ownerWindow.AssignHandle(ownerHandle);
                                result = dialog.ShowDialog(ownerWindow);
                            }
                            finally
                            {
                                ownerWindow.ReleaseHandle();
                            }
                        }
                    }
                    finally
                    {
                        parentWindow.Topmost = originalTopmost;
                    }
                }
                else
                {
                    result = dialog.ShowDialog();
                }

                return result == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
                    ? Tuple.Create(true, dialog.SelectedPath)
                    : Tuple.Create(false, string.Empty);
            }
        }

        public static void TryOpenUrlOrFile(string pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl))
                return;

            pathOrUrl = pathOrUrl.Trim();

            // 去掉外层引号，方便直接粘贴路径：
            // "C:\Users\xxx\Desktop\Codex.lnk"
            if (pathOrUrl.Length >= 2 &&
                pathOrUrl.StartsWith("\"", StringComparison.Ordinal) &&
                pathOrUrl.EndsWith("\"", StringComparison.Ordinal))
            {
                pathOrUrl = pathOrUrl.Substring(1, pathOrUrl.Length - 2).Trim();
            }

            pathOrUrl = Environment.ExpandEnvironmentVariables(pathOrUrl);

            try
            {
                // shell:startup / shell:AppsFolder 等
                if (pathOrUrl.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = pathOrUrl,
                        UseShellExecute = true
                    });
                    return;
                }

                // 本地路径：文件夹
                if (Path.IsPathRooted(pathOrUrl) && Directory.Exists(pathOrUrl))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = pathOrUrl,
                        UseShellExecute = true
                    });
                    return;
                }

                // 本地路径：文件、exe、lnk、txt、html 等
                if (Path.IsPathRooted(pathOrUrl) && File.Exists(pathOrUrl))
                {
                    string? dir = Path.GetDirectoryName(pathOrUrl);

                    var psi = new ProcessStartInfo
                    {
                        FileName = pathOrUrl,
                        UseShellExecute = true
                    };

                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        psi.WorkingDirectory = dir;

                    Process.Start(psi);
                    return;
                }

                // URL 或系统可识别协议
                Process.Start(new ProcessStartInfo
                {
                    FileName = pathOrUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "无法打开文件或网址：\r\n" + pathOrUrl + "\r\n\r\n" + ex.Message,
                    "打开失败");
            }
        }

        private static bool? ShowCommonDialog(FileDialog dialog, bool topmost, Window? parentWindow)
        {
            if (parentWindow != null)
            {
                bool originalTopmost = parentWindow.Topmost;
                try
                {
                    if (topmost && !originalTopmost)
                        parentWindow.Topmost = true;

                    return dialog.ShowDialog(parentWindow);
                }
                finally
                {
                    parentWindow.Topmost = originalTopmost;
                }
            }

            return dialog.ShowDialog();
        }

    }
}