using System;
using System.Collections.Generic;

namespace IcoConverter.Models
{
    /// <summary>
    /// 可执行文件中提取出的图标组合描述。
    /// </summary>
    public sealed class ExecutableIconCandidate
    {
        public ExecutableIconCandidate(
            string displayName,
            int groupId,
            int languageId,
            IReadOnlyList<IconFrameInfo> frames,
            byte[] iconBytes,
            int qualityScore)
        {
            DisplayName = displayName;
            GroupId = groupId;
            LanguageId = languageId;
            Frames = frames ?? throw new ArgumentNullException(nameof(frames));
            IconBytes = iconBytes ?? throw new ArgumentNullException(nameof(iconBytes));
            QualityScore = qualityScore;
        }

        public string DisplayName { get; }

        public int GroupId { get; }

        public int LanguageId { get; }

        public IReadOnlyList<IconFrameInfo> Frames { get; }

        public byte[] IconBytes { get; }

        public int QualityScore { get; }
    }
}
