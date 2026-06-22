using KkjQuicker.Utilities.Hooks.Interop;
using KkjQuicker.Utilities.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KkjQuicker.Utilities.Hooks
{
    /// <summary>
    /// 全局低级键盘 Hook。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该类基于 <c>WH_KEYBOARD_LL</c>，可监听系统范围内的键盘输入。
    /// </para>
    /// <para>
    /// 只有在确实需要捕获全局键盘事件或拦截按键时才建议使用。
    /// 如果只是响应固定快捷键，优先考虑 <see cref="GlobalHotkey"/>。
    /// </para>
    /// </remarks>
    public sealed class GlobalKeyboardHook : LowLevelGlobalHookBase
    {
        /// <summary>
        /// 获取当前 Hook 类型。
        /// </summary>
        protected override int HookType
        {
            get { return NativeConstants.WH_KEYBOARD_LL; }
        }

        /// <summary>
        /// 当收到键盘消息时发生。
        /// </summary>
        /// <remarks>
        /// 事件处理程序可通过设置 <see cref="KeyboardHookEventArgs.Handled"/> 为 <c>true</c>
        /// 来拦截当前按键消息，使其不再传递给后续 Hook 或目标窗口。
        /// </remarks>
        public event EventHandler<KeyboardHookEventArgs> KeyboardEvent;

        /// <summary>
        /// 初始化一个全局低级键盘 Hook。
        /// </summary>
        public GlobalKeyboardHook()
        {
            HookProcInstance = Callback;
        }

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            // Fix #1：IsDisposed 防止 UnhookWindowsHookEx 后在途回调触发事件
            if (code == NativeConstants.HC_ACTION && !IsDisposed)
            {
                EventHandler<KeyboardHookEventArgs> handler = KeyboardEvent;
                if (handler != null)
                {
                    KBDLLHOOKSTRUCT data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    // Fix #2（主文件对应）：明确两步转换，消除 IntPtr 到枚举的隐式截断
                    KeyboardHookEventArgs args = new KeyboardHookEventArgs(
                        (KeyboardMessage)(int)wParam,
                        data.vkCode,
                        data.scanCode,
                        data.flags);
                    try
                    {
                        handler(this, args);
                    }
                    catch (Exception ex)
                    {
                        // Fix #3：改用 Debug.WriteLine，Release 配置下零开销
                        Debug.WriteLine("GlobalKeyboardHook.KeyboardEvent 事件处理程序抛出了异常：" + ex);
                    }

                    if (args.Handled)
                        return new IntPtr(1);
                }
            }

            return NativeMethods.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }
    }
}