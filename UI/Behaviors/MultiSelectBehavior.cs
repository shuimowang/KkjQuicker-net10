using System;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace KkjQuicker.UI.Behaviors
{
    /// <summary>
    /// 为 <see cref="ListBox"/> 和 HandyControl <see cref="HandyControl.Controls.CheckComboBox"/>
    /// 提供可绑定的 SelectedItems 附加属性，支持双向同步。
    /// </summary>
    /// <remarks>
    /// 绑定集合若实现 <see cref="INotifyCollectionChanged"/>（如
    /// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>），
    /// ViewModel 端的增删操作将自动同步到 UI，无需重新赋值整个集合。
    /// <para>
    /// <see cref="ListBox.SelectionMode"/> 必须为
    /// <see cref="SelectionMode.Multiple"/> 或 <see cref="SelectionMode.Extended"/>。
    /// </para>
    /// </remarks>
    public static class MultiSelectBehavior
    {
        #region SelectedItems 附加属性

        /// <summary>获取控件绑定的选中项集合。</summary>
        public static IList GetSelectedItems(DependencyObject obj)
            => (IList)obj.GetValue(SelectedItemsProperty);

        /// <summary>设置控件绑定的选中项集合。</summary>
        public static void SetSelectedItems(DependencyObject obj, IList value)
            => obj.SetValue(SelectedItemsProperty, value);

        /// <summary>标识 <c>SelectedItems</c> 附加属性。</summary>
        public static readonly DependencyProperty SelectedItemsProperty =
            DependencyProperty.RegisterAttached(
                "SelectedItems",
                typeof(IList),
                typeof(MultiSelectBehavior),
                new PropertyMetadata(null, OnSelectedItemsChanged));

        #endregion

        #region 集合变更订阅管理

        // 以控件为弱键存储集合变更处理器，用于切换绑定时正确取消旧订阅
        private static readonly ConditionalWeakTable<DependencyObject, HandlerToken>
            _collectionTokens = new ConditionalWeakTable<DependencyObject, HandlerToken>();

        private sealed class HandlerToken
        {
            public EventHandler<NotifyCollectionChangedEventArgs>? Handler { get; set; }
        }

        #endregion

        #region 内部实现

        private static void OnSelectedItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!IsSupported(d)) return;

            var lb = (ListBox)d;
            if (lb.SelectionMode == SelectionMode.Single)
                throw new InvalidOperationException(
                    "MultiSelectBehavior.SelectedItems 要求 ListBox.SelectionMode 为 Multiple 或 Extended。");

            // 立即解除 SelectionChanged 订阅，防止窗口期内以旧 UI 状态向新集合写入脏数据
            DetachSelectionChanged(d);

            // 取消旧集合的变更订阅
            var oldIncc = e.OldValue as INotifyCollectionChanged;
            if (oldIncc != null)
            {
                HandlerToken? token;
                if (_collectionTokens.TryGetValue(d, out token))
                {
                    CollectionChangedEventManager.RemoveHandler(oldIncc, token.Handler);
                    _collectionTokens.Remove(d);
                }
            }

            // 订阅新集合的变更通知
            // CollectionChangedEventManager 对 handler.Target（闭包对象）持弱引用；
            // ConditionalWeakTable 以控件为弱键强持闭包，两者配合：
            // 控件存活 → 闭包存活 → 订阅有效；控件被 GC → 闭包随之释放 → 订阅自动失效。
            var newIncc = e.NewValue as INotifyCollectionChanged;
            if (newIncc != null)
            {
                var captured = d;
                EventHandler<NotifyCollectionChangedEventArgs> handler =
                    (s, args) => OnBoundCollectionChanged(captured, args);
                _collectionTokens.Add(d, new HandlerToken { Handler = handler });
                CollectionChangedEventManager.AddHandler(newIncc, handler);
            }

            // 全量刷新 UI 选中状态；无绑定集合时不订阅 SelectionChanged
            lb.Dispatcher.InvokeAsync(() =>
            {
                DetachSelectionChanged(d);
                ApplyFullSelection(d, e.NewValue as IList);
                if (e.NewValue != null)
                    AttachSelectionChanged(d);
            }, DispatcherPriority.Loaded);
        }

        private static void OnBoundCollectionChanged(DependencyObject d, NotifyCollectionChangedEventArgs e)
        {
            ((ListBox)d).Dispatcher.InvokeAsync(() =>
            {
                DetachSelectionChanged(d);

                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        if (e.NewItems != null)
                            foreach (var item in e.NewItems)
                                SetItemSelected(d, item, true);
                        break;

                    case NotifyCollectionChangedAction.Remove:
                        if (e.OldItems != null)
                            foreach (var item in e.OldItems)
                                SetItemSelected(d, item, false);
                        break;

                    case NotifyCollectionChangedAction.Replace:
                        if (e.OldItems != null)
                            foreach (var item in e.OldItems)
                                SetItemSelected(d, item, false);
                        if (e.NewItems != null)
                            foreach (var item in e.NewItems)
                                SetItemSelected(d, item, true);
                        break;

                    case NotifyCollectionChangedAction.Reset:
                        // Clear() 等操作触发 Reset，无法获取旧项，只能全量同步
                        ApplyFullSelection(d, GetSelectedItems(d));
                        break;

                        // Move 不影响选中状态，忽略
                }

                AttachSelectionChanged(d);
            }, DispatcherPriority.Loaded);
        }

        private static void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var d = sender as DependencyObject;
            if (d == null) return;
            var boundList = GetSelectedItems(d);
            if (boundList == null) return;

            foreach (var item in e.RemovedItems)
                boundList.Remove(item);
            foreach (var item in e.AddedItems)
                if (!boundList.Contains(item))
                    boundList.Add(item);
        }

        /// <summary>全量将 <paramref name="targetList"/> 中的项应用到控件选中状态。</summary>
        private static void ApplyFullSelection(DependencyObject d, IList? targetList)
        {
            var lb = (ListBox)d;
            lb.UnselectAll();
            if (targetList != null)
                foreach (var item in targetList)
                    lb.SelectedItems.Add(item);
        }

        /// <summary>对单个数据项增量设置选中状态。</summary>
        private static void SetItemSelected(DependencyObject d, object item, bool selected)
        {
            var lb = (ListBox)d;
            if (selected)
            {
                if (!lb.SelectedItems.Contains(item))
                    lb.SelectedItems.Add(item);
            }
            else
            {
                lb.SelectedItems.Remove(item);
            }
        }

        // CheckComboBox 继承自 ListBox，d is ListBox 已覆盖全部支持类型
        private static bool IsSupported(DependencyObject d)
            => d is ListBox;

        private static void AttachSelectionChanged(DependencyObject d)
            => ((ListBox)d).SelectionChanged += OnSelectionChanged;

        private static void DetachSelectionChanged(DependencyObject d)
            => ((ListBox)d).SelectionChanged -= OnSelectionChanged;

        #endregion
    }
}