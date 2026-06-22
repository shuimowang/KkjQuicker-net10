using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KkjQuicker.Utilities.Win32
{
    public sealed class ForegroundWindowInfo
    {
        private string _className = null!;

        public IntPtr Handle { get; internal set; }

        public int Pid { get; internal set; }

        public string ProcessName { get; internal set; }

        public string ExeName { get; internal set; }

        public string ExePath { get; internal set; }

        public string Title { get; internal set; }

        public string ClassName
        {
            get
            {
                if (_className == null)
                    _className = WindowHelper.GetClassName(Handle) ?? string.Empty;

                return _className;
            }
        }

        public bool IsEmpty
        {
            get { return Handle == IntPtr.Zero; }
        }

        public override string ToString()
        {
            return string.Format(
                "{0} | {1} | {2}",
                ExeName ?? string.Empty,
                Title ?? string.Empty,
                Handle);
        }
    }

    public sealed class ForegroundWindowChangedEventArgs : EventArgs
    {
        public ForegroundWindowInfo ActivatedWindow { get; private set; }

        public ForegroundWindowInfo DeactivatedWindow { get; private set; }

        public ForegroundWindowChangedEventArgs(
            ForegroundWindowInfo activatedWindow,
            ForegroundWindowInfo deactivatedWindow)
        {
            ActivatedWindow = activatedWindow;
            DeactivatedWindow = deactivatedWindow;
        }
    }

    public sealed class ForegroundWindowMonitor : IDisposable
    {
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private readonly object _syncRoot = new object();
        private readonly NativeMethods.WinEventDelegate _callback;
        private readonly int _currentProcessId;

        private IntPtr _hook;
        private volatile bool _isDisposed;
        private volatile bool _isRunning;
        private int _version;
        private SynchronizationContext _synchronizationContext;

        private volatile ForegroundWindowInfo _currentWindow;
        private volatile ForegroundWindowInfo _previousWindow;
        private volatile ForegroundWindowInfo _lastExternalWindow;

        // volatile backing field，确保后台任务读取到最新值
        private volatile bool _marshalEventToContext;

        // volatile backing field，确保后台任务读取到最新值
        private volatile Func<ForegroundWindowInfo, bool> _ignorePredicate;

        public ForegroundWindowInfo CurrentWindow
        {
            get { return _currentWindow; }
        }

        public ForegroundWindowInfo PreviousWindow
        {
            get { return _previousWindow; }
        }

        public ForegroundWindowInfo LastExternalWindow
        {
            get { return _lastExternalWindow; }
        }

        public IntPtr CurrentHwnd
        {
            get
            {
                ForegroundWindowInfo info = _currentWindow;
                return info == null ? IntPtr.Zero : info.Handle;
            }
        }

        public IntPtr LastExternalHwnd
        {
            get
            {
                ForegroundWindowInfo info = _lastExternalWindow;
                return info == null ? IntPtr.Zero : info.Handle;
            }
        }

        public bool IsRunning
        {
            get { return _isRunning; }
        }

        /// <summary>
        /// 为 true 时，<see cref="ForegroundWindowChanged"/> 事件在 <see cref="Start()"/> 调用时
        /// 捕获的 <see cref="SynchronizationContext"/> 上触发；为 false 时在后台任务线程上直接触发。
        /// </summary>
        public bool MarshalEventToContext
        {
            get { return _marshalEventToContext; }
            set { _marshalEventToContext = value; }
        }

        public Func<ForegroundWindowInfo, bool> IgnorePredicate
        {
            get { return _ignorePredicate; }
            set { _ignorePredicate = value; }
        }

        public event EventHandler<ForegroundWindowChangedEventArgs>? ForegroundWindowChanged;

        public ForegroundWindowMonitor()
        {
            _callback = OnWinEvent;

            using (Process current = Process.GetCurrentProcess())
                _currentProcessId = current.Id;

            MarshalEventToContext = true;
        }

        public void Start()
        {
            Start(true);
        }

        /// <summary>
        /// 启动前台窗口监听。若 <paramref name="refreshImmediately"/> 为 <see langword="true"/>，
        /// 则立即获取当前前台窗口并触发一次 <see cref="ForegroundWindowChanged"/>。
        /// </summary>
        /// <param name="refreshImmediately">是否在启动后立即刷新当前前台窗口状态。</param>
        /// <remarks>
        /// 本方法及 <see cref="Stop"/>、<see cref="Dispose"/> 必须在同一线程上调用，
        /// 建议在 UI 线程或具有消息循环的线程上启动，以便 WinEvent 回调和 SynchronizationContext 事件投递行为保持稳定。
        /// 跨线程调用行为未定义。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">对象已释放。</exception>
        /// <exception cref="InvalidOperationException"><c>SetWinEventHook</c> 调用失败。</exception>
        public void Start(bool refreshImmediately)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (_hook != IntPtr.Zero)
                    return;

                _synchronizationContext = SynchronizationContext.Current;

                IntPtr hook = NativeMethods.SetWinEventHook(
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    NativeMethods.EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _callback,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT);

                if (hook == IntPtr.Zero)
                    throw new InvalidOperationException("SetWinEventHook failed.");

                _hook = hook;
                _isRunning = true;
            }

            if (refreshImmediately)
                Refresh(false);
        }

        /// <summary>
        /// 停止前台窗口监听。调用后已入队但尚未执行的后台任务将被放弃，不再触发事件。
        /// </summary>
        /// <remarks>
        /// 必须与 <see cref="Start()"/> 在同一线程上调用，详见 <see cref="Start(bool)"/> 备注。
        /// </remarks>
        public void Stop()
        {
            IntPtr hook;

            lock (_syncRoot)
            {
                if (_hook == IntPtr.Zero && !_isRunning)
                    return;

                hook = _hook;
                _hook = IntPtr.Zero;
                _isRunning = false;

                Interlocked.Increment(ref _version);
            }

            if (hook != IntPtr.Zero)
                NativeMethods.UnhookWinEvent(hook);
        }

        public void Refresh()
        {
            Refresh(true);
        }

        public void Refresh(bool forceRaise)
        {
            ThrowIfDisposed();

            if (!_isRunning)
                return;

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            UpdateByHwnd(hwnd, forceRaise);
        }

        public bool IsCurrentProcess(ForegroundWindowInfo info)
        {
            return info != null && info.Pid == _currentProcessId;
        }

        public bool IsProcessName(ForegroundWindowInfo info, string processName)
        {
            if (info == null || string.IsNullOrEmpty(processName))
                return false;

            return string.Equals(info.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(info.ExeName, processName, StringComparison.OrdinalIgnoreCase);
        }

        public bool TryActivateLastExternalWindow()
        {
            ForegroundWindowInfo info = _lastExternalWindow;

            if (info == null || info.Handle == IntPtr.Zero)
                return false;

            if (!NativeMethods.IsWindow(info.Handle))
                return false;

            return NativeMethods.SetForegroundWindow(info.Handle);
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
            if (_isDisposed || !_isRunning)
                return;

            IntPtr hook = _hook;
            if (hook == IntPtr.Zero || hook != hWinEventHook)
                return;

            if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND)
                return;

            if (hwnd == IntPtr.Zero)
                return;

            UpdateByHwnd(hwnd, false);
        }

        private void UpdateByHwnd(IntPtr hwnd, bool forceRaise)
        {
            if (_isDisposed || !_isRunning)
                return;

            if (hwnd == IntPtr.Zero)
                return;

            if (!NativeMethods.IsWindow(hwnd))
                return;

            int pid = WindowHelper.GetWindowProcessId(hwnd);

            ForegroundWindowInfo current = _currentWindow;
            if (!forceRaise && current != null && current.Handle == hwnd && current.Pid == pid)
                return;

            int version = Interlocked.Increment(ref _version);

            Task task = Task.Run(delegate
            {
                if (!IsSameWindow(hwnd, pid))
                    return;

                ForegroundWindowInfo info = CreateWindowInfo(hwnd, pid);

                if (!IsActiveVersion(version))
                    return;

                if (!IsSameWindow(hwnd, pid))
                    return;

                ApplyWindowInfo(info, forceRaise, version);
            });

            ObserveBackgroundTask(task);
        }

        private bool IsActiveVersion(int version)
        {
            return !_isDisposed && _isRunning && version == Volatile.Read(ref _version);
        }

        private static bool IsSameWindow(IntPtr hwnd, int pid)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            if (!NativeMethods.IsWindow(hwnd))
                return false;

            return WindowHelper.GetWindowProcessId(hwnd) == pid;
        }

        private static void ObserveBackgroundTask(Task task)
        {
            task.ContinueWith(delegate (Task faultedTask)
            {
                AggregateException exception = faultedTask.Exception;
                if (exception != null)
                    Trace.TraceError("ForegroundWindowMonitor background task failed: {0}", exception.Flatten());
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        }

        private void ApplyWindowInfo(ForegroundWindowInfo info, bool forceRaise, int version)
        {
            if (!IsActiveVersion(version))
                return;

            if (ShouldIgnore(info))
                return;

            ForegroundWindowInfo oldWindow;

            lock (_syncRoot)
            {
                if (!IsActiveVersion(version))
                    return;

                if (!forceRaise && _currentWindow != null && _currentWindow.Handle == info.Handle && _currentWindow.Pid == info.Pid)
                    return;

                oldWindow = _currentWindow;
                _previousWindow = oldWindow;
                _currentWindow = info;

                if (!IsCurrentProcess(info))
                    _lastExternalWindow = info;
            }

            RaiseForegroundWindowChanged(info, oldWindow, version);
        }

        private bool ShouldIgnore(ForegroundWindowInfo info)
        {
            Func<ForegroundWindowInfo, bool> ignorePredicate = IgnorePredicate;
            if (ignorePredicate == null)
                return false;

            try
            {
                return ignorePredicate(info);
            }
            catch (Exception exception)
            {
                Trace.TraceError("ForegroundWindowMonitor IgnorePredicate failed: {0}", exception);
                return false;
            }
        }

        private void RaiseForegroundWindowChanged(
            ForegroundWindowInfo activatedWindow,
            ForegroundWindowInfo deactivatedWindow,
            int version)
        {
            EventHandler<ForegroundWindowChangedEventArgs> handler = ForegroundWindowChanged;
            if (handler == null)
                return;

            SynchronizationContext context = _synchronizationContext;

            if (MarshalEventToContext && context != null)
            {
                context.Post(delegate
                {
                    if (!IsActiveVersion(version))
                        return;

                    EventHandler<ForegroundWindowChangedEventArgs> h = ForegroundWindowChanged;
                    if (h == null)
                        return;

                    ForegroundWindowChangedEventArgs args = new ForegroundWindowChangedEventArgs(
                        activatedWindow,
                        deactivatedWindow);

                    h(this, args);
                }, null);

                return;
            }

            if (!IsActiveVersion(version))
                return;

            ForegroundWindowChangedEventArgs directArgs = new ForegroundWindowChangedEventArgs(
                activatedWindow,
                deactivatedWindow);

            handler(this, directArgs);
        }

        private ForegroundWindowInfo CreateWindowInfo(IntPtr hwnd, int pid)
        {
            ForegroundWindowInfo info = new ForegroundWindowInfo();
            info.Handle = hwnd;
            info.Pid = pid;
            info.Title = WindowHelper.GetWindowTitle(hwnd);

            if (pid <= 0)
            {
                info.ProcessName = string.Empty;
                info.ExeName = string.Empty;
                info.ExePath = string.Empty;
                return info;
            }

            string processName = string.Empty;

            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    processName = process.ProcessName ?? string.Empty;
                }
            }
            catch
            {
                processName = string.Empty;
            }

            string exePath = WindowHelper.GetProcessFilePath(pid);

            info.ProcessName = processName;
            info.ExePath = exePath ?? string.Empty;

            if (!string.IsNullOrEmpty(exePath))
            {
                info.ExeName = Path.GetFileName(exePath);
            }
            else if (!string.IsNullOrEmpty(processName))
            {
                info.ExeName = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? processName
                    : processName + ".exe";
            }
            else
            {
                info.ExeName = string.Empty;
            }

            return info;
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Stop();

            _isDisposed = true;
            ForegroundWindowChanged = null;
            IgnorePredicate = null;
        }
    }
}
