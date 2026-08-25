using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CoffeeBean.Excel
{
    /// <summary>生成选项。</summary>
    public sealed class CExcelGenerateOptions
    {
        /// <summary>输出目录（相对 Assets 或绝对路径）。</summary>
        public string OutputFolder = "Assets/Configs/Generated";

        /// <summary>生成类 / Getter 的命名空间。</summary>
        public string Namespace = "Config";

        /// <summary>类名（默认取表名，即文件名去扩展）。</summary>
        public string ClassName;

        /// <summary>主键列名（默认自动选择第一个 *_i/_l/_s 列）。</summary>
        public string PrimaryKey;

        /// <summary>是否生成 JSON 数据文件（默认 true）。</summary>
        public bool GenerateJson = true;

        /// <summary>是否生成 C# 数据类（默认 true）。</summary>
        public bool GenerateClass = true;

        /// <summary>是否生成 Getter 加载器（默认 true）。</summary>
        public bool GenerateGetter = true;
    }

    /// <summary>生成结果。</summary>
    public sealed class CExcelGenerateResult
    {
        public bool Success;

        /// <summary>生成的产物文件路径（相对项目根）。</summary>
        public List<string> GeneratedFiles = new List<string>();

        public List<CExcelIssue> Issues = new List<CExcelIssue>();
    }

    /// <summary>单张表的描述（列名 + 类型 + 行数据），供生成器使用。</summary>
    public sealed class CExcelTable
    {
        public string SourcePath;
        public string TableName;
        public List<string> Columns = new List<string>();
        public Dictionary<string, CExcelFieldKind> Kinds = new Dictionary<string, CExcelFieldKind>();
        public List<Dictionary<string, object>> Rows = new List<Dictionary<string, object>>();
        public string PrimaryKey;
    }

    /// <summary>
    /// 配置表生成器：把一张 Excel 表生成三件产物——
    ///
    ///   表名.json          表数据（JSON，格式 {"data":[...]}，运行时 Resources 加载）
    ///   表名.cs            强类型数据类（字段按列名后缀/推断类型）
    ///   表名Getter.cs      加载器（Resources + JsonUtility → List + 主键字典查询）
    ///
    /// 列类型约定见 <see cref="CExcelTypeInfer"/>（_i/_s/_ia 后缀或推断）。
    /// </summary>
    public static class CExcelGenerator
    {
        /// <summary>生成单张表（路径不存在 / 有阻塞错误时返回失败结果）。</summary>
        public static CExcelGenerateResult Generate(string excelPath, CExcelGenerateOptions options)
        {
            var result = new CExcelGenerateResult();
            options = options ?? new CExcelGenerateOptions();

            CExcelReadResult read = CExcelReader.Read(excelPath);
            if (read.HasBlockingErrors)
            {
                result.Issues.AddRange(read.Issues);
                return result;
            }

            try
            {
                CExcelTable table = BuildTable(excelPath, read, options);
                if (table.PrimaryKey == null)
                {
                    result.Issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Error,
                        Row = 0,
                        Column = "-",
                        Message = "未找到主键列（需存在 *_i / *_l / *_s 列），表: " + table.TableName,
                    });
                    return result;
                }

                EnsureFolder(options.OutputFolder);
                string className = options.ClassName ?? table.TableName;

                if (options.GenerateJson)
                {
                    string jsonPath = Path.Combine(options.OutputFolder, className + ".json");
                    File.WriteAllText(jsonPath, WriteJson(table), new UTF8Encoding(false));
                    result.GeneratedFiles.Add(jsonPath);
                }

                if (options.GenerateClass)
                {
                    string classPath = Path.Combine(options.OutputFolder, className + ".cs");
                    File.WriteAllText(classPath, WriteClass(table, className, options.Namespace), new UTF8Encoding(false));
                    result.GeneratedFiles.Add(classPath);
                }

                if (options.GenerateGetter)
                {
                    string getterPath = Path.Combine(options.OutputFolder, className + "Getter.cs");
                    File.WriteAllText(getterPath, WriteGetter(table, className, options.Namespace), new UTF8Encoding(false));
                    result.GeneratedFiles.Add(getterPath);
                }

                result.Success = true;
                return result;
            }
            catch (Exception e)
            {
                result.Issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Error,
                    Row = 0,
                    Column = "-",
                    Message = "生成失败: " + e.Message,
                });
                return result;
            }
        }

        /// <summary>批量生成目录下全部 .xlsx（跳过 ~$ 临时文件；单个失败不中断其余）。</summary>
        public static CExcelGenerateResult GenerateFolder(string folder, CExcelGenerateOptions options)
        {
            var result = new CExcelGenerateResult();
            if (!Directory.Exists(folder))
            {
                result.Issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Error,
                    Row = 0,
                    Column = "-",
                    Message = "目录不存在: " + folder,
                });
                return result;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xlsx"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("~$", StringComparison.Ordinal)) continue; // Excel 临时文件
                CExcelGenerateResult single = Generate(file, options);
                result.GeneratedFiles.AddRange(single.GeneratedFiles);
                result.Issues.AddRange(single.Issues);
                if (!single.Success) result.Success = false;
            }
            if (result.GeneratedFiles.Count > 0 && !result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error))
                result.Success = true;
            return result;
        }

        // ========== 表构建 ==========

        private static CExcelTable BuildTable(string excelPath, CExcelReadResult read, CExcelGenerateOptions options)
        {
            var table = new CExcelTable
            {
                SourcePath = excelPath,
                TableName = Path.GetFileNameWithoutExtension(excelPath),
                Columns = read.Columns,
                Rows = read.Rows,
            };

            foreach (string column in read.Columns)
            {
                var values = new List<object>();
                foreach (Dictionary<string, object> row in read.Rows)
                    values.Add(row.TryGetValue(column, out object v) ? v : null);
                table.Kinds[column] = CExcelTypeInfer.Infer(column, values);
            }

            table.PrimaryKey = options.PrimaryKey;
            if (string.IsNullOrEmpty(table.PrimaryKey) || !table.Kinds.ContainsKey(table.PrimaryKey))
                table.PrimaryKey = PickPrimaryKey(table);
            return table;
        }

        private static string PickPrimaryKey(CExcelTable table)
        {
            foreach (string column in table.Columns)
            {
                CExcelFieldKind kind = table.Kinds[column];
                if (kind == CExcelFieldKind.Int || kind == CExcelFieldKind.Long || kind == CExcelFieldKind.String)
                    return column;
            }
            return null;
        }

        // ========== JSON 生成 ==========

        private static string WriteJson(CExcelTable table)
        {
            var sb = new StringBuilder();
            sb.Append("{\"data\":[");
            for (int r = 0; r < table.Rows.Count; r++)
            {
                Dictionary<string, object> row = table.Rows[r];
                if (r > 0) sb.Append(',');
                sb.Append('{');
                bool first = true;
                foreach (string column in table.Columns)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(CExcelTypeInfer.ToFieldName(column)).Append("\":");
                    object value = row.TryGetValue(column, out object v) ? v : null;
                    sb.Append(WriteJsonValue(value, table.Kinds[column]));
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string WriteJsonValue(object value, CExcelFieldKind kind)
        {
            if (CExcelTypeInfer.IsArray(kind))
            {
                string text = CExcelValue.ToText(value);
                List<string> parts = CExcelTypeInfer.SplitArrayValue(text);
                var sb = new StringBuilder();
                sb.Append('[');
                for (int i = 0; i < parts.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(WriteJsonScalar(parts[i], CExcelTypeInfer.ElementKind(kind)));
                }
                sb.Append(']');
                return sb.ToString();
            }

            string raw = CExcelValue.ToText(value);
            if (raw.Length == 0) return kind == CExcelFieldKind.String ? "\"\"" : "0";
            return WriteJsonScalar(raw, kind);
        }

        private static string WriteJsonScalar(string raw, CExcelFieldKind kind)
        {
            switch (kind)
            {
                case CExcelFieldKind.Int:
                case CExcelFieldKind.Long:
                    return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv) ? lv.ToString(CultureInfo.InvariantCulture) : "0";
                case CExcelFieldKind.Float:
                case CExcelFieldKind.Double:
                    return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv)
                        ? dv.ToString("R", CultureInfo.InvariantCulture)
                        : "0";
                case CExcelFieldKind.Bool:
                    return IsTrueLiteral(raw) ? "true" : "false";
                default:
                    return Quote(raw);
            }
        }

        private static bool IsTrueLiteral(string raw)
            => string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1";

        /// <summary>JSON 字符串转义（中文不转义，保留 UTF-8）。</summary>
        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ========== C# 数据类 ==========

        private static string WriteClass(CExcelTable table, string className, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated by CoffeeBean.Excel - 请勿手动修改>");
            sb.AppendLine("// 源表: " + Path.GetFileName(table.SourcePath));
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + className + " 配置行（自动生成）。</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    public sealed class " + className);
            sb.AppendLine("    {");
            foreach (string column in table.Columns)
            {
                string type = CExcelTypeInfer.CSharpType(table.Kinds[column]);
                string field = CExcelTypeInfer.ToFieldName(column);
                sb.AppendLine("        /// <summary>" + column + "。</summary>");
                sb.AppendLine("        public " + type + " " + field + ";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== Getter 加载器 ==========

        private static string WriteGetter(CExcelTable table, string className, string ns)
        {
            CExcelFieldKind keyKind = table.Kinds[table.PrimaryKey];
            string keyType = CExcelTypeInfer.CSharpType(keyKind);
            string keyField = CExcelTypeInfer.ToFieldName(table.PrimaryKey);

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated by CoffeeBean.Excel - 请勿手动修改>");
            sb.AppendLine("// 源表: " + Path.GetFileName(table.SourcePath) + "  主键: " + table.PrimaryKey);
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + className + " 配置加载器（Resources 读取 + 主键查询，自动生成）。</summary>");
            sb.AppendLine("    public static class " + className + "Getter");
            sb.AppendLine("    {");
            sb.AppendLine("        private const string AssetPath = \"Configs/" + className + "\";");
            sb.AppendLine("        private static List<" + className + "> _all;");
            sb.AppendLine("        private static Dictionary<" + keyType + ", " + className + "> _byKey;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>全部配置（懒加载，Resources.Load + JsonUtility）。</summary>");
            sb.AppendLine("        public static List<" + className + "> All => _all ??= Load();");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>按主键查询；不存在返回 null。</summary>");
            sb.AppendLine("        public static " + className + " Get(" + keyType + " key)");
            sb.AppendLine("        {");
            sb.AppendLine("            _byKey ??= BuildIndex();");
            sb.AppendLine("            return _byKey.TryGetValue(key, out " + className + " item) ? item : null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static List<" + className + "> Load()");
            sb.AppendLine("        {");
            sb.AppendLine("            TextAsset asset = Resources.Load<TextAsset>(AssetPath);");
            sb.AppendLine("            if (asset == null) { Debug.LogError(\"配置缺失: \" + AssetPath); return new List<" + className + ">(); }");
            sb.AppendLine("            var wrapper = JsonUtility.FromJson<Wrapper>(asset.text);");
            sb.AppendLine("            return wrapper != null ? wrapper.data : new List<" + className + ">();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static Dictionary<" + keyType + ", " + className + "> BuildIndex()");
            sb.AppendLine("        {");
            sb.AppendLine("            var index = new Dictionary<" + keyType + ", " + className + ">();");
            sb.AppendLine("            foreach (" + className + " item in All) index[item." + keyField + "] = item;");
            sb.AppendLine("            return index;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [System.Serializable] private sealed class Wrapper { public List<" + className + "> data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void EnsureFolder(string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }
    }
}
