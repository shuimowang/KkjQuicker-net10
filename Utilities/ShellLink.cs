using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace KkjQuicker.Utilities
{
    /// <summary>
    /// 表示一个 Windows 快捷方式（.lnk）文件。
    /// </summary>
    /// <remarks>
    /// 本类基于 Windows Shell 的 IShellLinkW 与 IPersistFile 实现。
    /// Load 只负责加载快捷方式文件，不会自动 Resolve，以避免访问磁盘、网络路径或修改快捷方式状态。
    /// </remarks>
    public sealed class ShellLink : IDisposable
    {
        private const int MaxPath = 260;
        private const int LongPathBufferSize = 32767;
        private const int InfoTipSize = 1024;
        private const uint SLGP_UNCPRIORITY = 0x0002;

        private object? _comObject;
        private IShellLinkW? _shellLink;
        private bool _isDisposed;

        /// <summary>
        /// 当前已加载或保存的快捷方式文件路径。
        /// </summary>
        public string CurrentFile { get; private set; }

        /// <summary>
        /// 快捷方式目标路径。
        /// </summary>
        public string TargetPath
        {
            get
            {
                StringBuilder builder = new(LongPathBufferSize);
                var findData = new WIN32_FIND_DATAW();

                ShellLinkInterface.GetPath(
                    builder,
                    builder.Capacity,
                    ref findData,
                    SLGP_UNCPRIORITY);

                return builder.ToString();
            }
            set
            {
                ShellLinkInterface.SetPath(value ?? string.Empty);
            }
        }

        /// <summary>
        /// 快捷方式工作目录。
        /// </summary>
        public string WorkingDirectory
        {
            get
            {
                StringBuilder builder = new(LongPathBufferSize);
                ShellLinkInterface.GetWorkingDirectory(builder, builder.Capacity);
                return builder.ToString();
            }
            set
            {
                ShellLinkInterface.SetWorkingDirectory(value ?? string.Empty);
            }
        }

        /// <summary>
        /// 快捷方式启动参数。
        /// </summary>
        public string Arguments
        {
            get
            {
                StringBuilder builder = new(InfoTipSize);
                ShellLinkInterface.GetArguments(builder, builder.Capacity);
                return builder.ToString();
            }
            set
            {
                ShellLinkInterface.SetArguments(value ?? string.Empty);
            }
        }

        /// <summary>
        /// 快捷方式描述。
        /// </summary>
        public string Description
        {
            get
            {
                StringBuilder builder = new(InfoTipSize);
                ShellLinkInterface.GetDescription(builder, builder.Capacity);
                return builder.ToString();
            }
            set
            {
                ShellLinkInterface.SetDescription(value ?? string.Empty);
            }
        }

        /// <summary>
        /// 快捷方式图标文件路径。
        /// </summary>
        public string IconPath
        {
            get
            {
                int iconIndex;
                return GetIconLocation(out iconIndex);
            }
            set
            {
                int iconIndex;
                GetIconLocation(out iconIndex);
                ShellLinkInterface.SetIconLocation(value ?? string.Empty, iconIndex);
            }
        }

        /// <summary>
        /// 快捷方式图标索引。
        /// </summary>
        public int IconIndex
        {
            get
            {
                int iconIndex;
                GetIconLocation(out iconIndex);
                return iconIndex;
            }
            set
            {
                int iconIndex;
                string iconPath = GetIconLocation(out iconIndex);
                ShellLinkInterface.SetIconLocation(iconPath, value);
            }
        }

        /// <summary>
        /// 快捷方式启动窗口显示方式。
        /// </summary>
        public int ShowCommand
        {
            get
            {
                int showCommand;
                ShellLinkInterface.GetShowCmd(out showCommand);
                return showCommand;
            }
            set
            {
                ShellLinkInterface.SetShowCmd(value);
            }
        }

        public ShellLink()
        {
            _comObject = new ShellLinkComObject();
            _shellLink = (IShellLinkW)_comObject;
            CurrentFile = string.Empty;
        }

        public ShellLink(string linkFile)
            : this()
        {
            try
            {
                Load(linkFile);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 加载指定快捷方式文件。
        /// </summary>
        /// <param name="linkFile">快捷方式文件路径。</param>
        /// <exception cref="ArgumentException">路径为空白，或扩展名不是 .lnk。</exception>
        /// <exception cref="FileNotFoundException">快捷方式文件不存在。</exception>
        /// <exception cref="ObjectDisposedException">对象已释放。</exception>
        public void Load(string linkFile)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(linkFile))
                throw new ArgumentException("快捷方式文件路径不能为空。", nameof(linkFile));

            if (!string.Equals(Path.GetExtension(linkFile), ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("快捷方式文件扩展名必须是 .lnk。", nameof(linkFile));

            if (!File.Exists(linkFile))
                throw new FileNotFoundException("快捷方式文件不存在。", linkFile);

            string fullPath = Path.GetFullPath(linkFile);
            PersistFile.Load(fullPath, 0);
            CurrentFile = fullPath;
        }

        /// <summary>
        /// 保存到当前快捷方式文件。
        /// </summary>
        /// <exception cref="InvalidOperationException">当前没有关联的快捷方式文件。</exception>
        /// <exception cref="ObjectDisposedException">对象已释放。</exception>
        public void Save()
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(CurrentFile))
                throw new InvalidOperationException("当前没有关联的快捷方式文件。");

            Save(CurrentFile);
        }

        /// <summary>
        /// 保存到指定快捷方式文件。
        /// </summary>
        /// <param name="linkFile">快捷方式文件路径。</param>
        /// <exception cref="ArgumentException">路径为空白，或扩展名不是 .lnk。</exception>
        /// <exception cref="ObjectDisposedException">对象已释放。</exception>
        public void Save(string linkFile)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(linkFile))
                throw new ArgumentException("快捷方式文件路径不能为空。", nameof(linkFile));

            if (!string.Equals(Path.GetExtension(linkFile), ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("快捷方式文件扩展名必须是 .lnk。", nameof(linkFile));

            string fullPath = Path.GetFullPath(linkFile);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            PersistFile.Save(fullPath, true);
            CurrentFile = fullPath;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _shellLink = null;

            if (_comObject != null)
            {
                try
                {
                    if (Marshal.IsComObject(_comObject))
                        Marshal.FinalReleaseComObject(_comObject);
                }
                catch
                {
                }

                _comObject = null;
            }
        }

        public static bool TryGetTargetPath(string linkFile, out string targetPath)
        {
            targetPath = string.Empty;

            try
            {
                using (var link = new ShellLink(linkFile))
                {
                    targetPath = link.TargetPath;
                    return !string.IsNullOrEmpty(targetPath);
                }
            }
            catch (OutOfMemoryException)
            {
                throw;
            }
            catch (SEHException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        private IShellLinkW ShellLinkInterface
        {
            get
            {
                ThrowIfDisposed();
                if (_shellLink == null)
                    throw new ObjectDisposedException(nameof(ShellLink));

                return _shellLink;
            }
        }

        private ComTypes.IPersistFile PersistFile
        {
            get
            {
                ThrowIfDisposed();

                ComTypes.IPersistFile? persistFile = _comObject as ComTypes.IPersistFile;
                if (persistFile == null)
                    throw new COMException("无法获取 IPersistFile 接口。");

                return persistFile;
            }
        }

        private string GetIconLocation(out int iconIndex)
        {
            StringBuilder builder = new(LongPathBufferSize);
            ShellLinkInterface.GetIconLocation(builder, builder.Capacity, out iconIndex);
            return builder.ToString();
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ShellLink));
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkComObject
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                int cchMaxPath,
                ref WIN32_FIND_DATAW pfd,
                uint fFlags);

            void GetIDList(out IntPtr ppidl);

            void SetIDList(IntPtr pidl);

            void GetDescription(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
                int cchMaxName);

            void SetDescription(
                [MarshalAs(UnmanagedType.LPWStr)] string pszName);

            void GetWorkingDirectory(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
                int cchMaxPath);

            void SetWorkingDirectory(
                [MarshalAs(UnmanagedType.LPWStr)] string pszDir);

            void GetArguments(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
                int cchMaxPath);

            void SetArguments(
                [MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

            void GetHotkey(out ushort pwHotkey);

            void SetHotkey(ushort wHotkey);

            void GetShowCmd(out int piShowCmd);

            void SetShowCmd(int iShowCmd);

            void GetIconLocation(
                [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                int cchIconPath,
                out int piIcon);

            void SetIconLocation(
                [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
                int iIcon);

            void SetRelativePath(
                [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
                uint dwReserved);

            void Resolve(IntPtr hwnd, uint fFlags);

            void SetPath(
                [MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public ComTypes.FILETIME ftCreationTime;
            public ComTypes.FILETIME ftLastAccessTime;
            public ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxPath)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }
    }
}
