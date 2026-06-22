using KkjQuicker.Overlay.Engine;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KkjQuicker.Overlay.Applications
{
    /// <summary>
    /// 表示基于 <see cref="OverlayEngine"/> 的窗口外侧侧边栏停靠方向。
    /// </summary>
    public enum OverlaySidebarDock
    {
        /// <summary>
        /// 停靠在宿主窗口左侧。
        /// </summary>
        Left,

        /// <summary>
        /// 停靠在宿主窗口右侧。
        /// </summary>
        Right,

        /// <summary>
        /// 停靠在宿主窗口上方。
        /// </summary>
        Top,

        /// <summary>
        /// 停靠在宿主窗口下方。
        /// </summary>
        Bottom
    }

    /// <summary>
    /// 表示侧边栏在非停靠轴方向上的对齐方式。
    /// </summary>
    public enum OverlaySidebarAlignment
    {
        /// <summary>
        /// 起始对齐。
        /// </summary>
        Start,

        /// <summary>
        /// 居中对齐。
        /// </summary>
        Center,

        /// <summary>
        /// 末端对齐。
        /// </summary>
        End,

        /// <summary>
        /// 拉伸对齐。
        /// </summary>
        Stretch
    }

    /// <summary>
    /// 表示 Overlay 侧边栏的布局与外观状态。
    /// <para>
    /// 该类型仅保存侧边栏自身的布局与视觉配置，
    /// 不包含 Overlay 宿主级行为（如置顶策略）。
    /// </para>
    /// </summary>
    public sealed class OverlaySidebarState
    {
        /// <summary>
        /// 获取或设置停靠方向。
        /// </summary>
        public OverlaySidebarDock Dock { get; set; } = OverlaySidebarDock.Right;

        /// <summary>
        /// 获取或设置对齐方式。
        /// </summary>
        public OverlaySidebarAlignment Alignment { get; set; } = OverlaySidebarAlignment.Stretch;

        /// <summary>
        /// 获取或设置侧边栏宽度（DIP）。
        /// <para>
        /// 对于 <see cref="OverlaySidebarDock.Left"/> / <see cref="OverlaySidebarDock.Right"/> 模式使用。
        /// </para>
        /// </summary>
        public double Width { get; set; } = 280;

        /// <summary>
        /// 获取或设置侧边栏高度（DIP）。
        /// <para>
        /// 对于 <see cref="OverlaySidebarDock.Top"/> / <see cref="OverlaySidebarDock.Bottom"/> 模式使用。
        /// </para>
        /// </summary>
        public double Height { get; set; } = 180;

        /// <summary>
        /// 获取或设置侧边栏与宿主窗口之间的间距（DIP）。
        /// </summary>
        public double Gap { get; set; }

        /// <summary>
        /// 获取或设置侧边栏背景画刷。
        /// <para>
        /// 为 <see langword="null"/> 时使用默认背景。
        /// </para>
        /// </summary>
        public Brush Background { get; set; }

        /// <summary>
        /// 获取或设置侧边栏边框画刷。
        /// <para>
        /// 为 <see langword="null"/> 时使用默认边框。
        /// </para>
        /// </summary>
        public Brush BorderBrush { get; set; }

        /// <summary>
        /// 获取或设置侧边栏边框厚度。
        /// </summary>
        public Thickness BorderThickness { get; set; } = new Thickness(1);

        /// <summary>
        /// 创建当前状态的浅拷贝。
        /// </summary>
        /// <returns>复制后的状态对象。</returns>
        public OverlaySidebarState Clone()
        {
            return new OverlaySidebarState
            {
                Dock = Dock,
                Alignment = Alignment,
                Width = Width,
                Height = Height,
                Gap = Gap,
                Background = Background,
                BorderBrush = BorderBrush,
                BorderThickness = BorderThickness
            };
        }
    }

    /// <summary>
    /// 基于 <see cref="OverlayEngine"/> 的窗口外侧侧边栏宿主。
    /// <para>
    /// 该类型用于将一个任意 <see cref="UIElement"/> 紧贴在 WPF 宿主窗口外侧显示，
    /// 并支持在运行时更新布局、外观与可见状态。
    /// </para>
    /// </summary>
    public sealed class OverlaySidebarHost : IDisposable
    {
        private readonly Window _owner;
        private readonly SidebarLayer _layer;
        private readonly OverlaySidebarState _state;

        private OverlayEngine _overlay;
        private IDisposable _layerToken;
        private UIElement _content;
        private bool _disposed;

        private bool _topMost = true;
        private bool _forceTopMostViaWin32;

        /// <summary>
        /// 初始化一个新的 <see cref="OverlaySidebarHost"/> 实例。
        /// </summary>
        /// <param name="owner">宿主窗口。</param>
        /// <param name="content">初始侧边栏内容。允许为 <see langword="null"/>。</param>
        /// <param name="state">初始状态。传入 <see langword="null"/> 时使用默认配置。</param>
        public OverlaySidebarHost(Window owner, UIElement content = null, OverlaySidebarState state = null)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            _owner = owner;
            _content = content;
            _state = (state ?? new OverlaySidebarState()).Clone();

            ValidateState(_state);

            _layer = new SidebarLayer(this);
            _overlay = CreateOverlay();
            ApplyChrome();
        }

        /// <summary>
        /// 获取宿主窗口。
        /// </summary>
        public Window Owner
        {
            get { return _owner; }
        }

        /// <summary>
        /// 获取当前是否处于打开状态。
        /// </summary>
        public bool IsOpen
        {
            get { return _layerToken != null; }
        }

        /// <summary>
        /// 获取或设置当前侧边栏内容。
        /// <para>
        /// 可设置为 <see langword="null"/>，表示显示空内容容器。
        /// </para>
        /// <para>
        /// 若当前已打开，则新内容会立即应用；若当前未打开，则在下次打开时生效。
        /// </para>
        /// </summary>
        public UIElement Content
        {
            get { return _content; }
            set
            {
                ThrowIfDisposed();

                if (ReferenceEquals(_content, value))
                    return;

                _content = value;
                _layer.SetContent(_content);
                ApplyLayout();
            }
        }

        /// <summary>
        /// 获取或设置停靠方向。
        /// <para>
        /// 修改后会按当前状态重建内部 Overlay，并在必要时恢复打开状态。
        /// </para>
        /// </summary>
        public OverlaySidebarDock Dock
        {
            get { return _state.Dock; }
            set
            {
                ThrowIfDisposed();

                if (_state.Dock == value)
                    return;

                _state.Dock = value;
                RebuildOverlay();
                ApplyLayout();
            }
        }

        /// <summary>
        /// 获取或设置对齐方式。
        /// <para>
        /// 修改后会重新应用当前布局；若当前未打开，则在下次打开时生效。
        /// </para>
        /// </summary>
        public OverlaySidebarAlignment Alignment
        {
            get { return _state.Alignment; }
            set
            {
                ThrowIfDisposed();

                if (_state.Alignment == value)
                    return;

                _state.Alignment = value;
                ApplyLayout();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏宽度（DIP）。
        /// <para>
        /// 修改后会按当前状态重建内部 Overlay，并在必要时恢复打开状态。
        /// </para>
        /// </summary>
        public double SidebarWidth
        {
            get { return _state.Width; }
            set
            {
                ThrowIfDisposed();

                if (NearlyEquals(_state.Width, value))
                    return;

                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _state.Width = value;
                RebuildOverlay();
                ApplyLayout();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏高度（DIP）。
        /// <para>
        /// 修改后会按当前状态重建内部 Overlay，并在必要时恢复打开状态。
        /// </para>
        /// </summary>
        public double SidebarHeight
        {
            get { return _state.Height; }
            set
            {
                ThrowIfDisposed();

                if (NearlyEquals(_state.Height, value))
                    return;

                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _state.Height = value;
                RebuildOverlay();
                ApplyLayout();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏与宿主窗口之间的间距（DIP）。
        /// <para>
        /// 修改后会按当前状态重建内部 Overlay，并在必要时恢复打开状态。
        /// </para>
        /// </summary>
        public double Gap
        {
            get { return _state.Gap; }
            set
            {
                ThrowIfDisposed();

                if (NearlyEquals(_state.Gap, value))
                    return;

                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                _state.Gap = value;
                RebuildOverlay();
                ApplyLayout();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏背景画刷。
        /// <para>
        /// 修改后会立即应用到内部视图；若当前未打开，则在下次打开时生效。
        /// </para>
        /// </summary>
        public Brush Background
        {
            get { return _state.Background; }
            set
            {
                ThrowIfDisposed();

                if (ReferenceEquals(_state.Background, value))
                    return;

                _state.Background = value;
                ApplyChrome();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏边框画刷。
        /// <para>
        /// 修改后会立即应用到内部视图；若当前未打开，则在下次打开时生效。
        /// </para>
        /// </summary>
        public Brush BorderBrush
        {
            get { return _state.BorderBrush; }
            set
            {
                ThrowIfDisposed();

                if (ReferenceEquals(_state.BorderBrush, value))
                    return;

                _state.BorderBrush = value;
                ApplyChrome();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏边框厚度。
        /// <para>
        /// 修改后会立即应用到内部视图；若当前未打开，则在下次打开时生效。
        /// </para>
        /// </summary>
        public Thickness BorderThickness
        {
            get { return _state.BorderThickness; }
            set
            {
                ThrowIfDisposed();

                if (_state.BorderThickness == value)
                    return;

                _state.BorderThickness = value;
                ApplyChrome();
            }
        }

        /// <summary>
        /// 获取或设置 Overlay 是否置顶。
        /// <para>
        /// 修改后会重建内部 Overlay，以确保行为一致。
        /// </para>
        /// </summary>
        public bool TopMost
        {
            get { return _topMost; }
            set
            {
                ThrowIfDisposed();

                if (_topMost == value)
                    return;

                _topMost = value;
                RebuildOverlay();
            }
        }

        /// <summary>
        /// 获取或设置当 <see cref="TopMost"/> 为 <see langword="true"/> 时，
        /// 是否额外通过 Win32 强制声明一次置顶。
        /// <para>
        /// 修改后会重建内部 Overlay，以确保行为一致。
        /// </para>
        /// </summary>
        public bool ForceTopMostViaWin32
        {
            get { return _forceTopMostViaWin32; }
            set
            {
                ThrowIfDisposed();

                if (_forceTopMostViaWin32 == value)
                    return;

                _forceTopMostViaWin32 = value;
                RebuildOverlay();
            }
        }

        /// <summary>
        /// 打开侧边栏。
        /// <para>
        /// 若当前已处于打开状态，则该调用会被忽略。
        /// </para>
        /// </summary>
        public void Open()
        {
            ThrowIfDisposed();

            if (_layerToken != null)
                return;

            EnsureOverlayCreated();

            _layer.SetContent(_content);
            ApplyChrome();

            _layerToken = _overlay.Push(_layer);
            ApplyLayout();
        }

        /// <summary>
        /// 关闭侧边栏。
        /// <para>
        /// 若当前未打开，则该调用会被忽略。
        /// </para>
        /// </summary>
        public void Close()
        {
            if (_layerToken == null)
                return;

            try
            {
                _layerToken.Dispose();
            }
            finally
            {
                _layerToken = null;
            }
        }

        /// <summary>
        /// 切换打开状态。
        /// </summary>
        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        /// <summary>
        /// 批量更新布局相关配置。
        /// <para>
        /// 当停靠方向、宽度、高度或间距变化时，会重建内部 Overlay；
        /// 对齐方式变化仅重新应用布局。
        /// </para>
        /// </summary>
        /// <param name="dock">停靠方向。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="width">侧边栏宽度（DIP）。</param>
        /// <param name="height">侧边栏高度（DIP）。</param>
        /// <param name="gap">与宿主窗口之间的间距（DIP）。</param>
        public void UpdateLayout(
            OverlaySidebarDock dock,
            OverlaySidebarAlignment alignment,
            double width,
            double height,
            double gap)
        {
            ThrowIfDisposed();

            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            if (gap < 0)
                throw new ArgumentOutOfRangeException(nameof(gap));

            bool overlayShapeChanged =
                _state.Dock != dock ||
                !NearlyEquals(_state.Width, width) ||
                !NearlyEquals(_state.Height, height) ||
                !NearlyEquals(_state.Gap, gap);

            _state.Dock = dock;
            _state.Alignment = alignment;
            _state.Width = width;
            _state.Height = height;
            _state.Gap = gap;

            if (overlayShapeChanged)
                RebuildOverlay();

            ApplyLayout();
        }

        /// <summary>
        /// 批量更新外观相关配置。
        /// <para>
        /// 修改后会立即应用到内部视图；若当前未打开，则在下次打开时生效。
        /// </para>
        /// </summary>
        /// <param name="background">背景画刷。</param>
        /// <param name="borderBrush">边框画刷。</param>
        /// <param name="borderThickness">边框厚度。</param>
        public void UpdateChrome(
            Brush background,
            Brush borderBrush,
            Thickness borderThickness)
        {
            ThrowIfDisposed();

            _state.Background = background;
            _state.BorderBrush = borderBrush;
            _state.BorderThickness = borderThickness;

            ApplyChrome();
        }

        /// <summary>
        /// 根据当前状态重新应用布局。
        /// <para>
        /// 该方法依赖当前 Layer 已附着，且存在可用的 OwnerBounds。
        /// </para>
        /// <para>
        /// 若当前尚未打开、当前未附着，则本次调用会被忽略；
        /// 若当前已附着但 OwnerBounds 不可用，或计算结果无效，则会清空当前布局矩形。
        /// </para>
        /// </summary>
        public void ApplyLayout()
        {
            ThrowIfDisposed();

            if (!_layer.IsAttached)
                return;

            Rect ownerBounds = _layer.CurrentOwnerBounds;
            if (ownerBounds.IsEmpty)
            {
                _layer.ClearBounds();
                return;
            }

            Rect sidebarBounds = OverlaySidebarLayout.CalculateSidebarBounds(
                ownerBounds,
                _state.Dock,
                _state.Alignment,
                _state.Width,
                _state.Height,
                _state.Gap);

            if (sidebarBounds.IsEmpty)
            {
                _layer.ClearBounds();
                return;
            }

            _layer.ApplyBounds(sidebarBounds);
        }

        /// <summary>
        /// 根据当前状态重新应用外观。
        /// <para>
        /// 该方法不要求当前侧边栏已打开。
        /// 若当前尚未打开，则外观会先应用到内部视图，并在下次打开时直接生效。
        /// </para>
        /// </summary>
        public void ApplyChrome()
        {
            ThrowIfDisposed();

            _layer.ApplyChrome(
                _state.Background,
                _state.BorderBrush,
                _state.BorderThickness);
        }

        /// <summary>
        /// 释放当前宿主及相关资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            CloseAndReleaseOverlay();
        }

        private void EnsureOverlayCreated()
        {
            if (_overlay != null)
                return;

            _overlay = CreateOverlay();
        }

        private void CloseAndReleaseOverlay()
        {
            Close();

            if (_overlay != null)
            {
                _overlay.Dispose();
                _overlay = null;
            }
        }

        private OverlayEngine CreateOverlay()
        {
            Thickness extend = OverlaySidebarLayout.CalculateOverlayExtend(
                _state.Dock,
                _state.Width,
                _state.Height,
                _state.Gap);

            return new OverlayEngine(
                new OverlayOptions
                {
                    Placement = OverlayPlacementMode.FollowOwner,
                    Extend = extend,
                    AutoHideWindow = true,
                    AutoFocus = false,
                    TopMost = _topMost,
                    ForceTopMostViaWin32 = _forceTopMostViaWin32,
                    InputPolicy = OverlayInputPolicy.All
                },
                _owner);
        }

        private void RebuildOverlay()
        {
            bool reopen = IsOpen;

            CloseAndReleaseOverlay();
            _overlay = CreateOverlay();

            if (reopen)
            {
                Open();
            }
            else
            {
                _layer.SetContent(_content);
                ApplyChrome();
            }
        }

        private static void ValidateState(OverlaySidebarState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (state.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(state.Width));

            if (state.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(state.Height));

            if (state.Gap < 0)
                throw new ArgumentOutOfRangeException(nameof(state.Gap));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OverlaySidebarHost));
        }

        private static bool NearlyEquals(double a, double b)
        {
            return Math.Abs(a - b) < 0.01;
        }

        /// <summary>
        /// 承载侧边栏视图的 Overlay 图层。
        /// </summary>
        private sealed class SidebarLayer : OverlayInputLayerBase
        {
            private readonly OverlaySidebarHost _host;
            private readonly SidebarView _view;
            private OverlayContext _context;

            public SidebarLayer(OverlaySidebarHost host)
            {
                if (host == null)
                    throw new ArgumentNullException(nameof(host));

                _host = host;
                _view = new SidebarView();
            }

            public override UIElement View
            {
                get { return _view; }
            }

            public bool IsAttached
            {
                get { return _context != null; }
            }

            public Rect CurrentOwnerBounds
            {
                get
                {
                    if (_context == null)
                        return Rect.Empty;

                    return _context.OwnerBounds;
                }
            }

            public override void OnAttach(OverlayContext context)
            {
                _context = context;

                if (_context != null)
                    _context.OwnerBoundsChanged += OnOwnerBoundsChanged;

                _view.SetContent(_host._content);
                _host.ApplyChrome();
                _host.ApplyLayout();
            }

            public override void OnDetach()
            {
                if (_context != null)
                    _context.OwnerBoundsChanged -= OnOwnerBoundsChanged;

                _context = null;
            }

            public void SetContent(UIElement content)
            {
                _view.SetContent(content);
            }

            public void ApplyChrome(Brush background, Brush borderBrush, Thickness borderThickness)
            {
                _view.ApplyChrome(background, borderBrush, borderThickness);
            }

            public void ApplyBounds(Rect bounds)
            {
                _view.ApplyBounds(bounds);
            }

            public void ClearBounds()
            {
                _view.ClearBounds();
            }

            private void OnOwnerBoundsChanged(object? sender, EventArgs e)
            {
                _host.ApplyLayout();
            }
        }

        /// <summary>
        /// 侧边栏内部视图。
        /// <para>
        /// 仅负责内容承载、边框外观与最终矩形应用，不负责布局规则决策。
        /// </para>
        /// </summary>
        private sealed class SidebarView : Canvas
        {
            private static readonly Brush DefaultBackground = CreateDefaultBackground();
            private static readonly Brush DefaultBorderBrush = Brushes.DimGray;

            private readonly Border _outerBorder;
            private readonly ContentPresenter _contentPresenter;

            public SidebarView()
            {
                Background = Brushes.Transparent;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                VerticalAlignment = VerticalAlignment.Stretch;
                ClipToBounds = false;

                _contentPresenter = new ContentPresenter();

                _outerBorder = new Border
                {
                    Background = DefaultBackground,
                    BorderBrush = DefaultBorderBrush,
                    BorderThickness = new Thickness(1),
                    Child = _contentPresenter
                };

                Children.Add(_outerBorder);
            }

            public void SetContent(UIElement content)
            {
                if (ReferenceEquals(_contentPresenter.Content, content))
                    return;

                if (content != null)
                {
                    FrameworkElement fe = content as FrameworkElement;
                    if (fe != null && ReferenceEquals(fe.Parent, _contentPresenter))
                    {
                        _contentPresenter.Content = content;
                        return;
                    }

                    DetachFromParent(content);
                }

                _contentPresenter.Content = content;
            }

            public void ApplyChrome(Brush background, Brush borderBrush, Thickness borderThickness)
            {
                _outerBorder.Background = background ?? DefaultBackground;
                _outerBorder.BorderBrush = borderBrush ?? DefaultBorderBrush;
                _outerBorder.BorderThickness = borderThickness;
            }

            public void ApplyBounds(Rect rect)
            {
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
                    return;

                double oldX = Canvas.GetLeft(_outerBorder);
                double oldY = Canvas.GetTop(_outerBorder);

                if (double.IsNaN(oldX)) oldX = 0;
                if (double.IsNaN(oldY)) oldY = 0;

                if (Math.Abs(_outerBorder.Width - rect.Width) < 0.01 &&
                    Math.Abs(_outerBorder.Height - rect.Height) < 0.01 &&
                    Math.Abs(oldX - rect.X) < 0.01 &&
                    Math.Abs(oldY - rect.Y) < 0.01)
                {
                    return;
                }

                _outerBorder.Width = rect.Width;
                _outerBorder.Height = rect.Height;
                Canvas.SetLeft(_outerBorder, rect.X);
                Canvas.SetTop(_outerBorder, rect.Y);
            }

            /// <summary>
            /// 清空当前有效布局区域。
            /// <para>
            /// 这里通过将承载边框的矩形清零来表示当前无可用布局区域，
            /// 而不是切换额外的可见状态，以保持实现简单。
            /// </para>
            /// </summary>
            public void ClearBounds()
            {
                _outerBorder.Width = 0;
                _outerBorder.Height = 0;
                Canvas.SetLeft(_outerBorder, 0);
                Canvas.SetTop(_outerBorder, 0);
            }

            private static Brush CreateDefaultBackground()
            {
                SolidColorBrush brush = new SolidColorBrush(Color.FromArgb(245, 32, 32, 32));
                brush.Freeze();
                return brush;
            }

            private static void DetachFromParent(UIElement content)
            {
                if (content == null)
                    return;

                FrameworkElement fe = content as FrameworkElement;
                if (fe == null)
                    return;

                object parent = fe.Parent;
                if (parent == null)
                    return;

                Panel panel = parent as Panel;
                if (panel != null)
                {
                    panel.Children.Remove(content);
                    return;
                }

                Decorator decorator = parent as Decorator;
                if (decorator != null)
                {
                    if (ReferenceEquals(decorator.Child, content))
                        decorator.Child = null;

                    return;
                }

                ContentControl contentControl = parent as ContentControl;
                if (contentControl != null)
                {
                    if (ReferenceEquals(contentControl.Content, content))
                        contentControl.Content = null;

                    return;
                }

                ContentPresenter presenter = parent as ContentPresenter;
                if (presenter != null)
                {
                    if (ReferenceEquals(presenter.Content, content))
                        presenter.Content = null;
                }
            }
        }
    }

    /// <summary>
    /// 提供 OverlaySidebar 的纯布局计算逻辑。
    /// </summary>
    internal static class OverlaySidebarLayout
    {
        /// <summary>
        /// 计算侧边栏在 Overlay 内部的目标矩形（DIP，相对于 Overlay 左上角）。
        /// </summary>
        /// <param name="ownerBounds">Owner 在 Overlay 内部的矩形区域。</param>
        /// <param name="dock">停靠方向。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="width">侧边栏宽度（DIP）。</param>
        /// <param name="height">侧边栏高度（DIP）。</param>
        /// <param name="gap">与宿主窗口之间的间距（DIP）。</param>
        /// <returns>
        /// 返回侧边栏目标矩形。
        /// <para>
        /// 若输入区域或尺寸参数无效，则返回 <see cref="Rect.Empty"/>。
        /// </para>
        /// </returns>
        public static Rect CalculateSidebarBounds(
            Rect ownerBounds,
            OverlaySidebarDock dock,
            OverlaySidebarAlignment alignment,
            double width,
            double height,
            double gap)
        {
            if (ownerBounds.IsEmpty || ownerBounds.Width <= 0 || ownerBounds.Height <= 0)
                return Rect.Empty;

            if (width <= 0 || height <= 0 || gap < 0)
                return Rect.Empty;

            double actualWidth;
            double actualHeight;
            double x;
            double y;

            if (dock == OverlaySidebarDock.Left || dock == OverlaySidebarDock.Right)
            {
                actualWidth = width;
                actualHeight = GetVerticalHeight(ownerBounds, alignment, height);

                x = dock == OverlaySidebarDock.Left
                    ? ownerBounds.Left - actualWidth - gap
                    : ownerBounds.Right + gap;

                y = GetVerticalY(ownerBounds, alignment, actualHeight);
            }
            else
            {
                actualWidth = GetHorizontalWidth(ownerBounds, alignment, width);
                actualHeight = height;

                x = GetHorizontalX(ownerBounds, alignment, actualWidth);
                y = dock == OverlaySidebarDock.Top
                    ? ownerBounds.Top - actualHeight - gap
                    : ownerBounds.Bottom + gap;
            }

            return new Rect(x, y, actualWidth, actualHeight);
        }

        /// <summary>
        /// 计算 Overlay 需要为侧边栏预留的扩展区域。
        /// </summary>
        /// <param name="dock">停靠方向。</param>
        /// <param name="width">侧边栏宽度（DIP）。</param>
        /// <param name="height">侧边栏高度（DIP）。</param>
        /// <param name="gap">与宿主窗口之间的间距（DIP）。</param>
        /// <returns>Overlay 的扩展边距。</returns>
        public static Thickness CalculateOverlayExtend(
            OverlaySidebarDock dock,
            double width,
            double height,
            double gap)
        {
            double extendW = width + gap;
            double extendH = height + gap;

            switch (dock)
            {
                case OverlaySidebarDock.Left:
                    return new Thickness(extendW, 0, 0, 0);

                case OverlaySidebarDock.Right:
                    return new Thickness(0, 0, extendW, 0);

                case OverlaySidebarDock.Top:
                    return new Thickness(0, extendH, 0, 0);

                case OverlaySidebarDock.Bottom:
                    return new Thickness(0, 0, 0, extendH);

                default:
                    return new Thickness(0);
            }
        }

        private static double GetVerticalHeight(Rect ownerBounds, OverlaySidebarAlignment alignment, double height)
        {
            switch (alignment)
            {
                case OverlaySidebarAlignment.Stretch:
                    return ownerBounds.Height;

                default:
                    return height;
            }
        }

        private static double GetVerticalY(Rect ownerBounds, OverlaySidebarAlignment alignment, double height)
        {
            switch (alignment)
            {
                case OverlaySidebarAlignment.Center:
                    return ownerBounds.Top + (ownerBounds.Height - height) / 2.0;

                case OverlaySidebarAlignment.End:
                    return ownerBounds.Bottom - height;

                case OverlaySidebarAlignment.Stretch:
                case OverlaySidebarAlignment.Start:
                default:
                    return ownerBounds.Top;
            }
        }

        private static double GetHorizontalWidth(Rect ownerBounds, OverlaySidebarAlignment alignment, double width)
        {
            switch (alignment)
            {
                case OverlaySidebarAlignment.Stretch:
                    return ownerBounds.Width;

                default:
                    return width;
            }
        }

        private static double GetHorizontalX(Rect ownerBounds, OverlaySidebarAlignment alignment, double width)
        {
            switch (alignment)
            {
                case OverlaySidebarAlignment.Center:
                    return ownerBounds.Left + (ownerBounds.Width - width) / 2.0;

                case OverlaySidebarAlignment.End:
                    return ownerBounds.Right - width;

                case OverlaySidebarAlignment.Stretch:
                case OverlaySidebarAlignment.Start:
                default:
                    return ownerBounds.Left;
            }
        }
    }
}