using KkjQuicker.Utilities.Win32;
using System;

namespace KkjQuicker.Domain
{
    /// <summary>
    /// 提供应用级共享的前台窗口状态。
    /// </summary>
    /// <remarks>
    /// 该类型只负责复用一个 <see cref="ForegroundWindowMonitor"/> 实例，并提供常用状态的快捷读取入口。
    /// 在应用入口调用 <see cref="Start()"/>，各窗口读取属性或订阅 <see cref="ForegroundWindowChanged"/> 即可。
    /// </remarks>
    public static class AppState
    {
        private static readonly object SyncRoot = new object();

        private static ForegroundWindowMonitor? _monitor;

        /// <summary>
        /// 获取共享的前台窗口监控器。调用方不应释放该实例，应由 <see cref="Shutdown"/> 统一释放。
        /// </summary>
        public static ForegroundWindowMonitor Monitor
        {
            get => GetOrCreateMonitor();
        }

        /// <summary>当前前台窗口变化时触发。</summary>
        public static event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged
        {
            add
            {
                if (value == null)
                    return;

                GetOrCreateMonitor().ForegroundWindowChanged += value;
            }
            remove
            {
                if (value == null)
                    return;

                ForegroundWindowMonitor? monitor = GetMonitor();
                if (monitor != null)
                    monitor.ForegroundWindowChanged -= value;
            }
        }

        /// <summary>获取监控器是否已经启动。</summary>
        public static bool IsStarted => GetMonitor()?.IsRunning == true;

        /// <summary>获取当前前台窗口信息。</summary>
        public static ForegroundWindowInfo? CurrentWindow => GetMonitor()?.CurrentWindow;

        /// <summary>获取上一个前台窗口信息。</summary>
        public static ForegroundWindowInfo? PreviousWindow => GetMonitor()?.PreviousWindow;

        /// <summary>获取最近一个非当前进程的前台窗口信息。</summary>
        public static ForegroundWindowInfo? LastExternalWindow => GetMonitor()?.LastExternalWindow;

        /// <summary>获取当前前台窗口句柄。</summary>
        public static IntPtr CurrentHwnd => GetMonitor()?.CurrentHwnd ?? IntPtr.Zero;

        /// <summary>获取上一个前台窗口句柄。</summary>
        public static IntPtr PreviousHwnd => PreviousWindow?.Handle ?? IntPtr.Zero;

        /// <summary>获取最近一个非当前进程窗口的句柄。</summary>
        public static IntPtr LastExternalHwnd => GetMonitor()?.LastExternalHwnd ?? IntPtr.Zero;

        /// <summary>获取当前前台窗口所属进程 ID。</summary>
        public static int CurrentProcessId => CurrentWindow?.Pid ?? 0;

        /// <summary>获取当前前台窗口所属进程名称。</summary>
        public static string CurrentProcessName => CurrentWindow?.ProcessName ?? string.Empty;

        /// <summary>获取当前前台窗口所属可执行文件名称。</summary>
        public static string CurrentExeName => CurrentWindow?.ExeName ?? string.Empty;

        /// <summary>获取当前前台窗口所属可执行文件路径。</summary>
        public static string CurrentExePath => CurrentWindow?.ExePath ?? string.Empty;

        /// <summary>获取当前前台窗口标题。</summary>
        public static string CurrentWindowTitle => CurrentWindow?.Title ?? string.Empty;

        /// <summary>获取最近一个非当前进程窗口所属可执行文件路径。</summary>
        public static string LastExternalExePath => LastExternalWindow?.ExePath ?? string.Empty;

        /// <summary>获取或设置事件是否投递到启动时捕获的 SynchronizationContext。</summary>
        public static bool MarshalEventToContext
        {
            get => GetOrCreateMonitor().MarshalEventToContext;
            set { GetOrCreateMonitor().MarshalEventToContext = value; }
        }

        /// <summary>获取或设置要忽略的前台窗口过滤条件。</summary>
        public static Func<ForegroundWindowInfo, bool>? IgnorePredicate
        {
            get => GetOrCreateMonitor().IgnorePredicate;
            set { GetOrCreateMonitor().IgnorePredicate = value; }
        }

        /// <summary>
        /// 启动共享前台窗口监控器。
        /// </summary>
        /// <param name="refreshImmediately">是否立即刷新当前前台窗口状态。</param>
        public static void Start(bool refreshImmediately = true)
        {
            GetOrCreateMonitor().Start(refreshImmediately);
        }

        /// <summary>停止共享前台窗口监控器，但保留实例以便后续重新启动。</summary>
        public static void Stop()
        {
            GetMonitor()?.Stop();
        }

        /// <summary>刷新当前前台窗口状态。</summary>
        public static void Refresh(bool forceRaise = true)
        {
            GetMonitor()?.Refresh(forceRaise);
        }

        /// <summary>尝试激活最近一个非当前进程窗口。</summary>
        public static bool TryActivateLastExternalWindow()
        {
            return GetMonitor()?.TryActivateLastExternalWindow() == true;
        }

        /// <summary>停止并释放共享前台窗口监控器。</summary>
        public static void Shutdown()
        {
            ForegroundWindowMonitor? monitor;

            lock (SyncRoot)
            {
                monitor = _monitor;
                _monitor = null;
            }

            if (monitor == null)
                return;

            monitor.Stop();
            monitor.Dispose();
        }

        private static ForegroundWindowMonitor GetOrCreateMonitor()
        {
            lock (SyncRoot)
            {
                if (_monitor == null)
                    _monitor = new ForegroundWindowMonitor();

                return _monitor;
            }
        }

        private static ForegroundWindowMonitor? GetMonitor()
        {
            lock (SyncRoot)
                return _monitor;
        }
    }
}
