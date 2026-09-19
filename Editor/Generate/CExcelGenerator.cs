using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CoffeeBean
{
    /// <summary>生成代码的 API 风格。</summary>
    public enum CExcelApiStyle
    {
        /// <summary>
        /// 项目既有风格（默认）：<c>&lt;T&gt;_DataGetter</c> / <c>&lt;T&gt;_PropertyBase</c> / <c>&lt;T&gt;_DataBase</c>，
        /// 成员为 <c>GetData / GetDataByID / GetDataNullID / GetDataByIndex / GetDataNullIndexNull / GetArray / GetArrayLenth / GetXxxProptyList</c>，
        /// 类放在**全局命名空间**（业务代码不用加 using）。
        ///
        /// **为什么它是默认**：真实工程里已有上百处调用与 55 个 <c>*_DataGetter.cs</c>，
        /// 只有完全对齐这套名字，生成产物才能**直接替换**老代码、业务代码一行不改。
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// 本模块早期风格：<c>&lt;T&gt;Getter</c> / <c>&lt;T&gt;</c>，成员为 <c>Get / GetAll / GetByIndex / All / Find / FindAll</c>，
        /// 放在 <see cref="CExcelGenerateOptions.Namespace"/> 下。键重复时 <c>Get</c> 给首行、<c>GetAll</c> 给全部。
        /// </summary>
        Modern = 1,
    }

    /// <summary>生成选项。</summary>
    public sealed class CExcelGenerateOptions
    {
        /// <summary>
        /// 生成代码的 API 风格（默认 <see cref="CExcelApiStyle.Legacy"/> = 与项目既有 <c>*_DataGetter</c> 一致）。
        /// </summary>
        public CExcelApiStyle ApiStyle = CExcelApiStyle.Legacy;

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

        /// <summary>
        /// 列名别名（规范列名 → 该列在表头的写法）。生成时透传给读取器。
        /// 另外：把**多个表头**都映射到同一个规范列名，就等于声明"同名列横排 = 数组"（每列一个元素）。
        /// </summary>
        public Dictionary<string, string[]> ColumnAliases;

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
                    bool legacy = options.ApiStyle == CExcelApiStyle.Legacy;
                    string getterPath = Path.Combine(chapterCodeDir, group.Key + (legacy ? "_DataGetter.cs" : "Getter.cs"));
                    string getterText = legacy
                        ? WriteLegacyChapterFamily(table, group.Key, group.Value, options.Namespace)
                        : WriteChapterGetter(table, group.Key, group.Value, options.Namespace);
                    File.WriteAllText(getterPath, getterText, new UTF8Encoding(false));
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

            // 竖排"键值对"表（常量表那类）：表头行只有 1 个带类型后缀的列名，同行其余单元格是值和说明
            // （如 `TestInt_i | 300 | 测试整型`）。这不是常规配置表，硬按常规表生成只会得到一堆
            // "int 列填的值不合法"的**假错误**；这类表在本项目里由专用生成器（AppConstGenerator）维护。
            int typedColumns = 0;
            foreach (string column in read.Columns) if (CExcelTypeInfer.IsSuffixed(column)) typedColumns++;
            if (typedColumns < 2)
            {
                aggregate.Issues.Add(new CExcelIssue
                {
                    Level = CExcelIssueLevel.Warning,
                    Row = read.HeaderRowIndex + 1,
                    Column = "-",
                    Message = "表 " + sheetName + " 只检测到 " + typedColumns + " 个带类型后缀的列名 → 判定为竖排（键值对）表，"
                              + "不做常规表生成（请用专用生成器维护这类表）",
                });
                return result;
            }

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
                    string jsonText = WriteJson(table, CExcelCellJson.Separators(options.ArraySeparators),
                        options.ApiStyle == CExcelApiStyle.Legacy);
                    File.WriteAllBytes(dataPath, CExcelDataContainer.Encode(jsonText, options.CompressData, options.EncryptData));
                    result.GeneratedFiles.Add(dataPath);
                }

                if (options.GenerateClass)
                {
                    if (options.ApiStyle == CExcelApiStyle.Legacy)
                    {
                        // Legacy 风格：普通表一个文件（<T>_DataGetter.cs 内含 Getter + PropertyBase + DataBase）；
                        // 章节表的聚合文件在章节循环里写，这里只补每章节的 <前缀>_<N>_Data 空壳子类。
                        if (isChapter)
                        {
                            string chapterDataName = frontName + "_" + chapterIndex + "_Data";
                            string chapterDataPath = Path.Combine(codeDir, chapterDataName + ".cs");
                            File.WriteAllText(chapterDataPath,
                                WriteLegacyDataSubClass(chapterDataName, frontName + "_DataBase", sheetName, options.Namespace),
                                new UTF8Encoding(false));
                            result.GeneratedFiles.Add(chapterDataPath);
                        }
                        else
                        {
                            string legacyPath = Path.Combine(codeDir, className + "_DataGetter.cs");
                            File.WriteAllText(legacyPath, WriteLegacyGetter(table, className, options.Namespace, dataRelative), new UTF8Encoding(false));
                            result.GeneratedFiles.Add(legacyPath);

                            // <表>_Data.cs：数据子类空壳（与项目一致，Getter 里的静态缓存就是它）
                            string dataClassPath = Path.Combine(codeDir, className + "_Data.cs");
                            File.WriteAllText(dataClassPath,
                                WriteLegacyDataSubClass(className + "_Data", className + "_DataBase", sheetName, options.Namespace),
                                new UTF8Encoding(false));
                            result.GeneratedFiles.Add(dataClassPath);
                        }
                    }
                    else if (isChapter)
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
                CExcelReadResult read = CExcelReader.Read(path, new CExcelReadOptions { SheetName = sheet, ColumnAliases = Options.ColumnAliases });
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

                // 同名列横排但类型不是数组：只能取第一列 —— 说清楚，别让数据静默少了
                if (!CExcelTypeInfer.IsArray(kind)
                    && read.ColumnLetters.TryGetValue(column, out List<string> columnLetters) && columnLetters.Count > 1)
                {
                    issues.Add(new CExcelIssue
                    {
                        Level = CExcelIssueLevel.Warning,
                        Row = read.HeaderRowIndex + 1,
                        Column = column,
                        Message = "列 " + column + " 在表里横排了 " + columnLetters.Count + " 次，但它的类型不是数组 → 只取第一列。"
                                  + "要用「同名列 = 数组」，请把列名后缀改成数组类型（如 _ia / _sa）",
                    });
                }

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
        /// 自动选主键：按 <see cref="ScoreKeyColumn"/> 给每个"可做键"的列打分，取最高分。
        ///
        /// **为什么不是"第一个每行都有值的列"**（旧规则）：真实表尾部常有图例/草稿行，
        /// ID 列空着而"备注(Bz)"列写着字 —— 旧规则会把备注列选成主键。实测把
        /// <c>BuildingConfig</c> 的主键选成了 <c>Bz_s</c>、<c>ChapterConfig</c> 选成了 <c>ChapterIndex_s</c>，
        /// 而项目既有代码用的都是 <c>ID</c>：主键不一致 = <c>GetDataBySameID</c> 这类接口**静默给错数据**。
        /// </summary>
        private static string PickPrimaryKey(CExcelTable table)
        {
            string best = null;
            int bestScore = int.MinValue;
            foreach (string column in table.Columns)
            {
                if (!CExcelTypeInfer.IsKeyCandidate(table.Kinds[column])) continue;
                if (IsRemarkLikeColumn(column)) continue;   // 备注/说明列永远不当主键（见方法注释）
                int score = ScoreKeyColumn(column, table.Kinds[column], ColumnIsUsableKeyEverywhere(table, column));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = column;
                }
            }
            return best;
        }

        /// <summary>
        /// 是不是"备注/说明"这类列（`Bz` / `Bz2` / `BZ` / `Des` / `Desc` / `Note` / `Remark` / `备注` / `说明`）。
        ///
        /// **为什么必须排除**：这类列常是表里唯一"每行都有值"的字符串列，自动选主键会选中它；而备注为空的行
        /// 会被当"说明/图例行"跳过 —— 实测 `PZB_CorrectionMSPD`（老数据 10 行，我们只剩 1 行）、
        /// `PZB_CorrectionRNG`（9 → 1）就是这么丢数据的。项目既有生成器对这两张表**不设主键**，
        /// 排除后我们与它一致：无键 + 全行保留。
        /// </summary>
        private static bool IsRemarkLikeColumn(string column)
        {
            string name = KeyNamePart(column);
            string[] remarks = { "BZ", "Bz", "Bz1", "Bz2", "Bz3", "bz", "Des", "Desc", "Note", "Remark", "Explain", "备注", "说明", "注释" };
            foreach (string remark in remarks)
            {
                if (string.Equals(name, remark, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 主键列打分（越高越像主键）：
        /// ① 名字就是 ID/Id/id（工程里第一主键列几乎都叫这个）→ +100；
        /// ② 名字以 ID/Id 开头（ID_i、Id_s…）→ +60；
        /// ③ 非字符串类型（int/long/枚举）→ +2，字符串 +0；
        /// ④ 每一行都有合法值 → +30。
        ///
        /// 权重为什么这么排：ID 命名是**强约定**（+100/+60 保证它压过"另一列每行都有值"的 +30），
        /// 这样 <c>BuildingConfig</c> 的主键才是 <c>ID_i</c> 而不是被尾部图例行挤掉的 <c>Bz_s</c>；
        /// 而表里压根没有 ID 列时，+30 又能让"每行都有值"的列胜过一个一行空一行的数值列。
        /// </summary>
        private static int ScoreKeyColumn(string column, CExcelFieldKind kind, bool usableEverywhere)
        {
            string name = KeyNamePart(column);
            int score = 0;
            if (string.Equals(name, "ID", StringComparison.OrdinalIgnoreCase)) score += 100;
            else if (name.StartsWith("ID", StringComparison.OrdinalIgnoreCase)) score += 60;
            if (kind != CExcelFieldKind.String) score += 2;
            if (usableEverywhere) score += 30;
            return score;
        }

        /// <summary>列名去掉类型后缀后的名字部分（<c>ID_i</c> → <c>ID</c>、<c>State_e:MyEnum</c> → <c>State</c>）。</summary>
        private static string KeyNamePart(string column)
        {
            string name = CExcelTypeInfer.SuffixHead(column) ?? column;
            int underscore = name.LastIndexOf('_');
            return underscore > 0 ? name.Substring(0, underscore) : name;
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

        private static string WriteJson(CExcelTable table, char[] arraySeparators, bool legacy = false)
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
                    // 键名与生成字段同名（Legacy = 原样列名）：Newtonsoft 匹配虽不区分大小写，
                    // 但两边一致才好在排查时直接对照 JSON 与字段
                    sb.Append(CExcelCellJson.Quote(legacy
                        ? CExcelTypeInfer.ToLegacyFieldName(column)
                        : CExcelTypeInfer.ToFieldName(column))).Append(':');
                    object value = row.TryGetValue(column, out object v) ? v : null;
                    table.Enums.TryGetValue(column, out CExcelEnumDef enumDef);
                    // 同名列横排 = 多元素数组（每列一个元素）；标量类型却横排多列 → 只取第一列（另有警告）
                    if (value is List<string> cells)
                    {
                        if (CExcelTypeInfer.IsArray(table.Kinds[column]))
                            sb.Append(CExcelCellJson.LiteralFromCells(cells, table.Kinds[column], enumDef, arraySeparators, out _));
                        else
                            sb.Append(CExcelCellJson.Literal(cells.Count > 0 ? cells[0] : string.Empty, table.Kinds[column], enumDef, arraySeparators, out _));
                        continue;
                    }
                    sb.Append(CExcelCellJson.Literal(CExcelValue.ToText(value), table.Kinds[column], enumDef, arraySeparators, out _, legacy));
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

        /// <summary>写字段声明（含注释）。<paramref name="legacy"/> = Legacy 风格（字段名原样，不做 PascalCase）。</summary>
        private static void AppendField(StringBuilder sb, CExcelTable table, string column, string indent, bool legacy = false)
        {
            string type = table.CSharpTypeOf(column);
            string field = legacy ? CExcelTypeInfer.ToLegacyFieldName(column) : CExcelTypeInfer.ToFieldName(column);
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

        // ========== Legacy 风格：与项目既有 <T>_DataGetter 同名同形 ==========
        //
        // 为什么单独一套：真实工程里已经有 55 个 *_DataGetter.cs、上百处调用点（GetDataByID/GetArray/
        // GetDataByIndex/GetArrayLenth/GetDataNullID/GetDataBySameID/...）。要让生成产物**直接替换**它们、
        // 业务代码一行不改，就必须连类名、文件名、成员名、日志文案都对齐 —— 那套名字是项目的既成接口，
        // 不能靠"更现代"去说服 139 个调用点改代码。
        //
        // 与我们 Modern 风格的差异：
        //   1. 类名 <T>_DataGetter / <T>_PropertyBase / <T>_DataBase（+ 章节表的 <T>_<N>_Data 子类）；
        //   2. 查询成员名 GetDataByID / GetDataNullID / GetDataByIndex / GetDataNullIndexNull /
        //      GetArray / GetArrayLenth（原样保留项目里的拼写，含 Lenth 这个笔误）、Get<字段>ProptyList；
        //   3. 章节表每个成员带 `int chapterID = -1`，-1 = 当前章节；
        //   4. 类不放在命名空间里（与项目一致，业务代码无需 using）。
        //
        // 保留的项目语义（照抄，别"优化"）：
        //   GetDataByID 找不到 → LogError + 给**首行**（id<=0）或**末行**；GetDataNullID 找不到 → LogError + null；
        //   GetDataByIndex 越界 → LogError + 给首/末行；GetDataNullIndexNull 越界 → LogError + null；
        //   章节没配置 → LogWarning + 给最后一章数据。空表额外加了保护（老代码直接 DataArray[0] 会越界崩）。

        private static void AppendLegacyFileHeader(StringBuilder sb, CExcelTable table, string ns)
        {
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Source sheet: " + table.SheetName);
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using UnityEngine;");
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("using " + ns + ";");
            sb.AppendLine();
        }

        /// <summary>属性父类：字段与 Modern 风格完全一致（同一张表 → 同一批字段名/类型）。</summary>
        private static void AppendLegacyPropertyBase(StringBuilder sb, CExcelTable table, string typeName)
        {
            sb.AppendLine("//属性父类");
            sb.AppendLine("[System.Serializable]");
            sb.AppendLine("public class " + typeName);
            sb.AppendLine("{");
            foreach (string column in table.Columns)
                AppendField(sb, table, column, "    ", true);
            sb.AppendLine("}");
            AppendEnums(sb, table, "");
        }

        /// <summary>对象父类：数组 + 查询方法 + 每字段 ProptyList（成员名/语义与项目既有 _DataBase 一致）。</summary>
        private static void AppendLegacyDataBase(StringBuilder sb, CExcelTable table, string tableName, string propType,
            string keyType, string keyField)
        {
            bool hasKey = !string.IsNullOrEmpty(keyField);
            sb.AppendLine("//对象父类");
            sb.AppendLine("[System.Serializable]");
            sb.AppendLine("public class " + tableName + "_DataBase");
            sb.AppendLine("{");
            sb.AppendLine("    //对象数组");
            sb.AppendLine("    public " + propType + "[] DataArray;");
            if (hasKey)
            {
                sb.AppendLine("    //临时字典");
                sb.AppendLine("    public Dictionary<" + keyType + ", " + propType + "> DataDictionary = new Dictionary<" + keyType + ", " + propType + ">();");
            }
            sb.AppendLine("    //对象数组长度");
            sb.AppendLine("    public int ArrayLength;");
            sb.AppendLine();
            if (hasKey)
            {
                sb.AppendLine("    //通过ID获取数据,没有返回最后一个ID数据");
                sb.AppendLine("    public " + propType + " GetDataByID(" + keyType + " _id)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (ArrayLength == 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 数据为空（一行都没有）\");");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine("        if (DataDictionary.ContainsKey(_id))");
                sb.AppendLine("        {");
                sb.AppendLine("            return DataDictionary[_id];");
                sb.AppendLine("        }");
                sb.AppendLine("        for (int i = 0; i < ArrayLength; i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (!DataDictionary.ContainsKey(DataArray[i]." + keyField + "))");
                sb.AppendLine("            {");
                sb.AppendLine("                DataDictionary.Add(DataArray[i]." + keyField + ", DataArray[i]);");
                sb.AppendLine("                if (DataArray[i]." + keyField + " == _id)");
                sb.AppendLine("                {");
                sb.AppendLine("                    return DataArray[i];");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine("        Debug.LogError(\"表格：" + tableName + " 中找不到ID： \"+ _id);");
                sb.AppendLine("        if (" + (string.Equals(keyType, "string", StringComparison.Ordinal) ? "string.IsNullOrEmpty(_id)" : "_id<=0") + ")");
                sb.AppendLine("        {");
                sb.AppendLine("            return DataArray[0];");
                sb.AppendLine("        }");
                sb.AppendLine("        else");
                sb.AppendLine("        {");
                sb.AppendLine("            return DataArray[ArrayLength - 1];");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID获取数据,有空");
                sb.AppendLine("    public " + propType + " GetDataNullID(" + keyType + " _id)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (ArrayLength == 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 数据为空（一行都没有）\");");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine("        if (DataDictionary.ContainsKey(_id))");
                sb.AppendLine("        {");
                sb.AppendLine("            return DataDictionary[_id];");
                sb.AppendLine("        }");
                sb.AppendLine("        for (int i = 0; i < ArrayLength; i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (!DataDictionary.ContainsKey(DataArray[i]." + keyField + "))");
                sb.AppendLine("            {");
                sb.AppendLine("                DataDictionary.Add(DataArray[i]." + keyField + ", DataArray[i]);");
                sb.AppendLine("                if (DataArray[i]." + keyField + " == _id)");
                sb.AppendLine("                {");
                sb.AppendLine("                    return DataArray[i];");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine("        Debug.LogError(\"表格：" + tableName + " 中找不到ID： \"+ _id);");
                sb.AppendLine("        return null;");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //临时等级字典：缓存 ID -> 该 ID 的全部数据，避免重复遍历");
                sb.AppendLine("    public Dictionary<" + keyType + ", List<" + propType + ">> DataID_Levs = new Dictionary<" + keyType + ", List<" + propType + ">>();");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>");
                sb.AppendLine("    /// 根据ID获取第lev个数据（lev 从 1 开始）。");
                sb.AppendLine("    /// 有该ID但数量不足 lev → 返回最后一条并警告；完全没有该ID → 返回数组首元素。");
                sb.AppendLine("    /// </summary>");
                sb.AppendLine("    public " + propType + " GetDataBySameID(" + keyType + " _id, int _lev)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (ArrayLength == 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 数据为空（一行都没有）\");");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine("        if (_lev < 1)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " lev参数不能小于1，当前传入：\" + _lev);");
                sb.AppendLine("            return DataArray[0];");
                sb.AppendLine("        }");
                sb.AppendLine("        List<" + propType + "> dataList = GetSameIDList(_id);");
                sb.AppendLine("        if (dataList.Count == 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 中完全找不到ID：\" + _id + \" 对应数据\");");
                sb.AppendLine("            return DataArray[0];");
                sb.AppendLine("        }");
                sb.AppendLine("        if (_lev > dataList.Count)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogWarning(\"表格：" + tableName + " ID=\" + _id + \" 仅有\" + dataList.Count + \"条，不足要求lev=\" + _lev + \"，返回最后一条匹配数据\");");
                sb.AppendLine("            return dataList[dataList.Count - 1];");
                sb.AppendLine("        }");
                sb.AppendLine("        return dataList[_lev - 1];");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>获取该ID的最后一条数据（重复ID段的最后一条）。</summary>");
                sb.AppendLine("    public " + propType + " GetDataBySameIDMaxlev(" + keyType + " _id)");
                sb.AppendLine("    {");
                sb.AppendLine("        if (ArrayLength == 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 数据为空（一行都没有）\");");
                sb.AppendLine("            return null;");
                sb.AppendLine("        }");
                sb.AppendLine("        List<" + propType + "> dataList = GetSameIDList(_id);");
                sb.AppendLine("        if (dataList.Count == 0)");
                sb.AppendLine("        {");
                sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 中完全找不到ID：\" + _id + \" 对应数据\");");
                sb.AppendLine("            return DataArray[0];");
                sb.AppendLine("        }");
                sb.AppendLine("        return dataList[dataList.Count - 1];");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //同ID的全部数据（带缓存；同ID必须连续排布，同ID段内按出现顺序）");
                sb.AppendLine("    private List<" + propType + "> GetSameIDList(" + keyType + " _id)");
                sb.AppendLine("    {");
                sb.AppendLine("        List<" + propType + "> dataList;");
                sb.AppendLine("        if (DataID_Levs.TryGetValue(_id, out dataList))");
                sb.AppendLine("        {");
                sb.AppendLine("            return dataList;");
                sb.AppendLine("        }");
                sb.AppendLine("        dataList = new List<" + propType + ">();");
                sb.AppendLine("        for (int i = 0; i < ArrayLength; i++)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (DataArray[i]." + keyField + " == _id) dataList.Add(DataArray[i]);");
                sb.AppendLine("        }");
                sb.AppendLine("        DataID_Levs.Add(_id, dataList);");
                sb.AppendLine("        return dataList;");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("    //通过下标获取数据,没有返回最后一个ID数据");
            sb.AppendLine("    public " + propType + " GetDataByIndex(int _index)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (ArrayLength == 0)");
            sb.AppendLine("        {");
            sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 数据为空（一行都没有）\");");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine("        if (_index < 0 || _index >= ArrayLength)");
            sb.AppendLine("        {");
            sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 中下标越界： \"+ _index);");
            sb.AppendLine("            if (_index<0)");
            sb.AppendLine("            {");
            sb.AppendLine("                return DataArray[0];");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                return DataArray[ArrayLength - 1];");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        return DataArray[_index];");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //通过下标获取数据,没有返回null");
            sb.AppendLine("    public " + propType + " GetDataNullIndexNull(int _index)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (_index < 0 || _index >= ArrayLength)");
            sb.AppendLine("        {");
            sb.AppendLine("            Debug.LogError(\"表格：" + tableName + " 中下标越界： \"+ _index);");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine("        return DataArray[_index];");
            sb.AppendLine("    }");
            sb.AppendLine();
            AppendLegacyProptyLists(sb, table, propType, "    ");
            sb.AppendLine("}");
        }

        /// <summary>每字段一个 <c>Get&lt;字段&gt;ProptyList()</c>（成员名与项目既有代码逐字一致）。</summary>
        private static void AppendLegacyProptyLists(StringBuilder sb, CExcelTable table, string propType, string indent)
        {
            sb.AppendLine(indent + "#region 将字段装入List");
            foreach (string column in table.Columns)
            {
                string field = CExcelTypeInfer.ToLegacyFieldName(column);
                string type = table.CSharpTypeOf(column);
                sb.AppendLine(indent + "/// <summary>" + FieldComment(table, column) + "</summary>");
                sb.AppendLine(indent + "public List<" + type + "> Get" + field + "ProptyList()");
                sb.AppendLine(indent + "{");
                sb.AppendLine(indent + "    List<" + type + "> tempList = new List<" + type + ">(ArrayLength);");
                sb.AppendLine(indent + "    for (int i = 0; i < ArrayLength; i++)");
                sb.AppendLine(indent + "    {");
                sb.AppendLine(indent + "        tempList.Add(DataArray[i]." + field + ");");
                sb.AppendLine(indent + "    }");
                sb.AppendLine(indent + "    return tempList;");
                sb.AppendLine(indent + "}");
                sb.AppendLine();
            }
            sb.AppendLine(indent + "#endregion");
        }

        /// <summary>Legacy：普通单表 → 一个文件 <c>&lt;T&gt;_DataGetter.cs</c>（Getter + PropertyBase + DataBase）。</summary>
        private static string WriteLegacyGetter(CExcelTable table, string className, string ns, string dataRelativePath)
        {
            bool hasKey = !string.IsNullOrEmpty(table.PrimaryKey);
            string keyType = hasKey ? table.CSharpTypeOf(table.PrimaryKey) : "int";
            string keyField = hasKey ? CExcelTypeInfer.ToLegacyFieldName(table.PrimaryKey) : null;
            string propType = className + "_PropertyBase";
            string dbType = className + "_DataBase";
            string dataType = className + "_Data";

            var sb = new StringBuilder();
            AppendLegacyFileHeader(sb, table, ns);
            sb.AppendLine("/// <summary>" + className + " 数据访问（自动生成；类名/成员名与项目既有 *_DataGetter 一致）。</summary>");
            sb.AppendLine("public class " + className + "_DataGetter");
            sb.AppendLine("{");
            sb.AppendLine("    private const string DataPath = \"" + dataRelativePath + "\";");
            sb.AppendLine();
            sb.AppendLine("    private static " + dataType + " m_" + className + "_Data;");
            sb.AppendLine("    private static " + dataType + " M_" + className + "_Data");
            sb.AppendLine("    {");
            sb.AppendLine("        get");
            sb.AppendLine("        {");
            sb.AppendLine("            if (m_" + className + "_Data == null) ApplyContainer(ConfigTableRuntime.ReadData(DataPath));");
            sb.AppendLine("            return m_" + className + "_Data;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>取整表数据（懒加载）。</summary>");
            sb.AppendLine("    public static " + dbType + " GetData()");
            sb.AppendLine("    {");
            sb.AppendLine("        return M_" + className + "_Data;");
            sb.AppendLine("    }");
            sb.AppendLine();
            // 无键表：_DataBase 里没有 GetDataByID / GetDataNullID / SameID → 这里一并跳过（否则生成物编译不过）
            if (hasKey)
            {
                sb.AppendLine("    //通过ID拿数据,没有返回最后一个ID数据");
                sb.AppendLine("    public static " + propType + " GetDataByID(" + keyType + " id)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData().GetDataByID(id);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID拿数据,没有返回Null");
                sb.AppendLine("    public static " + propType + " GetDataNullID(" + keyType + " id)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData().GetDataNullID(id);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID(相同ID)拿数据--默认lev下标从1开始");
                sb.AppendLine("    public static " + propType + " GetDataBySameID(" + keyType + " id, int lev)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData().GetDataBySameID(id, lev);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID(相同ID)拿数据--最大lev");
                sb.AppendLine("    public static " + propType + " GetDataBySameIDMaxlev(" + keyType + " id)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData().GetDataBySameIDMaxlev(id);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("    //通过下标拿数据,没有返回最后一个数据");
            sb.AppendLine("    public static " + propType + " GetDataByIndex(int index)");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData().GetDataByIndex(index);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //通过下标拿数据,没有返回null");
            sb.AppendLine("    public static " + propType + " GetDataNullIndexNull(int index)");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData().GetDataNullIndexNull(index);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //获取数组长度");
            sb.AppendLine("    public static int GetArrayLenth()");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData().ArrayLength;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //获取数组");
            sb.AppendLine("    public static " + propType + "[] GetArray()");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData().DataArray;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #region 将字段装入List");
            foreach (string column in table.Columns)
            {
                string field = CExcelTypeInfer.ToLegacyFieldName(column);
                string type = table.CSharpTypeOf(column);
                sb.AppendLine("    /// <summary>" + FieldComment(table, column) + "</summary>");
                sb.AppendLine("    public List<" + type + "> Get" + field + "ProptyList()");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData().Get" + field + "ProptyList();");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            AppendLegacyRuntimeSupport(sb, className, new List<int>(), null);
            sb.AppendLine("}");
            sb.AppendLine();
            AppendLegacyPropertyBase(sb, table, propType);
            sb.AppendLine();
            AppendLegacyDataBase(sb, table, className, propType, keyType, keyField);
            return sb.ToString();
        }

        /// <summary>
        /// Legacy 的运行期支撑（自注册 + 容器灌入 + 重载）。
        /// 单表：<paramref name="dataClassName"/> = 表名、<paramref name="chapters"/> 为空；
        /// 章节族：<paramref name="chapters"/> 非空，<paramref name="dataClassName"/> = 章节前缀。
        /// </summary>
        private static void AppendLegacyRuntimeSupport(StringBuilder sb, string dataClassName, List<int> chapters, string dataPrefix)
        {
            bool isChapter = chapters != null && chapters.Count > 0;
            string dbType = dataClassName + "_DataBase";
            if (!isChapter)
            {
                sb.AppendLine("    /// <summary>丢弃缓存；下次访问重新读数据文件（热更后用）。</summary>");
                sb.AppendLine("    public static void Reload()");
                sb.AppendLine("    {");
                sb.AppendLine("        m_" + dataClassName + "_Data = null;");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>直接灌入容器字节（预加载/热更/测试用）。</summary>");
                sb.AppendLine("    public static void LoadFrom(byte[] container)");
                sb.AppendLine("    {");
                sb.AppendLine("        ApplyContainer(container);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    /// <summary>把容器字节解出来灌进缓存（ConfigTableRuntime.PreloadAll 调用）。</summary>");
                sb.AppendLine("    internal static void ApplyContainer(byte[] container)");
                sb.AppendLine("    {");
                sb.AppendLine("        " + dataClassName + "_Data data = new " + dataClassName + "_Data();");
                sb.AppendLine("        data.DataArray = ConfigTableRuntime.DecodeRows<" + dataClassName + "_PropertyBase>(container, \"" + dataClassName + "\");");
                sb.AppendLine("        data.ArrayLength = data.DataArray.Length;");
                sb.AppendLine("        m_" + dataClassName + "_Data = data;");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    private sealed class Registration : IConfigTable");
                sb.AppendLine("    {");
                sb.AppendLine("        public string DataRelativePath { get { return DataPath; } }");
                sb.AppendLine("        public void Apply(byte[] container) { ApplyContainer(container); }");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]");
                sb.AppendLine("    private static void __Register()");
                sb.AppendLine("    {");
                sb.AppendLine("        ConfigTableRuntime.Register(new Registration());");
                sb.AppendLine("    }");
                return;
            }

            sb.AppendLine("    /// <summary>丢弃全部章节缓存；下次访问重新读数据文件（热更后用）。</summary>");
            sb.AppendLine("    public static void Reload()");
            sb.AppendLine("    {");
            foreach (int chapter in chapters)
                sb.AppendLine("        m_" + dataClassName + "_" + chapter + "_Data = null;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>直接灌入某章节的容器字节（预加载/热更/测试用）。</summary>");
            sb.AppendLine("    public static void LoadFrom(int chapterID, byte[] container)");
            sb.AppendLine("    {");
            sb.AppendLine("        ApplyChapter(chapterID, container);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>把某章节的容器字节解出来灌进缓存（ConfigTableRuntime.PreloadAll 调用）。</summary>");
            sb.AppendLine("    internal static void ApplyChapter(int chapterID, byte[] container)");
            sb.AppendLine("    {");
            foreach (int chapter in chapters)
            {
                sb.AppendLine("        if (chapterID == " + chapter + ")");
                sb.AppendLine("        {");
                sb.AppendLine("            " + dataClassName + "_" + chapter + "_Data data = new " + dataClassName + "_" + chapter + "_Data();");
                sb.AppendLine("            data.DataArray = ConfigTableRuntime.DecodeRows<" + dataClassName + "_PropertyBase>(container, \"" + dataClassName + "_" + chapter + "\");");
                sb.AppendLine("            data.ArrayLength = data.DataArray.Length;");
                sb.AppendLine("            m_" + dataClassName + "_" + chapter + "_Data = data;");
                sb.AppendLine("            return;");
                sb.AppendLine("        }");
            }
            sb.AppendLine("        Debug.LogError(\"[" + dataClassName + "] 没有第 \" + chapterID + \" 章节的数据文件\");");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private sealed class Registration : IConfigTable");
            sb.AppendLine("    {");
            sb.AppendLine("        private readonly int _chapter;");
            sb.AppendLine("        public Registration(int chapter) { _chapter = chapter; }");
            sb.AppendLine("        public string DataRelativePath { get { return \"" + dataPrefix + "\" + _chapter + ConfigTableRuntime.DataExtension; } }");
            sb.AppendLine("        public void Apply(byte[] container) { ApplyChapter(_chapter, container); }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine("    private static void __Register()");
            sb.AppendLine("    {");
            sb.AppendLine("        foreach (int chapter in Chapters) ConfigTableRuntime.Register(new Registration(chapter));");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>可用章节号（升序）。</summary>");
            sb.AppendLine("    public static readonly int[] Chapters = new[] { " + string.Join(", ",
                chapters.Select(i => i.ToString(CultureInfo.InvariantCulture))) + " };");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>章节数量。</summary>");
            sb.AppendLine("    public static int ChapterCount");
            sb.AppendLine("    {");
            sb.AppendLine("        get { return Chapters.Length; }");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    private static readonly HashSet<int> WarnedMissingChapters = new HashSet<int>();");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>把章节号解析成实际存在的章节：-1 = 当前章节；没配置 → 警告 + 最后一章。</summary>");
            sb.AppendLine("    private static int ResolveChapter(int chapterID)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (chapterID == -1)");
            sb.AppendLine("        {");
            sb.AppendLine("            chapterID = ConfigTableRuntime.CurrentChapterId;");
            sb.AppendLine("        }");
            foreach (int chapter in chapters)
                sb.AppendLine("        if (chapterID == " + chapter + ") return chapterID;");
            sb.AppendLine("        if (WarnedMissingChapters.Add(chapterID))");
            sb.AppendLine("        {");
            sb.AppendLine("            Debug.LogWarning(\"策划没有配置 第\" + chapterID + \"章节    " + dataClassName
                              + "_DataGetter 数据表,默认给上一章节数据\");");
            sb.AppendLine("        }");
            sb.AppendLine("        return " + chapters[chapters.Count - 1] + ";");
            sb.AppendLine("    }");
        }

        /// <summary>
        /// Legacy：章节族聚合文件 <c>&lt;前缀&gt;_DataGetter.cs</c>
        /// （每章节一个 <c>&lt;前缀&gt;_&lt;N&gt;_Data</c> 静态缓存 + 全部成员带 <c>int chapterID = -1</c>）。
        /// </summary>
        private static string WriteLegacyChapterFamily(CExcelTable table, string frontName, List<int> chapters, string ns)
        {
            bool hasKey = !string.IsNullOrEmpty(table.PrimaryKey);
            string keyType = hasKey ? table.CSharpTypeOf(table.PrimaryKey) : "int";
            string keyField = hasKey ? CExcelTypeInfer.ToLegacyFieldName(table.PrimaryKey) : null;
            string propType = frontName + "_PropertyBase";
            string dbType = frontName + "_DataBase";
            string dataPrefix = frontName + "/Data/" + frontName + "_";

            var sb = new StringBuilder();
            AppendLegacyFileHeader(sb, table, ns);
            sb.AppendLine("/// <summary>" + frontName + " 多章节数据访问（自动生成；章节号省略 = 当前章节）。</summary>");
            sb.AppendLine("public class " + frontName + "_DataGetter");
            sb.AppendLine("{");
            sb.AppendLine("    #region 数据读取");
            foreach (int chapter in chapters)
            {
                string chapterType = frontName + "_" + chapter + "_Data";
                sb.AppendLine("    private static " + chapterType + " m_" + frontName + "_" + chapter + "_Data;");
                sb.AppendLine("    private static " + chapterType + " M_" + frontName + "_" + chapter + "_Data");
                sb.AppendLine("    {");
                sb.AppendLine("        get");
                sb.AppendLine("        {");
                sb.AppendLine("            if (m_" + frontName + "_" + chapter + "_Data == null)");
                sb.AppendLine("            {");
                sb.AppendLine("                m_" + frontName + "_" + chapter + "_Data = new " + chapterType + "();");
                sb.AppendLine("                m_" + frontName + "_" + chapter + "_Data.DataArray = ConfigTableRuntime.LoadRows<" + propType
                                  + ">(\"" + dataPrefix + chapter + "\" + ConfigTableRuntime.DataExtension);");
                sb.AppendLine("                m_" + frontName + "_" + chapter + "_Data.ArrayLength = m_" + frontName + "_" + chapter + "_Data.DataArray.Length;");
                sb.AppendLine("            }");
                sb.AppendLine("            return m_" + frontName + "_" + chapter + "_Data;");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }
            sb.AppendLine();
            sb.AppendLine("    #endregion");
            sb.AppendLine();
            AppendLegacyRuntimeSupport(sb, frontName, chapters, dataPrefix);
            sb.AppendLine();
            sb.AppendLine("    //获取对应章节的数据");
            sb.AppendLine("    public static " + dbType + " GetData(int chapterID = -1)");
            sb.AppendLine("    {");
            sb.AppendLine("        chapterID = ResolveChapter(chapterID);");
            foreach (int chapter in chapters)
                sb.AppendLine("        if (chapterID == " + chapter + ") return M_" + frontName + "_" + chapter + "_Data;");
            sb.AppendLine("        return M_" + frontName + "_" + chapters[chapters.Count - 1] + "_Data;");
            sb.AppendLine("    }");
            sb.AppendLine();
            if (hasKey)
            {
                sb.AppendLine("    //通过ID拿数据,没有返回最后一个ID数据");
                sb.AppendLine("    public static " + propType + " GetDataByID(" + keyType + " id, int chapterID = -1)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData(chapterID).GetDataByID(id);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID拿数据,没有返回Null");
                sb.AppendLine("    public static " + propType + " GetDataNullID(" + keyType + " id, int chapterID = -1)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData(chapterID).GetDataNullID(id);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID(相同ID)拿数据--默认lev下标从1开始");
                sb.AppendLine("    public static " + propType + " GetDataBySameID(" + keyType + " id, int lev, int chapterID = -1)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData(chapterID).GetDataBySameID(id, lev);");
                sb.AppendLine("    }");
                sb.AppendLine();
                sb.AppendLine("    //通过ID(相同ID)拿数据--最大lev");
                sb.AppendLine("    public static " + propType + " GetDataBySameIDMaxlev(" + keyType + " id, int chapterID = -1)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData(chapterID).GetDataBySameIDMaxlev(id);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("    //通过下标拿数据,没有返回最后一个数据");
            sb.AppendLine("    public static " + propType + " GetDataByIndex(int index, int chapterID = -1)");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData(chapterID).GetDataByIndex(index);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //通过下标拿数据,没有返回null");
            sb.AppendLine("    public static " + propType + " GetDataNullIndexNull(int index, int chapterID = -1)");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData(chapterID).GetDataNullIndexNull(index);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //获取数组长度");
            sb.AppendLine("    public static int GetArrayLenth(int chapterID = -1)");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData(chapterID).ArrayLength;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    //获取数组");
            sb.AppendLine("    public static " + propType + "[] GetArray(int chapterID = -1)");
            sb.AppendLine("    {");
            sb.AppendLine("        return GetData(chapterID).DataArray;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    #region 将字段装入List");
            foreach (string column in table.Columns)
            {
                string field = CExcelTypeInfer.ToLegacyFieldName(column);
                string type = table.CSharpTypeOf(column);
                sb.AppendLine("    /// <summary>" + FieldComment(table, column) + "</summary>");
                sb.AppendLine("    public List<" + type + "> Get" + field + "ProptyList(int chapterID = -1)");
                sb.AppendLine("    {");
                sb.AppendLine("        return GetData(chapterID).Get" + field + "ProptyList();");
                sb.AppendLine("    }");
                sb.AppendLine();
            }
            sb.AppendLine("    #endregion");
            sb.AppendLine("}");
            sb.AppendLine();
            AppendLegacyPropertyBase(sb, table, propType);
            sb.AppendLine();
            AppendLegacyDataBase(sb, table, frontName, propType, keyType, keyField);
            return sb.ToString();
        }

        /// <summary>
        /// Legacy：每个表/章节的数据子类（空壳 <c>&lt;X&gt;_Data : &lt;表&gt;_DataBase</c>）——
        /// 与项目既有 <c>&lt;T&gt;_Data.cs</c>（普通表）和 <c>&lt;T&gt;_&lt;N&gt;_Data.cs</c>（章节）逐字对应。
        /// </summary>
        private static string WriteLegacyDataSubClass(string subClassName, string baseClassName, string sourceLabel, string ns)
        {
            var sb = new StringBuilder();
            sb.AppendLine(HeaderLine);
            sb.AppendLine(TemplateLine);
            sb.AppendLine("// Source sheet: " + sourceLabel);
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("using " + ns + ";");
            sb.AppendLine();
            sb.AppendLine("//数据子类");
            sb.AppendLine("[System.Serializable]");
            sb.AppendLine("public class " + subClassName + " : " + baseClassName);
            sb.AppendLine("{");
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
            sb.AppendLine("        /// <summary>最后一章：章节号没配置时的兜底章节（对齐项目既有行为：给上一章数据）。</summary>");
            sb.AppendLine("        private static readonly int LastChapter = " + chapters[chapters.Count - 1] + ";");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>已经警告过的缺失章节号（同一个章节只刷一次日志，避免刷屏）。</summary>");
            sb.AppendLine("        private static readonly HashSet<int> WarnedMissingChapters = new HashSet<int>();");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>当前章节号（游戏通过 ConfigTableRuntime.Context 注入；没注入时是 FallbackChapterId）。</summary>");
            sb.AppendLine("        public static int CurrentChapterId => ConfigTableRuntime.CurrentChapterId;");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>是否存在该章节（只有存在对应数据文件的章节才会生成进来）。</summary>");
            sb.AppendLine("        public static bool HasChapter(int chapterId)");
            sb.AppendLine("        {");
            sb.AppendLine("            for (int i = 0; i < Chapters.Length; i++) if (Chapters[i] == chapterId) return true;");
            sb.AppendLine("            return false;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 把外部章节号解析成**实际存在的**章节号：");
            sb.AppendLine("        /// -1（省略）→ 当前章节（ConfigTableRuntime.CurrentChapterId）；");
            sb.AppendLine("        /// 没有配置的章节 → 只警告一次并回退到最后一章（不抛异常、不给空数据）。");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private static int ResolveChapter(int chapterId)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (chapterId == -1) chapterId = ConfigTableRuntime.CurrentChapterId;");
            sb.AppendLine("            if (HasChapter(chapterId)) return chapterId;");
            sb.AppendLine("            if (WarnedMissingChapters.Add(chapterId))");
            sb.AppendLine("            {");
            sb.AppendLine("                Debug.LogWarning(\"[" + frontName + "] 策划没有配置 第\" + chapterId + \"章节，默认给第 \" + LastChapter + \" 章数据\");");
            sb.AppendLine("            }");
            sb.AppendLine("            return LastChapter;");
            sb.AppendLine("        }");
            sb.AppendLine();
            sb.AppendLine("        /// <summary>取某章节的全部行（基类视角）；章节号省略（-1）时取当前章节。</summary>");
            sb.AppendLine("        public static IReadOnlyList<" + baseClass + "> GetChapter(int chapterId = -1)");
            sb.AppendLine("        {");
            sb.AppendLine("            switch (ResolveChapter(chapterId))");
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
                sb.AppendLine("        /// <summary>在指定章节里按主键取一行；章节号省略（-1）时取当前章节；同一个键有多行时给**第一行**（要全部用 GetAll）；找不到返回 null。</summary>");
                sb.AppendLine("        public static " + baseClass + " Get(" + keyType + " key, int chapterId = -1)");
                sb.AppendLine("        {");
                sb.AppendLine("            switch (ResolveChapter(chapterId))");
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
                sb.AppendLine("        public static IReadOnlyList<" + baseClass + "> GetAll(" + keyType + " key, int chapterId = -1)");
                sb.AppendLine("        {");
                sb.AppendLine("            switch (ResolveChapter(chapterId))");
                sb.AppendLine("            {");
                foreach (int chapter in chapters)
                {
                    sb.AppendLine("                case " + chapter + ": return Collect(Chapter" + chapter + ", key);");
                }
                sb.AppendLine("                default: return None;");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>在当前章节里按主键取一行；找不到返回 false。</summary>");
                sb.AppendLine("        public static bool TryGet(" + keyType + " key, out " + baseClass + " value) => TryGet(key, -1, out value);");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>在指定章节里按主键取一行；找不到返回 false。</summary>");
                sb.AppendLine("        public static bool TryGet(" + keyType + " key, int chapterId, out " + baseClass + " value)");
                sb.AppendLine("        {");
                sb.AppendLine("            value = Get(key, chapterId);");
                sb.AppendLine("            return value != null;");
                sb.AppendLine("        }");
                sb.AppendLine();
                sb.AppendLine("        /// <summary>指定章节里是否存在该主键（章节号省略时用当前章节）。</summary>");
                sb.AppendLine("        public static bool Contains(" + keyType + " key, int chapterId = -1) => Get(key, chapterId) != null;");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("        // 本表没有可做键的列 → 不生成 Get / GetAll / TryGet / Contains；");
                sb.AppendLine("        // 请用 GetChapter(chapterId) / ChapterN / HasChapter 访问（章节号省略 = 当前章节）。");
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
