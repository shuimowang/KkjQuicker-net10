using KkjQuicker.Overlay.Engine;
using KkjQuicker.Utilities.Imaging;
using KkjQuicker.Utilities.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KkjQuicker.Overlay.Layers
{
    /// <summary>
    /// 截图图层：冻结桌面、框选区域、自动吸附窗口、放大镜取色。
    /// <para>
    /// 坐标体系说明：
    /// <list type="bullet">
    /// <item><description><see cref="_selectionRect"/>：DIP，Canvas 坐标系（用于 UI 绘制）。</description></item>
    /// <item><description><see cref="_selectionBitmapPixelRect"/>：相对 <see cref="_screen"/> 位图的像素坐标（原点为 VirtualScreen 左上角对应像素，即 (0,0)）。</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <remarks>
    /// 本图层按"一次截图一个实例"的方式设计。
    /// 调用顺序应为：先将图层 Push 到 Overlay，再调用 <see cref="CaptureAsync"/>。
    /// 无需手动创建引擎和图层。</para>
    /// </remarks>
    public sealed class ScreenshotLayer : OverlayInputLayerBase
    {
        private readonly ScreenshotCanvas _canvas = null!;
        private OverlayContext _context = null!;

        private BitmapSource? _screen;
        private Rect _selectionRect;
        private Int32Rect _selectionBitmapPixelRect;
        private TaskCompletionSource<ScreenshotResult?>? _tcs;

        private bool _isDragging;
        private bool _captureStarted;
        private Point _startPos;
        private readonly System.Drawing.Rectangle _vsBounds;
        private Dictionary<IntPtr, RECT> _winRects = null!;

        // 均在 UI 线程上访问，无需 Interlocked。
        private bool _completed;
        private bool _focusPending;

        private string _currentHexColor = "#000000";

        /// <summary>
        /// 获取图层可视元素。
        /// </summary>
        public override UIElement View => _canvas;

        /// <summary>
        /// 表示一次截图的结果。
        /// </summary>
        public sealed class ScreenshotResult
        {
            /// <summary>
            /// 获取冻结时刻的整张虚拟屏截图。
            /// </summary>
            public BitmapSource FullScreen { get; private set; } = null!;

            /// <summary>
            /// 获取选区的 DIP 矩形（Canvas 坐标系）。
            /// </summary>
            public Rect SelectionRect { get; private set; }

            /// <summary>
            /// 获取选区在整张冻结位图中的像素矩形。
            /// </summary>
            public Int32Rect BitmapPixelRect { get; private set; }

            /// <summary>
            /// 获取裁剪后的截图位图。
            /// </summary>
            public BitmapSource Cropped { get; private set; } = null!;

            internal ScreenshotResult(BitmapSource full, Rect dipRect, Int32Rect bmpPxRect, BitmapSource cropped)
            {
                FullScreen = full;
                SelectionRect = dipRect;
                BitmapPixelRect = bmpPxRect;
                Cropped = cropped;
            }
        }

        /// <summary>
        /// 初始化一个新的 <see cref="ScreenshotLayer"/> 实例。
        /// </summary>
        public ScreenshotLayer()
        {
            _canvas = new ScreenshotCanvas(this)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent,
                Focusable = true
            };

            _vsBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            _selectionRect = Rect.Empty;
            _selectionBitmapPixelRect = new Int32Rect(0, 0, 0, 0);
        }

        /// <summary>
        /// 开始截图。
        /// </summary>
        /// <returns>表示截图结果的任务。取消时结果为 <see langword="null"/>。</returns>
        /// <remarks>
        /// 调用本方法前，图层必须已经附加到 Overlay。
        /// 一个实例只允许执行一次截图流程；如需再次截图，请创建新实例。
        /// </remarks>
        public Task<ScreenshotResult?> CaptureAsync()
        {
            if (_context == null)
                throw new InvalidOperationException("ScreenshotLayer must be attached before CaptureAsync is called.");

            if (_captureStarted)
                throw new InvalidOperationException("ScreenshotLayer instances are single-use. Create a new instance for each capture.");

            _captureStarted = true;
            _completed = false;
            _focusPending = false;
            _isDragging = false;
            _currentHexColor = "#000000";

            ClearSelectionState();
            _canvas.UpdateRender(Rect.Empty);

            IntPtr overlayHwnd = _context.Window?.GetHandle() ?? IntPtr.Zero;
            _winRects = WindowHelper.GetVisibleWindowRects(overlayHwnd);

            using (var bmp = ScreenCapture.CaptureVirtualScreen())
            {
                _screen = ImageHelper.ToBitmapSource(bmp);
                _screen.Freeze();
                _canvas.UpdateMagnifierSource(_screen);
            }

            _tcs = new TaskCompletionSource<ScreenshotResult?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            EnsureKeyboardFocus();
            return _tcs.Task;
        }

        /// <summary>
        /// 当图层附加到 Overlay 时调用。
        /// </summary>
        /// <param name="context">当前 Overlay 上下文。</param>
        public override void OnAttach(OverlayContext context)
        {
            _context = context;
            EnsureKeyboardFocus();
        }

        /// <summary>
        /// 当图层从 Overlay 中移除时调用。
        /// <para>
        /// 这里是兜底完成路径：如果前面已经成功完成，本方法中的 <see cref="Complete"/> 不会覆盖结果；
        /// 如果尚未完成，则将其视为取消并返回 <see langword="null"/>。
        /// </para>
        /// </summary>
        public override void OnDetach()
        {
            Complete(null);

            _screen = null;
            _winRects = null;
            _context = null;
            _isDragging = false;

            ClearSelectionState();

            try { _canvas.UpdateMagnifierSource(null); } catch { }
            try { _canvas.ReleaseMouseCapture(); } catch { }
        }

        /// <summary>
        /// 处理鼠标按下。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (_screen == null)
                return;

            EnsureKeyboardFocus();

            if (e.RightButton == MouseButtonState.Pressed)
            {
                CancelInternal();
                e.Handled = true;
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _startPos = ClampPointToCanvas(e.GetPosition(_canvas));

                try { _canvas.CaptureMouse(); } catch { }

                e.Handled = true;
            }
        }

        /// <summary>
        /// 处理鼠标移动。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public override void OnPreviewMouseMove(MouseEventArgs e)
        {
            if (_screen == null)
                return;

            Point currentPos = ClampPointToCanvas(e.GetPosition(_canvas));

            if (_isDragging)
            {
                UpdateSelection(currentPos);
            }
            else
            {
                AutoSelectWindow(currentPos);

                // 即使选区矩形没有变化，也仍需刷新放大镜与取色信息。
                _canvas.InvalidateVisual();
            }

            e.Handled = true;
        }

        /// <summary>
        /// 处理鼠标抬起。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            if (_screen == null)
                return;
            if (!_isDragging)
                return;

            _isDragging = false;

            try { _canvas.ReleaseMouseCapture(); } catch { }

            Rect dipRect = _selectionRect;
            Int32Rect bmpPx = _selectionBitmapPixelRect;

            if (dipRect.Width <= 0 || dipRect.Height <= 0)
            {
                e.Handled = true;
                return;
            }

            if (bmpPx.Width <= 0 || bmpPx.Height <= 0)
            {
                CancelInternal();
                e.Handled = true;
                return;
            }

            var cb = new CroppedBitmap(_screen, bmpPx);
            cb.Freeze();

            FinishWithResult(new ScreenshotResult(_screen, dipRect, bmpPx, cb));

            e.Handled = true;
        }

        /// <summary>
        /// 处理按键按下。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (_screen == null)
                return;

            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;

            POINT p;
            if (!NativeMethods.GetCursorPos(out p))
                return;

            switch (e.Key)
            {
                case Key.Escape:
                    CancelInternal();
                    e.Handled = true;
                    return;

                case Key.C:
                    try
                    {
                        Clipboard.SetText(_currentHexColor);
                        CancelInternal();
                    }
                    catch
                    {
                    }

                    e.Handled = true;
                    return;

                case Key.Left: p.X -= step; break;
                case Key.Right: p.X += step; break;
                case Key.Up: p.Y -= step; break;
                case Key.Down: p.Y += step; break;

                default:
                    return;
            }

            NativeMethods.SetCursorPos(p.X, p.Y);
            EnsureKeyboardFocus();
            e.Handled = true;
        }

        /// <summary>
        /// 当输入捕获丢失时调用。
        /// </summary>
        public override void OnInputCaptureLost()
        {
            _isDragging = false;

            try { _canvas.ReleaseMouseCapture(); } catch { }
        }

        private void EnsureKeyboardFocus()
        {
            try
            {
                if (_canvas.IsKeyboardFocusWithin)
                    return;

                _canvas.Focusable = true;
                Keyboard.Focus(_canvas);

                if (_focusPending)
                    return;

                _focusPending = true;

                _canvas.Dispatcher.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        if (_screen != null && !_canvas.IsKeyboardFocusWithin)
                            Keyboard.Focus(_canvas);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _focusPending = false;
                    }
                }), DispatcherPriority.Input);
            }
            catch
            {
                _focusPending = false;
            }
        }

        private void CancelInternal()
        {
            FinishWithResult(null);
        }

        private void FinishWithResult(ScreenshotResult? resultOrNull)
        {
            _isDragging = false;

            try { _canvas.ReleaseMouseCapture(); } catch { }

            Complete(resultOrNull);
            CloseSelf();
        }

        private void CloseSelf()
        {
            try { _context?.Close(); }
            catch { }
        }

        private void Complete(ScreenshotResult? resultOrNull)
        {
            if (_completed)
                return;

            _completed = true;

            try { _tcs?.TrySetResult(resultOrNull); }
            catch { }
        }

        private void ClearSelectionState()
        {
            _selectionRect = Rect.Empty;
            _selectionBitmapPixelRect = new Int32Rect(0, 0, 0, 0);
        }

        private void UpdateSelection(Point currentDip)
        {
            // new Rect(Point, Point) 由 WPF 自动规范化（无需额外 NormalizeRect）。
            ApplySelection(new Rect(_startPos, currentDip));
        }

        private void ApplySelection(Rect dipRect)
        {
            // dipRect 由调用方保证已规范化（来自 new Rect(p,p) 或 ClampRectToCanvas）。
            Int32Rect bmpPx = ClampToBitmap(DipRectToBitmapPxRect(dipRect), _screen);

            _selectionRect = dipRect;
            _selectionBitmapPixelRect = bmpPx;

            _canvas.UpdateRender(_selectionRect);
        }

        private Point ClampPointToCanvas(Point p)
        {
            return new Point(
                Math.Max(0, Math.Min(p.X, _canvas.ActualWidth)),
                Math.Max(0, Math.Min(p.Y, _canvas.ActualHeight)));
        }

        private static Rect NormalizeRect(Rect r)
        {
            double x = r.Width < 0 ? r.X + r.Width : r.X;
            double y = r.Height < 0 ? r.Y + r.Height : r.Y;
            return new Rect(x, y, Math.Abs(r.Width), Math.Abs(r.Height));
        }

        private Rect ClampRectToCanvas(Rect r)
        {
            r = NormalizeRect(r);

            double left = Math.Max(0, r.Left);
            double top = Math.Max(0, r.Top);
            double right = Math.Min(_canvas.ActualWidth, r.Right);
            double bottom = Math.Min(_canvas.ActualHeight, r.Bottom);

            if (right < left) right = left;
            if (bottom < top) bottom = top;

            return new Rect(new Point(left, top), new Point(right, bottom));
        }

        private Int32Rect DipRectToBitmapPxRect(Rect dipRect)
        {
            Rect screenPxRect = DpiHelper.DipToPxOnScreen(dipRect, _canvas);

            int left = (int)Math.Floor(screenPxRect.Left);
            int top = (int)Math.Floor(screenPxRect.Top);
            int right = (int)Math.Ceiling(screenPxRect.Right);
            int bottom = (int)Math.Ceiling(screenPxRect.Bottom);

            return new Int32Rect(
                left - _vsBounds.Left,
                top - _vsBounds.Top,
                Math.Max(0, right - left),
                Math.Max(0, bottom - top));
        }

        private static Int32Rect ClampToBitmap(Int32Rect r, BitmapSource bmp)
        {
            if (bmp == null)
                return new Int32Rect(0, 0, 0, 0);

            int x = r.X, y = r.Y, w = r.Width, h = r.Height;

            if (w <= 0 || h <= 0)
                return new Int32Rect(0, 0, 0, 0);

            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }

            if (x >= bmp.PixelWidth || y >= bmp.PixelHeight)
                return new Int32Rect(0, 0, 0, 0);

            if (x + w > bmp.PixelWidth) w = bmp.PixelWidth - x;
            if (y + h > bmp.PixelHeight) h = bmp.PixelHeight - y;

            if (w <= 0 || h <= 0)
                return new Int32Rect(0, 0, 0, 0);

            return new Int32Rect(x, y, w, h);
        }

        private bool AutoSelectWindow(Point mouseDip)
        {
            if (_winRects == null || _winRects.Count == 0)
                return false;

            Point mousePx = DpiHelper.DipToPxOnScreen(mouseDip, _canvas);

            RECT best = default(RECT);
            bool found = false;
            long bestArea = long.MaxValue;

            foreach (var kv in _winRects)
            {
                RECT r = kv.Value;
                if (r.Contains((int)mousePx.X, (int)mousePx.Y))
                {
                    long area = (long)r.Width * (long)r.Height;
                    if (area < bestArea)
                    {
                        bestArea = area;
                        best = r;
                        found = true;
                    }
                }
            }

            if (!found)
                return false;

            Rect newRect = ClampRectToCanvas(DpiHelper.PxToDipOnScreen(
                new Rect(new Point(best.Left, best.Top), new Point(best.Right, best.Bottom)),
                _canvas));

            if (newRect.Width <= 0 || newRect.Height <= 0)
                return false;

            if (NearlyEqualsRect(_selectionRect, newRect))
                return false;

            ApplySelection(newRect);
            return true;
        }

        private static bool NearlyEqualsRect(Rect a, Rect b)
        {
            return Math.Abs(a.X - b.X) < 0.01 &&
                   Math.Abs(a.Y - b.Y) < 0.01 &&
                   Math.Abs(a.Width - b.Width) < 0.01 &&
                   Math.Abs(a.Height - b.Height) < 0.01;
        }

        private sealed class ScreenshotCanvas : Canvas
        {
            private readonly ScreenshotLayer _owner;
            private Rect _drawRect;

            // 冻结画刷与画笔（构造后不再变化）
            private readonly Brush _maskBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
            private readonly Pen _edgePen = new Pen(Brushes.DeepSkyBlue, 2);
            private readonly Brush _magBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));
            private readonly Pen _magBorderPen = new Pen(Brushes.White, 4);
            private readonly Pen _centerCrossPen = new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 0, 0)), 1);
            private readonly Pen _lightGrayPen = new Pen(Brushes.LightGray, 1);

            // 复用对象（每帧更新内容但对象本身不重建）
            private readonly byte[] _pixelBuffer = new byte[4];
            private readonly SolidColorBrush _colorPreviewBrush = new SolidColorBrush();
            private readonly ImageBrush _magnifierBrush = new ImageBrush();

            // 字体缓存：DrawText 每次调用均复用，避免每帧重复分配。
            private static readonly Typeface _uiTypeface = new Typeface("Segoe UI, Microsoft YaHei");

            private const double MagSize = 130;
            private const double MagTextAreaH = 85;
            private const double MagOffset = 20;
            private const double MagViewSize = 120;
            private const double MagZoom = 8;

            public ScreenshotCanvas(ScreenshotLayer owner)
            {
                _owner = owner;

                Background = Brushes.Transparent;
                Cursor = Cursors.Cross;
                Focusable = true;

                _maskBrush.Freeze();
                _edgePen.Freeze();
                _magBackground.Freeze();
                _magBorderPen.Freeze();
                _centerCrossPen.Freeze();
                _lightGrayPen.Freeze();

                _magnifierBrush.Stretch = Stretch.Fill;
                _magnifierBrush.ViewboxUnits = BrushMappingMode.Absolute;
                _magnifierBrush.AlignmentX = AlignmentX.Center;
                _magnifierBrush.AlignmentY = AlignmentY.Center;
                RenderOptions.SetBitmapScalingMode(_magnifierBrush, BitmapScalingMode.NearestNeighbor);
            }

            public void UpdateMagnifierSource(BitmapSource? screen)
            {
                _magnifierBrush.ImageSource = screen;
            }

            public void UpdateRender(Rect r)
            {
                _drawRect = r;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext dc)
            {
                if (_owner._screen == null)
                    return;

                dc.DrawImage(_owner._screen, new Rect(0, 0, ActualWidth, ActualHeight));
                DrawMaskAndSelection(dc);
                DrawMagnifier(dc);
            }

            private void DrawMaskAndSelection(DrawingContext dc)
            {
                Rect r = _drawRect;

                if (r.Width <= 0 || r.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
                {
                    dc.DrawRectangle(_maskBrush, null,
                        new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)));
                    return;
                }

                double left = Math.Max(0, Math.Min(r.Left, ActualWidth));
                double top = Math.Max(0, Math.Min(r.Top, ActualHeight));
                double right = Math.Max(0, Math.Min(r.Right, ActualWidth));
                double bottom = Math.Max(0, Math.Min(r.Bottom, ActualHeight));

                if (right <= left || bottom <= top)
                {
                    dc.DrawRectangle(_maskBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));
                    return;
                }

                r = new Rect(new Point(left, top), new Point(right, bottom));

                dc.DrawRectangle(_maskBrush, null, new Rect(0, 0, ActualWidth, top));
                dc.DrawRectangle(_maskBrush, null, new Rect(0, top, left, bottom - top));
                dc.DrawRectangle(_maskBrush, null, new Rect(right, top, ActualWidth - right, bottom - top));
                dc.DrawRectangle(_maskBrush, null, new Rect(0, bottom, ActualWidth, ActualHeight - bottom));
                dc.DrawRectangle(null, _edgePen, r);

                var dpi = VisualTreeHelper.GetDpi(this);
                Int32Rect bmpPx = _owner._selectionBitmapPixelRect;

                string info = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1}   {2} × {3}",
                    bmpPx.X + _owner._vsBounds.Left,
                    bmpPx.Y + _owner._vsBounds.Top,
                    Math.Max(0, bmpPx.Width),
                    Math.Max(0, bmpPx.Height));

                var ft = new FormattedText(
                    info,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _uiTypeface,
                    13,
                    Brushes.White,
                    dpi.PixelsPerDip);

                const double pad = 6;
                Rect labelRect = new Rect(
                    r.X,
                    r.Y - ft.Height - pad * 2 - 6,
                    ft.Width + pad * 2,
                    ft.Height + pad * 2);

                if (labelRect.Y < 0)
                {
                    labelRect.Y = r.Y + 6;
                    labelRect.X = r.X + 6;
                }

                dc.DrawRoundedRectangle(_maskBrush, _edgePen, labelRect, 4, 4);
                dc.DrawText(ft, new Point(labelRect.X + pad, labelRect.Y + pad));
            }

            private void DrawMagnifier(DrawingContext dc)
            {
                Point mouseDip = Mouse.GetPosition(this);
                if (mouseDip.X < 0 || mouseDip.Y < 0)
                    return;

                var dpi = VisualTreeHelper.GetDpi(this);

                Point mousePx = PointToScreen(mouseDip);
                int physX = (int)Math.Round(mousePx.X);
                int physY = (int)Math.Round(mousePx.Y);

                Point mouseDipFromScreen = PointFromScreen(mousePx);

                double vSize = MagViewSize / MagZoom;
                _magnifierBrush.Viewbox = new Rect(
                    mouseDipFromScreen.X - vSize / 2,
                    mouseDipFromScreen.Y - vSize / 2,
                    vSize,
                    vSize);

                double magX = mouseDip.X + MagOffset;
                double magY = mouseDip.Y + MagOffset;

                if (magX + MagSize > ActualWidth)
                    magX = mouseDip.X - MagSize - MagOffset;

                if (magY + MagSize + MagTextAreaH > ActualHeight)
                    magY = mouseDip.Y - MagSize - MagTextAreaH - MagOffset;

                if (magX < 0) magX = 0;
                if (magY < 0) magY = 0;

                if (magX + MagSize > ActualWidth)
                    magX = Math.Max(0, ActualWidth - MagSize);

                if (magY + MagSize + MagTextAreaH > ActualHeight)
                    magY = Math.Max(0, ActualHeight - (MagSize + MagTextAreaH));

                int vx = physX - _owner._vsBounds.Left;
                int vy = physY - _owner._vsBounds.Top;

                Color c = GetPixelColor(_owner._screen, vx, vy);
                _owner._currentHexColor = string.Format(
                    CultureInfo.InvariantCulture,
                    "#{0:X2}{1:X2}{2:X2}",
                    c.R, c.G, c.B);

                double thickness = 1.0 / dpi.DpiScaleX;
                var g = new GuidelineSet();
                g.GuidelinesX.Add(magX + thickness / 2);
                g.GuidelinesY.Add(magY + thickness / 2);
                dc.PushGuidelineSet(g);

                Rect total = new Rect(magX, magY, MagSize, MagSize + MagTextAreaH);
                dc.DrawRoundedRectangle(_magBackground, _magBorderPen, total, 2, 2);

                Rect view = new Rect(magX + 5, magY + 5, MagSize - 10, MagSize - 10);
                dc.DrawRectangle(_magnifierBrush, null, view);

                double cx = view.X + view.Width / 2;
                double cy = view.Y + view.Height / 2;
                dc.DrawLine(_centerCrossPen, new Point(cx - 8, cy), new Point(cx + 8, cy));
                dc.DrawLine(_centerCrossPen, new Point(cx, cy - 8), new Point(cx, cy + 8));

                DrawText(dc, string.Format(CultureInfo.InvariantCulture, "POS: {0}, {1}", physX, physY),
                    11, Brushes.DimGray, new Point(magX + 10, magY + MagSize + 5));

                _colorPreviewBrush.Color = c;
                dc.DrawRectangle(_colorPreviewBrush, _lightGrayPen,
                    new Rect(magX + 10, magY + MagSize + 28, 14, 14));
                DrawText(dc, _owner._currentHexColor, 12, Brushes.Black,
                    new Point(magX + 30, magY + MagSize + 26));

                DrawText(dc, "按 C 复制色号", 9, Brushes.Silver, new Point(magX + 10, magY + MagSize + 48));
                DrawText(dc, "按 ESC / 右键 退出", 9, Brushes.Silver, new Point(magX + 10, magY + MagSize + 60));

                dc.Pop();
            }

            /// <summary>
            /// 读取单个像素颜色。
            /// <para>
            /// 保持 1×1 CopyPixels 的简单实现：
            /// 放大镜场景逻辑直接、可靠，不额外引入整块缓存以避免复杂度上升。
            /// </para>
            /// </summary>
            private Color GetPixelColor(BitmapSource bitmap, int x, int y)
            {
                if (bitmap == null)
                    return Colors.Transparent;

                x = Math.Max(0, Math.Min(x, bitmap.PixelWidth - 1));
                y = Math.Max(0, Math.Min(y, bitmap.PixelHeight - 1));

                bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), _pixelBuffer, 4, 0);
                return Color.FromArgb(_pixelBuffer[3], _pixelBuffer[2], _pixelBuffer[1], _pixelBuffer[0]);
            }

            private void DrawText(DrawingContext dc, string text, double size, Brush brush, Point pos)
            {
                var ft = new FormattedText(
                    text,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    _uiTypeface,
                    size,
                    brush,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                dc.DrawText(ft, pos);
            }
        }
    }
}
