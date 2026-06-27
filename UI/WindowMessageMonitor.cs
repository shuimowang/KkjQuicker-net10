using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;

using KkjQuicker.Utilities.Win32;

namespace KkjQuicker.UI
{
    /// <summary>
    /// WPF 窗口消息监听器。
    /// 用于监听显示器变化、DPI变化、系统参数变化、任务栏重建等窗口级消息。
    /// </summary>
    public sealed class WindowMessageMonitor : IDisposable
    {
        public const int WM_DISPLAYCHANGE = 0x007E;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int WM_DPICHANGED = 0x02E0;

        private readonly Window _window;
        private HwndSource? _source;
        private bool _isDisposed;
        private bool _isHooked;

        private static readonly int _taskbarCreatedMessage =
            unchecked((int)NativeMethods.RegisterWindowMessage("TaskbarCreated"));

        public WindowMessageMonitor(Window window)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (!window.Dispatcher.CheckAccess())
                throw new InvalidOperationException("WindowMessageMonitor 必须在目标窗口所属 UI 线程创建。");

            _window = window;

            _window.SourceInitialized += OnSourceInitialized;
            _window.Closed += OnWindowClosed;
            SystemParameters.StaticPropertyChanged += OnSystemParametersStaticPropertyChanged;

            Attach();
        }

        /// <summary>
        /// 获取当前是否已经安装窗口消息 Hook。
        /// </summary>
        public bool IsAttached
        {
            get { return _isHooked; }
        }

        /// <summary>
        /// 获取当前绑定的窗口句柄。窗口句柄尚未创建时返回 <see cref="IntPtr.Zero"/>。
        /// </summary>
        public IntPtr Handle { get; private set; }

        /// <summary>
        /// 显示器配置变化。
        /// 例如分辨率变化、显示器插拔、主屏切换等。
        /// </summary>
        public event EventHandler? DisplayChanged;

        /// <summary>
        /// 系统设置变化。
        /// 例如工作区、主题、任务栏位置等可能变化。
        /// </summary>
        public event EventHandler? SettingChanged;

        /// <summary>
        /// DPI 变化。
        /// 通常窗口跨 DPI 显示器或系统缩放变化时触发。
        /// </summary>
        public event EventHandler? DpiChanged;

        /// <summary>
        /// Explorer / 任务栏重建。
        /// 例如 explorer.exe 重启后，任务栏宿主窗口需要重新查找或重新嵌入。
        /// </summary>
        public event EventHandler? TaskbarCreated;

        /// <summary>
        /// WPF SystemParameters 变化。
        /// 可用于监听 WorkArea、PrimaryScreenWidth、PrimaryScreenHeight 等变化。
        /// </summary>
        public event EventHandler<PropertyChangedEventArgs>? SystemParametersChanged;

        /// <summary>
        /// 收到任意窗口消息时触发。
        /// </summary>
        public event EventHandler<WindowMessageEventArgs>? MessageReceived;

        public void Attach()
        {
            VerifyAccess();

            if (_isDisposed || _isHooked)
                return;

            Handle = _window.GetHandle();
            if (Handle == IntPtr.Zero)
                return;

            _source = HwndSource.FromHwnd(Handle);
            if (_source == null)
                return;

            _source.AddHook(WndProc);
            _isHooked = true;
        }

        public void Detach()
        {
            VerifyAccess();

            if (!_isHooked)
                return;

            if (_source != null)
                _source.RemoveHook(WndProc);

            _source = null;
            _isHooked = false;
            Handle = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            VerifyAccess();
            _isDisposed = true;

            Detach();

            _window.SourceInitialized -= OnSourceInitialized;
            _window.Closed -= OnWindowClosed;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersStaticPropertyChanged;

            DisplayChanged = null;
            SettingChanged = null;
            DpiChanged = null;
            TaskbarCreated = null;
            SystemParametersChanged = null;
            MessageReceived = null;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            Attach();
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            Dispose();
        }

        private void OnSystemParametersStaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var handler = SystemParametersChanged;
            if (handler != null)
                handler(this, e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            var messageReceived = MessageReceived;
            if (messageReceived != null)
            {
                messageReceived(this, new WindowMessageEventArgs(hwnd, msg, wParam, lParam));
            }

            if (msg == WM_DISPLAYCHANGE)
            {
                Raise(DisplayChanged);
            }
            else if (msg == WM_SETTINGCHANGE)
            {
                Raise(SettingChanged);
            }
            else if (msg == WM_DPICHANGED)
            {
                Raise(DpiChanged);
            }
            else if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
            {
                Raise(TaskbarCreated);
            }

            return IntPtr.Zero;
        }

        private void VerifyAccess()
        {
            if (!_window.Dispatcher.CheckAccess())
                throw new InvalidOperationException("WindowMessageMonitor 只能在目标窗口所属 UI 线程访问。");
        }

        private void Raise(EventHandler? handler)
        {
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }

    public sealed class WindowMessageEventArgs : EventArgs
    {
        public WindowMessageEventArgs(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam)
        {
            Hwnd = hwnd;
            Message = message;
            WParam = wParam;
            LParam = lParam;
        }

        public IntPtr Hwnd { get; }

        public int Message { get; }

        public IntPtr WParam { get; }

        public IntPtr LParam { get; }
    }
}
