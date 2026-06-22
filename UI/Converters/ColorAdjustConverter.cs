using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace KkjQuicker.UI.Converters
{
    /// <summary>
    /// 对颜色执行 Tint（混白）、Shade（混黑）、Alpha（设置透明度）操作。
    /// ConverterParameter 格式："Tint:0.86" / "Shade:0.2" / "Alpha:0.5"。
    /// 纯数字参数按 Tint 处理，例如 "0.9" 等同于 "Tint:0.9"。
    /// 根据绑定目标类型自动返回 <see cref="Color"/> 或 <see cref="SolidColorBrush"/>。
    /// </summary>
    public class ColorAdjustConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Color color;
            if (value is Color)
            {
                color = (Color)value;
            }
            else if (value is SolidColorBrush)
            {
                color = ((SolidColorBrush)value).Color;
            }
            else if (value is string)
            {
                string colorString = (string)value;
                if (string.IsNullOrWhiteSpace(colorString))
                    return Binding.DoNothing;

                try
                {
                    object converted = ColorConverter.ConvertFromString(colorString);
                    if (converted is Color)
                        color = (Color)converted;
                    else
                        return Binding.DoNothing;
                }
                catch (FormatException)
                {
                    return Binding.DoNothing;
                }
                catch (NotSupportedException)
                {
                    return Binding.DoNothing;
                }
            }
            else
            {
                return Binding.DoNothing;
            }

            string operation = "Tint";
            double amount = 0.9;

            if (parameter != null)
            {
                string param = parameter.ToString();
                int colon = param.IndexOf(':');

                if (colon >= 0)
                {
                    operation = param.Substring(0, colon).Trim();

                    double parsed;
                    if (double.TryParse(param.Substring(colon + 1).Trim(),
                        NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                    {
                        amount = parsed;
                    }
                }
                else
                {
                    double parsed;
                    if (double.TryParse(param, NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
                        amount = parsed;
                }
            }

            amount = Math.Max(0.0, Math.Min(1.0, amount));

            Color result;
            switch (operation.ToUpperInvariant())
            {
                case "TINT":
                    result = Tint(color, amount);
                    break;

                case "SHADE":
                    result = Shade(color, amount);
                    break;

                case "ALPHA":
                    result = WithAlpha(color, amount);
                    break;

                default:
                    result = color;
                    break;
            }

            if (targetType == typeof(Brush) || targetType == typeof(SolidColorBrush))
                return new SolidColorBrush(result);

            return result;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        // 向白色混合，amount=1 → 纯白，amount=0 → 原色
        private static Color Tint(Color c, double amount)
        {
            byte r = (byte)(c.R + (255 - c.R) * amount);
            byte g = (byte)(c.G + (255 - c.G) * amount);
            byte b = (byte)(c.B + (255 - c.B) * amount);

            return Color.FromArgb(c.A, r, g, b);
        }

        // 向黑色混合，amount=1 → 纯黑，amount=0 → 原色
        private static Color Shade(Color c, double amount)
        {
            byte r = (byte)(c.R * (1.0 - amount));
            byte g = (byte)(c.G * (1.0 - amount));
            byte b = (byte)(c.B * (1.0 - amount));

            return Color.FromArgb(c.A, r, g, b);
        }

        // 设置 Alpha，amount=1 → 全不透明，amount=0 → 全透明
        private static Color WithAlpha(Color c, double amount)
        {
            return Color.FromArgb((byte)Math.Round(amount * 255.0), c.R, c.G, c.B);
        }
    }
}