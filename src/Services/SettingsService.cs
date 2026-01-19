using System.IO;
using System.Text.Json;

namespace IcoConverter.Services
{
    /// <summary>
    /// 应用设置的文件读写实现。
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private static readonly JsonSerializerOptions SettingsJsonOptions = new()
        {
            WriteIndented = true
        };

        private static readonly string SettingsPath = Path.Combine(
            AppContext.BaseDirectory,
            "settings.json");

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new AppSettings();

                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                // 读取失败时返回默认设置
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            try
            {
                var folder = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrWhiteSpace(folder) && !Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var json = JsonSerializer.Serialize(settings, SettingsJsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 忽略保存错误
            }
        }
    }
}