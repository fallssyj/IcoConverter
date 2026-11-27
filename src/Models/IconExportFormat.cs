namespace IcoConverter.Models
{
    /// <summary>导出目标格式。三者共用尺寸选择界面，区别在于提示规则与写出实现。</summary>
    public enum IconExportFormat
    {
        Ico,
        Icns,

        // 多尺寸 PNG 集：每个勾选尺寸各输出一个文件，而非单个容器
        Png,
    }
}
