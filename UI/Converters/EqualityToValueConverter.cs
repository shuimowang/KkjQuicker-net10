using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 将绑定值与参数值进行等值比较，并输出 <typeparamref name="T"/> 类型结果。
    /// </summary>
    /// <typeparam name="T">输出值类型。</typeparam>
    /// <remarks>
    /// <para>
    /// 相等时输出 <see cref="TrueValue"/>，不相等时输出 <see cref="FalseValue"/>。
    /// </para>
    /// <para>
    /// 支持通过 <c>parameter</c> 传入形如
    /// <c>CompareValue|TrueValue|FalseValue|NotEqualValue</c> 的参数字符串。
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>CompareValue</c>：用于与绑定值比较的目标值。</description></item>
    /// <item><description><c>TrueValue</c>：相等时的输出值，覆盖 <see cref="TrueValue"/> 属性。</description></item>
    /// <item><description><c>FalseValue</c>：不相等时的输出值，覆盖 <see cref="FalseValue"/> 属性。</description></item>
    /// <item><description>
    /// <c>NotEqualValue</c>：仅用于 <see cref="ConvertBack"/>。
    /// 当输入值等于 <c>FalseValue</c> 时，将此值写回绑定源。
    /// </description></item>
    /// </list>
    /// <para>
    /// <see cref="ConvertBack"/> 不考虑 <see cref="IsReversed"/>，
    /// 因为 <see cref="IsReversed"/> 仅表示显示层逻辑反转，不改变源值的"等/不等"语义。
    /// </para>
    /// <para>
    /// 参数中支持以下占位标记：
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>{null}</c>：对于引用类型映射为 <see langword="null"/>；
    /// 对于值类型映射为 <c>default(T)</c>（即零值）。
    /// </description></item>
    /// <item><description>
    /// <c>{empty}</c>：若 <typeparamref name="T"/> 为 <see cref="string"/>，映射为空字符串；
    /// 否则与 <c>{null}</c> 等价。
    /// </description></item>
    /// </list>
    /// <para>
    /// 当比较参数为长度为 0 的字符串时（即 <c>parameter</c> 为空字符串，
    /// 或管道分隔参数中 <c>CompareValue</c> 部分为空），
    /// 视为"匹配空白字符串"：仅当绑定值为 <see langword="null"/>、
    /// 空字符串或纯空白字符串时判定相等。
    /// </para>
    /// </remarks>
    [ValueConversion(typeof(object), typeof(object))]
    public class EqualityToValueConverter<T> : IValueConverter
    {
        /// <summary>
        /// 获取或设置相等时输出的值。
        /// </summary>
        public T TrueValue { get; set; }

        /// <summary>
        /// 获取或设置不相等时输出的值。
        /// </summary>
        public T FalseValue { get; set; }

        /// <summary>
        /// 获取或设置是否在 <see cref="Convert"/> 中反转比较结果。
        /// </summary>
        /// <remarks>
        /// 默认值为 <see langword="false"/>。
        /// 仅影响 <see cref="Convert"/>，不影响 <see cref="ConvertBack"/>。
        /// </remarks>
        public bool IsReversed { get; set; }

        /// <summary>
        /// 获取或设置字符串比较方式。
        /// </summary>
        /// <remarks>
        /// 默认值为 <see cref="StringComparison.OrdinalIgnoreCase"/>。
        /// 仅在绑定值与比较值均为字符串时生效。
        /// </remarks>
        public StringComparison Comparison { get; set; }

        /// <summary>
        /// 初始化 <see cref="EqualityToValueConverter{T}"/> 的新实例。
        /// </summary>
        /// <remarks>
        /// 当 <typeparamref name="T"/> 为 <see cref="string"/> 时，
        /// 默认将 <see cref="TrueValue"/> 设为 <c>"True"</c>，
        /// 将 <see cref="FalseValue"/> 设为 <c>"False"</c>。
        /// 其他类型的 <see cref="TrueValue"/> 与 <see cref="FalseValue"/> 默认均为 <c>default(T)</c>，
        /// 通常需通过属性或 <c>parameter</c> 字符串显式指定。
        /// </remarks>
        public EqualityToValueConverter()
        {
            Comparison = StringComparison.OrdinalIgnoreCase;

            if (typeof(T) == typeof(string))
            {
                TrueValue = (T)(object)"True";
                FalseValue = (T)(object)"False";
            }
        }

        /// <summary>
        /// 将输入值与参数中的比较值进行等值比较，并返回相应结果。
        /// </summary>
        /// <param name="value">绑定源值。</param>
        /// <param name="targetType">绑定目标类型，当前实现不依赖该参数。</param>
        /// <param name="parameter">
        /// 比较参数，可为普通对象，或形如 <c>CompareValue|TrueValue|FalseValue|NotEqualValue</c> 的字符串。
        /// </param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>相等时返回真值，不相等时返回假值。</returns>
        public virtual object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parsed = ParseParameter(parameter, culture);

            bool isEqual = AreEqual(value, parsed.CompareTo, culture, Comparison);
            if (IsReversed)
                isEqual = !isEqual;

            return isEqual ? (object)parsed.TrueVal : parsed.FalseVal;
        }

        /// <summary>
        /// 将目标值转换回源值。
        /// </summary>
        /// <param name="value">目标值，通常为 <see cref="TrueValue"/> 或 <see cref="FalseValue"/>。</param>
        /// <param name="targetType">绑定源属性类型。</param>
        /// <param name="parameter">
        /// 比较参数，可为普通对象，或形如 <c>CompareValue|TrueValue|FalseValue|NotEqualValue</c> 的字符串。
        /// </param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>
        /// 当输入等于真值时，返回 <c>CompareValue</c> 转换后的结果；
        /// 当输入等于假值且 <c>NotEqualValue</c> 已指定时，返回 <c>NotEqualValue</c> 转换后的结果；
        /// 否则返回 <see cref="Binding.DoNothing"/>，不写回绑定源。
        /// </returns>
        public virtual object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parsed = ParseParameter(parameter, culture);

            if (!TryConvertToT(value, culture, out T incoming))
                return Binding.DoNothing;

            if (EqualityComparer<T>.Default.Equals(incoming, parsed.TrueVal))
                return ConvertToTargetType(parsed.CompareTo, targetType, culture);

            if (parsed.HasNotEqualVal && EqualityComparer<T>.Default.Equals(incoming, parsed.FalseVal))
                return ConvertToTargetType(parsed.NotEqualVal, targetType, culture);

            return Binding.DoNothing;
        }

        #region Internals

        private struct Parsed
        {
            public object CompareTo;
            public T TrueVal;
            public T FalseVal;
            public T NotEqualVal;
            public bool HasNotEqualVal;
        }

        private Parsed ParseParameter(object parameter, CultureInfo culture)
        {
            var result = new Parsed
            {
                CompareTo = parameter,
                TrueVal = TrueValue,
                FalseVal = FalseValue,
            };

            if (!(parameter is string text))
                return result;

            var parts = text.Split(new[] { '|' }, StringSplitOptions.None);

            if (parts.Length > 0)
                result.CompareTo = parts[0].Trim();

            if (parts.Length > 1)
                result.TrueVal = ConvertFromParameterToken(parts[1].Trim(), culture);

            if (parts.Length > 2)
                result.FalseVal = ConvertFromParameterToken(parts[2].Trim(), culture);

            if (parts.Length > 3)
            {
                result.NotEqualVal = ConvertFromParameterToken(parts[3].Trim(), culture);
                result.HasNotEqualVal = true;
            }

            return result;
        }

        private static T ConvertFromParameterToken(string token, CultureInfo culture)
        {
            if (string.Equals(token, "{null}", StringComparison.OrdinalIgnoreCase))
                return default;

            if (string.Equals(token, "{empty}", StringComparison.OrdinalIgnoreCase))
                return typeof(T) == typeof(string) ? (T)(object)string.Empty : default;

            return TryConvertFromString(token, culture, out T result) ? result : default;
        }

        // 调整判空短路与空字符串参数分支的顺序：
        // 空字符串参数的"匹配空白语义"需要覆盖 null 绑定值，必须置于 null 短路之前。
        private static bool AreEqual(object value, object parameter, CultureInfo culture, StringComparison comparison)
        {
            // 空字符串参数：视为"匹配空白字符串"
            // 仅当绑定值为 null、空字符串或纯空白字符串时判定相等
            if (parameter is string paramText && paramText.Length == 0)
                return value == null || (value is string sv && string.IsNullOrWhiteSpace(sv));

            if (value == null && parameter == null)
                return true;

            if (value == null || parameter == null)
                return false;

            if (value is string leftText && parameter is string rightText)
                return string.Equals(leftText, rightText, comparison);

            var valueType = value.GetType();
            var parameterType = parameter.GetType();
            var valueUnderlyingType = Nullable.GetUnderlyingType(valueType) ?? valueType;
            var parameterUnderlyingType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;

            try
            {
                if (valueUnderlyingType.IsEnum)
                {
                    // 将参数转为字符串后解析为枚举，兼容整数字符串（如 "1"）
                    var enumText = parameter as string ?? parameter.ToString();
                    var parsedEnum = Enum.Parse(valueUnderlyingType, enumText, ignoreCase: true);
                    return Equals(value, parsedEnum);
                }

                if (valueUnderlyingType != parameterUnderlyingType)
                {
                    var converted = System.Convert.ChangeType(parameter, valueUnderlyingType, culture);
                    return Equals(value, converted);
                }

                return Equals(value, parameter);
            }
            catch
            {
                try
                {
                    var converter = TypeDescriptor.GetConverter(valueUnderlyingType);
                    if (converter != null && converter.CanConvertFrom(parameterType))
                        return Equals(value, converter.ConvertFrom(null, culture, parameter));
                }
                catch
                {
                }

                return false;
            }
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

                if (string.IsNullOrWhiteSpace(text))
                {
                    // 仅可空类型接受空/空白字符串（映射为 null）
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
                    result = (T)converter.ConvertFromString(null, culture, text);
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
                // 引用类型和 Nullable<T> 接受 null；result 已为 default(T)（即 null）
                return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
            }

            if (value is T t)
            {
                result = t;
                return true;
            }

            // string 提前处理，避免后续枚举分支的重复判断
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

                if (value is IConvertible)
                {
                    result = (T)System.Convert.ChangeType(value, targetType, culture);
                    return true;
                }

                var converter = TypeDescriptor.GetConverter(targetType);
                if (converter != null && converter.CanConvertFrom(value.GetType()))
                {
                    result = (T)converter.ConvertFrom(null, culture, value);
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static object ConvertToTargetType(object source, Type targetType, CultureInfo culture)
        {
            if (targetType == null)
                return Binding.DoNothing;

            Type underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (source == null)
                return null;

            // 可空类型 + 空字符串 → null
            if (underlyingType != targetType && source is string s && s.Length == 0)
                return null;

            if (underlyingType.IsInstanceOfType(source))
                return source;

            try
            {
                if (underlyingType.IsEnum)
                {
                    var enumText = source as string ?? source.ToString();
                    return Enum.Parse(underlyingType, enumText, ignoreCase: true);
                }

                if (source is IConvertible)
                    return System.Convert.ChangeType(source, underlyingType, culture);

                var converter = TypeDescriptor.GetConverter(underlyingType);
                if (converter != null && converter.CanConvertFrom(source.GetType()))
                    return converter.ConvertFrom(null, culture, source);
            }
            catch
            {
            }

            return Binding.DoNothing;
        }

        #endregion
    }

    /// <summary>
    /// 将绑定值与参数值进行等值比较，并输出 <see cref="string"/> 类型结果的专用转换器。
    /// </summary>
    /// <remarks>
    /// 提供具体类型，便于在 XAML 中直接实例化，无需指定泛型参数。
    /// 默认真值为 <c>"True"</c>，默认假值为 <c>"False"</c>。
    /// </remarks>
    [ValueConversion(typeof(object), typeof(string))]
    public class EqualityToStringConverter : EqualityToValueConverter<string>
    {
    }

    /// <summary>
    /// 将绑定值与参数值进行等值比较，并输出 <see cref="object"/> 类型结果的专用转换器。
    /// </summary>
    /// <remarks>
    /// 提供具体类型，便于在 XAML 中直接实例化，无需指定泛型参数。
    /// <para>
    /// 注意：<see cref="EqualityToValueConverter{T}.TrueValue"/> 与
    /// <see cref="EqualityToValueConverter{T}.FalseValue"/> 默认均为 <see langword="null"/>，
    /// 通常需通过属性赋值或 <c>parameter</c> 字符串中的 <c>TrueValue|FalseValue</c> 部分显式指定。
    /// </para>
    /// </remarks>
    [ValueConversion(typeof(object), typeof(object))]
    public class EqualityToObjectConverter : EqualityToValueConverter<object>
    {
    }

    /// <summary>
    /// 将绑定值与参数值比较，并转换为 <see cref="Visibility"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 默认情况下，相等时返回 <see cref="Visibility.Visible"/>，
    /// 不相等时返回 <see cref="FalseVisibility"/>（默认 <see cref="Visibility.Collapsed"/>）。
    /// </para>
    /// <para>
    /// 支持 <see cref="EqualityToValueConverter{T}.IsReversed"/>、
    /// <see cref="EqualityToValueConverter{T}.Comparison"/>，
    /// 以及 <c>parameter</c> 字符串格式，详见基类文档。
    /// </para>
    /// <para>
    /// 不支持反向转换，<see cref="IValueConverter.ConvertBack"/> 始终返回
    /// <see cref="Binding.DoNothing"/>。
    /// </para>
    /// </remarks>
    [ValueConversion(typeof(object), typeof(Visibility))]
    public class EqualityToVisibilityConverter : EqualityToValueConverter<Visibility>
    {
        /// <summary>
        /// 获取或设置条件不满足时返回的可见性。
        /// </summary>
        /// <remarks>
        /// 默认值为 <see cref="Visibility.Collapsed"/>，可根据需要改为 <see cref="Visibility.Hidden"/>。
        /// 等价于直接设置基类的 <see cref="EqualityToValueConverter{T}.FalseValue"/>。
        /// </remarks>
        public Visibility FalseVisibility
        {
            get => FalseValue;
            set => FalseValue = value;
        }

        /// <summary>
        /// 初始化 <see cref="EqualityToVisibilityConverter"/> 的新实例。
        /// </summary>
        public EqualityToVisibilityConverter()
        {
            TrueValue = Visibility.Visible;
            FalseValue = Visibility.Collapsed;
        }

        /// <inheritdoc/>
        public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>
    /// 将绑定值与参数值进行等值比较，并输出 <see cref="bool"/> 类型结果的专用转换器。
    /// </summary>
    /// <remarks>
    /// 提供具体类型，便于在 XAML 中直接实例化，无需指定泛型参数。
    /// 默认相等时返回 <see langword="true"/>，不相等时返回 <see langword="false"/>。
    /// </remarks>
    [ValueConversion(typeof(object), typeof(bool))]
    public class EqualityToBoolConverter : EqualityToValueConverter<bool>
    {
        /// <summary>
        /// 初始化 <see cref="EqualityToBoolConverter"/> 的新实例。
        /// </summary>
        public EqualityToBoolConverter()
        {
            TrueValue = true;
            FalseValue = false;
        }
    }
}