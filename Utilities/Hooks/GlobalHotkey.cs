using KkjQuicker.Utilities.Hooks.Interop;
using KkjQuicker.Utilities.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Input;
using System.Windows.Interop;

namespace KkjQuicker.Utilities.Hooks
{
    /// <summary>
    /// 基于 Win32 <c>RegisterHotKey</c> 的全局热键。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与低级 Hook 相比，全局热键的系统开销更小、行为更明确。
    /// 如果只是监听固定组合键，优先考虑使用本类，而不是 <see cref="GlobalKeyboardHook"/>。
    /// </para>
    /// <para>
    /// 本类内部创建一个隐藏消息窗口，用于接收 <c>WM_HOTKEY</c> 消息。
    /// </para>
    /// </remarks>
    public sealed class GlobalHotkey : IDisposable
    {
        private static int _nextId;

        private readonly object _syncRoot = new();
        private readonly int _id;
        private readonly HwndSource _source;
        private readonly Action _callback;
        private volatile bool _disposed;

        /// <summary>
        /// 初始化一个全局热键实例，并立即向系统注册。
        /// </summary>
        /// <param name="modifiers">热键修饰键组合。</param>
        /// <param name="key">主键。</param>
        /// <param name="callback">热键触发时执行的回调。</param>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> 为 <c>null</c>。</exception>
        /// <exception cref="ArgumentException"><paramref name="key"/> 无效。</exception>
        /// <exception cref="Win32Exception">注册全局热键失败。</exception>
        public GlobalHotkey(HotkeyModifiers modifiers, Key key, Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            if (key == Key.None)
                throw new ArgumentException("热键主键不能为 Key.None。", nameof(key));

            _callback = callback;
            _id = Interlocked.Increment(ref _nextId);

            // Fix #4：对象初始化器
            _source = new HwndSource(new HwndSourceParameters("KkjQuicker.GlobalHotkey")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0
            });
            _source.AddHook(WndProc);

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            if (!NativeMethods.RegisterHotKey(_source.Handle, _id, (uint)modifiers, vk))
            {
                _source.RemoveHook(WndProc);
                _source.Dispose();
                ThrowLastError("注册全局热键失败。热键可能已被占用，或参数无效。");
            }
        }

        /// <summary>
        /// 释放全局热键注册及其内部消息窗口。
        /// </summary>
        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (!NativeMethods.UnregisterHotKey(_source.Handle, _id))
                {
                    int error = Marshal.GetLastWin32Error();
                    Debug.WriteLine("GlobalHotkey.UnregisterHotKey failed: " + error);
                }

                _source.RemoveHook(WndProc);
                _source.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_disposed && msg == NativeConstants.WM_HOTKEY && wParam.ToInt32() == _id)
            {
                handled = true;

                try
                {
                    _callback();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("GlobalHotkey callback failed: " + ex);
                }
            }

            return IntPtr.Zero;
        }

        private static void ThrowLastError(string message)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), message);
        }
    }

    /// <summary>
    /// 全局热键修饰键。
    /// </summary>
    [Flags]
    public enum HotkeyModifiers : uint
    {
        /// <summary>Alt 键。</summary>
        Alt = 0x0001,
        /// <summary>Ctrl 键。</summary>
        Ctrl = 0x0002,
        /// <summary>Shift 键。</summary>
        Shift = 0x0004,
        /// <summary>Windows 键。</summary>
        Win = 0x0008,
        // Fix #3：补充 NoRepeat，抑制长按连发（Windows 7+）
        /// <summary>
        /// 抑制按住不放时的重复触发（Windows 7 及以上）。
        /// </summary>
        NoRepeat = 0x4000
    }
}
