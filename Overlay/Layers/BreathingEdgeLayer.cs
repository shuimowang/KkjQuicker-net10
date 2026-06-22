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
    /// 屏幕四周呼吸发光边框图层。
    /// <para>挂载后自动开始循环呼吸动画，直到从引擎中移除。</para>
    /// <para>设计用于配合 <see cref="OverlayInputPolicy.None"/>（纯展示，鼠标穿透）。</para>
    /// <para>所有公开方法必须在 UI 线程上调用。</para>
    /// </summary>
    public class BreathingEdgeLayer : IOverlayLayer
    {
        private readonly Grid _view;
        private readonly double _cycleSeconds;
        private bool _animationStarted;
        private Storyboard? _storyboard;

        /// <summary>
        /// 获取该图层的建议优先级。
        /// <para>调用 <see cref="OverlayEngine.Push"/> 时可直接传入：
        /// <c>engine.Push(layer, layer.Priority)</c>。</para>
        /// </summary>
        public int Priority { get; }

        /// <inheritdoc/>
        public UIElement View => _view;

        /// <summary>
        /// 初始化屏幕四周呼吸发光边框图层。
        /// </summary>
        /// <param name="glowColor">发光颜色（建议高饱和度颜色，如红、橙、青）。</param>
        /// <param name="priority">建议优先级，传入 <see cref="OverlayEngine.Push"/> 时使用。默认 100。</param>
        /// <param name="thickness">边框向屏幕内侧渐变的厚度（DIP）。默认 80。</param>
        /// <param name="cycleSeconds">单次呼吸周期时长（秒），含淡入淡出各一次。默认 1.2。</param>
        public BreathingEdgeLayer(
            Color glowColor,
            int priority = 100,
            double thickness = 80,
            double cycleSeconds = 1.2)
        {
            Priority = priority;
            _cycleSeconds = Math.Max(0.1, cycleSeconds);

            _view = new Grid
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Opacity = 0.0
            };

            _view.Children.Add(CreateEdgeRect(glowColor, thickness, Dock.Top));
            _view.Children.Add(CreateEdgeRect(glowColor, thickness, Dock.Bottom));
            _view.Children.Add(CreateEdgeRect(glowColor, thickness, Dock.Left));
            _view.Children.Add(CreateEdgeRect(glowColor, thickness, Dock.Right));
        }

        /// <inheritdoc/>
        public void OnAttach(OverlayContext context)
        {
            if (_animationStarted)
                return;

            _animationStarted = true;

            if (_view.IsLoaded)
            {
                StartBreathingAnimation();
            }
            else
            {
                RoutedEventHandler? handler = null;
                handler = (s, e) =>
                {
                    _view.Loaded -= handler;
                    StartBreathingAnimation();
                };
                _view.Loaded += handler;
            }
        }

        /// <inheritdoc/>
        public void OnDetach()
        {
            if (_storyboard != null)
            {
                _storyboard.Stop();
                _storyboard = null;
            }

            _animationStarted = false;
        }

        private void StartBreathingAnimation()
        {
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.1,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(_cycleSeconds)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(opacityAnimation, _view);
            Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(UIElement.OpacityProperty));

            _storyboard = new Storyboard();
            _storyboard.Children.Add(opacityAnimation);
            _storyboard.Begin();
        }

        private static Rectangle CreateEdgeRect(Color color, double thickness, Dock dock)
        {
            var brush = new LinearGradientBrush();

            switch (dock)
            {
                case Dock.Top: brush.StartPoint = new Point(0, 0); brush.EndPoint = new Point(0, 1); break;
                case Dock.Bottom: brush.StartPoint = new Point(0, 1); brush.EndPoint = new Point(0, 0); break;
                case Dock.Left: brush.StartPoint = new Point(0, 0); brush.EndPoint = new Point(1, 0); break;
                default: brush.StartPoint = new Point(1, 0); brush.EndPoint = new Point(0, 0); break;
            }

            brush.GradientStops.Add(new GradientStop(color, 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1.0));
            brush.Freeze();

            var rect = new Rectangle { Fill = brush, IsHitTestVisible = false };

            if (dock == Dock.Top || dock == Dock.Bottom)
            {
                rect.Height = thickness;
                rect.VerticalAlignment = dock == Dock.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            }
            else
            {
                rect.Width = thickness;
                rect.HorizontalAlignment = dock == Dock.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }

            return rect;
        }
    }
}