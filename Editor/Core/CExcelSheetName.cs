namespace CoffeeBean.Excel
{
    /// <summary>
    /// Sheet 名解析：多章节约定的解析工具。
    /// 约定：sheet 名形如 "前缀_数字"（如 ChapterConfig_1、ChapterConfig_2）→
    /// 同前缀的 sheet 属于同一组配置的多个章节，由聚合 Getter 统一按章节查询。
    /// 非此命名的 sheet 为普通单表。
    /// </summary>
    public static class CExcelSheetName
    {
        /// <summary>
        /// 尝试解析多章节 sheet 名（前缀_数字）。返回 false 表示普通单表。
        /// </summary>
        public static bool TryParseChapter(string sheetName, out string frontName, out int chapterIndex)
        {
            frontName = null;
            chapterIndex = -1;
            if (string.IsNullOrEmpty(sheetName)) return false;

            int lastIndex = sheetName.LastIndexOf('_');
            if (lastIndex <= 0 || lastIndex == sheetName.Length - 1) return false;

            string suffix = sheetName.Substring(lastIndex + 1);
            if (!int.TryParse(suffix, out chapterIndex)) return false;
            if (chapterIndex <= 0) return false; // 章节号从 1 开始

            frontName = sheetName.Substring(0, lastIndex);
            return !string.IsNullOrEmpty(frontName);
        }

        /// <summary>sheet 名是否为约定跳过的 sheet（名字含 sheet / debug，如 Sheet1、debug 表）。</summary>
        public static bool IsSkippedSheet(string sheetName)
        {
            if (string.IsNullOrEmpty(sheetName)) return true;
            string lower = sheetName.ToLowerInvariant();
            return lower.Contains("sheet") || lower.Contains("debug");
        }
    }
}
