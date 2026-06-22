using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KkjQuicker.UI.Adorners
{
    /// <summary>
    /// 为 Overlay 面板中的可见性判断、点击行为等委托提供运行上下文。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该上下文会在刷新项目可见性时创建，并携带当前附着目标、所属面板以及所属 <see cref="OverlayAdorner"/>。
    /// </para>
    /// <para>
    /// 它的定位是一个轻量上下文对象，用于减少外部委托中重复的类型转换与对象访问代码。
    /// </para>
    /// </remarks>
    public sealed class OverlayActionContext
    {
        internal OverlayActionContext(FrameworkElement target, OverlayPanel panel, OverlayAdorner adorner)
        {
            Target = target;
            Panel = panel;
            Adorner = adorner;
        }

        /// <summary>
        /// 获取当前附着的目标元素。
        /// </summary>
        public FrameworkElement Target { get; private set; }

        /// <summary>
        /// 获取当前正在工作的 Overlay 面板。
        /// </summary>
        public OverlayPanel Panel { get; private set; }

        /// <summary>
        /// 获取当前所属的 <see cref="OverlayAdorner"/>。
        /// </summary>
        public OverlayAdorner Adorner { get; private set; }

        /// <summary>
        /// 将 <see cref="Target"/> 转换为指定引用类型。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <returns>转换成功时返回对应实例；否则返回 <see langword="null"/>。</returns>
        public T As<T>() where T : class
        {
            return Target as T;
        }

        /// <summary>
        /// 当 <see cref="Target"/> 可转换为指定类型时，执行函数并返回结果。
        /// </summary>
        /// <typeparam name="TTarget">目标元素希望转换成的类型。</typeparam>
        /// <typeparam name="TResult">返回值类型。</typeparam>
        /// <param name="func">要执行的函数。</param>
        /// <param name="defaultValue">
        /// 当 <paramref name="func"/> 为 <see langword="null"/>，或 <see cref="Target"/> 无法转换为
        /// <typeparamref name="TTarget"/> 时返回的默认值。
        /// </param>
        /// <returns>函数结果，或 <paramref name="defaultValue"/>。</returns>
        public TResult Dispatch<TTarget, TResult>(Func<TTarget, TResult> func, TResult defaultValue = default(TResult))
            where TTarget : class
        {
            if (func == null)
                return defaultValue;

            TTarget target = Target as TTarget;
            return target != null ? func(target) : defaultValue;
        }

        /// <summary>
        /// 当 <see cref="Target"/> 可转换为指定类型时，执行指定动作。
        /// </summary>
        /// <typeparam name="TTarget">目标元素希望转换成的类型。</typeparam>
        /// <param name="action">要执行的动作。</param>
        public void Dispatch<TTarget>(Action<TTarget> action) where TTarget : class
        {
            if (action == null)
                return;

            TTarget target = Target as TTarget;
            if (target != null)
                action(target);
        }
    }

    /// <summary>
    /// 表示 Overlay 面板中一个项目的最小统一接口。
    /// </summary>
    public interface IOverlayItem
    {
        /// <summary>
        /// 获取该项目对应的可视元素。
        /// </summary>
        UIElement Element { get; }

        /// <summary>
        /// 刷新当前项目的可见性，并返回刷新后的最终可见状态。
        /// </summary>
        /// <param name="context">当前运行上下文。</param>
        /// <returns>项目最终是否可见。</returns>
        bool RefreshVisibility(OverlayActionContext context);

        /// <summary>
        /// 当项目从所属面板分离时执行清理逻辑。
        /// </summary>
        void Detach();
    }

    /// <summary>
    /// <see cref="IOverlayItem"/> 的轻量基类。
    /// </summary>
    public abstract class OverlayItemBase : IOverlayItem
    {
        /// <summary>
        /// 获取该项目对应的可视元素。
        /// </summary>
        public abstract UIElement Element { get; }

        /// <summary>
        /// 刷新当前项目的可见性，并返回刷新后的最终可见状态。
        /// </summary>
        /// <param name="context">当前运行上下文。</param>
        /// <returns>项目最终是否可见。</returns>
        public abstract bool RefreshVisibility(OverlayActionContext context);

        /// <summary>
        /// 当项目从所属面板分离时执行清理逻辑。
        /// </summary>
        public virtual void Detach()
        {
        }

        /// <summary>
        /// 评估项目是否可见。
        /// </summary>
        /// <param name="visibleWhen">可见性判断委托；为 <see langword="null"/> 时默认视为可见。</param>
        /// <param name="context">当前运行上下文。</param>
        /// <returns>是否可见。</returns>
        /// <remarks>
        /// <para>
        /// 为保证 Overlay 逻辑稳定，委托抛出的异常会被捕获并按不可见处理。
        /// </para>
        /// <para>
        /// 在 DEBUG 模式下会通过 <see cref="Debug.WriteLine"/> 输出异常信息，便于排查外部条件函数中的错误。
        /// </para>
        /// </remarks>
        protected static bool EvaluateVisible(Func<OverlayActionContext, bool> visibleWhen, OverlayActionContext context)
        {
            if (visibleWhen == null)
                return true;

            try
            {
                return visibleWhen(context);
            }
            catch (Exception ex)
            {
                DebugLogException(ex);
                return false;
            }
        }

        /// <summary>
        /// 设置元素的可见性。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <param name="visible">是否可见。</param>
        protected static void SetElementVisibility(UIElement element, bool visible)
        {
            if (element == null)
                return;

            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        [Conditional("DEBUG")]
        private static void DebugLogException(Exception ex)
        {
            Debug.WriteLine("[OverlayItemBase] visibleWhen 委托执行时抛出异常，已按不可见处理：" + ex);
        }
    }

    /// <summary>
    /// 直接承载现成 <see cref="UIElement"/> 的最轻量项目实现。
    /// </summary>
    public sealed class RawItem : OverlayItemBase
    {
        private readonly UIElement _element;
        private readonly Func<OverlayActionContext, bool> _visibleWhen;

        /// <summary>
        /// 初始化一个 <see cref="RawItem"/> 实例。
        /// </summary>
        /// <param name="element">要承载的元素。</param>
        /// <param name="visibleWhen">可见条件；为 <see langword="null"/> 时默认可见。</param>
        public RawItem(UIElement element, Func<OverlayActionContext, bool> visibleWhen = null)
        {
            _element = element;
            _visibleWhen = visibleWhen;
        }

        /// <summary>
        /// 获取该项目对应的元素。
        /// </summary>
        public override UIElement Element
        {
            get { return _element; }
        }

        /// <summary>
        /// 刷新元素可见性。
        /// </summary>
        /// <param name="context">当前运行上下文。</param>
        /// <returns>元素最终是否可见。</returns>
        public override bool RefreshVisibility(OverlayActionContext context)
        {
            if (_element == null)
                return false;

            bool visible = EvaluateVisible(_visibleWhen, context);
            SetElementVisibility(_element, visible);
            return visible;
        }
    }

    /// <summary>
    /// 指定 Overlay 面板相对目标元素的锚点位置。
    /// </summary>
    public enum OverlayAnchor
    {
        /// <summary>左上角。</summary>
        TopLeft,
        /// <summary>上边中点。</summary>
        Top,
        /// <summary>右上角。</summary>
        TopRight,
        /// <summary>左边中点。</summary>
        Left,
        /// <summary>中心点。</summary>
        Center,
        /// <summary>右边中点。</summary>
        Right,
        /// <summary>左下角。</summary>
        BottomLeft,
        /// <summary>下边中点。</summary>
        Bottom,
        /// <summary>右下角。</summary>
        BottomRight
    }

    /// <summary>
    /// 指定 Overlay 面板中项目的排列方向。
    /// </summary>
    public enum OverlayOrientation
    {
        /// <summary>水平排列。</summary>
        Horizontal,
        /// <summary>垂直排列。</summary>
        Vertical
    }

    /// <summary>
    /// 表示 Overlay 面板的布局配置。
    /// </summary>
    public sealed class OverlayLayout
    {
        /// <summary>
        /// 初始化一个 <see cref="OverlayLayout"/> 实例，并填充默认布局配置。
        /// </summary>
        public OverlayLayout()
        {
            Anchor = OverlayAnchor.TopRight;
            Orientation = OverlayOrientation.Horizontal;
            Offset = new Point(0, 0);
        }

        /// <summary>
        /// 获取或设置面板相对目标元素的锚点位置。
        /// </summary>
        public OverlayAnchor Anchor { get; set; }

        /// <summary>
        /// 获取或设置面板内部项目的排列方向。
        /// </summary>
        public OverlayOrientation Orientation { get; set; }

        /// <summary>
        /// 获取或设置在锚点基础上的附加偏移量。
        /// </summary>
        /// <remarks>
        /// X 为水平偏移，Y 为垂直偏移。正负方向遵循 WPF 坐标系。
        /// </remarks>
        public Point Offset { get; set; }
    }

    /// <summary>
    /// 表示 Overlay 面板的样式配置。
    /// </summary>
    public sealed class OverlayStyle
    {
        /// <summary>
        /// 初始化一个 <see cref="OverlayStyle"/> 实例，并填充默认样式配置。
        /// </summary>
        public OverlayStyle()
        {
            Padding = new Thickness(4);
            ItemSpacing = 4;
            Background = new SolidColorBrush(Color.FromArgb(210, 32, 32, 32));
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(6);
            VisibleOpacity = 1.0;
            HiddenOpacity = 0.0;
            FadeDuration = TimeSpan.FromMilliseconds(120);
        }

        /// <summary>
        /// 获取或设置面板内边距。
        /// </summary>
        public Thickness Padding { get; set; }

        /// <summary>
        /// 获取或设置项目之间的统一间距。
        /// </summary>
        /// <remarks>
        /// 该间距由面板统一控制，添加的子元素其现有 <see cref="FrameworkElement.Margin"/> 会由面板重设。
        /// 如需精细控制外边距，请在元素内部自行包裹容器。
        /// </remarks>
        public double ItemSpacing { get; set; }

        /// <summary>
        /// 获取或设置面板背景画刷。
        /// </summary>
        public Brush Background { get; set; }

        /// <summary>
        /// 获取或设置面板边框画刷。
        /// </summary>
        public Brush BorderBrush { get; set; }

        /// <summary>
        /// 获取或设置面板边框厚度。
        /// </summary>
        public Thickness BorderThickness { get; set; }

        /// <summary>
        /// 获取或设置面板圆角。
        /// </summary>
        public CornerRadius CornerRadius { get; set; }

        /// <summary>
        /// 获取或设置面板可见时的不透明度。
        /// </summary>
        /// <remarks>通常应位于 0 到 1 之间。</remarks>
        public double VisibleOpacity { get; set; }

        /// <summary>
        /// 获取或设置面板隐藏状态使用的不透明度。
        /// </summary>
        /// <remarks>通常应位于 0 到 1 之间。</remarks>
        public double HiddenOpacity { get; set; }

        /// <summary>
        /// 获取或设置面板淡入动画时长。
        /// </summary>
        /// <remarks>
        /// 当值小于或等于 <see cref="TimeSpan.Zero"/> 时，不使用淡入动画。
        /// </remarks>
        public TimeSpan FadeDuration { get; set; }
    }

    /// <summary>
    /// 表示一个附着在目标元素上的 Overlay 面板。
    /// </summary>
    public sealed class OverlayPanel
    {
        private readonly FrameworkElement _target;
        private readonly OverlayAdorner _adorner;
        private readonly OverlayLayout _layout;
        private readonly OverlayStyle _style;
        private readonly List<IOverlayItem> _items = new List<IOverlayItem>();
        private readonly Dictionary<FrameworkElement, SizeChangedEventHandler> _sizeChangedHandlers =
            new Dictionary<FrameworkElement, SizeChangedEventHandler>();

        private Border _panelRoot;
        private StackPanel _stackPanel;
        private bool _isAttached;
        private bool _isDetached;
        private bool _isPanelVisible;

        internal OverlayPanel(
            FrameworkElement target,
            OverlayAdorner adorner,
            OverlayLayout layout,
            OverlayStyle style)
        {
            _target = target;
            _adorner = adorner;
            _layout = layout ?? new OverlayLayout();
            _style = style ?? new OverlayStyle();
        }

        internal UIElement RootElement
        {
            get
            {
                EnsureVisual();
                return _panelRoot;
            }
        }

        /// <summary>
        /// 获取当前面板的布局配置对象。
        /// </summary>
        /// <remarks>
        /// 返回的是活动配置实例本身。修改其属性后，可通过调用 <see cref="RefreshVisibility"/>
        /// 或宿主重排使界面更新。
        /// </remarks>
        public OverlayLayout Layout
        {
            get { return _layout; }
        }

        /// <summary>
        /// 获取当前面板的样式配置对象。
        /// </summary>
        /// <remarks>
        /// 返回的是活动配置实例本身。修改其属性后，如需反映到现有可视树，应自行触发相应刷新。
        /// </remarks>
        public OverlayStyle Style
        {
            get { return _style; }
        }

        /// <summary>
        /// 获取当前面板中的所有项目。
        /// </summary>
        public IReadOnlyList<IOverlayItem> Items
        {
            get { return _items; }
        }

        /// <summary>
        /// 向面板添加一个现成元素。
        /// </summary>
        /// <param name="element">要承载的元素。</param>
        /// <param name="visibleWhen">可见条件；为 <see langword="null"/> 时默认可见。</param>
        /// <returns>当前面板实例，便于链式调用。</returns>
        /// <exception cref="ObjectDisposedException">面板已分离，不能继续使用。</exception>
        /// <remarks>
        /// <para>
        /// 添加后会立即刷新当前面板显隐状态并请求重新布局。
        /// </para>
        /// <para>
        /// 为统一控制项目间距，面板会接管该元素的 <see cref="FrameworkElement.Margin"/>。
        /// </para>
        /// <para>
        /// 当添加的元素尺寸后续发生变化时，面板会自动请求重新布局，以修正锚点定位。
        /// </para>
        /// </remarks>
        public OverlayPanel AddChild(UIElement element, Func<OverlayActionContext, bool> visibleWhen = null)
        {
            ThrowIfDetached();

            if (element == null)
                return this;

            EnsureVisual();

            var item = new RawItem(element, visibleWhen);
            _items.Add(item);
            _stackPanel.Children.Add(element);

            RegisterChildLayoutListener(element);

            RefreshVisibility();
            _adorner.InvalidateArrange();

            return this;
        }

        /// <summary>
        /// 刷新所有项目的可见性，并同步更新面板自身的显隐状态。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 当所有项目均不可见时，面板会整体折叠；当至少有一个项目可见时，面板显示。
        /// </para>
        /// <para>
        /// 每次刷新后，面板会重新计算各项目的间距，确保末尾无多余空白。
        /// </para>
        /// </remarks>
        public void RefreshVisibility()
        {
            if (_isDetached)
                return;

            EnsureVisual();

            OverlayActionContext context = CreateActionContext();
            bool anyVisible = false;

            for (int i = 0; i < _items.Count; i++)
            {
                IOverlayItem item = _items[i];
                if (item == null)
                    continue;

                if (item.RefreshVisibility(context))
                    anyVisible = true;
            }

            // 在项目可见性更新后刷新间距。
            // 仅对可见子元素的末项清除间距，避免末尾出现多余空白。
            RefreshSpacing();

            ApplyPanelVisibility(anyVisible);
            _adorner.InvalidateArrange();
        }

        internal void AttachVisual()
        {
            if (_isAttached || _isDetached)
                return;

            EnsureVisual();
            RefreshVisibility();
            _isAttached = true;
        }

        internal void DetachVisual()
        {
            if (_isDetached)
                return;

            foreach (var pair in _sizeChangedHandlers)
            {
                pair.Key.SizeChanged -= pair.Value;
            }
            _sizeChangedHandlers.Clear();

            for (int i = 0; i < _items.Count; i++)
            {
                IOverlayItem item = _items[i];
                if (item != null)
                    item.Detach();
            }

            _isAttached = false;
            _isDetached = true;
        }

        internal void UpdatePlacement()
        {
            if (_panelRoot == null || _target == null)
                return;

            _stackPanel.Orientation = _layout.Orientation == OverlayOrientation.Horizontal
                ? Orientation.Horizontal
                : Orientation.Vertical;

            _panelRoot.Padding = _style.Padding;
            _panelRoot.Background = _style.Background;
            _panelRoot.BorderBrush = _style.BorderBrush;
            _panelRoot.BorderThickness = _style.BorderThickness;
            _panelRoot.CornerRadius = _style.CornerRadius;

            RefreshSpacing();

            _panelRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Size panelSize = _panelRoot.DesiredSize;
            Size targetSize = new Size(_target.ActualWidth, _target.ActualHeight);

            Point point = GetAnchorPoint(targetSize, panelSize, _layout.Anchor);
            point.Offset(_layout.Offset.X, _layout.Offset.Y);

            Canvas.SetLeft(_panelRoot, point.X);
            Canvas.SetTop(_panelRoot, point.Y);
        }

        private void EnsureVisual()
        {
            if (_panelRoot != null)
                return;

            _stackPanel = new StackPanel
            {
                Orientation = _layout.Orientation == OverlayOrientation.Horizontal
                    ? Orientation.Horizontal
                    : Orientation.Vertical
            };

            _panelRoot = new Border
            {
                Padding = _style.Padding,
                Background = _style.Background,
                BorderBrush = _style.BorderBrush,
                BorderThickness = _style.BorderThickness,
                CornerRadius = _style.CornerRadius,
                Opacity = _style.HiddenOpacity,
                Visibility = Visibility.Collapsed,
                Child = _stackPanel,
                IsHitTestVisible = true
            };
        }

        private void RegisterChildLayoutListener(UIElement element)
        {
            FrameworkElement fe = element as FrameworkElement;
            if (fe == null)
                return;

            if (_sizeChangedHandlers.ContainsKey(fe))
                return;

            SizeChangedEventHandler handler = delegate
            {
                if (_isDetached)
                    return;

                _adorner.InvalidateArrange();
            };

            _sizeChangedHandlers.Add(fe, handler);
            fe.SizeChanged += handler;
        }

        /// <summary>
        /// 刷新各子元素的外边距，使末尾可见项不带多余间距。
        /// </summary>
        /// <remarks>
        /// 间距基于当前可见性状态计算：<see cref="Visibility.Collapsed"/> 的元素不参与"末项"判断，
        /// 从而避免末尾出现因隐藏项占位留下的多余空白。
        /// </remarks>
        private void RefreshSpacing()
        {
            if (_stackPanel == null)
                return;

            bool horizontal = _layout.Orientation == OverlayOrientation.Horizontal;

            // 找到最后一个可见子元素的索引，只有它不需要右/下间距
            int lastVisibleIndex = -1;
            for (int i = _stackPanel.Children.Count - 1; i >= 0; i--)
            {
                if (_stackPanel.Children[i].Visibility != Visibility.Collapsed)
                {
                    lastVisibleIndex = i;
                    break;
                }
            }

            for (int i = 0; i < _stackPanel.Children.Count; i++)
            {
                FrameworkElement fe = _stackPanel.Children[i] as FrameworkElement;
                if (fe == null)
                    continue;

                bool isLastVisible = i == lastVisibleIndex;

                fe.Margin = horizontal
                    ? (isLastVisible ? new Thickness(0) : new Thickness(0, 0, _style.ItemSpacing, 0))
                    : (isLastVisible ? new Thickness(0) : new Thickness(0, 0, 0, _style.ItemSpacing));
            }
        }

        private OverlayActionContext CreateActionContext()
        {
            return new OverlayActionContext(_target, this, _adorner);
        }

        private void ApplyPanelVisibility(bool visible)
        {
            if (_panelRoot == null)
                return;

            if (!visible)
            {
                // 已经处于隐藏状态且参数未变化，无需重复操作
                if (!_isPanelVisible &&
                    _panelRoot.Visibility == Visibility.Collapsed &&
                    Math.Abs(_panelRoot.Opacity - _style.HiddenOpacity) < 0.0001)
                {
                    return;
                }

                _panelRoot.BeginAnimation(UIElement.OpacityProperty, null);
                _panelRoot.Opacity = _style.HiddenOpacity;
                _panelRoot.Visibility = Visibility.Collapsed;
                _isPanelVisible = false;
                return;
            }

            // 已处于可见状态：仅在 VisibleOpacity 发生变化时才重设，正常路径下直接返回
            if (_isPanelVisible && _panelRoot.Visibility == Visibility.Visible)
            {
                if (Math.Abs(_panelRoot.Opacity - _style.VisibleOpacity) > 0.0001)
                {
                    _panelRoot.BeginAnimation(UIElement.OpacityProperty, null);
                    _panelRoot.Opacity = _style.VisibleOpacity;
                }
                return;
            }

            // 从隐藏切换到可见
            _panelRoot.Visibility = Visibility.Visible;

            if (_style.FadeDuration <= TimeSpan.Zero)
            {
                _panelRoot.BeginAnimation(UIElement.OpacityProperty, null);
                _panelRoot.Opacity = _style.VisibleOpacity;
                _isPanelVisible = true;
                return;
            }

            _panelRoot.BeginAnimation(UIElement.OpacityProperty, null);
            _panelRoot.Opacity = _style.HiddenOpacity;

            var animation = new DoubleAnimation
            {
                From = _style.HiddenOpacity,
                To = _style.VisibleOpacity,
                Duration = new Duration(_style.FadeDuration),
                FillBehavior = FillBehavior.HoldEnd
            };

            _panelRoot.BeginAnimation(UIElement.OpacityProperty, animation);
            _isPanelVisible = true;
        }

        private void ThrowIfDetached()
        {
            if (_isDetached)
                throw new ObjectDisposedException(nameof(OverlayPanel));
        }

        private static Point GetAnchorPoint(Size targetSize, Size panelSize, OverlayAnchor anchor)
        {
            switch (anchor)
            {
                case OverlayAnchor.TopLeft:
                    return new Point(0, 0);

                case OverlayAnchor.Top:
                    return new Point((targetSize.Width - panelSize.Width) / 2, 0);

                case OverlayAnchor.TopRight:
                    return new Point(targetSize.Width - panelSize.Width, 0);

                case OverlayAnchor.Left:
                    return new Point(0, (targetSize.Height - panelSize.Height) / 2);

                case OverlayAnchor.Center:
                    return new Point(
                        (targetSize.Width - panelSize.Width) / 2,
                        (targetSize.Height - panelSize.Height) / 2);

                case OverlayAnchor.Right:
                    return new Point(
                        targetSize.Width - panelSize.Width,
                        (targetSize.Height - panelSize.Height) / 2);

                case OverlayAnchor.BottomLeft:
                    return new Point(0, targetSize.Height - panelSize.Height);

                case OverlayAnchor.Bottom:
                    return new Point(
                        (targetSize.Width - panelSize.Width) / 2,
                        targetSize.Height - panelSize.Height);

                case OverlayAnchor.BottomRight:
                    return new Point(
                        targetSize.Width - panelSize.Width,
                        targetSize.Height - panelSize.Height);

                default:
                    return new Point(0, 0);
            }
        }
    }

    /// <summary>
    /// 附着在目标元素上的 Overlay Adorner 主体。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该类型负责承载一个或多个 <see cref="OverlayPanel"/>，并在目标元素上方进行布局。
    /// </para>
    /// <para>
    /// 每个目标元素通常只需要一个 <see cref="OverlayAdorner"/> 实例；
    /// 重复调用 <see cref="Attach"/> 时会优先复用现有实例。
    /// </para>
    /// </remarks>
    public sealed class OverlayAdorner : Adorner
    {
        private readonly VisualCollection _visuals;
        private readonly Canvas _canvas;
        private readonly List<OverlayPanel> _panels = new List<OverlayPanel>();
        private bool _isDetached;

        private OverlayAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = true;
            _visuals = new VisualCollection(this);
            _canvas = new Canvas();
            _visuals.Add(_canvas);
        }

        /// <summary>
        /// 获取当前附着的目标元素。
        /// </summary>
        public FrameworkElement Target
        {
            get { return AdornedElement as FrameworkElement; }
        }

        /// <summary>
        /// 获取当前已添加的所有 Overlay 面板。
        /// </summary>
        public IReadOnlyList<OverlayPanel> Panels
        {
            get { return _panels; }
        }

        /// <summary>
        /// 附着到指定目标元素，并返回对应的 <see cref="OverlayAdorner"/>。
        /// </summary>
        /// <param name="target">要附着的目标元素。</param>
        /// <returns>与该目标关联的 <see cref="OverlayAdorner"/> 实例。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="InvalidOperationException">无法找到或创建可用的 <see cref="AdornerLayer"/>。</exception>
        /// <remarks>
        /// <para>
        /// 如果目标元素已经存在对应的 <see cref="OverlayAdorner"/>，则直接返回现有实例，不会重复创建。
        /// </para>
        /// <para>
        /// 当目标视觉树中不存在 <see cref="AdornerLayer"/> 时，该方法会尝试将宿主窗口的
        /// <see cref="Window.Content"/> 替换为 <see cref="AdornerDecorator"/> 包裹形式，
        /// 以便为目标元素提供 AdornerLayer。
        /// <b>注意：</b>此操作会改变窗口内容的视觉树结构，如果 XAML 中有针对窗口内容根元素的
        /// 绑定或样式引用，可能受到影响。建议在 XAML 中直接为窗口根内容添加
        /// <see cref="AdornerDecorator"/>，以避免此自动包裹行为。
        /// </para>
        /// </remarks>
        public static OverlayAdorner Attach(FrameworkElement target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            AdornerLayer layer = EnsureAdornerLayer(target);
            if (layer == null)
                throw new InvalidOperationException("无法找到或创建 AdornerLayer。");

            Adorner[] adorners = layer.GetAdorners(target);
            if (adorners != null)
            {
                for (int i = 0; i < adorners.Length; i++)
                {
                    OverlayAdorner existing = adorners[i] as OverlayAdorner;
                    if (existing != null)
                        return existing;
                }
            }

            var overlay = new OverlayAdorner(target);
            layer.Add(overlay);
            return overlay;
        }

        /// <summary>
        /// 添加一个 Overlay 面板。
        /// </summary>
        /// <param name="layout">布局配置；为 <see langword="null"/> 时使用默认布局。</param>
        /// <param name="style">样式配置；为 <see langword="null"/> 时使用默认样式。</param>
        /// <returns>新创建并附着到当前 Adorner 的面板实例。</returns>
        /// <exception cref="ObjectDisposedException">当前 Adorner 已分离，不能继续使用。</exception>
        public OverlayPanel AddPanel(OverlayLayout layout = null, OverlayStyle style = null)
        {
            if (_isDetached)
                throw new ObjectDisposedException(nameof(OverlayAdorner));

            var panel = new OverlayPanel(Target, this, layout ?? new OverlayLayout(), style ?? new OverlayStyle());
            _panels.Add(panel);
            _canvas.Children.Add(panel.RootElement);
            panel.AttachVisual();
            InvalidateArrange();
            return panel;
        }

        /// <summary>
        /// 从目标元素上解除附着，并释放当前 Adorner 管理的面板可视树。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 该方法可重复调用；重复调用不会抛异常。
        /// </para>
        /// <para>
        /// 分离后，当前实例以及其创建的 <see cref="OverlayPanel"/> 不应继续使用；
        /// 此后调用 <see cref="AddPanel"/> 或面板上的添加方法会抛出 <see cref="ObjectDisposedException"/>。
        /// </para>
        /// </remarks>
        public void Detach()
        {
            if (_isDetached)
                return;

            _isDetached = true;

            for (int i = 0; i < _panels.Count; i++)
            {
                _panels[i].DetachVisual();
            }

            _canvas.Children.Clear();
            _panels.Clear();

            AdornerLayer layer = Parent as AdornerLayer ?? AdornerLayer.GetAdornerLayer(AdornedElement);
            if (layer != null)
            {
                layer.Remove(this);
            }
        }

        /// <summary>
        /// 获取当前 Adorner 的可视子元素数量。
        /// </summary>
        protected override int VisualChildrenCount
        {
            get { return _visuals.Count; }
        }

        /// <summary>
        /// 按索引获取可视子元素。
        /// </summary>
        /// <param name="index">子元素索引。</param>
        /// <returns>对应的可视对象。</returns>
        protected override Visual GetVisualChild(int index)
        {
            return _visuals[index];
        }

        /// <summary>
        /// 布置内部画布及所有 Overlay 面板。
        /// </summary>
        /// <param name="finalSize">最终布局大小。</param>
        /// <returns>最终大小。</returns>
        protected override Size ArrangeOverride(Size finalSize)
        {
            _canvas.Arrange(new Rect(finalSize));

            for (int i = 0; i < _panels.Count; i++)
            {
                _panels[i].UpdatePlacement();
            }

            return finalSize;
        }

        private static AdornerLayer EnsureAdornerLayer(FrameworkElement target)
        {
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(target);
            if (layer != null)
                return layer;

            Window window = Window.GetWindow(target);
            if (window == null)
                return null;

            // 若视觉树中已有 AdornerDecorator 祖先，无需再包裹
            if (FindAncestorAdornerDecorator(target) != null)
                return AdornerLayer.GetAdornerLayer(target);

            FrameworkElement content = window.Content as FrameworkElement;
            if (content == null)
                return null;

            // 自动将窗口内容包裹在 AdornerDecorator 中，为目标元素提供 AdornerLayer。
            // 注意：此操作会改变 window.Content 的视觉树结构，详见 Attach 方法注释。
            if (content is AdornerDecorator)
                return AdornerLayer.GetAdornerLayer(target);

            var wrapper = new AdornerDecorator { Child = content };
            window.Content = wrapper;
            return AdornerLayer.GetAdornerLayer(target);
        }

        private static AdornerDecorator FindAncestorAdornerDecorator(DependencyObject current)
        {
            DependencyObject parent = current;
            while (parent != null)
            {
                AdornerDecorator decorator = parent as AdornerDecorator;
                if (decorator != null)
                    return decorator;

                parent = VisualTreeHelper.GetParent(parent);
            }

            return null;
        }
    }
}