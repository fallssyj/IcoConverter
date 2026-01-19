namespace IcoConverter.Services
{
    /// <summary>
    /// 设置读写服务接口。
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>
        /// 读取设置，若不存在则返回默认值。
        /// </summary>
        AppSettings Load();

        /// <summary>
        /// 保存设置。
        /// </summary>
        void Save(AppSettings settings);
    }
}