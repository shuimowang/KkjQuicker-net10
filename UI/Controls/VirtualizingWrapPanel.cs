using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KkjQuicker.UI.Controls
{
    /// <summary>
    /// 固定项尺寸的虚拟化 WrapPanel。
    /// 适用于大量等宽等高卡片的 ItemsControl / ListBox。
    /// </summary>
    public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
    {
        public static readonly DependencyProperty ItemSizeProperty =
            DependencyProperty.Register(
                "ItemSize",
                typeof(Size),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(
                    new Size(120.0, 80.0),
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnLayoutPropertyChanged));

        public static readonly DependencyProperty ItemGapProperty =
            DependencyProperty.Register(
                "ItemGap",
                typeof(double),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(
                    8.0,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnLayoutPropertyChanged));

        public static readonly DependencyProperty IsSpacingEnabledProperty =
            DependencyProperty.Register(
                "IsSpacingEnabled",
                typeof(bool),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(
                    true,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnLayoutPropertyChanged));

        public static readonly DependencyProperty CacheRowsProperty =
            DependencyProperty.Register(
                "CacheRows",
                typeof(int),
                typeof(VirtualizingWrapPanel),
                new FrameworkPropertyMetadata(
                    1,
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnLayoutPropertyChanged));

        private ItemsControl _itemsControl = null!;
        private Size _extent;
        private Size _viewport;
        private Point _offset;
        private int _itemsPerRow = 1;

        /// <summary>
        /// 每个项目的固定尺寸。
        /// </summary>
        public Size ItemSize
        {
            get { return (Size)GetValue(ItemSizeProperty); }
            set { SetValue(ItemSizeProperty, value); }
        }

        /// <summary>
        /// 项目之间的最小间距。
        /// </summary>
        public double ItemGap
        {
            get { return (double)GetValue(ItemGapProperty); }
            set { SetValue(ItemGapProperty, value); }
        }

        /// <summary>
        /// 是否将每行剩余宽度均分到项目间距和两侧边距中。
        /// </summary>
        public bool IsSpacingEnabled
        {
            get { return (bool)GetValue(IsSpacingEnabledProperty); }
            set { SetValue(IsSpacingEnabledProperty, value); }
        }

        /// <summary>
        /// 可视区域前后额外保留的缓存行数。
        /// </summary>
        public int CacheRows
        {
            get { return (int)GetValue(CacheRowsProperty); }
            set { SetValue(CacheRowsProperty, value); }
        }

        public bool CanHorizontallyScroll { get; set; }

        public bool CanVerticallyScroll { get; set; }

        public double ExtentWidth
        {
            get { return _extent.Width; }
        }

        public double ExtentHeight
        {
            get { return _extent.Height; }
        }

        public double ViewportWidth
        {
            get { return _viewport.Width; }
        }

        public double ViewportHeight
        {
            get { return _viewport.Height; }
        }

        public double HorizontalOffset
        {
            get { return _offset.X; }
        }

        public double VerticalOffset
        {
            get { return _offset.Y; }
        }

        public ScrollViewer? ScrollOwner { get; set; }

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            VirtualizingWrapPanel? panel = d as VirtualizingWrapPanel;
            if (panel == null)
                return;

            panel.InvalidateMeasure();

            if (panel.ScrollOwner != null)
                panel.ScrollOwner.InvalidateScrollInfo();
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            _itemsControl = ItemsControl.GetItemsOwner(this);
        }

        protected override void OnItemsChanged(object? sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            EnsureItemsControl();

            Size viewport = GetMeasureViewport(availableSize);
            int itemCount = GetItemCount();

            if (itemCount <= 0)
            {
                ClearGeneratedChildren();
                UpdateScrollInfo(viewport, new Size(0.0, 0.0));
                return viewport;
            }

            Size itemSize = GetSafeItemSize();
            double gap = GetSafeItemGap();

            _itemsPerRow = CalculateItemsPerRow(viewport.Width, itemSize.Width, gap);

            int rowCount = (int)Math.Ceiling((double)itemCount / _itemsPerRow);
            double extentHeight = rowCount * itemSize.Height + Math.Max(0, rowCount - 1) * gap;
            Size extent = new Size(viewport.Width, extentHeight);

            UpdateScrollInfo(viewport, extent);

            int firstIndex;
            int lastIndex;
            GetVisibleRange(itemCount, itemSize.Height, gap, out firstIndex, out lastIndex);

            CleanupItems(firstIndex, lastIndex);
            GenerateItems(firstIndex, lastIndex, itemSize);

            return viewport;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size itemSize = GetSafeItemSize();
            double gap = GetSafeItemGap();

            int itemsPerRow = CalculateItemsPerRow(finalSize.Width, itemSize.Width, gap);

            double startX;
            double actualGap;
            CalculateHorizontalSpacing(finalSize.Width, itemSize.Width, gap, itemsPerRow, out startX, out actualGap);

            IItemContainerGenerator generator = ItemContainerGenerator;

            for (int i = 0; i < InternalChildren.Count; i++)
            {
                UIElement child = InternalChildren[i];
                GeneratorPosition position = new GeneratorPosition(i, 0);
                int itemIndex = generator.IndexFromGeneratorPosition(position);

                if (itemIndex < 0)
                    continue;

                int row = itemIndex / itemsPerRow;
                int column = itemIndex % itemsPerRow;

                double x = startX + column * (itemSize.Width + actualGap) - _offset.X;
                double y = row * (itemSize.Height + gap) - _offset.Y;

                child.Arrange(new Rect(x, y, itemSize.Width, itemSize.Height));
            }

            return finalSize;
        }

        protected override void BringIndexIntoView(int index)
        {
            EnsureItemsControl();

            int itemCount = GetItemCount();
            if (itemCount <= 0)
                return;

            if (_viewport.Width <= 0 || _viewport.Height <= 0)
                return;

            if (index < 0)
                index = 0;
            else if (index >= itemCount)
                index = itemCount - 1;

            Size itemSize = GetSafeItemSize();
            double gap = GetSafeItemGap();

            int row = index / _itemsPerRow;
            double rowTop = row * (itemSize.Height + gap);
            double rowBottom = rowTop + itemSize.Height;

            if (rowTop < _offset.Y)
                SetVerticalOffset(rowTop);
            else if (rowBottom > _offset.Y + _viewport.Height)
                SetVerticalOffset(rowBottom - _viewport.Height);
        }

        public void LineUp()
        {
            SetVerticalOffset(VerticalOffset - GetLineScrollAmount());
        }

        public void LineDown()
        {
            SetVerticalOffset(VerticalOffset + GetLineScrollAmount());
        }

        public void LineLeft()
        {
            SetHorizontalOffset(HorizontalOffset - 16.0);
        }

        public void LineRight()
        {
            SetHorizontalOffset(HorizontalOffset + 16.0);
        }

        public void MouseWheelUp()
        {
            SetVerticalOffset(VerticalOffset - GetWheelScrollAmount());
        }

        public void MouseWheelDown()
        {
            SetVerticalOffset(VerticalOffset + GetWheelScrollAmount());
        }

        public void MouseWheelLeft()
        {
            LineLeft();
        }

        public void MouseWheelRight()
        {
            LineRight();
        }

        public void PageUp()
        {
            SetVerticalOffset(VerticalOffset - ViewportHeight);
        }

        public void PageDown()
        {
            SetVerticalOffset(VerticalOffset + ViewportHeight);
        }

        public void PageLeft()
        {
            SetHorizontalOffset(HorizontalOffset - ViewportWidth);
        }

        public void PageRight()
        {
            SetHorizontalOffset(HorizontalOffset + ViewportWidth);
        }

        public void SetHorizontalOffset(double offset)
        {
            double newOffset = CoerceOffset(offset, ExtentWidth, ViewportWidth);

            if (AreClose(_offset.X, newOffset))
                return;

            _offset.X = newOffset;

            if (ScrollOwner != null)
                ScrollOwner.InvalidateScrollInfo();

            InvalidateMeasure();
        }

        public void SetVerticalOffset(double offset)
        {
            double newOffset = CoerceOffset(offset, ExtentHeight, ViewportHeight);

            if (AreClose(_offset.Y, newOffset))
                return;

            _offset.Y = newOffset;

            if (ScrollOwner != null)
                ScrollOwner.InvalidateScrollInfo();

            InvalidateMeasure();
        }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            UIElement? element = visual as UIElement;
            if (element == null)
                return Rect.Empty;

            int itemIndex = GetItemIndexFromChild(element);
            if (itemIndex < 0)
                return rectangle;

            if (_viewport.Width <= 0 || _viewport.Height <= 0)
                return rectangle;

            Size itemSize = GetSafeItemSize();
            double gap = GetSafeItemGap();

            int itemsPerRow = CalculateItemsPerRow(_viewport.Width, itemSize.Width, gap);

            int row = itemIndex / itemsPerRow;
            int column = itemIndex % itemsPerRow;

            double startX;
            double actualGap;
            CalculateHorizontalSpacing(_viewport.Width, itemSize.Width, gap, itemsPerRow, out startX, out actualGap);

            Rect itemRect = new Rect(
                startX + column * (itemSize.Width + actualGap),
                row * (itemSize.Height + gap),
                itemSize.Width,
                itemSize.Height);

            if (itemRect.Top < VerticalOffset)
                SetVerticalOffset(itemRect.Top);
            else if (itemRect.Bottom > VerticalOffset + ViewportHeight)
                SetVerticalOffset(itemRect.Bottom - ViewportHeight);

            if (itemRect.Left < HorizontalOffset)
                SetHorizontalOffset(itemRect.Left);
            else if (itemRect.Right > HorizontalOffset + ViewportWidth)
                SetHorizontalOffset(itemRect.Right - ViewportWidth);

            return rectangle;
        }

        private void EnsureItemsControl()
        {
            if (_itemsControl == null)
                _itemsControl = ItemsControl.GetItemsOwner(this);
        }

        private int GetItemCount()
        {
            if (_itemsControl == null || _itemsControl.Items == null)
                return 0;

            return _itemsControl.Items.Count;
        }

        private Size GetSafeItemSize()
        {
            Size size = ItemSize;

            double width = size.Width;
            double height = size.Height;

            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
                width = 120.0;

            if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
                height = 80.0;

            return new Size(width, height);
        }

        private double GetSafeItemGap()
        {
            double gap = ItemGap;

            if (double.IsNaN(gap) || double.IsInfinity(gap) || gap < 0)
                return 0.0;

            return gap;
        }

        private int GetSafeCacheRows()
        {
            return Math.Max(0, CacheRows);
        }

        private Size GetMeasureViewport(Size availableSize)
        {
            Size itemSize = GetSafeItemSize();

            double width = availableSize.Width;
            double height = availableSize.Height;

            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            {
                if (_itemsControl != null && _itemsControl.ActualWidth > 0)
                    width = _itemsControl.ActualWidth;
                else
                    width = itemSize.Width;
            }

            if (double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            {
                if (_itemsControl != null && _itemsControl.ActualHeight > 0)
                    height = _itemsControl.ActualHeight;
                else
                    height = itemSize.Height;
            }

            return new Size(width, height);
        }

        private int CalculateItemsPerRow(double availableWidth, double itemWidth, double gap)
        {
            if (availableWidth <= 0 || itemWidth <= 0)
                return 1;

            return Math.Max(1, (int)Math.Floor((availableWidth + gap) / (itemWidth + gap)));
        }

        private void CalculateHorizontalSpacing(
            double availableWidth,
            double itemWidth,
            double gap,
            int itemsPerRow,
            out double startX,
            out double actualGap)
        {
            startX = 0.0;
            actualGap = gap;

            if (!IsSpacingEnabled)
                return;

            double usedWidth = itemsPerRow * itemWidth + Math.Max(0, itemsPerRow - 1) * gap;
            double extra = availableWidth - usedWidth;

            if (extra <= 0)
                return;

            if (itemsPerRow == 1)
            {
                startX = extra / 2.0;
                return;
            }

            double extraPerSlot = extra / (itemsPerRow + 1);
            startX = extraPerSlot;
            actualGap = gap + extraPerSlot;
        }

        private void GetVisibleRange(int itemCount, double itemHeight, double gap, out int firstIndex, out int lastIndex)
        {
            double rowHeight = itemHeight + gap;
            int cacheRows = GetSafeCacheRows();

            int firstRow = (int)Math.Floor(VerticalOffset / rowHeight) - cacheRows;
            int lastRow = (int)Math.Ceiling((VerticalOffset + ViewportHeight) / rowHeight) + cacheRows;

            firstRow = Math.Max(0, firstRow);
            lastRow = Math.Max(firstRow, lastRow);

            firstIndex = firstRow * _itemsPerRow;
            lastIndex = Math.Min(itemCount - 1, ((lastRow + 1) * _itemsPerRow) - 1);
        }

        private void GenerateItems(int firstIndex, int lastIndex, Size itemSize)
        {
            if (firstIndex > lastIndex)
                return;

            IItemContainerGenerator generator = ItemContainerGenerator;
            GeneratorPosition startPosition = generator.GeneratorPositionFromIndex(firstIndex);
            int childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

            using (generator.StartAt(startPosition, GeneratorDirection.Forward, true))
            {
                for (int itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
                {
                    bool isNewlyRealized;
                    UIElement? child = generator.GenerateNext(out isNewlyRealized) as UIElement;

                    if (child == null)
                        continue;

                    if (isNewlyRealized)
                    {
                        if (childIndex >= InternalChildren.Count)
                            AddInternalChild(child);
                        else
                            InsertInternalChild(childIndex, child);

                        generator.PrepareItemContainer(child);
                    }

                    child.Measure(itemSize);
                }
            }
        }

        private void CleanupItems(int firstIndex, int lastIndex)
        {
            IItemContainerGenerator generator = ItemContainerGenerator;

            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                GeneratorPosition position = new GeneratorPosition(i, 0);
                int itemIndex = generator.IndexFromGeneratorPosition(position);

                if (itemIndex < firstIndex || itemIndex > lastIndex)
                {
                    generator.Remove(position, 1);
                    RemoveInternalChildRange(i, 1);
                }
            }
        }

        private void ClearGeneratedChildren()
        {
            IItemContainerGenerator generator = ItemContainerGenerator;

            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                GeneratorPosition position = new GeneratorPosition(i, 0);
                generator.Remove(position, 1);
                RemoveInternalChildRange(i, 1);
            }
        }

        private void UpdateScrollInfo(Size viewport, Size extent)
        {
            _viewport = viewport;
            _extent = extent;

            _offset.X = CoerceOffset(_offset.X, _extent.Width, _viewport.Width);
            _offset.Y = CoerceOffset(_offset.Y, _extent.Height, _viewport.Height);

            if (ScrollOwner != null)
                ScrollOwner.InvalidateScrollInfo();
        }

        private double CoerceOffset(double offset, double extent, double viewport)
        {
            if (double.IsNaN(offset) || offset < 0)
                return 0.0;

            double maxOffset = Math.Max(0.0, extent - viewport);
            if (offset > maxOffset)
                return maxOffset;

            return offset;
        }

        private int GetItemIndexFromChild(UIElement child)
        {
            EnsureItemsControl();

            ItemContainerGenerator? generator = _itemsControl != null
                ? _itemsControl.ItemContainerGenerator
                : null;

            if (generator == null)
                return -1;

            DependencyObject current = child;
            while (current != null)
            {
                int index = generator.IndexFromContainer(current);
                if (index >= 0)
                    return index;

                if (ReferenceEquals(current, this))
                    break;

                current = VisualTreeHelper.GetParent(current);
            }

            return -1;
        }

        private double GetLineScrollAmount()
        {
            Size itemSize = GetSafeItemSize();
            return itemSize.Height + GetSafeItemGap();
        }

        private double GetWheelScrollAmount()
        {
            return GetLineScrollAmount() * 3.0;
        }

        private bool AreClose(double x, double y)
        {
            return Math.Abs(x - y) < 0.1;
        }
    }
}
