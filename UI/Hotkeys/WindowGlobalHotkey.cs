using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using KkjQuicker.Utilities.Win32;

namespace KkjQuicker.UI.Hotkeys
{
    /// <summary>
    /// 给 WPF Window 安装一个全局快捷键。
    ///
    /// 职责：
    /// 1. 跟随 Window 句柄生命周期注册快捷键。
    /// 2. 收到 WM_HOTKEY 后触发 Triggered 事件。
    /// 3. Window 真正 Closed 时自动卸载。
    /// 4. Hide 不会卸载，因此隐藏窗口仍可继续响应快捷键。
    ///
    /// 注意：
    /// 本类只负责“注册快捷键并触发事件”，不包含显示窗口、隐藏窗口、执行动作等业务逻辑。
    /// </summary>
    public sealed class WindowGlobalHotkey : IDisposable
    {
        const int WM_HOTKEY = 0x0312;

        const uint MOD_ALT = 0x0001;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_SHIFT = 0x0004;
        const uint MOD_WIN = 0x0008;
        const uint MOD_NOREPEAT = 0x4000;

        static int _nextId;

        readonly Window _window;
        readonly int _id;
        readonly uint _modifiers;
        readonly uint _virtualKey;

        HwndSource? _source;
        IntPtr _hwnd;
        bool _registered;
        bool _disposed;

        /// <summary>
        /// 快捷键触发时触发。
        /// </summary>
        public event Action? Triggered;

        /// <summary>
        /// 快捷键注册失败时触发。
        /// 参数为 Win32 错误码。
        /// 常见错误：1409 表示快捷键已被其它程序注册。
        /// </summary>
        public event Action<int>? RegistrationFailed;

        public bool IsRegistered
        {
            get { return _registered; }
        }

        public int Id
        {
            get { return _id; }
        }

        /// <summary>
        /// 安装一个全局快捷键。
        /// 调用处可以不保存返回值；Window 会通过事件订阅持有本实例。
        /// 如果需要提前卸载或动态更换快捷键，可以保存返回值并调用 Dispose。
        /// </summary>
        public static WindowGlobalHotkey Install(
            Window window,
            Key key,
            ModifierKeys modifiers,
            Action onTriggered,
            Action<int>? onRegistrationFailed)
        {
            if (onTriggered == null)
                throw new ArgumentNullException(nameof(onTriggered));

            var hotkey = new WindowGlobalHotkey(window, key, modifiers);

            hotkey.Triggered += onTriggered;

            if (onRegistrationFailed != null)
                hotkey.RegistrationFailed += onRegistrationFailed;

            hotkey.Start();

            return hotkey;
        }

        public static WindowGlobalHotkey Install(
            Window window,
            Key key,
            ModifierKeys modifiers,
            Action onTriggered)
        {
            return Install(window, key, modifiers, onTriggered, null);
        }

        WindowGlobalHotkey(Window window, Key key, ModifierKeys modifiers)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            if (!window.Dispatcher.CheckAccess())
                throw new InvalidOperationException("WindowGlobalHotkey 必须在窗口所属 UI 线程创建。");

            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
                throw new ArgumentException("无效的快捷键。", nameof(key));

            _window = window;
            _id = Interlocked.Increment(ref _nextId);
            _virtualKey = unchecked((uint)virtualKey);
            _modifiers = ToNativeModifiers(modifiers) | MOD_NOREPEAT;

            _window.SourceInitialized += Window_SourceInitialized;
            _window.Closed += Window_Closed;
        }

        void Start()
        {
            if (_disposed || _registered)
                return;

            // 如果句柄已经存在，立即注册。
            // 这条路径下若没有 RegistrationFailed 订阅者，则允许抛异常，方便调用方 try-catch。
            if (new WindowInteropHelper(_window).Handle != IntPtr.Zero)
                Register(true);
        }

        void Window_SourceInitialized(object? sender, EventArgs e)
        {
            // 延迟注册路径。
            // 这里不能无条件抛异常，否则会绕过创建窗口时的 try-catch。
            Register(false);
        }

        void Window_Closed(object? sender, EventArgs e)
        {
            Dispose();
        }

        void Register(bool allowThrow)
        {
            if (_registered || _disposed)
                return;

            _hwnd = new WindowInteropHelper(_window).Handle;
            if (_hwnd == IntPtr.Zero)
                return;

            _source = HwndSource.FromHwnd(_hwnd);
            if (_source != null)
                _source.AddHook(WndProc);

            if (!NativeMethods.RegisterHotKey(_hwnd, _id, _modifiers, _virtualKey))
            {
                int error = Marshal.GetLastWin32Error();
                Action<int>? failed = RegistrationFailed;

                Dispose();

                if (failed != null)
                {
                    failed(error);
                    return;
                }

                if (allowThrow)
                {
                    throw new InvalidOperationException(
                        "注册全局快捷键失败，可能快捷键已被其它程序占用。Win32Error=" + error);
                }

                return;
            }

            _registered = true;
        }

        IntPtr WndProc(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt64() == _id)
            {
                handled = true;

                Action? triggered = Triggered;
                if (triggered != null)
                    triggered();
            }

            return IntPtr.Zero;
        }

        static uint ToNativeModifiers(ModifierKeys modifiers)
        {
            uint result = 0;

            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                result |= MOD_ALT;

            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                result |= MOD_CONTROL;

            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                result |= MOD_SHIFT;

            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                result |= MOD_WIN;

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _window.SourceInitialized -= Window_SourceInitialized;
            _window.Closed -= Window_Closed;

            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }

            if (_registered && _hwnd != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(_hwnd, _id);
                _registered = false;
            }

            _hwnd = IntPtr.Zero;

            Triggered = null;
            RegistrationFailed = null;
        }
    }
}