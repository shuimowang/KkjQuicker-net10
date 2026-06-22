using FontAwesome5;
using KkjQuicker.Overlay.Engine;
using KkjQuicker.UI;
using KkjQuicker.Utilities.History;
using KkjQuicker.Utilities.Imaging;
using KkjQuicker.Utilities.Threading;
using KkjQuicker.Utilities.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace KkjQuicker.Overlay.Layers
{
    #region ===== Options & Helpers =====

    /// <summary>
    /// 截图编辑器的配置选项。
    /// </summary>
    public sealed class EditorOptions
    {
        /// <summary>保存行为（可选）。返回 true 表示已由宿主处理保存。</summary>
        public Func<EditorView, BitmapSource, bool> SaveHandler { get; set; }

        /// <summary>错误回调。</summary>
        public Action<Exception> OnError { get; set; }

        /// <summary>中键行为（可选）。</summary>
        public Func<EditorView, bool> MiddleClickHandler { get; set; }

        /// <summary>额外工具栏按钮（可选）。</summary>
        public List<ToolbarButton> ExtraToolbarButtons { get; set; } = new List<ToolbarButton>();

        /// <summary>右键菜单构造器（可选）。</summary>
        public Func<EditorView, ContextMenu> CreateContextMenu { get; set; }

        /// <summary>默认颜色预设。</summary>
        public Color[] ColorPresets { get; set; } = new[] { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Aqua, Colors.White };

        /// <summary>默认线宽。</summary>
        public double DefaultThickness { get; set; } = 3.0;

        /// <summary>马赛克遮罩画笔粗细。</summary>
        public double MosaicStrokeThickness { get; set; } = 24;

        /// <summary>马赛克像素块大小。</summary>
        public int MosaicPixelSize { get; set; } = 14;
    }

    /// <summary>
    /// 表示一个可注入到编辑器工具栏中的扩展动作按钮。
    /// </summary>
    public sealed class ToolbarButton
    {
        /// <summary>按钮图标或显示内容。</summary>
        public object Icon { get; set; }
        /// <summary>按钮提示文本。</summary>
        public string ToolTip { get; set; }
        /// <summary>点击按钮或触发对应快捷键时执行的动作。</summary>
        public Action<EditorView> OnClick { get; set; }
        /// <summary>快捷键主键。</summary>
        public Key ShortcutKey { get; set; } = Key.None;
        /// <summary>快捷键修饰键。</summary>
        public ModifierKeys ShortcutModifiers { get; set; } = ModifierKeys.None;
    }

    #endregion

    #region ===== Layer Wrapper =====

    /// <summary>
    /// 截图编辑图层:承载 EditorView 并统一结果收口。
    /// 支持静态冻结模式与实时透明穿透模式。
    /// </summary>
    public sealed class ScreenshotEditorLayer : OverlayInputLayerBase
    {
        private readonly EditorView _view;
        private OverlayContext _context;

        private readonly Action<BitmapSource, Int32Rect> _onAccepted;
        private readonly Action _onCanceled;
        private readonly EditorOptions _options;

        private readonly TaskCompletionSource<BitmapSource> _tcs =
            new TaskCompletionSource<BitmapSource>(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _finished;

        /// <summary>获取当前图层的可视元素。</summary>
        public override UIElement View => _view;

        /// <summary>获取编辑完成任务。取消或关闭时结果为 null。</summary>
        public Task<BitmapSource> Completion => _tcs.Task;

        /// <summary>
        /// 静态截图便捷构造函数。
        /// <para>等价于以 <paramref name="fullImage"/> 的完整像素范围作为画布调用主构造函数。</para>
        /// </summary>
        /// <param name="fullImage">完整截图源;不能为 <see langword="null"/>。</param>
        /// <param name="initialSelectionPixelRect">初始选区像素矩形;为空则使用整张图。</param>
        /// <param name="onAccepted">确认回调。</param>
        /// <param name="onCanceled">取消回调。</param>
        /// <param name="options">编辑器配置项。</param>
        /// <exception cref="ArgumentNullException"><paramref name="fullImage"/> 为 <see langword="null"/>。</exception>
        public ScreenshotEditorLayer(
            BitmapSource fullImage,
            Int32Rect initialSelectionPixelRect,
            Action<BitmapSource, Int32Rect>? onAccepted = null,
            Action? onCanceled = null,
            EditorOptions? options = null)
            : this(
                fullImage ?? throw new ArgumentNullException(nameof(fullImage)),
                new Int32Rect(0, 0, fullImage.PixelWidth, fullImage.PixelHeight),
                initialSelectionPixelRect,
                onAccepted,
                onCanceled,
                options)
        {
        }

        /// <summary>
        /// 截图编辑器主构造函数。
        /// <para><paramref name="fullImage"/> 为 <see langword="null"/> 时进入实时透传模式,
        /// 非 <see langword="null"/> 时进入静态冻结模式。</para>
        /// </summary>
        /// <param name="fullImage">完整截图源;实时模式下传 <see langword="null"/>。</param>
        /// <param name="canvasPixelBounds">画布像素范围;宽高必须大于 0。</param>
        /// <param name="initialSelectionPixelRect">初始选区像素矩形(相对屏幕);为空则使用整张画布。</param>
        /// <param name="onAccepted">确认回调。</param>
        /// <param name="onCanceled">取消回调。</param>
        /// <param name="options">编辑器配置项。</param>
        /// <exception cref="ArgumentException"><paramref name="canvasPixelBounds"/> 无效。</exception>
        public ScreenshotEditorLayer(
            BitmapSource fullImage,
            Int32Rect canvasPixelBounds,
            Int32Rect initialSelectionPixelRect,
            Action<BitmapSource, Int32Rect>? onAccepted = null,
            Action? onCanceled = null,
            EditorOptions? options = null)
        {
            if (canvasPixelBounds.Width <= 0 || canvasPixelBounds.Height <= 0)
                throw new ArgumentException("画布像素范围无效。", nameof(canvasPixelBounds));

            _onAccepted = onAccepted;
            _onCanceled = onCanceled;
            _options = options ?? new EditorOptions();

            Rect initialSelectionDip = ConvertPixelSelectionToLocalDipRect(canvasPixelBounds, initialSelectionPixelRect);

            _view = new EditorView(fullImage, canvasPixelBounds, initialSelectionDip, _options);

            _view.Accepted += OnViewAccepted;
            _view.Canceled += OnViewCanceled;
            _view.CloseRequested += OnViewCloseRequested;
        }

        private static Rect ConvertPixelSelectionToLocalDipRect(Int32Rect canvasPixelBounds, Int32Rect selectionPixelRect)
        {
            Int32Rect localPx;
            if (selectionPixelRect.Width <= 0 || selectionPixelRect.Height <= 0)
            {
                localPx = new Int32Rect(0, 0, canvasPixelBounds.Width, canvasPixelBounds.Height);
            }
            else
            {
                localPx = new Int32Rect(
                    selectionPixelRect.X - canvasPixelBounds.X,
                    selectionPixelRect.Y - canvasPixelBounds.Y,
                    selectionPixelRect.Width,
                    selectionPixelRect.Height);
            }

            Size originDip = DpiHelper.PxToDip(new Size(localPx.X, localPx.Y), (Visual)null);
            Size sizeDip = DpiHelper.PxToDip(new Size(localPx.Width, localPx.Height), (Visual)null);

            return new Rect(originDip.Width, originDip.Height, sizeDip.Width, sizeDip.Height);
        }

        public override void OnAttach(OverlayContext context)
        {
            _context = context;
            try
            {
                Keyboard.Focus(_view);
                _view.Dispatcher.BeginInvoke((Action)(() =>
                {
                    try { if (!_finished) Keyboard.Focus(_view); }
                    catch (Exception ex) { _options?.OnError?.Invoke(ex); }
                }), DispatcherPriority.Input);
            }
            catch (Exception ex) { _options?.OnError?.Invoke(ex); }
        }

        public override void OnDetach()
        {
            _context = null;
            if (!_finished)
            {
                _finished = true;
                _tcs.TrySetResult(null);
            }
        }

        public override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CompleteCanceled(); e.Handled = true; }
        }

        private void OnViewAccepted(object? sender, BitmapSource bmp) => CompleteAccepted(bmp);
        private void OnViewCanceled(object? sender, EventArgs e) => CompleteCanceled();
        private void OnViewCloseRequested(object? sender, EventArgs e) => CompleteClosedWithoutCancel();

        private void CompleteAccepted(BitmapSource bmp)
        {
            if (_finished) return;
            _finished = true;
            try { _onAccepted?.Invoke(bmp, _view.SelectionPixelRect); } catch (Exception ex) { _options?.OnError?.Invoke(ex); }
            _tcs.TrySetResult(bmp);
            CloseSelf();
        }

        private void CompleteCanceled(bool closeLayer = true)
        {
            if (_finished) return;
            _finished = true;
            try { _onCanceled?.Invoke(); } catch (Exception ex) { _options?.OnError?.Invoke(ex); }
            _tcs.TrySetResult(null);
            if (closeLayer) CloseSelf();
        }

        private void CompleteClosedWithoutCancel()
        {
            if (_finished) return;
            _finished = true;
            _tcs.TrySetResult(null);
            CloseSelf();
        }

        private void CloseSelf()
        {
            try { _context?.Close(); } catch (Exception ex) { _options?.OnError?.Invoke(ex); }
        }
    }

    #endregion

    #region ===== Editor View =====

    /// <summary>
    /// 截图编辑视图(纯 WPF Canvas)。
    /// 根据是否有背景图自动分派遮罩策略:
    /// 有背景图时使用内嵌遮罩(作为 Canvas 子元素参与 Z 轴);
    /// 无背景图时使用独立 GhostWindow(跨进程穿透)。
    /// 支持通过 SetFullImage 在两种模式之间动态切换。
    /// </summary>
    public sealed class EditorView : Canvas
    {
        public event EventHandler<BitmapSource>? Accepted;
        public event EventHandler? Canceled;
        internal event EventHandler CloseRequested;

        public enum EditTool { None, Pen, Rectangle, Arrow, Text, Number, Mosaic }
        private enum ThumbHit { N, NE, E, SE, S, SW, W, NW }
        private enum InteractionMode { None, MovingSelection, ResizingSelection, Drawing }

        private readonly EditorOptions _opt;
        private readonly HistoryManager _history = new HistoryManager();

        private BitmapSource _fullImage;
        private readonly Int32Rect _canvasPixelBounds;
        private int _imgPxW, _imgPxH;
        private double _imgDipW, _imgDipH;

        private bool _selectionBorderVisible = true;
        private bool _thumbsVisible = true;
        private bool _infoBadgeVisible = true;
        private bool _maskVisible = true;

        private Image _backgroundImage;
        private Rect _selection;
        private EditTool _currentTool = EditTool.None;
        private int _sequenceNumber = 1;

        private Color _currentColor;
        private double _currentThickness;

        private TextBlock _toolNameText;
        private TextBlock _thicknessValueText;
        private Slider _thicknessSlider;
        private StackPanel _effectPanel;
        private TextBlock _effectLabel;
        private Slider _effectSlider;
        private TextBlock _effectValueText;

        private bool IsDrawingMode => _currentTool != EditTool.None;

        private Border _selectionBorder;
        private Grid _clipHost;
        private RectangleGeometry _clipRect;
        private Canvas _inkLayer;
        private Image _mosaicImage;
        private Canvas _mosaicMask;
        private BitmapSource _mosaicBitmap;

        private Border _toolBarBorder;
        private StackPanel _mainToolbar;
        private StackPanel _settingsBar;
        private Border _currentColorSwatch;

        private Border _infoBorder;
        private TextBlock _infoText;

        private InteractionMode _interactionMode;
        private Point _startPoint;
        private Rect _moveStartRect;
        private Rect _resizeStartRect;

        private Shape _tempShape;
        private Polyline _tempPolyline;

        private readonly List<TextBox> _liveTextInputs = new List<TextBox>();
        private readonly Dictionary<Thumb, ThumbHit> _thumbMap = new Dictionary<Thumb, ThumbHit>();

        private int _mosaicPixelSize;
        private bool _effectSliderDragging;
        private ActionRateLimiter _mosaicRebuildLimiter;
        private bool _effectThumbHooked;
        private int _lastMosaicPixelSize = -1;
        private Int32Rect _selectionPixelRect;
        private bool _finished;

        private const double MinSelDip = 10.0;
        private static readonly Brush InteractiveTransparentBrush = CreateInteractiveTransparentBrush();

        /// <summary>获取当前选区的像素矩形范围。</summary>
        public Int32Rect SelectionPixelRect => _selectionPixelRect;

        /// <summary>获取原始全屏截图。</summary>
        public BitmapSource FullImage => _fullImage;

        /// <summary>是否具有背景图片(区分静态截图与实时模式)。</summary>
        public bool HasBackgroundImage => _fullImage != null;

        #region ===== 遮罩:GhostWindow(实时模式) + InlineMask(静态模式) =====

        // GhostWindow:实时透传模式下使用。独立 Topmost 窗口,跨进程穿透。
        // 语义上整窗即"遮罩":SetMaskVisible 直接切换整窗可见性。
        private Window _ghostWindow;
        private RectangleGeometry _maskOuter;
        private RectangleGeometry _maskHole;
        private Path _maskPath;

        // InlineMask:静态截图模式下使用。作为 EditorView 的子元素参与 Z 轴,
        // 天然位于工具栏/选框/手柄/信息标签之下,无需挖洞。
        private Path _inlineMaskPath;
        private RectangleGeometry _inlineMaskOuter;
        private RectangleGeometry _inlineMaskHole;

        /// <summary>
        /// 按当前模式(是否有背景图)确保遮罩载体正确。
        /// 幂等:重复调用或跨模式切换时都能正确销毁旧载体并创建新载体。
        /// </summary>
        private void EnsureMaskHostForCurrentMode()
        {
            if (HasBackgroundImage)
            {
                if (_ghostWindow != null) CloseGhostWindow();
                if (_inlineMaskPath == null) InitInlineMask();
            }
            else
            {
                if (_inlineMaskPath != null) RemoveInlineMask();
                if (_ghostWindow == null) InitGhostWindow();
            }
        }

        private void InitGhostWindow()
        {
            // 像素位置转 DIP,避免高 DPI 或多显示器非零偏移时错位
            Size posDip = DpiHelper.PxToDip(
                new Size(_canvasPixelBounds.X, _canvasPixelBounds.Y), (Visual)null);

            _ghostWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                Left = posDip.Width,
                Top = posDip.Height,
                Width = _imgDipW,
                Height = _imgDipH,
                ShowActivated = false,
                Focusable = false
            };

            var canvas = new Canvas { Background = null };
            _maskOuter = new RectangleGeometry(new Rect(0, 0, _imgDipW, _imgDipH));
            _maskHole = new RectangleGeometry(_selection);
            _maskPath = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude, _maskOuter, _maskHole)
            };
            canvas.Children.Add(_maskPath);
            _ghostWindow.Content = canvas;

            _ghostWindow.SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(_ghostWindow).Handle;
                WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_TRANSPARENT, true);
                WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_TOOLWINDOW, true);
            };

            // 本地函数:显示 ghost 并建立与宿主的 Z 轴所有者关系
            void OnHostLoaded()
            {
                if (_ghostWindow == null) return;

                _ghostWindow.Show();
                var parentWin = Window.GetWindow(this);
                if (parentWin != null)
                {
                    IntPtr parentHwnd = new WindowInteropHelper(parentWin).Handle;
                    IntPtr ghostHwnd = new WindowInteropHelper(_ghostWindow).Handle;

                    // 2. 核心:建立所有者关系(即便 ghost 没有 Owner 属性)
                    // 在 Win32 层面,这能确保 ghost 始终跟随 parent 的 Z 轴变动
                    NativeMethods.SetWindowLongPtr(ghostHwnd, -8, parentHwnd); // GWL_HWNDPARENT
                }

                // 若构造后、Loaded 前已将 _maskVisible 置为 false,需在此同步一次,
                // 否则 Show() 会把可见性拉回 Visible
                if (!_maskVisible)
                    _ghostWindow.Visibility = Visibility.Hidden;
            }

            this.Loaded += (s, e) => OnHostLoaded();
            this.Unloaded += (s, e) => CloseGhostWindow();

            // 若 Init 时本视图已经 Loaded(例如运行期 SetFullImage(null) 切入实时模式),
            // Loaded 事件不会再触发,立即执行一次以完成 Show 与 Z 轴绑定
            if (this.IsLoaded) OnHostLoaded();
        }

        private void CloseGhostWindow()
        {
            if (_ghostWindow != null)
            {
                try { _ghostWindow.Close(); } catch { }
                _ghostWindow = null;
            }
            _maskOuter = null;
            _maskHole = null;
            _maskPath = null;
        }

        private void InitInlineMask()
        {
            _inlineMaskOuter = new RectangleGeometry(new Rect(0, 0, _imgDipW, _imgDipH));
            _inlineMaskHole = new RectangleGeometry(_selection);

            _inlineMaskPath = new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude, _inlineMaskOuter, _inlineMaskHole),
                IsHitTestVisible = false,
                Visibility = _maskVisible ? Visibility.Visible : Visibility.Collapsed
            };

            // 紧跟在 _backgroundImage 之后插入,确保位于 _clipHost/选框/手柄/工具栏之下。
            // 若 InitUI 阶段调用,此时 Children 仅包含 _backgroundImage;
            // 若运行期切换调用,也只追加到当前 Children 末尾会压住其他 UI,因此显式定位在 _backgroundImage 之后。
            int insertIndex = _backgroundImage != null ? Children.IndexOf(_backgroundImage) + 1 : 0;
            if (insertIndex < 0) insertIndex = 0;
            Children.Insert(insertIndex, _inlineMaskPath);
        }

        private void RemoveInlineMask()
        {
            if (_inlineMaskPath != null)
            {
                Children.Remove(_inlineMaskPath);
                _inlineMaskPath = null;
            }
            _inlineMaskOuter = null;
            _inlineMaskHole = null;
        }

        #endregion

        /// <summary>
        /// 创建一个新的截图编辑器视图。
        /// </summary>
        public EditorView(BitmapSource fullImage, Int32Rect canvasPixelBounds, Rect initialRect, EditorOptions options)
        {
            if (canvasPixelBounds.Width <= 0 || canvasPixelBounds.Height <= 0)
                throw new ArgumentException("画布像素范围无效。", nameof(canvasPixelBounds));

            _opt = options ?? new EditorOptions();
            _canvasPixelBounds = canvasPixelBounds;

            Focusable = true;
            Cursor = Cursors.Arrow;

            ApplyImageAndCanvas(fullImage);

            _selection = NormalizeRect(initialRect);
            _currentColor = Colors.Red;
            _currentThickness = Math.Max(1, _opt.DefaultThickness);
            _mosaicPixelSize = Math.Max(2, _opt.MosaicPixelSize);

            _mosaicRebuildLimiter = new ActionRateLimiter(TimeSpan.FromMilliseconds(150));

            InitUI();
            UpdateInteractionSurface();
            UpdateVisuals(false);

            KeyDown += OnKeyDown;
            ContextMenu = _opt.CreateContextMenu != null ? _opt.CreateContextMenu(this) : BuildDefaultContextMenu();
        }

        /// <summary>
        /// 运行期切换背景图。传入 <see langword="null"/> 进入实时透传模式,
        /// 传入非空位图进入静态冻结模式。遮罩载体会随之自动切换。
        /// </summary>
        public void SetFullImage(BitmapSource fullImage)
        {
            CleanupTransientUiForModeSwitch();
            ApplyImageAndCanvas(fullImage);
            _lastMosaicPixelSize = -1;

            if (_backgroundImage != null)
            {
                _backgroundImage.Source = _fullImage;
                _backgroundImage.Width = _imgDipW;
                _backgroundImage.Height = _imgDipH;
                _backgroundImage.Visibility = _fullImage != null ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_clipHost != null) { _clipHost.Width = _imgDipW; _clipHost.Height = _imgDipH; }
            if (_inkLayer != null) { _inkLayer.Width = _imgDipW; _inkLayer.Height = _imgDipH; }
            if (_mosaicMask != null) { _mosaicMask.Width = _imgDipW; _mosaicMask.Height = _imgDipH; }
            if (_mosaicImage != null) { _mosaicImage.Width = _imgDipW; _mosaicImage.Height = _imgDipH; }

            if (_fullImage == null) _mosaicBitmap = null;
            if (_mosaicImage != null) _mosaicImage.Source = _mosaicBitmap;

            // 按当前模式切换遮罩载体(可能销毁或创建 GhostWindow / InlineMask)
            EnsureMaskHostForCurrentMode();

            // 同步当前存在的遮罩尺寸
            if (_ghostWindow != null)
            {
                _ghostWindow.Width = _imgDipW;
                _ghostWindow.Height = _imgDipH;
            }
            if (_maskOuter != null) _maskOuter.Rect = new Rect(0, 0, _imgDipW, _imgDipH);
            if (_inlineMaskOuter != null) _inlineMaskOuter.Rect = new Rect(0, 0, _imgDipW, _imgDipH);

            _selection = NormalizeRect(_selection);
            UpdateInteractionSurface();
            UpdateVisuals(false);
        }

        public void SetSelectionBorderVisible(bool visible)
        {
            _selectionBorderVisible = visible;
            if (_selectionBorder != null) _selectionBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetThumbsVisible(bool visible)
        {
            _thumbsVisible = visible;
            foreach (var thumb in _thumbMap.Keys) thumb.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetInfoBadgeVisible(bool visible)
        {
            _infoBadgeVisible = visible;
            if (_infoBorder != null) _infoBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 设置遮罩可见性。
        /// <para>实时模式下整个 GhostWindow 即语义上的遮罩,会随之显隐;
        /// 静态模式下切换内嵌遮罩 Path 的可见性。</para>
        /// </summary>
        public void SetMaskVisible(bool visible)
        {
            _maskVisible = visible;
            if (_ghostWindow != null)
                _ghostWindow.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
            if (_inlineMaskPath != null)
                _inlineMaskPath.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void CleanupTransientUiForModeSwitch()
        {
            _interactionMode = InteractionMode.None;
            try { if (IsMouseCaptured) ReleaseMouseCapture(); } catch { }
            _tempShape = null;
            _tempPolyline = null;
            CleanupTransientTextInputs();
        }

        private void ApplyImageAndCanvas(BitmapSource fullImage)
        {
            _fullImage = fullImage;
            _imgPxW = _fullImage != null ? _fullImage.PixelWidth : _canvasPixelBounds.Width;
            _imgPxH = _fullImage != null ? _fullImage.PixelHeight : _canvasPixelBounds.Height;
            Size dip = DpiHelper.PxToDip(new Size(_imgPxW, _imgPxH), (Visual)null);
            _imgDipW = dip.Width;
            _imgDipH = dip.Height;
            Width = _imgDipW;
            Height = _imgDipH;
        }

        private void InitUI()
        {
            _backgroundImage = new Image { Source = _fullImage, Width = _imgDipW, Height = _imgDipH, Stretch = Stretch.Fill, Visibility = _fullImage != null ? Visibility.Visible : Visibility.Collapsed };
            Children.Add(_backgroundImage);

            // 按当前模式创建遮罩载体(与 SetFullImage 共用同一入口)
            EnsureMaskHostForCurrentMode();

            _clipRect = new RectangleGeometry(_selection);

            _mosaicMask = new Canvas { Width = _imgDipW, Height = _imgDipH, Background = null, Visibility = Visibility.Collapsed };
            _mosaicImage = new Image { Width = _imgDipW, Height = _imgDipH, Stretch = Stretch.Fill };
            _mosaicImage.OpacityMask = new VisualBrush(_mosaicMask) { Stretch = Stretch.Fill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };

            _inkLayer = new Canvas { Width = _imgDipW, Height = _imgDipH, Background = null };

            _clipHost = new Grid { Width = _imgDipW, Height = _imgDipH, Clip = _clipRect };
            _clipHost.Children.Add(_mosaicImage);
            _clipHost.Children.Add(_inkLayer);
            _clipHost.Children.Add(_mosaicMask);
            Children.Add(_clipHost);

            _selectionBorder = new Border
            {
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(2),
                Background = null,
                IsHitTestVisible = false
            };
            Children.Add(_selectionBorder);

            BuildThumbs();
            BuildInfoBadge();
            BuildToolbars();
            Children.Add(_toolBarBorder);

            PreviewMouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            PreviewMouseUp += OnMouseUp;

            Unloaded += (s, e) => _mosaicRebuildLimiter?.Dispose();
        }

        private void BuildInfoBadge()
        {
            _infoText = new TextBlock { Foreground = Brushes.Gainsboro, FontSize = 11 };
            _infoBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(210, 25, 25, 25)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, Child = _infoText };
            Children.Add(_infoBorder);
        }

        private void BuildThumbs()
        {
            AddThumb(ThumbHit.N, Cursors.SizeNS); AddThumb(ThumbHit.S, Cursors.SizeNS);
            AddThumb(ThumbHit.E, Cursors.SizeWE); AddThumb(ThumbHit.W, Cursors.SizeWE);
            AddThumb(ThumbHit.NE, Cursors.SizeNESW); AddThumb(ThumbHit.SW, Cursors.SizeNESW);
            AddThumb(ThumbHit.NW, Cursors.SizeNWSE); AddThumb(ThumbHit.SE, Cursors.SizeNWSE);
        }

        private void AddThumb(ThumbHit hit, Cursor cursor)
        {
            var t = new Thumb { Width = 10, Height = 10, Background = Brushes.DodgerBlue, Opacity = 0.95, Cursor = cursor, Template = BuildThumbTemplate() };
            t.DragStarted += (s, e) => { Focus(); _interactionMode = InteractionMode.ResizingSelection; _resizeStartRect = _selection; SetToolbarVisible(false); };
            t.DragDelta += (s, e) => { Rect r = _selection; ApplyResize(hit, e.HorizontalChange, e.VerticalChange, ref r); _selection = NormalizeRect(r); UpdateVisuals(true); };
            t.DragCompleted += (s, e) =>
            {
                _interactionMode = InteractionMode.None; SetToolbarVisible(true);
                var before = _resizeStartRect; var after = _selection;
                if (!RectEquals(before, after)) PushSelectionHistory("ResizeSelection", before, after);
                UpdateVisuals(false);
            };
            _thumbMap[t] = hit; Children.Add(t);
        }

        private ControlTemplate BuildThumbTemplate()
        {
            var f = new FrameworkElementFactory(typeof(Border));
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Thumb.BackgroundProperty));
            f.SetValue(Border.BorderBrushProperty, Brushes.White);
            f.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            return new ControlTemplate(typeof(Thumb)) { VisualTree = f };
        }

        private static SvgAwesome CreateToolbarIcon(EFontAwesomeIcon icon, double size = 14) => new SvgAwesome { Icon = icon, Width = size, Height = size, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        private static object CreateToolbarContent(EFontAwesomeIcon icon) => CreateToolbarIcon(icon);

        private static string GetToolDisplayName(EditTool tool)
        {
            switch (tool)
            {
                case EditTool.Pen: return "画笔";
                case EditTool.Rectangle: return "矩形";
                case EditTool.Arrow: return "箭头";
                case EditTool.Text: return "文字";
                case EditTool.Number: return "序号";
                case EditTool.Mosaic: return "马赛克";
                default: return "无";
            }
        }

        private void BuildToolbars()
        {
            var rootStack = new StackPanel { Orientation = Orientation.Vertical };
            _mainToolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };

            AddToolBtn(CreateToolbarContent(EFontAwesomeIcon.Solid_Pen), "画笔", EditTool.Pen);
            AddToolBtn(CreateToolbarContent(EFontAwesomeIcon.Regular_Square), "矩形", EditTool.Rectangle);
            AddToolBtn(CreateToolbarContent(EFontAwesomeIcon.Solid_LongArrowAltRight), "箭头", EditTool.Arrow);
            AddToolBtn(CreateToolbarContent(EFontAwesomeIcon.Solid_Font), "文字", EditTool.Text);
            AddToolBtn(CreateToolbarContent(EFontAwesomeIcon.Solid_ListOl), "序号", EditTool.Number);
            AddToolBtn(CreateToolbarContent(EFontAwesomeIcon.Solid_ThLarge), "马赛克", EditTool.Mosaic);

            UiFactory.Add(_mainToolbar, UiFactory.ToolSep());

            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Solid_UndoAlt), "撤销 (Ctrl+Z)", SafeUndo));
            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Solid_RedoAlt), "重做 (Ctrl+Y)", SafeRedo));
            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Solid_TrashAlt), "清空标注(可撤销)", ClearAllAnnotations));

            if (_opt.ExtraToolbarButtons != null && _opt.ExtraToolbarButtons.Count > 0)
            {
                UiFactory.Add(_mainToolbar, UiFactory.ToolSep());
                foreach (var b in _opt.ExtraToolbarButtons)
                {
                    if (b == null) continue;
                    _mainToolbar.Children.Add(UiFactory.ToolButton(b.Icon ?? CreateToolbarContent(EFontAwesomeIcon.Solid_Star), BuildToolbarButtonToolTip(b), () => b.OnClick?.Invoke(this)));
                }
            }

            _mainToolbar.Children.Add(new Separator { Margin = new Thickness(8, 0, 8, 0), Background = Brushes.DimGray });
            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Solid_Times), "取消 (Esc)", Cancel));
            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Solid_Save), "保存为图片 (V)", SaveAsImage));
            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Regular_Copy), "复制 (C)", Copy));
            _mainToolbar.Children.Add(UiFactory.ToolButton(CreateToolbarContent(EFontAwesomeIcon.Solid_Check), "确认 (Enter)", Accept));

            _settingsBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 5, 2, 2), Visibility = Visibility.Collapsed };
            BuildSettingsBar();

            rootStack.Children.Add(_mainToolbar);
            rootStack.Children.Add(_settingsBar);

            _toolBarBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(245, 30, 30, 30)), CornerRadius = new CornerRadius(4), Padding = new Thickness(6), Effect = new DropShadowEffect { BlurRadius = 15, Opacity = 0.4 }, Child = rootStack };
        }

        private void BuildSettingsBar()
        {
            _settingsBar.Children.Clear();
            _toolNameText = new TextBlock { Text = "当前工具:无", Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Margin = new Thickness(2, 0, 10, 0) };
            _settingsBar.Children.Add(_toolNameText);

            _currentColorSwatch = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Brushes.DimGray, Margin = new Thickness(0, 0, 8, 0), ToolTip = "当前颜色" };
            _settingsBar.Children.Add(_currentColorSwatch);

            foreach (var col in (_opt.ColorPresets ?? new[] { Colors.Red, Colors.Yellow, Colors.Lime, Colors.Aqua, Colors.White }))
            {
                var rect = new Border { Width = 18, Height = 18, Background = new SolidColorBrush(col), Margin = new Thickness(3, 0, 3, 0), CornerRadius = new CornerRadius(2), Cursor = Cursors.Hand, ToolTip = "颜色:" + col };
                rect.MouseDown += (s, e) => { _currentColor = col; UpdateSettingsUI(); };
                _settingsBar.Children.Add(rect);
            }

            _settingsBar.Children.Add(new Separator { Margin = new Thickness(10, 0, 10, 0), Background = Brushes.DimGray, Width = 1 });
            _settingsBar.Children.Add(new TextBlock { Text = "线宽", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 });

            _thicknessSlider = new Slider { Minimum = 1, Maximum = 20, Value = _currentThickness, Width = 110, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), IsSnapToTickEnabled = true, TickFrequency = 1 };
            _thicknessSlider.ValueChanged += (s, e) => { _currentThickness = e.NewValue; UpdateSettingsUI(); };
            _settingsBar.Children.Add(_thicknessSlider);

            _thicknessValueText = new TextBlock { Text = _currentThickness.ToString("0"), Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, MinWidth = 24 };
            _settingsBar.Children.Add(_thicknessValueText);

            _settingsBar.Children.Add(new Separator { Margin = new Thickness(10, 0, 10, 0), Background = Brushes.DimGray, Width = 1 });
            _settingsBar.Children.Add(BuildMosaicEffectPanel());

            UpdateSettingsUI();
        }

        private StackPanel BuildMosaicEffectPanel()
        {
            _effectPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            _effectLabel = new TextBlock { Text = "马赛克", Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, FontSize = 10 };
            _effectPanel.Children.Add(_effectLabel);

            _effectSlider = new Slider { Minimum = 2, Maximum = 40, Value = _mosaicPixelSize, Width = 110, Margin = new Thickness(6, 0, 6, 0), IsSnapToTickEnabled = true, TickFrequency = 1 };
            _effectSlider.ValueChanged += (s, e) =>
            {
                _mosaicPixelSize = (int)Math.Round(e.NewValue);
                UpdateSettingsUI();
                if (!_effectSliderDragging) _mosaicRebuildLimiter.Debounce(TryRebuildMosaicBitmap);
            };
            _effectPanel.Children.Add(_effectSlider);

            _effectValueText = new TextBlock { Text = _mosaicPixelSize.ToString(), Foreground = Brushes.Gainsboro, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, MinWidth = 30 };
            _effectPanel.Children.Add(_effectValueText);

            _effectSlider.Loaded += (s, e) => HookSliderThumbDrag(_effectSlider);
            return _effectPanel;
        }

        private void HookSliderThumbDrag(Slider slider)
        {
            if (slider == null || _effectThumbHooked) return;
            _effectThumbHooked = true;
            var track = slider.Template.FindName("PART_Track", slider) as Track;
            if (track?.Thumb == null) return;
            track.Thumb.DragStarted += (s, e) => { _effectSliderDragging = true; };
            track.Thumb.DragCompleted += (s, e) => { _effectSliderDragging = false; TryRebuildMosaicBitmap(); };
        }

        private void UpdateSettingsUI()
        {
            if (_currentColorSwatch != null) _currentColorSwatch.Background = new SolidColorBrush(_currentColor);
            if (_toolNameText != null) _toolNameText.Text = "当前工具:" + GetToolDisplayName(_currentTool);
            if (_thicknessValueText != null) _thicknessValueText.Text = _currentThickness.ToString("0");

            if (_effectPanel == null) return;
            if (_currentTool == EditTool.Mosaic)
            {
                _effectPanel.Visibility = Visibility.Visible;
                _effectLabel.Text = "马赛克";
                if (_effectSlider != null && (int)Math.Round(_effectSlider.Value) != _mosaicPixelSize) _effectSlider.Value = _mosaicPixelSize;
                if (_effectValueText != null) _effectValueText.Text = _mosaicPixelSize.ToString();
            }
            else { _effectPanel.Visibility = Visibility.Collapsed; }
        }

        private void SetToolbarVisible(bool visible) { if (_toolBarBorder != null) _toolBarBorder.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; }

        private void AddToolBtn(object icon, string tip, EditTool tool)
        {
            var btn = UiFactory.ToolToggle(icon, tip, onClick: () =>
            {
                _currentTool = _currentTool == tool ? EditTool.None : tool;
                _settingsBar.Visibility = IsDrawingMode ? Visibility.Visible : Visibility.Collapsed;
                UpdateInteractionSurface();
                UpdateToolButtonStyles();
                UpdateSettingsUI();
            }, width: 38, height: 32);
            btn.Tag = tool; _mainToolbar.Children.Add(btn);
        }

        private void UpdateToolButtonStyles()
        {
            foreach (var child in _mainToolbar.Children)
            {
                var b = child as ToggleButton;
                if (b?.Tag == null) continue;
                b.IsChecked = (EditTool)b.Tag == _currentTool;
                b.Foreground = Brushes.White;
            }
        }

        private string BuildToolbarButtonToolTip(ToolbarButton def)
        {
            string text = def.ToolTip ?? "扩展按钮";
            string shortcut = FormatShortcutText(def.ShortcutModifiers, def.ShortcutKey);
            return string.IsNullOrEmpty(shortcut) ? text : text + " (" + shortcut + ")";
        }

        private static string FormatShortcutText(ModifierKeys modifiers, Key key)
        {
            if (key == Key.None) return null;
            string text = string.Empty;
            if ((modifiers & ModifierKeys.Control) != 0) text += "Ctrl+";
            if ((modifiers & ModifierKeys.Shift) != 0) text += "Shift+";
            if ((modifiers & ModifierKeys.Alt) != 0) text += "Alt+";
            if ((modifiers & ModifierKeys.Windows) != 0) text += "Win+";
            return text + key;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is TextBox) return;

            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; return; }
            if (e.Key == Key.Enter) { Accept(); e.Handled = true; return; }
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.None) { Copy(); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.None) { SaveAsImage(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { SafeUndo(); e.Handled = true; return; }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { SafeRedo(); e.Handled = true; return; }

            if (TryHandleExtraToolbarShortcut(e)) e.Handled = true;
        }

        private void OnMouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            Focus();
            if (_interactionMode == InteractionMode.ResizingSelection) return;

            if (e.OriginalSource is DependencyObject dep)
            {
                if (FindVisualParent<Thumb>(dep) != null) return;
                if (IsInToolbar(dep)) return;
                if (FindVisualParent<TextBox>(dep) != null) return;
                if (FindVisualParent<ButtonBase>(dep) != null) return;
            }

            Point p = e.GetPosition(this);

            if (IsDrawingMode)
            {
                if (!_selection.Contains(p)) return;
                StartDrawing(p);
                e.Handled = true;
                return;
            }

            if (_selection.Contains(p))
            {
                _interactionMode = InteractionMode.MovingSelection;
                _startPoint = p; _moveStartRect = _selection;
                CaptureMouse(); SetToolbarVisible(false);
                e.Handled = true;
            }
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_interactionMode == InteractionMode.ResizingSelection) return;
            Point p = e.GetPosition(this);
            UpdateCursor(p);

            if (_interactionMode == InteractionMode.MovingSelection)
            {
                Vector delta = p - _startPoint;
                double x = Clamp(_selection.X + delta.X, 0, _imgDipW - _selection.Width);
                double y = Clamp(_selection.Y + delta.Y, 0, _imgDipH - _selection.Height);
                _selection = new Rect(x, y, _selection.Width, _selection.Height);
                _startPoint = p;
                UpdateVisuals(true);
            }
            else if (_interactionMode == InteractionMode.Drawing) { UpdateDrawing(p); }
        }

        private void OnMouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle) { OnMouseMiddleButtonUp(sender, e); e.Handled = true; return; }
            if (e.ChangedButton != MouseButton.Left) return;
            if (_interactionMode == InteractionMode.ResizingSelection) return;

            if (_interactionMode == InteractionMode.MovingSelection)
            {
                _interactionMode = InteractionMode.None; ReleaseMouseCapture(); SetToolbarVisible(true);
                var before = _moveStartRect; var after = _selection;
                if (!RectEquals(before, after)) PushSelectionHistory("MoveSelection", before, after);
                UpdateVisuals(false); e.Handled = true; return;
            }

            if (_interactionMode == InteractionMode.Drawing)
            {
                EndDrawing(); UpdateVisuals(false); e.Handled = true;
            }
        }

        private void OnMouseMiddleButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (_opt?.MiddleClickHandler == null) return;
            try { if (_opt.MiddleClickHandler(this)) e.Handled = true; }
            catch (Exception ex) { _opt.OnError?.Invoke(ex); }
        }

        private void UpdateCursor(Point p)
        {
            if (!_selection.Contains(p)) { Cursor = Cursors.Arrow; return; }
            switch (_currentTool)
            {
                case EditTool.Text: Cursor = Cursors.IBeam; break;
                case EditTool.Pen:
                case EditTool.Mosaic: Cursor = Cursors.Pen; break;
                case EditTool.None: Cursor = Cursors.SizeAll; break;
                default: Cursor = Cursors.Cross; break;
            }
        }

        private bool TryHandleExtraToolbarShortcut(KeyEventArgs e)
        {
            if (_opt?.ExtraToolbarButtons == null || _opt.ExtraToolbarButtons.Count == 0) return false;
            if (Keyboard.FocusedElement is TextBox) return false;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            foreach (var btn in _opt.ExtraToolbarButtons)
            {
                if (btn == null || btn.ShortcutKey == Key.None) continue;
                if (btn.ShortcutKey == key && btn.ShortcutModifiers == modifiers)
                {
                    btn.OnClick?.Invoke(this); return true;
                }
            }
            return false;
        }

        private void StartDrawing(Point p)
        {
            if (!_selection.Contains(p)) return;

            _interactionMode = InteractionMode.Drawing;
            _startPoint = p; CaptureMouse(); SetToolbarVisible(false);

            if (_currentTool == EditTool.Rectangle)
            {
                _tempShape = new Rectangle { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = _currentThickness };
                _inkLayer.Children.Add(_tempShape); return;
            }
            if (_currentTool == EditTool.Arrow)
            {
                _tempShape = new Path { Stroke = new SolidColorBrush(_currentColor), StrokeThickness = Math.Max(1, _currentThickness), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
                _inkLayer.Children.Add(_tempShape); return;
            }
            if (_currentTool == EditTool.Pen)
            {
                _tempPolyline = CreatePolyline(new SolidColorBrush(_currentColor), _currentThickness);
                _tempPolyline.Points.Add(p); _inkLayer.Children.Add(_tempPolyline); return;
            }
            if (_currentTool == EditTool.Mosaic)
            {
                EnsureMosaicBitmap();
                var maskLine = CreatePolyline(Brushes.White, _opt.MosaicStrokeThickness);
                maskLine.Opacity = 1.0; maskLine.Points.Add(p); _tempPolyline = maskLine;
                _mosaicMask.Visibility = Visibility.Visible; _mosaicMask.Children.Add(maskLine); return;
            }
            if (_currentTool == EditTool.Number)
            {
                AddNumberTag(p); _interactionMode = InteractionMode.None; ReleaseMouseCapture(); SetToolbarVisible(true); return;
            }
            if (_currentTool == EditTool.Text)
            {
                ShowTextInput(p); _interactionMode = InteractionMode.None; ReleaseMouseCapture(); SetToolbarVisible(true);
            }
        }

        private void UpdateDrawing(Point p)
        {
            if (_tempShape is Rectangle rect)
            {
                SetLeft(rect, Math.Min(_startPoint.X, p.X)); SetTop(rect, Math.Min(_startPoint.Y, p.Y));
                rect.Width = Math.Abs(p.X - _startPoint.X); rect.Height = Math.Abs(p.Y - _startPoint.Y); return;
            }
            if (_tempShape is Path arrowPath) { arrowPath.Data = BuildArrowGeometry(_startPoint, p, 18, 28); return; }
            if (_tempPolyline != null) _tempPolyline.Points.Add(p);
        }

        private void EndDrawing()
        {
            _interactionMode = InteractionMode.None; ReleaseMouseCapture(); SetToolbarVisible(true);

            if (_currentTool == EditTool.Rectangle || _currentTool == EditTool.Arrow || _currentTool == EditTool.Pen)
            {
                UIElement final = (UIElement)_tempShape ?? _tempPolyline;
                if (final != null)
                {
                    _inkLayer.Children.Remove(final); var layer = _inkLayer;
                    _history.Execute(new DelegateUndoableCommand("Add " + _currentTool, redo: () => layer.Children.Add(final), undo: () => layer.Children.Remove(final)));
                }
                _tempShape = null; _tempPolyline = null; return;
            }
            if (_currentTool == EditTool.Mosaic)
            {
                var line = _tempPolyline; _tempPolyline = null;
                if (line == null) return;
                var host = _mosaicMask; host.Children.Remove(line);
                _history.Execute(new DelegateUndoableCommand("Add Mosaic",
                    redo: () => { host.Visibility = Visibility.Visible; host.Children.Add(line); },
                    undo: () => { host.Children.Remove(line); if (host.Children.Count == 0) host.Visibility = Visibility.Collapsed; }));
            }
        }

        private Polyline CreatePolyline(Brush stroke, double thickness) => new Polyline { Stroke = stroke, StrokeThickness = Math.Max(1, thickness), StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

        private Geometry BuildArrowGeometry(Point a, Point b, double headLength, double headAngleDeg)
        {
            Vector v = a - b; if (v.Length < 2) v = new Vector(2, 0); v.Normalize();
            double rad = headAngleDeg * Math.PI / 180.0;
            Vector left = Rotate(v, rad) * headLength; Vector right = Rotate(v, -rad) * headLength;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(a, false, false); ctx.LineTo(b, true, false);
                ctx.BeginFigure(b + left, false, false); ctx.LineTo(b, true, false);
                ctx.BeginFigure(b + right, false, false); ctx.LineTo(b, true, false);
            }
            geo.Freeze(); return geo;
        }

        private static Vector Rotate(Vector v, double rad)
        {
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }

        private void AddNumberTag(Point p)
        {
            int myNumber = _sequenceNumber;
            var border = new Border { Background = new SolidColorBrush(_currentColor), CornerRadius = new CornerRadius(11), Width = 22, Height = 22, Child = new TextBlock { Text = myNumber.ToString(), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, FontSize = 12 } };
            SetLeft(border, p.X - 11); SetTop(border, p.Y - 11);
            _history.Execute(new DelegateUndoableCommand("Number", redo: () => { _inkLayer.Children.Add(border); _sequenceNumber = Math.Max(_sequenceNumber, myNumber + 1); }, undo: () => _inkLayer.Children.Remove(border)));
        }

        private void ShowTextInput(Point p)
        {
            if (!_selection.Contains(p)) return;

            var input = new TextBox { MinWidth = 50, Background = Brushes.Transparent, Foreground = new SolidColorBrush(_currentColor), CaretBrush = new SolidColorBrush(_currentColor), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(_currentColor), FontSize = 16 };
            SetLeft(input, p.X); SetTop(input, p.Y);
            _inkLayer.Children.Add(input); _liveTextInputs.Add(input); input.Focus(); input.SelectAll();

            bool completed = false;
            Action commitOrDiscard = () =>
            {
                if (completed) return; completed = true;
                if (input.Parent != _inkLayer) return;
                _inkLayer.Children.Remove(input); _liveTextInputs.Remove(input);
                var text = input.Text; if (string.IsNullOrWhiteSpace(text)) return;

                var tb = new TextBlock { Text = text, Foreground = new SolidColorBrush(_currentColor), FontSize = 16 };

                // 增强渲染质量:使用高清晰度的像素对齐抗锯齿
                TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
                TextOptions.SetTextHintingMode(tb, TextHintingMode.Fixed);

                SetLeft(tb, p.X); SetTop(tb, p.Y);
                _history.Execute(new DelegateUndoableCommand("Text", redo: () => _inkLayer.Children.Add(tb), undo: () => _inkLayer.Children.Remove(tb)));
            };

            input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { commitOrDiscard(); e.Handled = true; }
                else if (e.Key == Key.Escape)
                {
                    if (completed) { e.Handled = true; return; }
                    completed = true; if (input.Parent == _inkLayer) _inkLayer.Children.Remove(input);
                    _liveTextInputs.Remove(input); e.Handled = true;
                }
            };
            input.LostKeyboardFocus += (s, e) => commitOrDiscard();
        }

        private void CleanupTransientTextInputs()
        {
            for (int i = _liveTextInputs.Count - 1; i >= 0; i--)
            {
                var tb = _liveTextInputs[i];
                if (tb == null) { _liveTextInputs.RemoveAt(i); continue; }
                if (tb.Parent == _inkLayer && string.IsNullOrWhiteSpace(tb.Text)) _inkLayer.Children.Remove(tb);
                _liveTextInputs.RemoveAt(i);
            }
        }

        private void EnsureMosaicBitmap()
        {
            if (_mosaicBitmap == null && _fullImage != null)
            {
                _mosaicBitmap = CreateMosaicBitmap(_fullImage, _mosaicPixelSize);
                _lastMosaicPixelSize = _mosaicPixelSize; _mosaicImage.Source = _mosaicBitmap;
            }
        }

        private bool NeedsRebuildMosaicBitmap()
        {
            if (_effectSliderDragging) return false;
            if (_mosaicMask == null || _mosaicMask.Children.Count == 0) return false;
            if (_fullImage == null) return false;
            if (_mosaicBitmap != null && _lastMosaicPixelSize == _mosaicPixelSize) return false;
            return true;
        }

        private void TryRebuildMosaicBitmap()
        {
            try
            {
                if (!NeedsRebuildMosaicBitmap()) return;
                _lastMosaicPixelSize = _mosaicPixelSize;
                _mosaicBitmap = CreateMosaicBitmap(_fullImage, _mosaicPixelSize);
                _mosaicImage.Source = _mosaicBitmap;
            }
            catch (Exception ex) { _opt?.OnError?.Invoke(ex); }
        }

        private static BitmapSource CreateMosaicBitmap(BitmapSource source, int pixelSize)
        {
            pixelSize = Math.Max(2, pixelSize); int w = source.PixelWidth, h = source.PixelHeight; double s = 1.0 / pixelSize;
            var small = new TransformedBitmap(source, new ScaleTransform(s, s)); small.Freeze();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) { RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.NearestNeighbor); dc.DrawImage(small, new Rect(0, 0, w, h)); }
            double dpiX = source.DpiX > 0 ? source.DpiX : 96; double dpiY = source.DpiY > 0 ? source.DpiY : 96;
            var rtb = new RenderTargetBitmap(w, h, dpiX, dpiY, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze(); return rtb;
        }

        private void UpdateVisuals(bool lightweight)
        {
            if (_maskHole != null) _maskHole.Rect = _selection;
            if (_inlineMaskHole != null) _inlineMaskHole.Rect = _selection;
            _clipRect.Rect = _selection;

            SetLeft(_selectionBorder, _selection.X); SetTop(_selectionBorder, _selection.Y);
            _selectionBorder.Width = _selection.Width; _selectionBorder.Height = _selection.Height;
            PositionThumbs();

            _selectionPixelRect = GetSelectionPixelRectCore();
            UpdateInfoText();
            if (!lightweight) UpdateToolbarPosition();

            if (_selectionBorder != null) _selectionBorder.Visibility = _selectionBorderVisible ? Visibility.Visible : Visibility.Collapsed;
            if (_infoBorder != null) _infoBorder.Visibility = _infoBadgeVisible ? Visibility.Visible : Visibility.Collapsed;

            foreach (var thumb in _thumbMap.Keys) thumb.Visibility = _thumbsVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateToolbarPosition()
        {
            if (_toolBarBorder == null || _toolBarBorder.Visibility != Visibility.Visible) return;
            _toolBarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Size size = _toolBarBorder.DesiredSize;
            Rect[] candidates = { new Rect(_selection.Left, _selection.Bottom + 10, size.Width, size.Height), new Rect(_selection.Left, _selection.Top - size.Height - 10, size.Width, size.Height), new Rect(_selection.Right + 10, _selection.Top, size.Width, size.Height), new Rect(_selection.Left - size.Width - 10, _selection.Top, size.Width, size.Height) };
            Rect screen = new Rect(0, 0, _imgDipW, _imgDipH); Rect final = Rect.Empty;
            foreach (var c in candidates) { if (screen.Contains(c)) { final = c; break; } }
            if (final == Rect.Empty) final = candidates[0];
            SetLeft(_toolBarBorder, Clamp(final.X, 0, _imgDipW - size.Width)); SetTop(_toolBarBorder, Clamp(final.Y, 0, _imgDipH - size.Height));
        }

        private void UpdateInfoText()
        {
            if (_infoBorder == null || _infoText == null) return;
            _infoText.Text = string.Format("{0},{1}  {2}×{3}", _selectionPixelRect.X, _selectionPixelRect.Y, _selectionPixelRect.Width, _selectionPixelRect.Height);
            _infoBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Size s = _infoBorder.DesiredSize;
            const double margin = 6; double outX = _selection.Left, outY = _selection.Top - s.Height - margin;
            if (outY < 0) { outX = _selection.Left - s.Width - margin; outY = _selection.Top; }
            bool outOfBounds = outX < 0 || outY < 0 || outX + s.Width > _imgDipW || outY + s.Height > _imgDipH;
            SetLeft(_infoBorder, outOfBounds ? Clamp(_selection.Left + margin, 0, _imgDipW - s.Width) : outX);
            SetTop(_infoBorder, outOfBounds ? Clamp(_selection.Top + margin, 0, _imgDipH - s.Height) : outY);
        }

        private Int32Rect GetSelectionPixelRectCore()
        {
            Int32Rect r = DpiHelper.DipToPxInt32Rect(_selection, this);
            int x = r.X, y = r.Y, w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) return new Int32Rect(0, 0, 0, 0);
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x >= _imgPxW || y >= _imgPxH) return new Int32Rect(0, 0, 0, 0);
            if (x + w > _imgPxW) w = _imgPxW - x; if (y + h > _imgPxH) h = _imgPxH - y;
            if (w <= 0 || h <= 0) return new Int32Rect(0, 0, 0, 0);
            return new Int32Rect(x + _canvasPixelBounds.X, y + _canvasPixelBounds.Y, w, h);
        }

        private void PositionThumbs()
        {
            double l = _selection.Left, t = _selection.Top, r = _selection.Right, b = _selection.Bottom, cx = (l + r) / 2, cy = (t + b) / 2;
            foreach (var kv in _thumbMap)
            {
                double x, y;
                switch (kv.Value) { case ThumbHit.N: x = cx; y = t; break; case ThumbHit.S: x = cx; y = b; break; case ThumbHit.E: x = r; y = cy; break; case ThumbHit.W: x = l; y = cy; break; case ThumbHit.NE: x = r; y = t; break; case ThumbHit.NW: x = l; y = t; break; case ThumbHit.SE: x = r; y = b; break; case ThumbHit.SW: x = l; y = b; break; default: x = cx; y = cy; break; }
                SetLeft(kv.Key, x - kv.Key.Width / 2); SetTop(kv.Key, y - kv.Key.Height / 2);
            }
        }

        private void ApplyResize(ThumbHit hit, double dx, double dy, ref Rect r)
        {
            double left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
            switch (hit) { case ThumbHit.N: top += dy; break; case ThumbHit.S: bottom += dy; break; case ThumbHit.E: right += dx; break; case ThumbHit.W: left += dx; break; case ThumbHit.NE: top += dy; right += dx; break; case ThumbHit.NW: top += dy; left += dx; break; case ThumbHit.SE: bottom += dy; right += dx; break; case ThumbHit.SW: bottom += dy; left += dx; break; }
            if (right - left < MinSelDip) { if (hit == ThumbHit.W || hit == ThumbHit.NW || hit == ThumbHit.SW) left = right - MinSelDip; else right = left + MinSelDip; }
            if (bottom - top < MinSelDip) { if (hit == ThumbHit.N || hit == ThumbHit.NW || hit == ThumbHit.NE) top = bottom - MinSelDip; else bottom = top + MinSelDip; }
            r = new Rect(new Point(left, top), new Point(right, bottom));
        }

        private Rect NormalizeRect(Rect r)
        {
            if (double.IsNaN(r.X) || double.IsNaN(r.Y) || double.IsNaN(r.Width) || double.IsNaN(r.Height)) return new Rect(0, 0, _imgDipW, _imgDipH);
            double x = r.Width < 0 ? r.X + r.Width : r.X, y = r.Height < 0 ? r.Y + r.Height : r.Y;
            double w = Math.Max(MinSelDip, Math.Min(Math.Abs(r.Width), _imgDipW)), h = Math.Max(MinSelDip, Math.Min(Math.Abs(r.Height), _imgDipH));
            return new Rect(Clamp(x, 0, _imgDipW - w), Clamp(y, 0, _imgDipH - h), w, h);
        }

        private static bool RectEquals(Rect a, Rect b)
        {
            const double eps = 0.0001; return Math.Abs(a.X - b.X) < eps && Math.Abs(a.Y - b.Y) < eps && Math.Abs(a.Width - b.Width) < eps && Math.Abs(a.Height - b.Height) < eps;
        }

        private Int32Rect GetValidSelectionPixelRect()
        {
            _selectionPixelRect = GetSelectionPixelRectCore();
            return (_selectionPixelRect.Width <= 0 || _selectionPixelRect.Height <= 0) ? new Int32Rect(0, 0, 0, 0) : _selectionPixelRect;
        }

        private BitmapSource RenderSelectionComposite(Int32Rect cropRect)
        {
            if (_fullImage == null) return CaptureCurrentSelectionFromScreen();

            var localCropRect = new Int32Rect(cropRect.X - _canvasPixelBounds.X, cropRect.Y - _canvasPixelBounds.Y, cropRect.Width, cropRect.Height);
            var croppedBase = new CroppedBitmap(_fullImage, localCropRect); croppedBase.Freeze();

            bool hasMosaic = _mosaicMask != null && _mosaicMask.Children.Count > 0;
            CroppedBitmap croppedMosaic = null;
            if (hasMosaic) { EnsureMosaicBitmap(); if (_mosaicBitmap != null) { croppedMosaic = new CroppedBitmap(_mosaicBitmap, localCropRect); croppedMosaic.Freeze(); } }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawImage(croppedBase, new Rect(0, 0, _selection.Width, _selection.Height));
                if (croppedMosaic != null) { var mask = new VisualBrush(_mosaicMask) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = _selection, Stretch = Stretch.Fill }; dc.PushOpacityMask(mask); dc.DrawImage(croppedMosaic, new Rect(0, 0, _selection.Width, _selection.Height)); dc.Pop(); }
                var ink = new VisualBrush(_inkLayer) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = _selection, Stretch = Stretch.Fill };
                dc.DrawRectangle(ink, null, new Rect(0, 0, _selection.Width, _selection.Height));
            }

            var dpi = DpiHelper.GetDpi(this);
            var rtb = new RenderTargetBitmap(cropRect.Width, cropRect.Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze(); return rtb;
        }

        private BitmapSource CaptureCurrentSelectionFromScreen()
        {
            Int32Rect rectPx = SelectionPixelRect; if (rectPx.Width <= 0 || rectPx.Height <= 0) return null;

            SetSelectionBorderVisible(false); SetThumbsVisible(false); SetInfoBadgeVisible(false); SetToolbarVisible(false);
            if (_ghostWindow != null) _ghostWindow.Visibility = Visibility.Hidden;

            Dispatcher.Invoke(delegate { }, DispatcherPriority.Render); NativeMethods.DwmFlush();

            try
            {
                using (var bmp = ScreenCapture.CaptureScreenBounds(new System.Drawing.Rectangle(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height)))
                {
                    IntPtr hBitmap = bmp.GetHbitmap();
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); source.Freeze(); return source;
                    }
                    finally { NativeMethods.DeleteObject(hBitmap); }
                }
            }
            finally
            {
                SetSelectionBorderVisible(_selectionBorderVisible); SetThumbsVisible(_thumbsVisible); SetInfoBadgeVisible(_infoBadgeVisible); SetToolbarVisible(true);
                if (_ghostWindow != null && _maskVisible) _ghostWindow.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// 将当前选区(含所有标注)导出为位图。
        /// </summary>
        public BitmapSource ExportWithAnnotations()
        {
            Int32Rect cropRect = GetValidSelectionPixelRect(); if (cropRect.Width <= 0 || cropRect.Height <= 0) return null;
            try { return RenderSelectionComposite(cropRect); } catch (Exception ex) { _opt?.OnError?.Invoke(ex); return null; }
        }

        private bool TryExport(out BitmapSource bmp)
        {
            CleanupTransientTextInputs(); bmp = null;
            try { bmp = ExportWithAnnotations(); return bmp != null; }
            catch (Exception ex) { _opt?.OnError?.Invoke(ex); return false; }
        }

        private void RequestClose()
        {
            CloseGhostWindow();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void SaveAsImage()
        {
            if (!TryExport(out BitmapSource bmp)) return;
            try
            {
                if (_opt.SaveHandler != null && _opt.SaveHandler(this, bmp)) { RequestClose(); return; }
                var dlg = new Microsoft.Win32.SaveFileDialog { Title = "保存截图", Filter = "PNG 图片 (*.png)|*.png|JPG 图片 (*.jpg)|*.jpg", FileName = "Screenshot.png", AddExtension = true, OverwritePrompt = true };
                if (dlg.ShowDialog() != true) return;
                using (var fs = System.IO.File.Create(dlg.FileName))
                {
                    if (dlg.FilterIndex == 2) { var enc = new JpegBitmapEncoder { QualityLevel = 92 }; enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(fs); }
                    else { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(bmp)); enc.Save(fs); }
                }
                RequestClose();
            }
            catch (Exception ex) { _opt?.OnError?.Invoke(ex); }
        }

        public void Accept()
        {
            if (_finished) return;
            if (!TryExport(out BitmapSource bmp)) return;
            _finished = true;
            CloseGhostWindow();
            Accepted?.Invoke(this, bmp);
        }

        private void Copy()
        {
            try { if (!TryExport(out BitmapSource bmp)) return; Clipboard.SetImage(bmp); RequestClose(); }
            catch (Exception ex) { _opt?.OnError?.Invoke(ex); }
        }

        public void Cancel()
        {
            if (!_finished)
            {
                _finished = true;
                CloseGhostWindow();
                Canceled?.Invoke(this, EventArgs.Empty);
            }
        }

        private void PushSelectionHistory(string name, Rect before, Rect after)
        {
            _history.Execute(new DelegateUndoableCommand(name, redo: () => { _selection = after; UpdateVisuals(false); }, undo: () => { _selection = before; UpdateVisuals(false); }));
        }

        private void ClearAllAnnotations()
        {
            CleanupTransientTextInputs();
            var ink = _inkLayer.Children.Cast<UIElement>().ToList(); var mm = _mosaicMask.Children.Cast<UIElement>().ToList(); int oldSeq = _sequenceNumber;
            _history.Execute(new DelegateUndoableCommand("ClearAll",
                redo: () => { _inkLayer.Children.Clear(); _mosaicMask.Children.Clear(); _mosaicMask.Visibility = Visibility.Collapsed; _sequenceNumber = 1; },
                undo: () => { foreach (var e in ink) _inkLayer.Children.Add(e); foreach (var e in mm) _mosaicMask.Children.Add(e); _mosaicMask.Visibility = _mosaicMask.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed; _sequenceNumber = oldSeq; }));
        }

        private void SafeUndo() { try { _history.Undo(); } catch (Exception ex) { _opt?.OnError?.Invoke(ex); } }
        private void SafeRedo() { try { _history.Redo(); } catch (Exception ex) { _opt?.OnError?.Invoke(ex); } }

        private ContextMenu BuildDefaultContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "撤销 (Ctrl+Z)", Command = new RelayCommand(_ => SafeUndo()) });
            menu.Items.Add(new MenuItem { Header = "重做 (Ctrl+Y)", Command = new RelayCommand(_ => SafeRedo()) });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "清空标注", Command = new RelayCommand(_ => ClearAllAnnotations()) });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "取消 (Esc)", Command = new RelayCommand(_ => Cancel()) });
            return menu;
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object> _act;
            public RelayCommand(Action<object> act) { _act = act; }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => _act(parameter);
            public event EventHandler CanExecuteChanged { add { } remove { } }
        }

        private static Brush CreateInteractiveTransparentBrush()
        {
            var b = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)); b.Freeze(); return b;
        }

        private void UpdateInteractionSurface()
        {
            if (HasBackgroundImage) { Background = Brushes.Transparent; return; }
            Background = IsDrawingMode ? InteractiveTransparentBrush : null;
        }

        private static double Clamp(double v, double min, double max) => Math.Max(min, Math.Min(max, v));

        private static T FindVisualParent<T>(DependencyObject obj) where T : DependencyObject
        {
            while (obj != null) { if (obj is T hit) return hit; obj = VisualTreeHelper.GetParent(obj); }
            return null;
        }

        private bool IsInToolbar(DependencyObject obj)
        {
            while (obj != null) { if (ReferenceEquals(obj, _toolBarBorder)) return true; obj = VisualTreeHelper.GetParent(obj); }
            return false;
        }
    }

    #endregion
}
