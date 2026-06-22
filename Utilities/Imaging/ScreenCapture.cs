using System;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace KkjQuicker.Utilities.Imaging
{
    /// <summary>
    /// ScreenCapture（Pro 版）
    /// ------------------------------------------------------------
    /// 工业级截图工具
    ///
    /// 支持：
    /// ✔ 多显示器
    /// ✔ 任意区域截图
    /// ✔ WPF Visual 渲染
    /// ✔ 高 DPI 正确处理
    /// ✔ 无 GDI 泄漏
    ///
    /// 设计原则：
    /// - GDI 使用 PArgb（与 WPF Pbgra32 对齐）
    /// - Visual 使用真实 DPI scale（避免模糊）
    /// </summary>
    public static class ScreenCapture
    {
        #region ===== 屏幕截图 =====

        /// <summary>
        /// 捕获主屏幕
        /// </summary>
        public static Bitmap CapturePrimaryScreen()
        {
            var screen = System.Windows.Forms.Screen.PrimaryScreen
                ?? throw new InvalidOperationException("No primary screen available.");
            return CaptureScreenBounds(screen.Bounds);
        }

        /// <summary>
        /// 捕获指定屏幕
        /// </summary>
        public static Bitmap CaptureScreen(int screenIndex = 0)
        {
            var screens = System.Windows.Forms.Screen.AllScreens;

            if (screenIndex < 0 || screenIndex >= screens.Length)
                throw new ArgumentOutOfRangeException(nameof(screenIndex));

            return CaptureScreenBounds(screens[screenIndex].Bounds);
        }

        /// <summary>
        /// 捕获整个虚拟屏幕（所有显示器）
        /// </summary>
        public static Bitmap CaptureVirtualScreen()
        {
            var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            return CaptureScreenBounds(bounds);
        }

        /// <summary>
        /// 捕获指定屏幕区域（屏幕坐标）
        /// NOTE: Graphics.CopyFromScreen 不支持 CaptureBlt 组合
        /// </summary>
        public static Bitmap CaptureScreenBounds(Rectangle bounds)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0)
                throw new ArgumentException("Invalid capture bounds");

            // 使用 PArgb（与 WPF 完美兼容）
            var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);

            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(
                    bounds.X, bounds.Y,
                    0, 0,
                    bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }

            return bmp;
        }

        #endregion ===== 屏幕截图 =====

        #region ===== WPF Visual 截图 =====

        /// <summary>
        /// 将 WPF Visual 渲染为 BitmapSource（DPI 安全）
        /// </summary>
        public static BitmapSource CaptureVisualToBitmapSource(Visual element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            var rect = VisualTreeHelper.GetDescendantBounds(element);

            if (rect.IsEmpty)
                throw new InvalidOperationException("Visual has no render bounds");

            // ★ 关键：使用真实 DPI（防模糊）
            var dpi = VisualTreeHelper.GetDpi(element);

            int width = (int)Math.Ceiling(rect.Width * dpi.DpiScaleX);
            int height = (int)Math.Ceiling(rect.Height * dpi.DpiScaleY);

            var rtb = new RenderTargetBitmap(
                width,
                height,
                dpi.PixelsPerInchX,
                dpi.PixelsPerInchY,
                PixelFormats.Pbgra32);

            rtb.Render(element);
            rtb.Freeze();

            return rtb;
        }

        #endregion ===== WPF Visual 截图 =====
    }
}