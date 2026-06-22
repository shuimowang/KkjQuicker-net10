using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace KkjQuicker.UI.Commands
{
    #region Interfaces

    /// <summary>
    /// 表示支持显式异步执行的命令。
    /// </summary>
    /// <typeparam name="T">命令参数类型。</typeparam>
    /// <remarks>
    /// <para>
    /// 该接口在 <see cref="ICommand"/> 基础上提供返回 <see cref="Task"/> 的执行入口，
    /// 以避免在业务代码中使用 <c>async void</c>。
    /// </para>
    /// <para>
    /// 当调用方希望等待命令执行完成、捕获异常或参与更复杂的异步流程编排时，
    /// 可优先使用 <see cref="ExecuteAsync(T)"/>。
    /// </para>
    /// </remarks>
    public interface IAsyncCommand<T> : ICommand
    {
        /// <summary>
        /// 异步执行命令。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        /// <returns>表示异步执行过程的任务。</returns>
        Task ExecuteAsync(T parameter);
    }

    #endregion Interfaces

    #region TaskExtensions

    /// <summary>
    /// 提供 <see cref="Task"/> 的辅助扩展方法。
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// 全局未观察异常处理器。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 当通过 <see cref="Forget(Task)"/> 以 fire-and-forget 方式执行任务时，
        /// 若任务内部发生异常，将通过此委托进行统一处理。
        /// </para>
        /// <para>
        /// 可在应用启动时注入日志系统，例如：
        /// </para>
        /// <code>
        /// TaskExtensions.UnhandledExceptionHandler = ex => Logger.Error(ex);
        /// </code>
        /// </remarks>
        public static Action<Exception> UnhandledExceptionHandler = null!;

        /// <summary>
        /// 以安全方式忽略任务结果（fire-and-forget）。
        /// </summary>
        /// <param name="task">要执行的任务。</param>
        /// <remarks>
        /// <para>
        /// 该方法适用于 <see cref="ICommand.Execute(object)"/> 这类必须同步返回、
        /// 但内部实际需要启动异步流程的场景。
        /// </para>
        /// <para>
        /// 任务异常不会向调用方传播，而是通过
        /// <see cref="UnhandledExceptionHandler"/> 统一上报，并写入调试输出。
        /// </para>
        /// </remarks>
        public static async void Forget(this Task task)
        {
            if (task == null)
                return;

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                try
                {
                    var handler = UnhandledExceptionHandler;
                    if (handler != null)
                        handler(ex);
                }
                catch
                {
                    // 避免异常处理器自身再次抛错影响最终兜底。
                }

                Debug.WriteLine("Unhandled command exception: " + ex);
            }
        }
    }

    #endregion TaskExtensions

    #region CommandBase

    /// <summary>
    /// 命令基类，封装 <see cref="ICommand"/> 的基础行为。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该类型不依赖 <see cref="CommandManager"/>，适用于需要手动、精确控制
    /// 命令可执行状态通知的场景。
    /// </para>
    /// <para>
    /// 通过 <see cref="RaiseCanExecuteChanged"/> 可从任意线程安全触发
    /// <see cref="CanExecuteChanged"/> 事件。
    /// </para>
    /// </remarks>
    public abstract class CommandBase : ICommand
    {
        /// <summary>
        /// 当命令的可执行状态发生变化时发生。
        /// </summary>
        public event EventHandler? CanExecuteChanged;

        /// <summary>
        /// 确定命令当前是否可执行。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        /// <returns>若命令可执行则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public abstract bool CanExecute(object parameter);

        /// <summary>
        /// 执行命令。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        public abstract void Execute(object parameter);

        /// <summary>
        /// 触发 <see cref="CanExecuteChanged"/> 事件。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 可从任意线程调用。
        /// </para>
        /// <para>
        /// 若当前线程不是 UI 线程，则会自动切换到应用程序主调度器异步触发事件，
        /// 以避免后台线程同步阻塞 UI 线程。
        /// </para>
        /// </remarks>
        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler == null)
                return;

            var app = Application.Current;
            var dispatcher = app?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(
                    DispatcherPriority.DataBind,
                    new Action(() => handler(this, EventArgs.Empty)));
            }
            else
            {
                handler(this, EventArgs.Empty);
            }
        }
    }

    #endregion CommandBase

    #region DelegateCommand (Sync)

    /// <summary>
    /// 表示带参数的同步委托命令。
    /// </summary>
    /// <typeparam name="T">命令参数类型。</typeparam>
    /// <remarks>
    /// <para>
    /// 该类型通过委托封装命令执行逻辑与可执行判断逻辑，
    /// 适用于大多数 MVVM 同步命令场景。
    /// </para>
    /// <para>
    /// 参数类型转换失败时，<see cref="CanExecute(object)"/> 返回 <see langword="false"/>，
    /// <see cref="Execute(object)"/> 将直接忽略本次调用。
    /// </para>
    /// </remarks>
    public class DelegateCommand<T> : CommandBase
    {
        /// <summary>
        /// 命令执行委托。
        /// </summary>
        protected readonly Action<T> _execute;

        /// <summary>
        /// 命令可执行判断委托。
        /// </summary>
        protected readonly Func<T, bool> _canExecute;

        /// <summary>
        /// 初始化 <see cref="DelegateCommand{T}"/> 的新实例。
        /// </summary>
        /// <param name="execute">命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <see langword="null"/>。</exception>
        public DelegateCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (_ => true);
        }

        /// <inheritdoc />
        public override bool CanExecute(object parameter)
        {
            T value;
            return TryGetParameter(parameter, out value) && _canExecute(value);
        }

        /// <inheritdoc />
        public override void Execute(object parameter)
        {
            T value;
            if (TryGetParameter(parameter, out value))
                _execute(value);
        }

        /// <summary>
        /// 尝试将原始命令参数转换为 <typeparamref name="T"/>。
        /// </summary>
        /// <param name="parameter">原始参数。</param>
        /// <param name="value">转换成功后的参数值。</param>
        /// <returns>
        /// 转换成功返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 该方法支持以下情况：
        /// </para>
        /// <list type="bullet">
        /// <item><description>参数本身就是 <typeparamref name="T"/> 类型。</description></item>
        /// <item><description>参数为 <see langword="null"/>，且 <typeparamref name="T"/> 为引用类型或可空值类型。</description></item>
        /// </list>
        /// <para>
        /// 不执行字符串到数值、枚举等隐式解析转换，以保持行为简单、可预测。
        /// </para>
        /// </remarks>
        public static bool TryGetParameter(object parameter, out T value)
        {
            if (parameter is T t)
            {
                value = t;
                return true;
            }

            if (parameter == null && default(T) == null)
            {
                value = default(T);
                return true;
            }

            value = default(T);
            return false;
        }
    }

    /// <summary>
    /// 表示无参数的同步委托命令。
    /// </summary>
    /// <remarks>
    /// 该类型是 <see cref="DelegateCommand{T}"/> 的无参数便捷封装。
    /// </remarks>
    public class DelegateCommand : DelegateCommand<object>
    {
        /// <summary>
        /// 初始化 <see cref="DelegateCommand"/> 的新实例。
        /// </summary>
        /// <param name="execute">命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <see langword="null"/>。</exception>
        public DelegateCommand(Action execute, Func<bool> canExecute = null)
            : base(
                WrapExecute(execute),
                WrapCanExecute(canExecute))
        {
        }

        private static Action<object> WrapExecute(Action execute)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));

            return _ => execute();
        }

        private static Func<object, bool> WrapCanExecute(Func<bool> canExecute)
        {
            if (canExecute == null)
                return _ => true;

            return _ => canExecute();
        }
    }

    #endregion DelegateCommand (Sync)

    #region AsyncDelegateCommand

    /// <summary>
    /// 表示带参数的异步委托命令。
    /// </summary>
    /// <typeparam name="T">命令参数类型。</typeparam>
    /// <remarks>
    /// <para>
    /// 该类型支持两种执行语义：
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// 使用 <see cref="Func{T, Task}"/> 构造时：执行期间自动禁用命令，避免重复触发。
    /// 适合保存、提交、刷新等按钮型操作。
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// 使用 <see cref="Func{T, CancellationToken, Task}"/> 构造时：新请求会取消旧请求，
    /// 始终保留最后一次执行。适合搜索、联想、实时过滤等场景。
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// 注意：<see cref="IsExecuting"/> 仅提供状态读取，本类型本身不实现属性变更通知。
    /// 若需要直接绑定 UI，可由外层 ViewModel 进行状态转发。
    /// </para>
    /// </remarks>
    public class AsyncDelegateCommand<T> : CommandBase, IAsyncCommand<T>
    {
        private readonly Func<T, Task> _execute;
        private readonly Func<T, CancellationToken, Task> _executeWithToken;
        private readonly Func<T, bool> _canExecute;

        private int _isExecuting;
        private long _executionId;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 获取命令当前是否正在执行。
        /// </summary>
        public bool IsExecuting
        {
            get { return Interlocked.CompareExchange(ref _isExecuting, 0, 0) != 0; }
        }

        /// <summary>
        /// 初始化 <see cref="AsyncDelegateCommand{T}"/> 的新实例。
        /// </summary>
        /// <param name="execute">异步命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <see langword="null"/>。</exception>
        public AsyncDelegateCommand(Func<T, Task> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (_ => true);
        }

        /// <summary>
        /// 初始化 <see cref="AsyncDelegateCommand{T}"/> 的新实例。
        /// </summary>
        /// <param name="execute">带取消令牌的异步命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <see langword="null"/>。</exception>
        public AsyncDelegateCommand(Func<T, CancellationToken, Task> execute, Func<T, bool> canExecute = null)
        {
            _executeWithToken = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute ?? (_ => true);
        }

        /// <inheritdoc />
        public override bool CanExecute(object parameter)
        {
            T value;
            if (!DelegateCommand<T>.TryGetParameter(parameter, out value))
                return false;

            if (_executeWithToken == null && IsExecuting)
                return false;

            return _canExecute(value);
        }

        /// <inheritdoc />
        public override void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            ExecuteAsyncInternal(parameter).Forget();
        }

        /// <summary>
        /// 异步执行命令。
        /// </summary>
        /// <param name="parameter">命令参数。</param>
        /// <returns>表示异步执行过程的任务。</returns>
        /// <remarks>
        /// 该方法与 <see cref="ICommand.Execute(object)"/> 共享同一套执行状态管理逻辑。
        /// </remarks>
        public Task ExecuteAsync(T parameter)
        {
            return ExecuteAsyncInternal(parameter);
        }

        /// <summary>
        /// 取消当前执行中的命令。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 仅当命令通过支持 <see cref="CancellationToken"/> 的构造函数创建时，
        /// 调用该方法才具有实际取消效果。
        /// </para>
        /// <para>
        /// 该方法只负责发出取消请求，具体何时结束取决于命令实现是否正确响应取消令牌。
        /// </para>
        /// </remarks>
        public void Cancel()
        {
            var cts = _cts;
            if (cts != null)
                cts.Cancel();
        }

        private Task ExecuteAsyncInternal(object parameter)
        {
            T value;
            if (!DelegateCommand<T>.TryGetParameter(parameter, out value))
                return Task.CompletedTask;

            return ExecuteCoreAsync(value);
        }

        private async Task ExecuteCoreAsync(T parameter)
        {
            if (!_canExecute(parameter))
                return;

            if (_executeWithToken == null)
            {
                await ExecuteSingleFlightAsync(parameter).ConfigureAwait(false);
                return;
            }

            await ExecuteLatestWinsAsync(parameter).ConfigureAwait(false);
        }

        private async Task ExecuteSingleFlightAsync(T parameter)
        {
            if (Interlocked.CompareExchange(ref _isExecuting, 1, 0) != 0)
                return;

            try
            {
                RaiseCanExecuteChanged();
                await _execute(parameter).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                RaiseCanExecuteChanged();
            }
        }

        private async Task ExecuteLatestWinsAsync(T parameter)
        {
            var currentExecutionId = Interlocked.Increment(ref _executionId);
            var currentCts = new CancellationTokenSource();
            var currentToken = currentCts.Token;

            var oldCts = Interlocked.Exchange(ref _cts, currentCts);
            if (oldCts != null)
            {
                oldCts.Cancel();
            }

            var shouldRaiseExecuting = Interlocked.Exchange(ref _isExecuting, 1) == 0;
            if (shouldRaiseExecuting)
                RaiseCanExecuteChanged();

            try
            {
                await _executeWithToken(parameter, currentToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!currentToken.IsCancellationRequested)
                    throw;
            }
            finally
            {
                Interlocked.CompareExchange(ref _cts, null, currentCts);
                currentCts.Dispose();

                if (Interlocked.Read(ref _executionId) == currentExecutionId)
                {
                    Interlocked.Exchange(ref _isExecuting, 0);
                    RaiseCanExecuteChanged();
                }
            }
        }
    }

    /// <summary>
    /// 表示无参数的异步委托命令。
    /// </summary>
    /// <remarks>
    /// 该类型是 <see cref="AsyncDelegateCommand{T}"/> 的无参数便捷封装。
    /// 使用此类型时，默认采用“执行期间禁用命令”的单飞行语义。
    /// </remarks>
    public class AsyncDelegateCommand : AsyncDelegateCommand<object>
    {
        /// <summary>
        /// 初始化 <see cref="AsyncDelegateCommand"/> 的新实例。
        /// </summary>
        /// <param name="execute">异步命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <exception cref="ArgumentNullException"><paramref name="execute"/> 为 <see langword="null"/>。</exception>
        public AsyncDelegateCommand(Func<Task> execute, Func<bool> canExecute = null)
            : base(
                WrapExecute(execute),
                WrapCanExecute(canExecute))
        {
        }

        private static Func<object, Task> WrapExecute(Func<Task> execute)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));

            return _ => execute();
        }

        private static Func<object, bool> WrapCanExecute(Func<bool> canExecute)
        {
            if (canExecute == null)
                return _ => true;

            return _ => canExecute();
        }
    }

    #endregion AsyncDelegateCommand

    #region CommandFactory

    /// <summary>
    /// 提供统一的命令创建入口。
    /// </summary>
    /// <remarks>
    /// 该工厂仅提供轻量级便捷封装，不引入额外生命周期或容器依赖。
    /// </remarks>
    public static class CommandFactory
    {
        /// <summary>
        /// 创建无参数同步命令。
        /// </summary>
        /// <param name="execute">命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <returns><see cref="DelegateCommand"/> 实例。</returns>
        public static DelegateCommand Create(Action execute, Func<bool> canExecute = null)
        {
            return new DelegateCommand(execute, canExecute);
        }

        /// <summary>
        /// 创建带参数同步命令。
        /// </summary>
        /// <typeparam name="T">命令参数类型。</typeparam>
        /// <param name="execute">命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <returns><see cref="DelegateCommand{T}"/> 实例。</returns>
        public static DelegateCommand<T> Create<T>(Action<T> execute, Func<T, bool> canExecute = null)
        {
            return new DelegateCommand<T>(execute, canExecute);
        }

        /// <summary>
        /// 创建无参数异步命令。
        /// </summary>
        /// <param name="execute">异步命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <returns><see cref="AsyncDelegateCommand"/> 实例。</returns>
        /// <remarks>
        /// 创建的命令默认采用“执行期间禁用命令”的单飞行语义。
        /// </remarks>
        public static AsyncDelegateCommand CreateAsync(Func<Task> execute, Func<bool> canExecute = null)
        {
            return new AsyncDelegateCommand(execute, canExecute);
        }

        /// <summary>
        /// 创建带参数异步命令。
        /// </summary>
        /// <typeparam name="T">命令参数类型。</typeparam>
        /// <param name="execute">异步命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <returns><see cref="AsyncDelegateCommand{T}"/> 实例。</returns>
        /// <remarks>
        /// 创建的命令默认采用“执行期间禁用命令”的单飞行语义。
        /// </remarks>
        public static AsyncDelegateCommand<T> CreateAsync<T>(Func<T, Task> execute, Func<T, bool> canExecute = null)
        {
            return new AsyncDelegateCommand<T>(execute, canExecute);
        }

        /// <summary>
        /// 创建支持取消的带参数异步命令。
        /// </summary>
        /// <typeparam name="T">命令参数类型。</typeparam>
        /// <param name="execute">带取消令牌的异步命令执行逻辑。</param>
        /// <param name="canExecute">命令可执行判断逻辑；为 <see langword="null"/> 时始终可执行。</param>
        /// <returns><see cref="AsyncDelegateCommand{T}"/> 实例。</returns>
        /// <remarks>
        /// 创建的命令采用“latest-wins”语义：新请求会取消旧请求。
        /// </remarks>
        public static AsyncDelegateCommand<T> CreateAsync<T>(
            Func<T, CancellationToken, Task> execute,
            Func<T, bool> canExecute = null)
        {
            return new AsyncDelegateCommand<T>(execute, canExecute);
        }
    }

    #endregion CommandFactory
}
