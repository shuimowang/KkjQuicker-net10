using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供 WPF 常用动画的轻量级创建与执行方法。
    /// </summary>
    /// <remarks>
    /// <para>设计目标：</para>
    /// <list type="bullet">
    /// <item><description>统一封装常见 <see cref="DoubleAnimation"/>、<see cref="ColorAnimation"/> 与 <see cref="Storyboard"/> 创建逻辑。</description></item>
    /// <item><description>提供淡入、淡出及常用位移、缩放原子动画，保持轻量、稳定、易维护。</description></item>
    /// </list>
    /// <para>使用说明：</para>
    /// <list type="bullet">
    /// <item><description>本类仅负责创建和启动动画，不维护动画状态。</description></item>
    /// <item><description>所有方法均应在 UI 线程调用。</description></item>
    /// <item><description>
    /// <b>FillBehavior 策略</b>：<see cref="FadeIn"/> 与 <see cref="FadeOut"/> 使用默认 <see cref="FillBehavior.HoldEnd"/>，
    /// 动画结束后属性值由 WPF 持续保持。<see cref="FadeOutAndHide"/> 与 <see cref="FadeOutAndRemove"/>
    /// 使用 <see cref="FillBehavior.Stop"/>，在回调中手动固化终态，避免属性被锁死影响后续操作。
    /// </description></item>
    /// <item><description>
    /// <b>Visibility 策略</b>：<see cref="FadeIn"/> 在动画开始前设为 <see cref="Visibility.Visible"/>；
    /// <see cref="FadeOut"/> 不修改 Visibility；
    /// <see cref="FadeOutAndHide"/> 在动画结束后设为 <see cref="Visibility.Collapsed"/>；
    /// <see cref="FadeOutAndRemove"/> 在动画结束后隐藏并尝试从父 <see cref="Panel"/> 中移除。
    /// </description></item>
    /// <item><description>位移与缩放动画基于 <see cref="UIElement.RenderTransform"/>，若目标元素缺少对应变换，本类会自动补齐。</description></item>
    /// <item><description>缩放动画不会自动设置 <see cref="UIElement.RenderTransformOrigin"/>，如需以中心为原点缩放，调用方应自行设置 <c>RenderTransformOrigin = new Point(0.5, 0.5)</c>。</description></item>
    /// </list>
    /// </remarks>
    public static class AnimationHelper
    {
        #region 公共属性路径常量

        /// <summary><see cref="Canvas.LeftProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath CanvasLeftPropertyPath
            = new PropertyPath(Canvas.LeftProperty);

        /// <summary><see cref="Canvas.TopProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath CanvasTopPropertyPath
            = new PropertyPath(Canvas.TopProperty);

        /// <summary><see cref="GradientStop.ColorProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath GradientStopColorPropertyPath
            = new PropertyPath(GradientStop.ColorProperty);

        /// <summary><see cref="FrameworkElement.HeightProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath HeightPropertyPath
            = new PropertyPath(FrameworkElement.HeightProperty);

        /// <summary><see cref="FrameworkElement.WidthProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath WidthPropertyPath
            = new PropertyPath(FrameworkElement.WidthProperty);

        /// <summary><see cref="UIElement.OpacityProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath OpacityPropertyPath
            = new PropertyPath(UIElement.OpacityProperty);

        /// <summary><see cref="SolidColorBrush.ColorProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath SolidColorBrushColorPropertyPath
            = new PropertyPath(SolidColorBrush.ColorProperty);

        /// <summary><see cref="ScaleTransform.ScaleXProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath ScaleXPropertyPath
            = new PropertyPath(ScaleTransform.ScaleXProperty);

        /// <summary><see cref="ScaleTransform.ScaleYProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath ScaleYPropertyPath
            = new PropertyPath(ScaleTransform.ScaleYProperty);

        /// <summary><see cref="TranslateTransform.XProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath TranslateTransformXPropertyPath
            = new PropertyPath(TranslateTransform.XProperty);

        /// <summary><see cref="TranslateTransform.YProperty"/> 的属性路径。</summary>
        public static readonly PropertyPath TranslateTransformYPropertyPath
            = new PropertyPath(TranslateTransform.YProperty);

        #endregion

        #region 创建动画

        /// <summary>
        /// 创建颜色动画。
        /// </summary>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始颜色；传入 <c>null</c> 表示由 WPF 使用当前值作为起点。</param>
        /// <param name="to">结束颜色；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <returns>创建好的 <see cref="ColorAnimation"/>。</returns>
        public static ColorAnimation CreateColorAnimation(int durationInMilliseconds, Color? from, Color? to)
        {
            return new ColorAnimation
            {
                From = from,
                To = to,
                Duration = CreateDuration(durationInMilliseconds)
            };
        }

        /// <summary>
        /// 创建绑定到指定目标对象和属性路径的颜色动画。
        /// </summary>
        /// <param name="element">动画目标对象。</param>
        /// <param name="path">目标属性路径。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始颜色；传入 <c>null</c> 表示由 WPF 使用当前值作为起点。</param>
        /// <param name="to">结束颜色；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <returns>创建好的 <see cref="ColorAnimation"/>。</returns>
        public static ColorAnimation CreateColorAnimation(
            DependencyObject element, PropertyPath path,
            int durationInMilliseconds, Color? from, Color? to)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(path);

            ColorAnimation animation = CreateColorAnimation(durationInMilliseconds, from, to);
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, path);
            return animation;
        }

        /// <summary>
        /// 创建数值动画。
        /// </summary>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始值；传入 <c>null</c> 表示由 WPF 使用当前值作为起点。</param>
        /// <param name="to">结束值；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <param name="easingFunction">缓动函数；传入 <c>null</c> 表示使用线性插值。</param>
        /// <returns>创建好的 <see cref="DoubleAnimation"/>。</returns>
        public static DoubleAnimation CreateDoubleAnimation(
            int durationInMilliseconds, double? from, double? to,
            IEasingFunction? easingFunction = null)
        {
            return new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = CreateDuration(durationInMilliseconds),
                EasingFunction = easingFunction
            };
        }

        /// <summary>
        /// 创建绑定到指定目标对象和属性路径的数值动画。
        /// </summary>
        /// <param name="element">动画目标对象。</param>
        /// <param name="path">目标属性路径。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始值；传入 <c>null</c> 表示由 WPF 使用当前值作为起点。</param>
        /// <param name="to">结束值；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <param name="easingFunction">缓动函数；传入 <c>null</c> 表示使用线性插值。</param>
        /// <returns>创建好的 <see cref="DoubleAnimation"/>。</returns>
        public static DoubleAnimation CreateDoubleAnimation(
            DependencyObject element, PropertyPath path,
            int durationInMilliseconds, double? from, double? to,
            IEasingFunction? easingFunction = null)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(path);

            DoubleAnimation animation = CreateDoubleAnimation(durationInMilliseconds, from, to, easingFunction);
            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, path);
            return animation;
        }

        /// <summary>
        /// 创建绑定到指定目标对象的透明度动画。
        /// </summary>
        /// <param name="element">动画目标对象。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始透明度；传入 <c>null</c> 表示由 WPF 使用当前值作为起点。</param>
        /// <param name="to">结束透明度；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <returns>创建好的透明度动画。</returns>
        public static DoubleAnimation CreateOpacityAnimation(
            DependencyObject element, int durationInMilliseconds, double? from, double? to)
        {
            return CreateDoubleAnimation(element, OpacityPropertyPath, durationInMilliseconds, from, to);
        }

        /// <summary>
        /// 创建 X 方向位移动画。
        /// </summary>
        /// <param name="element">动画目标元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始位移；传入 <c>null</c> 表示使用当前值。</param>
        /// <param name="to">结束位移；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <param name="easingFunction">缓动函数；传入 <c>null</c> 表示使用线性插值。</param>
        /// <returns>创建好的 X 方向位移动画。</returns>
        public static DoubleAnimation CreateTranslateXAnimation(
            UIElement element, int durationInMilliseconds, double? from, double? to,
            IEasingFunction? easingFunction = null)
        {
            TranslateTransform transform = GetOrCreateTranslateTransform(element);
            return CreateDoubleAnimation(transform, TranslateTransformXPropertyPath, durationInMilliseconds, from, to, easingFunction);
        }

        /// <summary>
        /// 创建 Y 方向位移动画。
        /// </summary>
        /// <param name="element">动画目标元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始位移；传入 <c>null</c> 表示使用当前值。</param>
        /// <param name="to">结束位移；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <param name="easingFunction">缓动函数；传入 <c>null</c> 表示使用线性插值。</param>
        /// <returns>创建好的 Y 方向位移动画。</returns>
        public static DoubleAnimation CreateTranslateYAnimation(
            UIElement element, int durationInMilliseconds, double? from, double? to,
            IEasingFunction? easingFunction = null)
        {
            TranslateTransform transform = GetOrCreateTranslateTransform(element);
            return CreateDoubleAnimation(transform, TranslateTransformYPropertyPath, durationInMilliseconds, from, to, easingFunction);
        }

        /// <summary>
        /// 创建 X 方向缩放动画。
        /// </summary>
        /// <param name="element">动画目标元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始缩放值；传入 <c>null</c> 表示使用当前值。</param>
        /// <param name="to">结束缩放值；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <param name="easingFunction">缓动函数；传入 <c>null</c> 表示使用线性插值。</param>
        /// <returns>创建好的 X 方向缩放动画。</returns>
        public static DoubleAnimation CreateScaleXAnimation(
            UIElement element, int durationInMilliseconds, double? from, double? to,
            IEasingFunction? easingFunction = null)
        {
            ScaleTransform transform = GetOrCreateScaleTransform(element);
            return CreateDoubleAnimation(transform, ScaleXPropertyPath, durationInMilliseconds, from, to, easingFunction);
        }

        /// <summary>
        /// 创建 Y 方向缩放动画。
        /// </summary>
        /// <param name="element">动画目标元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <param name="from">起始缩放值；传入 <c>null</c> 表示使用当前值。</param>
        /// <param name="to">结束缩放值；传入 <c>null</c> 表示未显式指定终点。</param>
        /// <param name="easingFunction">缓动函数；传入 <c>null</c> 表示使用线性插值。</param>
        /// <returns>创建好的 Y 方向缩放动画。</returns>
        public static DoubleAnimation CreateScaleYAnimation(
            UIElement element, int durationInMilliseconds, double? from, double? to,
            IEasingFunction? easingFunction = null)
        {
            ScaleTransform transform = GetOrCreateScaleTransform(element);
            return CreateDoubleAnimation(transform, ScaleYPropertyPath, durationInMilliseconds, from, to, easingFunction);
        }

        #endregion

        #region 故事板

        /// <summary>
        /// 创建包含若干动画时间线的故事板。
        /// </summary>
        /// <param name="timelines">动画时间线列表；<c>null</c> 项将被跳过。</param>
        /// <returns>创建好的 <see cref="Storyboard"/>。</returns>
        public static Storyboard CreateStoryboard(params AnimationTimeline[] timelines)
        {
            ArgumentNullException.ThrowIfNull(timelines);

            var storyboard = new Storyboard();
            foreach (AnimationTimeline t in timelines)
            {
                if (t != null)
                    storyboard.Children.Add(t);
            }
            return storyboard;
        }

        #endregion

        #region 淡入 / 淡出扩展方法

        /// <summary>
        /// 对指定元素执行淡入动画，并立即开始播放。
        /// </summary>
        /// <param name="element">要淡入的元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <returns>已开始播放的 <see cref="Storyboard"/>。</returns>
        /// <remarks>
        /// 动画开始前将元素设为 <see cref="Visibility.Visible"/>。
        /// 使用默认 <see cref="FillBehavior.HoldEnd"/>，动画结束后 Opacity 持续保持为 1。
        /// </remarks>
        public static Storyboard FadeIn(this UIElement element, int durationInMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(element);

            Storyboard storyboard = CreateStoryboard(
                CreateOpacityAnimation(element, durationInMilliseconds, null, 1d));
            element.Visibility = Visibility.Visible;
            storyboard.Begin();
            return storyboard;
        }

        /// <summary>
        /// 对指定元素执行淡出动画，并立即开始播放。
        /// </summary>
        /// <param name="element">要淡出的元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <returns>已开始播放的 <see cref="Storyboard"/>。</returns>
        /// <remarks>
        /// 不修改 <see cref="UIElement.Visibility"/>。
        /// 使用默认 <see cref="FillBehavior.HoldEnd"/>，动画结束后 Opacity 持续保持为 0。
        /// 若后续需要手动修改 Opacity，应先启动新动画或调用 <c>BeginAnimation(OpacityProperty, null)</c> 解除锁定。
        /// </remarks>
        public static Storyboard FadeOut(this UIElement element, int durationInMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(element);

            Storyboard storyboard = CreateStoryboard(
                CreateOpacityAnimation(element, durationInMilliseconds, null, 0d));
            storyboard.Begin();
            return storyboard;
        }

        /// <summary>
        /// 对指定元素执行淡出动画，动画结束后将元素设为 <see cref="Visibility.Collapsed"/>。
        /// </summary>
        /// <param name="element">要淡出并隐藏的元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <returns>已开始播放的 <see cref="Storyboard"/>。</returns>
        /// <remarks>
        /// 使用 <see cref="FillBehavior.Stop"/>，在回调中手动固化 <c>Opacity = 0</c>，
        /// 避免属性被锁死，保证后续 <see cref="FadeIn"/> 或手动赋值可正常生效。
        /// </remarks>
        public static Storyboard FadeOutAndHide(this UIElement element, int durationInMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(element);

            DoubleAnimation animation = CreateOpacityAnimation(element, durationInMilliseconds, null, 0d);
            animation.FillBehavior = FillBehavior.Stop;

            Storyboard storyboard = CreateStoryboard(animation);

            EventHandler? handler = null;
            handler = (s, e) =>
            {
                storyboard.Completed -= handler;
                element.Opacity = 0d;
                element.Visibility = Visibility.Collapsed;
            };
            storyboard.Completed += handler;

            storyboard.Begin();
            return storyboard;
        }

        /// <summary>
        /// 对指定元素执行淡出动画，动画结束后先隐藏，再尝试从父 <see cref="Panel"/> 中移除。
        /// </summary>
        /// <param name="element">要淡出并移除的元素。</param>
        /// <param name="durationInMilliseconds">动画时长（毫秒），必须大于或等于 0。</param>
        /// <returns>已开始播放的 <see cref="Storyboard"/>。</returns>
        /// <remarks>
        /// 若父容器不是 <see cref="Panel"/>，则仅隐藏元素，不会抛出异常。
        /// 父容器在动画开始时捕获，动画期间的父容器变化不会影响移除操作。
        /// </remarks>
        public static Storyboard FadeOutAndRemove(this UIElement element, int durationInMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(element);

            // 提前捕获父容器，避免动画期间父容器变化导致移除失败或异常。
            Panel? parent = (element as FrameworkElement)?.Parent as Panel;

            DoubleAnimation animation = CreateOpacityAnimation(element, durationInMilliseconds, null, 0d);
            animation.FillBehavior = FillBehavior.Stop;

            Storyboard storyboard = CreateStoryboard(animation);

            EventHandler? handler = null;
            handler = (s, e) =>
            {
                storyboard.Completed -= handler;
                element.Visibility = Visibility.Collapsed;
                if (parent != null && parent.Children.Contains(element))
                    parent.Children.Remove(element);
            };
            storyboard.Completed += handler;

            storyboard.Begin();
            return storyboard;
        }

        #endregion

        #region 时长与关键帧时间

        /// <summary>
        /// 根据毫秒数创建 <see cref="Duration"/>。
        /// </summary>
        /// <param name="milliseconds">时长（毫秒），必须大于或等于 0。</param>
        /// <returns>对应的 <see cref="Duration"/>。</returns>
        public static Duration CreateDuration(int milliseconds)
        {
            if (milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "动画时长不能小于 0。");

            return new Duration(TimeSpan.FromMilliseconds(milliseconds));
        }

        /// <summary>
        /// 根据毫秒数创建 <see cref="KeyTime"/>。
        /// </summary>
        /// <param name="milliseconds">时长（毫秒），必须大于或等于 0。</param>
        /// <returns>对应的 <see cref="KeyTime"/>。</returns>
        public static KeyTime CreateKeyTime(int milliseconds)
        {
            if (milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds), "动画时长不能小于 0。");

            return KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds));
        }

        #endregion

        #region 变换辅助

        /// <summary>
        /// 获取目标元素的 <see cref="TranslateTransform"/>；若不存在则自动创建并附加。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <returns>可直接用于位移动画的 <see cref="TranslateTransform"/>。</returns>
        /// <remarks>
        /// 若当前 <see cref="UIElement.RenderTransform"/> 不是 <see cref="TranslateTransform"/>，
        /// 本方法会创建 <see cref="TransformGroup"/>，保留原有变换，并将新的 <see cref="TranslateTransform"/> 追加到组中。
        /// </remarks>
        public static TranslateTransform GetOrCreateTranslateTransform(UIElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            if (element.RenderTransform is TranslateTransform existing)
                return existing;

            if (element.RenderTransform is TransformGroup group)
            {
                foreach (Transform child in group.Children)
                {
                    if (child is TranslateTransform t)
                        return t;
                }
                var translate = new TranslateTransform();
                group.Children.Add(translate);
                return translate;
            }

            if (element.RenderTransform == null || element.RenderTransform == Transform.Identity)
            {
                var translate = new TranslateTransform();
                element.RenderTransform = translate;
                return translate;
            }

            var newGroup = new TransformGroup();
            newGroup.Children.Add(element.RenderTransform);
            var newTranslate = new TranslateTransform();
            newGroup.Children.Add(newTranslate);
            element.RenderTransform = newGroup;
            return newTranslate;
        }

        /// <summary>
        /// 获取目标元素的 <see cref="ScaleTransform"/>；若不存在则自动创建并附加。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <returns>可直接用于缩放动画的 <see cref="ScaleTransform"/>。</returns>
        /// <remarks>
        /// 若当前 <see cref="UIElement.RenderTransform"/> 不是 <see cref="ScaleTransform"/>，
        /// 本方法会创建 <see cref="TransformGroup"/>，保留原有变换，并将新的 <see cref="ScaleTransform"/> 追加到组中。
        /// <para>
        /// 缩放默认以元素左上角为原点。若需以中心缩放，调用方应自行设置：
        /// <c>element.RenderTransformOrigin = new Point(0.5, 0.5)</c>。
        /// </para>
        /// </remarks>
        public static ScaleTransform GetOrCreateScaleTransform(UIElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            if (element.RenderTransform is ScaleTransform existing)
                return existing;

            if (element.RenderTransform is TransformGroup group)
            {
                foreach (Transform child in group.Children)
                {
                    if (child is ScaleTransform s)
                        return s;
                }
                var scale = new ScaleTransform(1d, 1d);
                group.Children.Add(scale);
                return scale;
            }

            if (element.RenderTransform == null || element.RenderTransform == Transform.Identity)
            {
                var scale = new ScaleTransform(1d, 1d);
                element.RenderTransform = scale;
                return scale;
            }

            var newGroup = new TransformGroup();
            newGroup.Children.Add(element.RenderTransform);
            var newScale = new ScaleTransform(1d, 1d);
            newGroup.Children.Add(newScale);
            element.RenderTransform = newGroup;
            return newScale;
        }

        #endregion
    }
}
