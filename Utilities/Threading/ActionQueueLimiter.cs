using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace KkjQuicker.Utilities.Threading
{
    /// <summary>
    /// 提供按顺序排队执行同步或异步操作的能力，并在相邻两次执行之间保持最小时间间隔。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类适用于"触发不能丢失，但又不希望同时执行"的场景。
    /// 与只保留最后一次调用或在冷却期内直接忽略后续调用的限频器不同，
    /// 本类会保留所有已提交的操作，并按先进先出的顺序依次执行。
    /// </para>
    /// <para>执行规则：</para>
    /// <list type="bullet">
    /// <item><description>所有操作按提交顺序依次执行，不并发。</description></item>
    /// <item><description>每个操作执行完成后，会等待 <see cref="Interval"/>，再执行下一个。</description></item>
    /// <item><description><see cref="Cancel"/> 会清空尚未执行的队列项，并立刻结束当前的等待间隔，但不会中断当前正在执行的同步或异步操作。</description></item>
    /// <item><description><see cref="Dispose"/> 会停止接收新操作、清空未执行队列，并取消当前正在执行的异步操作。</description></item>
    /// </list>
    /// <para>线程上下文说明：</para>
    /// <para>
    /// 本类是后台串行队列，不是 UI 线程队列。
    /// 已入队的同步或异步操作默认在线程池线程执行；
    /// 若需要操作 WPF UI，请在队列项内部自行切回 Dispatcher。
    /// </para>
    /// <para>异常说明：</para>
    /// <list type="bullet">
    /// <item><description>同步队列项抛出的异常不会中断整个队列，会被捕获并写入 <see cref="Debug"/>。</description></item>
    /// <item><description>异步队列项抛出的非取消异常会传递到对应的 <see cref="EnqueueAsync(Func{CancellationToken, Task})"/> 返回任务，但不会中断整个队列。</description></item>
    /// </list>
    /// </remarks>
    public sealed class ActionQueueLimiter : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly Queue<QueueItem> _queue = new Queue<QueueItem>();
        private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

        private TimeSpan _interval;
        private bool _disposed;
        private bool _isProcessing;

        private CancellationTokenSource _delayCts;
        private CancellationTokenSource _currentExecutionCts;

        /// <summary>
        /// 初始化 <see cref="ActionQueueLimiter"/>。
        /// </summary>
        /// <param name="interval">相邻两次执行之间的最小间隔，必须大于 <see cref="TimeSpan.Zero"/>。</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="interval"/> 小于或等于零。</exception>
        public ActionQueueLimiter(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval));

            _interval = interval;
        }

        /// <summary>
        /// 获取或设置相邻两次执行之间的最小间隔。
        /// </summary>
        /// <value>必须大于 <see cref="TimeSpan.Zero"/>。</value>
        /// <remarks>
        /// 修改该属性只影响后续等待，不会回溯改变已经开始的操作。
        /// 若当前正处于间隔等待中，新值通常会从下一次等待开始生效。
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

                    _interval = value;
                }
            }
        }

        /// <summary>
        /// 将一个同步操作加入队列，按顺序执行。
        /// </summary>
        /// <param name="action">要加入队列的同步操作。</param>
        /// <remarks>
        /// 该方法只负责入队，不等待实际执行完成。
        /// 若调用时对象已释放，则该调用会被忽略。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public void Enqueue(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            EnqueueCore(new QueueItem(action));
        }

        /// <summary>
        /// 将一个异步操作加入队列，按顺序执行。
        /// </summary>
        /// <param name="action">要加入队列的异步操作。框架会向其传入在 <see cref="Dispose"/> 时取消的 <see cref="CancellationToken"/>。</param>
        /// <returns>
        /// 一个任务，用于等待该队列项最终结果：
        /// <see langword="true"/> 表示该项已实际执行完成；
        /// <see langword="false"/> 表示该项在执行前被 <see cref="Cancel"/> 或 <see cref="Dispose"/> 清除，或执行中因 <see cref="Dispose"/> 被取消。
        /// </returns>
        /// <remarks>
        /// 若异步委托本身抛出非取消异常，该返回任务会以异常结束。
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="action"/> 为 <see langword="null"/>。</exception>
        public Task<bool> EnqueueAsync(Func<CancellationToken, Task> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var item = new QueueItem(action);
            EnqueueCore(item);
            return item.Completion.Task;
        }

        /// <summary>
        /// 清空所有尚未执行的队列项，并立即结束当前的等待间隔。
        /// </summary>
        /// <remarks>
        /// <para>该方法会：</para>
        /// <list type="bullet">
        /// <item><description>清空尚未开始执行的同步队列项。</description></item>
        /// <item><description>使尚未开始执行的异步队列项返回 <see langword="false"/>。</description></item>
        /// <item><description>中断当前"执行完成后的间隔等待"，使处理循环立刻进入下一轮检查。</description></item>
        /// </list>
        /// <para>
        /// 该方法不会中断当前正在执行的同步操作，也不会取消当前正在执行的异步操作。
        /// </para>
        /// </remarks>
        public void Cancel()
        {
            Queue<QueueItem> pending = null;
            CancellationTokenSource delayToCancel = null;

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                if (_queue.Count > 0)
                {
                    pending = new Queue<QueueItem>(_queue);
                    _queue.Clear();
                }

                delayToCancel = _delayCts;
                // _delayCts 不置 null：由 ProcessLoopAsync 的 finally 负责释放
            }

            if (pending != null)
            {
                while (pending.Count > 0)
                {
                    pending.Dequeue().TrySetCanceledBeforeExecution();
                }
            }

            CancelToken(delayToCancel);
        }

        /// <summary>
        /// 释放当前实例占用的资源，并终止后续队列处理。
        /// </summary>
        /// <remarks>
        /// <para>该方法是幂等的，可安全重复调用。</para>
        /// <para>释放时会：</para>
        /// <list type="bullet">
        /// <item><description>停止接收新的队列项。</description></item>
        /// <item><description>清空尚未执行的队列项；尚未开始的异步项会返回 <see langword="false"/>。</description></item>
        /// <item><description>取消当前正在执行的异步项。</description></item>
        /// <item><description>结束当前等待间隔。</description></item>
        /// <item><description>释放内部取消资源。</description></item>
        /// </list>
        /// </remarks>
        public void Dispose()
        {
            Queue<QueueItem> pending = null;
            CancellationTokenSource delayToCancel = null;
            CancellationTokenSource executionToCancel = null;

            lock (_syncRoot)
            {
                if (_disposed)
                    return;

                _disposed = true;

                if (_queue.Count > 0)
                {
                    pending = new Queue<QueueItem>(_queue);
                    _queue.Clear();
                }

                delayToCancel = _delayCts;
                _delayCts = null;
                // _delayCts 已置 null，ProcessLoopAsync 的 finally 无法通过 ReferenceEquals 检查，
                // 必须由此处负责完整的 Cancel + Dispose，不能仅 Cancel。

                executionToCancel = _currentExecutionCts;
                _currentExecutionCts = null;
                // 同上，ExecuteItemAsync 的 finally 无法通过 ReferenceEquals 检查。
            }

            if (pending != null)
            {
                while (pending.Count > 0)
                {
                    pending.Dequeue().TrySetCanceledBeforeExecution();
                }
            }

            CancelDispose(ref delayToCancel);
            CancelDispose(ref executionToCancel);

            try
            {
                _disposeCts.Cancel();
            }
            catch
            {
            }

            try
            {
                _disposeCts.Dispose();
            }
            catch
            {
            }
        }

        private void EnqueueCore(QueueItem item)
        {
            bool shouldStart = false;

            lock (_syncRoot)
            {
                if (_disposed)
                {
                    item.TrySetCanceledBeforeExecution();
                    return;
                }

                _queue.Enqueue(item);

                if (!_isProcessing)
                {
                    _isProcessing = true;
                    shouldStart = true;
                }
            }

            if (!shouldStart)
                return;

            var task = Task.Run(ProcessLoopAsync);
            task.ContinueWith(
                t => { var _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task ProcessLoopAsync()
        {
            while (true)
            {
                QueueItem item;

                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        _isProcessing = false;
                        return;
                    }

                    if (_queue.Count == 0)
                    {
                        _isProcessing = false;
                        return;
                    }

                    item = _queue.Dequeue();
                }

                await ExecuteItemAsync(item).ConfigureAwait(false);

                TimeSpan delay;
                CancellationTokenSource localDelayCts;

                lock (_syncRoot)
                {
                    if (_disposed)
                    {
                        _isProcessing = false;
                        return;
                    }

                    CancelDispose(ref _delayCts);
                    _delayCts = new CancellationTokenSource();

                    localDelayCts = _delayCts;
                    delay = _interval;
                }

                try
                {
                    await Task.Delay(delay, localDelayCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        if (ReferenceEquals(_delayCts, localDelayCts))
                        {
                            CancelDispose(ref _delayCts);
                        }
                    }
                }
            }
        }

        private async Task ExecuteItemAsync(QueueItem item)
        {
            if (item.IsAsync)
            {
                CancellationTokenSource executionCts = null;

                try
                {
                    lock (_syncRoot)
                    {
                        if (_disposed)
                        {
                            item.TrySetCanceledBeforeExecution();
                            return;
                        }

                        CancelDispose(ref _currentExecutionCts);
                        _currentExecutionCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
                        executionCts = _currentExecutionCts;
                    }

                    await item.AsyncAction(executionCts.Token).ConfigureAwait(false);
                    item.TrySetSucceeded();
                }
                catch (OperationCanceledException)
                {
                    item.TrySetCanceled();
                }
                catch (ObjectDisposedException)
                {
                    item.TrySetCanceled();
                }
                catch (Exception ex)
                {
                    item.TrySetException(ex);
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        if (ReferenceEquals(_currentExecutionCts, executionCts))
                        {
                            CancelDispose(ref _currentExecutionCts);
                        }
                    }
                }

                return;
            }

            try
            {
                item.SyncAction();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static void CancelToken(CancellationTokenSource cts)
        {
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }
        }

        private static void CancelDispose(ref CancellationTokenSource cts)
        {
            if (cts == null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            try
            {
                cts.Dispose();
            }
            catch
            {
            }

            cts = null;
        }

        private sealed class QueueItem
        {
            public readonly Action SyncAction;
            public readonly Func<CancellationToken, Task> AsyncAction;
            public readonly TaskCompletionSource<bool> Completion;

            public bool IsAsync
            {
                get { return AsyncAction != null; }
            }

            public QueueItem(Action action)
            {
                SyncAction = action;
            }

            public QueueItem(Func<CancellationToken, Task> action)
            {
                AsyncAction = action;
                Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public void TrySetSucceeded()
            {
                if (Completion != null)
                    Completion.TrySetResult(true);
            }

            public void TrySetCanceledBeforeExecution()
            {
                if (Completion != null)
                    Completion.TrySetResult(false);
            }

            public void TrySetCanceled()
            {
                if (Completion != null)
                    Completion.TrySetResult(false);
            }

            public void TrySetException(Exception ex)
            {
                if (Completion != null)
                    Completion.TrySetException(ex);
            }
        }
    }
}