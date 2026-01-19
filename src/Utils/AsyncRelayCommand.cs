using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace IcoConverter.Utils
{
    /// <summary>
    /// 支持异步执行的 <see cref="ICommand"/> 实现。
    /// </summary>
    public class AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null) : ICommand
    {
        private readonly Func<Task> _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        private readonly Func<bool>? _canExecute = canExecute;
        private bool _isExecuting;

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                _isExecuting = true;
                CommandManager.InvalidateRequerySuggested();
                await _executeAsync().ConfigureAwait(true);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}