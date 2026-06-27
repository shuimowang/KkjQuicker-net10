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

                // Let the overlay window finish SourceInitialized, first layout and render before capturing.
                await WaitForRenderAsync();

                var result = await layer.CaptureAsync();
                return result?.Cropped;
            }
            finally
            {
                overlay.Dispose();
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
        /// 若已设置 <paramref name="autoCloseSeconds"/>，到期后引擎自动释放；
        /// 此后再次调用 <see cref="IDisposable.Dispose"/> 是安全的 no-op。
        /// </returns>
        public static IDisposable StartBreathingAlert(
            Color? color = null,
            double thickness = 80,
            double cycleSeconds = 1.2,
            int priority = 100,
            double? autoCloseSeconds = null)
        {
            var engine = new OverlayEngine(BuildFullscreenPassthroughOptions());
            DispatcherTimer? timer = null;

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
                    timer = new DispatcherTimer
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

                return new TimedOverlayHandle(engine, timer);
            }
            catch
            {
                timer?.Stop();
                engine.Dispose();
                throw;
            }
        }

        private static Task WaitForRenderAsync()
        {
            return Application.Current?.Dispatcher.InvokeAsync(
                static () => { },
                DispatcherPriority.Render).Task ?? Task.CompletedTask;
        }

        private sealed class TimedOverlayHandle : IDisposable
        {
            private readonly OverlayEngine _engine;
            private DispatcherTimer? _timer;
            private bool _disposed;

            public TimedOverlayHandle(OverlayEngine engine, DispatcherTimer? timer)
            {
                _engine = engine ?? throw new ArgumentNullException(nameof(engine));
                _timer = timer;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _timer?.Stop();
                _timer = null;
                _engine.Dispose();
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
