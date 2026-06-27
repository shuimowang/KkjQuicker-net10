using System;
using System.Globalization;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 将 <see cref="DateTime"/> 转换为“刚刚”“N 分钟前”“昨天 HH:mm”等相对时间文本。
    /// </summary>
    /// <remarks>
    /// 本转换器只在绑定刷新时重新计算文本，不会自行启动定时器。
    /// 若需要“分钟前”等文案随时间自动更新，请由宿主定时触发绑定源属性变更或刷新绑定。
    /// </remarks>
    public sealed class RelativeTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DateTime))
            {
                return string.Empty;
            }

            DateTime dt = (DateTime)value;
            DateTime now = DateTime.Now;
            DateTime today = now.Date;

            if (dt > now)
            {
                return dt.ToString("MM-dd HH:mm", culture);
            }

            TimeSpan diff = now - dt;

            if (dt.Date == today)
            {
                if (diff.TotalMinutes < 1)
                {
                    return "刚刚";
                }

                if (diff.TotalMinutes < 60)
                {
                    return string.Format("{0} 分钟前", (int)diff.TotalMinutes);
                }

                return string.Format("{0} 小时前", (int)diff.TotalHours);
            }

            if (dt.Date == today.AddDays(-1))
            {
                return string.Format("昨天 {0:HH:mm}", dt);
            }

            int days = (today - dt.Date).Days;
            if (days < 7)
            {
                return string.Format("{0} 天前", days);
            }

            return dt.ToString("MM-dd HH:mm", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
