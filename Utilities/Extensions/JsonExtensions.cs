using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace KkjQuicker.Utilities.Extensions
{
    /// <summary>
    /// 提供常用的 JSON 序列化与反序列化扩展方法。
    /// </summary>
    /// <remarks>
    /// 本类聚焦于对象与 JSON 字符串之间的常见互转场景，并补充少量高复用的 JSON 辅助能力，
    /// 例如安全反序列化、JSON 有效性判断、基于 JSON 的深拷贝与结构比较。
    /// </remarks>
    public static class JsonExtensions
    {
        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <param name="indented">是否格式化输出。</param>
        /// <param name="ignoreNull">是否忽略值为 <see langword="null"/> 的属性。</param>
        /// <param name="camelCase">是否使用 camelCase 属性命名。</param>
        /// <returns>序列化后的 JSON 字符串。</returns>
        /// <remarks>
        /// 当 <paramref name="obj"/> 为 <see langword="null"/> 时，返回 JSON 字面量 <c>"null"</c>。
        /// </remarks>
        public static string ToJson(this object obj, bool indented = false, bool ignoreNull = false, bool camelCase = false)
        {
            var settings = CreateSerializerSettings(ignoreNull, camelCase);
            var formatting = indented ? Formatting.Indented : Formatting.None;
            return JsonConvert.SerializeObject(obj, formatting, settings);
        }

        /// <summary>
        /// 将 JSON 字符串反序列化为指定类型的对象。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        /// <exception cref="JsonException">JSON 格式无效或无法转换为目标类型。</exception>
        public static T? FromJson<T>(this string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// 将 JSON 字符串反序列化为指定运行时类型的对象。
        /// </summary>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <param name="type">目标类型。</param>
        /// <returns>反序列化后的对象。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> 为 <see langword="null"/>。</exception>
        /// <exception cref="JsonException">JSON 格式无效或无法转换为目标类型。</exception>
        public static object? FromJson(this string json, Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return JsonConvert.DeserializeObject(json, type);
        }

        /// <summary>
        /// 尝试将 JSON 字符串反序列化为指定类型的对象。
        /// </summary>
        /// <typeparam name="T">目标类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <param name="result">反序列化结果。</param>
        /// <returns>
        /// 反序列化成功且结果不为 <see langword="null"/> 时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// 当输入为空、格式无效、无法转换为目标类型，或反序列化结果为 <see langword="null"/> 时，
        /// 本方法不会抛出异常，而是返回 <see langword="false"/>。
        /// </remarks>
        public static bool TryFromJson<T>(this string json, out T? result)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                result = default(T);
                return false;
            }

            try
            {
                T? value = JsonConvert.DeserializeObject<T>(json);
                if (object.Equals(value, null))
                {
                    result = default(T);
                    return false;
                }
                result = value;
                return true;
            }
            catch (Exception)
            {
                result = default(T);
                return false;
            }
        }

        /// <summary>
        /// 通过 JSON 序列化与反序列化创建对象的深拷贝。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="obj">要复制的对象。</param>
        /// <returns>
        /// 深拷贝后的新对象。
        /// 若 <paramref name="obj"/> 为 <see langword="null"/>，则返回 <c>default(T)</c>。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 本方法适用于配置对象、简单对象图、临时编辑副本等场景。
        /// </para>
        /// <para>
        /// 仅对可被 Newtonsoft.Json 正常序列化和反序列化的对象有效。
        /// </para>
        /// </remarks>
        public static T? DeepClone<T>(this T obj)
        {
            if (object.Equals(obj, null))
                return default(T);

            string json = JsonConvert.SerializeObject(obj);
            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// 判断字符串是否为有效的 JSON。
        /// </summary>
        /// <param name="json">要检查的字符串。</param>
        /// <returns>
        /// 当字符串是有效 JSON 对象、数组或 JSON 字面量时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool IsJson(this string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                JToken.Parse(json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 判断两个对象序列化后的 JSON 结构是否相等。
        /// </summary>
        /// <param name="left">左侧对象。</param>
        /// <param name="right">右侧对象。</param>
        /// <returns>
        /// 当两个对象序列化后的 JSON 结构相等时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        /// <remarks>
        /// 基于 JSON 结构比较，而非对象引用或 <see cref="object.Equals(object)"/>。
        /// 仅对可被 Newtonsoft.Json 正常序列化的对象有效；
        /// 若对象包含循环引用或不可序列化成员，将抛出 <see cref="JsonException"/>。
        /// </remarks>
        public static bool JsonEquals(this object left, object right)
        {
            if (ReferenceEquals(left, right))
                return true;

            if (left == null || right == null)
                return false;

            JToken leftToken = JToken.FromObject(left);
            JToken rightToken = JToken.FromObject(right);
            return JToken.DeepEquals(leftToken, rightToken);
        }

        private static JsonSerializerSettings CreateSerializerSettings(bool ignoreNull, bool camelCase)
        {
            var settings = new JsonSerializerSettings();

            if (ignoreNull)
            {
                settings.NullValueHandling = NullValueHandling.Ignore;
            }

            if (camelCase)
            {
                settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            }

            return settings;
        }
    }
}