using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace IcoConverter.Services
{
    public class SettingsService : ISettingsService
    {
        private static readonly JsonSerializerOptions SettingsJsonOptions = new()
        {
            WriteIndented = true
        };

        private const string SettingsFileName = "settings.json";

        private static readonly string SettingsPath = AppPaths.SettingsPath;

        public AppSettings Load()
        {
            try
            {
                var path = ResolveSettingsPath();
                if (path == null) return new AppSettings();

                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                // 从旧路径读取后写入 exe 目录，避免后续重复回退查找
                if (!string.Equals(path, SettingsPath, StringComparison.OrdinalIgnoreCase))
                {
                    Save(settings);
                }

                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] 读取设置失败: {ex}");
                // 设置读取或反序列化失败时使用默认设置。
                return new AppSettings();
            }
        }

        // 便携模式下若缺少配置，尝试从旧版用户目录迁移。
        private static string? ResolveSettingsPath()
        {
            if (File.Exists(SettingsPath))
            {
                return SettingsPath;
            }

            if (!AppPaths.IsPortable)
            {
                return null;
            }

            var legacyPath = Path.Combine(AppPaths.LegacyDirectory, SettingsFileName);
            return File.Exists(legacyPath) ? legacyPath : null;
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
                WriteAtomically(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SettingsService] 保存设置失败: {ex}");
            }
        }

        // 先写临时文件再替换，避免中断后留下不完整的 settings.json
        private static void WriteAtomically(string path, string content)
        {
            var tempPath = path + ".tmp";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                // 替换前将临时文件的缓冲数据刷新到磁盘。
                stream.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // 原子替换失败时回退到普通覆盖写入。
                Debug.WriteLine($"[SettingsService] 原子替换失败，回退为覆盖写: {ex.Message}");
                File.Copy(tempPath, path, overwrite: true);
                File.Delete(tempPath);
            }
        }
    }
}