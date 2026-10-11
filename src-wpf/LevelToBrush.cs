using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Grobyq
{
    // LogLevel -> text brush for the Journal. Colors echo the app palette (App.xaml).
    public sealed class LevelToBrush : IValueConverter
    {
        private static readonly Brush Normal = New("#D6D9DC");
        private static readonly Brush Good   = New("#36B37E");
        private static readonly Brush Bad    = New("#E5534B");
        private static readonly Brush Dim    = New("#8A9096");
        private static readonly Brush Accent = New("#4C8DFF");
        private static readonly Brush Irq    = New("#C08CFF");

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            switch (value is LogLevel lv ? lv : LogLevel.Normal)
            {
                case LogLevel.Good:   return Good;
                case LogLevel.Bad:    return Bad;
                case LogLevel.Dim:    return Dim;
                case LogLevel.Accent: return Accent;
                case LogLevel.Irq:    return Irq;
                default:              return Normal;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static Brush New(string hex)
        {
            var b = (Brush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }
    }
}
