using KkjQuicker.Utilities.Imaging;
using KkjQuicker.Utilities.Win32;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace KkjQuicker.Overlay.Applications
{
    /// <summary>
    /// 表示侧边栏窗口的 Z 序策略。
    /// </summary>
    public enum SidebarZOrderMode
    {
        /// <summary>
        /// 跟随目标窗口的 Z 序组别与前后关系。
        /// </summary>
        FollowTarget,

        /// <summary>
        /// 不主动干预侧边栏窗口自身的 Z 序。
        /// <para>
        /// 在该模式下，<see cref="HwndSidebarHost"/> 仅同步侧边栏窗口的位置与可见状态，
        /// 不主动同步其 Z 序。侧边栏窗口是否置顶、是否使用 Win32 强化置顶，
        /// 由侧边栏窗口自身或调用方自行决定。
        /// </para>
        /// </summary>
        Independent
    }

    /// <summary>
    /// 表示一个跟随外部 HWND 窗口的侧边栏宿主。
    /// <para>
    /// 该类型将一个 WPF <see cref="Window"/> 停靠在任意外部顶级窗口侧边，
    /// 自动跟随目标窗口的位置、可见性与 Z 序变化。
    /// </para>
    /// <para>
    /// 内部以 WinEventHook 驱动实时位置与 Z 序同步，辅以低频轮询作为兜底。
    /// 位置更新在 <see cref="DispatcherPriority.Render"/> 优先级执行，
    /// 可在每帧渲染前完成，跟随延迟通常低于一个显示帧（16ms @ 60Hz）。
    /// </para>
    /// <para>
    /// <b>线程安全：</b><see cref="Dispose"/> 可从任意线程调用；其余公开成员须在
    /// 侧边栏窗口所属的 Dispatcher 线程上调用。
    /// </para>
    /// <para>
    /// <b>注意：</b>目标窗口必须属于当前进程之外的进程。若目标窗口与本类处于同一进程，
    /// WinEventHook 将因 <c>WINEVENT_SKIPOWNPROCESS</c> 标志静默失效，
    /// 仅低频轮询兜底继续生效。
    /// </para>
    /// </summary>
    public sealed class HwndSidebarHost : IDisposable
    {
        #region 常量与 Hook 槽位

        private const int FallbackPollMs = 500;

        // 每隔多少次轮询 Tick 执行一次 Z 序兜底同步。
        // Z 序通过 EVENT_SYSTEM_FOREGROUND 已主动处理，此处仅作保险。
        private const int ZOrderFallbackInterval = 2;

        // WinEvent Hook 槽位索引（共 4 个，含 range hook；原设计需 6 个独立 hook）
        private const int SlotForeground = 0;   // EVENT_SYSTEM_FOREGROUND（全局，Z 序用）
        private const int SlotMinimize = 1;      // EVENT_SYSTEM_MINIMIZESTART..END（range）
        private const int SlotVisibility = 2;    // EVENT_OBJECT_DESTROY..HIDE（range 0x8001-0x8003）
        private const int SlotLocation = 3;      // EVENT_OBJECT_LOCATIONCHANGE
        private const int SlotCount = 4;

        #endregion

        #region 字段

        private readonly IntPtr _targetHwnd;
        private readonly Window _sidebarWindow;
        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _fallbackTimer;

        // Hook 句柄数组；WinEventDelegate 必须作为字段保持存活，防止被 GC 回收
        private readonly IntPtr[] _hooks = new IntPtr[SlotCount];
        private NativeMethods.WinEventDelegate? _winEventProc;

        // 生命周期状态。
        // _disposed 需跨线程可见（WinEvent 回调在线程池线程读取），故标记 volatile。
        // _isActive / _sidebarWindowClosed 仅在 Dispatcher 线程上访问，不需要 volatile。
        private volatile bool _disposed;
        private bool _isActive;

        // 通过 Closed 事件置位，用于准确判断侧边栏窗口是否已关闭。
        // 相比"HWND 为零 且 未 Loaded"的组合判断，不会误判全新窗口。
        private bool _sidebarWindowClosed;

        // 布局参数
        private OverlaySidebarDock _dock = OverlaySidebarDock.Right;
        private OverlaySidebarAlignment _alignment = OverlaySidebarAlignment.Stretch;
        private double _gap;
        private SidebarZOrderMode _zOrderMode = SidebarZOrderMode.FollowTarget;

        // 刷新调度（Interlocked：0 = 无待执行刷新，1 = 已调度）
        private volatile int _refreshPending;

        // 位置缓存与可见性跟踪（仅 Dispatcher 线程访问）
        private bool _sidebarVisible;
        private Rect _lastAppliedBoundsDip = Rect.Empty;

        // Z 序兜底计数器（仅 Dispatcher 线程访问）
        private int _zOrderTickCounter;

        #endregion

        #region 构造函数

        /// <summary>
        /// 初始化一个新的 <see cref="HwndSidebarHost"/> 实例。
        /// </summary>
        /// <param name="targetHwnd">要跟随的目标外部窗口句柄。必须为有效的非零句柄。</param>
        /// <param name="sidebarWindow">用作侧边栏的 WPF 窗口。不得为 <see langword="null"/>。</param>
        /// <exception cref="ArgumentException"><paramref name="targetHwnd"/> 为零或不是有效窗口。</exception>
        /// <exception cref="ArgumentNullException"><paramref name="sidebarWindow"/> 为 <see langword="null"/>。</exception>
        public HwndSidebarHost(IntPtr targetHwnd, Window sidebarWindow)
        {
            if (targetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(targetHwnd))
                throw new ArgumentException("targetHwnd 无效。", nameof(targetHwnd));

            if (sidebarWindow == null)
                throw new ArgumentNullException(nameof(sidebarWindow));

            _targetHwnd = targetHwnd;
            _sidebarWindow = sidebarWindow;
            _dispatcher = sidebarWindow.Dispatcher;

            sidebarWindow.ShowInTaskbar = false;
            sidebarWindow.Closed += OnSidebarClosed;

            _fallbackTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(FallbackPollMs)
            };
            _fallbackTimer.Tick += OnFallbackTick;
        }

        #endregion

        #region 公开属性

        /// <summary>
        /// 获取目标外部窗口句柄。
        /// </summary>
        public IntPtr TargetHwnd
        {
            get { return _targetHwnd; }
        }

        /// <summary>
        /// 获取当前侧边栏窗口。
        /// </summary>
        public Window SidebarWindow
        {
            get { return _sidebarWindow; }
        }

        /// <summary>
        /// 获取当前是否处于打开状态。
        /// </summary>
        public bool IsOpen
        {
            get { return _isActive; }
        }

        /// <summary>
        /// 获取或设置停靠方向。默认为 <see cref="OverlaySidebarDock.Right"/>。
        /// </summary>
        public OverlaySidebarDock Dock
        {
            get { return _dock; }
            set
            {
                ThrowIfDisposed();
                if (_dock == value) return;
                _dock = value;
                if (_isActive) ScheduleRefresh();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏在非停靠轴方向上的对齐方式。
        /// </summary>
        public OverlaySidebarAlignment Alignment
        {
            get { return _alignment; }
            set
            {
                ThrowIfDisposed();
                if (_alignment == value) return;
                _alignment = value;
                if (_isActive) ScheduleRefresh();
            }
        }

        /// <summary>
        /// 获取或设置侧边栏与目标窗口之间的间距（DIP）。
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">值小于 0。</exception>
        public double Gap
        {
            get { return _gap; }
            set
            {
                ThrowIfDisposed();
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                if (NearlyEquals(_gap, value)) return;
                _gap = value;
                if (_isActive) ScheduleRefresh();
            }
        }

        /// <summary>
        /// 获取或设置 Z 序策略。默认为 <see cref="SidebarZOrderMode.FollowTarget"/>。
        /// </summary>
        public SidebarZOrderMode ZOrderMode
        {
            get { return _zOrderMode; }
            set
            {
                ThrowIfDisposed();
                if (_zOrderMode == value) return;
                _zOrderMode = value;
                if (_isActive) SyncZOrder();
            }
        }

        #endregion

        #region 公开方法

        /// <summary>
        /// 打开侧边栏，开始跟随目标窗口。
        /// <para>该方法是幂等的；若已处于打开状态，则不执行任何操作。</para>
        /// </summary>
        /// <exception cref="ObjectDisposedException">当前实例已释放。</exception>
        /// <exception cref="InvalidOperationException">侧边栏窗口已关闭，当前 Host 不可复用。</exception>
        public void Open()
        {
            ThrowIfDisposed();
            if (_isActive) return;

            if (_sidebarWindowClosed)
                throw new InvalidOperationException("侧边栏窗口已关闭，当前 Host 已不可复用。请创建新的 Window 与 HwndSidebarHost。");

            _isActive = true;
            _zOrderTickCounter = 0;

            InstallHooks();
            _fallbackTimer.Start();

            ScheduleRefresh();
        }

        /// <summary>
        /// 关闭侧边栏，停止跟随目标窗口并隐藏侧边栏窗口。
        /// <para>该方法是幂等的；若已处于关闭状态，则不执行任何操作。</para>
        /// </summary>
        public void Close()
        {
            if (!_isActive) return;
            _isActive = false;

            UninstallHooks();
            _fallbackTimer.Stop();
            HideSidebar();
        }

        /// <summary>
        /// 切换侧边栏的打开/关闭状态。
        /// </summary>
        /// <exception cref="ObjectDisposedException">当前实例已释放（在尝试打开时）。</exception>
        public void Toggle()
        {
            ThrowIfDisposed();
            if (_isActive) Close();
            else Open();
        }

        /// <summary>
        /// 立即刷新目标窗口状态、侧边栏布局与 Z 序。
        /// <para>若当前处于关闭或已释放状态，则不执行任何操作。</para>
        /// </summary>
        public void Refresh()
        {
            ThrowIfDisposed();
            if (_isActive) ScheduleRefresh();
        }

        /// <summary>
        /// 立即按当前布局参数重新计算并应用侧边栏位置。
        /// <para>若当前处于关闭或已释放状态，则不执行任何操作。</para>
        /// <para>等价于 <see cref="Refresh"/>；此方法语义上用于"布局参数已在外部变化"的场景。</para>
        /// </summary>
        public void ApplyLayout()
        {
            ThrowIfDisposed();
            if (_isActive) ScheduleRefresh();
        }

        /// <summary>
        /// 释放宿主持有的所有资源，并隐藏侧边栏窗口（不销毁窗口本身）。
        /// <para>
        /// 该方法可从任意线程安全调用。UI 相关清理（隐藏窗口）会自动派发到
        /// 侧边栏窗口所属的 Dispatcher 线程执行。
        /// </para>
        /// <para>释放后对象不可再使用。</para>
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            // 先置位，阻断 WinEvent 线程池回调中的后续 BeginInvoke 入队
            _disposed = true;
            _isActive = false;

            UninstallHooks();

            // _fallbackTimer 和 _sidebarWindow 是 WPF 对象，必须在 Dispatcher 线程上操作
            if (_dispatcher.CheckAccess())
            {
                DisposeUIResources();
            }
            else
            {
                // 从非 Dispatcher 线程调用时，BeginInvoke 派发 UI 清理
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(DisposeUIResources));
            }
        }

        #endregion

        #region Dispose UI 清理（必须在 Dispatcher 线程执行）

        private void DisposeUIResources()
        {
            _fallbackTimer.Stop();
            _fallbackTimer.Tick -= OnFallbackTick;

            try { _sidebarWindow.Closed -= OnSidebarClosed; } catch { }

            HideSidebar();
            _lastAppliedBoundsDip = Rect.Empty;
        }

        #endregion

        #region Hook 安装与回调

        private void InstallHooks()
        {
            if (_winEventProc != null) return;

            _winEventProc = OnWinEvent;

            uint targetPid;
            NativeMethods.GetWindowThreadProcessId(_targetHwnd, out targetPid);

            uint flagsTargeted =
                (uint)WinEventHookFlags.WINEVENT_OUTOFCONTEXT |
                (uint)WinEventHookFlags.WINEVENT_SKIPOWNPROCESS;

            uint flagsGlobal =
                (uint)WinEventHookFlags.WINEVENT_OUTOFCONTEXT |
                (uint)WinEventHookFlags.WINEVENT_SKIPOWNPROCESS;

            // Hook 0：FOREGROUND 全局监听——任意窗口获得前台都可能影响 Z 序
            _hooks[SlotForeground] = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _winEventProc,
                0, 0, flagsGlobal);

            // Hook 1：MINIMIZESTART..MINIMIZEEND（range，2 个事件合 1 个 hook）
            _hooks[SlotMinimize] = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MINIMIZESTART,
                NativeMethods.EVENT_SYSTEM_MINIMIZEEND,
                IntPtr.Zero, _winEventProc,
                targetPid, 0, flagsTargeted);

            // Hook 2：DESTROY + SHOW + HIDE（range 0x8001-0x8003，3 个事件合 1 个 hook）
            _hooks[SlotVisibility] = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_DESTROY,
                NativeMethods.EVENT_OBJECT_HIDE,
                IntPtr.Zero, _winEventProc,
                targetPid, 0, flagsTargeted);

            // Hook 3：LOCATIONCHANGE
            _hooks[SlotLocation] = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero, _winEventProc,
                targetPid, 0, flagsTargeted);
        }

        private void UninstallHooks()
        {
            if (_winEventProc == null) return;

            // 先清空委托引用，再逐个 Unhook，防止重入时重复卸载
            _winEventProc = null;

            for (int i = 0; i < SlotCount; i++)
            {
                if (_hooks[i] == IntPtr.Zero) continue;
                try { NativeMethods.UnhookWinEvent(_hooks[i]); } catch { }
                _hooks[i] = IntPtr.Zero;
            }
        }

        private void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            // 此回调在线程池线程执行（WINEVENT_OUTOFCONTEXT）
            if (_disposed) return;

            // FOREGROUND 是全局事件：hwnd 是新获得前台的窗口，不一定是目标窗口。
            // 任意前台切换都可能改变目标窗口与侧边栏的 Z 序关系，无需过滤 hwnd。
            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(SyncZOrder));
                return;
            }

            // 其余事件只关注目标窗口自身（OBJID_WINDOW，idChild == 0）
            if (hwnd != _targetHwnd) return;
            if (idObject != NativeMethods.OBJID_WINDOW || idChild != 0) return;

            if (eventType == NativeMethods.EVENT_OBJECT_DESTROY)
            {
                // 目标窗口销毁，在 UI 线程上触发 Dispose
                _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Dispose));
                return;
            }

            // LOCATIONCHANGE / SHOW / HIDE / MINIMIZESTART / MINIMIZEEND
            ScheduleRefresh();
        }

        #endregion

        #region 刷新调度与核心逻辑

        /// <summary>
        /// 将一次刷新调度到 Dispatcher 队列。
        /// <para>
        /// 利用 <see cref="Interlocked.CompareExchange"/> 保证同一时刻最多只有一个待执行的刷新。
        /// 拖动窗口时 LOCATIONCHANGE 每像素触发一次（可达数百次/秒），若每次都入队一个
        /// Dispatcher 操作会快速堆积；此模式只保留最新的一次，既减少 UI 线程压力，
        /// 又确保最终能呈现最新位置。
        /// </para>
        /// <para>
        /// 使用 <see cref="DispatcherPriority.Render"/> 优先级，使位置更新在 WPF 渲染前完成，
        /// 侧边栏跟随延迟通常低于一个显示帧（16ms @ 60Hz）。
        /// </para>
        /// </summary>
        private void ScheduleRefresh()
        {
            // 提前守卫：_disposed 为 true 时不再入队，避免向已关闭 Dispatcher 投递操作
            if (_disposed) return;

            if (Interlocked.CompareExchange(ref _refreshPending, 1, 0) == 0)
            {
                _dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(ExecutePendingRefresh));
            }
        }

        private void ExecutePendingRefresh()
        {
            Interlocked.Exchange(ref _refreshPending, 0);

            // Close/Dispose 可能在此回调入队后、执行前完成，需在此处再次校验
            if (!_isActive || _disposed) return;

            RefreshCore();
        }

        private void RefreshCore()
        {
            if (!NativeMethods.IsWindow(_targetHwnd))
            {
                Dispose();
                return;
            }

            if (!NativeMethods.IsWindowVisible(_targetHwnd) || NativeMethods.IsIconic(_targetHwnd))
            {
                HideSidebar();
                return;
            }

            RECT targetRect;
            if (!WindowHelper.TryGetWindowBounds(_targetHwnd, out targetRect))
            {
                HideSidebar();
                return;
            }

            // 在此处一次性读取窗口尺寸，后续计算复用，避免同一帧内多次读取导致不一致
            double sw = ReadWindowWidth();
            double sh = ReadWindowHeight();
            if (sw <= 0 || sh <= 0)
            {
                HideSidebar();
                return;
            }

            Rect ownerDip = PhysicalRectToDip(targetRect, _targetHwnd);

            Rect rawBounds = OverlaySidebarLayout.CalculateSidebarBounds(
                ownerDip, _dock, _alignment, sw, sh, _gap);

            if (rawBounds.IsEmpty)
            {
                HideSidebar();
                return;
            }

            Rect finalBounds = NormalizeBoundsByAlignment(rawBounds, sw, sh);
            if (finalBounds.IsEmpty)
            {
                HideSidebar();
                return;
            }

            ApplyBounds(finalBounds);
            ShowSidebar();
        }

        #endregion

        #region 位置应用

        private void ApplyBounds(Rect boundsDip)
        {
            if (NearlyEqualsRect(_lastAppliedBoundsDip, boundsDip)) return;

            IntPtr sidebarHwnd = new WindowInteropHelper(_sidebarWindow).Handle;

            if (sidebarHwnd == IntPtr.Zero)
            {
                // 窗口尚未获得 HWND（Show 之前）：通过 WPF 属性设置初始位置
                _sidebarWindow.Left = boundsDip.X;
                _sidebarWindow.Top = boundsDip.Y;
                _sidebarWindow.Width = boundsDip.Width;
                _sidebarWindow.Height = boundsDip.Height;
            }
            else
            {
                // 已有 HWND：直接调用 Win32 SetWindowPos。
                // 相比写 WPF 的 Left/Top/Width/Height 属性（会触发布局管道），
                // SetWindowPos 更直接，延迟更低，适合高频更新场景。
                var dpi = DpiHelper.GetDpi(_sidebarWindow);
                int x = (int)Math.Round(boundsDip.X * dpi.DpiScaleX);
                int y = (int)Math.Round(boundsDip.Y * dpi.DpiScaleY);
                int w = Math.Max(1, (int)Math.Round(boundsDip.Width * dpi.DpiScaleX));
                int h = Math.Max(1, (int)Math.Round(boundsDip.Height * dpi.DpiScaleY));

                NativeMethods.SetWindowPos(
                    sidebarHwnd,
                    IntPtr.Zero,
                    x, y, w, h,
                    SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
            }

            _lastAppliedBoundsDip = boundsDip;
        }

        /// <summary>
        /// 根据对齐方式将布局计算结果修正为侧边栏实际应占用的矩形。
        /// <para>
        /// <paramref name="sw"/> / <paramref name="sh"/> 由调用方传入，
        /// 避免在同一帧内重复读取窗口尺寸。
        /// </para>
        /// </summary>
        private Rect NormalizeBoundsByAlignment(Rect bounds, double sw, double sh)
        {
            bool horizontal = _dock == OverlaySidebarDock.Left || _dock == OverlaySidebarDock.Right;

            double w, h;

            if (horizontal)
            {
                // 停靠轴（X）宽度固定为窗口自身宽度；
                // 非停靠轴（Y）Stretch 时撑满目标高度，否则用窗口自身高度。
                w = sw;
                h = _alignment == OverlaySidebarAlignment.Stretch ? bounds.Height : sh;
            }
            else
            {
                // 停靠轴（Y）高度固定为窗口自身高度；
                // 非停靠轴（X）Stretch 时撑满目标宽度，否则用窗口自身宽度。
                w = _alignment == OverlaySidebarAlignment.Stretch ? bounds.Width : sw;
                h = sh;
            }

            if (w <= 0 || h <= 0) return Rect.Empty;

            if (horizontal)
            {
                double y = AlignOffset(bounds.Top, bounds.Height, h, _alignment);
                return new Rect(bounds.X, y, w, h);
            }
            else
            {
                double x = AlignOffset(bounds.Left, bounds.Width, w, _alignment);
                return new Rect(x, bounds.Y, w, h);
            }
        }

        /// <summary>
        /// 根据对齐方式计算元素在容器区间内的起始偏移量。
        /// </summary>
        private static double AlignOffset(
            double containerStart,
            double containerSize,
            double elementSize,
            OverlaySidebarAlignment alignment)
        {
            switch (alignment)
            {
                case OverlaySidebarAlignment.Center:
                    return containerStart + (containerSize - elementSize) / 2.0;
                case OverlaySidebarAlignment.End:
                    return containerStart + containerSize - elementSize;
                default: // Start / Stretch
                    return containerStart;
            }
        }

        #endregion

        #region 可见性管理

        private void ShowSidebar()
        {
            if (_sidebarVisible) return;

            if (!_sidebarWindow.IsVisible)
                _sidebarWindow.Show();

            _sidebarVisible = true;
            SyncZOrder();
        }

        private void HideSidebar()
        {
            if (!_sidebarVisible && !_sidebarWindow.IsVisible) return;

            try
            {
                if (_sidebarWindow.IsVisible)
                    _sidebarWindow.Hide();
            }
            catch { }

            _sidebarVisible = false;
            _lastAppliedBoundsDip = Rect.Empty;
        }

        #endregion

        #region Z 序同步

        /// <summary>
        /// 将侧边栏 Z 序与目标窗口对齐。
        /// <para>
        /// 在 <see cref="SidebarZOrderMode.FollowTarget"/> 模式下：
        /// 先同步 Topmost 标志，再通过
        /// <c>SetWindowPos(hWndInsertAfter = _targetHwnd)</c>
        /// 将侧边栏精确地置于目标窗口正下方，使两者在 Z 序上紧密相邻。
        /// </para>
        /// <para>
        /// 该方法主动由 EVENT_SYSTEM_FOREGROUND 事件触发，
        /// 任意前台切换后即时响应，无需等待轮询周期。
        /// </para>
        /// </summary>
        private void SyncZOrder()
        {
            if (_zOrderMode != SidebarZOrderMode.FollowTarget) return;
            if (_disposed || !_isActive) return;

            IntPtr sidebarHwnd = new WindowInteropHelper(_sidebarWindow).Handle;
            if (sidebarHwnd == IntPtr.Zero) return;
            if (!NativeMethods.IsWindow(_targetHwnd)) return;

            bool topMost = IsWindowTopMost(_targetHwnd);

            if (_sidebarWindow.Topmost != topMost)
                _sidebarWindow.Topmost = topMost;

            WindowHelper.SetTopMost(sidebarHwnd, topMost, noActivate: true, showIfHidden: false);

            NativeMethods.SetWindowPos(
                sidebarHwnd,
                _targetHwnd,
                0, 0, 0, 0,
                SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
        }

        #endregion

        #region 事件处理

        private void OnSidebarClosed(object? sender, EventArgs e)
        {
            _sidebarWindowClosed = true;
            Dispose();
        }

        private void OnFallbackTick(object? sender, EventArgs e)
        {
            if (!_isActive || _disposed) return;

            // 主要职责：兜底位置同步（应对 WinEventHook 偶发丢失事件的场景）
            RefreshCore();

            // 次要职责：Z 序兜底（EVENT_SYSTEM_FOREGROUND 已覆盖绝大多数切换场景）
            _zOrderTickCounter++;
            if (_zOrderTickCounter >= ZOrderFallbackInterval)
            {
                _zOrderTickCounter = 0;
                SyncZOrder();
            }
        }

        #endregion

        #region 辅助方法

        private double ReadWindowWidth()
        {
            double w = _sidebarWindow.Width;
            if (double.IsNaN(w) || w <= 0) w = _sidebarWindow.ActualWidth;
            return w;
        }

        private double ReadWindowHeight()
        {
            double h = _sidebarWindow.Height;
            if (double.IsNaN(h) || h <= 0) h = _sidebarWindow.ActualHeight;
            return h;
        }

        /// <summary>
        /// 将目标窗口的物理像素矩形转换为 DIP 坐标。
        /// <para>
        /// 使用目标窗口所在显示器的 DPI 进行换算。在目标窗口与侧边栏跨越
        /// DPI 不同的两个显示器时，位置换算可能存在 1~2px 的近似误差，
        /// 属于当前架构的已知限制。
        /// </para>
        /// </summary>
        private static Rect PhysicalRectToDip(RECT rect, IntPtr hwnd)
        {
            var dpi = DpiHelper.GetDpiFromHwnd(hwnd);
            return new Rect(
                rect.Left / dpi.DpiScaleX,
                rect.Top / dpi.DpiScaleY,
                rect.Width / dpi.DpiScaleX,
                rect.Height / dpi.DpiScaleY);
        }

        private static bool IsWindowTopMost(IntPtr hwnd)
        {
            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            return (exStyle & (long)WindowStyles.WS_EX_TOPMOST) != 0;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HwndSidebarHost));
        }

        private static bool NearlyEquals(double a, double b)
        {
            return Math.Abs(a - b) < 0.01;
        }

        private static bool NearlyEqualsRect(Rect a, Rect b)
        {
            return NearlyEquals(a.X, b.X)
                && NearlyEquals(a.Y, b.Y)
                && NearlyEquals(a.Width, b.Width)
                && NearlyEquals(a.Height, b.Height);
        }

        #endregion
    }
}