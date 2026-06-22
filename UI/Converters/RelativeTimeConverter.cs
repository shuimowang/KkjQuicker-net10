using System;
using System.Globalization;
using System.Windows.Data;

namespace KkjQuicker.UI.Converters
{
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