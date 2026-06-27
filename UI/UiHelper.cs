using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供常用 WPF UI 辅助方法，聚焦视觉树查找、数据上下文反查、ItemsControl 定位和元素边界计算。
    /// </summary>
    public static class UiHelper
    {
        private static IEnumerable<DependencyObject> TraverseVisualTree(DependencyObject? root)
        {
            if (root == null)
                yield break;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                yield return current;

                int childCount;
                try
                {
                    childCount = VisualTreeHelper.GetChildrenCount(current);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                for (int i = 0; i < childCount; i++)
                {
                    DependencyObject child;
                    try
                    {
                        child = VisualTreeHelper.GetChild(current, i);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    queue.Enqueue(child);
                }
            }
        }

        /// <summary>
        /// 沿视觉树优先、逻辑树兜底的路径向上查找指定类型的祖先元素。
        /// </summary>
        public static T? FindAncestor<T>(DependencyObject? current, bool includeSelf = true)
            where T : DependencyObject
        {
            if (current == null)
                return null;

            if (includeSelf && current is T self)
                return self;

            DependencyObject? parent = GetParentObject(current);
            while (parent != null)
            {
                if (parent is T found)
                    return found;

                parent = GetParentObject(parent);
            }

            return null;
        }

        /// <summary>
        /// 沿逻辑树向上查找指定类型的祖先元素。
        /// </summary>
        public static T? FindLogicalAncestor<T>(DependencyObject? current, bool includeSelf = true)
            where T : DependencyObject
        {
            if (current == null)
                return null;

            if (includeSelf && current is T self)
                return self;

            DependencyObject? parent = LogicalTreeHelper.GetParent(current);
            while (parent != null)
            {
                if (parent is T found)
                    return found;

                parent = LogicalTreeHelper.GetParent(parent);
            }

            return null;
        }

        /// <summary>
        /// 在模板场景中向上查找第一个指定类型的模板宿主元素。
        /// </summary>
        public static T? FindTemplatedParent<T>(DependencyObject? element)
            where T : DependencyObject
        {
            DependencyObject? current = element;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.TemplatedParent is T matched)
                    return matched;

                current = GetParentObject(current);
            }

            return null;
        }

        /// <summary>
        /// 在视觉树中查找第一个指定类型的元素，包含起始元素自身。
        /// </summary>
        public static T? FindChild<T>(DependencyObject? reference)
            where T : DependencyObject
        {
            if (reference == null)
                return null;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(reference);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                if (current is T matched)
                    return matched;

                EnqueueVisualChildren(current, queue);
            }

            return null;
        }

        /// <summary>
        /// 在视觉树中查找第一个满足条件的指定类型元素，包含起始元素自身。
        /// </summary>
        public static T? FindChild<T>(DependencyObject? reference, Func<T, bool> predicate)
            where T : DependencyObject
        {
            ArgumentNullException.ThrowIfNull(predicate);

            if (reference == null)
                return null;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(reference);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                if (current is T matched && predicate(matched))
                    return matched;

                EnqueueVisualChildren(current, queue);
            }

            return null;
        }

        /// <summary>
        /// 在视觉树中查找所有指定类型的元素，包含起始元素自身。
        /// </summary>
        public static IEnumerable<T> FindAllChildren<T>(DependencyObject? reference)
            where T : DependencyObject
        {
            foreach (DependencyObject current in TraverseVisualTree(reference))
            {
                if (current is T matched)
                    yield return matched;
            }
        }

        public static List<T> FindAllChildrenSnapshot<T>(DependencyObject? reference)
            where T : DependencyObject
        {
            var result = new List<T>();

            foreach (DependencyObject current in TraverseVisualTree(reference))
            {
                if (current is T matched)
                    result.Add(matched);
            }

            return result;
        }

        /// <summary>
        /// 获取指定元素的直接视觉子元素。
        /// </summary>
        public static IEnumerable<DependencyObject> GetVisualChildren(DependencyObject? parent)
        {
            if (parent == null)
                yield break;

            int count;
            try
            {
                count = VisualTreeHelper.GetChildrenCount(parent);
            }
            catch (InvalidOperationException)
            {
                yield break;
            }

            for (int i = 0; i < count; i++)
            {
                DependencyObject child;
                try
                {
                    child = VisualTreeHelper.GetChild(parent, i);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                yield return child;
            }
        }

        /// <summary>
        /// 从 RoutedEventArgs.OriginalSource 向上查找指定类型的祖先元素。
        /// </summary>
        public static T? FindOriginalSourceAncestor<T>(RoutedEventArgs? e, bool includeSelf = true)
            where T : DependencyObject
        {
            return e?.OriginalSource is DependencyObject source
                ? FindAncestor<T>(source, includeSelf)
                : null;
        }

        /// <summary>
        /// 从 RoutedEventArgs.OriginalSource 反查第一个匹配类型的数据上下文。
        /// </summary>
        public static T? GetDataContextFromOriginalSource<T>(RoutedEventArgs? e)
            where T : class
        {
            DependencyObject? current = e?.OriginalSource as DependencyObject;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is T feData)
                    return feData;

                if (current is FrameworkContentElement fce && fce.DataContext is T fceData)
                    return fceData;

                current = GetParentObject(current);
            }

            return null;
        }

        /// <summary>
        /// 判断指定屏幕坐标是否命中目标元素。
        /// </summary>
        public static bool IsMouseOverElement(UIElement? element, Point screenPoint)
        {
            return HitTestScreenPoint(element, screenPoint);
        }

        public static bool HitTestScreenPoint(UIElement? element, Point screenPoint)
        {
            if (element == null)
                return false;

            try
            {
                Point point = element.PointFromScreen(screenPoint);
                return VisualTreeHelper.HitTest(element, point) != null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>
        /// 在 ItemsControl 中按指定属性值查找并定位第一项匹配的数据项。
        /// </summary>
        public static bool SelectItemByProperty<T>(
            this ItemsControl? itemsControl,
            Func<T, object?>? propertySelector,
            object? value)
        {
            if (itemsControl == null || propertySelector == null)
                return false;

            foreach (T item in itemsControl.Items.OfType<T>())
            {
                if (!Equals(propertySelector(item), value))
                    continue;

                if (itemsControl is Selector selector)
                    selector.SelectedItem = item;

                ScrollItemIntoView(itemsControl, item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 在 ItemsControl 中按条件查找并定位第一项匹配的数据项。
        /// </summary>
        public static bool SelectItemWhere<T>(this ItemsControl? itemsControl, Func<T, bool>? predicate)
        {
            if (itemsControl == null || predicate == null)
                return false;

            foreach (T item in itemsControl.Items.OfType<T>())
            {
                if (!predicate(item))
                    continue;

                if (itemsControl is Selector selector)
                    selector.SelectedItem = item;

                ScrollItemIntoView(itemsControl, item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取指定数据项对应的容器。若容器尚未生成，会先滚动到该项并刷新布局后重试。
        /// </summary>
        public static TContainer? GetItemContainer<TContainer>(this ItemsControl? itemsControl, object? item)
            where TContainer : DependencyObject
        {
            if (itemsControl == null || item == null)
                return null;

            DependencyObject? container = itemsControl.ItemContainerGenerator.ContainerFromItem(item);
            if (container != null)
                return container as TContainer;

            ScrollItemIntoView(itemsControl, item);
            itemsControl.UpdateLayout();

            return itemsControl.ItemContainerGenerator.ContainerFromItem(item) as TContainer;
        }

        public static async Task<TContainer?> GetItemContainerAsync<TContainer>(
            this ItemsControl? itemsControl,
            object? item,
            DispatcherPriority priority = DispatcherPriority.Loaded)
            where TContainer : DependencyObject
        {
            if (itemsControl == null || item == null)
                return null;

            if (!itemsControl.Dispatcher.CheckAccess())
            {
                return await itemsControl.Dispatcher.InvokeAsync(
                    () => itemsControl.GetItemContainer<TContainer>(item),
                    priority);
            }

            TContainer? container = itemsControl.GetItemContainer<TContainer>(item);
            if (container != null)
                return container;

            ScrollItemIntoView(itemsControl, item);
            await itemsControl.Dispatcher.InvokeAsync(() => { }, priority);
            itemsControl.UpdateLayout();

            return itemsControl.ItemContainerGenerator.ContainerFromItem(item) as TContainer;
        }

        /// <summary>
        /// 安全地将焦点设置到指定元素。
        /// </summary>
        public static bool SetFocus(UIElement? element)
        {
            if (element == null || !element.Focusable || !element.IsEnabled || !element.IsVisible)
                return false;

            return element.Focus();
        }

        /// <summary>
        /// 查找并聚焦到第一个可聚焦的可视子元素。
        /// </summary>
        public static bool FocusFirstChild(DependencyObject? parent)
        {
            if (parent == null)
                return false;

            var queue = new Queue<DependencyObject>();
            EnqueueVisualChildren(parent, queue);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                if (current is UIElement element && SetFocus(element))
                    return true;

                EnqueueVisualChildren(current, queue);
            }

            return false;
        }

        /// <summary>
        /// 获取元素在屏幕坐标系中的边界矩形。
        /// </summary>
        public static Rect GetScreenBounds(this FrameworkElement? element)
        {
            if (element == null || !element.IsLoaded)
                return Rect.Empty;

            try
            {
                Point p1 = element.PointToScreen(new Point(0, 0));
                Point p2 = element.PointToScreen(new Point(element.ActualWidth, 0));
                Point p3 = element.PointToScreen(new Point(0, element.ActualHeight));
                Point p4 = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));

                double minX = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
                double minY = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
                double maxX = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
                double maxY = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));

                return new Rect(new Point(minX, minY), new Point(maxX, maxY));
            }
            catch (InvalidOperationException)
            {
                return Rect.Empty;
            }
        }

        /// <summary>
        /// 获取元素相对于指定祖先视觉对象的边界矩形。
        /// </summary>
        public static Rect GetBoundsRelativeTo(this FrameworkElement? element, Visual? ancestor)
        {
            if (element == null || ancestor == null)
                return Rect.Empty;

            try
            {
                GeneralTransform transform = element.TransformToAncestor(ancestor);
                return transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                return Rect.Empty;
            }
        }

        /// <summary>
        /// 判断元素是否至少部分位于所属 ScrollViewer 的可视区域内。
        /// </summary>
        public static bool IsVisibleInScrollViewer(this FrameworkElement? element)
        {
            if (element == null || !element.IsVisible)
                return false;

            ScrollViewer? scrollViewer = FindAncestor<ScrollViewer>(element, includeSelf: false);
            if (scrollViewer == null)
                return element.IsVisible;

            try
            {
                Rect elementBounds = element.TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                var viewportBounds = new Rect(0, 0, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);

                return viewportBounds.IntersectsWith(elementBounds);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static void ScrollItemIntoView(ItemsControl itemsControl, object item)
        {
            switch (itemsControl)
            {
                case ListView listView:
                    listView.ScrollIntoView(item);
                    break;
                case ListBox listBox:
                    listBox.ScrollIntoView(item);
                    break;
                case DataGrid dataGrid:
                    dataGrid.ScrollIntoView(item);
                    break;
                case ComboBox { IsDropDownOpen: true } comboBox:
                    if (comboBox.ItemContainerGenerator.ContainerFromItem(item) is ComboBoxItem comboBoxItem)
                        comboBoxItem.BringIntoView();
                    break;
            }
        }

        private static void EnqueueVisualChildren(DependencyObject parent, Queue<DependencyObject> queue)
        {
            int count;
            try
            {
                count = VisualTreeHelper.GetChildrenCount(parent);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    queue.Enqueue(VisualTreeHelper.GetChild(parent, i));
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        private static DependencyObject? GetParentObject(DependencyObject? current)
        {
            if (current == null)
                return null;

            try
            {
                DependencyObject? visualParent = VisualTreeHelper.GetParent(current);
                if (visualParent != null)
                    return visualParent;
            }
            catch (InvalidOperationException)
            {
            }

            if (current is FrameworkElement fe)
                return fe.Parent;

            if (current is FrameworkContentElement fce)
                return fce.Parent;

            return null;
        }
    }
}
