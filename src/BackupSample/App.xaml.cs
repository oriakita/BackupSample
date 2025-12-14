using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BackupSample
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling
            DispatcherUnhandledException += (s, ex) =>
            {
                System.Windows.MessageBox.Show($"An unexpected error occurred: {ex.Exception.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };
        }
    }

    // Value converter for bytes formatting
    public class BytesConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                return FormatBytes(bytes);
            }
            return "0 B";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static string FormatBytes(long bytes)
        {
            var units = new[] { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:F2} {units[unitIndex]}";
        }
    }
}
