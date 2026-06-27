using System;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace KkjQuicker.UI.Adorners
{
    /// <summary>
    /// 拖拽虚影 Adorner：显示被拖拽元素的"幽灵预览"，并可选显示文本标签。
    ///
    /// 典型用途：ItemsControl 内部按钮拖拽，从一个列表拖到另一个列表时显示按钮虚影。
    ///
    /// 使用建议：
    /// - adornedElement 建议选 Window 根容器（或两个 ItemsControl 的共同父容器），这样虚影可跨控件显示且不易被裁剪。
    /// - dragOffset 建议传 e.GetPosition(item)，保证虚影的点击点与鼠标一致（手感最好）。
    /// </summary>
    public sealed class DragAdorner : Adorner
    {
        private readonly VisualBrush _brush;

        private UIElement? _dragVisual;              // Current drag preview visual.
        private FrameworkElement? _dragFE;           // Optional SizeChanged source.
        private SizeChangedEventHandler? _sizeHandler;

        private Point _dragOffset;                  // 鼠标在拖拽元素内的相对点
        private Point _mousePos;                    // 鼠标相对 AdornedElement 的位置

        // Text
        private string? _text;

        private bool _textDirty = true;
        private Typeface _typeface = new Typeface("Segoe UI");
        private double _fontSize = 12;
        private Brush _textBrush = Brushes.Black;
        private FormattedText? _formattedText;
        private double _lastPixelsPerDip = -1;

        /// <summary>文本相对鼠标的偏移。</summary>
        public Point TextOffset { get; set; } = new Point(16, 16);

        /// <summary>幽灵预览透明度（不遮蔽基类 UIElement.Opacity）。</summary>
        public double GhostOpacity
        {
            get { return _brush.Opacity; }
            set { _brush.Opacity = value; }
        }

        /// <summary>是否绘制文本背景框。</summary>
        public bool DrawTextBackground { get; set; } = true;

        /// <summary>文本背景画刷（默认白底）。</summary>
        public Brush TextBackground { get; set; } = Brushes.White;

        /// <summary>
        /// 文本边框画笔（默认灰边，1px）。
        /// 若替换默认值，建议对自定义 <see cref="Pen"/> 调用 <see cref="Freezable.Freeze"/> 以避免渲染开销。
        /// </summary>
        public Pen TextBorder { get; set; } = CreateDefaultBorder();

        /// <summary>文本背景内边距（默认 4,3,4,3）。</summary>
        public Thickness TextPadding { get; set; } = new Thickness(4, 3, 4, 3);

        /// <summary>当前文本（null/empty 表示不显示）。</summary>
        public string? Text
        {
            get { return _text; }
            set { SetText(value); }
        }

        private DragAdorner(UIElement adornedElement, UIElement dragVisual, Point dragOffset, double opacity, string? text)
            : base(adornedElement ?? throw new ArgumentNullException(nameof(adornedElement)))
        {
            ArgumentNullException.ThrowIfNull(dragVisual);

            IsHitTestVisible = false;

            _brush = new VisualBrush(dragVisual)
            {
                Opacity = opacity,
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };

            _dragOffset = dragOffset;
            _text = text;
            _textDirty = true;

            SetDragVisualCore(dragVisual);
        }

        private static Pen CreateDefaultBorder()
        {
            var pen = new Pen(Brushes.Gray, 1);
            pen.Freeze();
            return pen;
        }

        #region ===== Rendering =====

        protected override void OnRender(DrawingContext dc)
        {
            var visual = _dragVisual;
            if (visual == null || _brush.Visual == null)
                return;

            // 1) 绘制幽灵预览（按 RenderSize）
            // UIElement 直接提供 RenderSize，无需强转为 FrameworkElement
            var size = visual.RenderSize;
            if (!size.IsEmpty &&
                size.Width > 0 && size.Height > 0 &&
                !double.IsNaN(size.Width) && !double.IsNaN(size.Height))
            {
                var origin = new Point(_mousePos.X - _dragOffset.X, _mousePos.Y - _dragOffset.Y);
                dc.DrawRectangle(_brush, null, new Rect(origin, size));
            }

            // 2) 绘制文本（可选）
            if (!string.IsNullOrEmpty(_text))
            {
                EnsureFormattedText();
                if (_formattedText != null)
                {
                    var textPos = new Point(_mousePos.X + TextOffset.X, _mousePos.Y + TextOffset.Y);

                    var bgSize = new Size(
                        _formattedText.Width + TextPadding.Left + TextPadding.Right,
                        _formattedText.Height + TextPadding.Top + TextPadding.Bottom);

                    var bgRect = new Rect(textPos, bgSize);

                    if (DrawTextBackground)
                        dc.DrawRectangle(TextBackground, TextBorder, bgRect);

                    var drawPos = new Point(textPos.X + TextPadding.Left, textPos.Y + TextPadding.Top);
                    dc.DrawText(_formattedText, drawPos);
                }
            }
        }

        private void EnsureFormattedText()
        {
            double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (!_textDirty && _lastPixelsPerDip == ppd) return;

            _lastPixelsPerDip = ppd;
            _textDirty = false;

            if (string.IsNullOrEmpty(_text))
            {
                _formattedText = null;
                return;
            }

            _formattedText = new FormattedText(
                _text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _typeface,
                Math.Max(1, _fontSize),
                _textBrush,
                ppd);
        }

        #endregion ===== Rendering =====

        #region ===== Update Position =====

        /// <summary>用 MouseEventArgs 更新鼠标位置。</summary>
        public void UpdatePosition(MouseEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            UpdatePositionCore(e.GetPosition(AdornedElement));
        }

        /// <summary>用 DragEventArgs 更新鼠标位置。</summary>
        public void UpdatePosition(DragEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            UpdatePositionCore(e.GetPosition(AdornedElement));
        }

        /// <summary>
        /// 直接用 Point 更新鼠标位置（推荐：你在 DragOver/MouseMove 常常已经算好相对坐标）。
        /// mousePos 为相对 AdornedElement 坐标。
        /// </summary>
        public void UpdatePosition(Point mousePos)
        {
            UpdatePositionCore(mousePos);
        }

        private void UpdatePositionCore(Point pos)
        {
            if (pos == _mousePos) return;
            _mousePos = pos;
            InvalidateVisual();
        }

        #endregion ===== Update Position =====

        #region ===== Update Visual / Offset / Text =====

        /// <summary>更新拖拽偏移（鼠标在拖拽元素内的相对点）。</summary>
        public void SetDragOffset(Point dragOffset)
        {
            if (dragOffset == _dragOffset) return;
            _dragOffset = dragOffset;
            InvalidateVisual();
        }

        /// <summary>更新预览视觉（拖拽中如果需要替换虚影视觉）。</summary>
        public void SetDragVisual(UIElement dragVisual)
        {
            ArgumentNullException.ThrowIfNull(dragVisual);
            SetDragVisualCore(dragVisual);
            InvalidateVisual();
        }

        /// <summary>
        /// 设置显示在虚影旁的文本标签；传入 <see langword="null"/> 或空字符串表示不显示。
        /// </summary>
        public void SetText(string? text)
        {
            if (string.Equals(_text, text, StringComparison.Ordinal)) return;
            _text = text;
            _textDirty = true;
            InvalidateVisual();
        }

        /// <summary>清除文本标签。</summary>
        public void ClearText()
        {
            if (string.IsNullOrEmpty(_text)) return;
            _text = null;
            _formattedText = null;
            _textDirty = false;
            InvalidateVisual();
        }

        /// <summary>
        /// 更新文本样式。仅传入需要修改的参数，未传入的参数保持当前值不变。
        /// </summary>
        /// <param name="fontSize">字号；须大于 0，否则忽略。</param>
        /// <param name="textBrush">文本画刷。</param>
        /// <param name="typeface">字体。</param>
        public void SetTextStyle(double? fontSize = null, Brush? textBrush = null, Typeface? typeface = null)
        {
            bool changed = false;

            if (fontSize.HasValue && fontSize.Value > 0 && !fontSize.Value.Equals(_fontSize))
            {
                _fontSize = fontSize.Value;
                changed = true;
            }

            if (textBrush != null && !ReferenceEquals(textBrush, _textBrush))
            {
                _textBrush = textBrush;
                changed = true;
            }

            if (typeface != null && !Equals(typeface, _typeface))
            {
                _typeface = typeface;
                changed = true;
            }

            if (changed)
            {
                _textDirty = true;
                InvalidateVisual();
            }
        }

        private void SetDragVisualCore(UIElement dragVisual)
        {
            if (ReferenceEquals(_dragVisual, dragVisual) && ReferenceEquals(_brush.Visual, dragVisual))
                return;

            // 解绑旧 SizeChanged，避免泄漏/错绘
            if (_dragFE != null && _sizeHandler != null)
            {
                _dragFE.SizeChanged -= _sizeHandler;
                _dragFE = null;
                _sizeHandler = null;
            }

            _dragVisual = dragVisual;
            _brush.Visual = dragVisual;

            // 监听新视觉尺寸变化（比如按钮模板变化、内容变化导致 RenderSize 改变）
            var fe = dragVisual as FrameworkElement;
            if (fe != null)
            {
                _dragFE = fe;
                _sizeHandler = (s, e) => InvalidateVisual();
                _dragFE.SizeChanged += _sizeHandler;
            }
        }

        #endregion ===== Update Visual / Offset / Text =====

        #region ===== Attach / Detach =====

        /// <summary>
        /// 创建或更新 DragAdorner。
        /// </summary>
        public static DragAdorner Attach(
            UIElement adornedElement,
            UIElement dragVisual,
            Point dragOffset,
            double opacity = 0.7,
            string? text = null)
        {
            ArgumentNullException.ThrowIfNull(adornedElement);
            ArgumentNullException.ThrowIfNull(dragVisual);

            var layer = AdornerLayer.GetAdornerLayer(adornedElement);
            if (layer == null)
                throw new InvalidOperationException("找不到 AdornerLayer。请确认 adornedElement 在可视树中，并存在 AdornerDecorator。");

            var adorners = layer.GetAdorners(adornedElement);
            if (adorners != null)
            {
                for (int i = 0; i < adorners.Length; i++)
                {
                    var da = adorners[i] as DragAdorner;
                    if (da != null)
                    {
                        da._dragOffset = dragOffset;
                        da.GhostOpacity = opacity;
                        da._text = text;
                        da._textDirty = true;

                        // ★ 关键：更新视觉必须同步更新 _dragVisual 与 SizeChanged 订阅
                        da.SetDragVisualCore(dragVisual);

                        da.InvalidateVisual();
                        return da;
                    }
                }
            }

            var adorner = new DragAdorner(adornedElement, dragVisual, dragOffset, opacity, text);
            layer.Add(adorner);
            return adorner;
        }

        /// <summary>
        /// 从 adornedElement 上移除 DragAdorner（若存在），并返回被移除的实例（可能为 null）。
        /// 适合：拖拽结束时不持有引用也能清理。
        /// </summary>
        public static DragAdorner? Detach(UIElement? adornedElement)
        {
            if (adornedElement == null) return null;

            var layer = AdornerLayer.GetAdornerLayer(adornedElement);
            if (layer == null) return null;

            var adorners = layer.GetAdorners(adornedElement);
            if (adorners == null) return null;

            for (int i = 0; i < adorners.Length; i++)
            {
                var da = adorners[i] as DragAdorner;
                if (da != null)
                {
                    layer.Remove(da);
                    da.Cleanup();
                    return da;
                }
            }

            return null;
        }

        /// <summary>移除指定 DragAdorner。</summary>
        public static void Detach(DragAdorner? adorner)
        {
            if (adorner == null) return;

            var layer = AdornerLayer.GetAdornerLayer(adorner.AdornedElement);
            layer?.Remove(adorner);

            adorner.Cleanup();
        }

        private void Cleanup()
        {
            if (_dragFE != null && _sizeHandler != null)
            {
                _dragFE.SizeChanged -= _sizeHandler;
                _dragFE = null;
                _sizeHandler = null;
            }

            _brush.Visual = null;
            _dragVisual = null;

            _formattedText = null;
            _textDirty = true;
        }

        #endregion ===== Attach / Detach =====
    }
}
