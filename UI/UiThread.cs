using System;
using System.Windows;
using System.Windows.Threading;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供与 WPF UI 线程调度相关的辅助方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类用于在任意线程中安全地切换到当前应用程序的 UI 线程执行代码。
    /// </para>
    /// <para>
    /// 设计原则:
    /// </para>
    /// <list type="bullet">
    /// <item><description>仅负责线程调度,不负责通知、日志或异常提示。</description></item>
    /// <item><description>若当前已处于 UI 线程,则直接执行,避免无意义的再次调度。</description></item>
    /// <item><description>同步方法保留异常语义,不吞异常。</description></item>
    /// </list>
    /// </remarks>
    public static class UiThread
    {
        /// <summary>
        /// 在 UI 线程上异步执行指定操作。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        /// <param name="priority">调度优先级。</param>
        /// <remarks>
        /// <para>
        /// 若当前线程已经是 UI 线程,则会直接执行 <paramref name="action"/>。
        /// </para>
        /// <para>
        /// 若当前应用程序不存在可用的 <see cref="Dispatcher"/>,也会直接执行。
        /// </para>
        /// <para>
        /// 若 <see cref="Dispatcher"/> 已开始或已完成关闭,本方法静默返回,不再调度。
        /// 该行为与 fire-and-forget 语义一致,用于规避后台线程在应用退出阶段调用时
        /// 抛出 <see cref="InvalidOperationException"/> 导致进程崩溃。
        /// </para>
        /// </remarks>
        public static void Run(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
        {
            if (action == null)
                return;

            Dispatcher dispatcher = GetDispatcher();

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                return;

            dispatcher.BeginInvoke(action, priority);
        }

        /// <summary>
        /// 在 UI 线程上同步执行指定操作,并等待完成。
        /// </summary>
        /// <param name="action">要执行的操作。</param>
        /// <param name="priority">调度优先级。</param>
        /// <remarks>
        /// <para>
        /// 若当前线程已经是 UI 线程,则会直接执行 <paramref name="action"/>。
        /// </para>
        /// <para>
        /// 若当前应用程序不存在可用的 <see cref="Dispatcher"/>,
        /// 也会直接在当前调用线程同步执行,而非切换到任何 UI 线程。
        /// </para>
        /// <para>
        /// 若 <see cref="Dispatcher"/> 已开始或已完成关闭,
        /// 底层 <see cref="Dispatcher.Invoke(Delegate, DispatcherPriority, object[])"/>
        /// 抛出的异常会按"不吞异常"原则原样向上抛出。
        /// </para>
        /// </remarks>
        public static void RunAndWait(Action action, DispatcherPriority priority = DispatcherPriority.Send)
        {
            if (action == null)
                return;

            Dispatcher dispatcher = GetDispatcher();

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Invoke(action, priority);
        }

        /// <summary>
        /// 在 UI 线程上同步执行指定函数,并返回结果。
        /// </summary>
        /// <typeparam name="TResult">返回值类型。</typeparam>
        /// <param name="func">要执行的函数。</param>
        /// <param name="priority">调度优先级。</param>
        /// <returns>函数执行结果;若 <paramref name="func"/> 为 <see langword="null"/>,返回 <typeparamref name="TResult"/> 的默认值。</returns>
        /// <remarks>
        /// <para>
        /// 若当前线程已经是 UI 线程,则会直接执行 <paramref name="func"/>。
        /// </para>
        /// <para>
        /// 若当前应用程序不存在可用的 <see cref="Dispatcher"/>,
        /// 也会直接在当前调用线程同步执行。
        /// </para>
        /// <para>
        /// 若 <see cref="Dispatcher"/> 已开始或已完成关闭,
        /// 底层 <see cref="Dispatcher.Invoke{TResult}(Func{TResult}, DispatcherPriority)"/>
        /// 抛出的异常会按"不吞异常"原则原样向上抛出。
        /// </para>
        /// </remarks>
        public static TResult RunAndWait<TResult>(Func<TResult> func, DispatcherPriority priority = DispatcherPriority.Send)
        {
            if (func == null)
                return default(TResult);

            Dispatcher dispatcher = GetDispatcher();

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                return func();
            }

            return dispatcher.Invoke(func, priority);
        }

        /// <summary>
        /// 判断当前线程是否可以直接访问 UI 线程对象。
        /// </summary>
        /// <returns>
        /// 若当前存在可用的 <see cref="Dispatcher"/> 且当前线程拥有访问权限,则返回 <c>true</c>;否则返回 <c>false</c>。
        /// </returns>
        /// <remarks>
        /// 当 <see cref="Application.Current"/> 不可用时(如单元测试、纯后台进程),
        /// 此方法返回 <c>false</c>,但 <see cref="Run"/> 与 <see cref="RunAndWait(Action, DispatcherPriority)"/>
        /// 在该情况下会直接在当前线程同步执行,而非进行异步调度。
        /// 若需区分"是否在 UI 线程"与"是否存在 UI 线程",请直接检查 <see cref="GetDispatcher"/> 的返回值。
        /// </remarks>
        public static bool CheckAccess()
        {
            Dispatcher dispatcher = GetDispatcher();
            return dispatcher != null && dispatcher.CheckAccess();
        }

        /// <summary>
        /// 获取当前应用程序的 UI 线程 <see cref="Dispatcher"/>。
        /// </summary>
        /// <returns>当前应用程序的 <see cref="Dispatcher"/>;若不可用则返回 <c>null</c>。</returns>
        public static Dispatcher? GetDispatcher()
        {
            return Application.Current != null ? Application.Current.Dispatcher : null;
        }
    }
}