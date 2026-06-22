using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace KkjQuicker.Utilities.Extensions
{
    /// <summary>
    /// 为枚举类型提供常用扩展方法。
    /// </summary>
    /// <remarks>
    /// 本类仅保留在界面显示、配置展示等场景中复用价值较高的方法。
    /// </remarks>
    public static class EnumExtensions
    {
        /// <summary>
        /// 获取枚举值的显示名称。
        /// </summary>
        /// <param name="value">要读取显示名称的枚举值。</param>
        /// <returns>
        /// 若枚举成员上标注了 <see cref="DisplayAttribute"/>，则返回其名称；
        /// 否则返回枚举值的 <see cref="Enum.ToString()"/> 结果。
        /// 若 <paramref name="value"/> 为 <see langword="null"/>，则返回空字符串。
        /// </returns>
        /// <remarks>
        /// 当枚举值未能对应到具体成员时，也会回退为 <see cref="Enum.ToString()"/> 结果。
        /// 该方法适用于 WPF 下拉框显示、状态文本展示、配置项名称显示等场景。
        /// </remarks>
        public static string? GetEnumDisplayName(this Enum value)
        {
            if (value == null)
                return string.Empty;

            string? name = value.ToString();
            MemberInfo? member = value.GetType().GetMember(name ?? "").FirstOrDefault();
            if (member == null)
                return name;

            DisplayAttribute? attribute = member.GetCustomAttribute<DisplayAttribute>(false);
            return attribute != null ? attribute.GetName() : name;
        }
    }
}