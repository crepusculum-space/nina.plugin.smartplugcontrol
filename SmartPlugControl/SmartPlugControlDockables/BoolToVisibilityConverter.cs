using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDockables {
    /// <summary>True/non-null -> Visible, everything else (false/null) -> Collapsed.</summary>
    public class BoolToVisibilityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            bool visible = value switch {
                bool b => b,
                null => false,
                _ => true
            };
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}
