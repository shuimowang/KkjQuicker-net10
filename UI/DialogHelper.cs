using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供常用系统对话框的统一封装。
    /// </summary>
    /// <remarks>
    /// <para>设计目标：</para>
    /// <list type="bullet">
    /// <item><description>优先保持调用简单直观。</description></item>
    /// <item><description>尽量避免不必要的宿主窗口、焦点跳动和额外状态管理。</description></item>
    /// <item><description>统一返回"是否确认 + 结果路径"的二元组风格，便于现有代码直接接入。</description></item>
    /// </list>
    /// <para>说明：</para>
    /// <list type="bullet">
    /// <item><description>
    /// <paramref name="topmost"/> 仅在提供父窗口时，通过临时提升父窗口置顶状态来改善对话框显示层级，
    /// 不保证在所有系统环境下绝对置顶。
    /// </description></item>
    /// <item><description>
    /// <paramref name="initialDir"/> 为 <see langword="null"/>、空白字符串或不存在的目录时，由系统决定默认位置。
    /// </description></item>
    /// </list>
    /// </remarks>
    public static class DialogHelper
    {
        /// <summary>
        /// 显示文件选择对话框。
        /// </summary>
        /// <param name="filter">文件筛选器，例如 <c>"文本文件|*.txt|所有文件|*.*"</c>。</param>
        /// <param name="defaultExt">默认扩展名，例如 <c>".txt"</c>。</param>
        /// <param name="defaultFileName">默认显示的文件名。</param>
        /// <param name="initialDir">初始目录。</param>
        /// <param name="title">对话框标题；为 <see langword="null"/> 或空字符串时使用系统默认标题。</param>
        /// <param name="filterIndex">默认选中的筛选器索引，从 1 开始。</param>
        /// <param name="topmost">是否尝试以前置方式显示对话框。</param>
        /// <param name="parentWindow">父窗口；为 <see langword="null"/> 时以无宿主方式显示。</param>
        /// <returns>
        /// <c>(true, 文件路径)</c> 表示用户已确认选择；<c>(false, "")</c> 表示用户取消。
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="filter"/> 为空白字符串。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="filterIndex"/> 小于 1。</exception>
        public static Tuple<bool, string> ShowSelectFileDialog(
            string filter,
            string defaultExt,
            string defaultFileName,
            string initialDir,
            string title = null,
            int filterIndex = 1,
            bool topmost = false,
            Window parentWindow = null)
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
        /// <param name="filter">文件筛选器，例如 <c>"文本文件|*.txt|所有文件|*.*"</c>。</param>
        /// <param name="defaultExt">默认扩展名，例如 <c>".txt"</c>。</param>
        /// <param name="defaultFileName">默认显示的文件名。</param>
        /// <param name="initialDir">初始目录。</param>
        /// <param name="title">对话框标题；为 <see langword="null"/> 或空字符串时使用系统默认标题。</param>
        /// <param name="overwritePrompt">目标文件已存在时是否提示用户确认覆盖。</param>
        /// <param name="addExtension">用户未输入扩展名时是否自动补全默认扩展名。</param>
        /// <param name="filterIndex">默认选中的筛选器索引，从 1 开始。</param>
        /// <param name="topmost">是否尝试以前置方式显示对话框。</param>
        /// <param name="parentWindow">父窗口；为 <see langword="null"/> 时以无宿主方式显示。</param>
        /// <returns>
        /// <c>(true, 文件路径)</c> 表示用户已确认选择；<c>(false, "")</c> 表示用户取消。
        /// </returns>
        /// <exception cref="ArgumentException"><paramref name="filter"/> 为空白字符串。</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="filterIndex"/> 小于 1。</exception>
        public static Tuple<bool, string> ShowSaveFileDialog(
            string filter,
            string defaultExt,
            string defaultFileName,
            string initialDir,
            string title = null,
            bool overwritePrompt = true,
            bool addExtension = true,
            int filterIndex = 1,
            bool topmost = false,
            Window parentWindow = null)
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
        /// <param name="initialDir">初始选中的文件夹路径。</param>
        /// <param name="title">对话框说明文本；为 <see langword="null"/> 或空字符串时使用系统默认说明。</param>
        /// <param name="showNewFolderButton">是否显示"新建文件夹"按钮。</param>
        /// <param name="topmost">是否尝试以前置方式显示对话框。</param>
        /// <param name="parentWindow">父窗口；为 <see langword="null"/> 时以无宿主方式显示。</param>
        /// <returns>
        /// <c>(true, 文件夹路径)</c> 表示用户已确认选择；<c>(false, "")</c> 表示用户取消。
        /// </returns>
        public static Tuple<bool, string> ShowSelectFolderDialog(
            string initialDir,
            string title = null,
            bool showNewFolderButton = true,
            bool topmost = false,
            Window parentWindow = null)
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

                        result = dialog.ShowDialog(new Win32WindowAdapter(parentWindow));
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

        private static bool? ShowCommonDialog(FileDialog dialog, bool topmost, Window parentWindow)
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

        /// <summary>
        /// 将 WPF <see cref="Window"/> 适配为 WinForms 所需的窗口句柄宿主。
        /// </summary>
        private sealed class Win32WindowAdapter : Forms.IWin32Window
        {
            public IntPtr Handle { get; }

            public Win32WindowAdapter(Window window)
            {
                if (window != null)
                    Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            }
        }
    }
}