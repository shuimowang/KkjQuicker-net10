using KkjQuicker.Utilities.Hooks.Interop;
using KkjQuicker.Utilities.Win32;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KkjQuicker.Utilities.Hooks
{
    /// <summary>
    /// 低级全局 Win32 Hook 基类。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本基类面向低级全局 Hook 场景，例如 <c>WH_KEYBOARD_LL</c>、<c>WH_MOUSE_LL</c>。
    /// </para>
    /// <para>
    /// 当前实现固定使用：
    /// <list type="bullet">
    /// <item><description><c>dwThreadId = 0</c>，安装到全局输入链</description></item>
    /// <item><description><c>hMod = IntPtr.Zero</c></description></item>
    /// </list>
    /// 因此它并不是适用于所有 Win32 Hook 类型的通用基类。
    /// </para>
    /// <para>
    /// 派生类只需要：
    /// <list type="number">
    /// <item><description>通过 <see cref="HookType"/> 指定 Hook 类型</description></item>
    /// <item><description>在构造函数中为 <see cref="HookProcInstance"/> 赋值，并持有该委托引用，防止被 GC 回收</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public abstract class LowLevelGlobalHookBase : IDisposable
    {
        private readonly object _syncRoot = new object();
        private SafeHookHandle _handle;

        // Fix #2：标记 volatile，确保 Callback 线程与 Dispose 线程之间的可见性
        private volatile bool _disposed;

        /// <summary>
        /// 派生类必须持有的 Hook 回调委托实例。
        /// </summary>
        /// <remarks>
        /// 必须保存为实例字段，避免委托被垃圾回收后导致原生回调失效。
        /// 通常在派生类构造函数中赋值，例如：
        /// <code>
        /// HookProcInstance = Callback;
        /// </code>
        /// </remarks>
        protected NativeMethods.HookProc HookProcInstance;

        /// <summary>
        /// 获取当前 Hook 类型。
        /// </summary>
        protected abstract int HookType { get; }

        /// <summary>
        /// 获取当前对象是否已释放。
        /// </summary>
        protected bool IsDisposed => _disposed;

        /// <summary>
        /// 获取当前 Hook 是否已成功安装。
        /// </summary>
        public bool IsInstalled
        {
            get
            {
                // Fix #3：IsInstalled 加锁，避免与 Install/Uninstall 并发时的非原子读取
                lock (_syncRoot)
                    return _handle != null && !_handle.IsInvalid && !_handle.IsClosed;
            }
        }

        /// <summary>
        /// 安装 Hook。
        /// </summary>
        /// <remarks>
        /// 重复调用是安全的；若当前已安装，则直接返回。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">对象已释放。</exception>
        /// <exception cref="InvalidOperationException">派生类未正确初始化 Hook 回调委托。</exception>
        /// <exception cref="Win32Exception">Win32 安装 Hook 失败。</exception>
        public void Install()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();

                if (IsInstalled)
                    return;

                if (HookProcInstance == null)
                    throw new InvalidOperationException(
                        "HookProcInstance 尚未初始化。请在派生类构造函数中为其赋值。");

                IntPtr handle = NativeMethods.SetWindowsHookEx(
                    HookType,
                    HookProcInstance,
                    IntPtr.Zero,
                    0);

                if (handle == IntPtr.Zero)
                    ThrowLastError("安装 Hook 失败。");

                _handle = new SafeHookHandle(handle);
            }
        }

        /// <summary>
        /// 卸载 Hook。
        /// </summary>
        /// <remarks>
        /// 重复调用是安全的；若当前未安装，则直接返回。
        /// </remarks>
        public void Uninstall()
        {
            lock (_syncRoot)
            {
                if (_handle != null)
                {
                    _handle.Dispose();
                    _handle = null;
                }
            }
        }

        /// <summary>
        /// 释放当前 Hook 对象。
        /// </summary>
        public void Dispose()
        {
            // Fix #4：整体移入锁，彻底消除并发 Dispose 的双重入口问题
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                Dispose(true);
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源。派生类可重写此方法以释放自身持有的资源。
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true"/> 表示由 <see cref="Dispose()"/> 主动调用；
        /// <see langword="false"/> 表示由终结器调用（当前基类无终结器，保留此参数以遵循标准模式）。
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
                Uninstall();
        }

        /// <summary>
        /// 当对象已释放时抛出异常。
        /// </summary>
        /// <exception cref="ObjectDisposedException">对象已释放。</exception>
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        private static void ThrowLastError(string message)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), message);
        }
    }
}