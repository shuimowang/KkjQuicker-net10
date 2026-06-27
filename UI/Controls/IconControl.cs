using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using FontAwesome5;
using FontAwesome5.WPF;

using KkjQuicker.Utilities.Win32;

namespace KkjQuicker.UI.Controls
{
    /// <summary>
    /// 显示 FontAwesome5 矢量图标、网络图片、本地图片或 Shell 路径图标的轻量图标控件。
    /// </summary>
    public class IconControl : ContentControl
    {
        private const double DefaultIconSize = 16.0;
        private const int MaxImageCacheCount = 256;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        private static readonly object ImageCacheLock = new();

        private static readonly Dictionary<string, ImageCacheEntry> ImageCache =
            new(StringComparer.OrdinalIgnoreCase);
        private static long _imageCacheClock;

        private CancellationTokenSource? _loadCancellation;
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
        public object? Icon
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
        public Brush? DefaultIconBrush
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
        public static void SetDefaultIconColor(DependencyObject obj, string? value)
        {
            obj.SetValue(DefaultIconColorProperty, value ?? string.Empty);
        }

        /// <summary>
        /// 获取默认图标画刷。
        /// </summary>
        public static Brush? GetDefaultIconBrush(DependencyObject obj)
        {
            return (Brush?)obj.GetValue(DefaultIconBrushProperty);
        }

        /// <summary>
        /// 设置默认图标画刷。
        /// </summary>
        public static void SetDefaultIconBrush(DependencyObject obj, Brush? value)
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
            Brush? explicitBrush;
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
            Brush? explicitBrush;
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

        private static bool HasIconValue(object? icon)
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

        private static string NormalizeIconText(string? text)
        {
            if (text == null)
                return string.Empty;

            text = text.Trim();

            if (text.Length >= 2 && text[0] == '[' && text[text.Length - 1] == ']')
                text = text.Substring(1, text.Length - 2).Trim();

            return text;
        }

        private static bool TryGetFontAwesomeIcon(object? icon, out EFontAwesomeIcon fontIcon, out Brush? explicitBrush)
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

                Brush? brush;
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

        private void SetFontAwesomeIcon(EFontAwesomeIcon fontIcon, Brush? explicitBrush)
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
                Brush? parsedBrush;
                if (TryCreateBrush(colorText, out parsedBrush))
                    return parsedBrush;
            }

            return Foreground ?? Brushes.Black;
        }

        private static bool TryCreateBrush(string? colorText, out Brush? brush)
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

        private static async Task<ImageSource?> ResolveImageSourceAsync(string icon, CancellationToken cancellationToken)
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
                    () => LoadBitmapFromHttpAsync(url, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            if (IsHttpUrl(icon))
            {
                return await GetOrLoadImageAsync(
                    "url:" + icon,
                    () => LoadBitmapFromHttpAsync(icon, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            if (icon.StartsWith("icon:", StringComparison.OrdinalIgnoreCase))
            {
                var shellPath = icon.Substring("icon:".Length).Trim();
                if (shellPath.Length == 0)
                    return null;

                return await GetOrLoadImageAsync(
                    "shell:" + NormalizeShellIconCacheKey(shellPath),
                    () => Task.Run<ImageSource?>(() => ShellIconHelper.GetIcon(shellPath), cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }

            if (IsImagePath(icon))
            {
                if (IsLocalFileImagePath(icon))
                {
                    return await GetOrLoadImageAsync(
                        "file:" + NormalizeCacheKeyPath(icon),
                        () => Task.Run<ImageSource>(() => LoadBitmapFromUri(icon), cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                }

                return LoadBitmapFromUri(icon);
            }

            return null;
        }

        private static async Task<ImageSource?> GetOrLoadImageAsync(
            string cacheKey,
            Func<Task<ImageSource?>> loadFactory,
            CancellationToken cancellationToken)
        {
            lock (ImageCacheLock)
            {
                ImageCacheEntry? cached;
                if (ImageCache.TryGetValue(cacheKey, out cached))
                {
                    cached.LastAccess = NextImageCacheAccess();
                    return cached.Source;
                }
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
                ImageCacheEntry? existingEntry;
                if (ImageCache.TryGetValue(cacheKey, out existingEntry))
                {
                    existingEntry.LastAccess = NextImageCacheAccess();
                    return existingEntry.Source;
                }

                if (ImageCache.Count >= MaxImageCacheCount)
                    RemoveLeastRecentlyUsedImageCacheEntry();

                ImageCache[cacheKey] = new ImageCacheEntry(source, NextImageCacheAccess());
            }

            cancellationToken.ThrowIfCancellationRequested();

            return source;
        }

        private static long NextImageCacheAccess()
        {
            unchecked
            {
                return ++_imageCacheClock;
            }
        }

        private static void RemoveLeastRecentlyUsedImageCacheEntry()
        {
            if (ImageCache.Count == 0)
                return;

            string? oldestKey = null;
            long oldestAccess = long.MaxValue;

            foreach (var pair in ImageCache)
            {
                if (pair.Value.LastAccess < oldestAccess)
                {
                    oldestAccess = pair.Value.LastAccess;
                    oldestKey = pair.Key;
                }
            }

            if (oldestKey != null)
                ImageCache.Remove(oldestKey);
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

            Uri? uri;
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

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
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

        private sealed class ImageCacheEntry
        {
            public ImageCacheEntry(ImageSource source, long lastAccess)
            {
                Source = source;
                LastAccess = lastAccess;
            }

            public ImageSource Source { get; }

            public long LastAccess { get; set; }
        }

    }
}
