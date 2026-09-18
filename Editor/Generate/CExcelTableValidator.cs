using System;
using System.Collections.Generic;

namespace CoffeeBean
{
    /// <summary>
    /// 表级校验：**生成之前**把数据问题挑出来（错误级阻塞生成）。
    ///
    /// 存在的理由：以前 `Level_i` 填了 `abc` 会被安静地写成 `0`，`Gold_b` 被 Excel 记成
    /// 科学计数法会变成完全不同的数 —— 这类"静默错数据"比编译错误难查得多。
    /// 现在每格都按声明的类型真解析一遍，解析不了就指名道姓报第几行第几列。
    ///
    /// 关掉：<see cref="CExcelGenerateOptions.StrictTypeCheck"/> = false（只跳过值校验，枚举定义校验仍在）。
    /// </summary>
    public static class CExcelTableValidator
    {
        /// <summary>校验整张表，把问题追加进 <paramref name="issues"/>。</summary>
        public static void Validate(CExcelTable table, CExcelGenerateOptions options, List<CExcelIssue> issues)
        {
            if (table == null) return;
            char[] separators = CExcelCellJson.Separators(options != null ? options.ArraySeparators : null);
            bool strictTypeCheck = options == null || options.StrictTypeCheck;

            CheckDuplicateFieldNames(table, issues);

            foreach (string column in table.Columns)
            {
                if (!table.Kinds.TryGetValue(column, out CExcelFieldKind kind)) continue;
                table.Enums.TryGetValue(column, out CExcelEnumDef enumDef);

                WarnIfBigIntLooksLikeOldBool(table, column, kind, issues);

                if (!strictTypeCheck) continue;

                for (int r = 0; r < table.Rows.Count; r++)
                {
                    Dictionary<string, object> row = table.Rows[r];
                    object raw = row.TryGetValue(column, out object v) ? v : null;
                    string text = CExcelValue.ToText(raw);
                    int excelRow = table.HeaderRowIndex + r + 2;

                    if (text.Trim().Length == 0)
                    {
                        // 空 → 用默认值，不算错……除非这是主键列：有行没主键的表没有意义
                        // （关掉 SkipRowsWithoutKey 时才会走到这里；开着的话这些行已经被丢掉了）
                        if (string.Equals(column, table.PrimaryKey, StringComparison.OrdinalIgnoreCase))
                        {
                            issues.Add(new CExcelIssue
                            {
                                Level = CExcelIssueLevel.Error,
                                Row = excelRow,
                                Column = column,
                                Message = "主键列是空的 —— 有行没有主键的配置表没有意义。"
                                          + "（说明行/图例行请打开\"跳过主键无效的行\"让它自动跳过）",
                            });
                        }
                        continue;
                    }

                    string error = CExcelCellJson.Check(text, kind, enumDef, separators);
                    if (error == null) continue;

                    issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Error,
                        Row = excelRow,
                        Column = column,
                        Message = $"{DeclaredName(kind, enumDef)} 列填的值不合法：{error}",
                    });
                }
            }
        }

        /// <summary>
        /// 两个列名去掉后缀后撞成同一个字段名 → 生成的 C# 类会有两个同名字段（CS0102），
        /// 而且它们的枚举类型名也会撞（CS0101）。典型长相：`State_e` + `State_ea`，或 `Name_s` + `Name_i`。
        /// 这类错在生成代码里很难看出根因，所以在生成前就指名道姓报出来。
        /// </summary>
        private static void CheckDuplicateFieldNames(CExcelTable table, List<CExcelIssue> issues)
        {
            var byField = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string column in table.Columns)
            {
                string field = CExcelTypeInfer.ToFieldName(column);
                if (byField.TryGetValue(field, out string first))
                {
                    issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Error,
                        Row = 0,
                        Column = column,
                        Message = $"列 {first} 与 {column} 去掉类型后缀后都是字段 \"{field}\" —— 生成的数据类会有两个同名字段（C# 直接编译不过）。" +
                                  "请把其中一个列名改掉（例如 State_e / States_ea）。",
                    });
                }
                else
                {
                    byField[field] = column;
                }
            }
        }

        /// <summary>
        /// `_b` 从 bool 改成 BigInteger 是破坏性变更 —— 老表里 `Enabled_b` 填 true/false 的地方
        /// 现在会变成"BigInteger 解析失败（不是整数）"。这里给一条**指名道姓的迁移提示**。
        /// </summary>
        private static void WarnIfBigIntLooksLikeOldBool(CExcelTable table, string column, CExcelFieldKind kind, List<CExcelIssue> issues)
        {
            if (kind != CExcelFieldKind.BigInt && kind != CExcelFieldKind.BigIntArray) return;

            bool sawBool = false;
            foreach (Dictionary<string, object> row in table.Rows)
            {
                string text = CExcelValue.ToText(row.TryGetValue(column, out object v) ? v : null).Trim();
                if (text.Length == 0) continue;
                bool isWord = string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                              || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
                if (!isWord) return;   // 只要有一个不是 true/false，就不像旧 bool 用法
                sawBool = true;
            }

            if (!sawBool) return;
            issues.Add(new CExcelIssue
            {
                Level = CExcelIssueLevel.Warning,
                Row = 0,
                Column = column,
                Message = $"列 {column} 的值全是 true/false，看起来是旧版写法 —— `_b` 现在是 BigInteger（大整数），布尔请改用 `_bool`。",
            });
        }

        private static string DeclaredName(CExcelFieldKind kind, CExcelEnumDef enumDef)
        {
            if (CExcelTypeInfer.IsEnumKind(kind)) return enumDef != null ? enumDef.TypeName : "枚举";
            return CExcelTypeInfer.CSharpType(kind) ?? kind.ToString();
        }
    }
}
