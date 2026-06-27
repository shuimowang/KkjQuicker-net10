using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KkjQuicker.Utilities.Win32
{
    /// <summary>
    /// Provides helpers for reading Windows Shell icons from file-system paths and known folders.
    /// </summary>
    public static class ShellIconHelper
    {
        private static readonly object KnownFolderPathLock = new();

        private static readonly Guid[] KnownFolderIds =
        [
            new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), // Desktop
            new Guid("374DE290-123F-4565-9164-39C4925E467B"), // Downloads
            new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), // Documents
            new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), // Pictures
            new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), // Music
            new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC")  // Videos
        ];

        private static Dictionary<string, Guid>? _knownFolderPathMap;

        /// <summary>
        /// Gets the Windows Shell icon for a file, directory, extension-like path, or known folder path.
        /// </summary>
        /// <param name="path">The path or pseudo path. A trailing slash or ".folder" requests a folder icon.</param>
        /// <param name="width">The bitmap width requested from the Shell icon handle.</param>
        /// <param name="height">The bitmap height requested from the Shell icon handle.</param>
        /// <returns>A frozen <see cref="ImageSource"/> when an icon is available; otherwise <see langword="null"/>.</returns>
        public static ImageSource? GetIcon(string? path, int width = 32, int height = 32)
        {
            path = NormalizeInputPath(path);
            if (path.Length == 0)
                return null;

            IntPtr iconHandle = IntPtr.Zero;

            try
            {
                bool existsAsDirectory = Directory.Exists(path);

                if (existsAsDirectory)
                {
                    ImageSource? knownFolderIcon = TryGetKnownFolderIcon(path, width, height);
                    if (knownFolderIcon != null)
                        return knownFolderIcon;
                }

                var info = new SHFILEINFO();
                bool existsAsFile = File.Exists(path);

                uint attributes = 0;
                uint flags = SHGFI_ICON;

                if (!existsAsFile && !existsAsDirectory)
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = ShouldUseDirectoryIcon(path)
                        ? FILE_ATTRIBUTE_DIRECTORY
                        : FILE_ATTRIBUTE_NORMAL;
                }

                IntPtr result = SHGetFileInfo(
                    path,
                    attributes,
                    ref info,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    flags);

                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                    return null;

                iconHandle = info.hIcon;
                return CreateFrozenImageSource(iconHandle, width, height);
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                    DestroyIcon(iconHandle);
            }
        }

        private static ImageSource? TryGetKnownFolderIcon(string path, int width, int height)
        {
            if (!TryGetKnownFolderIdByPath(path, out Guid knownFolderId))
                return null;

            IntPtr pidl = IntPtr.Zero;
            IntPtr iconHandle = IntPtr.Zero;

            try
            {
                Guid id = knownFolderId;
                int hr = SHGetKnownFolderIDList(
                    ref id,
                    0,
                    IntPtr.Zero,
                    out pidl);

                if (hr != 0 || pidl == IntPtr.Zero)
                    return null;

                var info = new SHFILEINFO();
                IntPtr result = SHGetFileInfo(
                    pidl,
                    0,
                    ref info,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    SHGFI_PIDL | SHGFI_ICON);

                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                    return null;

                iconHandle = info.hIcon;
                return CreateFrozenImageSource(iconHandle, width, height);
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                    DestroyIcon(iconHandle);

                if (pidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pidl);
            }
        }

        private static ImageSource CreateFrozenImageSource(IntPtr iconHandle, int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(width, height));

            if (source.CanFreeze)
                source.Freeze();

            return source;
        }

        private static bool TryGetKnownFolderIdByPath(string path, out Guid knownFolderId)
        {
            knownFolderId = Guid.Empty;

            string normalizedPath = NormalizePathForCompare(path);
            if (normalizedPath.Length == 0)
                return false;

            Dictionary<string, Guid> map = GetKnownFolderPathMap();
            return map.TryGetValue(normalizedPath, out knownFolderId);
        }

        private static Dictionary<string, Guid> GetKnownFolderPathMap()
        {
            lock (KnownFolderPathLock)
            {
                if (_knownFolderPathMap != null)
                    return _knownFolderPathMap;

                var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < KnownFolderIds.Length; i++)
                {
                    Guid id = KnownFolderIds[i];

                    if (!TryGetKnownFolderPath(id, out string? knownPath))
                        continue;

                    string normalizedKnownPath = NormalizePathForCompare(knownPath);
                    if (normalizedKnownPath.Length == 0)
                        continue;

                    map.TryAdd(normalizedKnownPath, id);
                }

                _knownFolderPathMap = map;
                return _knownFolderPathMap;
            }
        }

        private static bool TryGetKnownFolderPath(Guid knownFolderId, out string? path)
        {
            path = null;
            IntPtr pathPtr = IntPtr.Zero;

            try
            {
                Guid id = knownFolderId;
                int hr = SHGetKnownFolderPath(
                    ref id,
                    0,
                    IntPtr.Zero,
                    out pathPtr);

                if (hr != 0 || pathPtr == IntPtr.Zero)
                    return false;

                path = Marshal.PtrToStringUni(pathPtr);
                return !string.IsNullOrWhiteSpace(path);
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pathPtr);
            }
        }

        private static string NormalizeInputPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Environment.ExpandEnvironmentVariables(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }

        private static string NormalizePathForCompare(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                path = Environment.ExpandEnvironmentVariables(path.Trim());
                path = Path.GetFullPath(path);

                return path.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ShouldUseDirectoryIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            if (string.Equals(path, ".folder", StringComparison.OrdinalIgnoreCase))
                return true;

            return path.EndsWith("\\", StringComparison.Ordinal)
                || path.EndsWith("/", StringComparison.Ordinal);
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_PIDL = 0x000000008;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(
            string pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHGetFileInfo")]
        private static extern IntPtr SHGetFileInfo(
            IntPtr pszPath,
            uint dwFileAttributes,
            ref SHFILEINFO psfi,
            uint cbFileInfo,
            uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppszPath);

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderIDList(
            ref Guid rfid,
            uint dwFlags,
            IntPtr hToken,
            out IntPtr ppidl);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
