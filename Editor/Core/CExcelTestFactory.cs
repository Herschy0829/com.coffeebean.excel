using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using MiniExcelLibs;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// 测试工厂：在 Editor 程序集内封装 MiniExcel 写表（测试程序集无法直接引用
    /// Editor/Plugins 下的插件 DLL，需经本类间接使用——与 purchase 的 ExcelTestFactory 同模式）。
    /// 注意：MiniExcel 写表请用 List&lt;IDictionary&lt;string,object&gt;&gt;（key 为表头列名）或 DataSet（多 sheet），
    /// 不要用 object[][]（MiniExcel 会把它当"字典化对象"反射其属性，读回列名错误）。
    /// </summary>
    public static class CExcelTestFactory
    {
        /// <summary>
        /// 用行字典序列造一张临时 xlsx（key = 表头列名），返回文件路径。
        /// sheet 名 = namePrefix（避免 MiniExcel 默认 "Sheet1" 被生成器跳过）。
        /// </summary>
        public static string CreateTempTable(IEnumerable<IDictionary<string, object>> rows, string namePrefix = "coffeebean_excel_test")
        {
            string path = Path.Combine(Path.GetTempPath(),
                $"{namePrefix}_{Guid.NewGuid():N}.xlsx");
            MiniExcel.SaveAs(path, rows, overwriteFile: true, sheetName: namePrefix);
            return path;
        }

        /// <summary>
        /// 用 DataSet 造一张多 sheet 的临时 xlsx（每个 DataTable 一个 sheet，TableName = sheet 名）。
        /// 例：CreateMultiSheetTempTable(("ChapterConfig_1", rows1), ("ChapterConfig_2", rows2))
        /// </summary>
        public static string CreateMultiSheetTempTable(params (string SheetName, Dictionary<string, object>[] Rows)[] sheets)
        {
            var dataSet = new DataSet();
            foreach ((string sheetName, Dictionary<string, object>[] rows) in sheets)
            {
                var table = new DataTable(sheetName);
                if (rows.Length == 0) continue;
                foreach (string key in rows[0].Keys) table.Columns.Add(key);
                foreach (Dictionary<string, object> row in rows)
                {
                    var values = new object[table.Columns.Count];
                    for (int i = 0; i < table.Columns.Count; i++)
                        values[i] = row.TryGetValue(table.Columns[i].ColumnName, out object v) ? v : null;
                    table.Rows.Add(values);
                }
                dataSet.Tables.Add(table);
            }

            string path = Path.Combine(Path.GetTempPath(),
                $"{Guid.NewGuid():N}.xlsx");
            MiniExcel.SaveAs(path, dataSet);
            return path;
        }

        /// <summary>构造一行（键 = 列名，值 = 单元格值）。</summary>
        public static Dictionary<string, object> Row(params object[] keyValues)
        {
            var row = new Dictionary<string, object>();
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                row[(string)keyValues[i]] = keyValues[i + 1];
            return row;
        }

        public static void DeleteTempFile(string path)
        {
            if (path != null && File.Exists(path)) File.Delete(path);
        }
    }
}
