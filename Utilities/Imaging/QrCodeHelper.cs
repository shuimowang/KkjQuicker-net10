using QRCoder;
using QRCoder.Exceptions;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;

namespace KkjQuicker.Utilities.Imaging
{
    /// <summary>
    /// 二维码编解码帮助类。
    /// </summary>
    public static class QrCodeHelper
    {
        private const int DefaultPixelsPerModule = 8;
        private const QRCodeGenerator.ECCLevel DefaultEccLevel = QRCodeGenerator.ECCLevel.Q;
        private const bool DefaultDrawQuietZones = true;

        /// <summary>
        /// 将文本编码为二维码 BitmapSource，适合 WPF Image.Source 直接绑定。
        /// </summary>
        /// <exception cref="ArgumentException">text 为空；或内容长度超出当前纠错级别下二维码可承载的最大容量。</exception>
        public static BitmapSource Encode(
            string text,
            int pixelsPerModule = DefaultPixelsPerModule,
            QRCodeGenerator.ECCLevel eccLevel = DefaultEccLevel,
            bool drawQuietZones = DefaultDrawQuietZones)
        {
            ValidateEncodeArguments(text, pixelsPerModule, eccLevel);

            try
            {
                using (QRCodeGenerator generator = new QRCodeGenerator())
                using (QRCodeData qrCodeData = generator.CreateQrCode(
                    text,
                    eccLevel,
                    forceUtf8: true,
                    utf8BOM: false,
                    eciMode: QRCodeGenerator.EciMode.Utf8))
                using (QRCode qrCode = new QRCode(qrCodeData))
                using (Bitmap bitmap = qrCode.GetGraphic(
                    pixelsPerModule,
                    Color.Black,
                    Color.White,
                    drawQuietZones))
                {
                    return bitmap.ToBitmapSource();
                }
            }
            catch (DataTooLongException ex)
            {
                throw new ArgumentException("二维码内容长度超出当前纠错级别下的最大容量。", nameof(text), ex);
            }
        }

        /// <summary>
        /// 识别 BitmapSource 中的二维码。
        /// 识别失败返回空字符串。
        /// </summary>
        public static string Decode(BitmapSource bitmapSource)
        {
            if (bitmapSource == null)
                return string.Empty;

            using (Bitmap bitmap = bitmapSource.ToBitmap())
            {
                return DecodeCore(bitmap);
            }
        }

        /// <summary>
        /// 识别图片文件中的二维码。
        /// 文件不存在、图片无效或识别失败时返回空字符串。
        /// </summary>
        public static string DecodeFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                using (Bitmap bitmap = new Bitmap(filePath))
                {
                    return DecodeCore(bitmap);
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (IOException)
            {
                return string.Empty;
            }
            catch (UnauthorizedAccessException)
            {
                return string.Empty;
            }
            catch (OutOfMemoryException)
            {
                return string.Empty;
            }
        }

        private static void ValidateEncodeArguments(
            string text,
            int pixelsPerModule,
            QRCodeGenerator.ECCLevel eccLevel)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("二维码内容不能为空。", nameof(text));

            if (pixelsPerModule <= 0)
                throw new ArgumentOutOfRangeException(nameof(pixelsPerModule), "每模块像素数必须大于 0。");

            if (!Enum.IsDefined(typeof(QRCodeGenerator.ECCLevel), eccLevel))
                throw new ArgumentOutOfRangeException(nameof(eccLevel), "纠错级别无效。");
        }

        private static string DecodeCore(Bitmap bitmap)
        {
            if (bitmap == null)
                return string.Empty;

            var reader = new BarcodeReader<Bitmap>(CreateLuminanceSource)
            {
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[]
                    {
                        BarcodeFormat.QR_CODE
                    }
                }
            };

            using (Bitmap readable = CreateReadableBitmap(bitmap))
            {
                Result result = reader.Decode(readable);
                return result == null ? string.Empty : result.Text;
            }
        }

        private static LuminanceSource CreateLuminanceSource(Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                int sourceStride = bitmapData.Stride;
                int targetStride = bitmapData.Width * 3;
                var bytes = new byte[targetStride * bitmapData.Height];

                for (int y = 0; y < bitmapData.Height; y++)
                {
                    IntPtr sourceRow = IntPtr.Add(bitmapData.Scan0, y * sourceStride);
                    System.Runtime.InteropServices.Marshal.Copy(sourceRow, bytes, y * targetStride, targetStride);
                }

                return new RGBLuminanceSource(
                    bytes,
                    bitmapData.Width,
                    bitmapData.Height,
                    RGBLuminanceSource.BitmapFormat.BGR24);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }

        private static Bitmap CreateReadableBitmap(Bitmap source)
        {
            var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);

            try
            {
                float dpiX = source.HorizontalResolution > 0 ? source.HorizontalResolution : 96f;
                float dpiY = source.VerticalResolution > 0 ? source.VerticalResolution : 96f;
                bitmap.SetResolution(dpiX, dpiY);

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.White);
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }
}
