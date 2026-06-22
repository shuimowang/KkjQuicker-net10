using System;
using System.Collections.Generic;
using System.Globalization;

namespace KkjQuicker.Utilities.Extensions
{
    /// <summary>
    /// 提供 <see cref="IDictionary{TKey, TValue}"/>（键为 <see cref="string"/>、值为 <see cref="object"/>）
    /// 的安全读取扩展方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类适用于 <c>Dictionary&lt;string, object&gt;</c>、参数字典、弱类型上下文数据等场景，
    /// 便于在读取时安全地转换为常用基础类型。
    /// </para>
    /// <para>
    /// 若键不存在、值为 <see langword="null"/>、值为 <see cref="DBNull.Value"/>，
    /// 或类型转换失败，则 <c>GetXxx</c> 方法返回调用方指定的默认值。
    /// </para>
    /// </remarks>
    public static class ObjectDictionaryExtensions
    {
        /// <summary>
        /// 安全获取指定键对应的原始值。
        /// </summary>
        /// <param name="dict">要读取的字典。</param>
        /// <param name="key">键名。</param>
        /// <returns>
        /// 若字典为 <see langword="null"/>、键名为 <see langword="null"/> 或空字符串、键不存在、
        /// 对应值为 <see langword="null"/> 或 <see cref="DBNull.Value"/>，则返回 <see langword="null"/>；
        /// 否则返回对应的原始值。
        /// </returns>
        public static object? GetValueOrNull(this IDictionary<string, object> dict, string key)
        {
            if (dict == null)
                return null;

            if (string.IsNullOrEmpty(key))
                return null;

            object? value;
            if (!dict.TryGetValue(key, out value))
                return null;

            return value == DBNull.Value ? null : value;
        }

        /// <summary>
        /// 判断字典中是否包含指定键，且其值不为 <see langword="null"/> 或 <see cref="DBNull.Value"/>。
        /// </summary>
        /// <param name="dict">要检查的字典。</param>
        /// <param name="key">键名。</param>
        /// <returns>
        /// 当字典中存在指定键，且对应值不为 <see langword="null"/> 且不为 <see cref="DBNull.Value"/> 时返回 <see langword="true"/>；
        /// 否则返回 <see langword="false"/>。
        /// </returns>
        public static bool HasValue(this IDictionary<string, object> dict, string key)
        {
            if (dict == null)
                return false;

            if (string.IsNullOrEmpty(key))
                return false;

            object? value;
            return dict.TryGetValue(key, out value) && value != null && value != DBNull.Value;
        }

        /// <summary>
        /// 获取指定键对应的布尔值；若键不存在、值为空或转换失败，则返回默认值。
        /// </summary>
        /// <param name="dict">要读取的字典。</param>
        /// <param name="key">键名。</param>
        /// <param name="defaultValue">当读取失败时返回的默认值。</param>
        /// <returns>转换后的布尔值，或 <paramref name="defaultValue"/>。</returns>
        public static bool GetBool(this IDictionary<string, object> dict, string key, bool defaultValue = false)
        {
            return ToBoolOrDefault(dict.GetValueOrNull(key), defaultValue);
        }

        /// <summary>
        /// 获取指定键对应的整数值；若键不存在、值为空或转换失败，则返回默认值。
        /// </summary>
        /// <param name="dict">要读取的字典。</param>
        /// <param name="key">键名。</param>
        /// <param name="defaultValue">当读取失败时返回的默认值。</param>
        /// <param name="minValue">允许的最小值；为 <see langword="null"/> 时不限制下限。</param>
        /// <param name="maxValue">允许的最大值；为 <see langword="null"/> 时不限制上限。</param>
        /// <returns>转换后并按需限制范围的整数值，或限制后的 <paramref name="defaultValue"/>。</returns>
        public static int GetInt(
            this IDictionary<string, object> dict,
            string key,
            int defaultValue = 0,
            int? minValue = null,
            int? maxValue = null)
        {
            return Clamp(ToIntOrDefault(dict.GetValueOrNull(key), defaultValue), minValue, maxValue);
        }

        /// <summary>
        /// 获取指定键对应的双精度浮点值；若键不存在、值为空或转换失败，则返回默认值。
        /// </summary>
        /// <param name="dict">要读取的字典。</param>
        /// <param name="key">键名。</param>
        /// <param name="defaultValue">当读取失败时返回的默认值。</param>
        /// <param name="minValue">允许的最小值；为 <see langword="null"/> 时不限制下限。</param>
        /// <param name="maxValue">允许的最大值；为 <see langword="null"/> 时不限制上限。</param>
        /// <returns>转换后并按需限制范围的双精度浮点值，或限制后的 <paramref name="defaultValue"/>。</returns>
        public static double GetDouble(
            this IDictionary<string, object> dict,
            string key,
            double defaultValue = 0,
            double? minValue = null,
            double? maxValue = null)
        {
            double value = ToDoubleOrDefault(dict.GetValueOrNull(key), defaultValue);

            if (double.IsNaN(value) || double.IsInfinity(value))
                value = defaultValue;

            return Clamp(value, minValue, maxValue);
        }

        /// <summary>
        /// 获取指定键对应的字符串值；若键不存在、值为空，则返回默认值。
        /// </summary>
        /// <param name="dict">要读取的字典。</param>
        /// <param name="key">键名。</param>
        /// <param name="defaultValue">当读取失败时返回的默认值。</param>
        /// <returns>对象的字符串表示，或 <paramref name="defaultValue"/>。</returns>
        /// <remarks>
        /// 本方法不会将空字符串视为失败；若原始值为非空对象，则返回其 <see cref="object.ToString"/> 结果。
        /// </remarks>
        public static string? GetString(this IDictionary<string, object> dict, string key, string? defaultValue = null)
        {
            return ToStringOrDefault(dict.GetValueOrNull(key), defaultValue);
        }

        private static bool ToBoolOrDefault(object? value, bool defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is bool)
                return (bool)value;

            string? text = value as string;
            if (text != null)
            {
                text = text.Trim().ToLowerInvariant();

                if (text == "true" || text == "1" || text == "yes" || text == "y" || text == "on")
                    return true;

                if (text == "false" || text == "0" || text == "no" || text == "n" || text == "off")
                    return false;

                return defaultValue;
            }

            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static int ToIntOrDefault(object? value, int defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is int)
                return (int)value;

            string? text = value as string;
            if (text != null)
            {
                int parsed;
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                    ? parsed
                    : defaultValue;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static double ToDoubleOrDefault(object? value, double defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            if (value is double)
                return (double)value;

            if (value is float)
                return (float)value;

            string? text = value as string;
            if (text != null)
            {
                double parsed;
                return double.TryParse(
                    text,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out parsed)
                    ? parsed
                    : defaultValue;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static string? ToStringOrDefault(object? value, string? defaultValue)
        {
            if (value == null || value == DBNull.Value)
                return defaultValue;

            try
            {
                return value.ToString();
            }
            catch
            {
                return defaultValue;
            }
        }

        private static int Clamp(int value, int? minValue, int? maxValue)
        {
            if (minValue.HasValue && maxValue.HasValue && minValue.Value > maxValue.Value)
                throw new ArgumentException("minValue 不能大于 maxValue。", "minValue");

            if (minValue.HasValue && value < minValue.Value)
                return minValue.Value;

            if (maxValue.HasValue && value > maxValue.Value)
                return maxValue.Value;

            return value;
        }

        private static double Clamp(double value, double? minValue, double? maxValue)
        {
            if (minValue.HasValue && maxValue.HasValue && minValue.Value > maxValue.Value)
                throw new ArgumentException("minValue 不能大于 maxValue。", "minValue");

            if (minValue.HasValue && value < minValue.Value)
                return minValue.Value;

            if (maxValue.HasValue && value > maxValue.Value)
                return maxValue.Value;

            return value;
        }
    }
}