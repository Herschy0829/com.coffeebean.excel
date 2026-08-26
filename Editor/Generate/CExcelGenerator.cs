using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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

        /// <summary>类名（默认取表名/sheet 名）。</summary>
        public string ClassName;

        /// <summary>主键列名（默认自动选择第一个 *_i/_l/_s 列）。</summary>
        public string PrimaryKey;

        /// <summary>指定 sheet 名（null = 默认 sheet）。</summary>
        public string SheetName;

        /// <summary>是否生成 JSON 数据文件（默认 true）。</summary>
        public bool GenerateJson = true;

        /// <summary>是否生成 C# 数据类 / Getter（默认 true）。</summary>
        public bool GenerateClass = true;

        /// <summary>JSON 输出目录（必须位于 Resources 下，否则运行时 Resources.Load 读不到；默认 Assets/Resources/Configs）。</summary>
        public string JsonResourcesFolder = "Assets/Resources/Configs";

        /// <summary>Getter 的 Resources 相对路径（默认 "Configs"，与 JsonResourcesFolder 对齐）。</summary>
        public string ResourcesPath = "Configs";

        /// <summary>
        /// 是否对生成的 JSON 做混淆加密（默认 true，对齐 Idle 项目）。
        /// 加密后打包产物里的配置不是明文（防普通读取）；运行时 Getter 透明解密。
        /// 注意：这是混淆级保护（key 在生成代码里，不能防专业逆向），调试时可关闭以便直接查看 JSON。
        /// </summary>
        public bool EncryptJson = true;
    }

    /// <summary>生成结果。</summary>
    public sealed class CExcelGenerateResult
    {
        public bool Success;

        /// <summary>生成的产物文件路径（相对项目根）。</summary>
        public List<string> GeneratedFiles = new List<string>();

        public List<CExcelIssue> Issues = new List<CExcelIssue>();
    }

    /// <summary>单张表的描述（列名 + 类型 + 行数据 + 列说明），供生成器使用。</summary>
    public sealed class CExcelTable
    {
        public string SourcePath;
        public string SheetName;
        public string TableName;
        public List<string> Columns = new List<string>();
        public Dictionary<string, CExcelFieldKind> Kinds = new Dictionary<string, CExcelFieldKind>();
        public Dictionary<string, string> Comments = new Dictionary<string, string>();
        public List<Dictionary<string, object>> Rows = new List<Dictionary<string, object>>();
        public string PrimaryKey;
    }

    /// <summary>
    /// 配置表生成器：把 Excel 表生成产物（对齐 Idle 项目约定）——
    ///
    /// **普通 sheet（单表）**：
    ///   表名.json          表数据（JSON，格式 {"data":[...]}）
    ///   表名.cs            强类型数据类（字段注释取自表头说明行）
    ///   表名Getter.cs      加载器（Resources + JsonUtility → List + 主键查询）
    ///
    /// **多章节 sheet（sheet 名 "前缀_数字"，如 ChapterConfig_1）**：
    ///   前缀_章节.json                  每章节数据
    ///   前缀ConfigBase.cs               章节数据基类（全字段）
    ///   前缀_章节Config.cs              每章节数据子类（: 基类）
    ///   前缀_章节Getter.cs              每章节独立加载器
    ///   前缀Getter.cs                   聚合加载器（按章节查询 GetByID(id, chapterId)）
    ///
    /// 全 sheet 生成（<see cref="GenerateAllSheets"/>）：跳过名字含 sheet/debug 的 sheet，
    /// 多章节 sheet 聚合为一个 Getter。
    /// </summary>
    public static class CExcelGenerator
    {
        /// <summary>生成单 sheet（<see cref="CExcelGenerateOptions.SheetName"/> 为空时用第一个非跳过 sheet）。</summary>
        public static CExcelGenerateResult Generate(string excelPath, CExcelGenerateOptions options)
        {
            options = options ?? new CExcelGenerateOptions();
            string sheetName = options.SheetName;
            if (string.IsNullOrEmpty(sheetName))
            {
                List<string> names = CExcelReader.GetSheetNames(excelPath);
                sheetName = names.FirstOrDefault(n => !CExcelSheetName.IsSkippedSheet(n));
                if (string.IsNullOrEmpty(sheetName))
                {
                    var fail = new CExcelGenerateResult();
                    fail.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "未找到可用 sheet（文件可能为空或全部被跳过）: " + excelPath });
                    return fail;
                }
            }
            return GenerateSheet(excelPath, sheetName, options);
        }

        /// <summary>
        /// 批量生成目录下全部 .xlsx（跳过 ~$ 临时文件）；每张表生成全部可用 sheet，
        /// 多章节 sheet 聚合生成一个 Getter。单个表失败不中断其余。
        /// </summary>
        public static CExcelGenerateResult GenerateFolder(string folder, CExcelGenerateOptions options)
        {
            var result = new CExcelGenerateResult();
            if (!Directory.Exists(folder))
            {
                result.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "目录不存在: " + folder });
                return result;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xlsx"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("~$", StringComparison.Ordinal)) continue; // Excel 临时文件
                CExcelGenerateResult single = GenerateAllSheets(file, options);
                result.GeneratedFiles.AddRange(single.GeneratedFiles);
                result.Issues.AddRange(single.Issues);
                if (!single.Success) result.Success = false;
            }
            if (result.GeneratedFiles.Count > 0 && !result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error))
                result.Success = true;
            return result;
        }

        /// <summary>生成单张 Excel 的全部可用 sheet；多章节 sheet 聚合为一个 Getter。</summary>
        public static CExcelGenerateResult GenerateAllSheets(string excelPath, CExcelGenerateOptions options)
        {
            var result = new CExcelGenerateResult();
            options = options ?? new CExcelGenerateOptions();

            List<string> sheetNames = CExcelReader.GetSheetNames(excelPath);
            var usable = sheetNames.Where(n => !CExcelSheetName.IsSkippedSheet(n)).ToList();
            if (usable.Count == 0)
            {
                result.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "未找到可用 sheet: " + excelPath });
                return result;
            }

            // 分章节分组：前缀_数字 → 同前缀聚合
            var chapterGroups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (string sheet in usable)
            {
                if (CExcelSheetName.TryParseChapter(sheet, out string front, out int index))
                {
                    if (!chapterGroups.TryGetValue(front, out List<int> list))
                    {
                        list = new List<int>();
                        chapterGroups[front] = list;
                    }
                    list.Add(index);
                }
            }

            // 逐 sheet 生成（含分章节的子类/章节 Getter）
            foreach (string sheet in usable)
            {
                CExcelGenerateResult single = GenerateSheet(excelPath, sheet, options);
                result.GeneratedFiles.AddRange(single.GeneratedFiles);
                result.Issues.AddRange(single.Issues);
                if (!single.Success) result.Success = false;
            }

            // 聚合 Getter：同前缀章节组生成一次
            foreach (KeyValuePair<string, List<int>> group in chapterGroups)
            {
                group.Value.Sort();
                // 用该组第一个章节的列生成基类/主键信息（各章节同列；sheet 名 = 前缀_章节号）
                string firstSheet = group.Key + "_" + group.Value[0];
                CExcelReadResult read = CExcelReader.Read(excelPath, new CExcelReadOptions { SheetName = firstSheet });
                if (read.HasBlockingErrors)
                {
                    result.Issues.AddRange(read.Issues);
                    result.Success = false;
                    continue;
                }
                CExcelTable table = BuildTable(excelPath, read, options, firstSheet);
                if (table.PrimaryKey == null)
                {
                    result.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "未找到主键列: " + group.Key });
                    result.Success = false;
                    continue;
                }

                try
                {
                    EnsureFolder(options.OutputFolder);
                    string getterPath = Path.Combine(options.OutputFolder, group.Key + "Getter.cs");
                    File.WriteAllText(getterPath, WriteChapterGetter(table, group.Key, group.Value, options.Namespace, options.ResourcesPath, options.EncryptJson), new UTF8Encoding(false));
                    result.GeneratedFiles.Add(getterPath);
                }
                catch (Exception e)
                {
                    result.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "生成聚合 Getter 失败: " + e.Message });
                    result.Success = false;
                }
            }

            if (result.GeneratedFiles.Count > 0 && !result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error))
                result.Success = true;
            return result;
        }

        /// <summary>生成单 sheet 的产物（分章节：基类 + 子类 + 章节 Getter；普通：三件套）。</summary>
        private static CExcelGenerateResult GenerateSheet(string excelPath, string sheetName, CExcelGenerateOptions options)
        {
            var result = new CExcelGenerateResult();

            CExcelReadResult read = CExcelReader.Read(excelPath, new CExcelReadOptions { SheetName = sheetName });
            if (read.HasBlockingErrors)
            {
                result.Issues.AddRange(read.Issues);
                return result;
            }

            try
            {
                CExcelTable table = BuildTable(excelPath, read, options, sheetName);
                if (table.PrimaryKey == null)
                {
                    result.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "未找到主键列（需存在 *_i / *_l / *_s 列），表: " + sheetName });
                    return result;
                }

                EnsureFolder(options.OutputFolder);
                bool isChapter = CExcelSheetName.TryParseChapter(sheetName, out string frontName, out int chapterIndex);
                // 类名：普通表 = 选项 ClassName 或 sheet 名；分章节 = sheet 名（前缀_数字）
                string className = isChapter || string.IsNullOrEmpty(options.ClassName) ? sheetName : options.ClassName;

                if (options.GenerateJson)
                {
                    // JSON 必须生成到 Resources 下，运行时 Resources.Load 才能读到；
                    // EncryptJson 时写 XOR 密文字节（TextAsset.bytes 保留原始字节，运行时解密）
                    EnsureFolder(options.JsonResourcesFolder);
                    string jsonPath = Path.Combine(options.JsonResourcesFolder, className + ".json");
                    string jsonText = WriteJson(table);
                    if (options.EncryptJson)
                        File.WriteAllBytes(jsonPath, CExcelCrypto.Encode(jsonText));
                    else
                        File.WriteAllText(jsonPath, jsonText, new UTF8Encoding(false));
                    result.GeneratedFiles.Add(jsonPath);
                }

                if (options.GenerateClass)
                {
                    if (isChapter)
                    {
                        // 基类（同前缀共用一个，覆盖写）+ 章节子类 + 章节 Getter
                        string basePath = Path.Combine(options.OutputFolder, frontName + "ConfigBase.cs");
                        File.WriteAllText(basePath, WriteChapterBaseClass(table, frontName, options.Namespace), new UTF8Encoding(false));
                        if (!result.GeneratedFiles.Contains(basePath)) result.GeneratedFiles.Add(basePath);

                        string subPath = Path.Combine(options.OutputFolder, sheetName + "Config.cs");
                        File.WriteAllText(subPath, WriteChapterSubClass(table, frontName, options.Namespace), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(subPath);

                        string getterPath = Path.Combine(options.OutputFolder, sheetName + "Getter.cs");
                        File.WriteAllText(getterPath, WriteGetter(table, sheetName, sheetName + "Config", options.Namespace, options.ResourcesPath, options.EncryptJson), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(getterPath);
                    }
                    else
                    {
                        string classPath = Path.Combine(options.OutputFolder, className + ".cs");
                        File.WriteAllText(classPath, WriteClass(table, className, options.Namespace), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(classPath);

                        string getterPath = Path.Combine(options.OutputFolder, className + "Getter.cs");
                        File.WriteAllText(getterPath, WriteGetter(table, className, className, options.Namespace, options.ResourcesPath, options.EncryptJson), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(getterPath);
                    }
                }

                result.Success = true;
                return result;
            }
            catch (Exception e)
            {
                result.Issues.Add(new CExcelIssue { Level = CExcelIssueLevel.Error, Row = 0, Column = "-", Message = "生成失败: " + e.Message });
                return result;
            }
        }

        // ========== 表构建 ==========

        private static CExcelTable BuildTable(string excelPath, CExcelReadResult read, CExcelGenerateOptions options, string sheetName)
        {
            var table = new CExcelTable
            {
                SourcePath = excelPath,
                SheetName = sheetName,
                TableName = sheetName,
                Columns = read.Columns,
                Rows = read.Rows,
                Comments = read.ColumnComments,
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

        // ========== 模板公共 ==========

        private const string HeaderLine = "// Auto-generated by CoffeeBean.Excel. Do not edit.";

        /// <summary>字段注释：优先表头中文说明行，否则源列名。</summary>
        private static string FieldComment(CExcelTable table, string column)
        {
            string comment = table.Comments != null && table.Comments.TryGetValue(column, out string c) && c.Length > 0
                ? c
                : column;
            return comment;
        }

        // ========== 普通单表：数据类 ==========

        private static string WriteClass(CExcelTable table, string className, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine("// Source sheet: " + table.SheetName);
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + className + " config row.</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    public sealed class " + className);
            sb.AppendLine("    {");
            foreach (string column in table.Columns)
            {
                string type = CExcelTypeInfer.CSharpType(table.Kinds[column]);
                string field = CExcelTypeInfer.ToFieldName(column);
                sb.AppendLine("        /// <summary>" + FieldComment(table, column) + "</summary>");
                sb.AppendLine("        public " + type + " " + field + ";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== 多章节：基类 / 子类 ==========

        private static string WriteChapterBaseClass(CExcelTable table, string frontName, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine("// Multi-chapter base: " + frontName + " (sheets like " + frontName + "_1, " + frontName + "_2 ...)");
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + frontName + " chapter base (auto-generated).</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    public class " + frontName + "ConfigBase");
            sb.AppendLine("    {");
            foreach (string column in table.Columns)
            {
                string type = CExcelTypeInfer.CSharpType(table.Kinds[column]);
                string field = CExcelTypeInfer.ToFieldName(column);
                sb.AppendLine("        /// <summary>" + FieldComment(table, column) + "</summary>");
                sb.AppendLine("        public " + type + " " + field + ";");
            }
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string WriteChapterSubClass(CExcelTable table, string frontName, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine("// Source sheet: " + table.SheetName);
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + table.SheetName + " chapter data (auto-generated).</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    public sealed class " + table.SheetName + "Config : " + frontName + "ConfigBase");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== Getter（普通表 / 章节独立） ==========

        /// <param name="className">Getter 类名与 AssetPath（普通表 = 表名；章节 = sheet 名）。</param>
        /// <param name="dataType">数据类名（普通表 = 表名；章节 = 子类名，如 ChapterConfig_1Config）。</param>
        /// <param name="resourcesPath">Resources 相对路径（如 "Configs"）。</param>
        /// <param name="encrypt">JSON 是否加密（生成时决定，Getter 加载时对应解密）。</param>
        private static string WriteGetter(CExcelTable table, string className, string dataType, string ns, string resourcesPath, bool encrypt)
        {
            CExcelFieldKind keyKind = table.Kinds[table.PrimaryKey];
            string keyType = CExcelTypeInfer.CSharpType(keyKind);
            string keyField = CExcelTypeInfer.ToFieldName(table.PrimaryKey);
            string assetPath = resourcesPath + "/" + className;

            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine("// Source sheet: " + table.SheetName + "  Primary key: " + table.PrimaryKey);
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + className + " config loader (auto-generated).</summary>");
            sb.AppendLine("    public static class " + className + "Getter");
            sb.AppendLine("    {");
            sb.AppendLine("        private const string AssetPath = \"" + assetPath + "\";");
            sb.AppendLine("        private static List<" + dataType + "> _all;");
            sb.AppendLine("        private static Dictionary<" + keyType + ", " + dataType + "> _byKey;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>All config rows (lazy loaded).</summary>");
            sb.AppendLine("        public static List<" + dataType + "> All => _all ??= Load();");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>Query by primary key; null if not found.</summary>");
            sb.AppendLine("        public static " + dataType + " Get(" + keyType + " key)");
            sb.AppendLine("        {");
            sb.AppendLine("            _byKey ??= BuildIndex();");
            sb.AppendLine("            return _byKey.TryGetValue(key, out " + dataType + " item) ? item : null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static List<" + dataType + "> Load()");
            sb.AppendLine("        {");
            sb.AppendLine("            TextAsset asset = Resources.Load<TextAsset>(AssetPath);");
            sb.AppendLine("            if (asset == null) { Debug.LogError(\"Config missing: \" + AssetPath); return new List<" + dataType + ">(); }");
            if (encrypt)
                sb.AppendLine("            var wrapper = JsonUtility.FromJson<Wrapper>(Decode(asset.bytes));");
            else
                sb.AppendLine("            var wrapper = JsonUtility.FromJson<Wrapper>(asset.text);");
            sb.AppendLine("            return wrapper != null ? wrapper.data : new List<" + dataType + ">();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static Dictionary<" + keyType + ", " + dataType + "> BuildIndex()");
            sb.AppendLine("        {");
            sb.AppendLine("            var index = new Dictionary<" + keyType + ", " + dataType + ">();");
            sb.AppendLine("            foreach (" + dataType + " item in All) index[item." + keyField + "] = item;");
            sb.AppendLine("            return index;");
            sb.AppendLine("        }");
            if (encrypt) AppendDecryptMethods(sb);
            sb.AppendLine();
            sb.AppendLine("        [System.Serializable] private sealed class Wrapper { public List<" + dataType + "> data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>向生成代码追加解密方法（与生成端 CExcelCrypto 同种子同算法；仅 encrypt 时生成）。</summary>
        private static void AppendDecryptMethods(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("        private static string Decode(byte[] data)");
            sb.AppendLine("        {");
            sb.AppendLine("            byte[] key = GenerateKey(data.Length);");
            sb.AppendLine("            var result = new byte[data.Length];");
            sb.AppendLine("            for (int i = 0; i < data.Length; i++)");
            sb.AppendLine("                result[i] = (byte)(data[i] ^ key[i] ^ (byte)(i & 0x7F));");
            sb.AppendLine("            return System.Text.Encoding.UTF8.GetString(result);");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static byte[] GenerateKey(int length)");
            sb.AppendLine("        {");
            sb.AppendLine("            var key = new byte[length];");
            sb.AppendLine("            uint state = 2166136261;");
            sb.AppendLine("            for (int i = 0; i < length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                state ^= \"" + CExcelCrypto.KeySeed + "\"[i % " + CExcelCrypto.KeySeed.Length + "];");
            sb.AppendLine("                state *= 16777619;");
            sb.AppendLine("                key[i] = (byte)(state >> 24);");
            sb.AppendLine("            }");
            sb.AppendLine("            return key;");
            sb.AppendLine("        }");
        }

        // ========== 多章节：聚合 Getter ==========

        private static string WriteChapterGetter(CExcelTable table, string frontName, List<int> chapters, string ns, string resourcesPath, bool encrypt)
        {
            CExcelFieldKind keyKind = table.Kinds[table.PrimaryKey];
            string keyType = CExcelTypeInfer.CSharpType(keyKind);
            string keyField = CExcelTypeInfer.ToFieldName(table.PrimaryKey);
            string baseClass = frontName + "ConfigBase";
            string chaptersArray = string.Join(", ", chapters.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            string assetPath = resourcesPath + "/" + frontName + "_";

            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine("// Multi-chapter getter: " + frontName + " (chapters: " + chaptersArray + ")");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + frontName + " multi-chapter config loader (auto-generated).</summary>");
            sb.AppendLine("    public static class " + frontName + "Getter");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>Available chapter ids.</summary>");
            sb.AppendLine("        public static readonly int[] Chapters = new[] { " + chaptersArray + " };");
            sb.AppendLine();
            sb.AppendLine("        public static int ChapterCount => Chapters.Length;");
            sb.AppendLine();

            // 每章节懒加载属性 + 静态字段
            foreach (int chapter in chapters)
            {
                string subClass = frontName + "_" + chapter + "Config";
                sb.AppendLine("        private static List<" + subClass + "> _c" + chapter + ";");
                sb.AppendLine("        /// <summary>Chapter " + chapter + " rows (lazy loaded).</summary>");
                sb.AppendLine("        public static List<" + subClass + "> Chapter" + chapter + " => _c" + chapter + " ??= Load<" + subClass + ">(" + chapter + ");");
                sb.AppendLine();
            }

            // 按章节查询
            sb.AppendLine("        /// <summary>Query by primary key in a chapter; null if not found.</summary>");
            sb.AppendLine("        public static " + baseClass + " GetByID(" + keyType + " key, int chapterId) => chapterId switch");
            sb.AppendLine("        {");
            foreach (int chapter in chapters)
            {
                string subClass = frontName + "_" + chapter + "Config";
                sb.AppendLine("            " + chapter + " => Find(Chapter" + chapter + ", key),");
            }
            sb.AppendLine("            _ => null,");
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>All rows of a chapter (as base type).</summary>");
            sb.AppendLine("        public static IEnumerable<" + baseClass + "> GetChapter(int chapterId) => chapterId switch");
            sb.AppendLine("        {");
            foreach (int chapter in chapters)
            {
                sb.AppendLine("            " + chapter + " => Chapter" + chapter + ",");
            }
            sb.AppendLine("            _ => Enumerable.Empty<" + baseClass + ">(),");
            sb.AppendLine("        };");
            sb.AppendLine();
            sb.AppendLine("        private static List<T> Load<T>(int chapterId) where T : " + baseClass);
            sb.AppendLine("        {");
            sb.AppendLine("            TextAsset asset = Resources.Load<TextAsset>(\"" + assetPath + "\" + chapterId);");
            sb.AppendLine("            if (asset == null) { Debug.LogError(\"Config missing: " + assetPath + "\" + chapterId); return new List<T>(); }");
            if (encrypt)
                sb.AppendLine("            var wrapper = JsonUtility.FromJson<Wrapper<T>>(Decode(asset.bytes));");
            else
                sb.AppendLine("            var wrapper = JsonUtility.FromJson<Wrapper<T>>(asset.text);");
            sb.AppendLine("            return wrapper != null ? wrapper.data : new List<T>();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static T Find<T>(List<T> rows, " + keyType + " key) where T : " + baseClass);
            sb.AppendLine("            => rows.FirstOrDefault(x => x." + keyField + " == key);");
            if (encrypt) AppendDecryptMethods(sb);
            sb.AppendLine();
            sb.AppendLine("        [System.Serializable] private sealed class Wrapper<T> { public List<T> data; }");
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
