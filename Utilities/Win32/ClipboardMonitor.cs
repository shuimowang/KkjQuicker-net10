using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KkjQuicker.Utilities.Win32
{
    /// <summary>
    /// 基于 AddClipboardFormatListener 的剪贴板变化监听器。
    /// </summary>
    /// <remarks>
    /// 本类非线程安全，所有公开成员需在同一线程调用。
    /// 使用完毕需调用 <see cref="Dispose"/> 以释放底层窗口句柄和监听注册。
    /// </remarks>
    public sealed class ClipboardMonitor : NativeWindow, IDisposable
    {
        private const int WM_CLOSE = 0x0010;
        private const int WM_DESTROY = 0x0002;
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        // HWND_MESSAGE：将窗口创建为 message-only window，不参与 EnumWindows，不接收顶层广播消息。
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        private bool _isStarted;
        private bool _disposed;

        /// <summary>
        /// 剪贴板内容发生变化时触发。事件在首次调用 <see cref="Start"/> 并创建底层窗口的线程上回调。
        /// </summary>
        public event EventHandler? ClipboardChanged;

        /// <summary>
        /// 当前是否已经注册剪贴板监听。
        /// </summary>
        public bool IsStarted
        {
            get { return _isStarted; }
        }

        /// <summary>
        /// 创建剪贴板监听器。底层 message-only window 将在首次调用 <see cref="Start"/> 时创建。
        /// </summary>
        public ClipboardMonitor()
        {
        }

        /// <summary>
        /// 开始监听剪贴板变化。重复调用无副作用。
        /// </summary>
        /// <exception cref="ObjectDisposedException">实例已被释放。</exception>
        /// <exception cref="Win32Exception">向系统注册剪贴板监听失败。</exception>
        public void Start()
        {
            ThrowIfDisposed();

            if (_isStarted)
            {
                return;
            }

            EnsureHandle();

            if (!NativeMethods.AddClipboardFormatListener(Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "注册剪贴板监听失败。");
            }

            _isStarted = true;
        }

        /// <summary>
        /// 停止监听剪贴板变化。重复调用或未启动时调用均无副作用。
        /// </summary>
        public void Stop()
        {
            if (_disposed || !_isStarted)
            {
                return;
            }

            if (Handle != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
            }

            _isStarted = false;
        }

        /// <summary>
        /// 释放剪贴板监听器占用的窗口句柄和监听注册。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            // 先置位，避免 DestroyHandle 触发的 WM_DESTROY 在 WndProc 中再次走清理分支。
            _disposed = true;

            if (_isStarted && Handle != IntPtr.Zero)
            {
                NativeMethods.RemoveClipboardFormatListener(Handle);
                _isStarted = false;
            }

            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }

            GC.SuppressFinalize(this);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                OnClipboardChanged();
            }
            else if (m.Msg == WM_CLOSE || m.Msg == WM_DESTROY)
            {
                // 句柄被外部销毁前完成监听注销，避免泄漏系统级监听者列表中的失效句柄项。
                if (_isStarted)
                {
                    if (Handle != IntPtr.Zero)
                    {
                        NativeMethods.RemoveClipboardFormatListener(Handle);
                    }

                    _isStarted = false;
                }
            }

            base.WndProc(ref m);
        }

        private void EnsureHandle()
        {
            if (Handle != IntPtr.Zero)
            {
                return;
            }

            CreateParams cp = new CreateParams();
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        private void OnClipboardChanged()
        {
            EventHandler? handler = ClipboardChanged;

            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}