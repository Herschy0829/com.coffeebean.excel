using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MiniExcelLibs;

namespace CoffeeBean.Excel
{
    /// <summary>
    /// Excel 读取器（MiniExcel 封装，只读 .xlsx）：
    ///
    /// - 读取指定 sheet 的全部行（键为列字母），自动做**表头行检测**
    ///   （前 N 行中"带类型后缀列名"最多的行，兼容"中文说明行 + 字段名行"双行表头）
    /// - 列名归一：表头单元格 → 规范列名（别名映射 / trim / 去重）
    /// - 数据行：跳过空行与注释行（首列 # 开头），按规范列名取单元格值
    /// - 问题分级：文件缺失 / 空表 / 解析失败 = 错误；跳过行等 = 警告
    ///
    /// 值保留原始类型（数字为 double / long，字符串为 string，空为 null），
    /// 供类型推断与 JSON 生成使用；需要文本时用 <see cref="CExcelValue.ToText"/>。
    /// </summary>
    public static class CExcelReader
    {
        /// <summary>读取 Excel 表。路径不存在 / 损坏时返回带错误的结果（不抛异常）。</summary>
        public static CExcelReadResult Read(string path, CExcelReadOptions options = null)
        {
            var result = new CExcelReadResult { SourcePath = path };
            options = options ?? new CExcelReadOptions();

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                result.Issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Error,
                    Row = 0,
                    Column = "-",
                    Message = "Excel 文件不存在: " + path,
                });
                return result;
            }

            try
            {
                // useHeaderRow:false → 每行是 IDictionary<string,object>，键为列字母（A/B/C...）
                var rows = new List<IDictionary<string, object>>();
                foreach (dynamic row in MiniExcel.Query(path, useHeaderRow: false, sheetName: options.SheetName))
                    rows.Add((IDictionary<string, object>)row);

                if (rows.Count == 0)
                {
                    result.Issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Error,
                        Row = 0,
                        Column = "-",
                        Message = "Excel 工作表为空",
                    });
                    return result;
                }

                // 表头检测：前 N 行中带类型后缀列名最多的行
                result.HeaderRowIndex = FindHeaderRow(rows, options);
                var headerRow = rows[result.HeaderRowIndex];

                // 列名归一：规范列名（别名映射 / trim / 去重），列字母 → 规范列名
                var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int maxCol = headerRow.Count;
                for (int c = 0; c < maxCol; c++)
                {
                    string raw = CExcelValue.ToText(CExcelValue.GetCell(headerRow, c)).Trim();
                    if (string.IsNullOrEmpty(raw)) continue;
                    string canonical = ResolveCanonical(raw, options);
                    if (string.IsNullOrEmpty(canonical)) continue;
                    if (!normalized.ContainsKey(canonical))
                    {
                        normalized[canonical] = CExcelValue.ColumnLetter(c);
                        result.Columns.Add(canonical);
                    }
                }

                if (result.Columns.Count == 0)
                {
                    result.Issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Error,
                        Row = 0,
                        Column = "-",
                        Message = "未检测到带类型后缀的列名（表头行 " + (result.HeaderRowIndex + 1) + "）",
                    });
                    return result;
                }

                // 数据行（表头之后；Excel 行号 = 行索引 + 1）
                for (int r = result.HeaderRowIndex + 1; r < rows.Count; r++)
                {
                    var row = rows[r];
                    int rowNumber = r + 1;

                    if (options.SkipEmptyRows && CExcelValue.IsRowEmpty(row)) continue;
                    if (options.SkipCommentRows && IsCommentRow(row)) continue;

                    var data = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    bool hasValue = false;
                    foreach (string column in result.Columns)
                    {
                        // 按列字母取单元格（MiniExcel useHeaderRow:false 键为列字母）
                        string letter = normalized[column];
                        object value = row.TryGetValue(letter, out object cell) ? cell : null;
                        data[column] = value;
                        if (value != null && CExcelValue.ToText(value).Length > 0) hasValue = true;
                    }

                    if (!hasValue)
                    {
                        // 整行只有空值：视为空行
                        if (options.SkipEmptyRows) continue;
                    }
                    result.Rows.Add(data);
                }

                return result;
            }
            catch (Exception e)
            {
                result.Issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Error,
                    Row = 0,
                    Column = "-",
                    Message = "Excel 解析失败: " + e.Message,
                });
                return result;
            }
        }

        // ===== 内部 =====

        /// <summary>在前 N 行中找到"带类型后缀列名"最多的行作为表头（兼容双行表头）。找不到则用第 1 行。
        /// internal 供单元测试直接驱动（伪行集合）。</summary>
        internal static int FindHeaderRow(List<IDictionary<string, object>> rows, CExcelReadOptions options)
        {
            int best = 0;
            int bestCount = -1;
            int scan = Math.Min(rows.Count, Math.Max(1, options.HeaderScanRows));
            for (int i = 0; i < scan; i++)
            {
                int count = CountHeaderMatches(rows[i], options);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = i;
                }
            }
            return best;
        }

        private static int CountHeaderMatches(IDictionary<string, object> row, CExcelReadOptions options)
        {
            int count = 0;
            for (int c = 0; c < row.Count; c++)
            {
                string name = CExcelValue.ToText(CExcelValue.GetCell(row, c)).Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (options.StrictSuffix && !CExcelTypeInfer.IsSuffixed(name)) continue;
                // 别名：只认各组别名中的"规范列名"（第一个），避免中文说明行得分
                if (options.ColumnAliases != null && options.ColumnAliases.Count > 0)
                {
                    bool isCanonical = false;
                    foreach (string[] aliases in options.ColumnAliases.Values)
                    {
                        if (aliases.Length > 0 && string.Equals(name, aliases[0], StringComparison.OrdinalIgnoreCase))
                        {
                            isCanonical = true;
                            break;
                        }
                    }
                    if (!isCanonical) continue;
                }
                count++;
            }
            return count;
        }

        /// <summary>表头单元格 → 规范列名（别名映射优先，否则原样）。</summary>
        private static string ResolveCanonical(string rawName, CExcelReadOptions options)
        {
            if (options.ColumnAliases != null)
            {
                foreach (KeyValuePair<string, string[]> kv in options.ColumnAliases)
                {
                    foreach (string alias in kv.Value)
                    {
                        if (string.Equals(rawName, alias, StringComparison.OrdinalIgnoreCase))
                            return kv.Key;
                    }
                }
            }
            return rawName; // 无别名命中：原样（应为带类型后缀的规范列名）
        }

        private static bool IsCommentRow(IDictionary<string, object> row)
        {
            string first = CExcelValue.ToText(CExcelValue.GetCell(row, 0)).Trim();
            return first.StartsWith("#", StringComparison.Ordinal);
        }
    }
}
