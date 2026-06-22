using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 支持单值与多值绑定的轻量合并转换器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该转换器适合用于 <see cref="MultiBinding"/> 场景下的描述文本合成，
    /// 例如将类型、动作、数据、说明等多个字段拼接为一段 ToolTip 或摘要文本。
    /// </para>
    /// <para>
    /// 它同时支持简单数值求和，因此也可用于少量界面尺寸、间距等数值加法场景。
    /// </para>
    /// <para>
    /// 设计目标是减少界面层重复的轻量拼接逻辑，而不是替代复杂格式化或业务计算。
    /// </para>
    /// <para>
    /// 当所有有效输入值均可解析为数字时，优先执行数值求和，结果再转换为目标类型；
    /// 否则退回字符串拼接。目标类型不影响数值路径的判断。
    /// </para>
    /// </remarks>
    [ValueConversion(typeof(object), typeof(object))]
    public class ValueCombineConverter : IValueConverter, IMultiValueConverter
    {
        /// <summary>
        /// 获取或设置字符串拼接时使用的分隔符。
        /// </summary>
        /// <remarks>
        /// 默认值为空字符串。
        /// </remarks>
        public string Separator { get; set; }

        /// <summary>
        /// 初始化 <see cref="ValueCombineConverter"/> 的新实例。
        /// </summary>
        public ValueCombineConverter()
        {
            Separator = string.Empty;
        }

        #region IValueConverter（单值）

        /// <summary>
        /// 对单个值执行轻量合并。
        /// </summary>
        /// <param name="value">绑定源值。</param>
        /// <param name="targetType">绑定目标类型。</param>
        /// <param name="parameter">
        /// 附加值；在字符串模式下作为额外文本参与拼接，在数值模式下作为附加数值参与求和。
        /// </param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>求和后转换为目标类型的数值，或拼接后的字符串。</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (IsIgnoredValue(value))
                return parameter;

            if (TryToDouble(value, culture, out double v1))
            {
                if (!IsIgnoredValue(parameter) && TryToDouble(parameter, culture, out double v2))
                    return ChangeTypeSafe(v1 + v2, targetType, culture, value.GetType());

                if (IsIgnoredValue(parameter))
                    return ChangeTypeSafe(v1, targetType, culture, value.GetType());
            }

            return CombineStrings(new[] { value }, parameter);
        }

        /// <summary>
        /// 不支持反向转换，始终返回 <see cref="Binding.DoNothing"/>。
        /// </summary>
        /// <param name="value">目标值。</param>
        /// <param name="targetType">绑定源类型。</param>
        /// <param name="parameter">转换参数。</param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>始终返回 <see cref="Binding.DoNothing"/>。</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        #endregion IValueConverter（单值）

        #region IMultiValueConverter（多值）

        /// <summary>
        /// 对多个值执行轻量合并。
        /// </summary>
        /// <param name="values">绑定源值数组。</param>
        /// <param name="targetType">绑定目标类型。</param>
        /// <param name="parameter">
        /// 附加值；在字符串模式下作为额外文本参与拼接，在数值模式下作为附加数值参与求和。
        /// </param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>求和后转换为目标类型的数值，或拼接后的字符串。</returns>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0)
                return parameter;

            var effectiveValues = values.Where(v => !IsIgnoredValue(v)).ToArray();
            if (effectiveValues.Length == 0)
                return parameter;

            bool allNumeric = effectiveValues.All(v => TryToDouble(v, culture, out _));
            double paramNumber = 0d;
            bool paramNumeric = IsIgnoredValue(parameter)
                || TryToDouble(parameter, culture, out paramNumber);

            if (allNumeric && paramNumeric)
            {
                double sum = effectiveValues.Sum(v => ToDouble(v, culture));
                if (!IsIgnoredValue(parameter))
                    sum += paramNumber;

                return ChangeTypeSafe(sum, targetType, culture);
            }

            return CombineStrings(effectiveValues, parameter);
        }

        /// <summary>
        /// 不支持反向转换，始终对每个绑定源返回 <see cref="Binding.DoNothing"/>。
        /// </summary>
        /// <param name="value">目标值。</param>
        /// <param name="targetTypes">绑定源类型数组。</param>
        /// <param name="parameter">转换参数。</param>
        /// <param name="culture">转换使用的区域性信息。</param>
        /// <returns>与 <paramref name="targetTypes"/> 等长的 <see cref="Binding.DoNothing"/> 数组。</returns>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => targetTypes?.Select(_ => Binding.DoNothing).ToArray() ?? Array.Empty<object>();

        #endregion IMultiValueConverter（多值）

        #region 辅助方法

        private string CombineStrings(IEnumerable values, object parameter)
        {
            var parts = values.Cast<object>()
                .Where(v => !IsIgnoredValue(v))
                .Select(v => v.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (!IsIgnoredValue(parameter))
            {
                var parameterText = parameter.ToString();
                if (!string.IsNullOrWhiteSpace(parameterText))
                    parts.Add(parameterText);
            }

            return string.Join(Separator ?? string.Empty, parts);
        }

        private static bool TryToDouble(object value, CultureInfo culture, out double result)
        {
            result = 0d;

            if (IsIgnoredValue(value))
                return false;

            if (IsNumber(value))
            {
                result = System.Convert.ToDouble(value, culture);
                return true;
            }

            if (value is string text)
                return double.TryParse(text, NumberStyles.Any, culture, out result);

            return false;
        }

        private static double ToDouble(object value, CultureInfo culture)
        {
            TryToDouble(value, culture, out double result);
            return result;
        }

        private static object ChangeTypeSafe(object value, Type targetType, CultureInfo culture, Type? fallbackType = null)
        {
            if (targetType == null)
                return value;

            Type effectiveTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (effectiveTargetType == typeof(object) || effectiveTargetType.IsAssignableFrom(value.GetType()))
                return value;

            try
            {
                return System.Convert.ChangeType(value, effectiveTargetType, culture);
            }
            catch
            {
                if (fallbackType != null)
                {
                    Type effectiveFallbackType = Nullable.GetUnderlyingType(fallbackType) ?? fallbackType;
                    try
                    {
                        return System.Convert.ChangeType(value, effectiveFallbackType, culture);
                    }
                    catch { }
                }

                return value;
            }
        }

        private static bool IsNumber(object value)
        {
            return value is sbyte || value is byte ||
                   value is short || value is ushort ||
                   value is int || value is uint ||
                   value is long || value is ulong ||
                   value is float || value is double ||
                   value is decimal;
        }

        private static bool IsIgnoredValue(object value)
        {
            return value == null
                || value == DependencyProperty.UnsetValue
                || value == Binding.DoNothing;
        }

        #endregion 辅助方法
    }
}