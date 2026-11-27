using System.IO;

namespace IcoConverter.Services
{
    /// <summary>
    /// 绿色发布：设置与日志随 exe 目录存放，保持便携性。
    /// 仅当 exe 目录不可写（Program Files、只读介质、受限共享目录）时回退到 %LOCALAPPDATA%，
    /// 避免在权限受限的环境下因写入失败而中断。
    /// </summary>
    public static class AppPaths
    {
        private const string AppFolderName = "IcoConverter";
        private const string ProbeFileName = ".write-probe";

        /// <summary>旧版本固定使用的路径；数据目录位于 exe 侧时用于一次性迁移。</summary>
        // 静态字段按声明顺序初始化，这个必须排在 DataDirectory 前面：回退时要用到它
        public static string LegacyDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName);

        /// <summary>实际使用的数据目录（exe 目录或回退目录）。</summary>
        public static string DataDirectory { get; } = ResolveDataDirectory();

        public static string SettingsPath { get; } = Path.Combine(DataDirectory, "settings.json");

        public static string LogDirectory { get; } = Path.Combine(DataDirectory, "logs");

        /// <summary>true 表示数据写在 exe 旁边（便携），false 表示走了回退目录。</summary>
        public static bool IsPortable { get; } = !string.Equals(
            DataDirectory,
            LegacyDirectory,
            StringComparison.OrdinalIgnoreCase);

        private static string ResolveDataDirectory()
        {
            var exeDirectory = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (!string.IsNullOrEmpty(exeDirectory) && IsWritable(exeDirectory))
            {
                return exeDirectory;
            }

            return LegacyDirectory;
        }

        // 尝试以写入权限打开探测文件。流关闭时由 DeleteOnClose 清理探测文件。
        private static bool IsWritable(string directory)
        {
            try
            {
                var probePath = Path.Combine(directory, ProbeFileName);
                using var stream = new FileStream(
                    probePath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppPaths] exe 目录不可写，改用回退目录: {ex.Message}");
                return false;
            }
        }
    }
}
