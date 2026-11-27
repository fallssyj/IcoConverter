using System.IO;
using System.Text;

namespace IcoConverter.Services
{
    // 与应用日志共用存储目录。写入后尽力清理超过保留上限的崩溃日志。
    public static class CrashLog
    {
        private const int MaxLogFiles = 20;
        private static readonly object Gate = new();

        public static string LogDirectory { get; } = AppPaths.LogDirectory;

        // 成功时返回日志路径。缺少异常或日志处理失败时返回空字符串。
        public static string Write(Exception? exception, string source)
        {
            if (exception == null)
            {
                return string.Empty;
            }

            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogDirectory);

                    var path = Path.Combine(
                        LogDirectory,
                        $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

                    var builder = new StringBuilder();
                    builder.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                    builder.AppendLine($"来源: {source}");
                    builder.AppendLine($"版本: {typeof(CrashLog).Assembly.GetName().Version}");
                    builder.AppendLine($"系统: {Environment.OSVersion}");
                    builder.AppendLine($"运行时: .NET {Environment.Version}");
                    builder.AppendLine(new string('-', 60));
                    builder.AppendLine(exception.ToString());

                    File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
                    Prune();
                    return path;
                }
            }
            catch
            {
                // 此时已处于崩溃处理流程，日志写入失败不再向外抛出
                return string.Empty;
            }
        }

        private static void Prune()
        {
            var files = new DirectoryInfo(LogDirectory).GetFiles("crash-*.log");
            if (files.Length <= MaxLogFiles)
            {
                return;
            }

            Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (var i = MaxLogFiles; i < files.Length; i++)
            {
                try
                {
                    files[i].Delete();
                }
                catch (IOException)
                {
                    // 文件正被其他进程占用，留待下次清理
                }
            }
        }
    }
}
