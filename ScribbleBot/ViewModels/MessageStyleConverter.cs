using Microsoft.Extensions.AI;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ScribbleBot.ViewModels
{
    public class MessageStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ChatRole role)
            {
                bool isUser = role == ChatRole.User;
                string param = parameter?.ToString() ?? string.Empty;

                return param switch
                {
                    "Alignment" => isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                    "Background" => new SolidColorBrush((Color)ColorConverter.ConvertFromString(isUser ? "#2A2D3D" : "#252526")),
                    "BorderBrush" => new SolidColorBrush((Color)ColorConverter.ConvertFromString(isUser ? "#007ACC" : "#10B981")),
                    "RoleText" => isUser ? "YOU" : "SCRIBBLEBOT",
                    "RoleColor" => new SolidColorBrush((Color)ColorConverter.ConvertFromString(isUser ? "#007ACC" : "#10B981")),
                    _ => DependencyProperty.UnsetValue
                };
            }

            return DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
