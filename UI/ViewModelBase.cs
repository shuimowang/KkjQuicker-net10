using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供属性更改通知的基类，适用于 WPF 的 ViewModel 或其他支持数据绑定的对象。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该基类封装了 <see cref="INotifyPropertyChanged"/> 的常见实现，
    /// 并提供属性设置、批量暂停通知，以及将属性更改通知调度到关联 <see cref="Dispatcher"/> 的能力。
    /// </para>
    /// <para>
    /// 注意：本类型负责将属性更改通知封送到关联的 <see cref="Dispatcher"/>，
    /// 但不保证对象状态本身的完全线程安全。
    /// </para>
    /// </remarks>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        private readonly Dispatcher _dispatcher;
        private int _notificationSuspendCount;
        private bool _hasPendingChanges;

        /// <summary>
        /// 初始化 <see cref="ViewModelBase"/> 类的新实例。
        /// </summary>
        /// <remarks>
        /// 优先使用当前 <see cref="Application"/> 的 <see cref="Dispatcher"/> 作为关联调度器；
        /// 若当前应用尚未创建 <see cref="Application"/>，则回退到当前线程的 <see cref="Dispatcher"/>。
        /// 为获得最符合 WPF 绑定预期的行为，通常应在 UI 线程创建派生对象实例。
        /// </remarks>
        protected ViewModelBase()
        {
            // Fix #1：缓存 Application.Current，避免两次读取之间的窗口期。
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        /// <summary>
        /// 当属性值发生更改时发生。
        /// </summary>
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 暂停属性更改通知，适用于批量更新多个属性的场景。
        /// </summary>
        /// <returns>
        /// 一个作用域对象。释放该对象后将恢复通知；
        /// 若暂停期间存在属性变更，则会统一触发一次"所有属性已更改"的通知。
        /// </returns>
        /// <remarks>
        /// <para>典型用法：</para>
        /// <code>
        /// using (SuspendNotifications())
        /// {
        ///     Foo = 1;
        ///     Bar = 2;
        ///     Baz = 3;
        /// }
        /// </code>
        /// <para>支持嵌套调用。只有最外层暂停作用域释放时，才会真正恢复通知。</para>
        /// </remarks>
        public IDisposable SuspendNotifications()
        {
            _notificationSuspendCount++;

            // Fix #3：移除冗余的 count > 0 防御判断，NotificationScope 已保证 Dispose 幂等。
            return new NotificationScope(() =>
            {
                _notificationSuspendCount--;

                if (_notificationSuspendCount == 0 && _hasPendingChanges)
                {
                    _hasPendingChanges = false;
                    RaiseAllPropertiesChanged();
                }
            });
        }

        /// <summary>
        /// 触发指定属性的更改通知。
        /// </summary>
        /// <param name="propertyName">
        /// 属性名称。通常由编译器通过 <see cref="CallerMemberNameAttribute"/> 自动提供。
        /// </param>
        /// <remarks>
        /// 当处于通知暂停状态时，不会立即发出通知，而是仅记录存在待处理更改；
        /// 恢复通知后，会统一触发一次"所有属性已更改"的通知。
        /// </remarks>
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            if (_notificationSuspendCount > 0)
            {
                _hasPendingChanges = true;
                return;
            }

            RaisePropertyChanged(propertyName);
        }

        /// <summary>
        /// 设置字段值，并在值发生变化时执行附加回调与属性更改通知。
        /// </summary>
        /// <typeparam name="T">字段类型。</typeparam>
        /// <param name="field">要更新的字段引用。</param>
        /// <param name="value">新值。</param>
        /// <param name="propertyName">
        /// 属性名称。通常由编译器通过 <see cref="CallerMemberNameAttribute"/> 自动提供。
        /// </param>
        /// <param name="onChanged">
        /// 字段值更新后的附加回调。仅当值确实发生变化时才会执行，且在属性更改通知之前调用。
        /// </param>
        /// <returns>若字段值已更新则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        protected bool SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string? propertyName = null,
            Action? onChanged = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            onChanged?.Invoke(); // Fix #2
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// 触发"所有属性已更改"的通知。
        /// </summary>
        /// <remarks>
        /// 发出属性名为空字符串的 <see cref="PropertyChangedEventArgs"/>，
        /// 通知绑定系统当前对象的所有属性均应视为已更新。
        /// 适用于批量更新、整体刷新、配置重置等场景。
        /// </remarks>
        public void RaiseAllPropertiesChanged()
        {
            RaisePropertyChanged(string.Empty);
        }

        private void RaisePropertyChanged(string? propertyName)
        {
            var handler = PropertyChanged;
            if (handler == null)
                return;

            var args = new PropertyChangedEventArgs(propertyName);

            if (_dispatcher.CheckAccess())
            {
                handler(this, args);
            }
            else
            {
                // 在 BeginInvoke 内二次读取 PropertyChanged，确保以调度时的最新订阅者为准。
                _dispatcher.BeginInvoke(
                    DispatcherPriority.DataBind,
                    new Action(() => PropertyChanged?.Invoke(this, args)));
            }
        }

        private sealed class NotificationScope : IDisposable
        {
            private Action? _onDispose;

            public NotificationScope(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                var action = _onDispose;
                if (action != null)
                {
                    _onDispose = null;
                    action();
                }
            }
        }
    }
}
