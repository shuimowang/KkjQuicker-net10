using System;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using ToastNotifications;
using ToastNotifications.Core;
using ToastNotifications.Lifetime;
using ToastNotifications.Messages;
using ToastNotifications.Position;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 应用级通知助手。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>单例 Notifier，首次显示通知时自动初始化，无需手动调用初始化方法。</description></item>
    /// <item><description>调用方无需关心线程与 Dispatcher，内部已确保在 UI 线程操作。</description></item>
    /// <item><description>通知固定显示在主屏幕底部居中，单条显示 3 秒，最多同时显示 5 条。</description></item>
    /// <item><description>点击通知时若提供了 <c>callback</c>，执行回调后关闭通知；未提供则仅关闭。</description></item>
    /// </list>
    /// </remarks>
    public static class NotifierHelper
    {
        private static Notifier? _notifier;
        private static Dispatcher? _dispatcher;
        private static readonly object _lock = new object();

        /// <summary>
        /// 当前通知系统是否已初始化。
        /// </summary>
        public static bool IsInitialized => _notifier != null;

        /// <summary>
        /// 显示信息通知。
        /// </summary>
        /// <param name="message">通知内容。</param>
        /// <param name="callback">点击通知时执行的回调；为 <see langword="null"/> 时点击仅关闭通知。</param>
        public static void ShowInformation(string message, Action? callback = null)
        {
            ShowInternal(message, callback, (m, o) => _notifier!.ShowInformation(m, o));
        }

        /// <summary>
        /// 显示成功通知。
        /// </summary>
        /// <param name="message">通知内容。</param>
        /// <param name="callback">点击通知时执行的回调；为 <see langword="null"/> 时点击仅关闭通知。</param>
        public static void ShowSuccess(string message, Action? callback = null)
        {
            ShowInternal(message, callback, (m, o) => _notifier!.ShowSuccess(m, o));
        }

        /// <summary>
        /// 显示警告通知。
        /// </summary>
        /// <param name="message">通知内容。</param>
        /// <param name="callback">点击通知时执行的回调；为 <see langword="null"/> 时点击仅关闭通知。</param>
        public static void ShowWarning(string message, Action? callback = null)
        {
            ShowInternal(message, callback, (m, o) => _notifier!.ShowWarning(m, o));
        }

        /// <summary>
        /// 显示错误通知。
        /// </summary>
        /// <param name="message">通知内容。</param>
        /// <param name="callback">点击通知时执行的回调；为 <see langword="null"/> 时点击仅关闭通知。</param>
        public static void ShowError(string message, Action? callback = null)
        {
            ShowInternal(message, callback, (m, o) => _notifier!.ShowError(m, o));
        }

        /// <summary>
        /// 释放通知系统资源。
        /// </summary>
        /// <remarks>
        /// 这是完整销毁通知系统，而非仅关闭当前通知。
        /// 下次调用 Show* 方法时会自动重新初始化。
        /// 建议在应用退出时调用，避免资源泄漏。
        /// </remarks>
        public static void Dispose()
        {
            lock (_lock)
            {
                if (_notifier == null)
                    return;

                try
                {
                    _notifier.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"通知系统释放失败: {ex.Message}");
                }
                finally
                {
                    _notifier = null;
                    _dispatcher = null;
                }
            }
        }

        private static void ShowInternal(
            string message,
            Action? callback,
            Action<string, MessageOptions> showAction)
        {
            EnsureInitialized();

            var options = new MessageOptions
            {
                FontSize = 15,
                UnfreezeOnMouseLeave = true,
                NotificationClickAction = n =>
                {
                    try
                    {
                        callback?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"通知回调执行失败: {ex.Message}");
                    }
                    finally
                    {
                        n.Close();
                    }
                }
            };

            try
            {
                showAction(message, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"通知显示失败: {ex.Message}");
            }
        }

        private static void EnsureInitialized()
        {
            if (_notifier != null)
                return;

            lock (_lock)
            {
                if (_notifier != null)
                    return;

                _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

                _dispatcher.Invoke(() =>
                {
                    TryInjectDefaultStyle();

                    _notifier = new Notifier(cfg =>
                    {
                        cfg.PositionProvider = new PrimaryScreenPositionProvider(
                            corner: Corner.BottomCenter,
                            offsetX: 0,
                            offsetY: 30);

                        cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                            notificationLifetime: TimeSpan.FromSeconds(3),
                            maximumNotificationCount: MaximumNotificationCount.FromCount(5));

                        cfg.Dispatcher = _dispatcher;
                    });
                });
            }
        }

        /// <summary>
        /// 向应用资源注入 ToastNotifications 默认样式。
        /// </summary>
        private static void TryInjectDefaultStyle()
        {
            if (Application.Current == null)
                return;

            try
            {
                var uri = new Uri(
                    "pack://application:,,,/ToastNotifications.Messages;component/Themes/Default.xaml");

                bool alreadyLoaded = Application.Current.Resources.MergedDictionaries
                    .Any(d => d.Source != null && d.Source.AbsoluteUri == uri.AbsoluteUri);

                if (!alreadyLoaded)
                {
                    Application.Current.Resources.MergedDictionaries.Add(
                        new ResourceDictionary { Source = uri });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ToastNotifications 样式注入失败，通知可能无默认样式: {ex.Message}");
            }
        }
    }
}
