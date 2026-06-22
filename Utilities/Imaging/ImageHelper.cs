using KkjQuicker.Utilities.Win32;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace KkjQuicker.Utilities.Imaging
{
    /// <summary>
    /// 提供常用的图像加载、保存、缩放、裁剪、Bitmap 与 BitmapSource 互转、Base64 转换、剪贴板操作等辅助方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类同时面向 GDI+（<see cref="Bitmap"/>）与 WPF（<see cref="BitmapSource"/>）场景，
    /// 适用于桌面工具、截图处理、预览显示、图像上传等常见需求。
    /// </para>
    /// <para>
    /// 对透明位图优先采用无 GDI 句柄复制路径；
    /// 对不透明位图在受控释放前提下可使用更快的 GDI 路径，避免句柄泄漏。
    /// </para>
    /// <para>
    /// 剪贴板相关方法（<see cref="CopyToClipboard"/> / <see cref="GetFromClipboard"/>）
    /// 必须在 STA 线程上调用，WPF 应用的 UI 线程满足此要求。
    /// </para>
    /// </remarks>
    public static class ImageHelper
    {
        #region 加载

        /// <summary>
        /// 从文件加载位图，并立即脱离底层文件流，避免文件锁定。
        /// </summary>
        /// <param name="path">图像文件路径。</param>
        /// <returns>加载得到的新位图。</returns>
        public static Bitmap LoadBitmap(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var image = Image.FromStream(fs, true, true))
            {
                return new Bitmap(image);
            }
        }

        /// <summary>
        /// 从文件加载 <see cref="BitmapSource"/>，并立即脱离底层文件流。
        /// </summary>
        /// <param name="path">图像文件路径。</param>
        /// <returns>加载后的 <see cref="BitmapSource"/>。</returns>
        public static BitmapSource LoadBitmapSource(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            var image = new BitmapImage();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = fs;
                image.EndInit();
                image.Freeze();
            }

            return image;
        }

        #endregion

        #region Base64 编解码

        /// <summary>
        /// 从 Base64 字符串解码并加载 <see cref="Bitmap"/>。
        /// </summary>
        /// <param name="base64">
        /// Base64 字符串；支持携带 <c>data:image/...;base64,</c> 前缀，会自动剥离。
        /// </param>
        /// <returns>解码得到的新位图。</returns>
        public static Bitmap BitmapFromBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                throw new ArgumentNullException(nameof(base64));

            base64 = StripBase64Header(base64);
            byte[] bytes = Convert.FromBase64String(base64);

            using (var ms = new MemoryStream(bytes))
            using (var image = Image.FromStream(ms, true, true))
            {
                return new Bitmap(image);
            }
        }

        /// <summary>
        /// 从 Base64 字符串解码并加载 <see cref="BitmapSource"/>。
        /// </summary>
        /// <param name="base64">
        /// Base64 字符串；支持携带 <c>data:image/...;base64,</c> 前缀，会自动剥离。
        /// </param>
        /// <returns>解码得到的 <see cref="BitmapSource"/>。</returns>
        public static BitmapSource BitmapSourceFromBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                throw new ArgumentNullException(nameof(base64));

            base64 = StripBase64Header(base64);
            byte[] bytes = Convert.FromBase64String(base64);

            var image = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
            }

            return image;
        }

        /// <summary>
        /// 将 <see cref="Bitmap"/> 编码为 Base64 字符串。
        /// </summary>
        /// <remarks>
        /// JPEG 格式不支持 Alpha 通道；源图包含透明度且输出为 JPEG 时，透明信息会由编码器丢弃。
        /// </remarks>
        /// <param name="bitmap">源位图。</param>
        /// <param name="includeDataHeader">是否包含 data URL 头。</param>
        /// <param name="format">图像格式；为 <see langword="null"/> 时默认使用 PNG。</param>
        /// <param name="jpegQuality">JPEG 质量，范围建议为 0~100；超范围时会自动钳制。</param>
        /// <returns>Base64 字符串；可选附带 data URL 头。</returns>
        public static string ToBase64(this Bitmap bitmap, bool includeDataHeader = false, ImageFormat? format = null, long jpegQuality = 90)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (format == null)
                format = ImageFormat.Png;

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                if (format.Guid == ImageFormat.Jpeg.Guid)
                {
                    var encoder = GetEncoder(ImageFormat.Jpeg);
                    if (encoder != null)
                    {
                        long quality = ClampJpegQuality(jpegQuality);
                        using (var encoderParams = new EncoderParameters(1))
                        {
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                            bitmap.Save(ms, encoder, encoderParams);
                        }
                    }
                    else
                    {
                        bitmap.Save(ms, ImageFormat.Jpeg);
                    }
                }
                else
                {
                    bitmap.Save(ms, format);
                }

                bytes = ms.ToArray();
            }

            string base64 = Convert.ToBase64String(bytes);
            return includeDataHeader ? "data:" + GetMimeType(format) + ";base64," + base64 : base64;
        }

        /// <summary>
        /// 将 <see cref="BitmapSource"/> 编码为 Base64 字符串。
        /// </summary>
        /// <remarks>
        /// JPEG 格式不支持 Alpha 通道；源图包含透明度且输出为 JPEG 时，透明信息会由编码器丢弃。
        /// </remarks>
        /// <param name="source">源图像。</param>
        /// <param name="includeDataHeader">是否包含 data URL 头。</param>
        /// <param name="imageExtension">
        /// 输出格式扩展名，例如 <c>.png</c>、<c>.jpg</c>、<c>.jpeg</c>、<c>.bmp</c>、<c>.gif</c>、<c>.tif</c>。
        /// 为空时默认使用 <c>.png</c>。
        /// </param>
        /// <param name="jpegQuality">JPEG 质量，范围建议为 0~100；超范围时会自动钳制。</param>
        /// <returns>Base64 字符串；可选附带 data URL 头。</returns>
        public static string ToBase64(this BitmapSource source, bool includeDataHeader = false, string imageExtension = ".png", long jpegQuality = 90)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (string.IsNullOrWhiteSpace(imageExtension))
                imageExtension = ".png";

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                BitmapEncoder encoder = CreateEncoderByExtension(imageExtension, jpegQuality);
                encoder.Frames.Add(BitmapFrame.Create(source));
                encoder.Save(ms);
                bytes = ms.ToArray();
            }

            string base64 = Convert.ToBase64String(bytes);
            return includeDataHeader ? "data:" + GetMimeTypeByExtension(imageExtension) + ";base64," + base64 : base64;
        }

        #endregion

        #region 缩放

        /// <summary>
        /// 按指定尺寸缩放位图。
        /// </summary>
        /// <param name="bitmap">源位图。</param>
        /// <param name="width">目标宽度（像素）。</param>
        /// <param name="height">目标高度（像素）。</param>
        /// <returns>缩放后的新位图。</returns>
        public static Bitmap Resize(Bitmap bitmap, int width, int height)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            var result = new Bitmap(width, height, GdiPixelFormat.Format32bppArgb);

            float dpiX = bitmap.HorizontalResolution > 0 ? bitmap.HorizontalResolution : 96f;
            float dpiY = bitmap.VerticalResolution > 0 ? bitmap.VerticalResolution : 96f;
            result.SetResolution(dpiX, dpiY);

            using (var g = Graphics.FromImage(result))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var attributes = new ImageAttributes())
                {
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(
                        bitmap,
                        new Rectangle(0, 0, width, height),
                        0,
                        0,
                        bitmap.Width,
                        bitmap.Height,
                        GraphicsUnit.Pixel,
                        attributes);
                }
            }

            return result;
        }

        /// <summary>
        /// 按比例缩放位图。
        /// </summary>
        /// <param name="bitmap">源位图。</param>
        /// <param name="scale">缩放比例。</param>
        /// <returns>缩放后的新位图。</returns>
        public static Bitmap ResizeByScale(Bitmap bitmap, double scale)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (scale <= 0)
                throw new ArgumentOutOfRangeException(nameof(scale));

            int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            return Resize(bitmap, width, height);
        }

        /// <summary>
        /// 等比缩放位图，使其不超过指定的最大边界尺寸。
        /// </summary>
        /// <param name="bitmap">源位图。</param>
        /// <param name="maxWidth">允许的最大宽度（像素）。</param>
        /// <param name="maxHeight">允许的最大高度（像素）。</param>
        /// <param name="upscale">
        /// 若为 <see langword="true"/>，当原图小于目标边界时同样放大；
        /// 若为 <see langword="false"/>（默认），原图已在边界内时返回原始尺寸副本，不放大。
        /// </param>
        /// <returns>缩放后的新位图。</returns>
        public static Bitmap ResizeToFit(Bitmap bitmap, int maxWidth, int maxHeight, bool upscale = false)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (maxWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxWidth));

            if (maxHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxHeight));

            double scale = Math.Min(maxWidth / (double)bitmap.Width, maxHeight / (double)bitmap.Height);

            if (!upscale && scale >= 1.0)
                return new Bitmap(bitmap);

            int targetWidth = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            int targetHeight = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            return Resize(bitmap, targetWidth, targetHeight);
        }

        #endregion

        #region 裁剪

        /// <summary>
        /// 按像素矩形裁剪位图。
        /// </summary>
        /// <param name="bitmap">源位图。</param>
        /// <param name="cropRect">裁剪区域（像素）。</param>
        /// <returns>裁剪后的新位图。</returns>
        public static Bitmap Crop(Bitmap bitmap, Rectangle cropRect)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            var validRect = Rectangle.Intersect(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                cropRect);

            if (validRect.Width <= 0 || validRect.Height <= 0)
                throw new ArgumentException("裁剪区域无效。", nameof(cropRect));

            GdiPixelFormat format = bitmap.PixelFormat;
            if ((format & GdiPixelFormat.Indexed) != 0 ||
                format == GdiPixelFormat.Undefined ||
                format == GdiPixelFormat.DontCare)
            {
                format = GdiPixelFormat.Format32bppArgb;
            }

            return bitmap.Clone(validRect, format);
        }

        /// <summary>
        /// 按 DIP 矩形裁剪 <see cref="BitmapSource"/>。
        /// </summary>
        /// <param name="source">源图像。</param>
        /// <param name="cropRectDip">裁剪区域（DIP）；会与图像边界取交集。</param>
        /// <returns>裁剪后的新图像。</returns>
        public static BitmapSource Crop(BitmapSource source, Rect cropRectDip)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            double scaleX = (source.DpiX > 0 ? source.DpiX : 96.0) / 96.0;
            double scaleY = (source.DpiY > 0 ? source.DpiY : 96.0) / 96.0;

            int x = (int)Math.Round(cropRectDip.X * scaleX);
            int y = (int)Math.Round(cropRectDip.Y * scaleY);
            int width = (int)Math.Round(cropRectDip.Width * scaleX);
            int height = (int)Math.Round(cropRectDip.Height * scaleY);

            int left = Math.Max(0, x);
            int top = Math.Max(0, y);
            int right = Math.Min(source.PixelWidth, x + width);
            int bottom = Math.Min(source.PixelHeight, y + height);

            int validWidth = right - left;
            int validHeight = bottom - top;

            if (validWidth <= 0 || validHeight <= 0)
                throw new ArgumentException("裁剪区域无效。", nameof(cropRectDip));

            var validRect = new Int32Rect(left, top, validWidth, validHeight);

            var cropped = new CroppedBitmap(source, validRect);
            cropped.Freeze();
            return cropped;
        }

        #endregion

        #region 格式互转

        /// <summary>
        /// 将 <see cref="Bitmap"/> 转换为 <see cref="BitmapSource"/>。
        /// </summary>
        /// <param name="bitmap">源位图。</param>
        /// <returns>转换后的 <see cref="BitmapSource"/>。</returns>
        public static BitmapSource ToBitmapSource(this Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            bool hasAlpha = Image.IsAlphaPixelFormat(bitmap.PixelFormat);
            return hasAlpha ? ToBitmapSourceSafeAlpha(bitmap) : ToBitmapSourceFastOpaque(bitmap);
        }

        /// <summary>
        /// 将 <see cref="BitmapSource"/> 转换为 <see cref="Bitmap"/>。
        /// </summary>
        /// <remarks>
        /// 返回的 <see cref="Bitmap"/> 格式为 <see cref="GdiPixelFormat.Format32bppArgb"/>（直通 alpha），
        /// 与 WPF 内部 Bgra32 布局一致。
        /// </remarks>
        /// <param name="source">源图像。</param>
        /// <returns>转换后的 <see cref="Bitmap"/>。</returns>
        public static Bitmap ToBitmap(this BitmapSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            BitmapSource converted = source.Format == PixelFormats.Bgra32
                ? source
                : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            int width = converted.PixelWidth;
            int height = converted.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            converted.CopyPixels(pixels, stride, 0);

            // WPF Bgra32 为直通 alpha，对应 GDI+ Format32bppArgb，两者内存布局相同。
            // 不可使用 Format32bppPArgb，否则 GDI+ 会将直通数据误当预乘处理，导致颜色错误。
            var bitmap = new Bitmap(width, height, GdiPixelFormat.Format32bppArgb);

            float dpiX = converted.DpiX > 0 ? (float)converted.DpiX : 96f;
            float dpiY = converted.DpiY > 0 ? (float)converted.DpiY : 96f;
            bitmap.SetResolution(dpiX, dpiY);

            var rect = new Rectangle(0, 0, width, height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, GdiPixelFormat.Format32bppArgb);

            try
            {
                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        #endregion

        #region 保存

        /// <summary>
        /// 将 <see cref="Bitmap"/> 保存到文件。
        /// </summary>
        /// <remarks>
        /// JPEG 格式不支持 Alpha 通道；源图包含透明度且保存为 JPEG 时，透明信息会由编码器丢弃。
        /// </remarks>
        /// <param name="bitmap">要保存的位图。</param>
        /// <param name="path">目标文件路径。</param>
        /// <param name="format">保存格式；为 <see langword="null"/> 时按扩展名推断。</param>
        /// <param name="jpegQuality">JPEG 质量，范围建议为 0~100；超范围时会自动钳制。</param>
        public static void SaveToFile(this Bitmap bitmap, string path, ImageFormat? format = null, long jpegQuality = 90)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            EnsureParentDirectory(path);

            if (format == null)
                format = GetImageFormatByFileExtension(path);

            if (format.Guid == ImageFormat.Jpeg.Guid)
            {
                var encoder = GetEncoder(ImageFormat.Jpeg);
                if (encoder == null)
                {
                    bitmap.Save(path, ImageFormat.Jpeg);
                    return;
                }

                long quality = ClampJpegQuality(jpegQuality);
                using (var encoderParams = new EncoderParameters(1))
                {
                    encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                    bitmap.Save(path, encoder, encoderParams);
                }

                return;
            }

            bitmap.Save(path, format);
        }

        /// <summary>
        /// 将 <see cref="BitmapSource"/> 保存到文件。
        /// </summary>
        /// <remarks>
        /// JPEG 格式不支持 Alpha 通道；源图包含透明度且保存为 JPEG 时，透明信息会由编码器丢弃。
        /// </remarks>
        /// <param name="source">要保存的图像。</param>
        /// <param name="path">目标文件路径。</param>
        /// <param name="jpegQuality">JPEG 质量，范围建议为 0~100；超范围时会自动钳制。</param>
        public static void SaveToFile(this BitmapSource source, string path, long jpegQuality = 90)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            EnsureParentDirectory(path);

            BitmapEncoder encoder = CreateEncoderByFileExtension(path, jpegQuality);
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(fs);
            }
        }

        #endregion

        #region 剪贴板

        /// <summary>
        /// 将 <see cref="BitmapSource"/> 写入系统剪贴板。
        /// </summary>
        /// <remarks>
        /// 必须在 STA 线程上调用；WPF 应用的 UI 线程满足此要求。
        /// </remarks>
        /// <param name="source">要写入剪贴板的图像。</param>
        public static void CopyToClipboard(this BitmapSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            System.Windows.Clipboard.SetImage(source);
        }

        /// <summary>
        /// 从系统剪贴板获取图像。
        /// </summary>
        /// <remarks>
        /// <para>必须在 STA 线程上调用；WPF 应用的 UI 线程满足此要求。</para>
        /// <para>
        /// 部分来源（如截图工具、Office）写入剪贴板时不携带 DPI 信息，
        /// WPF 可能将返回图像的 DPI 报告为 96，与实际屏幕 DPI 不符，调用方按需自行处理。
        /// </para>
        /// </remarks>
        /// <returns>剪贴板中的图像；剪贴板不含图像时返回 <see langword="null"/>。</returns>
        public static BitmapSource? GetFromClipboard()
        {
            if (!System.Windows.Clipboard.ContainsImage())
                return null;

            return System.Windows.Clipboard.GetImage();
        }

        #endregion

        #region 私有辅助方法

        private static BitmapSource ToBitmapSourceSafeAlpha(Bitmap bitmap)
        {
            if (bitmap.PixelFormat == GdiPixelFormat.Format32bppArgb ||
                bitmap.PixelFormat == GdiPixelFormat.Format32bppPArgb)
            {
                return CreateBitmapSourceFrom32BitBitmap(bitmap);
            }

            using (var converted = new Bitmap(bitmap.Width, bitmap.Height, GdiPixelFormat.Format32bppPArgb))
            {
                float dpiX = bitmap.HorizontalResolution > 0 ? bitmap.HorizontalResolution : 96f;
                float dpiY = bitmap.VerticalResolution > 0 ? bitmap.VerticalResolution : 96f;
                converted.SetResolution(dpiX, dpiY);

                using (var g = Graphics.FromImage(converted))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
                }

                return CreateBitmapSourceFrom32BitBitmap(converted);
            }
        }

        private static BitmapSource CreateBitmapSourceFrom32BitBitmap(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

            try
            {
                int sourceStride = data.Stride;
                int targetStride = Math.Abs(sourceStride);
                int height = data.Height;
                byte[] pixels = new byte[targetStride * height];

                if (sourceStride > 0)
                {
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        IntPtr sourceRow = IntPtr.Add(data.Scan0, y * sourceStride);
                        Marshal.Copy(sourceRow, pixels, y * targetStride, targetStride);
                    }
                }

                // Format32bppArgb  → Bgra32  (直通 alpha，内存布局相同)
                // Format32bppPArgb → Pbgra32 (预乘 alpha，内存布局相同)
                var wpfFormat = bitmap.PixelFormat == GdiPixelFormat.Format32bppArgb
                    ? PixelFormats.Bgra32
                    : PixelFormats.Pbgra32;

                var source = BitmapSource.Create(
                    data.Width,
                    data.Height,
                    bitmap.HorizontalResolution > 0 ? bitmap.HorizontalResolution : 96.0,
                    bitmap.VerticalResolution > 0 ? bitmap.VerticalResolution : 96.0,
                    wpfFormat,
                    null,
                    pixels,
                    targetStride);

                source.Freeze();
                return source;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static BitmapSource ToBitmapSourceFastOpaque(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();

            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
            }
        }

        private static string StripBase64Header(string base64)
        {
            int commaIndex = base64.IndexOf(',');
            return commaIndex >= 0 ? base64.Substring(commaIndex + 1) : base64;
        }

        private static BitmapEncoder CreateEncoderByFileExtension(string path, long jpegQuality)
        {
            string extension = Path.GetExtension(path);
            return CreateEncoderByExtension(extension, jpegQuality);
        }

        private static BitmapEncoder CreateEncoderByExtension(string extension, long jpegQuality)
        {
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            extension = extension.Trim().ToLowerInvariant();

            if (extension == ".jpg" || extension == ".jpeg")
                return new JpegBitmapEncoder { QualityLevel = (int)ClampJpegQuality(jpegQuality) };

            if (extension == ".bmp")
                return new BmpBitmapEncoder();

            if (extension == ".gif")
                return new GifBitmapEncoder();

            if (extension == ".tif" || extension == ".tiff")
                return new TiffBitmapEncoder();

            return new PngBitmapEncoder();
        }

        private static ImageFormat GetImageFormatByFileExtension(string path)
        {
            string extension = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(extension))
                return ImageFormat.Png;

            extension = extension.Trim().ToLowerInvariant();

            if (extension == ".jpg" || extension == ".jpeg")
                return ImageFormat.Jpeg;

            if (extension == ".bmp")
                return ImageFormat.Bmp;

            if (extension == ".gif")
                return ImageFormat.Gif;

            if (extension == ".tif" || extension == ".tiff")
                return ImageFormat.Tiff;

            return ImageFormat.Png;
        }

        private static string GetMimeType(ImageFormat format)
        {
            if (format == null)
                return "image/png";

            if (format.Guid == ImageFormat.Jpeg.Guid)
                return "image/jpeg";

            if (format.Guid == ImageFormat.Bmp.Guid)
                return "image/bmp";

            if (format.Guid == ImageFormat.Gif.Guid)
                return "image/gif";

            if (format.Guid == ImageFormat.Tiff.Guid)
                return "image/tiff";

            return "image/png";
        }

        private static string GetMimeTypeByExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return "image/png";

            extension = extension.Trim().ToLowerInvariant();

            if (extension == ".jpg" || extension == ".jpeg")
                return "image/jpeg";

            if (extension == ".bmp")
                return "image/bmp";

            if (extension == ".gif")
                return "image/gif";

            if (extension == ".tif" || extension == ".tiff")
                return "image/tiff";

            return "image/png";
        }

        private static long ClampJpegQuality(long jpegQuality)
        {
            if (jpegQuality < 0)
                return 0;

            if (jpegQuality > 100)
                return 100;

            return jpegQuality;
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            if (format == null)
                return null;

            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            for (int i = 0; i < codecs.Length; i++)
            {
                if (codecs[i].FormatID == format.Guid)
                    return codecs[i];
            }

            return null;
        }

        private static void EnsureParentDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        #endregion
    }
}
