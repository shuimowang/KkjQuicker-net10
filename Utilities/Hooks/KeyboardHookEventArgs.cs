using System;
using System.Windows.Input;

namespace KkjQuicker.Utilities.Hooks
{
    /// <summary>
    /// 全局键盘 Hook 事件参数。
    /// </summary>
    public sealed class KeyboardHookEventArgs : EventArgs
    {
        // KBDLLHOOKSTRUCT flags 标志位，对应 MSDN KBDLLHOOKSTRUCT.flags
        private const uint LLKHF_EXTENDED = 0x0001;
        private const uint LLKHF_LOWER_IL_INJECTED = 0x0002;
        private const uint LLKHF_INJECTED = 0x0010;
        private const uint LLKHF_ALTDOWN = 0x0020;

        /// <summary>
        /// 初始化一个键盘 Hook 事件参数实例。
        /// </summary>
        /// <param name="message">键盘消息类型。</param>
        /// <param name="vk">虚拟键码。</param>
        /// <param name="scan">扫描码。</param>
        /// <param name="flags">低级键盘 Hook 标志位。</param>
        internal KeyboardHookEventArgs(
            KeyboardMessage message,
            uint vk,
            uint scan,
            uint flags)
        {
            Message = message;
            VirtualKeyCode = vk;
            ScanCode = scan;
            Flags = flags;
        }

        /// <summary>
        /// 获取键盘消息类型。
        /// </summary>
        public KeyboardMessage Message { get; }

        /// <summary>
        /// 获取 Win32 虚拟键码。
        /// </summary>
        public uint VirtualKeyCode { get; }

        /// <summary>
        /// 获取硬件扫描码。
        /// </summary>
        public uint ScanCode { get; }

        /// <summary>
        /// 获取原始标志位。
        /// </summary>
        public uint Flags { get; }

        /// <summary>
        /// 获取 WPF 友好的 <see cref="Key"/> 值。
        /// </summary>
        public Key Key
        {
            get => KeyInterop.KeyFromVirtualKey((int)VirtualKeyCode);
        }

        /// <summary>
        /// 获取当前按键是否为扩展键（如方向键、Insert、Delete 等）。
        /// </summary>
        public bool IsExtended
        {
            get => (Flags & LLKHF_EXTENDED) != 0;
        }

        /// <summary>
        /// 获取当前按键是否由注入输入产生（包含低完整性级别注入）。
        /// </summary>
        /// <remarks>
        /// <see cref="IsInjected"/> 对应 <c>LLKHF_INJECTED</c>（普通注入）；
        /// <see cref="IsLowerIlInjected"/> 对应 <c>LLKHF_LOWER_IL_INJECTED</c>（低完整性级别进程注入）。
        /// 两者并不互斥，低完整性注入时两个标志均会置位。
        /// </remarks>
        public bool IsInjected
        {
            get => (Flags & LLKHF_INJECTED) != 0;
        }

        /// <summary>
        /// 获取当前按键是否由低完整性级别进程注入产生。
        /// </summary>
        public bool IsLowerIlInjected
        {
            get => (Flags & LLKHF_LOWER_IL_INJECTED) != 0;
        }

        /// <summary>
        /// 获取事件触发时 Alt 键是否处于按下状态。
        /// </summary>
        /// <remarks>
        /// 可用于识别 Alt+键 组合，与 <see cref="KeyboardMessage.SysKeyDown"/> /
        /// <see cref="KeyboardMessage.SysKeyUp"/> 配合使用。
        /// </remarks>
        public bool IsAltDown
        {
            get => (Flags & LLKHF_ALTDOWN) != 0;
        }

        /// <summary>
        /// 获取或设置是否拦截当前键盘消息。
        /// </summary>
        /// <remarks>
        /// 当设置为 <c>true</c> 时，当前消息不会继续传递给后续 Hook 或目标窗口。
        /// </remarks>
        public bool Handled { get; set; }
    }

    /// <summary>
    /// 键盘消息类型。
    /// </summary>
    public enum KeyboardMessage
    {
        /// <summary>键按下。</summary>
        KeyDown = 0x0100,
        /// <summary>键抬起。</summary>
        KeyUp = 0x0101,
        /// <summary>系统键按下，例如 Alt 组合键。</summary>
        SysKeyDown = 0x0104,
        /// <summary>系统键抬起，例如 Alt 组合键。</summary>
        SysKeyUp = 0x0105
    }
}
