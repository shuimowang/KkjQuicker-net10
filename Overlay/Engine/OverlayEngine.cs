using KkjQuicker.Utilities.Imaging;
using KkjQuicker.Utilities.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KkjQuicker.Overlay.Engine
{
    #region ===== 1. Contracts =====

    /// <summary>
    /// 表示一个可被 <see cref="OverlayEngine"/> 托管的覆盖层。
    /// <para>实现者需提供可视元素及附加/分离生命周期处理。</para>
    /// <para>图层优先级由引擎侧管理，通过 <see cref="OverlayEngine.Push"/> 指定
    /// 或通过 <see cref="OverlayEngine.SetPriority"/> 动态修改。</para>
    /// <para>该接口的所有成员均应在 UI 线程上使用。</para>
    /// </summary>
    public interface IOverlayLayer
    {
        /// <summary>
        /// 获取该图层的可视元素。
        /// <para>返回值不能为 <see langword="null"/>。</para>
        /// <para>该元素在任一时刻只能属于一个视觉树；当图层被附加到引擎时，
        /// 引擎会尝试将该元素从其原有父容器中分离后再挂载到 Overlay 宿主根容器。</para>
        /// </summary>
        UIElement View { get; }

        /// <summary>
        /// 当图层被附加到引擎时调用。
        /// <para>调用发生在 UI 线程。</para>
        /// <para>调用时 <see cref="View"/> 已挂载到 Overlay 宿主视觉树，
        /// 但图层尚未完成内部集合登记与最终层级排序。</para>
        /// </summary>
        /// <param name="context">当前图层的运行时上下文。</param>
        /// <remarks>
        /// 若该方法抛出异常，则本次附加失败，图层不会进入引擎，
        /// 并且已挂载的 <see cref="View"/> 会被移除，且不会触发 <see cref="OnDetach"/>。
        /// </remarks>
        void OnAttach(OverlayContext context);

        /// <summary>
        /// 当图层从引擎中移除时调用。
        /// <para>调用发生在 UI 线程。</para>
        /// <para>此时图层已从宿主视觉树中分离，可执行清理逻辑。</para>
        /// </summary>
        void OnDetach();
    }

    /// <summary>
    /// 表示一个可接收输入事件的覆盖层。
    /// </summary>
    public interface IOverlayInputLayer : IOverlayLayer
    {
        /// <summary>
        /// 处理 PreviewKeyDown 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        void OnPreviewKeyDown(KeyEventArgs e);

        /// <summary>
        /// 处理 PreviewKeyUp 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        void OnPreviewKeyUp(KeyEventArgs e);

        /// <summary>
        /// 处理 PreviewMouseDown 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        void OnPreviewMouseDown(MouseButtonEventArgs e);

        /// <summary>
        /// 处理 PreviewMouseUp 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        void OnPreviewMouseUp(MouseButtonEventArgs e);

        /// <summary>
        /// 处理 PreviewMouseMove 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        void OnPreviewMouseMove(MouseEventArgs e);

        /// <summary>
        /// 处理 PreviewMouseWheel 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        void OnPreviewMouseWheel(MouseWheelEventArgs e);

        /// <summary>
        /// 当输入捕获被释放、图层被移除或被其他图层抢占时调用。
        /// <para>若该图层正处于独占输入路由状态，则该方法表示独占输入已结束。</para>
        /// </summary>
        void OnInputCaptureLost();
    }

    /// <summary>
    /// <see cref="IOverlayInputLayer"/> 的基础实现。
    /// <para>提供默认空实现，实现者只需覆写关心的方法。</para>
    /// </summary>
    public abstract class OverlayInputLayerBase : IOverlayInputLayer
    {
        /// <summary>
        /// 获取该图层的可视元素。
        /// </summary>
        public abstract UIElement View { get; }

        /// <summary>
        /// 当图层被附加到引擎时调用。
        /// </summary>
        /// <param name="context">当前图层的运行时上下文。</param>
        public virtual void OnAttach(OverlayContext context)
        {
        }

        /// <summary>
        /// 当图层从引擎中移除时调用。
        /// </summary>
        public virtual void OnDetach()
        {
        }

        /// <summary>
        /// 处理 PreviewKeyDown 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public virtual void OnPreviewKeyDown(KeyEventArgs e)
        {
        }

        /// <summary>
        /// 处理 PreviewKeyUp 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public virtual void OnPreviewKeyUp(KeyEventArgs e)
        {
        }

        /// <summary>
        /// 处理 PreviewMouseDown 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public virtual void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
        }

        /// <summary>
        /// 处理 PreviewMouseUp 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public virtual void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
        }

        /// <summary>
        /// 处理 PreviewMouseMove 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public virtual void OnPreviewMouseMove(MouseEventArgs e)
        {
        }

        /// <summary>
        /// 处理 PreviewMouseWheel 事件。
        /// </summary>
        /// <param name="e">事件参数。</param>
        public virtual void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
        }

        /// <summary>
        /// 当输入捕获被释放、图层被移除或被其他图层抢占时调用。
        /// </summary>
        public virtual void OnInputCaptureLost()
        {
        }
    }

    #endregion

    #region ===== 2. Context =====

    /// <summary>
    /// 表示覆盖层运行时上下文。
    /// <para>该对象会在图层附加时传入 <see cref="IOverlayLayer.OnAttach"/>，且与图层一一对应。</para>
    /// </summary>
    public sealed class OverlayContext
    {
        private readonly WeakReference<OverlayEngine> _engineRef = null!;
        private readonly Dispatcher _dispatcher = null!;
        private readonly IOverlayLayer _ownerLayer = null!;

        internal OverlayContext(
            OverlayEngine engine,
            Window window,
            OverlayOptions options,
            Rect ownerBounds,
            IOverlayLayer ownerLayer)
        {
            _engineRef = new WeakReference<OverlayEngine>(engine);
            _dispatcher = window != null ? window.Dispatcher : Dispatcher.CurrentDispatcher;
            _ownerLayer = ownerLayer;
            Window = window;
            Options = options;
            OwnerBounds = ownerBounds;
        }

        /// <summary>
        /// 获取承载当前图层的 Overlay 窗口。
        /// <para>引擎销毁后该窗口会被关闭，调用者不应依赖其持续存活。</para>
        /// </summary>
        public Window Window { get; } = null!;

        /// <summary>
        /// 获取当前 Overlay 配置。
        /// <para>该对象为传入配置对象的引用。</para>
        /// <para>调用方不应假设运行期直接修改该对象的属性会自动触发生效；
        /// 如需使修改生效，应由外部重新驱动同步或重建引擎。</para>
        /// </summary>
        public OverlayOptions Options { get; } = null!;

        /// <summary>
        /// 获取 Owner 在 Overlay 内部的可交互区域（DIP，相对于 Overlay 左上角）。
        /// <para>当当前无可用 Owner 区域时，值为 <see cref="Rect.Empty"/>。</para>
        /// </summary>
        public Rect OwnerBounds { get; internal set; }

        /// <summary>
        /// 关闭当前图层自身。
        /// <para>等效于 <c>CloseLayer(ownerLayer)</c>，无需在图层内部持有 <c>this</c> 引用重复传入。</para>
        /// </summary>
        public void Close()
        {
            CloseLayer(_ownerLayer);
        }

        /// <summary>
        /// 请求关闭指定图层。
        /// <para>可用于一个图层主动关闭另一个图层。若要关闭自身，优先使用 <see cref="Close"/>。</para>
        /// </summary>
        /// <param name="layer">要关闭的图层。</param>
        public void CloseLayer(IOverlayLayer layer)
        {
            if (layer == null)
                return;

            if (!_engineRef.TryGetTarget(out var engine))
                return;

            if (_dispatcher.CheckAccess())
            {
                engine.Remove(layer);
                return;
            }

            _dispatcher.BeginInvoke((Action)(() => engine.Remove(layer)));
        }

        /// <summary>
        /// 当 <see cref="OwnerBounds"/> 更新时触发。
        /// <para>供图层订阅，以响应 Owner 位置或尺寸变化。</para>
        /// </summary>
        public event EventHandler? OwnerBoundsChanged;

        internal void UpdateOwnerBounds(Rect rect)
        {
            if (OwnerBounds == rect)
                return;

            OwnerBounds = rect;
            OwnerBoundsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region ===== 3. Options =====

    /// <summary>
    /// 表示 Overlay 窗口布局模式。
    /// </summary>
    public enum OverlayPlacementMode
    {
        /// <summary>
        /// 跟随 Owner 窗口的位置与尺寸。
        /// <para>可通过 <see cref="OverlayOptions.Extend"/> 向四边扩展。</para>
        /// </summary>
        FollowOwner,

        /// <summary>
        /// 覆盖整个虚拟屏幕区域（含多显示器）。
        /// </summary>
        GlobalFullscreen,

        /// <summary>
        /// 使用手动指定的区域。
        /// <para>由 <see cref="OverlayOptions.CustomBounds"/> 指定具体区域（DIP）。</para>
        /// </summary>
        Manual
    }

    /// <summary>
    /// 表示 Overlay 输入策略。
    /// <para>设为 <see cref="None"/> 时，Overlay 为纯展示层，鼠标事件穿透到下层窗口。</para>
    /// </summary>
    [Flags]
    public enum OverlayInputPolicy
    {
        /// <summary>
        /// 不处理任何输入，窗口鼠标穿透（纯展示层）。
        /// </summary>
        None = 0,

        /// <summary>
        /// 处理鼠标输入。
        /// </summary>
        Mouse = 1,

        /// <summary>
        /// 处理键盘输入。
        /// </summary>
        Keyboard = 2,

        /// <summary>
        /// 处理滚轮输入。
        /// </summary>
        Wheel = 4,

        /// <summary>
        /// 处理全部输入。
        /// </summary>
        All = Mouse | Keyboard | Wheel
    }

    /// <summary>
    /// 表示 <see cref="OverlayEngine"/> 的配置项。
    /// </summary>
    public sealed class OverlayOptions
    {
        /// <summary>
        /// 获取或设置 Overlay 窗口是否置顶。
        /// <para>默认 <see langword="true"/>。</para>
        /// </summary>
        public bool TopMost { get; set; } = true;

        /// <summary>
        /// 获取或设置当 <see cref="TopMost"/> 为 <see langword="true"/> 时，
        /// 是否额外通过 Win32 再声明一次置顶。
        /// <para>该选项用于增强部分特殊场景下的置顶稳定性。</para>
        /// </summary>
        public bool ForceTopMostViaWin32 { get; set; }

        /// <summary>
        /// 获取或设置 Overlay 背景画刷。
        /// <para>未设置时默认使用透明背景。</para>
        /// </summary>
        public Brush? Background { get; set; }

        /// <summary>
        /// 获取或设置当没有任何图层时，是否自动隐藏 Overlay 窗口。
        /// <para>默认 <see langword="true"/>。</para>
        /// </summary>
        public bool AutoHideWindow { get; set; } = true;

        /// <summary>
        /// 获取或设置输入策略。
        /// <para>设为 <see cref="OverlayInputPolicy.None"/> 时 Overlay 为纯展示层，鼠标事件穿透。</para>
        /// <para>默认 <see cref="OverlayInputPolicy.All"/>。</para>
        /// </summary>
        public OverlayInputPolicy InputPolicy { get; set; } = OverlayInputPolicy.All;

        /// <summary>
        /// 获取或设置显示时是否自动获取焦点。
        /// <para>默认 <see langword="false"/>。</para>
        /// </summary>
        public bool AutoFocus { get; set; }

        /// <summary>
        /// 获取或设置布局模式。
        /// <para>默认 <see cref="OverlayPlacementMode.FollowOwner"/>。</para>
        /// </summary>
        public OverlayPlacementMode Placement { get; set; } = OverlayPlacementMode.FollowOwner;

        /// <summary>
        /// 获取或设置手动模式下的 Overlay 区域（DIP）。
        /// <para>仅在 <see cref="Placement"/> 为 <see cref="OverlayPlacementMode.Manual"/> 时生效。</para>
        /// </summary>
        public Rect CustomBounds { get; set; }

        /// <summary>
        /// 获取或设置 <see cref="OverlayPlacementMode.FollowOwner"/> 模式下
        /// 相对 Owner 向四边扩展的距离（DIP）。
        /// <para>负值会在计算时按 0 处理。</para>
        /// </summary>
        public Thickness Extend { get; set; }

        internal Thickness NormalizedExtend
        {
            get
            {
                return new Thickness(
                    Math.Max(0, Extend.Left),
                    Math.Max(0, Extend.Top),
                    Math.Max(0, Extend.Right),
                    Math.Max(0, Extend.Bottom));
            }
        }
    }

    internal sealed class DisposeAction : IDisposable
    {
        private Action _dispose;

        public DisposeAction(Action disposeAction)
        {
            _dispose = disposeAction;
        }

        public void Dispose()
        {
            var action = System.Threading.Interlocked.Exchange(ref _dispose, null);
            action?.Invoke();
        }
    }

    #endregion

    #region ===== 4. Engine =====

    /// <summary>
    /// 表示 Overlay 总控引擎。
    /// <para>该对象为严格 UI 线程对象，必须在其所属 Dispatcher 线程上创建和访问。</para>
    /// </summary>
    public sealed class OverlayEngine : IDisposable
    {
        private readonly Dispatcher _dispatcher = null!;
        private readonly OverlayOptions _options = null!;
        private readonly WeakReference<Window>? _ownerRef = null!;
        private readonly OverlayWindowHost _host = null!;
        private readonly OverlayLayerCollection _layers = null!;
        private readonly OverlayInputRouter _inputRouter = null!;

        private bool _disposed;
        private bool _syncScheduled;
        private Rect _currentOverlayBounds = Rect.Empty;
        private Rect _currentOwnerBounds = Rect.Empty;

        /// <summary>
        /// 初始化一个新的 <see cref="OverlayEngine"/> 实例。
        /// <para>必须在 UI 线程上调用。Overlay 宿主窗口在构造时立即创建（初始不可见）。</para>
        /// </summary>
        /// <param name="options">配置项；传入 <see langword="null"/> 时使用默认配置。</param>
        /// <param name="owner">Owner 窗口；传入后会在支持的布局模式下参与同步。</param>
        public OverlayEngine(OverlayOptions? options = null, Window? owner = null)
        {
            _options = options ?? new OverlayOptions();
            _ownerRef = owner != null ? new WeakReference<Window>(owner) : null;

            _dispatcher = owner != null
                ? owner.Dispatcher
                : (Application.Current != null
                    ? Application.Current.Dispatcher
                    : Dispatcher.CurrentDispatcher);

            _layers = new OverlayLayerCollection();
            _inputRouter = new OverlayInputRouter(_layers);
            _host = new OverlayWindowHost(_options);
            HookHostEvents(_host);

            if (owner != null)
            {
                _host.Window.Owner = owner;
                AttachOwnerEvents(owner);
            }
        }

        /// <summary>
        /// 添加一个图层到当前 Overlay。
        /// <para>必须在当前引擎所属的 Dispatcher 线程上调用。</para>
        /// <para>返回的对象可用于在不再需要时移除该图层。</para>
        /// <para>若同一图层实例已存在，则不会重复添加；返回的释放句柄仍可安全调用。</para>
        /// </summary>
        /// <param name="layer">要添加的图层。</param>
        /// <param name="priority">图层优先级；数值越大，显示层级越高。默认 0。</param>
        /// <returns>用于移除该图层的释放句柄。</returns>
        public IDisposable Push(IOverlayLayer layer, int priority = 0)
        {
            VerifyAccess();

            if (layer == null)
                throw new ArgumentNullException(nameof(layer));

            ThrowIfDisposed();

            bool added = _layers.Add(layer, priority, _host.Root, this, _host.Window, _options);
            if (added)
            {
                SyncToOwner();
                UpdateWindowState();
            }

            return new DisposeAction(() => RemoveFromAnyThread(layer));
        }

        /// <summary>
        /// 移除指定图层。
        /// <para>必须在当前引擎所属的 Dispatcher 线程上调用。</para>
        /// <para>若图层不存在，则忽略。</para>
        /// </summary>
        /// <param name="layer">要移除的图层。</param>
        public void Remove(IOverlayLayer layer)
        {
            VerifyAccess();

            if (layer == null || _disposed)
                return;

            _inputRouter.NotifyLayerRemoving(layer);
            _layers.Remove(layer);
            UpdateWindowState();
        }

        private void RemoveFromAnyThread(IOverlayLayer layer)
        {
            if (layer == null)
                return;

            if (_dispatcher.CheckAccess())
            {
                Remove(layer);
                return;
            }

            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
                return;

            try
            {
                _dispatcher.BeginInvoke((Action)(() => Remove(layer)));
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine(ex);
            }
        }

        /// <summary>
        /// 修改指定图层的优先级。
        /// <para>修改后会立即重新排序所有图层的显示层级。</para>
        /// </summary>
        /// <param name="layer">目标图层；若不在引擎中则忽略。</param>
        /// <param name="priority">新的优先级值；数值越大，显示层级越高。</param>
        public void SetPriority(IOverlayLayer layer, int priority)
        {
            VerifyAccess();

            if (layer == null || _disposed)
                return;

            _layers.SetPriority(layer, priority);
        }

        /// <summary>
        /// 将输入捕获切换到指定图层。
        /// <para>捕获后，输入将独占路由到该图层，
        /// 直到显式调用 <see cref="ReleaseInputCapture"/>、
        /// 图层被移除或被其他图层重新捕获。</para>
        /// </summary>
        /// <param name="layer">要捕获输入的图层。</param>
        public void CaptureInput(IOverlayInputLayer layer)
        {
            VerifyAccess();

            if (_disposed)
                return;

            _inputRouter.Capture(layer);
        }

        /// <summary>
        /// 释放当前输入捕获。
        /// <para>若当前存在输入捕获图层，则会触发其 <see cref="IOverlayInputLayer.OnInputCaptureLost"/>。</para>
        /// </summary>
        public void ReleaseInputCapture()
        {
            VerifyAccess();

            if (_disposed)
                return;

            _inputRouter.Release();
        }

        /// <summary>
        /// 释放当前引擎及相关资源。
        /// <para>必须在当前引擎所属的 Dispatcher 线程上调用。</para>
        /// </summary>
        public void Dispose()
        {
            VerifyAccess();

            if (_disposed)
                return;

            _disposed = true;

            if (_ownerRef != null && _ownerRef.TryGetTarget(out var owner))
                DetachOwnerEvents(owner);

            _inputRouter.ReleaseSilently();
            _layers.Clear();
            UnhookHostEvents(_host);
            _host.Dispose();
        }

        private void HookHostEvents(OverlayWindowHost host)
        {
            host.PreviewKeyDown += Host_PreviewKeyDown;
            host.PreviewKeyUp += Host_PreviewKeyUp;
            host.PreviewMouseDown += Host_PreviewMouseDown;
            host.PreviewMouseUp += Host_PreviewMouseUp;
            host.PreviewMouseMove += Host_PreviewMouseMove;
            host.PreviewMouseWheel += Host_PreviewMouseWheel;
            host.BecameVisible += Host_BecameVisible;
        }

        private void UnhookHostEvents(OverlayWindowHost host)
        {
            host.PreviewKeyDown -= Host_PreviewKeyDown;
            host.PreviewKeyUp -= Host_PreviewKeyUp;
            host.PreviewMouseDown -= Host_PreviewMouseDown;
            host.PreviewMouseUp -= Host_PreviewMouseUp;
            host.PreviewMouseMove -= Host_PreviewMouseMove;
            host.PreviewMouseWheel -= Host_PreviewMouseWheel;
            host.BecameVisible -= Host_BecameVisible;
        }

        private void Host_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (!CanRouteKeyboardInput())
                return;

            _inputRouter.DispatchKeyDown(e);
        }

        private void Host_PreviewKeyUp(object? sender, KeyEventArgs e)
        {
            if (!CanRouteKeyboardInput())
                return;

            _inputRouter.DispatchKeyUp(e);
        }

        private void Host_PreviewMouseDown(object? sender, MouseButtonEventArgs e)
        {
            if (!CanRouteMouseInput())
                return;

            _inputRouter.DispatchMouseDown(e);
        }

        private void Host_PreviewMouseUp(object? sender, MouseButtonEventArgs e)
        {
            if (!CanRouteMouseInput())
                return;

            _inputRouter.DispatchMouseUp(e);
        }

        private void Host_PreviewMouseMove(object? sender, MouseEventArgs e)
        {
            if (!CanRouteMouseInput())
                return;

            _inputRouter.DispatchMouseMove(e);
        }

        private void Host_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
        {
            if (!CanRouteWheelInput())
                return;

            _inputRouter.DispatchMouseWheel(e);
        }

        private void Host_BecameVisible(object? sender, EventArgs e)
        {
            if (!_options.AutoFocus || !CanRouteKeyboardInput())
                return;

            _host.TryFocusRoot();
        }

        private bool CanRouteInputCore()
        {
            return !_disposed &&
                   _options.InputPolicy != OverlayInputPolicy.None &&
                   _layers.Count > 0;
        }

        private bool CanRouteKeyboardInput()
        {
            return CanRouteInputCore() &&
                   _options.InputPolicy.HasFlag(OverlayInputPolicy.Keyboard);
        }

        private bool CanRouteMouseInput()
        {
            return CanRouteInputCore() &&
                   _options.InputPolicy.HasFlag(OverlayInputPolicy.Mouse);
        }

        private bool CanRouteWheelInput()
        {
            return CanRouteInputCore() &&
                   _options.InputPolicy.HasFlag(OverlayInputPolicy.Wheel);
        }

        private void AttachOwnerEvents(Window owner)
        {
            if (owner == null)
                return;

            owner.LocationChanged += OnOwnerChanged;
            owner.SizeChanged += OnOwnerChanged;
            owner.StateChanged += OnOwnerChanged;
            owner.Closed += OnOwnerClosed;
        }

        private void DetachOwnerEvents(Window owner)
        {
            if (owner == null)
                return;

            owner.LocationChanged -= OnOwnerChanged;
            owner.SizeChanged -= OnOwnerChanged;
            owner.StateChanged -= OnOwnerChanged;
            owner.Closed -= OnOwnerClosed;
        }

        private void OnOwnerClosed(object? sender, EventArgs e)
        {
            Dispose();
        }

        private void OnOwnerChanged(object? sender, EventArgs e)
        {
            ScheduleSyncToOwner();
        }

        private void ScheduleSyncToOwner()
        {
            if (_disposed || _syncScheduled)
                return;

            _syncScheduled = true;

            _dispatcher.BeginInvoke((Action)(() =>
            {
                _syncScheduled = false;

                if (_disposed)
                    return;

                SyncToOwner();
                UpdateWindowState();
            }), DispatcherPriority.Render);
        }

        private void SyncToOwner()
        {
            if (_disposed)
                return;

            switch (_options.Placement)
            {
                case OverlayPlacementMode.FollowOwner:
                    SyncFollowOwner();
                    break;

                case OverlayPlacementMode.GlobalFullscreen:
                    _currentOverlayBounds = DpiHelper.RoundToDevicePixels(
                        new Rect(
                            SystemParameters.VirtualScreenLeft,
                            SystemParameters.VirtualScreenTop,
                            SystemParameters.VirtualScreenWidth,
                            SystemParameters.VirtualScreenHeight),
                        _host.Window);
                    _currentOwnerBounds = Rect.Empty;
                    _layers.UpdateOwnerBounds(_currentOwnerBounds);
                    break;

                case OverlayPlacementMode.Manual:
                    if (!OverlayGeometryHelper.IsValidRect(_options.CustomBounds))
                    {
                        _currentOverlayBounds = Rect.Empty;
                        _currentOwnerBounds = Rect.Empty;
                    }
                    else
                    {
                        _currentOverlayBounds = DpiHelper.RoundToDevicePixels(_options.CustomBounds, _host.Window);
                        _currentOwnerBounds = Rect.Empty;
                    }

                    _layers.UpdateOwnerBounds(_currentOwnerBounds);
                    break;
            }
        }

        private void SyncFollowOwner()
        {
            if (!TryGetOwnerOverlayBoundsDip(out var overlayBounds, out var ownerBounds))
            {
                _currentOverlayBounds = Rect.Empty;
                _currentOwnerBounds = Rect.Empty;
            }
            else
            {
                _currentOverlayBounds = overlayBounds;
                _currentOwnerBounds = ownerBounds;
            }

            _layers.UpdateOwnerBounds(_currentOwnerBounds);
        }

        private void UpdateWindowState()
        {
            if (_disposed)
                return;

            bool hasLayers = _layers.Count > 0;
            bool hasBounds = OverlayGeometryHelper.IsValidRect(_currentOverlayBounds);
            bool shouldShow = hasBounds && (hasLayers || !_options.AutoHideWindow);

            bool shouldHitTest = shouldShow &&
                                 hasLayers &&
                                 _options.InputPolicy != OverlayInputPolicy.None;

            bool requiresMouseHitTest = shouldShow &&
                                        hasLayers &&
                                        (_options.InputPolicy.HasFlag(OverlayInputPolicy.Mouse) ||
                                         _options.InputPolicy.HasFlag(OverlayInputPolicy.Wheel));

            if (shouldShow)
            {
                _host.SetBounds(_currentOverlayBounds);
                _host.SetHitTestVisible(shouldHitTest);
                _host.SetMousePassthrough(!requiresMouseHitTest);
                _host.Show();
            }
            else
            {
                _host.SetHitTestVisible(false);
                _host.SetMousePassthrough(true);
                _host.Hide();
            }
        }

        private bool TryGetOwnerWindowAndHwnd(out Window owner, out IntPtr hwnd)
        {
            owner = null;
            hwnd = IntPtr.Zero;

            if (_ownerRef == null || !_ownerRef.TryGetTarget(out owner))
                return false;

            if (!owner.IsLoaded || !owner.IsVisible)
                return false;

            hwnd = owner.GetHandle();
            if (hwnd == IntPtr.Zero)
                return false;

            if (NativeMethods.IsIconic(hwnd))
                return false;

            return true;
        }

        private bool TryGetOwnerOverlayBoundsDip(out Rect overlayBoundsDip, out Rect ownerBoundsInOverlayDip)
        {
            overlayBoundsDip = Rect.Empty;
            ownerBoundsInOverlayDip = Rect.Empty;

            if (!TryGetOwnerWindowAndHwnd(out var owner, out var hwnd))
                return false;

            if (!WindowHelper.TryGetWindowBounds(hwnd, out RECT pxRect) ||
                pxRect.Width <= 0 ||
                pxRect.Height <= 0)
            {
                return false;
            }

            var dpi = DpiHelper.GetDpiFromHwnd(hwnd);

            double ownerLeftDip = pxRect.Left / dpi.DpiScaleX;
            double ownerTopDip = pxRect.Top / dpi.DpiScaleY;
            double ownerWidthDip = pxRect.Width / dpi.DpiScaleX;
            double ownerHeightDip = pxRect.Height / dpi.DpiScaleY;

            Thickness extend = _options.NormalizedExtend;

            var rawOverlayBounds = new Rect(
                ownerLeftDip - extend.Left,
                ownerTopDip - extend.Top,
                ownerWidthDip + extend.Left + extend.Right,
                ownerHeightDip + extend.Top + extend.Bottom);

            overlayBoundsDip = DpiHelper.RoundToDevicePixels(rawOverlayBounds, owner);

            ownerBoundsInOverlayDip = new Rect(
                ownerLeftDip - overlayBoundsDip.X,
                ownerTopDip - overlayBoundsDip.Y,
                ownerWidthDip,
                ownerHeightDip);

            return OverlayGeometryHelper.IsValidRect(overlayBoundsDip) &&
                   OverlayGeometryHelper.IsValidRect(ownerBoundsInOverlayDip);
        }

        private void VerifyAccess()
        {
            if (!_dispatcher.CheckAccess())
                throw new InvalidOperationException("OverlayEngine must be accessed on its owning Dispatcher thread.");
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OverlayEngine));
        }
    }

    #endregion

    #region ===== 5. OverlayWindowHost =====

    internal sealed class OverlayWindowHost : IDisposable
    {
        private readonly HostWindow _window = null!;
        private bool _disposed;
        private bool _mousePassthrough = true;

        public OverlayWindowHost(OverlayOptions options)
        {
            _window = new HostWindow(options ?? new OverlayOptions());
            WireWindowEvents(_window);
        }

        public Window Window
        {
            get { return _window; }
        }

        public Grid Root
        {
            get { return _window.Root; }
        }

        public event KeyEventHandler? PreviewKeyDown;
        public event KeyEventHandler? PreviewKeyUp;
        public event MouseButtonEventHandler? PreviewMouseDown;
        public event MouseButtonEventHandler? PreviewMouseUp;
        public event MouseEventHandler? PreviewMouseMove;
        public event MouseWheelEventHandler? PreviewMouseWheel;
        public event EventHandler? BecameVisible;

        public void SetBounds(Rect bounds)
        {
            if (!OverlayGeometryHelper.IsValidRect(bounds))
                return;

            if (OverlayGeometryHelper.NearlyEquals(_window.Left, bounds.X) &&
                OverlayGeometryHelper.NearlyEquals(_window.Top, bounds.Y) &&
                OverlayGeometryHelper.NearlyEquals(_window.Width, bounds.Width) &&
                OverlayGeometryHelper.NearlyEquals(_window.Height, bounds.Height))
            {
                return;
            }

            _window.Left = bounds.X;
            _window.Top = bounds.Y;
            _window.Width = bounds.Width;
            _window.Height = bounds.Height;
        }

        public void SetHitTestVisible(bool enabled)
        {
            if (_window.IsHitTestVisible == enabled)
                return;

            _window.IsHitTestVisible = enabled;
        }

        public void SetMousePassthrough(bool passthrough)
        {
            if (_mousePassthrough == passthrough)
                return;

            _mousePassthrough = passthrough;
            ApplyWindowStyles();
        }

        public void Show()
        {
            if (!_window.IsVisible)
                _window.Show();

            ApplyWindowStyles();
        }

        public void Hide()
        {
            if (_window.IsVisible)
                _window.Hide();
        }

        public void TryFocusRoot()
        {
            _window.TryFocusRoot();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            UnwireWindowEvents(_window);

            try
            {
                if (_window.IsLoaded || _window.IsVisible)
                    _window.InternalClose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void ApplyWindowStyles()
        {
            IntPtr hwnd = _window.GetHandle();
            if (hwnd == IntPtr.Zero)
                return;

            WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_TOOLWINDOW, true);
            WindowHelper.SetExStyleFlag(hwnd, WindowStyles.WS_EX_TRANSPARENT, _mousePassthrough);
        }

        private void WireWindowEvents(HostWindow window)
        {
            window.PreviewKeyDown += OnPreviewKeyDownInternal;
            window.PreviewKeyUp += OnPreviewKeyUpInternal;
            window.PreviewMouseDown += OnPreviewMouseDownInternal;
            window.PreviewMouseUp += OnPreviewMouseUpInternal;
            window.PreviewMouseMove += OnPreviewMouseMoveInternal;
            window.PreviewMouseWheel += OnPreviewMouseWheelInternal;
            window.BecameVisible += OnBecameVisibleInternal;
            window.SourceInitializedCompleted += OnSourceInitializedCompletedInternal;
        }

        private void UnwireWindowEvents(HostWindow window)
        {
            window.PreviewKeyDown -= OnPreviewKeyDownInternal;
            window.PreviewKeyUp -= OnPreviewKeyUpInternal;
            window.PreviewMouseDown -= OnPreviewMouseDownInternal;
            window.PreviewMouseUp -= OnPreviewMouseUpInternal;
            window.PreviewMouseMove -= OnPreviewMouseMoveInternal;
            window.PreviewMouseWheel -= OnPreviewMouseWheelInternal;
            window.BecameVisible -= OnBecameVisibleInternal;
            window.SourceInitializedCompleted -= OnSourceInitializedCompletedInternal;
        }

        private void OnPreviewKeyDownInternal(object? sender, KeyEventArgs e)
            => PreviewKeyDown?.Invoke(this, e);

        private void OnPreviewKeyUpInternal(object? sender, KeyEventArgs e)
            => PreviewKeyUp?.Invoke(this, e);

        private void OnPreviewMouseDownInternal(object? sender, MouseButtonEventArgs e)
            => PreviewMouseDown?.Invoke(this, e);

        private void OnPreviewMouseUpInternal(object? sender, MouseButtonEventArgs e)
            => PreviewMouseUp?.Invoke(this, e);

        private void OnPreviewMouseMoveInternal(object? sender, MouseEventArgs e)
            => PreviewMouseMove?.Invoke(this, e);

        private void OnPreviewMouseWheelInternal(object? sender, MouseWheelEventArgs e)
            => PreviewMouseWheel?.Invoke(this, e);

        private void OnBecameVisibleInternal(object? sender, EventArgs e)
            => BecameVisible?.Invoke(this, EventArgs.Empty);

        private void OnSourceInitializedCompletedInternal(object? sender, EventArgs e)
            => ApplyWindowStyles();

        private sealed class HostWindow : Window
        {
            private readonly OverlayOptions _options;
            private bool _allowClose;

            public HostWindow(OverlayOptions options)
            {
                _options = options ?? new OverlayOptions();

                AllowsTransparency = true;
                WindowStyle = WindowStyle.None;
                ShowInTaskbar = false;
                ShowActivated = _options.AutoFocus;
                Topmost = _options.TopMost;
                Background = _options.Background ?? Brushes.Transparent;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.Manual;

                Root = new Grid();
                Root.Focusable = true;
                FocusManager.SetIsFocusScope(Root, true);

                Content = Root;
                IsVisibleChanged += OnIsVisibleChangedInternal;
            }

            public Grid Root { get; private set; }

            public event EventHandler? BecameVisible;
            public event EventHandler? SourceInitializedCompleted;

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                ApplyTopMostViaWin32IfNeeded();
                SourceInitializedCompleted?.Invoke(this, EventArgs.Empty);
            }

            protected override void OnClosing(CancelEventArgs e)
            {
                if (!_allowClose)
                    e.Cancel = true;

                base.OnClosing(e);
            }

            public void TryFocusRoot()
            {
                try
                {
                    Activate();
                    Keyboard.Focus(Root);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            public void InternalClose()
            {
                _allowClose = true;
                Close();
            }

            private void ApplyTopMostViaWin32IfNeeded()
            {
                if (!_options.TopMost || !_options.ForceTopMostViaWin32)
                    return;

                IntPtr hwnd = this.GetHandle();
                if (hwnd == IntPtr.Zero)
                    return;

                WindowHelper.SetTopMost(hwnd, true, noActivate: true, showIfHidden: false);
            }

            private void OnIsVisibleChangedInternal(object? sender, DependencyPropertyChangedEventArgs e)
            {
                if (!(bool)e.NewValue)
                    return;

                ApplyTopMostViaWin32IfNeeded();
                BecameVisible?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    #endregion

    #region ===== 6. OverlayLayerCollection =====

    internal sealed class OverlayLayerCollection
    {
        private readonly List<LayerEntry> _layers = new(8);
        private long _seq;
        private Rect _ownerBounds;

        private sealed class LayerEntry
        {
            public IOverlayLayer Layer = null!;
            public UIElement View = null!;
            public int Priority;
            public long Seq;
            public OverlayContext Context = null!;
        }

        public int Count
        {
            get { return _layers.Count; }
        }

        public bool Add(IOverlayLayer layer, int priority, Panel root, OverlayEngine engine, Window hostWindow, OverlayOptions options)
        {
            if (layer == null)
                throw new ArgumentNullException(nameof(layer));
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));
            if (hostWindow == null)
                throw new ArgumentNullException(nameof(hostWindow));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (Contains(layer))
                return false;

            UIElement view = layer.View;
            if (view == null)
                throw new InvalidOperationException("Layer.View cannot be null.");

            DetachFromParent(view);

            var context = new OverlayContext(engine, hostWindow, options, _ownerBounds, layer);

            root.Children.Add(view);

            try
            {
                layer.OnAttach(context);
            }
            catch
            {
                try { DetachFromParent(view); }
                catch (Exception ex) { Debug.WriteLine(ex); }

                throw;
            }

            _layers.Add(new LayerEntry
            {
                Layer = layer,
                View = view,
                Priority = priority,
                Seq = _seq++,
                Context = context
            });

            SyncOrder();
            return true;
        }

        public void Remove(IOverlayLayer layer)
        {
            if (layer == null)
                return;

            int index = FindIndex(layer);
            if (index < 0)
                return;

            LayerEntry entry = _layers[index];

            // 先从集合移除，再执行可能递归的清理逻辑，防止索引失效
            _layers.RemoveAt(index);

            try { DetachFromParent(entry.View); }
            catch (Exception ex) { Debug.WriteLine(ex); }

            try { layer.OnDetach(); }
            catch (Exception ex) { Debug.WriteLine(ex); }

            SyncOrder();
        }

        public void Clear()
        {
            for (int i = _layers.Count - 1; i >= 0; i--)
                Remove(_layers[i].Layer);

            _seq = 0;
        }

        public void SetPriority(IOverlayLayer layer, int priority)
        {
            int index = FindIndex(layer);
            if (index < 0)
                return;

            if (_layers[index].Priority == priority)
                return;

            _layers[index].Priority = priority;
            SyncOrder();
        }

        public void UpdateOwnerBounds(Rect rect)
        {
            _ownerBounds = rect;

            for (int i = 0; i < _layers.Count; i++)
                _layers[i].Context?.UpdateOwnerBounds(rect);
        }

        public bool Contains(IOverlayLayer layer)
        {
            return FindIndex(layer) >= 0;
        }

        /// <summary>
        /// 返回当前所有可见输入图层的快照列表（从高层到低层）。
        /// <para>每次派发前重新构建，确保图层可见性变化能立即影响输入路由。</para>
        /// </summary>
        public List<IOverlayInputLayer> GetInputLayersTopToBottom()
        {
            return BuildInputSnapshot();
        }

        private List<IOverlayInputLayer> BuildInputSnapshot()
        {
            List<IOverlayInputLayer> result = new(_layers.Count);
            for (int i = _layers.Count - 1; i >= 0; i--)
            {
                IOverlayInputLayer? inputLayer = _layers[i].Layer as IOverlayInputLayer;
                if (inputLayer == null)
                    continue;

                UIElement view = _layers[i].View;
                if (view == null || !view.IsVisible)
                    continue;

                result.Add(inputLayer);
            }
            return result;
        }

        private void SyncOrder()
        {
            if (_layers.Count <= 1)
            {
                if (_layers.Count == 1)
                    Panel.SetZIndex(_layers[0].View, 0);

                return;
            }

            _layers.Sort((a, b) =>
            {
                int cmp = a.Priority.CompareTo(b.Priority);
                return cmp != 0 ? cmp : a.Seq.CompareTo(b.Seq);
            });

            for (int i = 0; i < _layers.Count; i++)
                Panel.SetZIndex(_layers[i].View, i);
        }

        private int FindIndex(IOverlayLayer layer)
        {
            for (int i = 0; i < _layers.Count; i++)
            {
                if (ReferenceEquals(_layers[i].Layer, layer))
                    return i;
            }

            return -1;
        }

        private static void DetachFromParent(UIElement view)
        {
            if (view == null)
                return;

            FrameworkElement? fe = view as FrameworkElement;
            if (fe == null)
                return;

            object parent = fe.Parent;
            if (parent == null)
                return;

            Panel? panel = parent as Panel;
            if (panel != null)
            {
                panel.Children.Remove(view);
                return;
            }

            Decorator? decorator = parent as Decorator;
            if (decorator != null)
            {
                if (ReferenceEquals(decorator.Child, view))
                    decorator.Child = null;

                return;
            }

            ContentControl? contentControl = parent as ContentControl;
            if (contentControl != null)
            {
                if (ReferenceEquals(contentControl.Content, view))
                    contentControl.Content = null;

                return;
            }

            ContentPresenter? contentPresenter = parent as ContentPresenter;
            if (contentPresenter != null)
            {
                if (ReferenceEquals(contentPresenter.Content, view))
                    contentPresenter.Content = null;
            }
        }
    }

    #endregion

    #region ===== 7. OverlayInputRouter =====

    internal sealed class OverlayInputRouter
    {
        private readonly OverlayLayerCollection _layers;
        private IOverlayInputLayer? _captured;

        public OverlayInputRouter(OverlayLayerCollection layers)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        }

        public void Capture(IOverlayInputLayer layer)
        {
            if (layer == null || !_layers.Contains(layer))
                return;

            if (!ReferenceEquals(_captured, layer))
            {
                var lost = _captured;
                _captured = layer;
                NotifyInputCaptureLost(lost);
            }
        }

        public void Release()
        {
            var lost = _captured;
            _captured = null;
            NotifyInputCaptureLost(lost);
        }

        /// <summary>
        /// 仅用于销毁路径，不触发 <see cref="IOverlayInputLayer.OnInputCaptureLost"/>。
        /// </summary>
        public void ReleaseSilently()
        {
            _captured = null;
        }

        public void NotifyLayerRemoving(IOverlayLayer layer)
        {
            if (layer == null)
                return;

            if (_captured != null && ReferenceEquals(_captured, layer))
            {
                var lost = _captured;
                _captured = null;
                NotifyInputCaptureLost(lost);
            }
        }

        public void DispatchKeyDown(KeyEventArgs e) =>
            Dispatch(e, (layer, args) => layer.OnPreviewKeyDown(args));

        public void DispatchKeyUp(KeyEventArgs e) =>
            Dispatch(e, (layer, args) => layer.OnPreviewKeyUp(args));

        public void DispatchMouseDown(MouseButtonEventArgs e) =>
            Dispatch(e, (layer, args) => layer.OnPreviewMouseDown(args));

        public void DispatchMouseUp(MouseButtonEventArgs e) =>
            Dispatch(e, (layer, args) => layer.OnPreviewMouseUp(args));

        public void DispatchMouseMove(MouseEventArgs e) =>
            Dispatch(e, (layer, args) => layer.OnPreviewMouseMove(args));

        public void DispatchMouseWheel(MouseWheelEventArgs e) =>
            Dispatch(e, (layer, args) => layer.OnPreviewMouseWheel(args));

        private void Dispatch<T>(T e, Action<IOverlayInputLayer, T> action)
            where T : RoutedEventArgs
        {
            if (e == null || action == null)
                return;

            if (_captured != null)
            {
                if (_layers.Contains(_captured))
                {
                    DispatchToLayer(_captured, e, action);
                    return;
                }

                // 被捕获图层已不在集合，补发丢失通知后继续广播
                var lost = _captured;
                _captured = null;
                NotifyInputCaptureLost(lost);
            }

            var snapshot = _layers.GetInputLayersTopToBottom();
            foreach (IOverlayInputLayer layer in snapshot)
            {
                DispatchToLayer(layer, e, action);
                if (e.Handled)
                    break;
            }
        }

        private static void DispatchToLayer<T>(
            IOverlayInputLayer layer,
            T e,
            Action<IOverlayInputLayer, T> action)
            where T : RoutedEventArgs
        {
            try
            {
                action(layer, e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static void NotifyInputCaptureLost(IOverlayInputLayer? layer)
        {
            if (layer == null)
                return;

            try
            {
                layer.OnInputCaptureLost();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    #endregion

    #region ===== 8. Geometry Helpers =====

    internal static class OverlayGeometryHelper
    {
        private const double Epsilon = 0.01d;

        public static bool IsValidRect(Rect rect)
        {
            if (rect.IsEmpty)
                return false;

            return rect.Width > 0 &&
                   rect.Height > 0 &&
                   !double.IsNaN(rect.X) &&
                   !double.IsNaN(rect.Y) &&
                   !double.IsNaN(rect.Width) &&
                   !double.IsNaN(rect.Height) &&
                   !double.IsInfinity(rect.X) &&
                   !double.IsInfinity(rect.Y) &&
                   !double.IsInfinity(rect.Width) &&
                   !double.IsInfinity(rect.Height);
        }

        public static bool NearlyEquals(double a, double b)
        {
            return Math.Abs(a - b) < Epsilon;
        }
    }

    #endregion
}
