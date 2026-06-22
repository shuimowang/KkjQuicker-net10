using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 将 <see cref="bool"/> 转换为任意类型 <typeparamref name="T"/> 的值。
    /// </summary>
    /// <typeparam name="T">目标值类型。</typeparam>
    /// <remarks>
    /// <para>
    /// 默认使用 <see cref="TrueValue"/> 和 <see cref="FalseValue"/> 作为转换结果。
    /// 也可通过 <see cref="IValueConverter.Convert(object, Type, object, CultureInfo)"/> /
    /// <see cref="IValueConverter.ConvertBack(object, Type, object, CultureInfo)"/> 的
    /// <c>parameter</c> 传入形如 <c>"TrueValue|FalseValue"</c> 的字符串，
    /// 以覆盖本次转换使用的真值与假值。
    /// </para>
    /// <para>
    /// <see cref="IsReversed"/> 仅影响 <see cref="Convert"/>，不影响 <see cref="ConvertBack"/>。
    /// </para>
    /// <para>
    /// 在 <see cref="Convert"/> 中，<see langword="null"/> 与非 <see cref="bool"/> 输入均按
    /// <see langword="false"/> 处理，且均不参与 <see cref="IsReversed"/> 反转。
    /// </para>
    /// </remarks>
    [ValueConversion(typeof(bool), typeof(object))]
    public class BoolToValueConverter<T> : IValueConverter
    {
        /// <summary>
        /// 获取或设置布尔值为 <see langword="true"/> 时返回的值。
        /// </summary>
        public T TrueValue { get; set; } = default!;

        /// <summary>
        /// 获取或设置布尔值为 <see langword="false"/> 时返回的值。
        /// </summary>
        public T FalseValue { get; set; } = default!;

        /// <summary>
        /// 获取或设置是否在 <see cref="Convert"/> 时反转布尔逻辑。
        /// </summary>
        /// <remarks>
        /// 仅影响 <see cref="Convert"/>，不影响 <see cref="ConvertBack"/>。
        /// 对 <see langword="null"/> 及非 <see cref="bool"/> 输入不执行反转。
        /// </remarks>
        public bool IsReversed { get; set; }

        /// <summary>
        /// 初始化 <see cref="BoolToValueConverter{T}"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 当 <typeparamref name="T"/> 为 <see cref="string"/> 时，
        /// 默认将 <see cref="TrueValue"/> 设为 <c>"True"</c>，
        /// 将 <see cref="FalseValue"/> 设为 <c>"False"</c>。
        /// </remarks>
        public BoolToValueConverter()
        {
            if (typeof(T) == typeof(string))
            {
                TrueValue = (T)(object)"True";
                FalseValue = (T)(object)"False";
            }
        }

        /// <summary>
        /// 将布尔值转换为 <typeparamref name="T"/> 对应的真值或假值。
        /// </summary>
        /// <param name="value">
        /// 要转换的值，应为 <see cref="bool"/>。
        /// <see langword="null"/> 与非 <see cref="bool"/> 输入均按 <see langword="false"/> 处理，
        /// 且不参与 <see cref="IsReversed"/> 反转。
        /// </param>
        /// <param name="targetType">绑定目标类型，当前实现不依赖该参数。</param>
        /// <param name="parameter">
        /// 可选参数，格式为 <c>"TrueValue|FalseValue"</c>，用于覆盖本次转换的真值与假值。
        /// 若格式不符或解析失败，则回退到 <see cref="TrueValue"/> / <see cref="FalseValue"/>。
        /// </param>
        /// <param name="culture">转换所使用的区域性信息。</param>
        /// <returns>输入为有效 <see langword="true"/> 时返回真值，否则返回假值。</returns>
        public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            GetEffectiveValues(parameter, culture, out T trueValue, out T falseValue);

            // null 与非 bool 均视为 false，且不参与 IsReversed 反转
            if (value == null || !(value is bool))
                return falseValue;

            bool flag = (bool)value;
            if (IsReversed)
                flag = !flag;

            return flag ? (object)trueValue! : falseValue!;
        }

        /// <summary>
        /// 将输入值转换回 <see cref="bool"/>。
        /// </summary>
        /// <param name="value">要转换回布尔值的输入值。</param>
        /// <param name="targetType">绑定目标类型，当前实现不依赖该参数。</param>
        /// <param name="parameter">
        /// 可选参数，格式为 <c>"TrueValue|FalseValue"</c>；仅第一个值用于判定真值。
        /// </param>
        /// <param name="culture">转换所使用的区域性信息。</param>
        /// <returns>
        /// 当输入值与当前真值相等时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// 若输入无法转换为 <typeparamref name="T"/>，也返回 <see langword="false"/>。
        /// </returns>
        public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            GetEffectiveValues(parameter, culture, out T trueValue, out T falseValue);

            return TryConvertToT(value, culture, out T incoming)
                && EqualityComparer<T>.Default.Equals(incoming, trueValue);
        }

        private void GetEffectiveValues(object parameter, CultureInfo culture, out T trueValue, out T falseValue)
        {
            trueValue = TrueValue;
            falseValue = FalseValue;

            var parameterText = parameter as string;
            if (string.IsNullOrWhiteSpace(parameterText))
                return;

            var parts = parameterText.Split(new[] { '|' }, 2);
            if (parts.Length != 2)
                return;

            if (TryConvertFromString(parts[0].Trim(), culture, out T parsedTrue))
                trueValue = parsedTrue;

            if (TryConvertFromString(parts[1].Trim(), culture, out T parsedFalse))
                falseValue = parsedFalse;
        }

        private static bool TryConvertFromString(string text, CultureInfo culture, out T result)
        {
            result = default;

            Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            try
            {
                if (targetType == typeof(string))
                {
                    result = (T)(object)text;
                    return true;
                }

                if (string.IsNullOrEmpty(text))
                {
                    // 仅可空类型接受空字符串（映射为 null）
                    return Nullable.GetUnderlyingType(typeof(T)) != null;
                }

                if (targetType.IsEnum)
                {
                    result = (T)Enum.Parse(targetType, text, ignoreCase: true);
                    return true;
                }

                var converter = TypeDescriptor.GetConverter(targetType);
                if (converter != null && converter.CanConvertFrom(typeof(string)))
                {
                    result = (T)converter.ConvertFromString(null, culture, text)!;
                    return true;
                }

                result = (T)System.Convert.ChangeType(text, targetType, culture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryConvertToT(object value, CultureInfo culture, out T result)
        {
            result = default;

            if (value == null)
            {
                // 引用类型及 Nullable<X> 均可合法持有 null
                bool acceptsNull = !typeof(T).IsValueType
                                   || Nullable.GetUnderlyingType(typeof(T)) != null;
                if (acceptsNull)
                {
                    result = default; // 对引用类型和 Nullable<X> 而言即 null
                    return true;
                }
                return false;
            }

            if (value is T t)
            {
                result = t;
                return true;
            }

            // string 提前处理，避免后续枚举分支中的重复判断
            if (value is string text)
                return TryConvertFromString(text, culture, out result);

            Type targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            try
            {
                if (targetType.IsEnum)
                {
                    result = (T)Enum.ToObject(targetType, value);
                    return true;
                }

                result = (T)System.Convert.ChangeType(value, targetType, culture);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 将 <see cref="bool"/> 转换为 <see cref="string"/> 的专用转换器。
    /// </summary>
    /// <remarks>
    /// 提供具体类型，便于在 XAML 中直接实例化，无需指定泛型参数。
    /// 默认真值为 <c>"True"</c>，默认假值为 <c>"False"</c>。
    /// </remarks>
    public class BoolToStringConverter : BoolToValueConverter<string>
    {
    }

    /// <summary>
    /// 将 <see cref="bool"/> 转换为 <see cref="Visibility"/> 的专用转换器。
    /// </summary>
    /// <remarks>
    /// 提供具体类型，便于在 XAML 中直接实例化，无需指定泛型参数。
    /// 默认 <see langword="true"/> 返回 <see cref="Visibility.Visible"/>，
    /// <see langword="false"/> 返回 <see cref="Visibility.Collapsed"/>。
    /// <para>
    /// 不支持反向转换，<see cref="IValueConverter.ConvertBack"/> 始终返回
    /// <see cref="Binding.DoNothing"/>。
    /// </para>
    /// </remarks>
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : BoolToValueConverter<Visibility>
    {
        /// <summary>
        /// 获取或设置条件不满足时返回的可见性。
        /// </summary>
        /// <remarks>
        /// 默认值为 <see cref="Visibility.Collapsed"/>，可根据需要改为 <see cref="Visibility.Hidden"/>。
        /// 等价于直接设置基类的 <see cref="BoolToValueConverter{T}.FalseValue"/>。
        /// </remarks>
        public Visibility FalseVisibility
        {
            get => FalseValue;
            set => FalseValue = value;
        }

        /// <summary>
        /// 初始化 <see cref="BoolToVisibilityConverter"/> 的新实例。
        /// </summary>
        public BoolToVisibilityConverter()
        {
            TrueValue = Visibility.Visible;
            FalseValue = Visibility.Collapsed;
        }

        /// <inheritdoc/>
        public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}