using System.Globalization;
using System.Windows.Data;

namespace BackupSample.Converters
{
    public class EnumBooleanConverter : IValueConverter
    {
        // Convert enum value -> bool (IsChecked)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string parameterString = parameter.ToString();
            if (string.IsNullOrEmpty(parameterString))
                return false;

            return value.ToString().Equals(parameterString, StringComparison.InvariantCultureIgnoreCase);
        }

        // Convert back bool -> enum value
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter == null)
                return System.Windows.Data.Binding.DoNothing;

            bool useValue = (bool)value;
            if (!useValue)
                return System.Windows.Data.Binding.DoNothing;

            string parameterString = parameter.ToString()!;
            return Enum.Parse(targetType, parameterString!);
        }
    }
}
