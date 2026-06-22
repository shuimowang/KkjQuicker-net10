using System;
using System.Windows.Input;
using KkjQuicker.Utilities.Win32;
using WindowsInput;
using WindowsInput.Native;

namespace KkjQuicker.Utilities.Input
{
    /// <summary>
    /// 输入辅助工具。
    /// 聚焦于 WindowsInput 不提供的能力：键鼠状态查询、修饰键管理、组合键解析。
    /// 不重复 WindowsInput 的输入模拟 API（如需模拟按键，使用 <see cref="Simulator"/>）。
    /// 鼠标位置 / 命中窗口请见 <c>WindowHelper</c>。
    /// </summary>
    /// <remarks>
    /// 修饰键检测与释放的典型场景：
    /// 用户按 Ctrl+Shift+Q 触发动作，动作内部要模拟 Ctrl+V，
    /// 但用户的物理 Shift 还按着，实际发出的是 Ctrl+Shift+V → 翻车。
    /// 模拟前调用 <see cref="ReleaseAllModifierKeys"/> 或
    /// <see cref="ReleaseModifiersFromCombo"/> 清理残留可避免此类问题。
    ///
    /// 模拟输入复用 Quicker 环境的 <see cref="new InputSimulator()"/>，
    /// 该实例默认带 CustomExtraInfo 标记，可被 Quicker 自身的全局 Hook 识别，
    /// 避免模拟输入触发 Quicker 自身热键造成循环。
    /// </remarks>
    public static class InputHelper
    {
        /// <summary>
        /// 共享的 InputSimulator 实例（来自 Quicker 环境，已带 CustomExtraInfo 标记）。
        /// 调用方可通过此属性做更细粒度的模拟操作。
        /// </summary>
        public static IInputSimulator Simulator
        {
            get { return new InputSimulator(); }
        }

        // ============================================================
        // 虚拟键常量
        // ============================================================

        // Mouse VK
        public const int VK_LBUTTON = 0x01;
        public const int VK_RBUTTON = 0x02;
        public const int VK_MBUTTON = 0x04;
        public const int VK_XBUTTON1 = 0x05;
        public const int VK_XBUTTON2 = 0x06;

        // Modifier VK
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12; // Alt
        public const int VK_LWIN = 0x5B;
        public const int VK_RWIN = 0x5C;

        public const int VK_LSHIFT = 0xA0;
        public const int VK_RSHIFT = 0xA1;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4;
        public const int VK_RMENU = 0xA5;

        // ============================================================
        // 键状态查询
        // ============================================================

        /// <summary>
        /// 判断指定虚拟键当前是否按下（高位）。
        /// </summary>
        public static bool IsKeyDown(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        }

        /// <summary>
        /// 判断指定虚拟键自上次 GetAsyncKeyState 调用后是否发生过按下（低位）。
        /// 注意：低位状态在整个进程中是全局共享的——多个调用方查询同一个键时
        /// 会互相清掉对方的状态位。仅适合轻量辅助判断，不要用于严肃热键逻辑。
        /// </summary>
        public static bool WasKeyPressedSinceLastCall(int virtualKey)
        {
            return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x0001) != 0;
        }

        public static bool IsCtrlDown()
        {
            return IsKeyDown(VK_CONTROL) || IsKeyDown(VK_LCONTROL) || IsKeyDown(VK_RCONTROL);
        }

        public static bool IsAltDown()
        {
            return IsKeyDown(VK_MENU) || IsKeyDown(VK_LMENU) || IsKeyDown(VK_RMENU);
        }

        public static bool IsShiftDown()
        {
            return IsKeyDown(VK_SHIFT) || IsKeyDown(VK_LSHIFT) || IsKeyDown(VK_RSHIFT);
        }

        public static bool IsWinDown()
        {
            return IsKeyDown(VK_LWIN) || IsKeyDown(VK_RWIN);
        }

        public static bool IsAnyModifierDown()
        {
            return IsCtrlDown() || IsAltDown() || IsShiftDown() || IsWinDown();
        }

        /// <summary>
        /// 获取当前修饰键的 WPF ModifierKeys 表示。
        /// 在 Hook 回调、定时器、后台线程等非 WPF 输入路径里，
        /// <see cref="Keyboard.Modifiers"/> 永远返回 None，必须用本方法查询实际状态。
        /// </summary>
        public static ModifierKeys GetCurrentModifiers()
        {
            ModifierKeys modifiers = ModifierKeys.None;

            if (IsCtrlDown()) modifiers |= ModifierKeys.Control;
            if (IsAltDown()) modifiers |= ModifierKeys.Alt;
            if (IsShiftDown()) modifiers |= ModifierKeys.Shift;
            if (IsWinDown()) modifiers |= ModifierKeys.Windows;

            return modifiers;
        }

        // ============================================================
        // 鼠标按键状态
        // ============================================================

        public static bool IsMouseLeftDown() { return IsKeyDown(VK_LBUTTON); }
        public static bool IsMouseRightDown() { return IsKeyDown(VK_RBUTTON); }
        public static bool IsMouseMiddleDown() { return IsKeyDown(VK_MBUTTON); }
        public static bool IsMouseXButton1Down() { return IsKeyDown(VK_XBUTTON1); }
        public static bool IsMouseXButton2Down() { return IsKeyDown(VK_XBUTTON2); }

        // ============================================================
        // 修饰键释放
        // ============================================================

        /// <summary>
        /// 释放所有左右修饰键（Ctrl/Alt/Shift/Win）。
        /// 常用于模拟快捷键前清理物理修饰键残留，避免目标程序收到混合状态。
        /// </summary>
        public static void ReleaseAllModifierKeys()
        {
            ReleaseModifiers(true, true, true, true);
        }

        /// <summary>
        /// 按需释放 Ctrl / Alt / Shift / Win 的左右物理键。
        /// </summary>
        public static void ReleaseModifiers(bool ctrl, bool alt, bool shift, bool win)
        {
            if (ctrl)
            {
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.LCONTROL);
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.RCONTROL);
            }

            if (alt)
            {
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.LMENU);
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.RMENU);
            }

            if (shift)
            {
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.LSHIFT);
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.RSHIFT);
            }

            if (win)
            {
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.LWIN);
                new InputSimulator().Keyboard.KeyUp(VirtualKeyCode.RWIN);
            }
        }

        /// <summary>
        /// 根据组合键文本释放源组合键中出现的修饰键。
        /// 支持 Ctrl / Control / Alt / Shift / Win / Windows，忽略空格和大小写。
        /// 返回是否实际释放过修饰键。
        /// </summary>
        public static bool ReleaseModifiersFromCombo(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo))
                return false;

            bool ctrl = HasCtrlInCombo(combo);
            bool alt = HasAltInCombo(combo);
            bool shift = HasShiftInCombo(combo);
            bool win = HasWinInCombo(combo);

            if (!ctrl && !alt && !shift && !win)
                return false;

            ReleaseModifiers(ctrl, alt, shift, win);
            return true;
        }

        // ============================================================
        // 组合键文本解析
        // ============================================================

        /// <summary>
        /// 判断组合键文本是否包含任一修饰键。
        /// </summary>
        public static bool HasModifierInCombo(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo))
                return false;

            return HasCtrlInCombo(combo) ||
                   HasAltInCombo(combo) ||
                   HasShiftInCombo(combo) ||
                   HasWinInCombo(combo);
        }

        public static bool HasCtrlInCombo(string combo)
        {
            return ContainsModifier(combo, "Ctrl") || ContainsModifier(combo, "Control");
        }

        public static bool HasAltInCombo(string combo)
        {
            return ContainsModifier(combo, "Alt");
        }

        public static bool HasShiftInCombo(string combo)
        {
            return ContainsModifier(combo, "Shift");
        }

        public static bool HasWinInCombo(string combo)
        {
            return ContainsModifier(combo, "Win") || ContainsModifier(combo, "Windows");
        }

        /// <summary>
        /// 检测组合键中是否包含指定修饰键名。
        /// 通过按 + 切分并忽略空格/大小写比较，避免子串误判
        /// （例如 "Alt" 不会在 "Salt+X" 中被误识别）。
        /// </summary>
        private static bool ContainsModifier(string combo, string modifierName)
        {
            if (string.IsNullOrEmpty(combo) || string.IsNullOrEmpty(modifierName))
                return false;

            string[] parts = combo.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i].Trim(), modifierName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}