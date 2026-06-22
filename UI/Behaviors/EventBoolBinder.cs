using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace KkjQuicker.UI.Behaviors
{
    /// <summary>
    /// 附加属性形式的 EventBool 绑定行为，无需引入第三方库。
    /// 将控件上的两个路由事件分别映射为 <see cref="IsActiveProperty"/> 的 <c>true</c> / <c>false</c>，
    /// 支持防抖。
    /// <para>
    /// 当多个控件的 <see cref="IsActiveProperty"/> 绑定到同一数据源的同一属性时，
    /// 它们将自动共享同一个防抖计时器。这使得"鼠标从按钮移入 Popup"等跨控件联动场景
    /// 能够正确工作：任意一个控件触发 TrueEvent，都会取消其他控件挂起的关闭计时。
    /// </para>
    /// <para>
    /// 若无法识别共享目标（如未使用标准绑定），则自动降级为每个控件独立的私有计时器。
    /// </para>
    /// <para>
    /// 注意：同一控件只支持附加一套 <see cref="EventBoolBinder"/>。
    /// </para>
    /// <para>
    /// 已知行为：共享计时器由 <see cref="System.Windows.Threading.DispatcherTimer"/> 实现，
    /// 在防抖窗口期内即使 ViewModel 已不可达，计时器也会在 Tick 后自然停止，不会持续泄漏。
    /// </para>
    /// </summary>
    /// <example>
    /// <code language="xml">
    /// &lt;!-- 两个控件绑定同一属性，自动共享计时器 --&gt;
    /// &lt;Button
    ///     behaviors:EventBoolBinder.TrueEventName="MouseEnter"
    ///     behaviors:EventBoolBinder.FalseEventName="MouseLeave"
    ///     behaviors:EventBoolBinder.IsActive="{Binding IsHelpOpen}"
    ///     behaviors:EventBoolBinder.DebounceMilliseconds="300"
    ///     behaviors:EventBoolBinder.DebounceOnlyFalse="True"/&gt;
    ///
    /// &lt;Grid
    ///     behaviors:EventBoolBinder.TrueEventName="MouseEnter"
    ///     behaviors:EventBoolBinder.FalseEventName="MouseLeave"
    ///     behaviors:EventBoolBinder.IsActive="{Binding IsHelpOpen}"
    ///     behaviors:EventBoolBinder.DebounceMilliseconds="300"
    ///     behaviors:EventBoolBinder.DebounceOnlyFalse="True"/&gt;
    /// </code>
    /// </example>
    public static class EventBoolBinder
    {
        #region 公开附加属性

        /// <summary>
        /// 触发时将 <see cref="IsActiveProperty"/> 设为 <c>true</c> 的路由事件名称，例如 <c>"MouseEnter"</c>。
        /// </summary>
        public static readonly DependencyProperty TrueEventNameProperty =
            DependencyProperty.RegisterAttached(
                "TrueEventName", typeof(string), typeof(EventBoolBinder),
                new PropertyMetadata(null, OnTrueEventNameChanged));

        /// <summary>获取 <see cref="TrueEventNameProperty"/>。</summary>
        public static string GetTrueEventName(DependencyObject obj) => (string)obj.GetValue(TrueEventNameProperty);
        /// <summary>设置 <see cref="TrueEventNameProperty"/>。</summary>
        public static void SetTrueEventName(DependencyObject obj, string value) => obj.SetValue(TrueEventNameProperty, value);

        /// <summary>
        /// 触发时将 <see cref="IsActiveProperty"/> 设为 <c>false</c> 的路由事件名称，例如 <c>"MouseLeave"</c>。
        /// </summary>
        public static readonly DependencyProperty FalseEventNameProperty =
            DependencyProperty.RegisterAttached(
                "FalseEventName", typeof(string), typeof(EventBoolBinder),
                new PropertyMetadata(null, OnFalseEventNameChanged));

        /// <summary>获取 <see cref="FalseEventNameProperty"/>。</summary>
        public static string GetFalseEventName(DependencyObject obj) => (string)obj.GetValue(FalseEventNameProperty);
        /// <summary>设置 <see cref="FalseEventNameProperty"/>。</summary>
        public static void SetFalseEventName(DependencyObject obj, string value) => obj.SetValue(FalseEventNameProperty, value);

        /// <summary>
        /// 与控件交互状态双向绑定的布尔值。默认支持 <c>TwoWay</c> 绑定。
        /// </summary>
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive", typeof(bool), typeof(EventBoolBinder),
                new FrameworkPropertyMetadata(false,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>获取 <see cref="IsActiveProperty"/>。</summary>
        public static bool GetIsActive(DependencyObject obj) => (bool)obj.GetValue(IsActiveProperty);
        /// <summary>设置 <see cref="IsActiveProperty"/>。</summary>
        public static void SetIsActive(DependencyObject obj, bool value) => obj.SetValue(IsActiveProperty, value);

        /// <summary>
        /// 防抖延迟时间（毫秒）。<c>0</c> 或负数均表示禁用防抖。
        /// </summary>
        public static readonly DependencyProperty DebounceMillisecondsProperty =
            DependencyProperty.RegisterAttached(
                "DebounceMilliseconds", typeof(int), typeof(EventBoolBinder),
                new PropertyMetadata(0));

        /// <summary>获取 <see cref="DebounceMillisecondsProperty"/>。</summary>
        public static int GetDebounceMilliseconds(DependencyObject obj) => (int)obj.GetValue(DebounceMillisecondsProperty);
        /// <summary>设置 <see cref="DebounceMillisecondsProperty"/>。</summary>
        public static void SetDebounceMilliseconds(DependencyObject obj, int value) => obj.SetValue(DebounceMillisecondsProperty, value);

        /// <summary>
        /// 是否仅对"设为 <c>false</c>"方向启用防抖，默认为 <c>true</c>。
        /// <para>
        /// <c>true</c>（默认）：TrueEvent 立即生效并取消挂起的防抖，FalseEvent 延迟执行。
        /// 适用于"即时显示、延迟隐藏"的悬停 Popup 场景。
        /// </para>
        /// <para>
        /// <c>false</c>：两个方向均受 <see cref="DebounceMillisecondsProperty"/> 约束，
        /// 最后触发的事件在计时到期后生效。
        /// </para>
        /// </summary>
        public static readonly DependencyProperty DebounceOnlyFalseProperty =
            DependencyProperty.RegisterAttached(
                "DebounceOnlyFalse", typeof(bool), typeof(EventBoolBinder),
                new PropertyMetadata(true));

        /// <summary>获取 <see cref="DebounceOnlyFalseProperty"/>。</summary>
        public static bool GetDebounceOnlyFalse(DependencyObject obj) => (bool)obj.GetValue(DebounceOnlyFalseProperty);
        /// <summary>设置 <see cref="DebounceOnlyFalseProperty"/>。</summary>
        public static void SetDebounceOnlyFalse(DependencyObject obj, bool value) => obj.SetValue(DebounceOnlyFalseProperty, value);

        #endregion

        #region 私有状态附加属性

        // 每个元素独立持有一个 BehaviorState 实例，存储订阅状态等运行时信息
        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "State", typeof(BehaviorState), typeof(EventBoolBinder),
                new PropertyMetadata(null));

        #endregion

        #region 共享计时器基础设施

        // key = ViewModel 实例（弱引用，ViewModel GC 后自动清理）
        // value = 该 ViewModel 下所有属性的共享计时器注册表
        private static readonly ConditionalWeakTable<object, SharedTimerRegistry> _sharedTimerRegistries
            = new ConditionalWeakTable<object, SharedTimerRegistry>();

        /// <summary>
        /// 共享计时器注册表，按「属性路径 + Dispatcher」索引，支持多窗口场景。
        /// <para>
        /// Dispatcher 使用引用相等（不覆写 GetHashCode/Equals），
        /// 多窗口环境下每个窗口拥有独立 Dispatcher 实例，因此自然隔离。
        /// </para>
        /// </summary>
        private class SharedTimerRegistry
        {
            // 仅在 UI 线程访问，无需线程安全
            private readonly Dictionary<(string, Dispatcher), SharedTimerState> _timers
                = new Dictionary<(string, Dispatcher), SharedTimerState>();

            public SharedTimerState GetOrCreate(string propertyPath, Dispatcher dispatcher)
            {
                var key = (propertyPath, dispatcher);
                if (!_timers.TryGetValue(key, out var state))
                {
                    state = new SharedTimerState(dispatcher);
                    _timers[key] = state;
                }
                return state;
            }
        }

        /// <summary>
        /// 可被多个 <see cref="BehaviorState"/> 共享的防抖计时器。
        /// 最后一次 <see cref="Schedule"/> 的 Action 在计时到期后执行；
        /// 任意持有方调用 <see cref="Cancel"/> 可取消待执行的动作。
        /// </summary>
        private class SharedTimerState
        {
            private readonly DispatcherTimer _timer;
            private Action _pendingAction;

            public SharedTimerState(Dispatcher dispatcher)
            {
                _timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
                _timer.Tick += OnTick;
            }

            /// <summary>重置计时并在 <paramref name="delayMs"/> 毫秒后执行 <paramref name="action"/>。</summary>
            public void Schedule(int delayMs, Action action)
            {
                _pendingAction = action;
                _timer.Interval = TimeSpan.FromMilliseconds(delayMs);
                _timer.Stop();
                _timer.Start();
            }

            /// <summary>取消待执行的动作并停止计时。</summary>
            public void Cancel()
            {
                _timer.Stop();
                _pendingAction = null;
            }

            private void OnTick(object? sender, EventArgs e)
            {
                _timer.Stop();
                var action = _pendingAction;
                _pendingAction = null;
                action?.Invoke();
            }
        }

        #endregion

        #region RoutedEvent 反射缓存

        // 拉平为单层 ConcurrentDictionary，彻底消除嵌套字典的线程安全隐患。
        // key = (控件类型, 事件名)；value = 对应的 RoutedEvent（查找失败时为 null）。
        // GetOrAdd 的工厂在极端并发下可能执行多次，但反射结果是确定性的，不影响正确性。
        private static readonly ConcurrentDictionary<(Type, string), RoutedEvent> _eventCache
            = new ConcurrentDictionary<(Type, string), RoutedEvent>();

        /// <summary>
        /// 通过反射在控件类型上查找名为 <c>{eventName}Event</c> 的静态 <see cref="RoutedEvent"/> 字段，
        /// 结果全局缓存，同类型 + 同事件名只反射一次。
        /// </summary>
        private static RoutedEvent FindRoutedEvent(FrameworkElement element, string eventName)
        {
            // Trim 防止 XAML 手误带入空格，避免缓存污染和反射查找失败
            if (string.IsNullOrWhiteSpace(eventName))
                return null;

            eventName = eventName.Trim();

            return _eventCache.GetOrAdd((element.GetType(), eventName), key =>
            {
                var field = key.Item1.GetField(
                    key.Item2 + "Event",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                var routedEvent = field?.GetValue(null) as RoutedEvent;

#if DEBUG
                if (routedEvent == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[EventBoolBinder] 未找到路由事件：{key.Item2}Event on {key.Item1.Name}，请检查事件名拼写。");
                }
#endif
                return routedEvent;
            });
        }

        #endregion

        #region 属性变更回调

        private static void OnTrueEventNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement element)) return;
            EnsureState(element).ResubscribeTrue(element, (string)e.OldValue);
        }

        private static void OnFalseEventNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is FrameworkElement element)) return;
            EnsureState(element).ResubscribeFalse(element, (string)e.OldValue);
        }

        private static BehaviorState EnsureState(FrameworkElement element)
        {
            var state = (BehaviorState)element.GetValue(StateProperty);
            if (state != null) return state;

            state = new BehaviorState();
            element.SetValue(StateProperty, state);

            element.Unloaded += OnElementUnloaded;
            element.Loaded += OnElementLoaded;

            return state;
        }

        private static void OnElementUnloaded(object? sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            ((BehaviorState)element.GetValue(StateProperty))?.Cleanup(element);
        }

        private static void OnElementLoaded(object? sender, RoutedEventArgs e)
        {
            var element = (FrameworkElement)sender;
            ((BehaviorState)element.GetValue(StateProperty))?.Restore(element);
        }

        #endregion

        #region 事件处理辅助

        private static void AddHandler(FrameworkElement element, string eventName, RoutedEventHandler handler)
        {
            var re = FindRoutedEvent(element, eventName);
            if (re != null) element.AddHandler(re, handler);
        }

        private static void RemoveHandler(FrameworkElement element, string eventName, RoutedEventHandler handler)
        {
            var re = FindRoutedEvent(element, eventName);
            if (re != null) element.RemoveHandler(re, handler);
        }

        #endregion

        #region BehaviorState（每个元素独立的运行时状态）

        private class BehaviorState
        {
            // 计时器实例：懒解析，Cleanup 后置 null 强制下次 Trigger 时重新解析。
            // 若绑定目标可识别则与其他控件共享同一实例（_isSharedTimer = true）；
            // 否则为私有实例（_isSharedTimer = false）。
            private SharedTimerState _timer = null!;
            private bool _isSharedTimer;

            private RoutedEventHandler _trueHandler = null!;
            private RoutedEventHandler _falseHandler = null!;

            // 记录当前实际订阅的事件名；null 表示未订阅
            private string _subscribedTrueEventName = null!;
            private string _subscribedFalseEventName = null!;

            // 处理器在首次订阅时按需创建，避免在 BehaviorState 构造时捕获 element
            private RoutedEventHandler GetTrueHandler(FrameworkElement element)
            {
                if (_trueHandler == null)
                    _trueHandler = (s, e) => Trigger(element, true);
                return _trueHandler;
            }

            private RoutedEventHandler GetFalseHandler(FrameworkElement element)
            {
                if (_falseHandler == null)
                    _falseHandler = (s, e) => Trigger(element, false);
                return _falseHandler;
            }

            /// <summary>
            /// 懒解析计时器。在 Trigger 时调用（元素已在可视树中，绑定已解析），确保解析成功率。
            /// Cleanup 后 _timer 置 null，下次 Trigger 重新解析，支持元素复用。
            /// </summary>
            private SharedTimerState GetTimer(FrameworkElement element)
            {
                if (_timer != null) return _timer;

                var expr = BindingOperations.GetBindingExpression(element, IsActiveProperty);
                var source = expr?.ResolvedSource;
                var path = expr?.ParentBinding?.Path?.Path;

                if (source != null && !string.IsNullOrEmpty(path))
                {
                    var registry = _sharedTimerRegistries.GetValue(source, _ => new SharedTimerRegistry());
                    _timer = registry.GetOrCreate(path, element.Dispatcher);
                    _isSharedTimer = true;
                }
                else
                {
                    // 降级：无法识别共享目标，使用私有计时器
                    _timer = new SharedTimerState(element.Dispatcher);
                    _isSharedTimer = false;

#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        "[EventBoolBinder] IsActive 绑定目标无法识别，使用私有计时器。" +
                        $" 控件：{element.GetType().Name}。" +
                        " 若需跨控件共享防抖，请确保 IsActive 使用标准 Binding 且 ResolvedSource 非 null。");
#endif
                }

                return _timer;
            }

            /// <summary>仅重订阅 True 侧，由调用方传入旧事件名以正确取消。</summary>
            public void ResubscribeTrue(FrameworkElement element, string oldEventName)
            {
                // 优先以自身记录的已订阅名称为准，oldEventName 作为 fallback（首次设置时 _subscribedTrueEventName 为 null）
                var nameToRemove = _subscribedTrueEventName ?? oldEventName;
                if (!string.IsNullOrEmpty(nameToRemove))
                {
                    RemoveHandler(element, nameToRemove, GetTrueHandler(element));
                    _subscribedTrueEventName = null;
                }
                string newName = GetTrueEventName(element);
                if (!string.IsNullOrEmpty(newName))
                {
                    AddHandler(element, newName, GetTrueHandler(element));
                    _subscribedTrueEventName = newName;
                }
            }

            /// <summary>仅重订阅 False 侧，由调用方传入旧事件名以正确取消。</summary>
            public void ResubscribeFalse(FrameworkElement element, string oldEventName)
            {
                if (!string.IsNullOrEmpty(oldEventName))
                {
                    RemoveHandler(element, oldEventName, GetFalseHandler(element));
                    _subscribedFalseEventName = null;
                }
                string newName = GetFalseEventName(element);
                if (!string.IsNullOrEmpty(newName))
                {
                    AddHandler(element, newName, GetFalseHandler(element));
                    _subscribedFalseEventName = newName;
                }
            }

            /// <summary>
            /// 元素离开可视树时取消事件订阅，并按计时器类型决定是否取消防抖动作。
            /// <para>
            /// 私有计时器：取消——元素已离开，无需执行。
            /// 共享计时器：不取消——不干涉同组其他控件已排队的防抖动作。
            /// </para>
            /// </summary>
            public void Cleanup(FrameworkElement element)
            {
                // 私有计时器：自己取消；共享计时器：不干涉其他控件的排队动作
                if (!_isSharedTimer)
                    _timer?.Cancel();

                _timer = null;
                _isSharedTimer = false;

                if (!string.IsNullOrEmpty(_subscribedTrueEventName))
                {
                    RemoveHandler(element, _subscribedTrueEventName, GetTrueHandler(element));
                    _subscribedTrueEventName = null;
                }

                if (!string.IsNullOrEmpty(_subscribedFalseEventName))
                {
                    RemoveHandler(element, _subscribedFalseEventName, GetFalseHandler(element));
                    _subscribedFalseEventName = null;
                }
            }

            /// <summary>
            /// 元素重新进入可视树时，从附加属性读取当前配置并恢复事件订阅。
            /// 仅在 <see cref="Cleanup"/> 已清除订阅状态的情况下才重新订阅，
            /// 避免属性在加载前设置时与 <see cref="ResubscribeTrue"/>/<see cref="ResubscribeFalse"/>
            /// 产生重复订阅。
            /// </summary>
            public void Restore(FrameworkElement element)
            {
                // _subscribedXxxEventName 非 null 说明 Cleanup 未被调用（属性在加载前已订阅），
                // 无需重复订阅；null 说明 Cleanup 已清理，需要恢复。
                if (string.IsNullOrEmpty(_subscribedTrueEventName))
                {
                    var trueName = GetTrueEventName(element);
                    if (!string.IsNullOrEmpty(trueName))
                    {
                        AddHandler(element, trueName, GetTrueHandler(element));
                        _subscribedTrueEventName = trueName;
                    }
                }

                if (string.IsNullOrEmpty(_subscribedFalseEventName))
                {
                    var falseName = GetFalseEventName(element);
                    if (!string.IsNullOrEmpty(falseName))
                    {
                        AddHandler(element, falseName, GetFalseHandler(element));
                        _subscribedFalseEventName = falseName;
                    }
                }
            }

            private void Trigger(FrameworkElement element, bool value)
            {
                int delay = Math.Max(0, GetDebounceMilliseconds(element));
                // DebounceOnlyFalse=true 时：value=true 立即生效，value=false 走防抖
                bool needDebounce = delay > 0 && (!GetDebounceOnlyFalse(element) || !value);

                var timer = GetTimer(element);

                if (needDebounce)
                {
                    // IsLoaded 防护：若元素在计时期间被卸载，跳过写入，避免对离开可视树的元素回写绑定
                    timer.Schedule(delay, () => { if (element.IsLoaded) SetIsActive(element, value); });
                }
                else
                {
                    // 取消同组其他控件挂起的防抖动作（共享计时器的核心价值）
                    timer.Cancel();
                    SetIsActive(element, value);
                }
            }
        }

        #endregion
    }
}
