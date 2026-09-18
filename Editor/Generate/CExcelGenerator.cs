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
        /// <summary>
        /// 代码输出根 = 内嵌包根（相对工程根或绝对路径）。默认 <c>Packages/com.coffeebean.config.generated</c>。
        ///
        /// **为什么是"包"而不是 Assets**：生成的代码要在 Assets 之外仍被 Unity 编译，只有包这一条路
        /// （Unity 只编译 `Assets/` 与 `Packages/` 下的脚本）。内嵌包免改 manifest，可直接入库；
        /// 每张表在其下占一个文件夹：<c>&lt;包&gt;/&lt;表&gt;/Code/</c> 放代码、<c>&lt;包&gt;/&lt;表&gt;/Data/</c> 放数据。
        /// </summary>
        public string CodeFolder = "Packages/com.coffeebean.config.generated";

        /// <summary>
        /// 包名（= package.json 的 name，同时决定 Player 里 StreamingAssets 下的子目录名）。
        /// **必须与 <see cref="CodeFolder"/> 末级目录名一致**：构建钩子按标记文件里的包名挂载、
        /// 运行时按生成时固化的包名查找，不一致就会错位成"运行时找不到配置"。
        /// </summary>
        public string PackageName = "com.coffeebean.config.generated";

        /// <summary>生成类 / Getter 的命名空间（默认 CoffeeBean 根命名空间，`using CoffeeBean;` 即可访问）。</summary>
        public string Namespace = "CoffeeBean";

        /// <summary>类名（默认取表名/sheet 名）。</summary>
        public string ClassName;

        /// <summary>主键列名（默认自动选择第一个可做键的列）。</summary>
        public string PrimaryKey;

        /// <summary>指定 sheet 名（null = 默认 sheet）。</summary>
        public string SheetName;

        /// <summary>是否生成数据文件（默认 true）。</summary>
        public bool GenerateData = true;

        /// <summary>是否生成 C# 数据类 / Getter（默认 true）。</summary>
        public bool GenerateClass = true;

        /// <summary>是否压缩数据（默认 true，Deflate；零新依赖，System.IO.Compression）。</summary>
        public bool CompressData = true;

        /// <summary>
        /// 是否加密数据（默认 true）。**顺序恒为"先压缩再加密"** —— 反过来密文近似随机、压不动。
        ///
        /// **安全边界**：这是混淆级保护（key 硬编码在生成代码里，可被反编译），不能防专业逆向；
        /// 目的是让打包产物里的配置不是明文。真正的安全需要服务器下发 / AssetBundle 加密 / 代码混淆。
        /// </summary>
        public bool EncryptData = true;

        /// <summary>
        /// 严格类型校验（默认 true）：每个单元格都按列声明的类型真解析一遍，解析不了就报错并中止该表。
        /// 为什么默认开：以前 `Level_i` 填 `abc` 会被**安静地写成 0**，这类错数据比编译错误难查得多。
        /// 老表迁移期可临时关掉（只跳过值校验，枚举定义校验仍然生效）。
        /// </summary>
        public bool StrictTypeCheck = true;

        /// <summary>
        /// 数组元素分隔符（默认 <see cref="CExcelCellJson.DefaultArraySeparators"/> = `;` `,` `|` `_` 与中文全角）。
        ///
        /// **为什么默认带 `_`**：真实项目里最常见的写法就是 `13_100`（id_数量）、`0.2_0.8_1`、`10001_10002_10003`，
        /// 甚至字符串数组也这么写。不带 `_` 的话这些表全读不出来（会被当成一个非法元素）。
        ///
        /// **什么时候该去掉 `_`**：字符串数组（`_sa`）的元素本身含下划线时（如 `fire_dragon;ice_wolf`）——
        /// 那就把 `_` 从这个集合里删掉，只用 `;` `,` `|` 分隔。
        /// 枚举数组（`_ea` / `_flagsa`）**永远不按 `_` 拆**（`_` 是枚举"名字_值"语法的组成部分，如 `green_3`）。
        /// </summary>
        public string ArraySeparators = CExcelCellJson.DefaultArraySeparators;

        /// <summary>
        /// 跳过"主键无效"的行（默认 true）。真实配置表里几乎都有说明行 / 图例行 / 草稿块
        /// （典型长相：表头下面又写一段"字段 | 说明"，或者某个列旁边贴一串临时算的数值），
        /// 这些行主键是空的 —— 当数据行会让整张表校验失败。
        ///
        /// 跳过时会出**警告**并列出被跳过的行号，不是悄悄吞掉。
        /// 若跳完一行都不剩，则报错（多半是自动选错了主键列），不生成空表。
        /// </summary>
        public bool SkipRowsWithoutKey = true;
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
    /// 配置表生成器：产物全部落在**内嵌包**（<see cref="CExcelGenerateOptions.CodeFolder"/>，默认
    /// <c>Packages/com.coffeebean.config.generated</c>，**不写进 Assets**），一表一文件夹：
    ///
    /// **普通 sheet（单表）** —— <c>&lt;包&gt;/&lt;表&gt;/</c>：
    ///   Code/表名.cs            强类型数据类（字段注释取自表头说明行）+ 该表用到的枚举定义
    ///   Code/表名Getter.cs      加载器（容器解码 + 懒加载 + 主键/下标/条件查询）
    ///   Data/表名.cbcfg         表数据容器（Deflate 压缩 + XOR 加密 + 校验和）
    ///
    /// **多章节 sheet（sheet 名 "前缀_数字"，如 ChapterConfig_1）** —— <c>&lt;包&gt;/&lt;前缀&gt;/</c>：
    ///   Code/前缀Base.cs                章节基类（全字段 + 共用枚举定义）
    ///   Code/前缀ChapterN.cs            每章节数据子类（: 基类）
    ///   Code/前缀ChapterNGetter.cs      每章节独立加载器
    ///   Code/前缀Getter.cs              聚合加载器（按章节查询 Get(key, chapterId)）
    ///   Data/前缀_N.cbcfg               每章节数据容器
    ///
    /// 包根另有：<c>package.json</c>、标记文件 <c>coffeebean.configgen.json</c>（构建钩子靠它把
    /// 各 <c>&lt;表&gt;/Data</c> 挂进产物 StreamingAssets）、<c>{命名空间}.Generated.asmdef</c>、
    /// <c>Runtime/ConfigTableRuntime.cs</c>（运行时容器解码 + 预加载）。
    /// 数据格式与命名决策见 CHANGELOG 的 0.6.0 —— 起因是 <c>.json</c> 后缀会被 spine-unity 逐个扫描报错。
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
        public const int TemplateVersion = 3;

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
            CExcelTableValidator.Validate(table, effective, issues);
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
                    // 章节族没有可做键的列：仍然生成聚合 Getter（只给 GetChapter/ChapterN），只提示不阻断
                    result.Issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Warning,
                        Row = 0,
                        Column = "-",
                        Message = "章节族 " + group.Key + " 没有可做键的列 → 聚合 Getter 只提供 GetChapter / ChapterN，"
                                  + "不生成按主键查询的接口",
                    });
                }

                try
                {
                    // 聚合 Getter 与章节代码同处章节族目录：<包>/<前缀>/Code/
                    string chapterCodeDir = Path.Combine(options.CodeFolder, group.Key, "Code");
                    EnsureFolder(chapterCodeDir);
                    EnsurePackageSkeleton(options);
                    string getterPath = Path.Combine(chapterCodeDir, group.Key + "Getter.cs");
                    File.WriteAllText(getterPath, WriteChapterGetter(table, group.Key, group.Value, options.Namespace), new UTF8Encoding(false));
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

            // 包名非法就别生成了：UPM 解析失败会让**整个工程打不开**（实测），必须在这里拦住。
            // 章节表还要登记 FailedGroups，否则后面的聚合 Getter 环节仍会写出非法 package.json。
            string packageNameError = ValidatePackageName(options.PackageName);
            if (packageNameError != null)
            {
                CExcelIssue blocking = Error(0, "-",
                    "包名非法: \"" + options.PackageName + "\" —— " + packageNameError +
                    "（非法包名会让 UPM 解析失败、工程打不开，已中止生成）");
                result.Issues.Add(blocking);
                aggregate.Issues.Add(blocking);
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }
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
            CExcelTableValidator.Validate(table, options, issues);
            aggregate.Issues.AddRange(issues);
            if (issues.Exists(i => i.Level == CExcelIssueLevel.Error))
            {
                if (isChapter) context.FailedGroups.Add(frontName);
                return result;
            }

            if (table.PrimaryKey == null)
            {
                // 没有可做键的列 ≠ 不能生成：这类表靠 All / GetByIndex / Find / FindAll 访问。
                // （以前这里直接报错不生成，于是"没有 ID 的表"根本产不出数据访问代码。）
                aggregate.Issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Warning,
                    Row = 0,
                    Column = "-",
                    Message = "表 " + sheetName + " 没有可做键的列（如 *_i / *_l / *_s / *_e）→ 生成的 Getter 只提供 "
                              + "All / GetByIndex / Find / FindAll，不生成按主键查询的接口",
                });
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

            // 一表一文件夹：普通表 = 类名；章节表 = 章节族共用前缀目录（代码本来就共用基类）
            string tableFolder = isChapter ? frontName : className;
            string codeDir = Path.Combine(options.CodeFolder, tableFolder, "Code");
            string dataDir = Path.Combine(options.CodeFolder, tableFolder, "Data");
            string dataName = className;   // 章节表 = sheet 名（ChapterConfig_1），普通表 = 类名
            string dataRelative = tableFolder + "/Data/" + dataName + CExcelDataContainer.Extension;

            try
            {
                EnsureFolder(codeDir);
                EnsurePackageSkeleton(options);

                if (options.GenerateData)
                {
                    // 数据与代码同处内嵌包（Assets 之外）；扩展名刻意不用 .json —— 见 CExcelDataContainer 的说明
                    EnsureFolder(dataDir);
                    string dataPath = Path.Combine(dataDir, dataName + CExcelDataContainer.Extension);
                    string jsonText = WriteJson(table, CExcelCellJson.Separators(options.ArraySeparators));
                    File.WriteAllBytes(dataPath, CExcelDataContainer.Encode(jsonText, options.CompressData, options.EncryptData));
                    result.GeneratedFiles.Add(dataPath);
                }

                if (options.GenerateClass)
                {
                    if (isChapter)
                    {
                        // 基类（同前缀共用一个，覆盖写 —— 各章节内容一致因为枚举用的是并集）+ 章节子类 + 章节 Getter
                        // 命名：<前缀>Base / <前缀>Chapter<N> / <前缀>Chapter<N>Getter
                        // （旧版是 前缀ConfigBase / 前缀_NConfig —— "ConfigConfig" 这类重复读起来很糟）
                        string rowName = frontName + "Chapter" + chapterIndex;
                        string basePath = Path.Combine(codeDir, frontName + "Base.cs");
                        File.WriteAllText(basePath, WriteChapterBaseClass(table, frontName, options.Namespace), new UTF8Encoding(false));
                        if (!result.GeneratedFiles.Contains(basePath)) result.GeneratedFiles.Add(basePath);

                        string subPath = Path.Combine(codeDir, rowName + ".cs");
                        File.WriteAllText(subPath, WriteChapterSubClass(table, frontName, rowName, options.Namespace), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(subPath);

                        string getterPath = Path.Combine(codeDir, rowName + "Getter.cs");
                        File.WriteAllText(getterPath, WriteGetter(table, rowName, rowName, options.Namespace, dataRelative), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(getterPath);
                    }
                    else
                    {
                        string classPath = Path.Combine(codeDir, className + ".cs");
                        File.WriteAllText(classPath, WriteClass(table, className, options.Namespace), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(classPath);

                        string getterPath = Path.Combine(codeDir, className + "Getter.cs");
                        File.WriteAllText(getterPath, WriteGetter(table, className, className, options.Namespace, dataRelative), new UTF8Encoding(false));
                        result.GeneratedFiles.Add(getterPath);
                    }
                }

                result.Success = true;
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
                || !CExcelTypeInfer.IsKeyCandidate(table.Kinds[table.PrimaryKey]))
                table.PrimaryKey = PickPrimaryKey(table);

            if (context.Options.SkipRowsWithoutKey && table.PrimaryKey != null)
                DropRowsWithoutKey(table, context.Options, issues);
            return table;
        }

        /// <summary>
        /// 自动选主键：**优先选"每行都填了合法值"的第一列**（真正的键列通常如此），
        /// 没有这样的列再退回"第一个可做键的列"。
        /// </summary>
        private static string PickPrimaryKey(CExcelTable table)
        {
            string first = null;
            foreach (string column in table.Columns)
            {
                if (!CExcelTypeInfer.IsKeyCandidate(table.Kinds[column])) continue;
                if (first == null) first = column;
                if (ColumnIsUsableKeyEverywhere(table, column)) return column;
            }
            return first;
        }

        private static bool ColumnIsUsableKeyEverywhere(CExcelTable table, string column)
        {
            CExcelFieldKind kind = table.Kinds[column];
            table.Enums.TryGetValue(column, out CExcelEnumDef enumDef);
            bool sawValue = false;
            foreach (Dictionary<string, object> row in table.Rows)
            {
                string text = CExcelValue.ToText(row.TryGetValue(column, out object v) ? v : null).Trim();
                if (text.Length == 0) return false;
                if (CExcelCellJson.Check(text, kind, enumDef) != null) return false;
                sawValue = true;
            }
            return sawValue;
        }

        /// <summary>
        /// 丢掉"主键无效"的行（说明行 / 图例行 / 边上的草稿块）。
        ///
        /// 真实配置表里几乎都有这类行（表头下面又写一段"字段 | 说明"，或者某列旁边贴一串临时算的数），
        /// 它们的主键是空的 —— 当数据行会让整张表校验失败。跳过时出**警告并列出行号**，不是悄悄吞。
        /// 跳完一行都不剩 → 报错（多半是自动选错了主键列），不生成空表。
        /// </summary>
        private static void DropRowsWithoutKey(CExcelTable table, CExcelGenerateOptions options, List<CExcelIssue> issues)
        {
            string key = table.PrimaryKey;
            CExcelFieldKind kind = table.Kinds[key];
            table.Enums.TryGetValue(key, out CExcelEnumDef enumDef);
            char[] separators = CExcelCellJson.Separators(options != null ? options.ArraySeparators : null);

            var kept = new List<Dictionary<string, object>>(table.Rows.Count);
            var skipped = new List<int>();
            for (int r = 0; r < table.Rows.Count; r++)
            {
                Dictionary<string, object> row = table.Rows[r];
                string text = CExcelValue.ToText(row.TryGetValue(key, out object v) ? v : null).Trim();
                bool usable = text.Length > 0 && CExcelCellJson.Check(text, kind, enumDef, separators) == null;
                if (usable) kept.Add(row);
                else skipped.Add(table.HeaderRowIndex + r + 2);
            }

            if (skipped.Count == 0) return;

            if (kept.Count == 0)
            {
                issues.Add(Error(0, key,
                    $"主键列 {key} 在**所有行**上都是空的或非法值 —— 无法确定数据行。请检查表头检测是否选错行，或用选项显式指定主键列。"));
                return;
            }

            table.Rows = kept;
            issues.Add(new CExcelIssue
            {
                Level = CExcelIssueLevel.Warning,
                Row = skipped[0],
                Column = key,
                Message = $"跳过 {skipped.Count} 行（主键 {key} 为空或不是合法值，通常是说明/图例/草稿行）：第 " + DescribeRows(skipped) + " 行。",
            });
        }

        private static string DescribeRows(List<int> rows)
        {
            const int limit = 12;
            var parts = new List<string>();
            for (int i = 0; i < rows.Count && i < limit; i++) parts.Add(rows[i].ToString(CultureInfo.InvariantCulture));
            string text = string.Join(", ", parts);
            return rows.Count > limit ? text + " …(共 " + rows.Count + " 行)" : text;
        }

        // ========== JSON 生成 ==========

        private static string WriteJson(CExcelTable table, char[] arraySeparators)
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
                    sb.Append(CExcelCellJson.Literal(CExcelValue.ToText(value), table.Kinds[column], enumDef, arraySeparators, out _));
                }
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ========== 模板公共 ==========

        private const string HeaderLine = "// Auto-generated by CoffeeBean.Excel. Do not edit.";

        /// <summary>模板版本注释：产物里能直接看出是哪版模板生成的。</summary>
        private static string TemplateLine => $"// Generator template: v{TemplateVersion} (container {CExcelDataContainer.Extension} + package layout)";

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

        /// <summary>
        /// 数据类需要的 using。
        /// `Dictionary<,>` 是唯一一个**没写全名**的类型（其它 System.* / UnityEngine.* 都写全名），
        /// 所以只有表里有字典列时才补 `System.Collections.Generic` ——
        /// 这条是**把生成产物真丢进工程编译**才发现的（少了它 → CS0246）。
        /// </summary>
        private static void AppendUsings(StringBuilder sb, CExcelTable table)
        {
            sb.AppendLine("using System;");
            foreach (string column in table.Columns)
            {
                if (!table.Kinds.TryGetValue(column, out CExcelFieldKind kind)) continue;
                CExcelFieldKind element = CExcelTypeInfer.IsArray(kind) ? CExcelTypeInfer.ElementKind(kind) : kind;
                if (element == CExcelFieldKind.Dictionary)
                {
                    sb.AppendLine("using System.Collections.Generic;");
                    return;
                }
            }
        }
        // ========== 普通单表：数据类 ==========

        private static string WriteClass(CExcelTable table, string className, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Source sheet: " + table.SheetName);
            AppendUsings(sb, table);
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
            AppendUsings(sb, table);
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + frontName + " 章节基类（全字段 + 共用枚举定义，自动生成）。</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    public class " + frontName + "Base");
            sb.AppendLine("    {");
            foreach (string column in table.Columns)
                AppendField(sb, table, column, "        ");
            sb.AppendLine("    }");
            AppendEnums(sb, table, "    ");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string WriteChapterSubClass(CExcelTable table, string frontName, string rowName, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Source sheet: " + table.SheetName);
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + table.SheetName + " 章节数据（自动生成，继承章节基类）。</summary>");
            sb.AppendLine("    [Serializable]");
            sb.AppendLine("    public sealed class " + rowName + " : " + frontName + "Base");
            sb.AppendLine("    {");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== Getter（普通表 / 章节独立） ==========

        /// <param name="className">Getter 类名（普通表 = 表名；章节 = sheet 名）。</param>
        /// <param name="dataType">数据类名（普通表 = 表名；章节 = 子类名，如 ChapterConfig_1Config）。</param>
        /// <param name="dataRelativePath">数据文件相对包根的路径（如 "Table/Data/Table.cbcfg"）。</param>
        private static string WriteGetter(CExcelTable table, string className, string dataType, string ns, string dataRelativePath)
        {
            // 没有可做键的列（PrimaryKey == null）时**照样生成**，只是不产出按主键查询的接口 ——
            // 靠 All / GetByIndex / Find / FindAll 访问。以前这里直接报错不生成，"没有 ID 的表"就完全没有访问入口。
            bool hasKey = !string.IsNullOrEmpty(table.PrimaryKey);
            string keyType = hasKey ? table.CSharpTypeOf(table.PrimaryKey) : null;
            string keyField = hasKey ? CExcelTypeInfer.ToFieldName(table.PrimaryKey) : null;

            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Source sheet: " + table.SheetName + "  Primary key: " + (hasKey ? table.PrimaryKey : "(无：本表无可做键的列)"));
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Newtonsoft.Json;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + className + " config loader (auto-generated).</summary>");
            sb.AppendLine("    public static class " + className + "Getter");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>数据文件相对包根的路径（生成时固化）。</summary>");
            sb.AppendLine("        private const string DataPath = \"" + dataRelativePath + "\";");
            sb.AppendLine();
            sb.AppendLine("        private static List<" + dataType + "> _all;");
            if (hasKey)
            {
                sb.AppendLine("        private static Dictionary<" + keyType + ", " + dataType + "> _byKey;");
                sb.AppendLine("        private static Dictionary<" + keyType + ", List<" + dataType + ">> _byKeyAll;");
                sb.AppendLine("        private static readonly List<" + dataType + "> Empty = new List<" + dataType + ">();");
            }
            else
            {
                sb.AppendLine("        // 本表没有可做键的列 → 不生成 Get / TryGet / Contains / GetAll；");
                sb.AppendLine("        // 请用 All / GetByIndex(i) / Find(pred) / FindAll(pred) 访问。");
            }
            sb.AppendLine();
            sb.AppendLine("        /// <summary>是否已加载（预加载过、或访问过任何接口后为 true）。</summary>");
            sb.AppendLine("        public static bool IsLoaded => _all != null;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>行数（会触发加载）。</summary>");
            sb.AppendLine("        public static int Count => All.Count;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>全部行（懒加载；只读，避免误改缓存）。</summary>");
            sb.AppendLine("        public static IReadOnlyList<" + dataType + "> All => _all ??= Load();");
            sb.AppendLine();
            if (hasKey)
            {
                sb.AppendLine("        /// <summary>按主键取一行；同一个键有多行时给**第一行**（要全部用 GetAll）；找不到返回 null。</summary>");
                sb.AppendLine("        public static " + dataType + " Get(" + keyType + " key)");
                sb.AppendLine("        {");
                sb.AppendLine("            _byKey ??= BuildIndex();");
                sb.AppendLine("            return _byKey.TryGetValue(key, out " + dataType + " item) ? item : null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>按主键取一行；找不到返回 false（不抛异常）。</summary>");
                sb.AppendLine("        public static bool TryGet(" + keyType + " key, out " + dataType + " value)");
                sb.AppendLine("        {");
                sb.AppendLine("            _byKey ??= BuildIndex();");
                sb.AppendLine("            return _byKey.TryGetValue(key, out value);");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>取该键的**全部**行（一个键对应多行、或想稳一点时用它）；没有则返回空列表。</summary>");
                sb.AppendLine("        public static IReadOnlyList<" + dataType + "> GetAll(" + keyType + " key)");
                sb.AppendLine("        {");
                sb.AppendLine("            _byKeyAll ??= BuildIndexAll();");
                sb.AppendLine("            return _byKeyAll.TryGetValue(key, out List<" + dataType + "> rows) ? rows : Empty;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>是否存在该主键。</summary>");
                sb.AppendLine("        public static bool Contains(" + keyType + " key) => Get(key) != null;");
                sb.AppendLine();
            }
            sb.AppendLine("        /// <summary>按下标取一行（顺序 = 数据文件里的顺序）；越界返回 null。</summary>");
            sb.AppendLine("        public static " + dataType + " GetByIndex(int index)");
            sb.AppendLine("        {");
            sb.AppendLine("            IReadOnlyList<" + dataType + "> rows = All;");
            sb.AppendLine("            return index >= 0 && index < rows.Count ? rows[index] : null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>取第一个满足条件的行；没有返回 null。</summary>");
            sb.AppendLine("        public static " + dataType + " Find(Func<" + dataType + ", bool> predicate)");
            sb.AppendLine("        {");
            sb.AppendLine("            foreach (" + dataType + " item in All) if (predicate(item)) return item;");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>取所有满足条件的行（永不为 null）。</summary>");
            sb.AppendLine("        public static List<" + dataType + "> FindAll(Func<" + dataType + ", bool> predicate)");
            sb.AppendLine("        {");
            sb.AppendLine("            var hits = new List<" + dataType + ">();");
            sb.AppendLine("            foreach (" + dataType + " item in All) if (predicate(item)) hits.Add(item);");
            sb.AppendLine("            return hits;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>丢弃缓存；下次访问重新读数据文件（热更后用）。</summary>");
            sb.AppendLine("        public static void Reload()");
            sb.AppendLine("        {");
            sb.AppendLine("            _all = null;");
            if (hasKey)
            {
                sb.AppendLine("            _byKey = null;");
                sb.AppendLine("            _byKeyAll = null;");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>直接灌入容器字节（预加载/热更/测试用）。</summary>");
            sb.AppendLine("        public static void LoadFrom(byte[] container) => ApplyContainer(container);");
            sb.AppendLine();
            sb.AppendLine("        private static List<" + dataType + "> Load() => ApplyContainer(ConfigTableRuntime.ReadData(DataPath));");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>把容器字节解出来灌进缓存（ConfigTableRuntime.PreloadAll 调用）。</summary>");
            sb.AppendLine("        internal static List<" + dataType + "> ApplyContainer(byte[] container)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (container == null) { _all = new List<" + dataType + ">(); return _all; }");
            sb.AppendLine("            string error;");
            sb.AppendLine("            string json = ConfigTableRuntime.Decode(container, out error);");
            sb.AppendLine("            if (json == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogError(\"[" + className + "] 配置解码失败: \" + error + \" (\" + DataPath + \")\");");
            sb.AppendLine("                _all = new List<" + dataType + ">();");
            sb.AppendLine("                return _all;");
            sb.AppendLine("            }");
            sb.AppendLine("            var file = JsonConvert.DeserializeObject<DataFile>(json);");
            sb.AppendLine("            _all = file != null && file.data != null ? file.data : new List<" + dataType + ">();");
            if (hasKey)
            {
                sb.AppendLine("            _byKey = null;");
                sb.AppendLine("            _byKeyAll = null;");
            }
            sb.AppendLine("            return _all;");
            sb.AppendLine("        }");
            sb.AppendLine();
            if (hasKey)
            {
                sb.AppendLine("        /// <summary>主键 → 首行（同一个键重复出现时保留第一次出现的那行）。</summary>");
                sb.AppendLine("        private static Dictionary<" + keyType + ", " + dataType + "> BuildIndex()");
                sb.AppendLine("        {");
                sb.AppendLine("            var index = new Dictionary<" + keyType + ", " + dataType + ">();");
                sb.AppendLine("            foreach (" + dataType + " item in All) if (!index.ContainsKey(item." + keyField + ")) index[item." + keyField + "] = item;");
                sb.AppendLine("            return index;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>主键 → 该键的全部行（懒建：只有调用 GetAll 时才会构建）。</summary>");
                sb.AppendLine("        private static Dictionary<" + keyType + ", List<" + dataType + ">> BuildIndexAll()");
                sb.AppendLine("        {");
                sb.AppendLine("            var map = new Dictionary<" + keyType + ", List<" + dataType + ">>();");
                sb.AppendLine("            foreach (" + dataType + " item in All)");
                sb.AppendLine("            {");
                sb.AppendLine("                if (!map.TryGetValue(item." + keyField + ", out List<" + dataType + "> rows)) { rows = new List<" + dataType + ">(); map[item." + keyField + "] = rows; }");
                sb.AppendLine("                rows.Add(item);");
                sb.AppendLine("            }");
                sb.AppendLine("            return map;");
                sb.AppendLine("        }");
            }
            sb.AppendLine();
            sb.AppendLine("        private sealed class Registration : IConfigTable");
            sb.AppendLine("        {");
            sb.AppendLine("            public string DataRelativePath { get { return DataPath; } }");
            sb.AppendLine("            public void Apply(byte[] container) { ApplyContainer(container); }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("        private static void __Register() { ConfigTableRuntime.Register(new Registration()); }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>数据文件外层结构 { \"data\": [...] }。</summary>");
            sb.AppendLine("        [System.Serializable] public sealed class DataFile { public List<" + dataType + "> data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // ========== 多章节：聚合 Getter ==========

        private static string WriteChapterGetter(CExcelTable table, string frontName, List<int> chapters, string ns)
        {
            // 同 WriteGetter：没有可做键的列时不生成按主键查询的接口，但仍生成（按章节/下标访问）
            bool hasKey = !string.IsNullOrEmpty(table.PrimaryKey);
            string keyType = hasKey ? table.CSharpTypeOf(table.PrimaryKey) : null;
            string keyField = hasKey ? CExcelTypeInfer.ToFieldName(table.PrimaryKey) : null;
            string baseClass = frontName + "Base";
            string chaptersArray = string.Join(", ", chapters.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            string dataPrefix = frontName + "/Data/" + frontName + "_";

            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Multi-chapter getter: " + frontName + " (chapters: " + chaptersArray + ")");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Newtonsoft.Json;");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine();
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
            sb.AppendLine("    /// <summary>" + frontName + " 多章节加载器（自动生成）。</summary>");
            sb.AppendLine("    public static class " + frontName + "Getter");
            sb.AppendLine("    {");
            sb.AppendLine("        /// <summary>数据文件相对包根的路径前缀（后面补章节号 + 扩展名）。</summary>");
            sb.AppendLine("        private const string DataPathPrefix = \"" + dataPrefix + "\";");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>可用章节号（升序）。</summary>");
            sb.AppendLine("        public static readonly int[] Chapters = new[] { " + chaptersArray + " };");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>章节数量。</summary>");
            sb.AppendLine("        public static int ChapterCount => Chapters.Length;");
            sb.AppendLine();

            // 每章节：强类型只读访问器
            foreach (int chapter in chapters)
            {
                string rowType = frontName + "Chapter" + chapter;
                sb.AppendLine("        private static List<" + rowType + "> _c" + chapter + ";");
                sb.AppendLine("        /// <summary>第 " + chapter + " 章（强类型，懒加载）。</summary>");
                sb.AppendLine("        public static IReadOnlyList<" + rowType + "> Chapter" + chapter + " => _c" + chapter + " ??= LoadChapter<" + rowType + ">(" + chapter + ");");
                sb.AppendLine();
            }

            sb.AppendLine("        private static readonly " + baseClass + "[] None = new " + baseClass + "[0];");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>是否存在该章节（只有存在对应数据文件的章节才会生成进来）。</summary>");
            sb.AppendLine("        public static bool HasChapter(int chapterId)");
            sb.AppendLine("        {");
            sb.AppendLine("            for (int i = 0; i < Chapters.Length; i++) if (Chapters[i] == chapterId) return true;");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>取某章节的全部行（基类视角）；章节不存在返回空列表，不抛异常。</summary>");
            sb.AppendLine("        public static IReadOnlyList<" + baseClass + "> GetChapter(int chapterId)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (chapterId)");
            sb.AppendLine("            {");
            foreach (int chapter in chapters)
            {
                sb.AppendLine("                case " + chapter + ": return Chapter" + chapter + ";");
            }
            sb.AppendLine("                default: return None;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            if (hasKey)
            {
                sb.AppendLine("        /// <summary>在指定章节里按主键取一行；同一个键有多行时给**第一行**（要全部用 GetAll）；找不到返回 null。</summary>");
                sb.AppendLine("        public static " + baseClass + " Get(" + keyType + " key, int chapterId)");
                sb.AppendLine("        {");
                sb.AppendLine("            switch (chapterId)");
                sb.AppendLine("            {");
                foreach (int chapter in chapters)
                {
                    sb.AppendLine("                case " + chapter + ": return Find(Chapter" + chapter + ", key);");
                }
                sb.AppendLine("                default: return null;");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>取指定章节里该键的**全部**行（一个键对应多行、或想稳一点时用它）；没有则返回空列表。</summary>");
                sb.AppendLine("        public static IReadOnlyList<" + baseClass + "> GetAll(" + keyType + " key, int chapterId)");
                sb.AppendLine("        {");
                sb.AppendLine("            switch (chapterId)");
                sb.AppendLine("            {");
                foreach (int chapter in chapters)
                {
                    sb.AppendLine("                case " + chapter + ": return Collect(Chapter" + chapter + ", key);");
                }
                sb.AppendLine("                default: return None;");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>在指定章节里按主键取一行；找不到返回 false。</summary>");
                sb.AppendLine("        public static bool TryGet(" + keyType + " key, int chapterId, out " + baseClass + " value)");
                sb.AppendLine("        {");
                sb.AppendLine("            value = Get(key, chapterId);");
                sb.AppendLine("            return value != null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>指定章节里是否存在该主键。</summary>");
                sb.AppendLine("        public static bool Contains(" + keyType + " key, int chapterId) => Get(key, chapterId) != null;");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        // 本表没有可做键的列 → 不生成 Get / GetAll / TryGet / Contains；");
                sb.AppendLine("        // 请用 GetChapter(chapterId) / ChapterN / HasChapter 访问。");
                sb.AppendLine();
            }
            sb.AppendLine("        /// <summary>丢弃所有章节缓存；下次访问重新读数据文件（热更后用）。</summary>");
            sb.AppendLine("        public static void Reload()");
            sb.AppendLine("        {");
            foreach (int chapter in chapters)
            {
                sb.AppendLine("            _c" + chapter + " = null;");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>直接灌入某章节的容器字节（预加载/热更/测试用）。</summary>");
            sb.AppendLine("        public static void LoadFrom(int chapterId, byte[] container) => ApplyChapter(chapterId, container);");
            sb.AppendLine();
            sb.AppendLine("        private static List<T> LoadChapter<T>(int chapterId) where T : " + baseClass);
            sb.AppendLine("            => ApplyChapter<T>(chapterId, ConfigTableRuntime.ReadData(DataPathPrefix + chapterId + ConfigTableRuntime.DataExtension));");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>把某章节的容器字节解出来灌进缓存（ConfigTableRuntime.PreloadAll 调用）。</summary>");
            sb.AppendLine("        internal static void ApplyChapter(int chapterId, byte[] container)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (chapterId)");
            sb.AppendLine("            {");
            foreach (int chapter in chapters)
            {
                string rowType = frontName + "Chapter" + chapter;
                sb.AppendLine("                case " + chapter + ": _c" + chapter + " = ApplyChapter<" + rowType + ">(" + chapter + ", container); break;");
            }
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        private static List<T> ApplyChapter<T>(int chapterId, byte[] container) where T : " + baseClass);
            sb.AppendLine("        {");
            sb.AppendLine("            if (container == null) return new List<T>();");
            sb.AppendLine("            string error;");
            sb.AppendLine("            string json = ConfigTableRuntime.Decode(container, out error);");
            sb.AppendLine("            if (json == null)");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogError(\"[" + frontName + "] 章节 \" + chapterId + \" 配置解码失败: \" + error);");
            sb.AppendLine("                return new List<T>();");
            sb.AppendLine("            }");
            sb.AppendLine("            var file = JsonConvert.DeserializeObject<DataFile<T>>(json);");
            sb.AppendLine("            return file != null && file.data != null ? file.data : new List<T>();");
            sb.AppendLine("        }");
            sb.AppendLine();
            if (hasKey)
            {
                sb.AppendLine("        /// <summary>章节内按主键取首行。</summary>");
                sb.AppendLine("        private static T Find<T>(IReadOnlyList<T> rows, " + keyType + " key) where T : " + baseClass);
                sb.AppendLine("        {");
                sb.AppendLine("            for (int i = 0; i < rows.Count; i++) if (rows[i]." + keyField + " == key) return rows[i];");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>章节内按主键取全部行。</summary>");
                sb.AppendLine("        private static IReadOnlyList<T> Collect<T>(IReadOnlyList<T> rows, " + keyType + " key) where T : " + baseClass);
                sb.AppendLine("        {");
                sb.AppendLine("            var hits = new List<T>();");
                sb.AppendLine("            for (int i = 0; i < rows.Count; i++) if (rows[i]." + keyField + " == key) hits.Add(rows[i]);");
                sb.AppendLine("            return hits;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("        private sealed class Registration : IConfigTable");
            sb.AppendLine("        {");
            sb.AppendLine("            private readonly int _chapter;");
            sb.AppendLine("            public Registration(int chapter) { _chapter = chapter; }");
            sb.AppendLine("            public string DataRelativePath { get { return DataPathPrefix + _chapter + ConfigTableRuntime.DataExtension; } }");
            sb.AppendLine("            public void Apply(byte[] container) { ApplyChapter(_chapter, container); }");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("        private static void __Register()");
            sb.AppendLine("        {");
            sb.AppendLine("            foreach (int chapter in Chapters) ConfigTableRuntime.Register(new Registration(chapter));");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>数据文件外层结构 { \"data\": [...] }。</summary>");
            sb.AppendLine("        [System.Serializable] public sealed class DataFile<T> { public List<T> data; }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>
        /// 校验 UPM 包名是否合法；合法返回 null，否则返回原因。
        ///
        /// **为什么必须校验**：非法包名（例如以下划线开头）会让 UPM 在解析阶段直接失败并阻断启动 ——
        /// 实测报 <c>Folder [.../Packages] contains invalid packages: Package name 'x' is invalid</c>，
        /// **整个 Unity 工程都打不开**。宁可生成时报错，也不要在工程里留下一个打不开的包。
        ///
        /// 规则：以小写字母开头，只含 <c>a-z0-9._-</c>，不以 <c>.</c>/<c>-</c>/<c>_</c> 结尾，不含连续 <c>.</c>。
        /// </summary>
        internal static string ValidatePackageName(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return "包名为空";

            char first = packageName[0];
            if (first < 'a' || first > 'z') return "必须以小写字母开头（不能是数字、下划线或大写）";

            for (int i = 0; i < packageName.Length; i++)
            {
                char c = packageName[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '-' || c == '_';
                if (!allowed) return "只允许小写字母、数字、'.'、'-'、'_'（非法字符: '" + c + "'）";
            }

            char last = packageName[packageName.Length - 1];
            if (last == '.' || last == '-' || last == '_') return "不能以 '.'、'-'、'_' 结尾";
            if (packageName.Contains("..")) return "不能包含连续的 '.'";
            return null;
        }

        /// <summary>
        /// 确保内嵌包骨架存在：package.json（幂等）、根 asmdef（幂等）、标记文件（覆盖）、运行时支撑代码（覆盖）。
        ///
        /// **每次生成都调用**（幂等且轻量）：标记文件与运行时模板必须跟着生成器版本走，
        /// 否则会出现"数据挂不进产物""运行时解不开新容器"这类只在打包/真机上暴露的问题。
        /// </summary>
        internal static void EnsurePackageSkeleton(CExcelGenerateOptions options)
        {
            try
            {
                if (string.IsNullOrEmpty(options.CodeFolder)) return;
                string ns = string.IsNullOrEmpty(options.Namespace) ? "CoffeeBean" : options.Namespace;
                EnsureFolder(options.CodeFolder);

                // 程序集隔离：改配置表只重编译生成程序集，业务程序集不参与
                EnsureGeneratedAsmdef(options.CodeFolder, ns);

                string packageJson = Path.Combine(options.CodeFolder, "package.json");
                if (!File.Exists(packageJson))
                    File.WriteAllText(packageJson, CExcelRuntimeTemplate.PackageJson(options.PackageName, ns), new UTF8Encoding(false));

                // 标记文件：构建钩子靠它发现数据目录（内容随选项走，覆盖写）
                File.WriteAllText(
                    Path.Combine(options.CodeFolder, CExcelRuntimeTemplate.MarkerFileName),
                    CExcelRuntimeTemplate.MarkerJson(options.PackageName, ns),
                    new UTF8Encoding(false));

                // 运行时支撑代码：属于容器格式的一部分，必须覆盖写（模板升级要跟着走）
                string runtimeDir = Path.Combine(options.CodeFolder, "Runtime");
                EnsureFolder(runtimeDir);
                File.WriteAllText(
                    Path.Combine(runtimeDir, CExcelRuntimeTemplate.RuntimeFileName),
                    CExcelRuntimeTemplate.RuntimeSource(ns, options.PackageName, CExcelCrypto.KeySeed),
                    new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                // 骨架失败不阻断生成，但必须响：缺 package.json 的 Packages/ 子目录会让 Unity 报包错误
                UnityEngine.Debug.LogWarning("[CoffeeBean.Excel] 生成包骨架失败: " + e.Message);
            }
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
