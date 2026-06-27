using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 实时获取容器项在 <see cref="ItemsControl"/> 中从 1 开始的显示序号。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 需要在 <see cref="MultiBinding"/> 中按顺序绑定三个源：
    /// </para>
    /// <list type="number">
    ///   <item><description>当前容器项（AncestorType=ListBoxItem 或 ComboBoxItem 等）</description></item>
    ///   <item><description>所属 <see cref="ItemsControl"/>（AncestorType=ListBox、ListView 等）</description></item>
    ///   <item><description><see cref="ItemsControl.Items"/> 的 Count 属性（用于在增删后触发所有可见项刷新）</description></item>
    /// </list>
    /// <para>
    /// 序号通过 <see cref="ItemContainerGenerator.IndexFromContainer"/> 计算，
    /// 仅对已生成容器的可见项有效。启用 UI 虚拟化时，视口外尚未生成容器的项
    /// 在滚动进入视口后会自动生成容器并获得正确序号，通常无需关闭虚拟化。
    /// </para>
    /// <para>
    /// 刷新由 <see cref="ItemsControl.Items"/> 的 Count 变化驱动，
    /// 因此增删和筛选场景均可正确更新。若发生总数不变的纯排序或移位操作，
    /// 序号不会自动刷新，此为已知限制。
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="xaml">
    /// &lt;ListBox ItemsSource="{Binding Items}"&gt;
    ///     &lt;ListBox.ItemTemplate&gt;
    ///         &lt;DataTemplate&gt;
    ///             &lt;TextBlock&gt;
    ///                 &lt;TextBlock.Text&gt;
    ///                     &lt;MultiBinding Converter="{StaticResource ItemIndexConv}"&gt;
    ///                         &lt;Binding RelativeSource="{RelativeSource AncestorType=ListBoxItem}"/&gt;
    ///                         &lt;Binding RelativeSource="{RelativeSource AncestorType=ListBox}"/&gt;
    ///                         &lt;Binding RelativeSource="{RelativeSource AncestorType=ListBox}" Path="Items.Count"/&gt;
    ///                     &lt;/MultiBinding&gt;
    ///                 &lt;/TextBlock.Text&gt;
    ///             &lt;/TextBlock&gt;
    ///         &lt;/DataTemplate&gt;
    ///     &lt;/ListBox.ItemTemplate&gt;
    /// &lt;/ListBox&gt;
    /// </code>
    /// </example>
    public class ItemIndexConverter : IMultiValueConverter
    {
        /// <summary>
        /// 返回当前项从 1 开始的显示序号字符串。
        /// </summary>
        /// <param name="values">
        /// 依次为当前容器项（<see cref="DependencyObject"/>）、所属 <see cref="ItemsControl"/>、
        /// 以及 <see cref="ItemsControl.Items"/> 的 Count（仅用于触发刷新）。
        /// </param>
        /// <param name="targetType">绑定目标类型。</param>
        /// <param name="parameter">未使用。</param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>从 1 开始的序号字符串；容器不可用时返回空字符串。</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return string.Empty;

            if (values[0] is DependencyObject item && values[1] is ItemsControl itemsControl)
            {
                int index = itemsControl.ItemContainerGenerator.IndexFromContainer(item);
                if (index >= 0)
                    return (index + 1).ToString(culture);
            }

            return string.Empty;
        }

        /// <summary>
        /// 不支持反向转换。
        /// </summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => targetTypes == null ? Array.Empty<object>() : targetTypes.Select(_ => Binding.DoNothing).ToArray();
    }
}
