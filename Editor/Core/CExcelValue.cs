using System;
using System.Collections.Generic;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// 单元格读取与值规范化的内部工具（MiniExcel useHeaderRow:false 时键为列字母）。
    /// </summary>
    public static class CExcelValue
    {
        /// <summary>按列索引取单元格（键为列字母）。</summary>
        public static object GetCell(IDictionary<string, object> row, int index)
        {
            string key = ColumnLetter(index);
            return row.TryGetValue(key, out object value) ? value : null;
        }

        /// <summary>列索引 → 列字母（0→A，25→Z，26→AA...）。</summary>
        public static string ColumnLetter(int index)
        {
            string s = string.Empty;
            int n = index;
            while (n >= 0)
            {
                s = (char)('A' + (n % 26)) + s;
                n = n / 26 - 1;
            }
            return s;
        }

        /// <summary>单元格值 → 文本（数字去尾零、布尔转 1/0、日期格式化、null → 空串）。</summary>
        public static string ToText(object value)
        {
            switch (value)
            {
                case null: return string.Empty;
                case string s: return s;
                case bool b: return b ? "1" : "0";
                case double d: return IsIntegral(d) ? ((long)d).ToString() : d.ToString("R");
                case float f: return IsIntegral(f) ? ((long)f).ToString() : f.ToString("R");
                case decimal m: return IsIntegral(m) ? ((long)m).ToString() : m.ToString();
                case int i: return i.ToString();
                case long l: return l.ToString();
                case DateTime dt: return dt.ToString("yyyy-MM-dd HH:mm:ss");
                default: return value.ToString() ?? string.Empty;
            }
        }

        /// <summary>是否全空行（所有单元格 null 或空文本）。</summary>
        public static bool IsRowEmpty(IDictionary<string, object> row)
        {
            foreach (KeyValuePair<string, object> kv in row)
            {
                if (kv.Value != null && ToText(kv.Value).Length > 0) return false;
            }
            return true;
        }

        private static bool IsIntegral(double d) => d == Math.Floor(d) && Math.Abs(d) < 9.2e18;

        private static bool IsIntegral(float f) => f == Math.Floor(f) && Math.Abs(f) < 9.2e18;

        private static bool IsIntegral(decimal m) => m == Math.Floor(m);
    }
}
