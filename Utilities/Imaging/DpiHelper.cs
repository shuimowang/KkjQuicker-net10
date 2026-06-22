using KkjQuicker.Utilities.Win32;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;

namespace KkjQuicker.Utilities.Imaging
{
    /// <summary>
    /// 提供与 DPI、DIP、设备像素及屏幕坐标转换相关的辅助方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类面向 WPF 场景，优先使用当前 <see cref="Visual"/> 所在呈现目标的 DPI；
    /// 当 <see cref="Visual"/> 尚未连接到可用的 <see cref="PresentationSource"/> 时，
    /// 会尝试通过 HWND 获取 DPI；若仍无法获取，则退回到进程启动时的系统 DPI 快照。
    /// </para>
    /// <para>
    /// 设计目标：
    /// </para>
    /// <list type="bullet">
    /// <item><description>无状态。</description></item>
    /// <item><description>线程安全。</description></item>
    /// <item><description><see cref="Visual"/> 未加载或未连接时自动回退。</description></item>
    /// </list>
    /// <para>
    /// 适用环境：
    /// </para>
    /// <list type="bullet">
    /// <item><description>.NET Framework 4.7.2 及以上。</description></item>
    /// <item><description>Windows 10 及以上。</description></item>
    /// </list>
    /// <para>
    /// 注意：
    /// 常规 DIP/像素换算方法默认要求传入的是"规范矩形"（宽高非负）。
    /// 对于屏幕坐标矩形转换，本类采用"两角点分别变换后重建矩形"的策略，
    /// 以更好地适配实际屏幕坐标换算语义。
    /// </para>
    /// </remarks>
    public static class DpiHelper
    {
        #region ===== System DPI =====

        /// <summary>
        /// 获取系统 DPI（进程启动时的快照值，仅作为兜底值使用）。
        /// </summary>
        /// <remarks>
        /// 当无法从当前 <see cref="Visual"/> 或 HWND 获取更准确的 DPI 时，
        /// 将使用此值进行近似换算。
        /// </remarks>
        public static DpiScale SystemDpi
        {
            get { return _systemDpi; }
        }

        private static readonly DpiScale _systemDpi = CreateSystemDpi();

        private static DpiScale CreateSystemDpi()
        {
            IntPtr hdc = IntPtr.Zero;
            try
            {
                hdc = NativeMethods.GetDC(IntPtr.Zero);

                if (hdc == IntPtr.Zero)
                    return new DpiScale(1, 1);

                int x = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSX);
                int y = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSY);

                if (x <= 0) x = 96;
                if (y <= 0) y = 96;

                return new DpiScale(x / 96.0, y / 96.0);
            }
            catch
            {
                // 极端宿主环境下 Win32 调用可能失败；静态初始化器不允许向外抛出，
                // 此处回退到 1x 缩放，保证 DpiHelper 类型始终可用。
                return new DpiScale(1, 1);
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            }
        }

        #endregion ===== System DPI =====

        #region ===== Visual DPI =====

        /// <summary>
        /// 安全获取指定 <see cref="Visual"/> 的 DPI。
        /// </summary>
        /// <param name="visual">目标视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>
        /// 当前视觉对象所在呈现目标的 DPI；
        /// 若 <paramref name="visual"/> 为 <see langword="null"/>、未连接到可用的呈现源，
        /// 或无法通过 HWND 获取 DPI，则返回 <see cref="SystemDpi"/>。
        /// </returns>
        /// <remarks>
        /// 获取顺序如下：
        /// <list type="number">
        /// <item><description>优先使用 <see cref="VisualTreeHelper.GetDpi(Visual)"/>。</description></item>
        /// <item><description>若 <see cref="Visual"/> 尚未连接，则尝试从 HWND 获取。</description></item>
        /// <item><description>最后退回到 <see cref="SystemDpi"/>。</description></item>
        /// </list>
        /// </remarks>
        public static DpiScale GetDpi(Visual visual)
        {
            if (visual == null)
                return SystemDpi;

            PresentationSource source = PresentationSource.FromVisual(visual);
            if (source != null && source.CompositionTarget != null)
                return VisualTreeHelper.GetDpi(visual);

            IntPtr hwnd = TryGetHwnd(visual);
            if (hwnd != IntPtr.Zero)
                return GetDpiFromHwnd(hwnd);

            return SystemDpi;
        }

        /// <summary>
        /// 安全获取指定 <see cref="Window"/> 的 DPI。
        /// </summary>
        /// <param name="window">目标窗口。可为 <see langword="null"/>。</param>
        /// <returns>
        /// 当前窗口所在呈现目标的 DPI；
        /// 若 <paramref name="window"/> 为 <see langword="null"/> 或尚未连接，
        /// 则返回 <see cref="SystemDpi"/>。
        /// </returns>
        public static DpiScale GetDpi(Window window)
        {
            return GetDpi((Visual)window);
        }

        #endregion ===== Visual DPI =====

        #region ===== HWND DPI =====

        /// <summary>
        /// 从指定 HWND 获取 DPI。
        /// </summary>
        /// <param name="hwnd">目标窗口句柄。</param>
        /// <returns>
        /// 成功时返回与窗口关联的 DPI；
        /// 失败、句柄无效或当前环境不支持时返回 <see cref="SystemDpi"/>。
        /// </returns>
        /// <remarks>
        /// 本方法用于补偿 <see cref="Visual"/> 尚未连接到 <see cref="PresentationSource"/> 的场景。
        /// 由于运行环境、系统版本或宿主方式不同，底层调用可能失败；
        /// 为保证上层逻辑稳定，本方法会吞掉相关底层异常并回退到 <see cref="SystemDpi"/>。
        /// </remarks>
        public static DpiScale GetDpiFromHwnd(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return SystemDpi;

            try
            {
                uint dpi = NativeMethods.GetDpiForWindow(hwnd);

                if (dpi > 0)
                    return new DpiScale(dpi / 96.0, dpi / 96.0);
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (DllNotFoundException)
            {
            }
            catch (System.Runtime.InteropServices.SEHException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch
            {
                // 保留最终兜底，避免极端宿主环境或异常 Win32 行为向外抛出。
            }

            return SystemDpi;
        }

        #endregion ===== HWND DPI =====

        #region ===== DIP -> PX =====

        /// <summary>
        /// 将 X 方向的 DIP 值转换为设备像素值。
        /// </summary>
        /// <param name="dip">DIP 值。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的设备像素值。</returns>
        public static double DipToPxX(double dip, Visual visual)
        {
            return dip * GetDpi(visual).DpiScaleX;
        }

        /// <summary>
        /// 将 Y 方向的 DIP 值转换为设备像素值。
        /// </summary>
        /// <param name="dip">DIP 值。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的设备像素值。</returns>
        public static double DipToPxY(double dip, Visual visual)
        {
            return dip * GetDpi(visual).DpiScaleY;
        }

        /// <summary>
        /// 将点坐标从 DIP 转换为设备像素。
        /// </summary>
        /// <param name="dip">DIP 坐标点。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的设备像素坐标点。</returns>
        public static Point DipToPx(Point dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Point(
                dip.X * dpi.DpiScaleX,
                dip.Y * dpi.DpiScaleY);
        }

        /// <summary>
        /// 将尺寸从 DIP 转换为设备像素。
        /// </summary>
        /// <param name="dip">DIP 尺寸。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的设备像素尺寸。</returns>
        public static Size DipToPx(Size dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Size(
                dip.Width * dpi.DpiScaleX,
                dip.Height * dpi.DpiScaleY);
        }

        /// <summary>
        /// 将矩形从 DIP 转换为设备像素。
        /// </summary>
        /// <param name="dip">DIP 矩形。应为规范矩形（宽高非负）。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的设备像素矩形。</returns>
        public static Rect DipToPx(Rect dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Rect(
                dip.X * dpi.DpiScaleX,
                dip.Y * dpi.DpiScaleY,
                dip.Width * dpi.DpiScaleX,
                dip.Height * dpi.DpiScaleY);
        }

        /// <summary>
        /// 将 DIP 矩形转换为像素整型矩形。
        /// </summary>
        /// <param name="dip">DIP 矩形。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>
        /// 转换后的 <see cref="Int32Rect"/>。
        /// 结果采用"左上取 Floor、右下取 Ceiling"后重建的方式,
        /// 以尽量保证结果完整覆盖原始区域；若宽度或高度小于 0,则会被钳制为 0。
        /// 当传入 <see cref="Rect.Empty"/> 时直接返回 <see cref="Int32Rect.Empty"/>。
        /// </returns>
        /// <remarks>
        /// 适用于截图裁剪、Win32 定位、位图区域计算等需要整数像素边界的场景。
        /// 相比直接分别对 X、Y、Width、Height 四舍五入,本方法更适合区域边界换算。
        /// </remarks>
        public static Int32Rect DipToPxInt32Rect(Rect dip, Visual visual)
        {
            if (dip.IsEmpty)
                return Int32Rect.Empty;

            DpiScale dpi = GetDpi(visual);

            int left = (int)Math.Floor(dip.Left * dpi.DpiScaleX);
            int top = (int)Math.Floor(dip.Top * dpi.DpiScaleY);
            int right = (int)Math.Ceiling(dip.Right * dpi.DpiScaleX);
            int bottom = (int)Math.Ceiling(dip.Bottom * dpi.DpiScaleY);

            int width = right - left;
            int height = bottom - top;

            if (width < 0) width = 0;
            if (height < 0) height = 0;

            return new Int32Rect(left, top, width, height);
        }

        #endregion ===== DIP -> PX =====

        #region ===== PX -> DIP =====

        /// <summary>
        /// 将 X 方向的设备像素值转换为 DIP。
        /// </summary>
        /// <param name="px">设备像素值。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的 DIP 值。</returns>
        public static double PxToDipX(double px, Visual visual)
        {
            return px / GetDpi(visual).DpiScaleX;
        }

        /// <summary>
        /// 将 Y 方向的设备像素值转换为 DIP。
        /// </summary>
        /// <param name="px">设备像素值。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的 DIP 值。</returns>
        public static double PxToDipY(double px, Visual visual)
        {
            return px / GetDpi(visual).DpiScaleY;
        }

        /// <summary>
        /// 将点坐标从设备像素转换为 DIP。
        /// </summary>
        /// <param name="px">设备像素坐标点。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的 DIP 坐标点。</returns>
        public static Point PxToDip(Point px, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Point(
                px.X / dpi.DpiScaleX,
                px.Y / dpi.DpiScaleY);
        }

        /// <summary>
        /// 将尺寸从设备像素转换为 DIP。
        /// </summary>
        /// <param name="px">设备像素尺寸。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的 DIP 尺寸。</returns>
        public static Size PxToDip(Size px, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Size(
                px.Width / dpi.DpiScaleX,
                px.Height / dpi.DpiScaleY);
        }

        /// <summary>
        /// 将矩形从设备像素转换为 DIP。
        /// </summary>
        /// <param name="px">设备像素矩形。应为规范矩形（宽高非负）。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>转换后的 DIP 矩形。</returns>
        public static Rect PxToDip(Rect px, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Rect(
                px.X / dpi.DpiScaleX,
                px.Y / dpi.DpiScaleY,
                px.Width / dpi.DpiScaleX,
                px.Height / dpi.DpiScaleY);
        }

        #endregion ===== PX -> DIP =====

        #region ===== Screen Transform =====

        /// <summary>
        /// 将屏幕设备像素坐标转换为 DIP 坐标。
        /// </summary>
        /// <param name="px">屏幕设备像素坐标。</param>
        /// <param name="relativeTo">
        /// 用于提供屏幕设备变换矩阵的视觉对象。
        /// 当为 <see langword="null"/> 或未连接到可用的 <see cref="PresentationSource"/> 时，
        /// 会退回到 <see cref="SystemDpi"/> 进行近似换算。
        /// </param>
        /// <returns>转换后的 DIP 坐标。</returns>
        public static Point PxToDipOnScreen(Point px, Visual? relativeTo)
        {
            if (relativeTo == null)
            {
                DpiScale dpi = SystemDpi;
                return new Point(px.X / dpi.DpiScaleX, px.Y / dpi.DpiScaleY);
            }

            PresentationSource source = PresentationSource.FromVisual(relativeTo);

            if (source == null || source.CompositionTarget == null)
            {
                DpiScale dpi = SystemDpi;
                return new Point(px.X / dpi.DpiScaleX, px.Y / dpi.DpiScaleY);
            }

            return source.CompositionTarget.TransformFromDevice.Transform(px);
        }

        /// <summary>
        /// 将 DIP 坐标转换为屏幕设备像素坐标。
        /// </summary>
        /// <param name="dip">DIP 坐标。</param>
        /// <param name="relativeTo">
        /// 用于提供屏幕设备变换矩阵的视觉对象。
        /// 当为 <see langword="null"/> 或未连接到可用的 <see cref="PresentationSource"/> 时，
        /// 会退回到 <see cref="SystemDpi"/> 进行近似换算。
        /// </param>
        /// <returns>转换后的屏幕设备像素坐标。</returns>
        public static Point DipToPxOnScreen(Point dip, Visual relativeTo)
        {
            if (relativeTo == null)
            {
                DpiScale dpi = SystemDpi;
                return new Point(dip.X * dpi.DpiScaleX, dip.Y * dpi.DpiScaleY);
            }

            PresentationSource source = PresentationSource.FromVisual(relativeTo);

            if (source == null || source.CompositionTarget == null)
            {
                DpiScale dpi = SystemDpi;
                return new Point(dip.X * dpi.DpiScaleX, dip.Y * dpi.DpiScaleY);
            }

            return source.CompositionTarget.TransformToDevice.Transform(dip);
        }

        /// <summary>
        /// 将屏幕设备像素矩形转换为 DIP 矩形。
        /// </summary>
        /// <param name="px">屏幕设备像素矩形。</param>
        /// <param name="relativeTo">
        /// 用于提供屏幕设备变换矩阵的视觉对象。
        /// </param>
        /// <returns>转换后的 DIP 矩形。</returns>
        /// <remarks>
        /// 本方法按矩形左上角与右下角两个点分别进行转换，再重建结果矩形。
        /// 该策略更符合屏幕坐标空间与设备变换的实际语义。
        /// </remarks>
        public static Rect PxToDipOnScreen(Rect px, Visual? relativeTo)
        {
            Point topLeft = PxToDipOnScreen(new Point(px.Left, px.Top), relativeTo);
            Point bottomRight = PxToDipOnScreen(new Point(px.Right, px.Bottom), relativeTo);
            return new Rect(topLeft, bottomRight);
        }

        /// <summary>
        /// 将 DIP 矩形转换为屏幕设备像素矩形。
        /// </summary>
        /// <param name="dip">DIP 矩形。</param>
        /// <param name="relativeTo">
        /// 用于提供屏幕设备变换矩阵的视觉对象。
        /// </param>
        /// <returns>转换后的屏幕设备像素矩形。</returns>
        /// <remarks>
        /// 本方法按矩形左上角与右下角两个点分别进行转换，再重建结果矩形。
        /// 该策略更符合屏幕坐标空间与设备变换的实际语义。
        /// </remarks>
        public static Rect DipToPxOnScreen(Rect dip, Visual relativeTo)
        {
            Point topLeft = DipToPxOnScreen(new Point(dip.Left, dip.Top), relativeTo);
            Point bottomRight = DipToPxOnScreen(new Point(dip.Right, dip.Bottom), relativeTo);
            return new Rect(topLeft, bottomRight);
        }

        #endregion ===== Screen Transform =====

        #region ===== Pixel Align =====

        /// <summary>
        /// 将 X 方向的 DIP 值对齐到最近的设备像素边界，并返回对齐后的 DIP 值。
        /// </summary>
        /// <param name="dip">原始 DIP 值。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>对齐后的 DIP 值。</returns>
        public static double RoundToDevicePixelsX(double dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);
            double px = dip * dpi.DpiScaleX;
            return Math.Round(px, MidpointRounding.AwayFromZero) / dpi.DpiScaleX;
        }

        /// <summary>
        /// 将 Y 方向的 DIP 值对齐到最近的设备像素边界，并返回对齐后的 DIP 值。
        /// </summary>
        /// <param name="dip">原始 DIP 值。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>对齐后的 DIP 值。</returns>
        public static double RoundToDevicePixelsY(double dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);
            double px = dip * dpi.DpiScaleY;
            return Math.Round(px, MidpointRounding.AwayFromZero) / dpi.DpiScaleY;
        }

        /// <summary>
        /// 将点坐标对齐到最近的设备像素边界，并返回对齐后的 DIP 坐标点。
        /// </summary>
        /// <param name="dip">原始 DIP 坐标点。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>对齐后的 DIP 坐标点。</returns>
        public static Point RoundToDevicePixels(Point dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Point(
                Math.Round(dip.X * dpi.DpiScaleX, MidpointRounding.AwayFromZero) / dpi.DpiScaleX,
                Math.Round(dip.Y * dpi.DpiScaleY, MidpointRounding.AwayFromZero) / dpi.DpiScaleY);
        }

        /// <summary>
        /// 将尺寸对齐到最近的设备像素边界，并返回对齐后的 DIP 尺寸。
        /// </summary>
        /// <param name="dip">原始 DIP 尺寸。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>对齐后的 DIP 尺寸。</returns>
        public static Size RoundToDevicePixels(Size dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return new Size(
                Math.Round(dip.Width * dpi.DpiScaleX, MidpointRounding.AwayFromZero) / dpi.DpiScaleX,
                Math.Round(dip.Height * dpi.DpiScaleY, MidpointRounding.AwayFromZero) / dpi.DpiScaleY);
        }

        /// <summary>
        /// 将 DIP 矩形向设备像素边界扩展对齐，并返回对齐后的 DIP 矩形。
        /// </summary>
        /// <param name="dip">原始 DIP 矩形。应为规范矩形（宽高非负）。</param>
        /// <param name="visual">用于确定 DPI 的视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>对齐后的 DIP 矩形。</returns>
        /// <remarks>
        /// 与简单四舍五入不同，本方法采用"左上取 Floor、右下取 Ceiling"的策略，
        /// 保证结果矩形完整包住原始区域，适合绘制边框、裁剪区域和命中框对齐等场景。
        /// </remarks>
        public static Rect RoundToDevicePixels(Rect dip, Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            double x = Math.Floor(dip.X * dpi.DpiScaleX) / dpi.DpiScaleX;
            double y = Math.Floor(dip.Y * dpi.DpiScaleY) / dpi.DpiScaleY;
            double r = Math.Ceiling(dip.Right * dpi.DpiScaleX) / dpi.DpiScaleX;
            double b = Math.Ceiling(dip.Bottom * dpi.DpiScaleY) / dpi.DpiScaleY;

            return new Rect(x, y, r - x, b - y);
        }

        #endregion ===== Pixel Align =====

        #region ===== Utils =====

        /// <summary>
        /// 判断指定视觉对象当前是否处于高于 100% 缩放的 DPI 环境。
        /// </summary>
        /// <param name="visual">目标视觉对象。可为 <see langword="null"/>。</param>
        /// <returns>
        /// 当 X 或 Y 任一方向的 DPI 缩放系数大于 1 时返回 <see langword="true"/>；
        /// 否则返回 <see langword="false"/>。
        /// </returns>
        public static bool IsHighDpi(Visual visual)
        {
            DpiScale dpi = GetDpi(visual);

            return dpi.DpiScaleX > 1 ||
                   dpi.DpiScaleY > 1;
        }

        #endregion ===== Utils =====

        #region ===== Screen Area =====

        /// <summary>
        /// 获取指定视觉对象所在屏幕的工作区，并返回对应的 DIP 矩形。
        /// </summary>
        /// <param name="relativeTo">
        /// 用于确定目标屏幕及 DIP 换算参考的视觉对象。
        /// 当为 <see langword="null"/> 时，将改为使用鼠标当前所在屏幕，
        /// 并基于 <see cref="SystemDpi"/> 进行 DIP 换算。
        /// </param>
        /// <returns>
        /// 屏幕工作区对应的 DIP 矩形。
        /// 当 <paramref name="relativeTo"/> 不为 <see langword="null"/> 且能获取到其宿主窗口时，
        /// 返回该视觉对象所在屏幕的工作区；
        /// 否则返回鼠标当前所在屏幕的工作区。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 当 <paramref name="relativeTo"/> 已连接到有效的 <see cref="PresentationSource"/> 时，
        /// 本方法会通过 <see cref="PxToDipOnScreen(Rect, Visual)"/> 使用该视觉对象的设备变换进行换算，
        /// 以获得更符合当前 WPF 呈现目标的 DIP 坐标。
        /// </para>
        /// <para>
        /// 当 <paramref name="relativeTo"/> 为 <see langword="null"/>，或无法可靠确定其宿主窗口所在屏幕时，
        /// 本方法会退回到鼠标所在屏幕，并使用 <see cref="SystemDpi"/> 进行近似换算。
        /// </para>
        /// </remarks>
        public static Rect GetScreenWorkAreaDip(Visual? relativeTo = null)
        {
            return GetScreenRectDip(relativeTo, workArea: true);
        }

        /// <summary>
        /// 获取指定视觉对象所在屏幕的完整边界，并返回对应的 DIP 矩形。
        /// </summary>
        /// <param name="relativeTo">
        /// 用于确定目标屏幕及 DIP 换算参考的视觉对象。
        /// 当为 <see langword="null"/> 时，将改为使用鼠标当前所在屏幕，
        /// 并基于 <see cref="SystemDpi"/> 进行 DIP 换算。
        /// </param>
        /// <returns>
        /// 屏幕完整边界对应的 DIP 矩形。
        /// 当 <paramref name="relativeTo"/> 不为 <see langword="null"/> 且能获取到其宿主窗口时，
        /// 返回该视觉对象所在屏幕的完整边界；
        /// 否则返回鼠标当前所在屏幕的完整边界。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 当 <paramref name="relativeTo"/> 已连接到有效的 <see cref="PresentationSource"/> 时，
        /// 本方法会通过 <see cref="PxToDipOnScreen(Rect, Visual)"/> 使用该视觉对象的设备变换进行换算，
        /// 以获得更符合当前 WPF 呈现目标的 DIP 坐标。
        /// </para>
        /// <para>
        /// 当 <paramref name="relativeTo"/> 为 <see langword="null"/>，或无法可靠确定其宿主窗口所在屏幕时，
        /// 本方法会退回到鼠标所在屏幕，并使用 <see cref="SystemDpi"/> 进行近似换算。
        /// </para>
        /// </remarks>
        public static Rect GetScreenBoundsDip(Visual? relativeTo = null)
        {
            return GetScreenRectDip(relativeTo, workArea: false);
        }

        /// <summary>
        /// 获取指定视觉对象所在屏幕的工作区，并返回对应的物理像素矩形。
        /// </summary>
        /// <param name="relativeTo">
        /// 用于确定目标屏幕的视觉对象。
        /// 当为 <see langword="null"/> 时，将改为使用鼠标当前所在屏幕。
        /// </param>
        /// <returns>
        /// 屏幕工作区对应的物理像素矩形。
        /// 当 <paramref name="relativeTo"/> 不为 <see langword="null"/> 且能获取到其宿主窗口时，
        /// 返回该视觉对象所在屏幕的工作区；
        /// 否则返回鼠标当前所在屏幕的工作区。
        /// </returns>
        public static Int32Rect GetScreenWorkAreaPx(Visual? relativeTo = null)
        {
            Rect r = GetScreenRectPx(relativeTo, workArea: true);
            return new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        }

        /// <summary>
        /// 获取指定视觉对象所在屏幕的完整边界，并返回对应的物理像素矩形。
        /// </summary>
        /// <param name="relativeTo">
        /// 用于确定目标屏幕的视觉对象。
        /// 当为 <see langword="null"/> 时，将改为使用鼠标当前所在屏幕。
        /// </param>
        /// <returns>
        /// 屏幕完整边界对应的物理像素矩形。
        /// 当 <paramref name="relativeTo"/> 不为 <see langword="null"/> 且能获取到其宿主窗口时，
        /// 返回该视觉对象所在屏幕的完整边界；
        /// 否则返回鼠标当前所在屏幕的完整边界。
        /// </returns>
        public static Int32Rect GetScreenBoundsPx(Visual? relativeTo = null)
        {
            Rect r = GetScreenRectPx(relativeTo, workArea: false);
            return new Int32Rect((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        }

        /// <summary>
        /// 获取整个虚拟屏幕区域。
        /// </summary>
        /// <remarks>
        /// 返回值使用 WPF 逻辑坐标（DIP）。
        /// 该矩形表示所有显示器组合后的总边界，在多显示器环境下其坐标可能包含负值。
        /// </remarks>
        public static Rect VirtualScreenBounds
        {
            get
            {
                return new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);
            }
        }

        /// <summary>
        /// 获取指定元素在屏幕设备坐标中的边界（整数像素）。
        /// </summary>
        /// <param name="element">目标元素。不可为 <see langword="null"/>，且须已完成布局。</param>
        /// <returns>
        /// 元素在屏幕设备坐标中的边界矩形（整数像素）。
        /// 采用"左上取 Floor、右下取 Ceiling"策略，保证完整覆盖元素渲染区域。
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="element"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="InvalidOperationException">元素尚未连接到可用的呈现树。</exception>
        /// <remarks>
        /// 适用于截图裁剪、悬浮层全局定位、命中测试等需要元素屏幕物理坐标的场景。
        /// <para>
        /// <see cref="UIElement.PointToScreen"/> 内部已通过 <see cref="System.Windows.Media.CompositionTarget.TransformToDevice"/>
        /// 完成 DIP → 物理像素的转换，其返回值直接为屏幕物理像素坐标，无需再经额外的 DPI 缩放。
        /// </para>
        /// </remarks>
        public static Int32Rect GetElementScreenBoundsPx(FrameworkElement element)
        {
            if (element == null)
                throw new ArgumentNullException("element");

            // PointToScreen 要求元素已连接到 PresentationSource；
            // 若尚未连接，此处会向外抛出 InvalidOperationException，由调用方决策处理。
            // PointToScreen 返回值已是物理像素，直接对其取整即可。
            Point pxTopLeft = element.PointToScreen(new Point(0, 0));
            Point pxBottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));

            int left = (int)Math.Floor(pxTopLeft.X);
            int top = (int)Math.Floor(pxTopLeft.Y);
            int right = (int)Math.Ceiling(pxBottomRight.X);
            int bottom = (int)Math.Ceiling(pxBottomRight.Y);

            int width = Math.Max(0, right - left);
            int height = Math.Max(0, bottom - top);

            return new Int32Rect(left, top, width, height);
        }

        #endregion ===== Screen Area =====

        #region ===== HWND Helper =====

        private static IntPtr TryGetHwnd(Visual visual)
        {
            try
            {
                HwndSource? source = PresentationSource.FromVisual(visual) as HwndSource;
                if (source != null && source.Handle != IntPtr.Zero)
                    return source.Handle;

                Window host = visual as Window ?? Window.GetWindow(visual);
                if (host != null)
                {
                    IntPtr hwnd = new WindowInteropHelper(host).Handle;
                    if (hwnd != IntPtr.Zero)
                        return hwnd;
                }
            }
            catch
            {
                // 这里故意吞异常。
                // TryGetHwnd 仅作为"尽力获取"的辅助路径，失败时调用方会继续回退到 SystemDpi，
                // 不应让句柄获取问题影响主流程。
            }

            return IntPtr.Zero;
        }

        private static Rect GetScreenRectDip(Visual? relativeTo, bool workArea)
        {
            Rect pxRect = GetScreenRectPx(relativeTo, workArea);
            return PxToDipOnScreen(pxRect, relativeTo);
        }

        private static Rect GetScreenRectPx(Visual? relativeTo, bool workArea)
        {
            if (relativeTo != null)
            {
                try
                {
                    Window hostWindow = relativeTo as Window ?? Window.GetWindow(relativeTo);
                    if (hostWindow != null)
                    {
                        IntPtr hwnd = new WindowInteropHelper(hostWindow).Handle;
                        if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd))
                        {
                            Screen screen = Screen.FromHandle(hwnd);
                            System.Drawing.Rectangle area = workArea ? screen.WorkingArea : screen.Bounds;
                            return new Rect(area.Left, area.Top, area.Width, area.Height);
                        }
                    }
                }
                catch
                {
                    // 获取宿主窗口或屏幕失败时，保持静默回退到鼠标所在屏幕。
                }
            }

            POINT pt;
            NativeMethods.GetCursorPos(out pt);

            Screen mouseScreen = Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
            System.Drawing.Rectangle mouseArea = workArea ? mouseScreen.WorkingArea : mouseScreen.Bounds;
            return new Rect(mouseArea.Left, mouseArea.Top, mouseArea.Width, mouseArea.Height);
        }

        #endregion ===== HWND Helper =====
    }
}
