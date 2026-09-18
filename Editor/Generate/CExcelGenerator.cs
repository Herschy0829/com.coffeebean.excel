using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CoffeeBean
{
    /// <summary>生成选项。</summary>
    public sealed class CExcelGenerateOptions
    {
        /// <summary>输出目录（相对 Assets 或绝对路径）。</summary>
        public string OutputFolder = "Assets/Configs/Generated";

        /// <summary>生成类 / Getter 的命名空间（默认 CoffeeBean 根命名空间，`using CoffeeBean;` 即可访问）。</summary>
        public string Namespace = "CoffeeBean";

        /// <summary>类名（默认取表名/sheet 名）。</summary>
        public string ClassName;

        /// <summary>主键列名（默认自动选择第一个可做键的列）。</summary>
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

        /// <summary>
        /// 严格类型校验（默认 true）：每个单元格都按列声明的类型真解析一遍，解析不了就报错并中止该表。
        /// 为什么默认开：以前 `Level_i` 填 `abc` 会被**安静地写成 0**，这类错数据比编译错误难查得多。
        /// 老表迁移期可临时关掉（只跳过值校验，枚举定义校验仍然生效）。
        /// </summary>
        public bool StrictTypeCheck = true;
    }

    /// <summary>生成结果。</summary>
    public sealed class CExcelGenerateResult
    {
        public bool Success;

        /// <summary>生成的产物文件路径（相对项目根）。</summary>
        public List<string> GeneratedFiles = new List<string>();

        public List<CExcelIssue> Issues = new List<CExcelIssue>();
    }

    /// <summary>单张表的描述（列名 + 类型 + 枚举定义 + 行数据 + 列说明），供生成器使用。</summary>
    public sealed class CExcelTable
    {
        public string SourcePath;
        public string SheetName;
        public string TableName;

        /// <summary>枚举类型名前缀（普通表 = 类名；章节表 = 章节前缀，各章节共用一个枚举）。</summary>
        public string TypeNamePrefix;

        public List<string> Columns = new List<string>();
        public Dictionary<string, CExcelFieldKind> Kinds = new Dictionary<string, CExcelFieldKind>(StringComparer.OrdinalIgnoreCase);

        /// <summary>枚举族列的枚举定义（键 = 列名；普通列不在里面）。</summary>
        public Dictionary<string, CExcelEnumDef> Enums = new Dictionary<string, CExcelEnumDef>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> Comments = new Dictionary<string, string>();
        public List<Dictionary<string, object>> Rows = new List<Dictionary<string, object>>();
        public string PrimaryKey;
        public int HeaderRowIndex;

        /// <summary>列的实际 C# 类型（枚举族带上生成/引用的枚举类型名）。</summary>
        public string CSharpTypeOf(string column)
        {
            CExcelFieldKind kind = Kinds[column];
            if (CExcelTypeInfer.IsEnumKind(kind))
            {
                if (!Enums.TryGetValue(column, out CExcelEnumDef def)) return CExcelTypeInfer.IsArray(kind) ? "int[]" : "int";
                return CExcelTypeInfer.IsArray(kind) ? def.TypeName + "[]" : def.TypeName;
            }
            return CExcelTypeInfer.CSharpType(kind);
        }
    }

    /// <summary>
    /// 配置表生成器：把 Excel 表生成产物（对齐 Idle 项目约定）——
    ///
    /// **普通 sheet（单表）**：
    ///   表名.json          表数据（JSON，格式 {"data":[...]}，字段名 = 列名去后缀转 PascalCase）
    ///   表名.cs            强类型数据类（字段注释取自表头说明行）+ 该表用到的枚举定义
    ///   表名Getter.cs      加载器（Resources + Newtonsoft → List + 主键查询）
    ///
    /// **多章节 sheet（sheet 名 "前缀_数字"，如 ChapterConfig_1）**：
    ///   前缀_章节.json                  每章节数据
    ///   前缀ConfigBase.cs               章节数据基类（全字段 + 共用枚举定义）
    ///   前缀_章节Config.cs              每章节数据子类（: 基类）
    ///   前缀_章节Getter.cs              每章节独立加载器
    ///   前缀Getter.cs                   聚合加载器（按章节查询 GetByID(id, chapterId)）
    ///
    /// 全 sheet 生成（<see cref="GenerateAllSheets"/>）：跳过名字含 sheet/debug 的 sheet，
    /// 多章节 sheet 聚合为一个 Getter；同一章节组的枚举取值取**并集**（各章节共用一套编号）。
    ///
    /// **JSON 后端 = Newtonsoft.Json**（<c>com.unity.nuget.newtonsoft-json</c>）：
    /// JsonUtility 读不回 BigInteger / decimal / DateTime / Guid / Dictionary / Rect，
    /// 而本工具要支持这些类型，所以生成的 Getter 统一用 Newtonsoft。
    /// 注意 JSON **文本**由本工具手写（Newtonsoft 序列化 UnityEngine.Vector3 会因
    /// <c>normalized</c> 自引用直接抛异常），Newtonsoft 只负责反序列化。
    /// </summary>
    public static class CExcelGenerator
    {
        /// <summary>
        /// 模板版本：**改了生成模板就 +1**。增量生成器把它记进状态里，
        /// 于是升级框架后旧产物会被自动重新生成（否则"表没改"会一直跳过，生成代码永远停在旧模板）。
        /// v2：JSON 后端换成 Newtonsoft + 枚举生成 + 新增类型。
        /// </summary>
        public const int TemplateVersion = 2;

        /// <summary>生成单 sheet（<see cref="CExcelGenerateOptions.SheetName"/> 为空时用第一个非跳过 sheet）。</summary>
        public static CExcelGenerateResult Generate(string excelPath, CExcelGenerateOptions options)
        {
            options = options ?? new CExcelGenerateOptions();
            var context = new RunContext(options);
            var result = new CExcelGenerateResult();

            string sheetName = options.SheetName;
            if (string.IsNullOrEmpty(sheetName))
            {
                List<string> names = CExcelReader.GetSheetNames(excelPath);
                sheetName = names.FirstOrDefault(n => !CExcelSheetName.IsSkippedSheet(n));
                if (string.IsNullOrEmpty(sheetName))
                {
                    result.Issues.Add(Error(0, "-", "未找到可用 sheet（文件可能为空或全部被跳过）: " + excelPath));
                    return result;
                }
            }

            // 章节 sheet：先把同组各章节的枚举并集建好，再生成（否则基类里的枚举不完整）
            PrepareGroupFor(context, excelPath, sheetName, result.Issues);
            CExcelGenerateResult single = GenerateSheet(context, excelPath, sheetName, result);
            // GenerateSheet 把 Issues 写进 aggregate（= result），但产物与成败在它自己的返回值里
            result.GeneratedFiles.AddRange(single.GeneratedFiles);
            result.Success = single.Success;
            return result;
        }

        /// <summary>
        /// 只校验不生成（预览窗口的"校验"按钮用）：读表 + 建枚举定义 + 类型校验，
        /// 返回全部问题（含警告）。不写任何文件。
        /// </summary>
        public static List<CExcelIssue> Validate(string excelPath, string sheetName, CExcelGenerateOptions options)
        {
            CExcelGenerateOptions effective = options ?? new CExcelGenerateOptions();
            var context = new RunContext(effective);
            var issues = new List<CExcelIssue>();

            if (!string.IsNullOrEmpty(sheetName))
            {
                PrepareGroupFor(context, excelPath, sheetName, issues);
                if (issues.Exists(i => i.Level == CExcelIssueLevel.Error)) return issues;
            }

            CExcelReadResult read = context.Read(excelPath, sheetName);
            if (read.HasBlockingErrors)
            {
                issues.AddRange(read.Issues);
                return issues;
            }

            bool isChapter = CExcelSheetName.TryParseChapter(sheetName, out string frontName, out int _);
            string className = isChapter || string.IsNullOrEmpty(effective.ClassName) ? sheetName : effective.ClassName;
            string typeNamePrefix = isChapter ? frontName : className;

            CExcelTable table = BuildTable(context, read, sheetName, typeNamePrefix,
                isChapter ? context.GetGroupEnums(frontName) : null, issues);
            CExcelTableValidator.Validate(table, effective.StrictTypeCheck, issues);
            return issues;
        }

        /// <summary>批量生成目录下全部 .xlsx（跳过 ~$ 临时文件）；每张表生成全部可用 sheet，
        /// 多章节 sheet 聚合生成一个 Getter。单个表失败不中断其余。
        /// 枚举登记表跨文件共用 —— 重名枚举在**不同文件**里也会被查出来。
        /// </summary>
        public static CExcelGenerateResult GenerateFolder(string folder, CExcelGenerateOptions options)
        {
            options = options ?? new CExcelGenerateOptions();
            var context = new RunContext(options);
            var result = new CExcelGenerateResult();
            if (!Directory.Exists(folder))
            {
                result.Issues.Add(Error(0, "-", "目录不存在: " + folder));
                return result;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xlsx"))
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith("~$", StringComparison.Ordinal)) continue; // Excel 临时文件
                CExcelGenerateResult single = GenerateAllSheets(file, options, context);
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
            CExcelGenerateOptions effective = options ?? new CExcelGenerateOptions();
            return GenerateAllSheets(excelPath, effective, new RunContext(effective));
        }

        private static CExcelGenerateResult GenerateAllSheets(string excelPath, CExcelGenerateOptions options, RunContext context)
        {
            var result = new CExcelGenerateResult();

            List<string> sheetNames = CExcelReader.GetSheetNames(excelPath);
            var usable = sheetNames.Where(n => !CExcelSheetName.IsSkippedSheet(n)).ToList();
            if (usable.Count == 0)
            {
                result.Issues.Add(Error(0, "-", "未找到可用 sheet: " + excelPath));
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

            // 先把各章节组的枚举并集建好（基类要一次性写全）
            foreach (KeyValuePair<string, List<int>> group in chapterGroups)
            {
                group.Value.Sort();
                var groupIssues = new List<CExcelIssue>();
                BuildGroupEnums(context, excelPath, group.Key, group.Value, groupIssues);
                result.Issues.AddRange(groupIssues);
            }

            // 逐 sheet 生成（含分章节的子类/章节 Getter）
            foreach (string sheet in usable)
            {
                CExcelGenerateResult single = GenerateSheet(context, excelPath, sheet, result);
                result.GeneratedFiles.AddRange(single.GeneratedFiles);
            }

            // 聚合 Getter：同前缀章节组生成一次
            foreach (KeyValuePair<string, List<int>> group in chapterGroups)
            {
                if (context.FailedGroups.Contains(group.Key)) continue;

                string firstSheet = group.Key + "_" + group.Value[0];
                CExcelReadResult read = context.Read(excelPath, firstSheet);
                if (read.HasBlockingErrors)
                {
                    result.Issues.AddRange(read.Issues);
                    result.Success = false;
                    continue;
                }

                var issues = new List<CExcelIssue>();
                CExcelTable table = BuildTable(context, read, firstSheet, group.Key, context.GetGroupEnums(group.Key), issues);
                result.Issues.AddRange(issues);
                if (table.PrimaryKey == null)
                {
                    result.Issues.Add(Error(0, "-", "未找到主键列: " + group.Key));
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
                    result.Issues.Add(Error(0, "-", "生成聚合 Getter 失败: " + e.Message));
                    result.Success = false;
                }
            }

            if (result.GeneratedFiles.Count > 0 && !result.Issues.Exists(i => i.Level == CExcelIssueLevel.Error))
                result.Success = true;
            return result;
        }

        /// <summary>生成单 sheet 的产物（分章节：基类 + 子类 + 章节 Getter；普通：三件套）。</summary>
        private static CExcelGenerateResult GenerateSheet(RunContext context, string excelPath, string sheetName, CExcelGenerateResult aggregate)
        {
            CExcelGenerateOptions options = context.Options;
            var result = new CExcelGenerateResult();
            if (string.IsNullOrEmpty(options.Namespace)) options.Namespace = "CoffeeBean";

            bool isChapter = CExcelSheetName.TryParseChapter(sheetName, out string frontName, out int chapterIndex);
            if (isChapter && context.FailedGroups.Contains(frontName)) return result;

            CExcelReadResult read = context.Read(excelPath, sheetName);
            if (read.HasBlockingErrors)
            {
                result.Issues.AddRange(read.Issues);
                aggregate.Issues.AddRange(read.Issues);
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }

            string className = isChapter || string.IsNullOrEmpty(options.ClassName) ? sheetName : options.ClassName;
            string typeNamePrefix = isChapter ? frontName : className;
            Dictionary<string, CExcelEnumDef> presetEnums = isChapter ? context.GetGroupEnums(frontName) : null;

            var issues = new List<CExcelIssue>();
            CExcelTable table = BuildTable(context, read, sheetName, typeNamePrefix, presetEnums, issues);
            CExcelTableValidator.Validate(table, options.StrictTypeCheck, issues);
            aggregate.Issues.AddRange(issues);
            if (issues.Exists(i => i.Level == CExcelIssueLevel.Error))
            {
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }

            if (table.PrimaryKey == null)
            {
                aggregate.Issues.Add(Error(0, "-", "未找到主键列（需存在可做键的列，如 *_i / *_l / *_s / *_e），表: " + sheetName));
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }

            // 枚举类型登记：一次生成里同名的枚举成员必须一致，否则会生成重复的 C# 类型
            bool enumConflict = false;
            foreach (string column in table.Columns)
            {
                if (!table.Enums.TryGetValue(column, out CExcelEnumDef def)) continue;
                if (!context.Registry.Register(def, sheetName, aggregate.Issues)) enumConflict = true;
            }
            if (enumConflict)
            {
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }

            try
            {
                EnsureFolder(options.OutputFolder);
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
                        // 基类（同前缀共用一个，覆盖写 —— 各章节内容一致因为枚举用的是并集）+ 章节子类 + 章节 Getter
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
                // 确保生成代码所在目录有独立 asmdef（程序集级增量编译隔离：
                // 改配置表只重编译生成程序集，业务代码不重编译，加快迭代编译）
                EnsureGeneratedAsmdef(options.OutputFolder, options.Namespace);
                return result;
            }
            catch (Exception e)
            {
                aggregate.Issues.Add(Error(0, "-", "生成失败: " + e.Message));
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }
        }

        // ========== 运行上下文（读缓存 / 枚举登记 / 章节并集） ==========

        private sealed class RunContext
        {
            public readonly CExcelGenerateOptions Options;
            public readonly CExcelEnumRegistry Registry = new CExcelEnumRegistry();
            public readonly HashSet<string> FailedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            private readonly Dictionary<string, CExcelReadResult> _reads = new Dictionary<string, CExcelReadResult>(StringComparer.Ordinal);
            private readonly Dictionary<string, Dictionary<string, CExcelEnumDef>> _groupEnums =
                new Dictionary<string, Dictionary<string, CExcelEnumDef>>(StringComparer.OrdinalIgnoreCase);

            public RunContext(CExcelGenerateOptions options) => Options = options;

            /// <summary>读 sheet（一次运行内缓存：章节表要读两遍 —— 建枚举并集 + 生成）。</summary>
            public CExcelReadResult Read(string path, string sheet)
            {
                string key = path + "|" + sheet;
                if (_reads.TryGetValue(key, out CExcelReadResult cached)) return cached;
                CExcelReadResult read = CExcelReader.Read(path, new CExcelReadOptions { SheetName = sheet });
                _reads[key] = read;
                return read;
            }

            public Dictionary<string, CExcelEnumDef> GetGroupEnums(string front)
                => _groupEnums.TryGetValue(front, out Dictionary<string, CExcelEnumDef> defs) ? defs : null;

            public void SetGroupEnums(string front, Dictionary<string, CExcelEnumDef> defs) => _groupEnums[front] = defs;
        }

        /// <summary>单 sheet 生成：当它是章节表时，先把同组枚举并集备好。</summary>
        private static void PrepareGroupFor(RunContext context, string excelPath, string sheetName, List<CExcelIssue> issues)
        {
            if (!CExcelSheetName.TryParseChapter(sheetName, out string front, out int _)) return;
            if (context.GetGroupEnums(front) != null || context.FailedGroups.Contains(front)) return;

            var chapters = new List<int>();
            foreach (string sheet in CExcelReader.GetSheetNames(excelPath))
            {
                if (CExcelSheetName.TryParseChapter(sheet, out string sheetFront, out int index)
                    && string.Equals(sheetFront, front, StringComparison.OrdinalIgnoreCase))
                    chapters.Add(index);
            }
            chapters.Sort();
            if (chapters.Count == 0) chapters.Add(0);
            var groupIssues = new List<CExcelIssue>();
            BuildGroupEnums(context, excelPath, front, chapters, groupIssues);
            issues.AddRange(groupIssues);
        }

        /// <summary>
        /// 建一个章节组的枚举定义：把各章节同一列的取值**按章节顺序拼起来再建一次**。
        ///
        /// 为什么不是"各章节各建一套再合并"：自动编号是按首次出现顺序给的，
        /// 分开建会让同一成员在不同章节拿到不同值。拼起来建，编号天然一致。
        /// </summary>
        private static void BuildGroupEnums(RunContext context, string excelPath, string front, List<int> chapters, List<CExcelIssue> issues)
        {
            var columnOrder = new List<string>();
            var seenColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reads = new List<KeyValuePair<string, CExcelReadResult>>();

            foreach (int index in chapters)
            {
                string sheet = front + "_" + index;
                CExcelReadResult read = context.Read(excelPath, sheet);
                if (read.HasBlockingErrors)
                {
                    issues.AddRange(read.Issues);
                    continue;
                }
                reads.Add(new KeyValuePair<string, CExcelReadResult>(sheet, read));
                foreach (string column in read.Columns)
                    if (seenColumns.Add(column)) columnOrder.Add(column);
            }

            var defs = new Dictionary<string, CExcelEnumDef>(StringComparer.OrdinalIgnoreCase);
            foreach (string column in columnOrder)
            {
                CExcelFieldKind? kind = CExcelTypeInfer.FromSuffix(column);
                if (!kind.HasValue || !CExcelTypeInfer.IsEnumKind(kind.Value)) continue;

                var cells = new List<CExcelEnumCell>();
                foreach (KeyValuePair<string, CExcelReadResult> pair in reads)
                {
                    CExcelReadResult read = pair.Value;
                    if (!read.Columns.Contains(column)) continue;
                    for (int r = 0; r < read.Rows.Count; r++)
                    {
                        object value = read.Rows[r].TryGetValue(column, out object cell) ? cell : null;
                        cells.Add(new CExcelEnumCell(CExcelValue.ToText(value), read.HeaderRowIndex + r + 2, pair.Key));
                    }
                }

                defs[column] = CExcelEnumBuilder.Build(column, CExcelTypeInfer.EnumElementKind(kind.Value),
                    CExcelTypeInfer.IsArray(kind.Value), front, cells, issues);
            }

            context.SetGroupEnums(front, defs);
            if (issues.Exists(i => i.Level == CExcelIssueLevel.Error)) context.FailedGroups.Add(front);
        }

        // ========== 表构建 ==========

        private static CExcelTable BuildTable(RunContext context, CExcelReadResult read, string sheetName,
            string typeNamePrefix, Dictionary<string, CExcelEnumDef> presetEnums, List<CExcelIssue> issues)
        {
            var table = new CExcelTable
            {
                SourcePath = read.SourcePath,
                SheetName = sheetName,
                TableName = sheetName,
                TypeNamePrefix = typeNamePrefix,
                Columns = read.Columns,
                Rows = read.Rows,
                Comments = read.ColumnComments,
                HeaderRowIndex = read.HeaderRowIndex,
            };

            foreach (string column in read.Columns)
            {
                var values = new List<object>();
                foreach (Dictionary<string, object> row in read.Rows)
                    values.Add(row.TryGetValue(column, out object v) ? v : null);

                CExcelFieldKind kind = CExcelTypeInfer.Infer(column, values);
                table.Kinds[column] = kind;

                if (!CExcelTypeInfer.IsEnumKind(kind)) continue;

                if (presetEnums != null && presetEnums.TryGetValue(column, out CExcelEnumDef preset))
                {
                    table.Enums[column] = preset;
                    continue;
                }

                var cells = new List<CExcelEnumCell>();
                for (int r = 0; r < read.Rows.Count; r++)
                    cells.Add(new CExcelEnumCell(CExcelValue.ToText(values[r]), read.HeaderRowIndex + r + 2, sheetName));

                table.Enums[column] = CExcelEnumBuilder.Build(column, CExcelTypeInfer.EnumElementKind(kind),
                    CExcelTypeInfer.IsArray(kind), typeNamePrefix, cells, issues);
            }

            table.PrimaryKey = context.Options.PrimaryKey;
            if (string.IsNullOrEmpty(table.PrimaryKey) || !table.Kinds.ContainsKey(table.PrimaryKey)
                || CExcelTypeInfer.IsArray(table.Kinds[table.PrimaryKey]))
                table.PrimaryKey = PickPrimaryKey(table);
            return table;
        }

        private static string PickPrimaryKey(CExcelTable table)
        {
            foreach (string column in table.Columns)
            {
                if (CExcelTypeInfer.IsKeyCandidate(table.Kinds[column])) return column;
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
                    sb.Append(CExcelCellJson.Quote(CExcelTypeInfer.ToFieldName(column))).Append(':');
                    object value = row.TryGetValue(column, out object v) ? v : null;
                    table.Enums.TryGetValue(column, out CExcelEnumDef enumDef);
                    sb.Append(CExcelCellJson.Literal(CExcelValue.ToText(value), table.Kinds[column], enumDef, out _));
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ========== 模板公共 ==========

        private const string HeaderLine = "// Auto-generated by CoffeeBean.Excel. Do not edit.";

        /// <summary>模板版本注释：产物里能直接看出是哪版模板生成的。</summary>
        private static string TemplateLine => $"// Generator template: v{TemplateVersion} (JSON backend: Newtonsoft.Json)";

        private static CExcelIssue Error(int row, string column, string message)
            => new CExcelIssue { Level = CExcelIssueLevel.Error, Row = row, Column = column, Message = message };

        /// <summary>字段注释：优先表头中文说明行，否则源列名。</summary>
        private static string FieldComment(CExcelTable table, string column)
        {
            string comment = table.Comments != null && table.Comments.TryGetValue(column, out string c) && c.Length > 0
                ? c
                : column;
            return comment;
        }

        /// <summary>写字段声明（含注释）。</summary>
        private static void AppendField(StringBuilder sb, CExcelTable table, string column, string indent)
        {
            string type = table.CSharpTypeOf(column);
            string field = CExcelTypeInfer.ToFieldName(column);
            sb.AppendLine(indent + "/// <summary>" + FieldComment(table, column) + "</summary>");
            sb.AppendLine(indent + "public " + type + " " + field + ";");
        }

        /// <summary>
        /// 写本表生成出来的枚举定义（引用型枚举不生成，定义在别处）。
        /// 注释刻意用英文：生成的代码保持"语言中立"（只有表头说明行那种用户自己写的内容才可能是中文），
        /// <c>CExcelMultiSheetTests.GeneratedCode_NoToolChinese_CommentsUseColumnNames</c> 在锁这条。
        /// </summary>
        private static void AppendEnums(StringBuilder sb, CExcelTable table, string indent)
        {
            foreach (string column in table.Columns)
            {
                if (!table.Enums.TryGetValue(column, out CExcelEnumDef def)) continue;
                if (def.IsExternal || def.Members.Count == 0) continue;

                string field = CExcelTypeInfer.ToFieldName(column);
                sb.AppendLine();
                sb.AppendLine(indent + "/// <summary>Values of " + field + " (generated from column " + column + "; JSON stores the number).</summary>");
                if (def.IsFlags) sb.AppendLine(indent + "[System.Flags]");
                sb.AppendLine(indent + "public enum " + def.TypeName);
                sb.AppendLine(indent + "{");
                foreach (CExcelEnumMember member in def.Members)
                {
                    sb.AppendLine(indent + "    " + member.Name + " = " + member.Value.ToString(CultureInfo.InvariantCulture) + ",");
                }
                sb.AppendLine(indent + "}");
            }
        }

        // ========== 普通单表：数据类 ==========

        private static string WriteClass(CExcelTable table, string className, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
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
                AppendField(sb, table, column, "        ");
            sb.AppendLine("    }");
            AppendEnums(sb, table, "    ");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== 多章节：基类 / 子类 ==========

        private static string WriteChapterBaseClass(CExcelTable table, string frontName, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
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
                AppendField(sb, table, column, "        ");
            sb.AppendLine("    }");
            AppendEnums(sb, table, "    ");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string WriteChapterSubClass(CExcelTable table, string frontName, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
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
            string keyType = table.CSharpTypeOf(table.PrimaryKey);
            string keyField = CExcelTypeInfer.ToFieldName(table.PrimaryKey);
            string assetPath = resourcesPath + "/" + className;

            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Source sheet: " + table.SheetName + "  Primary key: " + table.PrimaryKey);
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Newtonsoft.Json;");
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
            sb.AppendLine("            var wrapper = JsonConvert.DeserializeObject<Wrapper>(" + (encrypt ? "Decode(asset.bytes)" : "asset.text") + ");");
            sb.AppendLine("            return wrapper != null && wrapper.data != null ? wrapper.data : new List<" + dataType + ">();");
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
            sb.AppendLine("        [System.Serializable] public sealed class Wrapper { public List<" + dataType + "> data; }");
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
            string keyType = table.CSharpTypeOf(table.PrimaryKey);
            string keyField = CExcelTypeInfer.ToFieldName(table.PrimaryKey);
            string baseClass = frontName + "ConfigBase";
            string chaptersArray = string.Join(", ", chapters.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            string assetPath = resourcesPath + "/" + frontName + "_";

            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Multi-chapter getter: " + frontName + " (chapters: " + chaptersArray + ")");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using Newtonsoft.Json;");
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
            sb.AppendLine("            var wrapper = JsonConvert.DeserializeObject<Wrapper<T>>(" + (encrypt ? "Decode(asset.bytes)" : "asset.text") + ");");
            sb.AppendLine("            return wrapper != null && wrapper.data != null ? wrapper.data : new List<T>();");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static T Find<T>(List<T> rows, " + keyType + " key) where T : " + baseClass);
            sb.AppendLine("            => rows.FirstOrDefault(x => x." + keyField + " == key);");
            if (encrypt) AppendDecryptMethods(sb);
            sb.AppendLine();
            sb.AppendLine("        [System.Serializable] public sealed class Wrapper<T> { public List<T> data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 在生成代码目录创建独立 asmdef（幂等：已存在不覆盖）。
        /// 程序集名 = {Namespace}.Generated；生成的表类/Getter 归入该程序集，
        /// 与业务代码隔离——之后改配置表只重编译生成程序集（小、快），业务程序集不参与。
        /// 生成代码用 Newtonsoft.Json：那个包的程序集是"自动引用"的插件，
        /// 所以这里不需要（也不应该）写 precompiledReferences —— 写了反而要开 overrideReferences，
        /// 会把 MiniExcel 之类其它插件一起挡在外面。
        /// </summary>
        internal static void EnsureGeneratedAsmdef(string outputFolder, string ns)
        {
            try
            {
                if (string.IsNullOrEmpty(outputFolder)) return;
                EnsureFolder(outputFolder);

                string effectiveNs = string.IsNullOrEmpty(ns) ? "CoffeeBean" : ns;
                string assemblyName = effectiveNs + ".Generated";
                string asmdefPath = Path.Combine(outputFolder, assemblyName + ".asmdef");
                if (File.Exists(asmdefPath)) return; // 已存在不覆盖（避免用户定制被覆盖）

                string json = "{\n" +
                              "  \"name\": \"" + assemblyName + "\",\n" +
                              "  \"rootNamespace\": \"" + effectiveNs + "\",\n" +
                              "  \"references\": [],\n" +
                              "  \"includePlatforms\": [],\n" +
                              "  \"excludePlatforms\": [],\n" +
                              "  \"allowUnsafeCode\": false,\n" +
                              "  \"overrideReferences\": false,\n" +
                              "  \"precompiledReferences\": [],\n" +
                              "  \"autoReferenced\": true,\n" +
                              "  \"defineConstraints\": [],\n" +
                              "  \"versionDefines\": [],\n" +
                              "  \"noEngineReferences\": false\n" +
                              "}\n";
                File.WriteAllText(asmdefPath, json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                // asmdef 生成失败不阻断主流程（只是编译优化），记录警告
                UnityEngine.Debug.LogWarning("[CoffeeBean.Excel] 生成独立 asmdef 失败（不影响生成产物）: " + e.Message);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        }
    }
}
