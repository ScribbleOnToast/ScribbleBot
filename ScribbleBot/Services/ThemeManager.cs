using System;
using System.Linq;
using System.Windows;

namespace ScribbleBot.Services
{
    public static class ThemeManager
    {
        public static void ApplyTheme(bool isDarkMode)
        {
            string themeUri = isDarkMode
                ? "UI/Themes/DarkTheme.xaml"
                : "UI/Themes/LightTheme.xaml";

            var newThemeDict = new ResourceDictionary
            {
                Source = new Uri(themeUri, UriKind.Relative)
            };

            // Locate existing theme dictionary if present
            var appResources = Application.Current.Resources.MergedDictionaries;
            var currentTheme = appResources.FirstOrDefault(d =>
                d.Source != null && (d.Source.OriginalString.Contains("DarkTheme.xaml") || d.Source.OriginalString.Contains("LightTheme.xaml")));

            if (currentTheme != null)
            {
                appResources.Remove(currentTheme);
            }

            appResources.Add(newThemeDict);
        }
    }
}