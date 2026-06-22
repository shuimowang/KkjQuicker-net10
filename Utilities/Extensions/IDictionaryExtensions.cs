using System;
using System.Collections.Generic;

namespace KkjQuicker.Utilities.Extensions
{
    /// <summary>
    /// 提供 <see cref="IDictionary{TKey, TValue}"/> 的常用扩展方法。
    /// </summary>
    /// <remarks>
    /// 本类包含通用的词典读取、合并与比较器转换辅助方法，
    /// 适用于大多数基于 <see cref="IDictionary{TKey, TValue}"/> 的使用场景。
    /// </remarks>
    public static class IDictionaryExtensions
    {
        /// <summary>
        /// 获取指定键对应的值；若字典为 <see langword="null"/> 或键不存在，则返回指定默认值。
        /// </summary>
        /// <typeparam name="TKey">词典键类型。</typeparam>
        /// <typeparam name="TValue">词典值类型。</typeparam>
        /// <param name="dict">要读取的词典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="defaultValue">键不存在或词典为 <see langword="null"/> 时返回的默认值。</param>
        /// <returns>
        /// 若成功找到指定键，则返回对应值；否则返回 <paramref name="defaultValue"/>。
        /// </returns>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> dict,
            TKey key,
            TValue defaultValue)
        {
            if (dict == null)
                return defaultValue;

            TValue value = default!;
            return dict.TryGetValue(key, out value) ? value : defaultValue;
        }

        /// <summary>
        /// 获取指定键对应的值；若字典为 <see langword="null"/> 或键不存在，则通过委托生成默认值。
        /// </summary>
        /// <typeparam name="TKey">词典键类型。</typeparam>
        /// <typeparam name="TValue">词典值类型。</typeparam>
        /// <param name="dict">要读取的词典。</param>
        /// <param name="key">要查找的键。</param>
        /// <param name="defaultValueProvider">
        /// 用于生成默认值的委托。仅在词典为 <see langword="null"/> 或键不存在时调用，不可为 <see langword="null"/>。
        /// </param>
        /// <returns>
        /// 若成功找到指定键，则返回对应值；否则返回 <paramref name="defaultValueProvider"/> 生成的值。
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="defaultValueProvider"/> 为 <see langword="null"/>。
        /// </exception>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IDictionary<TKey, TValue> dict,
            TKey key,
            Func<TValue> defaultValueProvider)
        {
            if (defaultValueProvider == null)
                throw new ArgumentNullException(nameof(defaultValueProvider));

            if (dict == null)
                return defaultValueProvider();

            TValue value = default!;
            return dict.TryGetValue(key, out value) ? value : defaultValueProvider();
        }

        /// <summary>
        /// 创建一个新的忽略键大小写的词典副本。
        /// </summary>
        /// <typeparam name="T">词典值类型。</typeparam>
        /// <param name="dict">源词典。</param>
        /// <returns>
        /// 返回一个新的 <see cref="Dictionary{TKey, TValue}"/>，其键比较规则为 <see cref="StringComparer.OrdinalIgnoreCase"/>；
        /// 若 <paramref name="dict"/> 为 <see langword="null"/>，则返回 <see langword="null"/>。
        /// </returns>
        /// <remarks>
        /// 若源词典中存在仅大小写不同但在忽略大小写比较下视为相同的键，
        /// 则创建新词典时会抛出异常。
        /// </remarks>
        public static IDictionary<string, T>? ToIgnoreCase<T>(this IDictionary<string, T> dict)
        {
            if (dict == null)
                return null;

            return new Dictionary<string, T>(dict, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 将源词典中的键值对合并到目标词典中。
        /// </summary>
        /// <typeparam name="TKey">词典键类型。</typeparam>
        /// <typeparam name="TValue">词典值类型。</typeparam>
        /// <param name="target">要写入数据的目标词典。</param>
        /// <param name="source">提供数据的源词典。</param>
        /// <param name="overwriteExistingKeys">
        /// 指示当目标词典中已存在同名键时是否覆盖原值。
        /// 为 <see langword="true"/> 时覆盖；为 <see langword="false"/> 时仅添加不存在的键。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> 为 <see langword="null"/>。
        /// </exception>
        public static void MergeFrom<TKey, TValue>(
            this IDictionary<TKey, TValue> target,
            IDictionary<TKey, TValue> source,
            bool overwriteExistingKeys = true)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (source == null)
                return;

            foreach (var pair in source)
            {
                if (overwriteExistingKeys || !target.ContainsKey(pair.Key))
                    target[pair.Key] = pair.Value;
            }
        }

        /// <summary>
        /// 将 <paramref name="defaults"/> 中的键值对补充到目标词典中，已存在的键不会被覆盖。
        /// </summary>
        /// <typeparam name="TKey">词典键类型。</typeparam>
        /// <typeparam name="TValue">词典值类型。</typeparam>
        /// <param name="target">要补充默认值的目标词典。</param>
        /// <param name="defaults">提供默认值的词典；为 <see langword="null"/> 时直接返回。</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="target"/> 为 <see langword="null"/>。
        /// </exception>
        public static void EnsureDefaults<TKey, TValue>(
            this IDictionary<TKey, TValue> target,
            IDictionary<TKey, TValue> defaults)
        {
            target.MergeFrom(defaults, false);
        }
    }
}