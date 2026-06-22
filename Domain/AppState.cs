using KkjQuicker.Utilities.Win32;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace KkjQuicker.Domain
{
    /// <summary>
    /// 表示进程级应用状态的变化类型。
    /// </summary>
    [Flags]
    public enum AppStateChangeKind
    {
        None = 0,
        Monitoring = 1,
        ForegroundWindow = 2,
        ForegroundProcess = 4,
        WindowTitle = 8,
        LastExternalWindow = 16,
        Clipboard = 32,
        Paste = 64
    }

    /// <summary>
    /// 表示某一时刻的进程级应用状态快照。
    /// </summary>
    /// <remarks>
    /// 实例不可变，可安全地由多个窗口或线程同时读取。
    /// </remarks>
    public sealed class AppStateSnapshot
    {
        internal static readonly AppStateSnapshot Empty = new AppStateSnapshot(
            false,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0);

        internal AppStateSnapshot(
            bool isMonitoring,
            IntPtr foregroundWindow,
            IntPtr previousForegroundWindow,
            IntPtr lastExternalWindow,
            int processId,
            string processName,
            string exeName,
            string exePath,
            string windowTitle,
            long lastClipboardChangeTime,
            long lastPasteTime)
        {
            IsMonitoring = isMonitoring;
            ForegroundWindow = foregroundWindow;
            PreviousForegroundWindow = previousForegroundWindow;
            LastExternalWindow = lastExternalWindow;
            ProcessId = processId;
            ProcessName = processName ?? string.Empty;
            ExeName = exeName ?? string.Empty;
            ExePath = exePath ?? string.Empty;
            WindowTitle = windowTitle ?? string.Empty;
            LastClipboardChangeTime = lastClipboardChangeTime;
            LastPasteTime = lastPasteTime;
        }

        /// <summary>获取前台窗口监控是否已经启动。</summary>
        public bool IsMonitoring { get; private set; }

        /// <summary>获取当前前台窗口句柄。</summary>
        public IntPtr ForegroundWindow { get; private set; }

        /// <summary>获取上一个前台窗口句柄。</summary>
        public IntPtr PreviousForegroundWindow { get; private set; }

        /// <summary>获取最近一个非当前进程窗口的句柄。</summary>
        public IntPtr LastExternalWindow { get; private set; }

        /// <summary>获取当前前台窗口所属进程 ID。</summary>
        public int ProcessId { get; private set; }

        /// <summary>获取当前前台窗口所属进程名称。</summary>
        public string ProcessName { get; private set; }

        /// <summary>获取当前前台窗口所属可执行文件名称。</summary>
        public string ExeName { get; private set; }

        /// <summary>获取当前前台窗口所属可执行文件路径。</summary>
        public string ExePath { get; private set; }

        /// <summary>获取当前前台窗口标题。</summary>
        public string WindowTitle { get; private set; }

        /// <summary>
        /// 获取最近一次剪贴板变化时的 <see cref="Environment.TickCount"/>。
        /// 0 表示尚未记录。
        /// </summary>
        public long LastClipboardChangeTime { get; private set; }

        /// <summary>
        /// 获取最近一次粘贴时的 <see cref="Environment.TickCount"/>。
        /// 0 表示尚未记录。
        /// </summary>
        public long LastPasteTime { get; private set; }

        internal AppStateSnapshot WithMonitoring(bool isMonitoring)
        {
            return new AppStateSnapshot(
                isMonitoring,
                ForegroundWindow,
                PreviousForegroundWindow,
                LastExternalWindow,
                ProcessId,
                ProcessName,
                ExeName,
                ExePath,
                WindowTitle,
                LastClipboardChangeTime,
                LastPasteTime);
        }

        internal AppStateSnapshot WithForeground(
            ForegroundWindowInfo? activated,
            ForegroundWindowInfo? deactivated,
            ForegroundWindowInfo? lastExternal)
        {
            return new AppStateSnapshot(
                IsMonitoring,
                activated == null ? IntPtr.Zero : activated.Handle,
                deactivated == null ? ForegroundWindow : deactivated.Handle,
                lastExternal == null ? LastExternalWindow : lastExternal.Handle,
                activated == null ? 0 : activated.Pid,
                activated == null ? string.Empty : activated.ProcessName,
                activated == null ? string.Empty : activated.ExeName,
                activated == null ? string.Empty : activated.ExePath,
                activated == null ? string.Empty : activated.Title,
                LastClipboardChangeTime,
                LastPasteTime);
        }

        internal AppStateSnapshot WithClipboardTime(long value)
        {
            return new AppStateSnapshot(
                IsMonitoring,
                ForegroundWindow,
                PreviousForegroundWindow,
                LastExternalWindow,
                ProcessId,
                ProcessName,
                ExeName,
                ExePath,
                WindowTitle,
                value,
                LastPasteTime);
        }

        internal AppStateSnapshot WithPasteTime(long value)
        {
            return new AppStateSnapshot(
                IsMonitoring,
                ForegroundWindow,
                PreviousForegroundWindow,
                LastExternalWindow,
                ProcessId,
                ProcessName,
                ExeName,
                ExePath,
                WindowTitle,
                LastClipboardChangeTime,
                value);
        }
    }

    /// <summary>
    /// 表示一次进程级应用状态变化。
    /// </summary>
    public sealed class AppStateChangedEventArgs : EventArgs
    {
        internal AppStateChangedEventArgs(
            AppStateSnapshot previous,
            AppStateSnapshot current,
            AppStateChangeKind changes,
            bool isInitial)
        {
            Previous = previous ?? AppStateSnapshot.Empty;
            Current = current ?? AppStateSnapshot.Empty;
            Changes = changes;
            IsInitial = isInitial;
        }

        /// <summary>获取变化前的状态快照。</summary>
        public AppStateSnapshot Previous { get; private set; }

        /// <summary>获取变化后的状态快照。</summary>
        public AppStateSnapshot Current { get; private set; }

        /// <summary>获取本次发生变化的状态类别。</summary>
        public AppStateChangeKind Changes { get; private set; }

        /// <summary>获取该通知是否为订阅时主动发送的初始快照。</summary>
        public bool IsInitial { get; private set; }
    }

    /// <summary>
    /// 提供进程级共享状态与唯一的前台窗口监控。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类型的生命周期属于整个进程，不属于任何窗口。
    /// 应在应用启动入口调用一次 <see cref="Start"/>，在应用退出入口调用一次
    /// <see cref="Stop"/>；各窗口只读取 <see cref="Current"/> 或通过
    /// <see cref="Subscribe"/> 订阅变化。
    /// </para>
    /// <para>
    /// 所有状态均以不可变快照发布。事件处理程序在锁外调用，一个订阅者的异常不会阻止其他订阅者。
    /// </para>
    /// </remarks>
    public static class AppState
    {
        private static readonly object SyncRoot = new object();

        private static ForegroundWindowMonitor? _foregroundMonitor = null!;
        private static Dispatcher? _dispatcher = null!;
        private static AppStateSnapshot _current = AppStateSnapshot.Empty;
        private static EventHandler<AppStateChangedEventArgs>? _changed;
        private static int _uiThreadId;

        /// <summary>
        /// 当进程级状态发生变化时触发。
        /// </summary>
        /// <remarks>
        /// 静态事件要求调用方在不再使用时解除订阅。一般优先使用 <see cref="Subscribe"/>，
        /// 通过释放返回的令牌自动解除订阅。
        /// </remarks>
        public static event EventHandler<AppStateChangedEventArgs>? Changed
        {
            add
            {
                if (value == null)
                    return;

                lock (SyncRoot)
                    _changed += value;
            }
            remove
            {
                if (value == null)
                    return;

                lock (SyncRoot)
                    _changed -= value;
            }
        }

        /// <summary>获取当前完整状态快照。</summary>
        public static AppStateSnapshot Current
        {
            get
            {
                lock (SyncRoot)
                    return _current;
            }
        }

        /// <summary>获取前台窗口监控是否已启动。</summary>
        public static bool IsStarted
        {
            get { return Current.IsMonitoring; }
        }

        /// <summary>获取当前前台窗口句柄。</summary>
        public static IntPtr ForegroundWindow
        {
            get { return Current.ForegroundWindow; }
        }

        /// <summary>获取上一个前台窗口句柄。</summary>
        public static IntPtr PreviousForegroundWindow
        {
            get { return Current.PreviousForegroundWindow; }
        }

        /// <summary>获取最近一个非当前进程窗口的句柄。</summary>
        public static IntPtr LastExternalWindow
        {
            get { return Current.LastExternalWindow; }
        }

        /// <summary>
        /// 获取最近一个非当前进程窗口的句柄。
        /// 保留该属性用于兼容旧调用，建议新代码使用 <see cref="LastExternalWindow"/>。
        /// </summary>
        public static IntPtr LastForegroundWindow
        {
            get { return Current.LastExternalWindow; }
        }

        public static int CurrentProcessId { get { return Current.ProcessId; } }
        public static string CurrentProcessName { get { return Current.ProcessName; } }
        public static string CurrentExeName { get { return Current.ExeName; } }
        public static string CurrentExePath { get { return Current.ExePath; } }
        public static string CurrentWindowTitle { get { return Current.WindowTitle; } }
        public static long LastClipboardChangeTime { get { return Current.LastClipboardChangeTime; } }
        public static long LastPasteTime { get { return Current.LastPasteTime; } }
        public static int UiThreadId { get { return Volatile.Read(ref _uiThreadId); } }

        /// <summary>
        /// 启动进程级前台窗口监控。
        /// </summary>
        /// <remarks>
        /// 该方法是幂等的，但进程内只能绑定一个 UI Dispatcher。
        /// 应由应用入口调用，而不是由各个窗口调用。
        /// </remarks>
        public static void Start(Dispatcher dispatcher)
        {
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                throw new InvalidOperationException("无法使用已经关闭的 Dispatcher 启动 AppState。");

            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(new Action(delegate { Start(dispatcher); }));
                return;
            }

            ForegroundWindowMonitor monitor;
            AppStateSnapshot previous;
            AppStateSnapshot current;

            lock (SyncRoot)
            {
                if (_foregroundMonitor != null)
                {
                    if (!ReferenceEquals(_dispatcher, dispatcher))
                        throw new InvalidOperationException("AppState 已绑定到另一个 Dispatcher。");

                    return;
                }

                monitor = new ForegroundWindowMonitor();
                monitor.ForegroundWindowChanged += OnForegroundChanged;

                _foregroundMonitor = monitor;
                _dispatcher = dispatcher;
                Volatile.Write(ref _uiThreadId, Thread.CurrentThread.ManagedThreadId);

                previous = _current;
                current = previous.WithMonitoring(true);
                _current = current;
            }

            try
            {
                monitor.Start();
            }
            catch
            {
                lock (SyncRoot)
                {
                    if (ReferenceEquals(_foregroundMonitor, monitor))
                    {
                        _foregroundMonitor = null;
                        _dispatcher = null;
                        _current = previous;
                        Volatile.Write(ref _uiThreadId, 0);
                    }
                }

                monitor.ForegroundWindowChanged -= OnForegroundChanged;
                monitor.Dispose();
                throw;
            }

            Publish(previous, current, AppStateChangeKind.Monitoring, dispatcher);
        }

        /// <summary>
        /// 停止进程级前台窗口监控。
        /// </summary>
        /// <remarks>
        /// 应由应用退出入口调用。窗口关闭时不应调用本方法。
        /// </remarks>
        public static void Stop()
        {
            Dispatcher? dispatcher;

            lock (SyncRoot)
                dispatcher = _dispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                    return;

                dispatcher.Invoke(new Action(Stop));
                return;
            }

            ForegroundWindowMonitor? monitor;
            AppStateSnapshot previous;
            AppStateSnapshot current;

            lock (SyncRoot)
            {
                monitor = _foregroundMonitor;
                if (monitor == null)
                    return;

                _foregroundMonitor = null;
                previous = _current;
                current = previous.WithMonitoring(false);
                _current = current;
                _dispatcher = null;
                Volatile.Write(ref _uiThreadId, 0);
            }

            monitor.ForegroundWindowChanged -= OnForegroundChanged;
            monitor.Dispose();

            Publish(previous, current, AppStateChangeKind.Monitoring, dispatcher);
        }

        /// <summary>
        /// 订阅状态变化，并返回用于解除订阅的令牌。
        /// </summary>
        /// <param name="handler">状态变化处理程序。</param>
        /// <param name="notifyImmediately">是否立即发送一次当前快照。</param>
        public static IDisposable Subscribe(
            EventHandler<AppStateChangedEventArgs> handler,
            bool notifyImmediately = true)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var subscription = new AppStateSubscription(handler);
            Changed += subscription.Handler;

            if (notifyImmediately)
            {
                AppStateSnapshot snapshot = Current;
                Dispatcher? dispatcher;

                lock (SyncRoot)
                    dispatcher = _dispatcher;

                PublishToHandler(
                    subscription.Handler,
                    new AppStateChangedEventArgs(
                        snapshot,
                        snapshot,
                        AppStateChangeKind.None,
                        true),
                    dispatcher);
            }

            return subscription;
        }

        /// <summary>记录一次剪贴板变化并向订阅者发布。</summary>
        public static void LogClipboardChange()
        {
            UpdateActivityTime(true);
        }

        /// <summary>记录一次粘贴并向订阅者发布。</summary>
        public static void LogPaste()
        {
            UpdateActivityTime(false);
        }

        private static void UpdateActivityTime(bool clipboard)
        {
            long value = Environment.TickCount;
            AppStateSnapshot previous;
            AppStateSnapshot current;
            Dispatcher? dispatcher;

            lock (SyncRoot)
            {
                previous = _current;
                current = clipboard
                    ? previous.WithClipboardTime(value)
                    : previous.WithPasteTime(value);
                _current = current;
                dispatcher = _dispatcher;
            }

            Publish(
                previous,
                current,
                clipboard ? AppStateChangeKind.Clipboard : AppStateChangeKind.Paste,
                dispatcher);
        }

        private static void OnForegroundChanged(
            object? sender,
            ForegroundWindowChangedEventArgs? e)
        {
            if (e == null || e.ActivatedWindow == null)
                return;

            ForegroundWindowMonitor? monitor = sender as ForegroundWindowMonitor;
            AppStateSnapshot previous;
            AppStateSnapshot current;
            Dispatcher? dispatcher;
            AppStateChangeKind changes;

            lock (SyncRoot)
            {
                if (!ReferenceEquals(_foregroundMonitor, monitor))
                    return;

                previous = _current;
                current = previous.WithForeground(
                    e.ActivatedWindow,
                    e.DeactivatedWindow,
                    monitor == null ? null : monitor.LastExternalWindow);
                _current = current;
                dispatcher = _dispatcher;
                changes = GetForegroundChanges(previous, current);
            }

            if (changes != AppStateChangeKind.None)
                Publish(previous, current, changes, dispatcher);
        }

        private static AppStateChangeKind GetForegroundChanges(
            AppStateSnapshot previous,
            AppStateSnapshot current)
        {
            AppStateChangeKind changes = AppStateChangeKind.None;

            if (previous.ForegroundWindow != current.ForegroundWindow ||
                previous.PreviousForegroundWindow != current.PreviousForegroundWindow)
            {
                changes |= AppStateChangeKind.ForegroundWindow;
            }

            if (previous.ProcessId != current.ProcessId ||
                !string.Equals(previous.ProcessName, current.ProcessName, StringComparison.Ordinal) ||
                !string.Equals(previous.ExeName, current.ExeName, StringComparison.Ordinal) ||
                !string.Equals(previous.ExePath, current.ExePath, StringComparison.Ordinal))
            {
                changes |= AppStateChangeKind.ForegroundProcess;
            }

            if (!string.Equals(previous.WindowTitle, current.WindowTitle, StringComparison.Ordinal))
                changes |= AppStateChangeKind.WindowTitle;

            if (previous.LastExternalWindow != current.LastExternalWindow)
                changes |= AppStateChangeKind.LastExternalWindow;

            return changes;
        }

        private static void Publish(
            AppStateSnapshot previous,
            AppStateSnapshot current,
            AppStateChangeKind changes,
            Dispatcher? dispatcher)
        {
            EventHandler<AppStateChangedEventArgs>? handler;

            lock (SyncRoot)
                handler = _changed;

            if (handler == null)
                return;

            var args = new AppStateChangedEventArgs(previous, current, changes, false);

            foreach (EventHandler<AppStateChangedEventArgs> single in handler.GetInvocationList())
                PublishToHandler(single, args, dispatcher);
        }

        private static void PublishToHandler(
            EventHandler<AppStateChangedEventArgs> handler,
            AppStateChangedEventArgs args,
            Dispatcher? dispatcher)
        {
            if (handler == null)
                return;

            Action invoke = delegate
            {
                try
                {
                    handler(null, args);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("AppState 状态订阅者执行失败：{0}", ex);
                }
            };

            if (dispatcher == null ||
                dispatcher.CheckAccess() ||
                dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
            {
                invoke();
                return;
            }

            try
            {
                dispatcher.BeginInvoke(invoke, DispatcherPriority.DataBind);
            }
            catch (InvalidOperationException)
            {
                invoke();
            }
            catch (TaskCanceledException)
            {
            }
        }

        private sealed class AppStateSubscription : IDisposable
        {
            private EventHandler<AppStateChangedEventArgs>? _target;

            public EventHandler<AppStateChangedEventArgs> Handler { get; private set; } = null!;

            public AppStateSubscription(EventHandler<AppStateChangedEventArgs> handler)
            {
                _target = handler;
                Handler = Forward;
            }

            private void Forward(object? sender, AppStateChangedEventArgs e)
            {
                EventHandler<AppStateChangedEventArgs>? target =
                    Volatile.Read(ref _target);

                if (target != null)
                    target(sender, e);
            }

            public void Dispose()
            {
                EventHandler<AppStateChangedEventArgs>? target =
                    Interlocked.Exchange(ref _target, null);

                if (target != null)
                    Changed -= Handler;
            }
        }
    }
}
