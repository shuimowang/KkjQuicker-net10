using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KkjQuicker.Utilities.Extensions
{
    /// <summary>
    /// 为 <see cref="string"/> 提供常用扩展方法。
    /// </summary>
    /// <remarks>
    /// 本类聚焦于常见字符串处理场景，包括按行分割、界面预览文本生成、
    /// 基础类型转换、正则匹配以及多候选比较等。
    /// </remarks>
    public static class StringExtensions
    {
        private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(1);

        /// <summary>
        /// 按行分割字符串。
        /// </summary>
        /// <param name="value">要分割的字符串。</param>
        /// <param name="removeEmptyEntries">是否移除空行。</param>
        /// <returns>
        /// 分割后的字符串数组。
        /// 若 <paramref name="value"/> 为 <see langword="null"/> 或空字符串，则返回空数组。
        /// </returns>
        /// <remarks>
        /// 同时支持 <c>\r\n</c>、<c>\n</c> 和 <c>\r</c> 三种常见换行形式。
        /// </remarks>
        public static string[] SplitLines(this string? value, bool removeEmptyEntries = true)
        {
            if (string.IsNullOrEmpty(value))
                return Array.Empty<string>();

            return value.Split(
                new[] { "\r\n", "\n", "\r" },
                removeEmptyEntries ? StringSplitOptions.RemoveEmptyEntries : StringSplitOptions.None);
        }

        /// <summary>
        /// 将字符串整理为适合界面展示的预览文本，并按最大字符数与最大行数进行裁剪。
        /// </summary>
        /// <param name="value">要处理的字符串。</param>
        /// <param name="maxChars">
        /// 结果允许的最大字符数。
        /// 当该值小于等于 0 时，返回 <see cref="string.Empty"/>。
        /// </param>
        /// <param name="maxLines">
        /// 结果允许的最大行数。
        /// 当该值小于等于 0 时，返回 <see cref="string.Empty"/>。
        /// </param>
        /// <returns>
        /// 适合界面展示的预览文本。
        /// 若原文本超过指定字符数或行数，则会被裁剪，并在末尾追加 <c>"..."</c>；
        /// 若 <paramref name="value"/> 为 <see langword="null"/>、空字符串或仅包含空白字符，则返回 <see cref="string.Empty"/>。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 该方法适用于复制提示、状态提示、内容预览、日志摘要等界面展示场景。
        /// </para>
        /// <para>
        /// 本方法会先去除整体首尾空白，并统一换行符后再处理；
        /// 会尽量保留原有多行结构，但限制最终输出的最大行数与最大字符数。
        /// </para>
        /// <para>
        /// 返回结果中的换行使用当前运行环境的标准换行符。
        /// </para>
        /// <para>
        /// 本方法按 <see cref="string.Length"/> 计数，不保证按用户可见字符边界裁剪。
        /// </para>
        /// </remarks>
        public static string ToPreviewText(this string? value, int maxChars, int maxLines)
        {
            if (string.IsNullOrEmpty(value) || maxChars <= 0 || maxLines <= 0)
            {
                return string.Empty;
            }

            value = value.Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            value = NormalizeLineBreaks(value);

            string[] lines = value.Split('\n');
            bool truncatedByLines = lines.Length > maxLines;

            int lineCount = truncatedByLines ? maxLines : lines.Length;
            StringBuilder sb = new(value.Length);

            for (int i = 0; i < lineCount; i++)
            {
                if (i > 0)
                {
                    sb.Append(Environment.NewLine);
                }

                sb.Append(lines[i]);
            }

            string result = sb.ToString();
            bool truncatedByChars = result.Length > maxChars;

            if (truncatedByChars || truncatedByLines)
            {
                return AppendEllipsisWithinLimit(result, maxChars);
            }

            return result;
        }

        /// <summary>
        /// 尝试将字符串转换为整数；失败时返回默认值。
        /// </summary>
        /// <param name="value">要转换的字符串。</param>
        /// <param name="defaultValue">转换失败时返回的默认值。</param>
        /// <returns>转换后的整数值，或 <paramref name="defaultValue"/>。</returns>
        /// <remarks>
        /// 按 <see cref="CultureInfo.InvariantCulture"/> 解析。
        /// </remarks>
        public static int ToInt(this string? value, int defaultValue = 0)
        {
            int result;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                ? result
                : defaultValue;
        }

        /// <summary>
        /// 尝试将字符串转换为双精度浮点数；失败时返回默认值。
        /// </summary>
        /// <param name="value">要转换的字符串。</param>
        /// <param name="defaultValue">转换失败时返回的默认值。</param>
        /// <returns>转换后的双精度浮点值，或 <paramref name="defaultValue"/>。</returns>
        /// <remarks>
        /// 按 <see cref="CultureInfo.InvariantCulture"/> 解析。
        /// </remarks>
        public static double ToDouble(this string? value, double defaultValue = 0)
        {
            double result;
            return double.TryParse(
                value,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out result)
                ? result
                : defaultValue;
        }

        /// <summary>
        /// 尝试将字符串转换为布尔值；失败时返回默认值。
        /// </summary>
        /// <param name="value">要转换的字符串。</param>
        /// <param name="defaultValue">转换失败时返回的默认值。</param>
        /// <returns>转换后的布尔值，或 <paramref name="defaultValue"/>。</returns>
        /// <remarks>
        /// 支持以下真值：<c>true</c>、<c>1</c>、<c>yes</c>、<c>y</c>、<c>on</c>。
        /// 支持以下假值：<c>false</c>、<c>0</c>、<c>no</c>、<c>n</c>、<c>off</c>。
        /// 比较时忽略大小写。
        /// </remarks>
        public static bool ToBool(this string? value, bool defaultValue = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            value = value.Trim().ToLowerInvariant();

            if (value == "true" || value == "1" || value == "yes" || value == "y" || value == "on")
                return true;

            if (value == "false" || value == "0" || value == "no" || value == "n" || value == "off")
                return false;

            return defaultValue;
        }

        /// <summary>
        /// 判断字符串是否匹配指定正则表达式。
        /// </summary>
        /// <param name="value">要匹配的字符串。</param>
        /// <param name="pattern">正则表达式模式。</param>
        /// <param name="options">正则匹配选项。</param>
        /// <param name="timeout">正则匹配超时；为 <see langword="null"/> 时使用默认超时。</param>
        /// <returns>
        /// 当字符串匹配指定模式时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool IsMatch(
            this string? value,
            string? pattern,
            RegexOptions options = RegexOptions.None,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern))
                return false;

            try
            {
                return Regex.IsMatch(value, pattern, options, timeout ?? DefaultRegexTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// 提取正则匹配结果中指定分组的值。
        /// </summary>
        /// <param name="value">要匹配的字符串。</param>
        /// <param name="pattern">正则表达式模式。</param>
        /// <param name="groupIndex">要返回的分组索引。</param>
        /// <param name="options">正则匹配选项。</param>
        /// <param name="timeout">正则匹配超时；为 <see langword="null"/> 时使用默认超时。</param>
        /// <returns>
        /// 若匹配成功且分组存在，则返回对应分组值；否则返回空字符串。
        /// </returns>
        public static string MatchValue(
            this string? value,
            string? pattern,
            int groupIndex = 1,
            RegexOptions options = RegexOptions.None,
            TimeSpan? timeout = null)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern) || groupIndex < 0)
                return string.Empty;

            try
            {
                Match match = Regex.Match(value, pattern, options, timeout ?? DefaultRegexTimeout);
                return match.Success && match.Groups.Count > groupIndex
                    ? match.Groups[groupIndex].Value
                    : string.Empty;
            }
            catch (RegexMatchTimeoutException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 判断当前字符串是否等于任意一个候选值。
        /// </summary>
        /// <param name="value">当前字符串。</param>
        /// <param name="ignoreCase">是否忽略大小写。</param>
        /// <param name="others">候选值列表。</param>
        /// <returns>
        /// 当当前字符串等于任意一个候选值时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool EqualsAny(this string? value, bool ignoreCase, params string?[]? others)
        {
            if (others == null || others.Length == 0)
                return false;

            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return others.Any(x => string.Equals(x, value, comparison));
        }

        /// <summary>
        /// 判断当前字符串是否包含任意一个指定子串。
        /// </summary>
        /// <param name="value">当前字符串。</param>
        /// <param name="patterns">要查找的子串列表。</param>
        /// <returns>
        /// 当当前字符串包含任意一个非空子串时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool ContainsAny(this string? value, params string?[]? patterns)
        {
            return ContainsAny(value, false, patterns);
        }

        /// <summary>
        /// 判断当前字符串是否包含任意一个指定子串。
        /// </summary>
        /// <param name="value">当前字符串。</param>
        /// <param name="ignoreCase">是否忽略大小写。</param>
        /// <param name="patterns">要查找的子串列表。</param>
        /// <returns>
        /// 当当前字符串包含任意一个非空子串时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool ContainsAny(this string? value, bool ignoreCase, params string?[]? patterns)
        {
            if (string.IsNullOrEmpty(value) || patterns == null)
                return false;

            if (!ignoreCase)
                return patterns.Any(x => !string.IsNullOrEmpty(x) && value.Contains(x));

            return patterns.Any(x =>
                !string.IsNullOrEmpty(x) &&
                value.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 从文本中提取常见 URL。
        /// </summary>
        /// <param name="inputText">要扫描的文本。</param>
        /// <returns>提取到的 URL 列表；以 <c>www.</c> 开头的地址会补全为 <c>https://</c>。</returns>
        /// <remarks>
        /// 本方法会剔除 URL 末尾常见的中英文标点，并为内部正则匹配设置超时。
        /// </remarks>
        public static List<string> ExtractUrls(this string? inputText)
        {
            List<string> list = [];

            if (string.IsNullOrWhiteSpace(inputText))
            {
                return list;
            }

            string pattern = @"\b(?:(?:https?|ftp)://|www\.)[^\s<>'""]+";

            try
            {
                foreach (Match match in Regex.Matches(
                    inputText,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    DefaultRegexTimeout))
                {
                    string url = match.Value.TrimEnd(
                        '.', ',', ';', ':', '!', '?',
                        '。', '，', '；', '：', '！', '？',
                        ')', ']', '}',
                        '）', '】', '》', '、');

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        continue;
                    }

                    if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    {
                        url = "https://" + url;
                    }

                    list.Add(url);
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }

            return list;
        }


        /// <summary>
        /// 判断当前字符串是否以任意一个指定前缀开头。
        /// </summary>
        /// <param name="value">当前字符串。</param>
        /// <param name="ignoreCase">是否忽略大小写。</param>
        /// <param name="others">前缀列表。</param>
        /// <returns>
        /// 当当前字符串以任意一个指定前缀开头时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool StartsWithAny(this string? value, bool ignoreCase, params string?[]? others)
        {
            if (string.IsNullOrEmpty(value) || others == null || others.Length == 0)
                return false;

            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return others.Any(x => !string.IsNullOrEmpty(x) && value.StartsWith(x, comparison));
        }

        /// <summary>
        /// 判断当前字符串是否以任意一个指定后缀结尾。
        /// </summary>
        /// <param name="value">当前字符串。</param>
        /// <param name="ignoreCase">是否忽略大小写。</param>
        /// <param name="others">后缀列表。</param>
        /// <returns>
        /// 当当前字符串以任意一个指定后缀结尾时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool EndsWithAny(this string? value, bool ignoreCase, params string?[]? others)
        {
            if (string.IsNullOrEmpty(value) || others == null || others.Length == 0)
                return false;

            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            return others.Any(x => !string.IsNullOrEmpty(x) && value.EndsWith(x, comparison));
        }

        /// <summary>
        /// 统一字符串中的换行符为 <c>\n</c>。
        /// </summary>
        /// <param name="value">要处理的字符串。</param>
        /// <returns>换行符已统一后的字符串。</returns>
        private static string NormalizeLineBreaks(string value)
        {
            return value.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        /// <summary>
        /// 在不超过指定长度的前提下，为文本追加省略标记。
        /// </summary>
        /// <param name="value">原始文本。</param>
        /// <param name="maxLength">允许的最大长度。</param>
        /// <returns>追加省略标记后的结果。</returns>
        private static string AppendEllipsisWithinLimit(string value, int maxLength)
        {
            if (maxLength <= 0)
            {
                return string.Empty;
            }

            if (maxLength <= 3)
            {
                return new string('.', maxLength);
            }

            value = value.TrimEnd();

            if (value.Length <= maxLength - 3)
            {
                return value + "...";
            }

            return value.Substring(0, maxLength - 3).TrimEnd() + "...";
        }

        /// <summary>
        /// 将字符串中的时间占位符替换为当前时间的对应分量并返回结果。
        /// </summary>
        /// <param name="template">包含占位符的模板字符串。</param>
        /// <returns>展开后的字符串；若模板为空则返回空字符串。</returns>
        /// <remarks>
        /// <para>支持的占位符：</para>
        /// <list type="bullet">
        /// <item><description><c>{yyyy} {MM} {dd} {HH} {mm} {ss} {fff}</c> — 当前时间各分量</description></item>
        /// <item><description><c>{guid}</c> — 32 位 GUID（无连字符）</description></item>
        /// <item><description><c>{guid8}</c> — GUID 前 8 位</description></item>
        /// </list>
        /// </remarks>
        public static string ResolveTemplate(this string? template)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            var now = DateTime.Now;
            string s = template;

            s = s.Replace("{yyyy}", now.ToString("yyyy", CultureInfo.InvariantCulture));
            s = s.Replace("{MM}", now.ToString("MM", CultureInfo.InvariantCulture));
            s = s.Replace("{dd}", now.ToString("dd", CultureInfo.InvariantCulture));
            s = s.Replace("{HH}", now.ToString("HH", CultureInfo.InvariantCulture));
            s = s.Replace("{mm}", now.ToString("mm", CultureInfo.InvariantCulture));
            s = s.Replace("{ss}", now.ToString("ss", CultureInfo.InvariantCulture));
            s = s.Replace("{fff}", now.ToString("fff", CultureInfo.InvariantCulture));

            string guid = Guid.NewGuid().ToString("N");
            s = s.Replace("{guid8}", guid.Substring(0, 8));
            s = s.Replace("{guid}", guid);

            return s;
        }
    }
}
