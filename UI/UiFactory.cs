using FontAwesome5;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供 Overlay / 工具栏场景下常用按钮与分隔项的快速创建方法。
    /// </summary>
    /// <remarks>
    /// 当前主要面向轻量工具栏 UI，而非通用全量 UI 控件工厂。
    /// </remarks>
    public static class UiFactory
    {
        private static readonly Brush TransparentBrush = Brushes.Transparent;

        private static readonly SolidColorBrush ToolHoverBrush =
            new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));

        private static readonly SolidColorBrush ToolPressedBrush =
            new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));

        private static readonly SolidColorBrush ToolCheckedBrush =
            new SolidColorBrush(Color.FromArgb(76, 30, 144, 255));

        private static readonly SolidColorBrush ToolSeparatorBrush =
            new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));

        static UiFactory()
        {
            ToolHoverBrush.Freeze();
            ToolPressedBrush.Freeze();
            ToolCheckedBrush.Freeze();
            ToolSeparatorBrush.Freeze();
        }

        /// <summary>
        /// 创建普通工具按钮。
        /// </summary>
        /// <param name="content">按钮内容。</param>
        /// <param name="tooltip">提示文本。</param>
        /// <param name="onClick">点击回调。</param>
        /// <param name="width">按钮宽度。</param>
        /// <param name="height">按钮高度。</param>
        /// <returns>创建后的按钮。</returns>
        public static Button ToolButton(
            object content,
            string tooltip,
            Action onClick,
            double width = 38.0,
            double height = 32.0)
        {
            var btn = new Button();
            InitToolButtonBase(btn, content, tooltip, width, height, new Thickness(2.0), 14.0);
            if (onClick != null)
                btn.Click += (_, __) => onClick();
            BindButtonVisualStates(btn);
            return btn;
        }

        /// <summary>
        /// 创建工具栏切换按钮。
        /// </summary>
        /// <param name="content">按钮内容。</param>
        /// <param name="tooltip">提示文本。</param>
        /// <param name="onClick">点击回调。</param>
        /// <param name="width">按钮宽度。</param>
        /// <param name="height">按钮高度。</param>
        /// <returns>创建后的切换按钮。</returns>
        public static ToggleButton ToolToggle(
            object content,
            string tooltip,
            Action onClick,
            double width = 38.0,
            double height = 32.0)
        {
            var btn = new ToggleButton();
            InitToolButtonBase(btn, content, tooltip, width, height, new Thickness(1.0), 14.0);
            if (onClick != null)
                btn.Click += (_, __) => onClick();
            BindToggleButtonVisualStates(btn);
            return btn;
        }

        /// <summary>
        /// 创建工具栏分隔项。
        /// </summary>
        /// <param name="marginX">左右外边距。</param>
        /// <param name="height">分隔线高度。</param>
        /// <returns>创建后的分隔项。</returns>
        public static Separator ToolSep(double marginX = 8.0, double height = 18.0)
        {
            return new Separator
            {
                Margin = new Thickness(marginX, 0.0, marginX, 0.0),
                Width = 1.0,
                Height = height,
                Background = ToolSeparatorBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        /// <summary>
        /// 将多个元素添加到指定面板中。
        /// </summary>
        /// <param name="panel">目标面板。</param>
        /// <param name="elements">要添加的元素集合。</param>
        public static void Add(Panel panel, params UIElement[] elements)
        {
            if (panel == null || elements == null)
                return;

            foreach (UIElement el in elements)
            {
                if (el != null)
                    panel.Children.Add(el);
            }
        }

        private static void InitToolButtonBase(
            ButtonBase button,
            object content,
            string tooltip,
            double width,
            double height,
            Thickness margin,
            double fontSize)
        {
            button.Content = NormalizeToolButtonContent(content);
            button.Width = width;
            button.Height = height;
            button.Margin = margin;
            button.Padding = new Thickness(0.0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.Cursor = Cursors.Hand;
            button.Foreground = Brushes.White;
            button.Background = TransparentBrush;
            button.BorderThickness = new Thickness(0.0);
            button.FontSize = fontSize;
            button.Focusable = false;
            button.IsTabStop = false;

            if (!string.IsNullOrEmpty(tooltip))
                button.ToolTip = new ToolTip { Content = tooltip, Placement = PlacementMode.Mouse };
        }

        /// <summary>
        /// 归一化工具按钮内容。
        /// 若内容为 <see cref="SvgAwesome"/> 图标，将其 Foreground 绑定到最近的
        /// <see cref="ButtonBase"/> 祖先，使图标颜色随按钮状态（如禁用时变灰）自动同步。
        /// </summary>
        private static object NormalizeToolButtonContent(object content)
        {
            var fa = content as SvgAwesome;
            if (fa != null)
            {
                fa.SetBinding(Control.ForegroundProperty, new Binding("Foreground")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ButtonBase), 1)
                });
                return fa;
            }
            return content;
        }

        private static void BindButtonVisualStates(Button btn)
        {
            btn.MouseEnter += (_, __) => RefreshButtonVisualState(btn);
            btn.MouseLeave += (_, __) => RefreshButtonVisualState(btn);
            btn.PreviewMouseDown += (_, __) => RefreshButtonVisualState(btn, isPressed: true);
            btn.PreviewMouseUp += (_, __) => RefreshButtonVisualState(btn);
            btn.LostMouseCapture += (_, __) => RefreshButtonVisualState(btn);
            btn.IsEnabledChanged += (_, __) => RefreshButtonVisualState(btn);
            RefreshButtonVisualState(btn);
        }

        private static void BindToggleButtonVisualStates(ToggleButton btn)
        {
            btn.MouseEnter += (_, __) => RefreshToggleButtonVisualState(btn);
            btn.MouseLeave += (_, __) => RefreshToggleButtonVisualState(btn);
            btn.PreviewMouseDown += (_, __) => RefreshToggleButtonVisualState(btn, isPressed: true);
            btn.PreviewMouseUp += (_, __) => RefreshToggleButtonVisualState(btn);
            btn.LostMouseCapture += (_, __) => RefreshToggleButtonVisualState(btn);
            btn.IsEnabledChanged += (_, __) => RefreshToggleButtonVisualState(btn);
            btn.Checked += (_, __) => RefreshToggleButtonVisualState(btn);
            btn.Unchecked += (_, __) => RefreshToggleButtonVisualState(btn);
            RefreshToggleButtonVisualState(btn);
        }

        private static void RefreshButtonVisualState(Button btn, bool isPressed = false)
        {
            if (!btn.IsEnabled)
            {
                btn.Background = TransparentBrush;
                return;
            }

            if (isPressed)
            {
                btn.Background = ToolPressedBrush;
                return;
            }

            btn.Background = btn.IsMouseOver ? ToolHoverBrush : TransparentBrush;
        }

        private static void RefreshToggleButtonVisualState(ToggleButton btn, bool isPressed = false)
        {
            if (!btn.IsEnabled)
            {
                btn.Background = TransparentBrush;
                return;
            }

            if (btn.IsChecked == true)
            {
                btn.Background = ToolCheckedBrush;
                return;
            }

            if (isPressed)
            {
                btn.Background = ToolPressedBrush;
                return;
            }

            btn.Background = btn.IsMouseOver ? ToolHoverBrush : TransparentBrush;
        }
    }
}