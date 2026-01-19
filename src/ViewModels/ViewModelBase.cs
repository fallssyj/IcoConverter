using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IcoConverter.ViewModels
{
    /// <summary>
    /// 实现了 <see cref="INotifyPropertyChanged"/> 接口的视图模型基类，
    /// 并提供了安全的 <see cref="SetField{T}(ref T, T, string)"/> 辅助方法。
    /// </summary>
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
