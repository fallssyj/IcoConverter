namespace IcoConverter.Services
{
    public interface ISettingsService
    {
        AppSettings Load();

        void Save(AppSettings settings);
    }
}