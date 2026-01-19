namespace IcoConverter.Services
{
    /// <summary>
    /// 日志服务接口，用于统一管理日志文本与状态消息。
    /// </summary>
    public interface ILogService
    {
        /// <summary>
        /// 当前日志文本（已拼接）。
        /// </summary>
        string LogText { get; }

        /// <summary>
        /// 当前状态消息。
        /// </summary>
        string StatusMessage { get; }

        /// <summary>
        /// 日志内容发生变化时触发。
        /// </summary>
        event Action<string, string>? LogUpdated;

        /// <summary>
        /// 追加一条日志。
        /// </summary>
        void Add(string message);

        /// <summary>
        /// 清空日志。
        /// </summary>
        void Clear();
    }
}