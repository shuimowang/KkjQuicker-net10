using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using WindowsInput.Native;

namespace KkjQuicker.UI.Hotkeys
{
    /// <summary>
    /// 快捷键值对象。
    ///
    /// 职责：
    /// 1. 表示一个快捷键：主键 + 修饰键。
    /// 2. 提供配置序列化 / 反序列化。
    /// 3. 提供显示文本。
    /// 4. 提供基础校验和比较能力。
    ///
    /// 不负责：
    /// 1. 注册全局快捷键。
    /// 2. 捕获用户输入。
    /// 3. 模拟按键。
    /// 4. 检测业务层快捷键冲突。
    /// </summary>
    public sealed class HotkeyGesture : IEquatable<HotkeyGesture>
    {
        static readonly VirtualKeyCode EmptyKey = (VirtualKeyCode)0;

        public static readonly HotkeyGesture None =
            new HotkeyGesture(EmptyKey, ModifierKeys.None, false);

        public VirtualKeyCode Key { get; private set; }

        public ModifierKeys Modifiers { get; private set; }

        public bool IsEmpty
        {
            get { return Key == EmptyKey; }
        }

        public bool HasModifier
        {
            get { return Modifiers != ModifierKeys.None; }
        }

        public uint VirtualKey
        {
            get { return unchecked((uint)Key); }
        }

        public string DisplayText
        {
            get { return ToString(); }
        }

        public Key WpfKey
        {
            get
            {
                if (IsEmpty)
                    return KeyInterop.KeyFromVirtualKey(0);

                return KeyInterop.KeyFromVirtualKey((int)Key);
            }
        }

        public HotkeyGesture(VirtualKeyCode key, ModifierKeys modifiers)
            : this(key, modifiers, true)
        {
        }

        public HotkeyGesture(Key key, ModifierKeys modifiers)
            : this(ToVirtualKeyCode(key), modifiers, true)
        {
        }

        HotkeyGesture(VirtualKeyCode key, ModifierKeys modifiers, bool validate)
        {
            if (key == EmptyKey)
            {
                Key = EmptyKey;
                Modifiers = ModifierKeys.None;
                return;
            }

            if (validate && !Enum.IsDefined(typeof(VirtualKeyCode), key))
                throw new ArgumentOutOfRangeException("key", "未知的虚拟键。");

            Key = key;
            Modifiers = NormalizeModifiers(modifiers);
        }

        /// <summary>
        /// 保存为配置字符串。
        /// 格式：修饰键数值|虚拟键数值
        /// 示例：3|86
        /// </summary>
        public string ToData()
        {
            if (IsEmpty)
                return string.Empty;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}",
                (uint)Modifiers,
                (int)Key);
        }

        public static bool TryParse(string data, out HotkeyGesture hotkey)
        {
            hotkey = None;

            if (string.IsNullOrWhiteSpace(data))
                return false;

            string[] parts = data.Split('|');
            if (parts.Length != 2)
                return false;

            uint modifierValue;
            int keyValue;

            if (!uint.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out modifierValue))
                return false;

            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out keyValue))
                return false;

            VirtualKeyCode key = unchecked((VirtualKeyCode)keyValue);

            if (key == EmptyKey)
            {
                hotkey = None;
                return true;
            }

            if (!Enum.IsDefined(typeof(VirtualKeyCode), key))
                return false;

            hotkey = new HotkeyGesture(
                key,
                NormalizeModifiers((ModifierKeys)modifierValue),
                false);

            return true;
        }

        public static HotkeyGesture ParseOrDefault(string data, HotkeyGesture defaultValue)
        {
            HotkeyGesture result;
            if (TryParse(data, out result))
                return result;

            return defaultValue ?? None;
        }

        public static HotkeyGesture ParseOrNone(string data)
        {
            HotkeyGesture result;
            if (TryParse(data, out result))
                return result;

            return None;
        }

        public static string DataToString(string data)
        {
            HotkeyGesture hotkey;
            if (!TryParse(data, out hotkey))
                return string.Empty;

            return hotkey.ToString();
        }

        public static HotkeyGesture FromVirtualKey(uint virtualKey, ModifierKeys modifiers)
        {
            return new HotkeyGesture(
                unchecked((VirtualKeyCode)virtualKey),
                modifiers);
        }

        public static HotkeyGesture FromWpfKey(Key key, ModifierKeys modifiers)
        {
            return new HotkeyGesture(key, modifiers);
        }

        /// <summary>
        /// 是否适合作为 RegisterHotKey 的全局快捷键。
        /// 默认不允许单键全局快捷键，避免误触和抢占系统按键。
        /// </summary>
        public bool CanRegisterGlobalHotkey()
        {
            return CanRegisterGlobalHotkey(false);
        }

        /// <summary>
        /// 是否适合作为 RegisterHotKey 的全局快捷键。
        /// </summary>
        public bool CanRegisterGlobalHotkey(bool allowSingleKey)
        {
            if (IsEmpty)
                return false;

            if (IsModifierKey(Key))
                return false;

            if (!allowSingleKey && Modifiers == ModifierKeys.None)
                return false;

            return true;
        }

        /// <summary>
        /// 获取 WindowsInput 可用的修饰键虚拟键。
        /// 这里只做转换，不执行模拟输入。
        /// </summary>
        public IEnumerable<VirtualKeyCode> GetModifierKeyCodes()
        {
            if ((Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                yield return VirtualKeyCode.CONTROL;

            if ((Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                yield return VirtualKeyCode.SHIFT;

            if ((Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                yield return VirtualKeyCode.MENU;

            if ((Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                yield return VirtualKeyCode.LWIN;
        }

        public override string ToString()
        {
            if (IsEmpty)
                return string.Empty;

            StringBuilder builder = new StringBuilder();

            if ((Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                builder.Append("Ctrl + ");

            if ((Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                builder.Append("Shift + ");

            if ((Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                builder.Append("Alt + ");

            if ((Modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                builder.Append("Win + ");

            builder.Append(GetKeyDisplayName(Key));

            return builder.ToString();
        }

        public bool Equals(HotkeyGesture other)
        {
            if (ReferenceEquals(other, null))
                return false;

            return Key == other.Key && Modifiers == other.Modifiers;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as HotkeyGesture);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Key * 397) ^ (int)Modifiers;
            }
        }

        public static bool operator ==(HotkeyGesture left, HotkeyGesture right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (ReferenceEquals(left, null))
                return false;

            if (ReferenceEquals(right, null))
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(HotkeyGesture left, HotkeyGesture right)
        {
            return !(left == right);
        }

        public static ModifierKeys NormalizeModifiers(ModifierKeys modifiers)
        {
            ModifierKeys result = ModifierKeys.None;

            if ((modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                result |= ModifierKeys.Control;

            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                result |= ModifierKeys.Shift;

            if ((modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
                result |= ModifierKeys.Alt;

            if ((modifiers & ModifierKeys.Windows) == ModifierKeys.Windows)
                result |= ModifierKeys.Windows;

            return result;
        }

        public static bool IsModifierKey(VirtualKeyCode key)
        {
            return key == VirtualKeyCode.CONTROL ||
                   key == VirtualKeyCode.LCONTROL ||
                   key == VirtualKeyCode.RCONTROL ||
                   key == VirtualKeyCode.SHIFT ||
                   key == VirtualKeyCode.LSHIFT ||
                   key == VirtualKeyCode.RSHIFT ||
                   key == VirtualKeyCode.MENU ||
                   key == VirtualKeyCode.LMENU ||
                   key == VirtualKeyCode.RMENU ||
                   key == VirtualKeyCode.LWIN ||
                   key == VirtualKeyCode.RWIN;
        }

        /// <summary>
        /// 类似 Quicker 控件里的 FilterControlKeysInSingleMode。
        /// 这些键在“单键模式”下通常也不建议作为普通快捷键主键。
        /// </summary>
        public static bool IsControlOnlyKey(VirtualKeyCode key)
        {
            return IsModifierKey(key) ||
                   key == VirtualKeyCode.APPS ||
                   key == VirtualKeyCode.CLEAR ||
                   key == VirtualKeyCode.OEM_CLEAR;
        }

        static VirtualKeyCode ToVirtualKeyCode(Key key)
        {
            if (key == System.Windows.Input.Key.None)
                return EmptyKey;

            int value = KeyInterop.VirtualKeyFromKey(key);
            return unchecked((VirtualKeyCode)value);
        }

        static string GetKeyDisplayName(VirtualKeyCode key)
        {
            if (key >= VirtualKeyCode.VK_A && key <= VirtualKeyCode.VK_Z)
                return ((char)('A' + ((int)key - (int)VirtualKeyCode.VK_A))).ToString();

            if (key >= VirtualKeyCode.VK_0 && key <= VirtualKeyCode.VK_9)
                return ((char)('0' + ((int)key - (int)VirtualKeyCode.VK_0))).ToString();

            if (key >= VirtualKeyCode.NUMPAD0 && key <= VirtualKeyCode.NUMPAD9)
            {
                int number = (int)key - (int)VirtualKeyCode.NUMPAD0;
                return "Num " + number.ToString(CultureInfo.InvariantCulture);
            }

            if (key >= VirtualKeyCode.F1 && key <= VirtualKeyCode.F24)
            {
                int number = (int)key - (int)VirtualKeyCode.F1 + 1;
                return "F" + number.ToString(CultureInfo.InvariantCulture);
            }

            switch (key)
            {
                case VirtualKeyCode.SPACE:
                    return "Space";

                case VirtualKeyCode.RETURN:
                    return "Enter";

                case VirtualKeyCode.ESCAPE:
                    return "Esc";

                case VirtualKeyCode.BACK:
                    return "Backspace";

                case VirtualKeyCode.DELETE:
                    return "Delete";

                case VirtualKeyCode.INSERT:
                    return "Insert";

                case VirtualKeyCode.TAB:
                    return "Tab";

                case VirtualKeyCode.HOME:
                    return "Home";

                case VirtualKeyCode.END:
                    return "End";

                case VirtualKeyCode.PRIOR:
                    return "PageUp";

                case VirtualKeyCode.NEXT:
                    return "PageDown";

                case VirtualKeyCode.LEFT:
                    return "Left";

                case VirtualKeyCode.RIGHT:
                    return "Right";

                case VirtualKeyCode.UP:
                    return "Up";

                case VirtualKeyCode.DOWN:
                    return "Down";

                case VirtualKeyCode.SNAPSHOT:
                    return "PrintScreen";

                case VirtualKeyCode.PAUSE:
                    return "Pause";

                case VirtualKeyCode.CAPITAL:
                    return "CapsLock";

                case VirtualKeyCode.NUMLOCK:
                    return "NumLock";

                case VirtualKeyCode.SCROLL:
                    return "ScrollLock";

                case VirtualKeyCode.ADD:
                    return "Num +";

                case VirtualKeyCode.SUBTRACT:
                    return "Num -";

                case VirtualKeyCode.MULTIPLY:
                    return "Num *";

                case VirtualKeyCode.DIVIDE:
                    return "Num /";

                case VirtualKeyCode.DECIMAL:
                    return "Num .";

                case VirtualKeyCode.OEM_PLUS:
                    return "+";

                case VirtualKeyCode.OEM_MINUS:
                    return "-";

                case VirtualKeyCode.OEM_COMMA:
                    return ",";

                case VirtualKeyCode.OEM_PERIOD:
                    return ".";

                case VirtualKeyCode.OEM_1:
                    return ";";

                case VirtualKeyCode.OEM_2:
                    return "/";

                case VirtualKeyCode.OEM_3:
                    return "`";

                case VirtualKeyCode.OEM_4:
                    return "[";

                case VirtualKeyCode.OEM_5:
                    return "\\";

                case VirtualKeyCode.OEM_6:
                    return "]";

                case VirtualKeyCode.OEM_7:
                    return "'";

                case VirtualKeyCode.APPS:
                    return "Apps";

                case VirtualKeyCode.LWIN:
                case VirtualKeyCode.RWIN:
                    return "Win";

                case VirtualKeyCode.CONTROL:
                case VirtualKeyCode.LCONTROL:
                case VirtualKeyCode.RCONTROL:
                    return "Ctrl";

                case VirtualKeyCode.SHIFT:
                case VirtualKeyCode.LSHIFT:
                case VirtualKeyCode.RSHIFT:
                    return "Shift";

                case VirtualKeyCode.MENU:
                case VirtualKeyCode.LMENU:
                case VirtualKeyCode.RMENU:
                    return "Alt";

                default:
                    return key.ToString();
            }
        }
    }
}