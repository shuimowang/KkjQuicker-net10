using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace KkjQuicker.UI
{
    /// <summary>
    /// 提供常用的 WPF UI 辅助方法。
    /// </summary>
    /// <remarks>
    /// <para>本类聚焦于以下高频场景：</para>
    /// <list type="bullet">
    /// <item><description>可视树 / 逻辑树查找与遍历。</description></item>
    /// <item><description>从 <see cref="RoutedEventArgs.OriginalSource"/> 反查宿主控件、项容器与数据上下文。</description></item>
    /// <item><description>ItemsControl 项定位与容器获取。</description></item>
    /// <item><description>焦点、绑定清理、界面刷新与边界计算。</description></item>
    /// </list>
    /// <para>该类型为无状态静态工具类，适合在 WPF 与 Quicker CustomWindow 场景中直接使用。</para>
    /// </remarks>
    public static class UiHelper
    {
        #region Tree Traversal Core (private)

        private static IEnumerable<DependencyObject> TraverseVisualTree(DependencyObject root)
        {
            if (root == null)
                yield break;

            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                DependencyObject current = queue.Dequeue();
                yield return current;

                int childCount = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < childCount; i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(current, i);
                    if (child != null)
                        queue.Enqueue(child);
                }
            }
        }

        #endregion

        #region Ancestor Search

        /// <summary>
        /// 在可视树中向上查找指定类型的祖先元素。
        /// </summary>
        /// <typeparam name="T">目标祖先类型。</typeparam>
        /// <param name="current">起始元素。</param>
        /// <param name="includeSelf">是否包含起始元素自身。默认值为 <c>true</c>。</param>
        /// <returns>找到的第一个匹配祖先；若未找到则返回 <c>null</c>。</returns>
        /// <remarks>
        /// 内部通过 <see cref="VisualTreeHelper.GetParent"/> 优先遍历可视树，
        /// 对 <see cref="ContentElement"/>（如 <c>Run</c>、<c>Hyperlink</c>）
        /// 自动降级为逻辑树父节点，确保在混合树结构中行为一致。
        /// </remarks>
        public static T FindAncestor<T>(DependencyObject current, bool includeSelf = true)
            where T : DependencyObject
        {
            if (current == null)
                return null;

            if (includeSelf && current is T self)
                return self;

            DependencyObject parent = GetParentObject(current);
            while (parent != null)
            {
                if (parent is T found)
                    return found;

                parent = GetParentObject(parent);
            }

            return null;
        }

        /// <summary>
        /// 在逻辑树中向上查找指定类型的祖先元素。
        /// </summary>
        /// <typeparam name="T">目标祖先类型。</typeparam>
        /// <param name="current">起始元素。</param>
        /// <param name="includeSelf">是否包含起始元素自身。默认值为 <c>true</c>。</param>
        /// <returns>找到的第一个匹配祖先；若未找到则返回 <c>null</c>。</returns>
        public static T FindLogicalAncestor<T>(DependencyObject current, bool includeSelf = true)
            where T : DependencyObject
        {
            if (current == null)
                return null;

            if (includeSelf && current is T self)
                return self;

            DependencyObject parent = LogicalTreeHelper.GetParent(current);
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
        /// <typeparam name="T">目标宿主类型。</typeparam>
        /// <param name="element">起始元素。</param>
        /// <returns>找到的第一个模板宿主；若未找到则返回 <c>null</c>。</returns>
        /// <remarks>
        /// 优先利用 <see cref="FrameworkElement.TemplatedParent"/>，
        /// 适合在 ControlTemplate / DataTemplate 内部元素反查宿主控件时使用。
        /// </remarks>
        public static T FindTemplatedParent<T>(DependencyObject element)
            where T : DependencyObject
        {
            DependencyObject current = element;
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.TemplatedParent is T matched)
                    return matched;

                current = GetParentObject(current);
            }

            return null;
        }

        #endregion

        #region Descendant Search

        /// <summary>
        /// 在可视树中查找指定类型的第一个匹配元素。
        /// </summary>
        /// <typeparam name="T">目标元素类型。</typeparam>
        /// <param name="reference">起始元素。搜索结果包含自身。</param>
        /// <returns>找到的第一个匹配元素；若未找到则返回 <c>null</c>。</returns>
        public static T FindChild<T>(DependencyObject reference) where T : DependencyObject
        {
            return TraverseVisualTree(reference).OfType<T>().FirstOrDefault();
        }

        /// <summary>
        /// 在可视树中查找满足条件的第一个指定类型元素。
        /// </summary>
        /// <typeparam name="T">目标元素类型。</typeparam>
        /// <param name="reference">起始元素。搜索结果包含自身。</param>
        /// <param name="predicate">匹配条件。</param>
        /// <returns>找到的第一个匹配元素；若未找到则返回 <c>null</c>。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="predicate"/> 为 <c>null</c>。</exception>
        public static T FindChild<T>(DependencyObject reference, Func<T, bool> predicate)
            where T : DependencyObject
        {
            if (predicate == null)
                throw new ArgumentNullException(nameof(predicate));

            return TraverseVisualTree(reference).OfType<T>().FirstOrDefault(predicate);
        }

        /// <summary>
        /// 在可视树中查找所有指定类型的匹配元素。
        /// </summary>
        /// <typeparam name="T">目标元素类型。</typeparam>
        /// <param name="reference">起始元素。搜索结果包含自身。</param>
        /// <returns>所有匹配元素；若无匹配项则返回空序列。</returns>
        public static IEnumerable<T> FindAllChildren<T>(DependencyObject reference)
            where T : DependencyObject
        {
            return TraverseVisualTree(reference).OfType<T>();
        }

        /// <summary>
        /// 获取指定元素的直接可视子元素。
        /// </summary>
        /// <param name="parent">父元素。</param>
        /// <returns>直接子元素序列；若无子元素则返回空序列。</returns>
        public static IEnumerable<DependencyObject> GetVisualChildren(DependencyObject parent)
        {
            if (parent == null)
                yield break;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child != null)
                    yield return child;
            }
        }

        /// <summary>
        /// 获取当前元素所在可视树的根节点。
        /// </summary>
        /// <param name="element">起始元素。</param>
        /// <returns>可视树根节点；若 <paramref name="element"/> 为 <c>null</c> 则返回 <c>null</c>。</returns>
        /// <remarks>
        /// 内部仅使用 <see cref="VisualTreeHelper.GetParent"/> 遍历，不降级到逻辑树。
        /// 若传入 <see cref="ContentElement"/>（如 <c>Run</c>、<c>Hyperlink</c>）等
        /// 非 <see cref="Visual"/>/<see cref="System.Windows.Media.Media3D.Visual3D"/> 元素，
        /// 将直接返回传入元素自身。
        /// </remarks>
        public static DependencyObject FindVisualRoot(DependencyObject element)
        {
            if (element == null)
                return null;

            DependencyObject current = element;
            DependencyObject parent;
            try
            {
                parent = VisualTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                // 非 Visual/Visual3D 元素，无可视父级，按文档约定返回自身。
                return current;
            }

            while (parent != null)
            {
                current = parent;
                parent = VisualTreeHelper.GetParent(current);
            }

            return current;
        }

        #endregion

        #region OriginalSource Helper

        /// <summary>
        /// 从 <see cref="RoutedEventArgs.OriginalSource"/> 向上查找指定类型的祖先元素。
        /// </summary>
        /// <typeparam name="T">目标祖先类型。</typeparam>
        /// <param name="e">路由事件参数。</param>
        /// <param name="includeSelf">是否包含原始事件源自身。默认值为 <c>true</c>。</param>
        /// <returns>找到的第一个匹配祖先；若未找到则返回 <c>null</c>。</returns>
        public static T FindOriginalSourceAncestor<T>(RoutedEventArgs e, bool includeSelf = true)
            where T : DependencyObject
        {
            if (e == null)
                return null;

            DependencyObject source = e.OriginalSource as DependencyObject;
            return source == null ? null : FindAncestor<T>(source, includeSelf);
        }

        /// <summary>
        /// 从 <see cref="RoutedEventArgs.OriginalSource"/> 反查第一个匹配类型的数据上下文。
        /// </summary>
        /// <typeparam name="T">目标数据类型。</typeparam>
        /// <param name="e">路由事件参数。</param>
        /// <returns>找到的第一个匹配数据上下文；若未找到则返回 <c>null</c>。</returns>
        /// <remarks>
        /// 同时覆盖 <see cref="FrameworkElement.DataContext"/> 与
        /// <see cref="FrameworkContentElement.DataContext"/>，适用于由文本元素
        /// （如 <c>Hyperlink</c>、<c>Run</c>）触发的路由事件场景。
        /// </remarks>
        public static T GetDataContextFromOriginalSource<T>(RoutedEventArgs e) where T : class
        {
            if (e == null)
                return null;

            DependencyObject current = e.OriginalSource as DependencyObject;
            while (current != null)
            {
                FrameworkElement fe = current as FrameworkElement;
                if (fe != null && fe.DataContext is T feData)
                    return feData;

                FrameworkContentElement fce = current as FrameworkContentElement;
                if (fce != null && fce.DataContext is T fceData)
                    return fceData;

                current = GetParentObject(current);
            }

            return null;
        }

        #endregion

        #region Mouse Hit Test

        /// <summary>
        /// 判断指定屏幕坐标是否命中目标元素。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <param name="screenPoint">屏幕坐标点。</param>
        /// <returns>命中时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        /// <remarks>
        /// 基于 <see cref="VisualTreeHelper.HitTest(Visual, Point)"/> 进行精确坐标命中测试，
        /// 不依赖 WPF 内置的 <see cref="UIElement.IsMouseOver"/> 状态。
        /// </remarks>
        public static bool IsMouseOverElement(UIElement element, Point screenPoint)
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

        #endregion

        #region Routed Event Trigger

        /// <summary>
        /// 手动触发按钮的 Click 事件。
        /// </summary>
        /// <param name="button">目标按钮。</param>
        /// <param name="ignoreIsEnabled">是否忽略 <see cref="UIElement.IsEnabled"/> 检查。</param>
        /// <exception cref="ArgumentNullException"><paramref name="button"/> 为 <c>null</c>。</exception>
        /// <exception cref="InvalidOperationException">按钮已禁用，且 <paramref name="ignoreIsEnabled"/> 为 <c>false</c>。</exception>
        public static void TriggerClick(this Button button, bool ignoreIsEnabled = false)
        {
            if (button == null)
                throw new ArgumentNullException(nameof(button));

            if (!ignoreIsEnabled && !button.IsEnabled)
                throw new InvalidOperationException("按钮已禁用，无法触发 Click 事件。");

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        #endregion

        #region ItemsControl Helper

        /// <summary>
        /// 在 ItemsControl 中按指定属性值查找并定位第一项匹配的数据项。
        /// </summary>
        /// <typeparam name="T">数据项类型。</typeparam>
        /// <param name="itemsControl">目标控件。</param>
        /// <param name="propertySelector">属性选择器。</param>
        /// <param name="value">待匹配的值。</param>
        /// <returns>找到匹配项时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        /// <remarks>
        /// 同时覆盖 <see cref="ItemsControl.ItemsSource"/> 绑定与通过 <c>Items.Add</c> 直接添加的条目。
        /// </remarks>
        public static bool SelectItemByProperty<T>(
            this ItemsControl itemsControl, Func<T, object> propertySelector, object value)
        {
            if (itemsControl == null || propertySelector == null)
                return false;

            foreach (T item in itemsControl.Items.OfType<T>())
            {
                if (!Equals(propertySelector(item), value))
                    continue;

                Selector selector = itemsControl as Selector;
                if (selector != null)
                    selector.SelectedItem = item;

                ScrollItemIntoView(itemsControl, item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 在 ItemsControl 中按条件查找并定位第一项匹配的数据项。
        /// </summary>
        /// <typeparam name="T">数据项类型。</typeparam>
        /// <param name="itemsControl">目标控件。</param>
        /// <param name="predicate">匹配条件。</param>
        /// <returns>找到匹配项时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        /// <remarks>
        /// 同时覆盖 <see cref="ItemsControl.ItemsSource"/> 绑定与通过 <c>Items.Add</c> 直接添加的条目。
        /// </remarks>
        public static bool SelectItemWhere<T>(this ItemsControl itemsControl, Func<T, bool> predicate)
        {
            if (itemsControl == null || predicate == null)
                return false;

            foreach (T item in itemsControl.Items.OfType<T>())
            {
                if (!predicate(item))
                    continue;

                Selector selector = itemsControl as Selector;
                if (selector != null)
                    selector.SelectedItem = item;

                ScrollItemIntoView(itemsControl, item);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 获取指定数据项对应的容器。
        /// </summary>
        /// <typeparam name="TContainer">容器类型。</typeparam>
        /// <param name="itemsControl">目标控件。</param>
        /// <param name="item">数据项。</param>
        /// <returns>对应容器；若未找到则返回 <c>null</c>。</returns>
        /// <remarks>
        /// 若容器已生成但类型与 <typeparamref name="TContainer"/> 不匹配，直接返回 <c>null</c>，不触发额外布局。
        /// 若容器尚未虚拟化生成，会先调用 ScrollIntoView 强制生成后重试。
        /// </remarks>
        public static TContainer GetItemContainer<TContainer>(this ItemsControl itemsControl, object item)
            where TContainer : DependencyObject
        {
            if (itemsControl == null || item == null)
                return null;

            DependencyObject rawContainer = itemsControl.ItemContainerGenerator.ContainerFromItem(item);
            if (rawContainer != null)
                return rawContainer as TContainer;

            // 容器尚未虚拟化生成，滚动到视图以强制生成后重试。
            ScrollItemIntoView(itemsControl, item);
            itemsControl.UpdateLayout();

            return itemsControl.ItemContainerGenerator.ContainerFromItem(item) as TContainer;
        }

        private static void ScrollItemIntoView(ItemsControl itemsControl, object item)
        {
            if (itemsControl == null || item == null)
                return;

            ListView listView = itemsControl as ListView;
            if (listView != null) { listView.ScrollIntoView(item); return; }

            ListBox listBox = itemsControl as ListBox;
            if (listBox != null) { listBox.ScrollIntoView(item); return; }

            DataGrid dataGrid = itemsControl as DataGrid;
            if (dataGrid != null) { dataGrid.ScrollIntoView(item); return; }

            ComboBox comboBox = itemsControl as ComboBox;
            if (comboBox != null && comboBox.IsDropDownOpen)
            {
                ListBoxItem listBoxItem =
                    comboBox.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (listBoxItem != null)
                    listBoxItem.BringIntoView();
            }
        }

        #endregion

        #region Focus Helper

        /// <summary>
        /// 安全地将焦点设置到指定元素。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <returns>设置成功时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public static bool SetFocus(UIElement element)
        {
            if (element == null || !element.Focusable || !element.IsEnabled || !element.IsVisible)
                return false;

            return element.Focus();
        }

        /// <summary>
        /// 查找并聚焦到第一个可聚焦的可视子元素。
        /// </summary>
        /// <param name="parent">父元素。</param>
        /// <returns>成功设置焦点时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public static bool FocusFirstChild(DependencyObject parent)
        {
            if (parent == null)
                return false;

            foreach (DependencyObject obj in TraverseVisualTree(parent).Skip(1))
            {
                UIElement element = obj as UIElement;
                if (element != null && SetFocus(element))
                    return true;
            }

            return false;
        }

        #endregion

        #region Batch Helper

        /// <summary>
        /// 清除指定元素及其可视子元素上的所有绑定。
        /// </summary>
        /// <param name="root">起始元素。处理范围包含自身。</param>
        public static void ClearAllBindings(this DependencyObject root)
        {
            if (root == null)
                return;

            foreach (DependencyObject obj in TraverseVisualTree(root))
                BindingOperations.ClearAllBindings(obj);
        }

        #endregion

        #region Render Helper

        /// <summary>
        /// 立即处理当前元素所属 Dispatcher 的渲染队列。
        /// </summary>
        /// <param name="element">目标元素。</param>
        public static void Refresh(this UIElement element)
        {
            if (element == null)
                return;

            element.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }

        #endregion

        #region Bounds Helper

        /// <summary>
        /// 获取元素在屏幕坐标系中的边界矩形。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <returns>边界矩形；若元素不可用则返回 <see cref="Rect.Empty"/>。</returns>
        /// <remarks>
        /// 通过映射四角到屏幕坐标计算包围盒，对带旋转/倾斜变换的元素同样有效。
        /// </remarks>
        public static Rect GetScreenBounds(this FrameworkElement element)
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
        /// <param name="element">目标元素。</param>
        /// <param name="ancestor">祖先视觉对象。</param>
        /// <returns>相对边界矩形；若无法计算则返回 <see cref="Rect.Empty"/>。</returns>
        public static Rect GetBoundsRelativeTo(this FrameworkElement element, Visual ancestor)
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
        /// 判断元素是否位于其所属 <see cref="ScrollViewer"/> 的可视区域内。
        /// </summary>
        /// <param name="element">目标元素。</param>
        /// <returns>
        /// 当元素至少部分位于可视区域内时返回 <c>true</c>；否则返回 <c>false</c>。
        /// 若未找到父级 <see cref="ScrollViewer"/>，则退化为返回元素自身的可见状态。
        /// </returns>
        public static bool IsVisibleInScrollViewer(this FrameworkElement element)
        {
            if (element == null || !element.IsVisible)
                return false;

            ScrollViewer scrollViewer = FindAncestor<ScrollViewer>(element, includeSelf: false);
            if (scrollViewer == null)
                return element.IsVisible;

            try
            {
                Rect elementBounds = element.TransformToAncestor(scrollViewer)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

                Rect viewportBounds = new Rect(0, 0, scrollViewer.ViewportWidth, scrollViewer.ViewportHeight);
                return viewportBounds.IntersectsWith(elementBounds);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        #endregion

        #region Private Helper

        private static DependencyObject GetParentObject(DependencyObject current)
        {
            if (current == null)
                return null;

            try
            {
                DependencyObject visualParent = VisualTreeHelper.GetParent(current);
                if (visualParent != null)
                    return visualParent;
            }
            catch (InvalidOperationException)
            {
                // 元素不在可视树中时降级，继续尝试逻辑树父节点。
            }

            if (current is FrameworkElement fe)
                return fe.Parent;

            if (current is FrameworkContentElement fce)
                return fce.Parent;

            return null;
        }

        #endregion
    }
}