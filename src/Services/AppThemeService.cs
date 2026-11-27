using System.Windows;

namespace IcoConverter.Services
{
    internal static class AppThemeService
    {
        // 将 WPF 实验性主题 API 的调用限制在此处。
#pragma warning disable WPF0001
        public static bool IsDark => Application.Current?.ThemeMode == ThemeMode.Dark;

        public static void Apply(bool isDark)
        {
            if (Application.Current is { } application)
            {
                application.ThemeMode = isDark ? ThemeMode.Dark : ThemeMode.Light;
            }
        }
#pragma warning restore WPF0001
    }
}
