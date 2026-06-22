using KkjQuicker.Overlay.Engine;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace KkjQuicker.Overlay.Layers
{
    /// <summary>
    /// 单次波纹通知图层。
    /// <para>在指定位置播放一次水波纹动画，所有圆环消失后自动触发
    /// <see cref="AnimationCompleted"/> 事件。</para>
    /// <para>设计用于配合 <see cref="OverlayInputPolicy.None"/>（纯展示，鼠标穿透）。</para>
    /// <para>典型用法：<c>engine.Push</c> → <c>TriggerRipple</c>
    /// → 在 <see cref="AnimationCompleted"/> 中 <c>engine.Dispose()</c>。</para>
    /// <para>所有公开方法必须在 UI 线程上调用。</para>
    /// </summary>
    public class WaterRippleLayer : OverlayInputLayerBase
    {
        private readonly Grid _view;
        private readonly Color _rippleColor;
        private readonly double _maxRadius;
        private readonly double _durationSeconds;
        private readonly int _ringCount;
        private int _pendingRings;

        /// <summary>
        /// 获取该图层的建议优先级。
        /// <para>调用 <see cref="OverlayEngine.Push"/> 时可直接传入：
        /// <c>engine.Push(layer, layer.Priority)</c>。</para>
        /// </summary>
        public int Priority { get; }

        /// <inheritdoc/>
        public override UIElement View => _view;

        /// <summary>
        /// 所有波纹圆环动画播完后在 UI 线程触发。
        /// <para>典型用法：在此事件中调用 <c>engine.Dispose()</c> 以自动释放资源。</para>
        /// </summary>
        public event EventHandler? AnimationCompleted;

        /// <summary>
        /// 初始化水波纹通知图层。
        /// </summary>
        /// <param name="rippleColor">波纹颜色（建议天蓝或青色）。</param>
        /// <param name="priority">建议优先级，传入 <see cref="OverlayEngine.Push"/> 时使用。默认 100。</param>
        /// <param name="maxRadius">波纹扩散最大半径（DIP）。默认 200。</param>
        /// <param name="durationSeconds">波纹扩散并消失的持续时间（秒）。默认 1.8。</param>
        /// <param name="ringCount">同心波纹圈数。默认 3。</param>
        public WaterRippleLayer(
            Color rippleColor,
            int priority = 100,
            double maxRadius = 200,
            double durationSeconds = 1.8,
            int ringCount = 3)
        {
            Priority = priority;
            _rippleColor = rippleColor;
            _maxRadius = Math.Max(1, maxRadius);
            _durationSeconds = Math.Max(0.1, durationSeconds);
            _ringCount = Math.Max(1, ringCount);

            _view = new Grid
            {
                Background = Brushes.Transparent,
                ClipToBounds = true
            };
        }

        /// <inheritdoc/>
        public override void OnDetach()
        {
            // 清空仍在播放的圆环；各 Storyboard 的 Completed 回调
            // 对空集合调用 Remove 是安全的 no-op。
            _view.Children.Clear();
        }

        /// <summary>
        /// 在指定坐标触发一次水波纹动画。
        /// <para>坐标相对于 Overlay 左上角（DIP）。</para>
        /// <para>所有圆环播完后自动触发 <see cref="AnimationCompleted"/>。</para>
        /// </summary>
        /// <param name="centerInOverlay">波纹中心坐标（DIP，相对于 Overlay 左上角）。</param>
        public void TriggerRipple(Point centerInOverlay)
        {
            CreateRipple(centerInOverlay);
        }

        private void CreateRipple(Point center)
        {
            _pendingRings += _ringCount;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = new Duration(TimeSpan.FromSeconds(_durationSeconds));

            for (int i = 0; i < _ringCount; i++)
            {
                var ring = new Ellipse
                {
                    Width = _maxRadius * 2,
                    Height = _maxRadius * 2,
                    Stroke = new SolidColorBrush(_rippleColor),
                    StrokeThickness = 3,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(center.X - _maxRadius, center.Y - _maxRadius, 0, 0),
                    IsHitTestVisible = false,
                    Opacity = 0,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new ScaleTransform(0.01, 0.01)
                };

                _view.Children.Add(ring);

                var beginTime = TimeSpan.FromMilliseconds(i * 150);
                var sb = new Storyboard();

                var scaleX = new DoubleAnimation { From = 0.01, To = 1, Duration = duration, BeginTime = beginTime, EasingFunction = ease };
                var scaleY = new DoubleAnimation { From = 0.01, To = 1, Duration = duration, BeginTime = beginTime, EasingFunction = ease };
                var opacity = new DoubleAnimation { From = 0.8, To = 0, Duration = duration, BeginTime = beginTime, EasingFunction = ease };

                Storyboard.SetTarget(scaleX, ring);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
                Storyboard.SetTarget(scaleY, ring);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
                Storyboard.SetTarget(opacity, ring);
                Storyboard.SetTargetProperty(opacity, new PropertyPath("UIElement.Opacity"));

                sb.Children.Add(scaleX);
                sb.Children.Add(scaleY);
                sb.Children.Add(opacity);

                sb.Completed += (s, e) =>
                {
                    _view.Children.Remove(ring);
                    _pendingRings--;
                    if (_pendingRings <= 0)
                        AnimationCompleted?.Invoke(this, EventArgs.Empty);
                };

                sb.Begin();
            }
        }
    }
}