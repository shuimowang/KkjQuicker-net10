using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 字符串时：从首个非空行开始提取前 N 行。
    /// 集合时：提取前 N 项。
    /// </summary>
    [ValueConversion(typeof(object), typeof(object))]
    public class TakeConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int count = ParseCount(parameter, culture);
            if (value == null)
                return null;
            if (value is string text)
                return TakeLines(text, count);
            if (value is IEnumerable enumerable)
                return TakeItems(enumerable, count);
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;

        private static int ParseCount(object parameter, CultureInfo culture)
        {
            if (!int.TryParse(
                    System.Convert.ToString(parameter, culture),
                    NumberStyles.Integer,
                    culture,
                    out int count) || count <= 0)
            {
                count = 1;
            }
            return count;
        }

        private static string TakeLines(string text, int lineCount)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            var lines = new List<string>();
            bool started = false;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!started)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        started = true;
                    }
                    lines.Add(line);
                    if (lines.Count >= lineCount)
                        break;
                }
            }
            return lines.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine, lines);
        }

        private static IList TakeItems(IEnumerable source, int count)
        {
            var result = new List<object>();
            foreach (var item in source)
            {
                result.Add(item);
                if (result.Count >= count)
                    break;
            }
            return result;
        }
    }
}