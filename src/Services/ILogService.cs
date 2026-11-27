namespace IcoConverter.Services
{
    public interface ILogService
    {
        string LogText { get; }

        string StatusMessage { get; }

        event Action<string, string>? LogUpdated;

        void Add(string message);

        void Clear();
    }
}