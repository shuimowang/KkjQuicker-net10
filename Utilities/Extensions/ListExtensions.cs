using System;
using System.Collections.Generic;

namespace KkjQuicker.Utilities.Extensions
{
    /// <summary>
    /// 提供对 <see cref="IList{T}"/> 和 <see cref="IEnumerable{T}"/> 的常用扩展方法。
    /// </summary>
    /// <remarks>
    /// 本类仅保留在实际项目中复用价值较高、且能明显补足原生集合操作缺口的方法，
    /// 包括安全取值、批量添加、按条件批量删除以及按键去重。
    /// </remarks>
    public static class ListExtensions
    {
        /// <summary>
        /// 获取指定索引处的元素；若索引无效或列表为 <see langword="null"/>，则返回默认值。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="list">要读取的列表。</param>
        /// <param name="index">元素索引。</param>
        /// <returns>
        /// 当索引有效时返回对应元素；否则返回 <c>default(T)</c>。
        /// </returns>
        public static T GetOrDefault<T>(this IList<T> list, int index)
        {
            return list != null && index >= 0 && index < list.Count
                ? list[index]
                : default(T);
        }

        /// <summary>
        /// 将一组元素追加到列表末尾。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="list">目标列表。</param>
        /// <param name="items">要追加的元素序列。</param>
        /// <remarks>
        /// 当 <paramref name="list"/> 或 <paramref name="items"/> 为 <see langword="null"/> 时，本方法不执行任何操作。
        /// 当目标列表本身为 <see cref="List{T}"/> 时，会优先调用其原生 <c>AddRange</c> 实现。
        /// </remarks>
        public static void AddRange<T>(this IList<T> list, IEnumerable<T> items)
        {
            if (list == null || items == null)
                return;

            var concreteList = list as List<T>;
            if (concreteList != null)
            {
                concreteList.AddRange(items);
                return;
            }

            foreach (var item in items)
            {
                list.Add(item);
            }
        }

        /// <summary>
        /// 从列表中删除所有满足条件的元素，并返回删除数量。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <param name="list">目标列表。</param>
        /// <param name="match">删除条件。</param>
        /// <returns>实际删除的元素数量。</returns>
        /// <remarks>
        /// 当 <paramref name="list"/> 或 <paramref name="match"/> 为 <see langword="null"/> 时，返回 0。
        /// 当目标列表本身为 <see cref="List{T}"/> 时，会优先调用其原生 <c>RemoveAll</c> 实现以获得 O(n) 性能。
        /// </remarks>
        public static int RemoveAll<T>(this IList<T> list, Predicate<T> match)
        {
            if (list == null || match == null)
                return 0;

            var concreteList = list as List<T>;
            if (concreteList != null)
                return concreteList.RemoveAll(match);

            int count = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (match(list[i]))
                {
                    list.RemoveAt(i);
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 根据指定键选择器返回去重后的元素列表，并保留每个键首次出现的元素。
        /// </summary>
        /// <typeparam name="T">元素类型。</typeparam>
        /// <typeparam name="TKey">去重键类型。</typeparam>
        /// <param name="source">源序列。</param>
        /// <param name="keySelector">用于提取去重键的委托。</param>
        /// <returns>
        /// 去重后的列表；若 <paramref name="source"/> 或 <paramref name="keySelector"/> 为 <see langword="null"/>，则返回空列表。
        /// </returns>
        /// <remarks>
        /// 该方法适用于 .NET Framework 4.7.2 等未内置 <c>DistinctBy</c> 的场景。
        /// </remarks>
        public static List<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
        {
            if (source == null || keySelector == null)
                return new List<T>();

            var seenKeys = new HashSet<TKey>();
            var result = new List<T>();

            foreach (var item in source)
            {
                if (seenKeys.Add(keySelector(item)))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        /// <summary>
        /// 返回指定引用在列表中首次出现的索引；若未找到或列表为 <see langword="null"/>，则返回 -1。
        /// </summary>
        /// <typeparam name="T">元素类型（引用类型）。</typeparam>
        /// <param name="list">要搜索的列表。</param>
        /// <param name="item">要查找的对象引用。</param>
        /// <returns>首次匹配的索引；未找到时返回 -1。</returns>
        public static int IndexOfByReference<T>(this IList<T> list, T item) where T : class
        {
            if (list == null)
                return -1;

            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], item))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 判断列表中是否存在与指定引用相同的元素。
        /// </summary>
        /// <typeparam name="T">元素类型（引用类型）。</typeparam>
        /// <param name="list">要搜索的列表。</param>
        /// <param name="item">要查找的对象引用。</param>
        /// <returns>若找到相同引用则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool ContainsByReference<T>(this IList<T> list, T item) where T : class
        {
            return IndexOfByReference(list, item) >= 0;
        }
    }
}