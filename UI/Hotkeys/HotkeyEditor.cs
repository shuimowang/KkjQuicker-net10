using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WindowsInput.Native;

namespace KkjQuicker.UI.Hotkeys
{
    /// <summary>
    /// 快捷键编辑输入框。
    ///
    /// 职责：
    /// 1. 捕获用户在输入框中按下的快捷键。
    /// 2. 生成 HotkeyGesture。
    /// 3. 支持双向绑定。
    /// 4. 支持普通组合键模式和单键模式。
    ///
    /// 不负责：
    /// 1. 注册全局快捷键。
    /// 2. 检测业务层快捷键冲突。
    /// 3. 显示复杂按钮/弹窗。
    /// </summary>
    public class HotkeyEditor : TextBox
    {
        public static readonly DependencyProperty HotkeyProperty =
            DependencyProperty.Register(
                "Hotkey",
                typeof(HotkeyGesture),
                typeof(HotkeyEditor),
                new FrameworkPropertyMetadata(
                    HotkeyGesture.None,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnHotkeyChanged));

        public static readonly DependencyProperty SingleKeyModeProperty =
            DependencyProperty.Register(
                "SingleKeyMode",
                typeof(bool),
                typeof(HotkeyEditor),
                new PropertyMetadata(false));

        public static readonly DependencyProperty FilterControlKeysInSingleModeProperty =
            DependencyProperty.Register(
                "FilterControlKeysInSingleMode",
                typeof(bool),
                typeof(HotkeyEditor),
                new PropertyMetadata(true));

        public static readonly DependencyProperty RequireModifierKeysProperty =
            DependencyProperty.Register(
                "RequireModifierKeys",
                typeof(bool),
                typeof(HotkeyEditor),
                new PropertyMetadata(true));

        public static readonly DependencyProperty EmptyTextProperty =
            DependencyProperty.Register(
                "EmptyText",
                typeof(string),
                typeof(HotkeyEditor),
                new PropertyMetadata(string.Empty, OnEmptyTextChanged));

        bool _updatingText;

        public event EventHandler<HotkeyChangedEventArgs> HotkeyChanged;

        /// <summary>
        /// 当前快捷键。
        /// </summary>
        public HotkeyGesture Hotkey
        {
            get { return (HotkeyGesture)GetValue(HotkeyProperty); }
            set { SetValue(HotkeyProperty, value); }
        }

        /// <summary>
        /// 是否启用单键模式。
        /// 单键模式下会忽略 Ctrl / Alt / Shift / Win 修饰键，只记录主键。
        /// </summary>
        public bool SingleKeyMode
        {
            get { return (bool)GetValue(SingleKeyModeProperty); }
            set { SetValue(SingleKeyModeProperty, value); }
        }

        /// <summary>
        /// 单键模式下是否过滤 Ctrl / Alt / Shift / Win / Apps / Clear 等控制键。
        /// </summary>
        public bool FilterControlKeysInSingleMode
        {
            get { return (bool)GetValue(FilterControlKeysInSingleModeProperty); }
            set { SetValue(FilterControlKeysInSingleModeProperty, value); }
        }

        /// <summary>
        /// 普通模式下是否要求必须包含 Ctrl / Alt / Shift / Win。
        /// 用于全局快捷键时建议保持 true。
        /// 如果你允许 F1、F2 这种单键快捷键，可以设为 false。
        /// </summary>
        public bool RequireModifierKeys
        {
            get { return (bool)GetValue(RequireModifierKeysProperty); }
            set { SetValue(RequireModifierKeysProperty, value); }
        }

        /// <summary>
        /// 没有快捷键时显示的文本。
        /// 如果不想显示占位文本，保持空字符串即可。
        /// </summary>
        public string EmptyText
        {
            get { return (string)GetValue(EmptyTextProperty); }
            set { SetValue(EmptyTextProperty, value); }
        }

        public HotkeyEditor()
        {
            IsReadOnly = true;
            IsUndoEnabled = false;
            AcceptsReturn = false;
            AcceptsTab = false;
            VerticalContentAlignment = VerticalAlignment.Center;

            InputMethod.SetIsInputMethodEnabled(this, false);

            DataObject.AddPastingHandler(this, OnPasting);

            Loaded += HotkeyEditor_Loaded;
        }

        void HotkeyEditor_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateText();
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            e.Handled = true;
            base.OnPreviewTextInput(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            e.Handled = true;

            Key key = NormalizeKey(e);
            ModifierKeys modifiers = GetCurrentModifiers();

            if (key == Key.None)
                return;

            if (IsClearKey(key, modifiers))
            {
                ClearHotkey();
                return;
            }

            VirtualKeyCode virtualKey;
            if (!TryGetVirtualKeyCode(key, out virtualKey))
                return;

            if (SingleKeyMode)
            {
                if (FilterControlKeysInSingleMode &&
                    HotkeyGesture.IsControlOnlyKey(virtualKey))
                {
                    return;
                }

                SetHotkey(new HotkeyGesture(virtualKey, ModifierKeys.None));
                return;
            }

            if (HotkeyGesture.IsControlOnlyKey(virtualKey))
                return;

            if (RequireModifierKeys && modifiers == ModifierKeys.None)
                return;

            SetHotkey(new HotkeyGesture(virtualKey, modifiers));
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            SelectAll();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            UpdateText();
        }

        void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            e.CancelCommand();
        }

        public void BeginInput()
        {
            Focus();
            SelectAll();
        }

        public void ClearHotkey()
        {
            SetHotkey(HotkeyGesture.None);
        }

        public string GetKeyData()
        {
            HotkeyGesture hotkey = Hotkey;
            if (hotkey == null || hotkey.IsEmpty)
                return string.Empty;

            return hotkey.ToData();
        }

        public void SetData(string data)
        {
            Hotkey = HotkeyGesture.ParseOrNone(data);
        }

        void SetHotkey(HotkeyGesture hotkey)
        {
            if (hotkey == null)
                hotkey = HotkeyGesture.None;

            Hotkey = hotkey;
        }

        static void OnHotkeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = d as HotkeyEditor;
            if (editor == null)
                return;

            editor.UpdateText();

            var handler = editor.HotkeyChanged;
            if (handler != null)
            {
                handler(
                    editor,
                    new HotkeyChangedEventArgs(
                        e.OldValue as HotkeyGesture,
                        e.NewValue as HotkeyGesture));
            }
        }

        static void OnEmptyTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var editor = d as HotkeyEditor;
            if (editor != null)
                editor.UpdateText();
        }

        void UpdateText()
        {
            if (_updatingText)
                return;

            try
            {
                _updatingText = true;

                HotkeyGesture hotkey = Hotkey;
                if (hotkey == null || hotkey.IsEmpty)
                {
                    Text = EmptyText ?? string.Empty;
                    return;
                }

                Text = hotkey.ToString();
            }
            finally
            {
                _updatingText = false;
            }
        }

        static bool IsClearKey(Key key, ModifierKeys modifiers)
        {
            if (modifiers != ModifierKeys.None)
                return false;

            return key == Key.Back ||
                   key == Key.Delete ||
                   key == Key.Escape;
        }

        static Key NormalizeKey(KeyEventArgs e)
        {
            Key key = e.Key;

            if (key == Key.System)
                key = e.SystemKey;

            if (key == Key.ImeProcessed)
                key = e.ImeProcessedKey;

            if (key == Key.DeadCharProcessed)
                key = e.DeadCharProcessedKey;

            return key;
        }

        static ModifierKeys GetCurrentModifiers()
        {
            ModifierKeys modifiers = Keyboard.Modifiers;

            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
                modifiers |= ModifierKeys.Windows;

            return HotkeyGesture.NormalizeModifiers(modifiers);
        }

        static bool TryGetVirtualKeyCode(Key key, out VirtualKeyCode virtualKey)
        {
            virtualKey = (VirtualKeyCode)0;

            int value = KeyInterop.VirtualKeyFromKey(key);
            if (value == 0)
                return false;

            virtualKey = unchecked((VirtualKeyCode)value);

            if (!Enum.IsDefined(typeof(VirtualKeyCode), virtualKey))
                return false;

            return true;
        }
    }

    public sealed class HotkeyChangedEventArgs : EventArgs
    {
        public HotkeyGesture OldHotkey { get; private set; }

        public HotkeyGesture NewHotkey { get; private set; }

        public HotkeyChangedEventArgs(HotkeyGesture oldHotkey, HotkeyGesture newHotkey)
        {
            OldHotkey = oldHotkey;
            NewHotkey = newHotkey;
        }
    }
}