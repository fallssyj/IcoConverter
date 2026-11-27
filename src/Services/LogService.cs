namespace IcoConverter.Services
{
    public class LogService : ILogService
    {
        private const int MaxLogLines = 200;
        private readonly Queue<string> _logBuffer = new();

        public string LogText { get; private set; } = string.Empty;

        public string StatusMessage { get; private set; } = "就绪";

        public event Action<string, string>? LogUpdated;

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

        public void Clear()
        {
            _logBuffer.Clear();
            LogText = string.Empty;
            StatusMessage = "日志已清空。";
            LogUpdated?.Invoke(LogText, StatusMessage);
        }
    }
}