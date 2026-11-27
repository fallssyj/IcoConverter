namespace IcoConverter.Models
{
    /// <summary>预览区底板颜色，用于判断透明区域在浅色与深色环境下的可辨识度。</summary>
    public enum PreviewBackgroundMode
    {
        Checker,
        White,
        Black,

        /// <summary>不绘制底板，透出窗口材质。置于末尾以保持已保存设置中的枚举值不变。</summary>
        None
    }
}
