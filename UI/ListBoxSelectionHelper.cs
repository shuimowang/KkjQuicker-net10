using KkjQuicker.Utilities.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Controls;

namespace KkjQuicker.UI
{
    /// <summary>
    /// WPF ListBox 选择辅助方法。
    /// </summary>
    public static class ListBoxSelectionHelper
    {
        /// <summary>
        /// 清除 ListBox 当前选择，并通过临时切换 SelectionMode 来重置 WPF Extended 多选内部 Shift 锚点。
        /// </summary>
        public static void ResetSelectionAnchor(this ListBox listBox)
        {
            if (listBox == null)
                throw new ArgumentNullException("listBox");

            var oldMode = listBox.SelectionMode;
            if (oldMode != SelectionMode.Single)
                listBox.SelectionMode = SelectionMode.Single;

            listBox.SelectedIndex = -1;

            if (oldMode != SelectionMode.Single)
                listBox.SelectionMode = oldMode;
        }

        /// <summary>
        /// 按当前 ICollectionView 的可见顺序，选中 anchor 到 current 之间的所有项。
        /// 如果锚点或目标项不在当前视图中，则回退为只选中 current。
        /// </summary>
        public static void SelectRangeByAnchor<T>(
            this ListBox listBox,
            ICollectionView view,
            T anchor,
            T current) where T : class
        {
            if (listBox == null)
                throw new ArgumentNullException("listBox");
            if (view == null)
                throw new ArgumentNullException("view");
            if (anchor == null)
                throw new ArgumentNullException("anchor");
            if (current == null)
                throw new ArgumentNullException("current");

            if (listBox.SelectionMode == SelectionMode.Single)
            {
                listBox.SelectedItem = current;
                return;
            }

            var visibleItems = ToTypedList<T>(view);
            var anchorIndex = visibleItems.IndexOfByReference(anchor);
            var currentIndex = visibleItems.IndexOfByReference(current);

            listBox.SelectedItems.Clear();

            if (anchorIndex < 0 || currentIndex < 0)
            {
                listBox.SelectedItem = current;
                return;
            }

            var start = Math.Min(anchorIndex, currentIndex);
            var end = Math.Max(anchorIndex, currentIndex);
            for (var i = start; i <= end; i++)
                listBox.SelectedItems.Add(visibleItems[i]);
        }

        /// <summary>
        /// 聚焦当前选中项对应的 ListBoxItem，并滚动到可见区域。
        /// </summary>
        public static void FocusSelectedItem(this ListBox listBox)
        {
            if (listBox == null)
                throw new ArgumentNullException("listBox");
            if (listBox.SelectedItem == null)
                return;

            listBox.ScrollIntoView(listBox.SelectedItem);
            listBox.UpdateLayout();

            var lbi = listBox.ItemContainerGenerator
                .ContainerFromItem(listBox.SelectedItem) as ListBoxItem;
            if (lbi != null)
                lbi.Focus();
        }

        private static List<T> ToTypedList<T>(ICollectionView view) where T : class
        {
            var result = new List<T>();
            foreach (var obj in view)
            {
                var item = obj as T;
                if (item != null)
                    result.Add(item);
            }
            return result;
        }
    }
}