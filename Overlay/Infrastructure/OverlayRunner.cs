using KkjQuicker.Overlay.Engine;
using KkjQuicker.Overlay.Layers;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KkjQuicker.Overlay.Infrastructure
{
    public static class OverlayRunner
    {
        public static async Task<BitmapSource?> RunScreenshotLayer()
        {
            var overlay = new OverlayEngine(new OverlayOptions
            {
                Placement = OverlayPlacementMode.GlobalFullscreen,
                AutoFocus = true,
                ForceTopMostViaWin32 = true
            });

            try
            {
                var layer = new ScreenshotLayer();
                overlay.Push(layer);

                // 让 Overlay 窗口完成 SourceInitialized、首次布局与渲染后再执行截屏。
                await Task.Yield();

                var result = await layer.CaptureAsync();
                return result?.Cropped;
            }
            finally
            {
                overlay.Dispose();
            }
        }

        /// <summary>
        /// 在屏幕指定位置播放一次水波纹通知动画，播完后自动释放所有资源。
        /// </summary>
        /// <param name="screenCenterDip">
        /// 波纹中心的屏幕绝对坐标（DIP）。
        /// 传入 <see langword="null"/> 时默认使用主屏幕中心。
        /// </param>
        /// <param name="rippleColor">波纹颜色，默认天蓝色。</param>
        public static void NotifyWithRipple(
            Point? screenCenterDip = null,
            Color? rippleColor = null)
        {
            var center = screenCenterDip ?? new Point(
                SystemParameters.PrimaryScreenWidth / 2.0,
                SystemParameters.PrimaryScreenHeight / 2.0);

            var engine = new OverlayEngine(BuildFullscreenPassthroughOptions());
            try
            {
                var layer = new WaterRippleLayer(rippleColor ?? Colors.DeepSkyBlue);

                layer.AnimationCompleted += (s, e) => engine.Dispose();
                engine.Push(layer, layer.Priority);

                // 屏幕绝对坐标 → Overlay 相对坐标
                // GlobalFullscreen 模式下 Overlay 左上角与虚拟屏幕原点对齐
                var overlayPos = new Point(
                    center.X - SystemParameters.VirtualScreenLeft,
                    center.Y - SystemParameters.VirtualScreenTop);

                layer.TriggerRipple(overlayPos);
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        }

        /// <summary>
        /// 在屏幕四周显示呼吸发光边框提醒，返回句柄可手动关闭。
        /// </summary>
        /// <param name="color">发光颜色，默认橙红色。</param>
        /// <param name="thickness">边框向屏幕内侧渐变的厚度（DIP）。默认 80。</param>
        /// <param name="cycleSeconds">单次呼吸周期时长（秒）。默认 1.2。</param>
        /// <param name="priority">图层优先级。默认 100。</param>
        /// <param name="autoCloseSeconds">
        /// 自动关闭延迟（秒）。
        /// 传入 <see langword="null"/> 时不自动关闭，由调用方通过返回的句柄手动释放。
        /// </param>
        /// <returns>
        /// 用于手动关闭效果的释放句柄。
        /// 若已设置 <paramref name="autoCloseSeconds"/>，到期后引擎自动释放，
        /// 此后再调用 <see cref="IDisposable.Dispose"/> 是安全的 no-op。
        /// </returns>
        public static IDisposable StartBreathingAlert(
            Color? color = null,
            double thickness = 80,
            double cycleSeconds = 1.2,
            int priority = 100,
            double? autoCloseSeconds = null)
        {
            var engine = new OverlayEngine(BuildFullscreenPassthroughOptions());
            try
            {
                var layer = new BreathingEdgeLayer(
                    color ?? Colors.OrangeRed,
                    priority,
                    thickness,
                    cycleSeconds);

                engine.Push(layer, layer.Priority);

                if (autoCloseSeconds.HasValue && autoCloseSeconds.Value > 0)
                {
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(autoCloseSeconds.Value)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        engine.Dispose();
                    };
                    timer.Start();
                }

                return engine;
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        }

        private static OverlayOptions BuildFullscreenPassthroughOptions()
        {
            return new OverlayOptions
            {
                Placement = OverlayPlacementMode.GlobalFullscreen,
                InputPolicy = OverlayInputPolicy.None,
                TopMost = true,
                ForceTopMostViaWin32 = true,
                Background = Brushes.Transparent,
                AutoFocus = false
            };
        }

    }
}