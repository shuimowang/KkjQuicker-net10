using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace KkjQuicker.Utilities.Threading
{
    /// <summary>
    /// 提供适用于 WPF 场景的同步/异步防抖与节流控制。
    /// </summary>
    /// <remarks>
    /// <para>同步方法：</para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Debounce(Action)"/>：Trailing Edge。
    /// 仅执行最后一次调用，执行时机为最后一次调用后的 <see cref="Interval"/>。
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Throttle(Action)"/>：Leading Edge。
    /// 首次立即执行，冷却期间忽略后续调用；冷却从本次执行结束后开始计算。
    /// </description>
    /// </item>
    /// </list>
    ///
    /// <para>异步方法：</para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="DebounceAsync(Func{CancellationToken, Task})"/>：Trailing Edge。
    /// 新的调用会取消前一次尚未开始执行的挂起请求；若前一次已开始执行，则会发出取消请求，
    /// 是否真正中止执行取决于用户委托是否正确响应 <see cref="CancellationToken"/>。
    /// 仅最后一次调用有机会成为最终执行项。
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="ThrottleAsync(Func{CancellationToken, Task})"/>：Leading Edge。
    /// 首次立即执行，执行完成后进入冷却；冷却结束前忽略后续调用。
    /// </description>
    /// </item>
    /// </list>
    ///
    /// <para>Dispatcher 说明：</para>
    /// <list type="bullet">
    /// <item><description>默认使用当前 WPF 应用的主线程 <see cref="Dispatcher"/>。</description></item>
    /// <item><description>也可通过构造函数显式传入目标 <see cref="Dispatcher"/>。</description></item>
    /// <item><description>若既未传入 <see cref="Dispatcher"/>，又没有可用的 <see cref="Application.Current"/>，构造时会抛出异常。</description></item>
    /// </list>
    ///
    /// <para>异常说明：</para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Debounce(Action)"/> 在实际执行委托时会捕获异常并写入 <see cref="Debug"/>，
    /// 以避免在 <see cref="DispatcherTimer"/> 回调中直接中断 UI 消息循环。
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <see cref="Throttle(Action)"/>、<see cref="DebounceAsync(Func{CancellationToken, Task})"/>、
    /// <see cref="ThrottleAsync(Func{CancellationToken, Task})"/> 中由用户委托抛出的非取消异常会继续向外传播。
    /// </description>
    /// </item>
    /// </list>
    ///
    /// <para>冷却期行为说明：</para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <see cref="Throttle(Action)"/> 与 <see cref="ThrottleAsync(Func{CancellationToken, Task})"/>
    /// 无论用户委托是否抛出异常，执行后均会进入冷却期。
    /// 若需在异常后跳过冷却，请自行在委托内捕获并处理。
    /// </description>
    /// </item>
    /// </list>
    ///
    /// <para>取消说明：</para>
    /// <list type="bullet">
    /// <item><description><see cref="Cancel"/> 会取消挂起中的同步/异步防抖，以及节流冷却状态。</description></item>
    /// <item>
    /// <description>
    /// <see cref="Cancel"/> 在非 Dispatcher 线程调用时为异步生效（内部使用 <c>BeginInvoke</c>），
    /// 方法返回时取消操作可能尚未实际发生。
    /// </description>
    /// </item>
    /// <item><description><see cref="Cancel"/> 不会中断已经开始执行中的 <see cref="ThrottleAsync(Func{CancellationToken, Task})"/>。</description></item>
    /// <item><description><see cref="Dispose"/> 会进一步请求取消正在执行中的异步操作。</description></item>
    /// </list>
    /// </remarks>
    public sealed class ActionRateLimiter : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly Dispatcher? _dispatcher;
        private readonly DispatcherPriority _priority;

        private readonly DispatcherTimer _debounceTimer;
        private readonly DispatcherTimer _throttleTimer;

        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

        private TimeSpan _interval;
        private bool _disposed;

        // Sync
        private Action? _pendingSyncAction = null!;
        private bool _isSyncThrottling;

        // Async debounce
        private CancellationTokenSource? _debounceCts = null!;

        // Async throttle
        private bool _isAsyncThrottling;
        private CancellationTokenSource? _cooldownCts = null!;

        /// <summary>
        /// 初始化 <see cref="ActionRateLimiter"/>。
        /// </summary>
        /// <param name="interval">防抖/节流间隔，必须大于 <see cref="TimeSpan.Zero"/>。</param>
        /// <param name="dispatcher">
        /// 目标 <see cref="Dispatcher"/>。
        /// 为 <see langword="null"/> 时，默认使用 <see cref="Application.Current"/> 的 <see cref="Dispatcher"/>。
        /// </param>
        /// <param name="priority">在 Dispatcher 中调度同步逻辑与计时器回调时使用的优先级。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> 小于或等于零。</exception>
        /// <exception cref="InvalidOperationException">无法获取可用的 WPF <see cref="Dispatcher"/>。</exception>
        public ActionRateLimiter(
            TimeSpan interval,
            Dispatcher? dispatcher = null,
            DispatcherPriority priority = DispatcherPriority.Background)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval));

            _interval = interval;
            _priority = priority;

            _dispatcher = dispatcher
                ?? (Application.Current != null ? Application.Current.Dispatcher : null);

            if (_dispatcher == null)
                throw new InvalidOperationException("ActionRateLimiter 需要 WPF Dispatcher。请在 WPF 中使用或显式传入 Dispatcher。");

            _debounceTimer = new DispatcherTimer(_priority, _dispatcher)
            {
                Interval = _interval
            };
            _debounceTimer.Tick += OnDebounceTick;

            _throttleTimer = new DispatcherTimer(_priority, _dispatcher)
            {
                Interval = _interval
            };
            _throttleTimer.Tick += OnThrottleTick;
        }

        /// <summary>
        /// 获取或设置当前的防抖/节流间隔。
        /// </summary>
        /// <value>必须大于 <see cref="TimeSpan.Zero"/>。</value>
        /// <remarks>
        /// 修改该属性会同步更新内部计时器间隔。
        /// 已经开始的同步节流冷却或异步延迟是否立刻体现新值，取决于当前阶段。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">设置的值小于或等于零。</exception>
        public TimeSpan Interval
        {
            get
            {
                lock (_syncRoot)
                    return _interval;
            }
            set
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value));

                lock (_syncRoot)
                {
                    if (_disposed)
                        return;

                    if (_interval == value)
                        return;

                    _interval = value;
                }

                Dispatch(() =>
                {
                    lock (_syncRoot)
                    {
                        if (_disposed)
                            return;

                        _debounceTimer.Interval = value;
                        _throttleTimer.Interval = value;
                    }
                }, true);
            }
        }

        #region Sync

        /// <summary>
        /// 请求执行一个同步防抖操作。
        /// </summary>
        /// <param name="action">要执行的委托。</param>
        /// <remarks>
        /// 每次调用都会覆盖前一次挂起的同步操作。
        /// 仅最后一次调用对应的委托会在最后一次调用后的 <see cref="Interval"/> 执行。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public void Debounce(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            Dispatch(() =>
            {
                lock (_syncRoot)
                {
                    if (_disposed)
                        return;

                    _pendingSyncAction = action;
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }, false);
        }

        /// <summary>
        /// 尝试执行一个同步节流操作。
        /// </summary>
        /// <param name="action">要执行的委托。</param>
        /// <returns>
        /// <see langword="true"/> 表示本次调用被接受并已执行；
        /// <see langword="false"/> 表示当前处于冷却期，或对象已释放。
        /// </returns>
        /// <remarks>
        /// 首次调用立即执行；执行结束后进入冷却期。
        /// 冷却期间的后续调用会被直接忽略，不会排队。
        /// 无论委托是否抛出异常，执行后均会进入冷却期。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public bool Throttle(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (_dispatcher?.CheckAccess() == true)
                return ThrottleInternal(action);

            bool result = false;
            _dispatcher?.Invoke(_priority, new Action(() =>
            {
                result = ThrottleInternal(action);
            }));
            return result;
        }

        private bool ThrottleInternal(Action action)
        {
            lock (_syncRoot)
            {
                if (_disposed || _isSyncThrottling)
                    return false;

                _isSyncThrottling = true;
            }

            try
            {
                action();
                return true;
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (!_disposed)
                    {
                        _throttleTimer.Stop();
                        _throttleTimer.Start();
                    }
                }
            }
        }

        private void OnDebounceTick(object? sender, EventArgs e)
        {
            Action? toRun = null;

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _debounceTimer.Stop();
                toRun = _pendingSyncAction;
                _pendingSyncAction = null;
            }

            if (toRun == null)
                return;

            try
            {
                toRun();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private void OnThrottleTick(object? sender, EventArgs e)
        {
            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _throttleTimer.Stop();
                _isSyncThrottling = false;
            }
        }

        #endregion

        #region Async

        /// <summary>
        /// 请求执行一个异步防抖操作。
        /// </summary>
        /// <param name="action">要执行的异步委托，需正确响应取消令牌。</param>
        /// <returns>
        /// <see langword="true"/> 表示本次调用对应的请求最终被调度并开始执行；
        /// <see langword="false"/> 表示在等待期内被后续调用覆盖、被取消，或对象已释放。
        /// </returns>
        /// <remarks>
        /// 每次调用都会取消前一次尚未开始执行的挂起请求，并重置等待时间。
        /// 若前一次已开始执行，则会发出取消请求；是否真正中止执行取决于用户委托是否响应该取消令牌。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public async Task<bool> DebounceAsync(Func<CancellationToken, Task> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            CancellationTokenSource cts;
            CancellationToken localToken;
            TimeSpan delay;

            lock (_syncRoot)
            {
                if (_disposed)
                    return false;

                _debounceCts?.Cancel();
                _debounceCts?.Dispose();

                _debounceCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                cts = _debounceCts!;
                localToken = cts.Token; // 在 CTS 确定存活时捕获结构体，避免后续并发 Dispose 后访问 .Token getter
                delay = _interval;
            }

            try
            {
                await Task.Delay(delay, localToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    if (_disposed || !ReferenceEquals(_debounceCts, cts))
                        return false;
                }

                await action(localToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (localToken.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_debounceCts, cts))
                    {
                        _debounceCts!.Dispose();
                        _debounceCts = null;
                    }
                }
            }
        }

        /// <summary>
        /// 尝试执行一个异步节流操作。
        /// </summary>
        /// <param name="action">要执行的异步委托，需正确响应取消令牌。</param>
        /// <returns>
        /// <see langword="true"/> 表示本次调用被接受并已执行；
        /// <see langword="false"/> 表示当前处于冷却期、被取消，或对象已释放。
        /// </returns>
        /// <remarks>
        /// 首次调用立即执行；执行结束后进入冷却期。
        /// 冷却期间的后续调用会被直接忽略，不会排队。
        /// 无论委托是否抛出异常，执行后均会进入冷却期。
        /// <para>
        /// 注意：<see cref="Cancel"/> 只会取消冷却并恢复可调用状态，
        /// 不会中断已经开始执行中的当前异步委托。
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public async Task<bool> ThrottleAsync(Func<CancellationToken, Task> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            CancellationTokenSource? cooldownCts = null;
            TimeSpan cooldownDelay;

            lock (_syncRoot)
            {
                if (_disposed || _isAsyncThrottling)
                    return false;

                _isAsyncThrottling = true;
                cooldownDelay = _interval;
            }

            bool accepted = false;
            try
            {
                await action(_disposeCts.Token).ConfigureAwait(false);
                accepted = true;
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (!_disposed && _isAsyncThrottling)
                    {
                        _cooldownCts?.Cancel();
                        _cooldownCts?.Dispose();
                        _cooldownCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                        cooldownCts = _cooldownCts;
                    }
                    else
                    {
                        _isAsyncThrottling = false;
                    }
                }

                if (cooldownCts != null)
                {
                    _ = CompleteThrottleCooldownAsync(cooldownCts, cooldownDelay);
                }
            }

            return accepted;
        }

        private async Task CompleteThrottleCooldownAsync(CancellationTokenSource cooldownCts, TimeSpan cooldownDelay)
        {
            if (cooldownCts == null)
                return;

            try
            {
                await Task.Delay(cooldownDelay, cooldownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cooldownCts.IsCancellationRequested || _disposeCts.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_cooldownCts, cooldownCts))
                    {
                        _cooldownCts!.Dispose();
                        _cooldownCts = null;
                    }

                    _isAsyncThrottling = false;
                }
            }
        }

        #endregion

        /// <summary>
        /// 取消当前所有挂起的同步/异步防抖，以及同步/异步节流冷却，并恢复可调用状态。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 该方法不会中断已经开始执行中的 <see cref="ThrottleAsync(Func{CancellationToken, Task})"/>。
        /// 对于已经开始执行中的 <see cref="DebounceAsync(Func{CancellationToken, Task})"/>，
        /// 会发出取消请求，但是否真正停止执行取决于用户委托是否响应取消令牌。
        /// </para>
        /// <para>
        /// 注意：在非 Dispatcher 线程调用时，取消操作为异步生效（内部通过 <c>BeginInvoke</c> 派发），
        /// 方法返回时实际取消可能尚未发生。
        /// </para>
        /// </remarks>
        public void Cancel()
        {
            Dispatch(() =>
            {
                CancellationTokenSource? debounceCts = null;
                CancellationTokenSource? cooldownCts = null;

                lock (_syncRoot)
                {
                    if (_disposed)
                        return;

                    _debounceTimer.Stop();
                    _throttleTimer.Stop();

                    _pendingSyncAction = null;
                    _isSyncThrottling = false;

                    debounceCts = _debounceCts;
                    _debounceCts = null;

                    cooldownCts = _cooldownCts;
                    _cooldownCts = null;

                    _isAsyncThrottling = false;
                }

                if (debounceCts != null)
                {
                    debounceCts.Cancel();
                    debounceCts.Dispose();
                }

                if (cooldownCts != null)
                {
                    cooldownCts.Cancel();
                    cooldownCts.Dispose();
                }
            }, false);
        }

        /// <summary>
        /// 释放当前实例并取消所有挂起或正在进行的异步请求。
        /// </summary>
        public void Dispose()
        {
            CancellationTokenSource? debounceCts = null;
            CancellationTokenSource? cooldownCts = null;

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;

                _pendingSyncAction = null;
                _isSyncThrottling = false;
                _isAsyncThrottling = false;

                debounceCts = _debounceCts;
                _debounceCts = null;

                cooldownCts = _cooldownCts;
                _cooldownCts = null;
            }

            try
            {
                Dispatch(() =>
                {
                    _debounceTimer.Stop();
                    _throttleTimer.Stop();
                }, true);
            }
            catch
            {
            }

            if (debounceCts != null)
            {
                debounceCts.Cancel();
                debounceCts.Dispose();
            }

            if (cooldownCts != null)
            {
                cooldownCts.Cancel();
                cooldownCts.Dispose();
            }

            _disposeCts.Cancel();
            _disposeCts.Dispose();

            _debounceTimer.Tick -= OnDebounceTick;
            _throttleTimer.Tick -= OnThrottleTick;
        }

        private bool DispatcherAlive
        {
            get
            {
                return _dispatcher != null
                    && !_dispatcher.HasShutdownStarted
                    && !_dispatcher.HasShutdownFinished;
            }
        }

        private void Dispatch(Action action, bool invoke)
        {
            if (action == null)
                return;

            if (!DispatcherAlive)
                return;

            try
            {
                if (_dispatcher?.CheckAccess() == true)
                {
                    action();
                    return;
                }

                if (invoke)
                    _dispatcher?.Invoke(_priority, action);
                else
                    _dispatcher?.BeginInvoke(_priority, action);
            }
            catch (TaskCanceledException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (InvalidOperationException)
            {
                if (DispatcherAlive)
                    throw;
            }
        }
    }
}