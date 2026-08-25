using System;
using System.Collections.Generic;

namespace CoffeeBean.Excel
{
    /// <summary>问题级别：Error = 阻塞（解析/校验失败），Warning = 不阻塞（跳过/提示）。</summary>
    public enum CExcelIssueLevel
    {
        Warning,
        Error,
    }

    /// <summary>Excel 解析 / 校验过程中的问题记录（错误/警告分级）。</summary>
    public sealed class CExcelIssue
    {
        public CExcelIssueLevel Level;

        /// <summary>行号（1-based；0 = 表级问题）。</summary>
        public int Row;

        /// <summary>关联列名（"-" = 表级）。</summary>
        public string Column;

        public string Message;

        public override string ToString()
            => $"{(Level == CExcelIssueLevel.Error ? "[错误]" : "[警告]")} 第 {Row} 行 [{Column}]: {Message}";
    }

    /// <summary>读取选项。</summary>
    public sealed class CExcelReadOptions
    {
        /// <summary>读取的 sheet 名（null = 第一个 sheet）。</summary>
        public string SheetName;

        /// <summary>表头检测扫描行数（默认前 3 行，兼容"中文说明行 + 字段名行"双行表头）。</summary>
        public int HeaderScanRows = 3;

        /// <summary>是否跳过全空行（默认 true）。</summary>
        public bool SkipEmptyRows = true;

        /// <summary>是否跳过注释行（首列以 # 开头，默认 true）。</summary>
        public bool SkipCommentRows = true;

        /// <summary>
        /// 列名别名：规范列名 → 可接受的别名列表（含中文表头等）。
        /// 表头单元格命中任一别名即映射为规范列名。
        /// </summary>
        public Dictionary<string, string[]> ColumnAliases;

        /// <summary>表头检测是否只统计带类型后缀（_i/_s/...）的列名（默认 true）。</summary>
        public bool StrictSuffix = true;
    }

    /// <summary>读取结果：规范化列名 + 行数据（列名 → 原始单元格值）+ 问题列表。</summary>
    public sealed class CExcelReadResult
    {
        public string SourcePath;

        public string SheetName;

        /// <summary>规范列名（表头行非空单元格，别名已归一，按出现顺序）。</summary>
        public List<string> Columns = new List<string>();

        /// <summary>数据行：规范列名 → 单元格值（null = 空；数字为 double/long，字符串为 string）。</summary>
        public List<Dictionary<string, object>> Rows = new List<Dictionary<string, object>>();

        /// <summary>问题列表（错误/警告分级）。</summary>
        public List<CExcelIssue> Issues = new List<CExcelIssue>();

        /// <summary>
        /// 列中文说明（表头行上方最近一行的对应单元格；双行表头"中文说明行"）。
        /// 生成代码时用作字段注释（对齐项目约定：第 0 行中文说明、第 1 行字段名）。
        /// </summary>
        public Dictionary<string, string> ColumnComments = new Dictionary<string, string>();

        /// <summary>表头行索引（0-based）。</summary>
        public int HeaderRowIndex;

        /// <summary>是否存在阻塞性错误（警告不算）。</summary>
        public bool HasBlockingErrors
        {
            get
            {
                foreach (CExcelIssue issue in Issues)
                    if (issue.Level == CExcelIssueLevel.Error) return true;
                return false;
            }
        }

        public int WarningCount
        {
            get
            {
                int n = 0;
                foreach (CExcelIssue issue in Issues)
                    if (issue.Level == CExcelIssueLevel.Warning) n++;
                return n;
            }
        }
    }
}
