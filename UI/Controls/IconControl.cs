// Fix #1, #2, #3, #4, #5
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using FontAwesome5;

namespace KkjQuicker.UI.Controls
{
    /// <summary>
    /// 显示 FontAwesome5 矢量图标、网络图片、本地图片或 Shell 路径图标的轻量图标控件。
    /// </summary>
    public class IconControl : ContentControl
    {
        private const double DefaultIconSize = 16.0;
        private const int MaxImageCacheCount = 256;

        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly object ImageCacheLock = new object();

        private static readonly Dictionary<string, ImageSource> ImageCache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        private static readonly object KnownFolderPathLock = new object();
        private static Dictionary<string, Guid> KnownFolderPathMap;

        private static readonly Guid[] KnownFolderIds =
        {
            new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"), // Desktop
            new Guid("374DE290-123F-4565-9164-39C4925E467B"), // Downloads
            new Guid("FDD39AD0-238F-46AF-ADB4-6C85480369C7"), // Documents
            new Guid("33E28130-4E1E-4676-835A-98395C3BC3BB"), // Pictures
            new Guid("4BD8D571-6D19-48D3-BE97-422220080E43"), // Music
            new Guid("18989B1D-99B5-455B-841C-AB7C74E4DDFC")  // Videos
        };

        private CancellationTokenSource _loadCancellation;
        private int _loadVersion;
        private bool _isLoading;

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(
                "Icon",
                typeof(object),
                typeof(IconControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnIconChanged));

        public static readonly DependencyProperty DefaultIconColorProperty =
            DependencyProperty.RegisterAttached(
                "DefaultIconColor",
                typeof(string),
                typeof(IconControl),
                new FrameworkPropertyMetadata(
                    string.Empty,
                    FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnIconAppearanceChanged));

        public static readonly DependencyProperty DefaultIconBrushProperty =
            DependencyProperty.RegisterAttached(
                "DefaultIconBrush",
                typeof(Brush),
                typeof(IconControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnIconAppearanceChanged));

        private static readonly DependencyPropertyKey HasIconPropertyKey =
            DependencyProperty.RegisterReadOnly(
                "HasIcon",
                typeof(bool),
                typeof(IconControl),
                new FrameworkPropertyMetadata(false));

        public static readonly DependencyProperty HasIconProperty =
            HasIconPropertyKey.DependencyProperty;

        static IconControl()
        {
            ForegroundProperty.OverrideMetadata(
                typeof(IconControl),
                new FrameworkPropertyMetadata(
                    Brushes.Black,
                    FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnIconAppearanceChanged));
        }

        public IconControl()
        {
            IsTabStop = false;
            Focusable = false;
            IsHitTestVisible = false;

            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            SnapsToDevicePixels = true;
            ClipToBounds = true;

            SetCurrentValue(WidthProperty, DefaultIconSize);
            SetCurrentValue(HeightProperty, DefaultIconSize);
        }

        /// <summary>
        /// 要显示的图标。支持 ImageSource、EFontAwesomeIcon、fa:、url:、http(s)、icon: 和本地图片路径。
        /// </summary>
        public object Icon
        {
            get { return GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        /// <summary>
        /// 当前 Icon 是否有可处理的输入值。
        /// </summary>
        public bool HasIcon
        {
            get { return (bool)GetValue(HasIconProperty); }
            private set { SetValue(HasIconPropertyKey, value); }
        }

        /// <summary>
        /// 默认图标颜色字符串，可在父元素上设置并继承到子 IconControl。
        /// </summary>
        public string DefaultIconColor
        {
            get { return GetDefaultIconColor(this); }
            set { SetDefaultIconColor(this, value); }
        }

        /// <summary>
        /// 默认图标画刷，可在父元素上设置并继承到子 IconControl。
        /// </summary>
        public Brush DefaultIconBrush
        {
            get { return GetDefaultIconBrush(this); }
            set { SetDefaultIconBrush(this, value); }
        }

        /// <summary>
        /// 获取默认图标颜色字符串。
        /// </summary>
        public static string GetDefaultIconColor(DependencyObject obj)
        {
            return (string)obj.GetValue(DefaultIconColorProperty);
        }

        /// <summary>
        /// 设置默认图标颜色字符串。
        /// </summary>
        public static void SetDefaultIconColor(DependencyObject obj, string value)
        {
            obj.SetValue(DefaultIconColorProperty, value);
        }

        /// <summary>
        /// 获取默认图标画刷。
        /// </summary>
        public static Brush GetDefaultIconBrush(DependencyObject obj)
        {
            return (Brush)obj.GetValue(DefaultIconBrushProperty);
        }

        /// <summary>
        /// 设置默认图标画刷。
        /// </summary>
        public static void SetDefaultIconBrush(DependencyObject obj, Brush value)
        {
            obj.SetValue(DefaultIconBrushProperty, value);
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);

            if (VisualParent == null)
            {
                CancelCurrentLoad();
            }
            else if (HasIcon && Content == null && !_isLoading)
            {
                UpdateIcon();
            }
        }

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((IconControl)d).UpdateIcon();
        }

        private static void OnIconAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as IconControl;
            if (control == null)
                return;

            var svgAwesome = control.Content as SvgAwesome;
            if (svgAwesome == null)
                return;

            EFontAwesomeIcon fontIcon;
            Brush explicitBrush;
            if (TryGetFontAwesomeIcon(control.Icon, out fontIcon, out explicitBrush))
                svgAwesome.Foreground = explicitBrush ?? control.ResolveIconBrush();
            else
                svgAwesome.Foreground = control.ResolveIconBrush();
        }

        private void UpdateIcon()
        {
            CancelCurrentLoad();

            Content = null;
            HasIcon = HasIconValue(Icon);

            if (!HasIcon)
                return;

            var imageSource = Icon as ImageSource;
            if (imageSource != null)
            {
                SetImage(imageSource);
                return;
            }

            EFontAwesomeIcon fontIcon;
            Brush explicitBrush;
            if (TryGetFontAwesomeIcon(Icon, out fontIcon, out explicitBrush))
            {
                SetFontAwesomeIcon(fontIcon, explicitBrush);
                return;
            }

            var text = Icon as string;
            if (string.IsNullOrWhiteSpace(text))
                return;

            text = NormalizeIconText(text);
            if (text.Length == 0)
                return;

            var version = ++_loadVersion;
            var cancellation = new CancellationTokenSource();

            _loadCancellation = cancellation;
            _isLoading = true;

            LoadImageAsync(text, version, cancellation.Token);
        }

        private static bool HasIconValue(object icon)
        {
            if (icon == null)
                return false;

            if (icon is ImageSource)
                return true;

            if (icon is EFontAwesomeIcon)
                return true;

            var text = icon as string;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return NormalizeIconText(text).Length > 0;
        }

        private static string NormalizeIconText(string text)
        {
            if (text == null)
                return string.Empty;

            text = text.Trim();

            if (text.Length >= 2 && text[0] == '[' && text[text.Length - 1] == ']')
                text = text.Substring(1, text.Length - 2).Trim();

            return text;
        }

        private static bool TryGetFontAwesomeIcon(object icon, out EFontAwesomeIcon fontIcon, out Brush explicitBrush)
        {
            explicitBrush = null;

            if (icon is EFontAwesomeIcon)
            {
                fontIcon = (EFontAwesomeIcon)icon;
                return true;
            }

            var text = icon as string;
            if (string.IsNullOrWhiteSpace(text))
            {
                fontIcon = default(EFontAwesomeIcon);
                return false;
            }

            text = NormalizeIconText(text);

            if (!text.StartsWith("fa:", StringComparison.OrdinalIgnoreCase))
            {
                fontIcon = default(EFontAwesomeIcon);
                return false;
            }

            text = text.Substring("fa:".Length).Trim();
            if (text.Length == 0)
            {
                fontIcon = default(EFontAwesomeIcon);
                return false;
            }

            var colorSeparatorIndex = text.IndexOf(':');
            if (colorSeparatorIndex >= 0)
            {
                var colorText = text.Substring(colorSeparatorIndex + 1).Trim();
                text = text.Substring(0, colorSeparatorIndex).Trim();

                Brush brush;
                if (TryCreateBrush(colorText, out brush))
                    explicitBrush = brush;
            }

            if (text.Length == 0)
            {
                fontIcon = default(EFontAwesomeIcon);
                return false;
            }

            return Enum.TryParse(text, true, out fontIcon);
        }

        private void SetFontAwesomeIcon(EFontAwesomeIcon fontIcon, Brush explicitBrush)
        {
            var icon = new SvgAwesome
            {
                Icon = fontIcon,
                Foreground = explicitBrush ?? ResolveIconBrush(),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };

            Content = icon;
        }

        private void SetImage(ImageSource source)
        {
            if (source == null)
                return;

            var image = new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };

            Content = image;
        }

        private Brush ResolveIconBrush()
        {
            var brush = DefaultIconBrush;
            if (brush != null)
                return brush;

            var colorText = DefaultIconColor;
            if (!string.IsNullOrWhiteSpace(colorText))
            {
                Brush parsedBrush;
                if (TryCreateBrush(colorText, out parsedBrush))
                    return parsedBrush;
            }

            return Foreground ?? Brushes.Black;
        }

        private static bool TryCreateBrush(string colorText, out Brush brush)
        {
            brush = null;

            if (string.IsNullOrWhiteSpace(colorText))
                return false;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorText);
                var solidBrush = new SolidColorBrush(color);

                if (solidBrush.CanFreeze)
                    solidBrush.Freeze();

                brush = solidBrush;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async void LoadImageAsync(string icon, int version, CancellationToken cancellationToken)
        {
            try
            {
                var source = await ResolveImageSourceAsync(icon, cancellationToken);

                if (cancellationToken.IsCancellationRequested || version != _loadVersion)
                    return;

                if (source != null)
                    SetImage(source);
                else
                    Content = null;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                if (version != _loadVersion)
                    return;

                Content = null;
            }
            finally
            {
                if (version == _loadVersion)
                {
                    _isLoading = false;

                    var old = _loadCancellation;
                    _loadCancellation = null;

                    if (old != null)
                        old.Dispose();
                }
            }
        }

        private static async Task<ImageSource> ResolveImageSourceAsync(string icon, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(icon))
                return null;

            icon = NormalizeIconText(icon);

            if (icon.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
            {
                var url = icon.Substring("url:".Length).Trim();
                if (!IsHttpUrl(url))
                    return null;

                return await GetOrLoadImageAsync(
                    "url:" + url,
                    delegate { return LoadBitmapFromHttpAsync(url, cancellationToken); },
                    cancellationToken).ConfigureAwait(false);
            }

            if (IsHttpUrl(icon))
            {
                return await GetOrLoadImageAsync(
                    "url:" + icon,
                    delegate { return LoadBitmapFromHttpAsync(icon, cancellationToken); },
                    cancellationToken).ConfigureAwait(false);
            }

            if (icon.StartsWith("icon:", StringComparison.OrdinalIgnoreCase))
            {
                var shellPath = icon.Substring("icon:".Length).Trim();
                if (shellPath.Length == 0)
                    return null;

                return await GetOrLoadImageAsync(
                    "shell:" + NormalizeShellIconCacheKey(shellPath),
                    delegate
                    {
                        return Task.Run<ImageSource>(
                            delegate { return GetShellIcon(shellPath); },
                            cancellationToken);
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            if (IsImagePath(icon))
            {
                if (IsLocalFileImagePath(icon))
                {
                    return await GetOrLoadImageAsync(
                        "file:" + NormalizeCacheKeyPath(icon),
                        delegate
                        {
                            return Task.Run<ImageSource>(
                                delegate { return LoadBitmapFromUri(icon); },
                                cancellationToken);
                        },
                        cancellationToken).ConfigureAwait(false);
                }

                return LoadBitmapFromUri(icon);
            }

            return null;
        }

        private static async Task<ImageSource> GetOrLoadImageAsync(
            string cacheKey,
            Func<Task<ImageSource>> loadFactory,
            CancellationToken cancellationToken)
        {
            ImageSource cached;

            lock (ImageCacheLock)
            {
                if (ImageCache.TryGetValue(cacheKey, out cached))
                    return cached;
            }

            var source = await loadFactory().ConfigureAwait(false);

            if (source == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }

            if (source.CanFreeze && !source.IsFrozen)
                source.Freeze();

            lock (ImageCacheLock)
            {
                ImageSource existingEntry;
                if (ImageCache.TryGetValue(cacheKey, out existingEntry))
                    return existingEntry;

                if (ImageCache.Count >= MaxImageCacheCount)
                    ImageCache.Clear();

                ImageCache[cacheKey] = source;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return source;
        }

        private static string NormalizeShellIconCacheKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalized = NormalizeCacheKeyPath(path);
            return normalized.Length > 0 ? "path:" + normalized : "raw:" + path.Trim();
        }

        private static bool IsHttpUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsImagePath(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                return true;

            string extension;
            try
            {
                extension = Path.GetExtension(text);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(extension))
                return false;

            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalFileImagePath(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (text.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
                return false;

            Uri uri;
            if (Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.IsFile)
                return true;

            try
            {
                return Path.IsPathRooted(text);
            }
            catch
            {
                return false;
            }
        }

        private static async Task<ImageSource> LoadBitmapFromHttpAsync(string url, CancellationToken cancellationToken)
        {
            using (var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                using (var stream = new MemoryStream(bytes))
                {
                    return LoadBitmapFromStream(stream);
                }
            }
        }

        private static ImageSource LoadBitmapFromUri(string uriText)
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.UriSource = new Uri(uriText, UriKind.RelativeOrAbsolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            if (bitmap.CanFreeze)
                bitmap.Freeze();

            return bitmap;
        }

        private static ImageSource LoadBitmapFromStream(Stream stream)
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            if (bitmap.CanFreeze)
                bitmap.Freeze();

            return bitmap;
        }

        private static ImageSource GetShellIcon(string path)
        {
            path = NormalizeInputPath(path);
            if (path.Length == 0)
                return null;

            IntPtr iconHandle = IntPtr.Zero;

            try
            {
                var existsAsDirectory = Directory.Exists(path);

                if (existsAsDirectory)
                {
                    var knownFolderIcon = TryGetKnownFolderIcon(path);
                    if (knownFolderIcon != null)
                        return knownFolderIcon;
                }

                var info = new SHFILEINFO();

                var existsAsFile = File.Exists(path);

                uint attributes = 0;
                uint flags = SHGFI_ICON;

                if (!existsAsFile && !existsAsDirectory)
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = ShouldUseDirectoryIcon(path)
                        ? FILE_ATTRIBUTE_DIRECTORY
                        : FILE_ATTRIBUTE_NORMAL;
                }

                var result = SHGetFileInfo(
                    path,
                    attributes,
                    ref info,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    flags);

                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                    return null;

                iconHandle = info.hIcon;

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));

                if (source.CanFreeze)
                    source.Freeze();

                return source;
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                    DestroyIcon(iconHandle);
            }
        }

        private static ImageSource TryGetKnownFolderIcon(string path)
        {
            Guid knownFolderId;
            if (!TryGetKnownFolderIdByPath(path, out knownFolderId))
                return null;

            IntPtr pidl = IntPtr.Zero;
            IntPtr iconHandle = IntPtr.Zero;

            try
            {
                var id = knownFolderId;

                var hr = SHGetKnownFolderIDList(
                    ref id,
                    0,
                    IntPtr.Zero,
                    out pidl);

                if (hr != 0 || pidl == IntPtr.Zero)
                    return null;

                var info = new SHFILEINFO();

                var result = SHGetFileInfo(
                    pidl,
                    0,
                    ref info,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                    SHGFI_PIDL | SHGFI_ICON);

                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
                    return null;

                iconHandle = info.hIcon;

                var source = Imaging.CreateBitmapSourceFromHIcon(
                    iconHandle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));

                if (source.CanFreeze)
                    source.Freeze();

                return source;
            }
            finally
            {
                if (iconHandle != IntPtr.Zero)
                    DestroyIcon(iconHandle);

                if (pidl != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(pidl);
            }
        }

        private static bool TryGetKnownFolderIdByPath(string path, out Guid knownFolderId)
        {
            knownFolderId = Guid.Empty;

            var normalizedPath = NormalizePathForCompare(path);
            if (normalizedPath.Length == 0)
                return false;

            var map = GetKnownFolderPathMap();
            return map.TryGetValue(normalizedPath, out knownFolderId);
        }

        private static Dictionary<string, Guid> GetKnownFolderPathMap()
        {
            lock (KnownFolderPathLock)
            {
                if (KnownFolderPathMap != null)
                    return KnownFolderPathMap;

                var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < KnownFolderIds.Length; i++)
                {
                    var id = KnownFolderIds[i];

                    string knownPath;
                    if (!TryGetKnownFolderPath(id, out knownPath))
                        continue;

                    var normalizedKnownPath = NormalizePathForCompare(knownPath);
                    if (normalizedKnownPath.Length == 0)
                        continue;

                    if (!map.ContainsKey(normalizedKnownPath))
                        map.Add(normalizedKnownPath, id);
                }

                KnownFolderPathMap = map;
                return KnownFolderPathMap;
            }
        }

        private static bool TryGetKnownFolderPath(Guid knownFolderId, out string path)
        {
            path = null;

            IntPtr pathPtr = IntPtr.Zero;

            try
            {
                var id = knownFolderId;

                var hr = SHGetKnownFolderPath(
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

        private static string NormalizeInputPath(string path)
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

        private static string NormalizePathForCompare(string path)
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

        private static string NormalizeCacheKeyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            }
            catch
            {
                return path.Trim();
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

        private void CancelCurrentLoad()
        {
            _loadVersion++;
            _isLoading = false;

            var old = _loadCancellation;
            _loadCancellation = null;

            if (old == null)
                return;

            try
            {
                old.Cancel();
            }
            finally
            {
                old.Dispose();
            }
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