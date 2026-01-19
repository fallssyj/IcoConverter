namespace IcoConverter.Services
{
    /// <summary>
    /// 批量转换统计结果。
    /// </summary>
    public readonly record struct BatchConvertResult(int Total, int Success, int Failed);
}