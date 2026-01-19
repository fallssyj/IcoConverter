namespace IcoConverter.Services
{
    /// <summary>
    /// 默认日志服务实现：负责维护日志缓冲与状态消息。
    /// </summary>
    public class LogService : ILogService
    {
        private const int MaxLogLines = 200;
        private readonly Queue<string> _logBuffer = new();

        public string LogText { get; private set; } = string.Empty;

        public string StatusMessage { get; private set; } = "就绪";

        public event Action<string, string>? LogUpdated;

        /// <summary>
        /// 追加一条日志，并更新状态消息。
        /// </summary>
        public void Add(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            _logBuffer.Enqueue($"[{timestamp}] {message}");
            while (_logBuffer.Count > MaxLogLines)
            {
                _logBuffer.Dequeue();
            }

            LogText = string.Join(Environment.NewLine, _logBuffer);
            StatusMessage = message;
            LogUpdated?.Invoke(LogText, StatusMessage);
        }

        /// <summary>
        /// 清空日志并刷新状态。
        /// </summary>
        public void Clear()
        {
            _logBuffer.Clear();
            LogText = string.Empty;
            StatusMessage = "日志已清空。";
            LogUpdated?.Invoke(LogText, StatusMessage);
        }
    }
}